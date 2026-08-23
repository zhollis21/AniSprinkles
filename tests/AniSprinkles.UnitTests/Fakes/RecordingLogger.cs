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

    public IReadOnlyList<string> Messages => _messages;

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
        => _messages.Add(formatter(state, exception));
}
