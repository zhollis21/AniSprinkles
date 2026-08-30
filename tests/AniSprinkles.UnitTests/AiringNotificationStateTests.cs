using System.Text.Json;
using AniSprinkles.UnitTests.Fakes;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #141. All of this used to be private static methods on <c>AiringCheckWorker</c>, in the MAUI app
/// project, where the test suite could not reach it — so whether a corrupt stored blob degrades
/// gracefully inside a background worker, and whether the prune actually bounds its growth, were
/// both untestable assertions in a comment.
/// </summary>
public class AiringNotificationStateTests
{
    private const long Now = 1_700_000_000;

    // ── Media IDs ───────────────────────────────────────────────────

    [Fact]
    public void MediaIds_RoundTrip()
    {
        var preferences = new FakePreferences();

        AiringNotificationState.WriteMediaIds(preferences, [21, 16498, 101922]);

        Assert.Equal([21, 16498, 101922], AiringNotificationState.ReadMediaIds(preferences));
    }

    [Fact]
    public void MediaIds_WhenUnset_AreEmpty()
        => Assert.Empty(AiringNotificationState.ReadMediaIds(new FakePreferences()));

    [Theory]
    [InlineData("", new int[0])]
    [InlineData("   ", new int[0])]
    [InlineData("21,,16498", new[] { 21, 16498 })]
    [InlineData("21, 16498 ,101922", new[] { 21, 16498, 101922 })]
    [InlineData("21,notanumber,16498", new[] { 21, 16498 })]
    [InlineData("notanumber", new int[0])]
    public void MalformedMediaIds_SkipTheBadEntriesRatherThanThrowing(string stored, int[] expected)
    {
        var preferences = new FakePreferences();
        preferences.Set(AiringNotificationState.MediaIdsKey, stored);

        Assert.Equal(expected, AiringNotificationState.ReadMediaIds(preferences));
    }

    // ── Checkpoint ──────────────────────────────────────────────────

    [Fact]
    public void AnUnsetCheckpoint_DefaultsToThirtyMinutesAgo()
        => Assert.Equal(Now - 1800, AiringNotificationState.ReadCheckpoint(new FakePreferences(), Now));

    [Fact]
    public void Checkpoint_RoundTripsAndResets()
    {
        var preferences = new FakePreferences();

        AiringNotificationState.AdvanceCheckpoint(preferences, Now);
        Assert.Equal(Now, AiringNotificationState.ReadCheckpoint(preferences, Now + 9999));

        AiringNotificationState.ResetCheckpoint(preferences);
        Assert.Equal(Now - 1800, AiringNotificationState.ReadCheckpoint(preferences, Now));
    }

    // ── Notified set ────────────────────────────────────────────────

    [Fact]
    public void NotifiedSet_RoundTrips()
    {
        var preferences = new FakePreferences();
        var set = new Dictionary<string, long> { ["21:1050"] = Now };

        AiringNotificationState.PruneAndSave(preferences, set, Now, hasNewEntries: true);

        Assert.Equal(set, AiringNotificationState.ReadNotifiedSet(preferences));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ this is not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\"a string\"")]
    [InlineData("null")]
    public void ACorruptNotifiedSet_DegradesToEmptyRatherThanThrowing(string stored)
    {
        // This runs inside a background worker, where a throw is invisible and costs the whole run.
        var preferences = new FakePreferences();
        preferences.Set(AiringNotificationState.NotifiedKey, stored);

        Assert.Empty(AiringNotificationState.ReadNotifiedSet(preferences));
    }

    [Fact]
    public void EntriesOlderThanTheCutoff_ArePruned()
    {
        var preferences = new FakePreferences();
        long cutoff = Now - (AiringNotificationState.StaleEntryDays * 86400);
        var set = new Dictionary<string, long>
        {
            ["21:1"] = cutoff - 1,   // stale by one second
            ["21:2"] = cutoff,       // exactly on the boundary — kept
            ["21:3"] = Now,
        };

        AiringNotificationState.PruneAndSave(preferences, set, Now, hasNewEntries: false);

        var reread = AiringNotificationState.ReadNotifiedSet(preferences);
        Assert.Equal(["21:2", "21:3"], reread.Keys.Order());
    }

    [Fact]
    public void WithNothingNewAndNothingStale_NothingIsWritten()
    {
        var preferences = new FakePreferences();
        var set = new Dictionary<string, long> { ["21:1050"] = Now };
        int writesBefore = preferences.SetCount;

        AiringNotificationState.PruneAndSave(preferences, set, Now, hasNewEntries: false);

        Assert.Equal(writesBefore, preferences.SetCount);
    }

    [Fact]
    public void APruneAlone_IsEnoughToTriggerAWrite()
    {
        var preferences = new FakePreferences();
        var set = new Dictionary<string, long> { ["21:1"] = Now - (30 * 86400) };

        AiringNotificationState.PruneAndSave(preferences, set, Now, hasNewEntries: false);

        Assert.Empty(AiringNotificationState.ReadNotifiedSet(preferences));
        Assert.Equal("{}", preferences.Get(AiringNotificationState.NotifiedKey, string.Empty));
    }

    // ── Dedup key and notification id ───────────────────────────────

    [Fact]
    public void TheDedupKey_IsMediaIdAndEpisode()
        => Assert.Equal("21:1050", AiringNotificationState.DedupKey(21, 1050));

    [Fact]
    public void TheNotificationId_IsStableAcrossProcesses()
    {
        // Golden values. The point of this test is that they are constants at all: the previous
        // implementation used HashCode.Combine, which is seeded randomly per process, so the same
        // episode got a different notification id after every restart and Android could not update
        // an already-posted notification in place. Any expected value here would have failed.
        Assert.Equal(1_454_118_894, AiringNotificationState.NotificationId(21, 1050));
        Assert.Equal(1_975_833_230, AiringNotificationState.NotificationId(16498, 25));
    }

    [Fact]
    public void TheNotificationId_IsNeverNegative()
    {
        // Ids are ints and a negative one is legal, but confusing to read in a bug report.
        foreach (int mediaId in (int[])[1, 21, 16498, 199_999, int.MaxValue])
        {
            foreach (int episode in (int[])[1, 25, 1050, 9999])
            {
                Assert.True(AiringNotificationState.NotificationId(mediaId, episode) >= 0);
            }
        }
    }

    [Fact]
    public void DifferentMediaWithTheSameEpisode_GetDifferentIds()
    {
        var ids = new[] { 21, 16498, 101922, 195600, 182205, 178789 }
            .Select(mediaId => AiringNotificationState.NotificationId(mediaId, 12))
            .ToHashSet();

        Assert.Equal(6, ids.Count);
    }

    [Fact]
    public void ANotifiedSetWithWrongValueTypes_DegradesToEmpty()
    {
        // Shape-valid JSON whose values aren't longs. Deserialize throws JsonException rather than
        // returning a partial dictionary, and a background worker must not die on it.
        var preferences = new FakePreferences();
        preferences.Set(AiringNotificationState.NotifiedKey, """{"21:1":"not-a-number"}""");

        Assert.Empty(AiringNotificationState.ReadNotifiedSet(preferences));
    }

    [Fact]
    public void WritingNoMediaIds_ReadsBackEmpty()
    {
        // The "last airing show finished" case: an empty write must be distinguishable from a stale
        // one, or the worker keeps polling yesterday's list.
        var preferences = new FakePreferences();
        AiringNotificationState.WriteMediaIds(preferences, [21, 16498]);

        AiringNotificationState.WriteMediaIds(preferences, []);

        Assert.Empty(AiringNotificationState.ReadMediaIds(preferences));
        Assert.True(preferences.ContainsKey(AiringNotificationState.MediaIdsKey));
    }

    [Fact]
    public void DifferentEpisodesOfTheSameMedia_GetDifferentIds()
    {
        // mediaId * 1000 + episode would collide here: One Piece is past episode 1100.
        var ids = Enumerable.Range(1, 1200)
            .Select(episode => AiringNotificationState.NotificationId(21, episode))
            .ToHashSet();

        Assert.Equal(1200, ids.Count);
    }

    // ── Permission prompt ───────────────────────────────────────────

    [Fact]
    public void ThePermissionPrompt_IsNotMarkedUntilItHappens()
    {
        var preferences = new FakePreferences();
        Assert.False(AiringNotificationState.HasPromptedForPermission(preferences));

        AiringNotificationState.MarkPromptedForPermission(preferences);

        Assert.True(AiringNotificationState.HasPromptedForPermission(preferences));
    }

    // ── Sign-out ────────────────────────────────────────────────────

    [Fact]
    public void ClearAll_RemovesEveryKeyTheSubsystemOwns()
    {
        // The sign-out contract: a different user must never see the previous user's episodes.
        var preferences = new FakePreferences();
        AiringNotificationState.WriteMediaIds(preferences, [21]);
        AiringNotificationState.AdvanceCheckpoint(preferences, Now);
        AiringNotificationState.MarkPromptedForPermission(preferences);
        preferences.Set(AiringNotificationState.NotifiedKey, JsonSerializer.Serialize(new Dictionary<string, long> { ["21:1"] = Now }));

        AiringNotificationState.ClearAll(preferences);

        Assert.False(preferences.ContainsKey(AiringNotificationState.MediaIdsKey));
        Assert.False(preferences.ContainsKey(AiringNotificationState.LastCheckKey));
        Assert.False(preferences.ContainsKey(AiringNotificationState.NotifiedKey));
        Assert.False(preferences.ContainsKey(AiringNotificationState.PermissionPromptedKey));
    }
}
