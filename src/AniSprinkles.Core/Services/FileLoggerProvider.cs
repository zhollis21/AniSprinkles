using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace AniSprinkles.Services;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly FileLogWriter _writer;
    private readonly LogLevel _minimumLevel;
    private bool _disposed;

    public FileLoggerProvider(string logDirectory, string fileName = "anisprinkles.log", LogLevel minimumLevel = LogLevel.Debug, long maxFileSizeBytes = 1024 * 1024, int retainedFiles = 3)
    {
        _writer = new FileLogWriter(logDirectory, fileName, maxFileSizeBytes, retainedFiles);
        _minimumLevel = minimumLevel;
        _writer.Write(LogLevel.Information, "AniSprinkles.Logging", 0, $"File logger initialized at {_writer.CurrentFilePath}", null);
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileLoggerProvider));
        }

        return new FileLogger(categoryName, _writer, _minimumLevel);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _writer.Dispose();
        _disposed = true;
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly FileLogWriter _writer;
        private readonly LogLevel _minimumLevel;

        public FileLogger(string categoryName, FileLogWriter writer, LogLevel minimumLevel)
        {
            _categoryName = categoryName;
            _writer = writer;
            _minimumLevel = minimumLevel;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None && logLevel >= _minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            _writer.Write(logLevel, _categoryName, eventId.Id, message, exception);
        }
    }

    private sealed class FileLogWriter : IDisposable
    {
        private readonly string _logDirectory;
        private readonly string _baseFileName;
        private readonly long _maxFileSizeBytes;
        private readonly int _retainedFiles;
        private readonly Channel<string> _queue;
        private readonly CancellationTokenSource _disposeTokenSource = new();
        private readonly Task _backgroundWriterTask;
        private bool _disposed;
        private bool _isFaulted;

        public FileLogWriter(string logDirectory, string baseFileName, long maxFileSizeBytes, int retainedFiles)
        {
            _logDirectory = logDirectory;
            _baseFileName = baseFileName;
            _maxFileSizeBytes = maxFileSizeBytes;
            _retainedFiles = Math.Max(retainedFiles, 1);

            try
            {
                Directory.CreateDirectory(_logDirectory);
            }
            catch (Exception ex) when (IsFileAccessException(ex))
            {
                _isFaulted = true;
            }

            _queue = Channel.CreateBounded<string>(new BoundedChannelOptions(2048)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });

            _backgroundWriterTask = Task.Run(ProcessQueueAsync);
        }

        public string CurrentFilePath => GetCurrentFilePath();

        public void Write(LogLevel logLevel, string category, int eventId, string? message, Exception? exception)
        {
            if (_disposed || _isFaulted)
            {
                return;
            }

            var timestamp = DateTimeOffset.UtcNow.ToString("O");
            var normalizedMessage = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Replace(Environment.NewLine, " ");
            var exceptionText = exception is null ? string.Empty : $" | {exception}";
            var line = $"{timestamp} [{logLevel}] {category} ({eventId}) {normalizedMessage}{exceptionText}";

            // Never block caller threads on disk IO.
            _queue.Writer.TryWrite(line);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _queue.Writer.TryComplete();
            _disposeTokenSource.Cancel();

            try
            {
                _backgroundWriterTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
                // Never throw from logging shutdown.
            }
            finally
            {
                _disposeTokenSource.Dispose();
            }
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                await foreach (var line in _queue.Reader.ReadAllAsync(_disposeTokenSource.Token))
                {
                    if (_disposed || _isFaulted)
                    {
                        return;
                    }

                    try
                    {
                        Directory.CreateDirectory(_logDirectory);
                        RotateIfNeeded();
                        File.AppendAllText(GetCurrentFilePath(), line + Environment.NewLine);
                    }
                    catch (Exception ex) when (IsFileAccessException(ex))
                    {
                        // Never throw from app logging; disable file sink after first failure.
                        _isFaulted = true;
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown path.
            }
        }

        private void RotateIfNeeded()
        {
            var currentPath = GetCurrentFilePath();
            if (!File.Exists(currentPath))
            {
                return;
            }

            var info = new FileInfo(currentPath);
            if (info.Length < _maxFileSizeBytes)
            {
                return;
            }

            for (var i = _retainedFiles - 1; i >= 1; i--)
            {
                var source = GetArchivedFilePath(i);
                var destination = GetArchivedFilePath(i + 1);

                if (File.Exists(source))
                {
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }

                    File.Move(source, destination);
                }
            }

            var firstArchive = GetArchivedFilePath(1);
            if (File.Exists(firstArchive))
            {
                File.Delete(firstArchive);
            }

            File.Move(currentPath, firstArchive);
        }

        private string GetCurrentFilePath()
            => Path.Combine(_logDirectory, _baseFileName);

        private string GetArchivedFilePath(int index)
            => Path.Combine(_logDirectory, $"{_baseFileName}.{index}");

        private static bool IsFileAccessException(Exception ex)
            => ex is IOException
                or UnauthorizedAccessException
                or DirectoryNotFoundException
                or NotSupportedException
                or PathTooLongException;
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
