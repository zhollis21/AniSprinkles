using Microsoft.Extensions.Logging;
using Sentry;

namespace AniSprinkles.Utilities;

/// <summary>
/// One deep link waiting to be followed, and the rule for when it is safe to follow it (#111).
/// <para>
/// A notification tap can arrive in four different states, and they do not divide neatly into "cold"
/// and "warm". The process may be dead; it may be alive with the activity destroyed, in which case
/// MAUI's statics can survive and Shell may already exist; or the activity may be alive and either
/// backgrounded or in the foreground. So rather than branching on lifecycle, the platform side
/// stashes the link here and asks to drain it from several places — when the intent arrives, once
/// Shell reports it has navigated, and again on resume. Draining is idempotent, so extra attempts
/// cost nothing and a missed one is covered by the next.
/// </para>
/// <para>
/// Deliberately holds a route and parameters rather than a media id: a future notification type
/// stashes a different route without touching this, and <see cref="AniListLinkTarget"/> composes
/// straight into it if a link ever arrives as a URL instead.
/// </para>
/// </summary>
public sealed class PendingDeepLink(IPreferences preferences, ILogger<PendingDeepLink>? logger = null)
{
    /// <summary>
    /// The last followed tap, persisted rather than held in memory.
    /// <para>
    /// Android records the intent that <em>created</em> a task and replays it whenever it rebuilds
    /// that task after killing the process — verified on device: a restore from recents arrived at
    /// <c>OnCreate(savedInstanceState=present)</c> in a new process carrying the original extras
    /// intact, several taps later. That copy lives in the system, so clearing extras on our own
    /// <c>Intent</c> cannot reach it, and an in-memory guard dies with the process.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> cleared on sign-out: clearing it would re-enable exactly the replay
    /// this prevents. It is one integer with no bearing on which account is signed in.
    /// </para>
    /// </summary>
    public const string ConsumedNonceKey = "deeplink_consumed_nonce";

    /// <summary>One queued tap. Held as a single value so its three parts cannot drift apart.</summary>
    private sealed record PendingLink(string Route, IDictionary<string, object> Parameters, int? Nonce);

    /// <summary>
    /// Guards a background caller, of which there are none today — every entry point runs on the UI
    /// thread. It is <b>not</b> what stops two drains navigating twice: <c>lock</c> is re-entrant, so
    /// it would let a second drain straight through on the same thread. The synchronous take in
    /// <see cref="TryNavigateAsync"/>, before any await, is what does that. Moving that clear to
    /// after the await would break it while this still looked like protection.
    /// </summary>
    private readonly object _gate = new();

    private PendingLink? _pending;

    /// <summary>
    /// The nonce of a link currently being navigated. <see cref="ConsumedNonce"/> is not written
    /// until the navigation succeeds, so without this a re-delivery arriving during that await — two
    /// quick taps on one notification, before <c>SetAutoCancel</c> clears it — would find nothing to
    /// match against, queue the same tap again, and follow it a second time.
    /// </summary>
    private int? _inFlightNonce;

    /// <summary>
    /// <see langword="null"/> means nothing has been followed yet — distinguished from a stored 0 by
    /// key presence rather than by a sentinel value, because 0 is a perfectly legal nonce.
    /// </summary>
    private int? ConsumedNonce
        => preferences.ContainsKey(ConsumedNonceKey) ? preferences.Get(ConsumedNonceKey, 0) : null;

    /// <summary>
    /// Records a tap as followed. A <see langword="null"/> nonce means the caller had nothing to
    /// deduplicate on, so there is nothing to remember — spelled as a method rather than a setter
    /// because "assign null" reads like "clear it" and means the opposite.
    /// </summary>
    private void MarkConsumed(int? nonce)
    {
        if (nonce is int value)
        {
            preferences.Set(ConsumedNonceKey, value);
        }
    }

    /// <summary>True while a link is waiting for a Shell that can take it.</summary>
    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _pending is not null;
            }
        }
    }

    /// <summary>
    /// Records a link to follow, replacing any earlier one — if two notifications are tapped before
    /// either is followed, the second is what the user last asked for.
    /// </summary>
    /// <param name="nonce">
    /// Identifies this particular tap, so the same one is never followed twice. Android re-delivers
    /// the original intent when it recreates an activity, which would otherwise re-navigate and pull
    /// the user off whatever they had browsed to since. Clearing the extra on the platform side
    /// covers the in-memory case; this covers a genuine process-death restore, where the intent
    /// comes back from the task record with its extras intact. Pass <see langword="null"/> when
    /// there is nothing to deduplicate on — not 0, which is a legal nonce like any other.
    /// </param>
    /// <returns>False when this tap has already been followed, so the caller can log the difference.</returns>
    public bool Set(string route, IDictionary<string, object> parameters, int? nonce)
    {
        // Read outside the lock: it hits the preferences store, and nothing here needs it to be
        // consistent with the assignment below — a nonce only ever moves from unconsumed to consumed.
        int? consumed = ConsumedNonce;

        lock (_gate)
        {
            // Already followed, or being followed right now. The second half matters because the
            // consumed nonce is only persisted once navigation succeeds.
            if (nonce is int candidate && (candidate == consumed || candidate == _inFlightNonce))
            {
                logger?.LogInformation("NAVTRACE DeepLink → ignoring replayed intent for {Route}", route);
                return false;
            }

            _pending = new PendingLink(route, parameters, nonce);
            return true;
        }
    }

    /// <summary>
    /// Follows the pending link, if there is one and Shell is ready for it.
    /// <para>
    /// <paramref name="shellReady"/> is passed in rather than inferred because the caller is the only
    /// one that can see Shell, and because <c>MauiShellNavigationService.GoToAsync</c> returns a
    /// completed task when <c>Shell.Current</c> is null — it logs a warning and bails. So its return
    /// value cannot distinguish a real navigation from a no-op, and a drain attempted too early
    /// would otherwise clear the link and lose it silently.
    /// </para>
    /// </summary>
    /// <returns>True if navigation was actually attempted.</returns>
    public async Task<bool> TryNavigateAsync(INavigationService navigation, bool shellReady)
    {
        PendingLink taken;

        lock (_gate)
        {
            if (_pending is null || !shellReady)
            {
                return false;
            }

            // Taken before awaiting, so a second drain racing this one finds nothing and cannot
            // navigate twice. The nonce is only *consumed* once the navigation succeeds — see below.
            taken = _pending;
            _pending = null;
            _inFlightNonce = taken.Nonce;
        }

        // A notification tap is a navigation entry point that exists nowhere else in the app, so it
        // is worth tracing: "how did they get to this page from a cold start" is otherwise
        // unanswerable from a crash report. Same shape as BioLinkFollower's.
        logger?.LogInformation("NAVTRACE DeepLink → {Route}", taken.Route);
        SentrySdk.AddBreadcrumb($"Follow notification deep link ({taken.Route})", "navigation", "user");

        try
        {
            await navigation.GoToAsync(taken.Route, animate: false, taken.Parameters);
        }
        catch
        {
            // The user tapped a notification and never arrived. Put the link back so a later drain
            // — OnResume, or Shell's next Navigated — can try again, and leave the nonce unconsumed
            // so a re-delivered intent is not mistaken for a replay of a tap that never landed.
            lock (_gate)
            {
                // Cleared before restoring, or the restored link would look like a replay of itself.
                // Only if it is still ours, for the same reason as the success path below: a newer
                // tap may already have started draining while this one was awaiting.
                if (_inFlightNonce == taken.Nonce)
                {
                    _inFlightNonce = null;
                }

                // Only if nothing newer arrived while we were awaiting: the most recent tap is what
                // the user last asked for, and restoring over it would resurrect a stale one.
                _pending ??= taken;
            }

            throw;
        }

        MarkConsumed(taken.Nonce);

        lock (_gate)
        {
            // Only if it is still ours: a failure-and-retry of a newer tap could already have taken
            // over, and clearing that would reopen the window this closes.
            if (_inFlightNonce == taken.Nonce)
            {
                _inFlightNonce = null;
            }
        }

        return true;
    }
}
