namespace AniSprinkles.Services;

/// <summary>One aired episode the check found, flattened out of whatever the caller fetched.</summary>
public sealed record AiringEntry
{
    public required int MediaId { get; init; }
    public required int Episode { get; init; }
    public required string MediaTitle { get; init; }

    /// <summary>
    /// Unix seconds the episode aired. Carried so a truncated fetch can still advance the checkpoint
    /// safely — see <see cref="AiringScheduleResult.Truncated"/>.
    /// </summary>
    public int AiringAt { get; init; }

    public string? CoverImageUrl { get; init; }
}

/// <summary>What one fetch returned, and whether it managed to read the whole window.</summary>
/// <param name="Truncated">
/// True when paging stopped at its safety bound rather than because the server said there was no
/// next page. The entries are real, but the window was not read to the end — so the checkpoint must
/// not jump past what was actually seen.
/// </param>
public readonly record struct AiringScheduleResult(IReadOnlyList<AiringEntry> Entries, bool Truncated);

/// <summary>Why a run ended, for the caller's log line.</summary>
public enum AiringCheckStatus
{
    /// <summary>Nothing cached to check. No fetch was made and the checkpoint was left alone.</summary>
    NoMediaIds,

    /// <summary>Ran to completion; the checkpoint advanced to the end of the window.</summary>
    Completed,

    /// <summary>
    /// Paging hit its safety bound, so the window was not read to the end. Notifications were still
    /// posted for what was read, and the checkpoint advanced only as far as that.
    /// </summary>
    Truncated,

    /// <summary>Stopped partway. Nothing was persisted — see the cancellation remarks on <see cref="AiringCheckRunner"/>.</summary>
    Cancelled,
}

public readonly record struct AiringCheckOutcome(AiringCheckStatus Status, int Examined, int Notified);

/// <summary>
/// The airing check itself, lifted out of the Android worker so it can be tested (#141).
/// <para>
/// The worker keeps everything platform-bound — WorkManager, its own HTTP/GraphQL layer, posting
/// notifications — and hands the fetch and notify in as delegates. That preserves the property the
/// worker exists for: it makes no use of MAUI DI, so it still runs after a reboot the app has not
/// been launched since.
/// </para>
/// <para>
/// Two rules here are load-bearing and previously existed only as comments, enforced by nothing:
/// the checkpoint advances only after a fetch that returned, and the window's end is captured
/// <em>before</em> the fetch rather than after.
/// </para>
/// </summary>
public static class AiringCheckRunner
{
    /// <summary>
    /// Runs one check.
    /// </summary>
    /// <param name="preferences">Where the checkpoint and dedup set live.</param>
    /// <param name="timeProvider">Supplies the window's end. Injected so the window arithmetic is testable.</param>
    /// <param name="fetch">
    /// Given the cached media IDs and the window <c>(airingAfter, airingBefore)</c>, returns what
    /// aired in it. <b>Must throw on any failure</b> — including a transport-level success carrying
    /// an error payload. A throw is what keeps the window available for the next run; returning an
    /// empty list instead silently marks a failed window as checked.
    /// </param>
    /// <param name="notify">Posts one notification. Called once per episode not already notified.</param>
    /// <param name="isCancelled">
    /// Polled before each notification and again before the final writes. Cancelling mid-run
    /// persists nothing at all, which is deliberate: sign-out cancels the WorkManager job and clears
    /// this state, but cancellation does not interrupt a run already in progress. Without this, an
    /// in-flight run would post the previous user's notifications after the shade was cleared and
    /// rewrite the keys sign-out had just removed.
    /// </param>
    public static AiringCheckOutcome Run(
        IPreferences preferences,
        TimeProvider timeProvider,
        Func<IReadOnlyList<int>, long, long, AiringScheduleResult> fetch,
        Action<AiringEntry> notify,
        Func<bool> isCancelled)
    {
        var mediaIds = AiringNotificationState.ReadMediaIds(preferences);
        if (mediaIds.Count == 0)
        {
            return new AiringCheckOutcome(AiringCheckStatus.NoMediaIds, 0, 0);
        }

        // Captured before the fetch, and reused as the checkpoint below. Taking "now" afterwards
        // instead would leave the fetch's own duration outside every window, losing any episode
        // that aired while the request was in flight.
        long nowUnix = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        long lastCheck = AiringNotificationState.ReadCheckpoint(preferences, nowUnix);

        // Throws propagate: the caller's failure path must not advance the checkpoint. See #144 for
        // the flip side — nothing currently bounds how wide this window can grow.
        var result = fetch(mediaIds, lastCheck, nowUnix);
        var entries = result.Entries;

        var notifiedSet = AiringNotificationState.ReadNotifiedSet(preferences);
        bool hasNewEntries = false;
        int notified = 0;

        foreach (var entry in entries)
        {
            if (isCancelled())
            {
                return new AiringCheckOutcome(AiringCheckStatus.Cancelled, entries.Count, notified);
            }

            string key = AiringNotificationState.DedupKey(entry.MediaId, entry.Episode);
            if (notifiedSet.ContainsKey(key))
            {
                continue;
            }

            notify(entry);

            notifiedSet[key] = nowUnix;
            hasNewEntries = true;
            notified++;
        }

        // Checked once more: the loop's guard cannot fire when there was nothing to notify, and
        // cancellation between the last notification and here must still suppress both writes.
        if (isCancelled())
        {
            return new AiringCheckOutcome(AiringCheckStatus.Cancelled, entries.Count, notified);
        }

        AiringNotificationState.AdvanceCheckpoint(preferences, CheckpointFor(result, lastCheck, nowUnix));
        AiringNotificationState.PruneAndSave(preferences, notifiedSet, nowUnix, hasNewEntries);

        return new AiringCheckOutcome(
            result.Truncated ? AiringCheckStatus.Truncated : AiringCheckStatus.Completed,
            entries.Count,
            notified);
    }

    /// <summary>
    /// How far the checkpoint may move given what the fetch actually read.
    /// <para>
    /// A complete fetch read the whole window, so the checkpoint goes to its end. A truncated one
    /// did not, and jumping to the end would permanently skip every episode past the page bound —
    /// the same silent loss that makes a failed fetch throw instead of returning empty. So it
    /// advances only as far as the newest entry actually seen. Anything at exactly that instant was
    /// notified and is in the dedup set, so re-reading it next run is free.
    /// </para>
    /// <para>
    /// Truncated with nothing to show is the one case that must still jump to the end: there is no
    /// episode to anchor to, and holding the checkpoint would re-fetch the same oversized window
    /// every run forever — a deadlock that costs a full page budget each time and never recovers.
    /// Nothing was found, so nothing is lost by moving on.
    /// </para>
    /// </summary>
    private static long CheckpointFor(AiringScheduleResult result, long lastCheck, long nowUnix)
    {
        if (!result.Truncated || result.Entries.Count == 0)
        {
            return nowUnix;
        }

        long newestSeen = result.Entries.Max(entry => (long)entry.AiringAt);

        // Never go backwards, and never past the window we asked for.
        return Math.Clamp(newestSeen, lastCheck, nowUnix);
    }
}
