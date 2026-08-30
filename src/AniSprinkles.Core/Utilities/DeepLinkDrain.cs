using Microsoft.Extensions.Logging;

namespace AniSprinkles.Utilities;

/// <summary>
/// What every hook that might follow a queued deep link does, in one place (#111).
/// <para>
/// Three of them ask — the intent arriving, Shell reporting it has navigated, and the activity
/// resuming — and they used to carry near-identical copies of this on the Android side, where none
/// of it could be tested and the two had already drifted apart in how they resolved services and
/// where they logged. The policy is small but it is real: never resolve navigation when nothing is
/// waiting, never throw at a lifecycle callback, and treat a missing service as "too early" rather
/// than as an error.
/// </para>
/// </summary>
public static class DeepLinkDrain
{
    /// <summary>
    /// Follows a queued link if there is one, the services exist, and Shell can take it.
    /// <para>
    /// <b>Never throws.</b> Every caller is a lifecycle callback or an event handler, where an
    /// escaping exception takes the app down, and a failed drain is recoverable on its own: the link
    /// is put back, so the next hook to fire tries again.
    /// </para>
    /// <para>
    /// <b>Must be called on the UI thread.</b> Not defensiveness — MAUI navigates on whatever thread
    /// you call it from, and off the main thread on Android that corrupts the navigation stack and
    /// leaves blank pages (dotnet/maui#13538) rather than throwing something you would notice.
    /// </para>
    /// </summary>
    /// <param name="pending">May be null when DI is not yet wired; treated as nothing to do.</param>
    /// <param name="navigation">May be null for the same reason.</param>
    /// <param name="shellReady">
    /// Whether Shell exists to navigate. Passed in because only the caller can see it, and because
    /// <c>GoToAsync</c> cannot report having done nothing — see <see cref="PendingDeepLink"/>.
    /// </param>
    /// <returns>True only if a navigation actually happened.</returns>
    public static async Task<bool> AttemptAsync(
        PendingDeepLink? pending,
        INavigationService? navigation,
        bool shellReady,
        ILogger? logger = null)
    {
        // Checked before touching navigation so the common case — a lifecycle callback with no link
        // waiting — costs nothing.
        if (pending is null || !pending.HasPending)
        {
            return false;
        }

        if (navigation is null)
        {
            logger?.LogInformation("NAVTRACE DeepLink → deferred, navigation service not available yet");
            return false;
        }

        try
        {
            return await pending.TryNavigateAsync(navigation, shellReady);
        }
        catch (Exception ex)
        {
            // PendingDeepLink has already put the link back, so this is a deferral, not a loss.
            logger?.LogWarning(ex, "Deep link navigation failed; it stays queued for the next attempt");
            return false;
        }
    }
}
