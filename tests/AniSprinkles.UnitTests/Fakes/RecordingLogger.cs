using Microsoft.Extensions.Logging;

namespace AniSprinkles.UnitTests.Fakes;

/// <summary>
/// Captures rendered log messages so tests can assert on the app's trace contract. The NAVTRACE /
/// DATATRACE / <c>load#N</c> lines are a documented diagnostic surface (see the <c>/ani-debug</c>
/// skill), so "does this still correlate correctly?" is a behaviour worth pinning, not incidental
/// output.
/// </summary>
public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<string> _messages = [];
    private readonly List<(LogLevel Level, string Message)> _entries = [];

    public IReadOnlyList<string> Messages => _messages;

    /// <summary>
    /// The same messages paired with the level they were logged at, for the cases where the level
    /// is the behaviour — Sentry's ILogger integration turns Error into an event and Information
    /// into a breadcrumb, so "was this reported?" is a question about the level.
    /// </summary>
    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries;

    /// <summary>Every captured message containing <paramref name="fragment"/>.</summary>
    public IReadOnlyList<string> Containing(string fragment)
        => [.. _messages.Where(m => m.Contains(fragment, StringComparison.Ordinal))];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        _messages.Add(message);
        _entries.Add((logLevel, message));
    }
}
