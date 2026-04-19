using Microsoft.Extensions.Logging;
using AndroidLog = Android.Util.Log;

namespace AniSprinkles.Platforms.Android;

/// <summary>
/// Bridges <see cref="ILogger"/> writes to <c>Android.Util.Log</c> so that
/// <c>adb logcat</c> picks up our Microsoft.Extensions.Logging output.
/// Without this provider ILogger writes are visible only in the file log.
/// </summary>
[ProviderAlias("AndroidLogcat")]
public sealed class AndroidLogcatLoggerProvider : ILoggerProvider
{
    private readonly LogLevel _minimumLevel;

    public AndroidLogcatLoggerProvider(LogLevel minimumLevel = LogLevel.Debug)
    {
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName)
        => new AndroidLogcatLogger(categoryName, _minimumLevel);

    public void Dispose()
    {
    }

    private sealed class AndroidLogcatLogger : ILogger
    {
        private const int MaxTagLength = 23;
        private readonly string _tag;
        private readonly LogLevel _minimumLevel;

        public AndroidLogcatLogger(string category, LogLevel minimumLevel)
        {
            _tag = TrimTag(category);
            _minimumLevel = minimumLevel;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None && logLevel >= _minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (exception is not null)
            {
                message = string.IsNullOrEmpty(message)
                    ? exception.ToString()
                    : $"{message}{Environment.NewLine}{exception}";
            }

            switch (logLevel)
            {
                case LogLevel.Trace:
                    AndroidLog.Verbose(_tag, message);
                    break;
                case LogLevel.Debug:
                    AndroidLog.Debug(_tag, message);
                    break;
                case LogLevel.Information:
                    AndroidLog.Info(_tag, message);
                    break;
                case LogLevel.Warning:
                    AndroidLog.Warn(_tag, message);
                    break;
                case LogLevel.Error:
                    AndroidLog.Error(_tag, message);
                    break;
                case LogLevel.Critical:
                    AndroidLog.Wtf(_tag, message);
                    break;
            }
        }

        // logcat tags are capped at 23 characters on API < 26. Trim from the left so the
        // most specific part of the ILogger category name (usually the class) survives.
        private static string TrimTag(string category)
            => category.Length <= MaxTagLength ? category : category[^MaxTagLength..];
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose()
        {
        }
    }
}
