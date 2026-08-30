using AniSprinkles.UnitTests.Fakes;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #141. The airing check's control flow used to live in <c>AiringCheckWorker.DoWork</c>, in the
/// MAUI app project. Its two most important rules — the checkpoint advances only after a fetch that
/// returned, and the window's end is captured before the fetch — existed only as comments, enforced
/// by nothing. These are those comments, made executable.
/// </summary>
public class AiringCheckRunnerTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private static long StartUnix => Start.ToUnixTimeSeconds();

    private static AiringEntry Entry(int mediaId, int episode, long airingAt = 0) => new()
    {
        MediaId = mediaId,
        Episode = episode,
        AiringAt = (int)airingAt,
        MediaTitle = $"Media {mediaId}",
    };

    /// <summary>Records what the runner asked for and hands back a scripted answer.</summary>
    private sealed class RecordingFetch
    {
        private readonly AiringScheduleResult _result;
        private readonly Exception? _throws;

        public RecordingFetch(IReadOnlyList<AiringEntry>? result = null, Exception? throws = null, bool truncated = false)
        {
            _result = new AiringScheduleResult(result ?? [], truncated);
            _throws = throws;
        }

        public int CallCount { get; private set; }
        public IReadOnlyList<int>? MediaIds { get; private set; }
        public long AiringAfter { get; private set; }
        public long AiringBefore { get; private set; }

        public AiringScheduleResult Invoke(IReadOnlyList<int> mediaIds, long after, long before)
        {
            CallCount++;
            MediaIds = mediaIds;
            AiringAfter = after;
            AiringBefore = before;
            return _throws is not null ? throw _throws : _result;
        }
    }

    private static FakePreferences PreferencesWith(params int[] mediaIds)
    {
        var preferences = new FakePreferences();
        AiringNotificationState.WriteMediaIds(preferences, mediaIds);
        return preferences;
    }

    // ── The checkpoint invariant ────────────────────────────────────

    [Fact]
    public void WhenTheFetchThrows_TheCheckpointIsNotAdvanced()
    {
        // The whole point of the checkpoint discipline: a failed window must stay available for the
        // next run rather than being silently marked as checked.
        var preferences = PreferencesWith(21);
        AiringNotificationState.AdvanceCheckpoint(preferences, StartUnix - 3600);
        var fetch = new RecordingFetch(throws: new HttpRequestException("503"));

        Assert.Throws<HttpRequestException>(() => AiringCheckRunner.Run(
            preferences, new ManualTimeProvider(Start), fetch.Invoke, _ => { }, () => false));

        Assert.Equal(StartUnix - 3600, AiringNotificationState.ReadCheckpoint(preferences, StartUnix));
    }

    [Fact]
    public void WhenTheFetchThrows_NothingIsNotified_AndTheNotifiedSetIsUntouched()
    {
        var preferences = PreferencesWith(21);
        var fetch = new RecordingFetch(throws: new InvalidOperationException("AniList returned errors"));
        var notified = new List<AiringEntry>();

        Assert.Throws<InvalidOperationException>(() => AiringCheckRunner.Run(
            preferences, new ManualTimeProvider(Start), fetch.Invoke, notified.Add, () => false));

        Assert.Empty(notified);
        Assert.False(preferences.ContainsKey(AiringNotificationState.NotifiedKey));
    }

    [Fact]
    public void AfterASuccessfulFetch_TheCheckpointAdvancesToTheWindowEnd()
    {
        var preferences = PreferencesWith(21);
        var fetch = new RecordingFetch([Entry(21, 1050)]);

        var outcome = AiringCheckRunner.Run(
            preferences, new ManualTimeProvider(Start), fetch.Invoke, _ => { }, () => false);

        Assert.Equal(AiringCheckStatus.Completed, outcome.Status);
        Assert.Equal(StartUnix, AiringNotificationState.ReadCheckpoint(preferences, StartUnix + 9999));
    }

    [Fact]
    public void TheWindow_RunsFromTheStoredCheckpointToNow()
    {
        var preferences = PreferencesWith(21, 16498);
        AiringNotificationState.AdvanceCheckpoint(preferences, StartUnix - 900);
        var fetch = new RecordingFetch();

        AiringCheckRunner.Run(preferences, new ManualTimeProvider(Start), fetch.Invoke, _ => { }, () => false);

        Assert.Equal(StartUnix - 900, fetch.AiringAfter);
        Assert.Equal(StartUnix, fetch.AiringBefore);
        Assert.Equal([21, 16498], fetch.MediaIds);
    }

    [Fact]
    public void TheWindowEnd_AndTheNewCheckpoint_AreTheSameInstant()
    {
        // Captured before the fetch and reused afterwards. Reading the clock again after the fetch
        // would leave its duration outside every window, losing anything that aired mid-request.
        var preferences = PreferencesWith(21);
        var fetch = new RecordingFetch();

        AiringCheckRunner.Run(preferences, new ManualTimeProvider(Start), fetch.Invoke, _ => { }, () => false);

        Assert.Equal(fetch.AiringBefore, AiringNotificationState.ReadCheckpoint(preferences, StartUnix + 9999));
    }

    [Fact]
    public void TheWindow_IsCurrentlyUnclamped()
    {
        // Documents today's behaviour rather than endorsing it: a checkpoint left behind by a long
        // gap produces an arbitrarily wide window. #144 tracks bounding this, and will flip this
        // test — #141 deliberately preserved the arithmetic unchanged.
        var preferences = PreferencesWith(21);
        AiringNotificationState.AdvanceCheckpoint(preferences, StartUnix - (30 * 86400));
        var fetch = new RecordingFetch();

        AiringCheckRunner.Run(preferences, new ManualTimeProvider(Start), fetch.Invoke, _ => { }, () => false);

        Assert.Equal(30 * 86400, fetch.AiringBefore - fetch.AiringAfter);
    }

    // ── Truncated fetches ───────────────────────────────────────────

    [Fact]
    public void ATruncatedFetch_AdvancesOnlyAsFarAsTheNewestEntrySeen()
    {
        // Paging stopped at its bound, so episodes past it were never read. Advancing to the end of
        // the window would skip them permanently — the same silent loss that makes a failed fetch
        // throw rather than return empty. The next run re-reads from the last entry we did see.
        var preferences = PreferencesWith(21);
        AiringNotificationState.AdvanceCheckpoint(preferences, StartUnix - 7200);
        var fetch = new RecordingFetch(
            [Entry(21, 1050, StartUnix - 5400), Entry(16498, 25, StartUnix - 3600)],
            truncated: true);

        var outcome = AiringCheckRunner.Run(
            preferences, new ManualTimeProvider(Start), fetch.Invoke, _ => { }, () => false);

        Assert.Equal(AiringCheckStatus.Truncated, outcome.Status);
        Assert.Equal(StartUnix - 3600, AiringNotificationState.ReadCheckpoint(preferences, StartUnix + 9999));
    }

    [Fact]
    public void ATruncatedFetch_StillNotifiesWhatItRead()
    {
        var preferences = PreferencesWith(21);
        var notified = new List<AiringEntry>();
        var fetch = new RecordingFetch([Entry(21, 1050, StartUnix - 60)], truncated: true);

        AiringCheckRunner.Run(preferences, new ManualTimeProvider(Start), fetch.Invoke, notified.Add, () => false);

        Assert.Single(notified);
    }

    [Fact]
    public void ATruncatedFetchWithNothingToShow_StillAdvancesToTheWindowEnd()
    {
        // The one case that must move on. There is no entry to anchor to, and holding the checkpoint
        // would re-fetch the same oversized window every run forever, burning a full page budget
        // each time and never recovering. Nothing was found, so nothing is lost.
        var preferences = PreferencesWith(21);
        AiringNotificationState.AdvanceCheckpoint(preferences, StartUnix - 7200);
        var fetch = new RecordingFetch(truncated: true);

        AiringCheckRunner.Run(preferences, new ManualTimeProvider(Start), fetch.Invoke, _ => { }, () => false);

        Assert.Equal(StartUnix, AiringNotificationState.ReadCheckpoint(preferences, StartUnix + 9999));
    }

    [Fact]
    public void ATruncatedFetch_NeverMovesTheCheckpointBackwards()
    {
        // A stale airingAt — clock skew, or an entry from before the window — must not rewind the
        // checkpoint and cause everything since to be re-notified.
        var preferences = PreferencesWith(21);
        AiringNotificationState.AdvanceCheckpoint(preferences, StartUnix - 3600);
        var fetch = new RecordingFetch([Entry(21, 1050, StartUnix - 99999)], truncated: true);

        AiringCheckRunner.Run(preferences, new ManualTimeProvider(Start), fetch.Invoke, _ => { }, () => false);

        Assert.Equal(StartUnix - 3600, AiringNotificationState.ReadCheckpoint(preferences, StartUnix + 9999));
    }

    [Fact]
    public void ATruncatedFetch_NeverMovesTheCheckpointPastTheWindow()
    {
        var preferences = PreferencesWith(21);
        var fetch = new RecordingFetch([Entry(21, 1050, StartUnix + 99999)], truncated: true);

        AiringCheckRunner.Run(preferences, new ManualTimeProvider(Start), fetch.Invoke, _ => { }, () => false);

        Assert.Equal(StartUnix, AiringNotificationState.ReadCheckpoint(preferences, StartUnix + 999999));
    }

    // ── No media IDs ────────────────────────────────────────────────

    [Fact]
    public void WithNoCachedMediaIds_NothingIsFetchedAndTheCheckpointIsLeftAlone()
    {
        var preferences = new FakePreferences();
        var fetch = new RecordingFetch();

        var outcome = AiringCheckRunner.Run(
            preferences, new ManualTimeProvider(Start), fetch.Invoke, _ => { }, () => false);

        Assert.Equal(AiringCheckStatus.NoMediaIds, outcome.Status);
        Assert.Equal(0, fetch.CallCount);
        Assert.False(preferences.ContainsKey(AiringNotificationState.LastCheckKey));
    }

    // ── Dedup ───────────────────────────────────────────────────────

    [Fact]
    public void AnEpisodeAlreadyNotified_IsNotNotifiedAgain()
    {
        var preferences = PreferencesWith(21);
        var notified = new List<AiringEntry>();
        var time = new ManualTimeProvider(Start);

        AiringCheckRunner.Run(preferences, time, new RecordingFetch([Entry(21, 1050)]).Invoke, notified.Add, () => false);
        time.Advance(TimeSpan.FromMinutes(15));
        AiringCheckRunner.Run(preferences, time, new RecordingFetch([Entry(21, 1050)]).Invoke, notified.Add, () => false);

        Assert.Single(notified);
    }

    [Fact]
    public void ANewEpisodeOfAnAlreadyNotifiedMedia_IsStillNotified()
    {
        var preferences = PreferencesWith(21);
        var notified = new List<AiringEntry>();
        var time = new ManualTimeProvider(Start);

        AiringCheckRunner.Run(preferences, time, new RecordingFetch([Entry(21, 1050)]).Invoke, notified.Add, () => false);
        time.Advance(TimeSpan.FromDays(7));
        AiringCheckRunner.Run(preferences, time, new RecordingFetch([Entry(21, 1051)]).Invoke, notified.Add, () => false);

        Assert.Equal([1050, 1051], notified.Select(e => e.Episode));
    }

    [Fact]
    public void DuplicatesWithinOneFetch_AreNotifiedOnce()
    {
        var preferences = PreferencesWith(21);
        var notified = new List<AiringEntry>();

        var outcome = AiringCheckRunner.Run(
            preferences,
            new ManualTimeProvider(Start),
            new RecordingFetch([Entry(21, 1050), Entry(21, 1050)]).Invoke,
            notified.Add,
            () => false);

        Assert.Single(notified);
        Assert.Equal(2, outcome.Examined);
        Assert.Equal(1, outcome.Notified);
    }

    // ── Cancellation (the sign-out race) ────────────────────────────

    [Fact]
    public void WhenCancelledBeforeTheFirstNotification_NothingIsPostedAndNothingIsPersisted()
    {
        // Sign-out cancels the WorkManager job and clears this state, but cancellation does not
        // interrupt a run already under way. Without this guard the run would post the previous
        // user's notifications and rewrite the keys sign-out had just removed.
        var preferences = PreferencesWith(21);
        var notified = new List<AiringEntry>();

        var outcome = AiringCheckRunner.Run(
            preferences,
            new ManualTimeProvider(Start),
            new RecordingFetch([Entry(21, 1050), Entry(16498, 25)]).Invoke,
            notified.Add,
            () => true);

        Assert.Equal(AiringCheckStatus.Cancelled, outcome.Status);
        Assert.Empty(notified);
        Assert.False(preferences.ContainsKey(AiringNotificationState.LastCheckKey));
        Assert.False(preferences.ContainsKey(AiringNotificationState.NotifiedKey));
    }

    [Fact]
    public void WhenCancelledPartWayThrough_TheRemainingEpisodesAreNotNotified()
    {
        var preferences = PreferencesWith(21);
        var notified = new List<AiringEntry>();
        bool cancelled = false;

        var outcome = AiringCheckRunner.Run(
            preferences,
            new ManualTimeProvider(Start),
            new RecordingFetch([Entry(21, 1050), Entry(16498, 25), Entry(101922, 12)]).Invoke,
            entry =>
            {
                notified.Add(entry);
                cancelled = true;
            },
            () => cancelled);

        Assert.Equal(AiringCheckStatus.Cancelled, outcome.Status);
        Assert.Single(notified);
        Assert.False(preferences.ContainsKey(AiringNotificationState.LastCheckKey));
        Assert.False(preferences.ContainsKey(AiringNotificationState.NotifiedKey));
    }

    [Fact]
    public void WhenCancelledAfterTheLastNotification_TheWritesAreStillSuppressed()
    {
        // The loop guard cannot fire here — there is nothing left to iterate — so the second check
        // before the writes is what covers this.
        var preferences = PreferencesWith(21);
        bool cancelled = false;

        var outcome = AiringCheckRunner.Run(
            preferences,
            new ManualTimeProvider(Start),
            new RecordingFetch([Entry(21, 1050)]).Invoke,
            _ => cancelled = true,
            () => cancelled);

        Assert.Equal(AiringCheckStatus.Cancelled, outcome.Status);
        Assert.False(preferences.ContainsKey(AiringNotificationState.LastCheckKey));
        Assert.False(preferences.ContainsKey(AiringNotificationState.NotifiedKey));
    }

    [Fact]
    public void CancellationWithNothingToNotify_StillSuppressesTheCheckpointAdvance()
    {
        var preferences = PreferencesWith(21);

        var outcome = AiringCheckRunner.Run(
            preferences, new ManualTimeProvider(Start), new RecordingFetch().Invoke, _ => { }, () => true);

        Assert.Equal(AiringCheckStatus.Cancelled, outcome.Status);
        Assert.False(preferences.ContainsKey(AiringNotificationState.LastCheckKey));
    }

    [Fact]
    public void WhenPostingANotificationThrows_TheCheckpointIsNotAdvanced()
    {
        // Same rule as a failed fetch: a run that did not finish must leave its window available.
        // The entries already notified before the throw are re-notified next run, which Android
        // renders as an update in place rather than a duplicate — the deterministic notification id
        // is what makes that true.
        var preferences = PreferencesWith(21);
        var fetch = new RecordingFetch([Entry(21, 1050), Entry(16498, 25)]);

        Assert.Throws<InvalidOperationException>(() => AiringCheckRunner.Run(
            preferences,
            new ManualTimeProvider(Start),
            fetch.Invoke,
            _ => throw new InvalidOperationException("notification manager unavailable"),
            () => false));

        Assert.False(preferences.ContainsKey(AiringNotificationState.LastCheckKey));
        Assert.False(preferences.ContainsKey(AiringNotificationState.NotifiedKey));
    }

    // ── Pruning across runs ─────────────────────────────────────────

    [Fact]
    public void TheNotifiedSet_DoesNotGrowWithoutBound()
    {
        var preferences = PreferencesWith(21);
        var time = new ManualTimeProvider(Start);

        for (int episode = 1; episode <= 10; episode++)
        {
            AiringCheckRunner.Run(preferences, time, new RecordingFetch([Entry(21, episode)]).Invoke, _ => { }, () => false);
            time.Advance(TimeSpan.FromDays(1));
        }

        // Ten daily runs ending on day 9, seven-day retention, and the boundary is inclusive — so
        // episodes 1 and 2 (days 0 and 1) fall outside the cutoff and 3 through 10 survive.
        var stored = AiringNotificationState.ReadNotifiedSet(preferences);
        Assert.Equal(["21:10", "21:3", "21:4", "21:5", "21:6", "21:7", "21:8", "21:9"], stored.Keys.Order());
    }
}
