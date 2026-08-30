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

    private readonly object _gate = new();
    private string? _route;
    private IDictionary<string, object>? _parameters;
    private int? _pendingNonce;

    /// <summary>
    /// <see langword="null"/> means nothing has been followed yet — distinguished from a stored 0 by
    /// key presence rather than by a sentinel value, because 0 is a perfectly legal nonce.
    /// </summary>
    private int? ConsumedNonce
    {
        get => preferences.ContainsKey(ConsumedNonceKey) ? preferences.Get(ConsumedNonceKey, 0) : null;
        set
        {
            if (value is int nonce)
            {
                preferences.Set(ConsumedNonceKey, nonce);
            }
        }
    }

    /// <summary>True while a link is waiting for a Shell that can take it.</summary>
    public bool HasPending
    {
        get
        {
            lock (_gate)
            {
                return _route is not null;
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
        lock (_gate)
        {
            if (nonce is int candidate && candidate == ConsumedNonce)
            {
                logger?.LogInformation("NAVTRACE DeepLink → ignoring replayed intent for {Route}", route);
                return false;
            }

            _route = route;
            _parameters = parameters;
            _pendingNonce = nonce;
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
        string route;
        IDictionary<string, object> parameters;
        int? nonce;

        lock (_gate)
        {
            if (_route is null || !shellReady)
            {
                return false;
            }

            route = _route;
            parameters = _parameters!;
            nonce = _pendingNonce;

            // Taken before awaiting, so a second drain racing this one finds nothing and cannot
            // navigate twice. The nonce is only *consumed* once the navigation succeeds — see below.
            _route = null;
            _parameters = null;
            _pendingNonce = null;
        }

        // A notification tap is a navigation entry point that exists nowhere else in the app, so it
        // is worth tracing: "how did they get to this page from a cold start" is otherwise
        // unanswerable from a crash report. Same shape as BioLinkFollower's.
        logger?.LogInformation("NAVTRACE DeepLink → {Route}", route);
        SentrySdk.AddBreadcrumb($"Follow notification deep link ({route})", "navigation", "user");

        try
        {
            await navigation.GoToAsync(route, animate: false, parameters);
        }
        catch
        {
            // The user tapped a notification and never arrived. Put the link back so a later drain
            // — OnResume, or Shell's next Navigated — can try again, and leave the nonce unconsumed
            // so a re-delivered intent is not mistaken for a replay of a tap that never landed.
            lock (_gate)
            {
                // Only if nothing newer arrived while we were awaiting: the most recent tap is what
                // the user last asked for, and restoring over it would resurrect a stale one.
                if (_route is null)
                {
                    _route = route;
                    _parameters = parameters;
                    _pendingNonce = nonce;
                }
            }

            throw;
        }

        lock (_gate)
        {
            ConsumedNonce = nonce;
        }

        return true;
    }
}
