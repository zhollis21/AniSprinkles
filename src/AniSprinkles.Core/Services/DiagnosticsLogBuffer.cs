using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// An in-memory ring of the last few minutes of log lines, kept so a user can send what led up to a
/// problem (#112).
/// <para>
/// This exists because neither existing sink can answer "what was the app doing just before this
/// went wrong". The file log keeps <see cref="LogLevel.Warning"/> and above in Release, which is
/// every trace worth having filtered out — no <c>NAVTRACE</c>, no <c>PageState</c>, no <c>HTTP</c>.
/// Logcat has all of it but lives on the far side of the app/Core boundary, in a buffer shared with
/// the rest of the system.
/// </para>
/// <para>
/// Nothing here touches disk. Entries are dropped once they age out or the ring fills, so an install
/// that never reports anything pays only the memory, and the window a report can cover is bounded by
/// construction rather than by how long the app happened to be running.
/// <see cref="DiagnosticsSessionLog"/> is what writes a snapshot down when the process is about to
/// be torn away.
/// </para>
/// </summary>
[ProviderAlias("Diagnostics")]
public sealed class DiagnosticsLogBuffer : ILoggerProvider
{
    /// <summary>
    /// How far back a report reaches. Five minutes is the span a user can actually narrate — "I
    /// tapped into a few manga and they all failed" — and it comfortably covers the lead-up to a
    /// failure they are still looking at.
    /// </summary>
    public static readonly TimeSpan DefaultRetention = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The hard ceiling on entries, independent of age. Retention alone is not a memory bound: a
    /// burst — a paged list, a retry storm — can emit thousands of lines inside the window. At a
    /// typical line length this caps the ring at a few hundred KB.
    /// </summary>
    public const int DefaultMaxEntries = 2000;

    private readonly object _lock = new();
    private readonly Queue<Entry> _entries = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _retention;
    private readonly int _maxEntries;
    private readonly LogLevel _minimumLevel;

    public DiagnosticsLogBuffer(
        TimeProvider? timeProvider = null,
        TimeSpan? retention = null,
        int maxEntries = DefaultMaxEntries,
        LogLevel minimumLevel = LogLevel.Information)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _retention = retention ?? DefaultRetention;
        _maxEntries = Math.Max(maxEntries, 1);
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) => new BufferLogger(categoryName, this);

    /// <summary>
    /// The lines currently in the window, oldest first, already formatted. Aged-out entries are
    /// dropped here as well as on write — otherwise a quiet stretch would hand a report lines from
    /// well outside the window it promises the user.
    /// </summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_lock)
        {
            TrimLocked();
            return _entries.Select(e => e.Line).ToArray();
        }
    }

    /// <summary>Drops everything. Used after a snapshot has been persisted, so the next flush cannot
    /// write the same lines a second time.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    public void Dispose()
    {
        // Nothing to release — the ring is plain memory, and callers may still hold loggers.
    }

    private void Append(LogLevel level, string category, int eventId, string? message, Exception? exception)
    {
        var timestamp = _timeProvider.GetUtcNow();
        var normalizedMessage = string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : message.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
        var exceptionText = exception is null ? string.Empty : $" | {exception}";

        // Same shape as the file log's lines, so a reader moving between the two isn't re-learning a
        // format. Note the newline normalization is on literal \r and \n rather than
        // Environment.NewLine: a report built on one platform can carry text produced on another,
        // and one stray newline breaks the one-record-per-line contract for every reader downstream.
        var line = $"{timestamp:O} [{level}] {category} ({eventId}) {normalizedMessage}{exceptionText}";

        lock (_lock)
        {
            _entries.Enqueue(new Entry(timestamp, line));
            TrimLocked();
        }
    }

    private void TrimLocked()
    {
        var cutoff = _timeProvider.GetUtcNow() - _retention;

        while (_entries.Count > 0 && _entries.Peek().At < cutoff)
        {
            _entries.Dequeue();
        }

        while (_entries.Count > _maxEntries)
        {
            _entries.Dequeue();
        }
    }

    private readonly record struct Entry(DateTimeOffset At, string Line);

    private sealed class BufferLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly DiagnosticsLogBuffer _owner;

        public BufferLogger(string categoryName, DiagnosticsLogBuffer owner)
        {
            _categoryName = categoryName;
            _owner = owner;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel != LogLevel.None && logLevel >= _owner._minimumLevel;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // Formatted here rather than at snapshot time because TState may be pooled and reused the
            // moment this returns — holding it to format later reads whatever the next caller put there.
            _owner.Append(logLevel, _categoryName, eventId.Id, formatter(state, exception), exception);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
    }
}
