namespace AniSprinkles.Services.Abstractions;

/// <summary>
/// Where a finished diagnostic report is sent (#112).
/// <para>
/// A seam for the same reason <see cref="IDialogService"/> is one, and with a sharper edge: an
/// uninitialised <c>SentrySdk</c> does not throw, it silently discards. A page model calling it
/// directly would no-op in a test and read as a pass, so "the report was sent" would never actually
/// be asserted anywhere.
/// </para>
/// </summary>
public interface IDiagnosticsSink
{
    /// <summary>
    /// Sends <paramref name="report"/>, with <paramref name="description"/> attached as the user's own
    /// account of what happened when they gave one. Returns whether it was accepted for delivery.
    /// Never throws — a failed send is reported to the user, not raised.
    /// </summary>
    Task<bool> SendAsync(string report, string? description, CancellationToken cancellationToken = default);
}
