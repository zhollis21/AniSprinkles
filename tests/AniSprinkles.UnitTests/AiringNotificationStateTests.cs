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

    [Test]
    public void MediaIds_RoundTrip()
    {
        var preferences = new FakePreferences();

        AiringNotificationState.WriteMediaIds(preferences, [21, 16498, 101922]);

        Assert.That(AiringNotificationState.ReadMediaIds(preferences), Is.EqualTo([21, 16498, 101922]));
    }

    [Test]
    public void MediaIds_WhenUnset_AreEmpty()
        => Assert.That(AiringNotificationState.ReadMediaIds(new FakePreferences()), Is.Empty);

    [TestCase("", new int[0])]
    [TestCase("   ", new int[0])]
    [TestCase("21,,16498", new[] { 21, 16498 })]
    [TestCase("21, 16498 ,101922", new[] { 21, 16498, 101922 })]
    [TestCase("21,notanumber,16498", new[] { 21, 16498 })]
    [TestCase("notanumber", new int[0])]
    public void MalformedMediaIds_SkipTheBadEntriesRatherThanThrowing(string stored, int[] expected)
    {
        var preferences = new FakePreferences();
        preferences.Set(AiringNotificationState.MediaIdsKey, stored);

        Assert.That(AiringNotificationState.ReadMediaIds(preferences), Is.EqualTo(expected));
    }

    // ── Checkpoint ──────────────────────────────────────────────────

    [Test]
    public void AnUnsetCheckpoint_DefaultsToThirtyMinutesAgo()
        => Assert.That(AiringNotificationState.ReadCheckpoint(new FakePreferences(), Now), Is.EqualTo(Now - 1800));

    [Test]
    public void Checkpoint_RoundTripsAndResets()
    {
        var preferences = new FakePreferences();

        AiringNotificationState.AdvanceCheckpoint(preferences, Now);
        Assert.That(AiringNotificationState.ReadCheckpoint(preferences, Now + 9999), Is.EqualTo(Now));

        AiringNotificationState.ResetCheckpoint(preferences);
        Assert.That(AiringNotificationState.ReadCheckpoint(preferences, Now), Is.EqualTo(Now - 1800));
    }

    // ── Notified set ────────────────────────────────────────────────

    [Test]
    public void NotifiedSet_RoundTrips()
    {
        var preferences = new FakePreferences();
        var set = new Dictionary<string, long> { ["21:1050"] = Now };

        AiringNotificationState.PruneAndSave(preferences, set, Now, hasNewEntries: true);

        Assert.That(AiringNotificationState.ReadNotifiedSet(preferences), Is.EqualTo(set));
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("{ this is not json")]
    [TestCase("[1,2,3]")]
    [TestCase("\"a string\"")]
    [TestCase("null")]
    public void ACorruptNotifiedSet_DegradesToEmptyRatherThanThrowing(string stored)
    {
        // This runs inside a background worker, where a throw is invisible and costs the whole run.
        var preferences = new FakePreferences();
        preferences.Set(AiringNotificationState.NotifiedKey, stored);

        Assert.That(AiringNotificationState.ReadNotifiedSet(preferences), Is.Empty);
    }

    [Test]
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
        Assert.That(reread.Keys.Order(), Is.EqualTo(["21:2", "21:3"]));
    }

    [Test]
    public void WithNothingNewAndNothingStale_NothingIsWritten()
    {
        var preferences = new FakePreferences();
        var set = new Dictionary<string, long> { ["21:1050"] = Now };
        int writesBefore = preferences.SetCount;

        AiringNotificationState.PruneAndSave(preferences, set, Now, hasNewEntries: false);

        Assert.That(preferences.SetCount, Is.EqualTo(writesBefore));
    }

    [Test]
    public void APruneAlone_IsEnoughToTriggerAWrite()
    {
        var preferences = new FakePreferences();
        var set = new Dictionary<string, long> { ["21:1"] = Now - (30 * 86400) };

        AiringNotificationState.PruneAndSave(preferences, set, Now, hasNewEntries: false);

        Assert.That(AiringNotificationState.ReadNotifiedSet(preferences), Is.Empty);
        Assert.That(preferences.Get(AiringNotificationState.NotifiedKey, string.Empty), Is.EqualTo("{}"));
    }

    // ── Dedup key and notification id ───────────────────────────────

    [Test]
    public void TheDedupKey_IsMediaIdAndEpisode()
        => Assert.That(AiringNotificationState.DedupKey(21, 1050), Is.EqualTo("21:1050"));

    [Test]
    public void TheNotificationId_IsStableAcrossProcesses()
    {
        // Golden values. The point of this test is that they are constants at all: the previous
        // implementation used HashCode.Combine, which is seeded randomly per process, so the same
        // episode got a different notification id after every restart and Android could not update
        // an already-posted notification in place. Any expected value here would have failed.
        Assert.That(AiringNotificationState.NotificationId(21, 1050), Is.EqualTo(1_454_118_894));
        Assert.That(AiringNotificationState.NotificationId(16498, 25), Is.EqualTo(1_975_833_230));
    }

    [Test]
    public void TheNotificationId_IsNeverNegative()
    {
        // Ids are ints and a negative one is legal, but confusing to read in a bug report.
        foreach (int mediaId in (int[])[1, 21, 16498, 199_999, int.MaxValue])
        {
            foreach (int episode in (int[])[1, 25, 1050, 9999])
            {
                Assert.That(AiringNotificationState.NotificationId(mediaId, episode) >= 0, Is.True);
            }
        }
    }

    [Test]
    public void DifferentMediaWithTheSameEpisode_GetDifferentIds()
    {
        var ids = new[] { 21, 16498, 101922, 195600, 182205, 178789 }
            .Select(mediaId => AiringNotificationState.NotificationId(mediaId, 12))
            .ToHashSet();

        Assert.That(ids.Count, Is.EqualTo(6));
    }

    [Test]
    public void ANotifiedSetWithWrongValueTypes_DegradesToEmpty()
    {
        // Shape-valid JSON whose values aren't longs. Deserialize throws JsonException rather than
        // returning a partial dictionary, and a background worker must not die on it.
        var preferences = new FakePreferences();
        preferences.Set(AiringNotificationState.NotifiedKey, """{"21:1":"not-a-number"}""");

        Assert.That(AiringNotificationState.ReadNotifiedSet(preferences), Is.Empty);
    }

    [Test]
    public void WritingNoMediaIds_ReadsBackEmpty()
    {
        // The "last airing show finished" case: an empty write must be distinguishable from a stale
        // one, or the worker keeps polling yesterday's list.
        var preferences = new FakePreferences();
        AiringNotificationState.WriteMediaIds(preferences, [21, 16498]);

        AiringNotificationState.WriteMediaIds(preferences, []);

        Assert.That(AiringNotificationState.ReadMediaIds(preferences), Is.Empty);
        Assert.That(preferences.ContainsKey(AiringNotificationState.MediaIdsKey), Is.True);
    }

    [Test]
    public void DifferentEpisodesOfTheSameMedia_GetDifferentIds()
    {
        // mediaId * 1000 + episode would collide here: One Piece is past episode 1100.
        var ids = Enumerable.Range(1, 1200)
            .Select(episode => AiringNotificationState.NotificationId(21, episode))
            .ToHashSet();

        Assert.That(ids.Count, Is.EqualTo(1200));
    }

    // ── Permission prompt ───────────────────────────────────────────

    [Test]
    public void ThePermissionPrompt_IsNotMarkedUntilItHappens()
    {
        var preferences = new FakePreferences();
        Assert.That(AiringNotificationState.HasPromptedForPermission(preferences), Is.False);

        AiringNotificationState.MarkPromptedForPermission(preferences);

        Assert.That(AiringNotificationState.HasPromptedForPermission(preferences), Is.True);
    }

    // ── Sign-out ────────────────────────────────────────────────────

    [Test]
    public void ClearAll_RemovesEveryKeyTheSubsystemOwns()
    {
        // The sign-out contract: a different user must never see the previous user's episodes.
        var preferences = new FakePreferences();
        AiringNotificationState.WriteMediaIds(preferences, [21]);
        AiringNotificationState.AdvanceCheckpoint(preferences, Now);
        AiringNotificationState.MarkPromptedForPermission(preferences);
        preferences.Set(AiringNotificationState.NotifiedKey, JsonSerializer.Serialize(new Dictionary<string, long> { ["21:1"] = Now }));

        AiringNotificationState.ClearAll(preferences);

        Assert.That(preferences.ContainsKey(AiringNotificationState.MediaIdsKey), Is.False);
        Assert.That(preferences.ContainsKey(AiringNotificationState.LastCheckKey), Is.False);
        Assert.That(preferences.ContainsKey(AiringNotificationState.NotifiedKey), Is.False);
        Assert.That(preferences.ContainsKey(AiringNotificationState.PermissionPromptedKey), Is.False);
    }
}
