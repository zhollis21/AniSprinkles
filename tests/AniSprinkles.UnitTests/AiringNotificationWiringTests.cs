using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #141. The two places Core writes airing state, asserted through the shared key constants rather
/// than string literals.
/// <para>
/// This is the half of the duplication problem that unit tests can reach. The keys used to be
/// literals here and private consts in the app project, so a rename left everything compiling and
/// green while notifications went silently dead — no test could span the boundary. These cannot
/// prove the worker reads what the page models write, but they do pin both sides to
/// <c>AiringNotificationState</c>, which is what makes the two halves impossible to separate.
/// </para>
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class AiringNotificationWiringTests
{
    // ── MyAnimePageModel caches the IDs the worker polls ────────────

    [Fact]
    public async Task AfterAListLoad_TheReleasingMediaIdsAreCachedForTheWorker()
    {
        var preferences = new FakePreferences();
        var client = Substitute.For<IAniListClient>();
        client.GetMyAnimeListGroupedAsync(Arg.Any<CancellationToken>()).Returns(
        [
            ("Watching", (IReadOnlyList<MediaListEntry>)[Releasing(21), Finished(16498)]),
            ("Rewatching", [Releasing(101922)]),
            ("Planning", [Releasing(195600)]),
            ("Completed", [Releasing(999)]),
        ]);

        await BuildMyAnime(client, preferences).LoadAsync();

        // Watching + Rewatching + Planning, RELEASING only. Completed is excluded even when the
        // show is still airing — the user is not waiting on those episodes.
        Assert.Equal([21, 101922, 195600], AiringNotificationState.ReadMediaIds(preferences).Order());
    }

    [Fact]
    public async Task AListWithNothingReleasing_CachesAnEmptyList()
    {
        // Not the same as "never written": the worker distinguishes an empty cache from a stale one
        // only by what is stored, so a user whose last airing show finished must end up with an
        // empty value rather than yesterday's IDs.
        var preferences = new FakePreferences();
        AiringNotificationState.WriteMediaIds(preferences, [21, 16498]);

        var client = Substitute.For<IAniListClient>();
        client.GetMyAnimeListGroupedAsync(Arg.Any<CancellationToken>()).Returns(
        [
            ("Watching", (IReadOnlyList<MediaListEntry>)[Finished(21)]),
        ]);

        await BuildMyAnime(client, preferences).LoadAsync();

        Assert.Empty(AiringNotificationState.ReadMediaIds(preferences));
    }

    [Fact]
    public async Task ARepeatedMediaAcrossGroups_IsCachedOnce()
    {
        var preferences = new FakePreferences();
        var client = Substitute.For<IAniListClient>();
        client.GetMyAnimeListGroupedAsync(Arg.Any<CancellationToken>()).Returns(
        [
            ("Watching", (IReadOnlyList<MediaListEntry>)[Releasing(21)]),
            ("Planning", [Releasing(21)]),
        ]);

        await BuildMyAnime(client, preferences).LoadAsync();

        Assert.Equal([21], AiringNotificationState.ReadMediaIds(preferences));
    }

    // ── SettingsPageModel resets the checkpoint when notifications go off ──

    [Fact]
    public async Task TurningNotificationsOff_ResetsTheCheckpoint()
    {
        // So re-enabling notifies only for new episodes rather than replaying everything that aired
        // while they were off.
        var preferences = new FakePreferences();
        AiringNotificationState.AdvanceCheckpoint(preferences, 1_700_000_000);

        var harness = await BuildLoadedSettings(preferences);
        harness.Model.AiringNotifications = false;
        await FlushAsync();

        Assert.False(preferences.ContainsKey(AiringNotificationState.LastCheckKey));
    }

    [Fact]
    public async Task TurningNotificationsOff_LeavesTheOtherAiringKeysAlone()
    {
        // Only the checkpoint resets. The notified set in particular must survive, or re-enabling
        // would re-notify episodes the user has already seen.
        var preferences = new FakePreferences();
        AiringNotificationState.AdvanceCheckpoint(preferences, 1_700_000_000);
        AiringNotificationState.WriteMediaIds(preferences, [21]);
        AiringNotificationState.MarkPromptedForPermission(preferences);
        AiringNotificationState.PruneAndSave(
            preferences, new Dictionary<string, long> { ["21:1"] = 1_700_000_000 }, 1_700_000_000, hasNewEntries: true);

        var harness = await BuildLoadedSettings(preferences);
        harness.Model.AiringNotifications = false;
        await FlushAsync();

        Assert.True(preferences.ContainsKey(AiringNotificationState.MediaIdsKey));
        Assert.True(preferences.ContainsKey(AiringNotificationState.NotifiedKey));
        Assert.True(preferences.ContainsKey(AiringNotificationState.PermissionPromptedKey));
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static MediaListEntry Releasing(int mediaId)
        => Entry(mediaId, "RELEASING");

    private static MediaListEntry Finished(int mediaId)
        => Entry(mediaId, "FINISHED");

    private static MediaListEntry Entry(int mediaId, string status)
    {
        var entry = TestDataBuilder.Entry(mediaId, progress: 0, status: MediaListStatus.Current);
        entry.MediaId = mediaId;
        entry.Media!.Id = mediaId;
        entry.Media.Status = status;
        return entry;
    }

    private static MyAnimePageModel BuildMyAnime(IAniListClient client, IPreferences preferences)
    {
        var auth = Substitute.For<IAuthService>();
        auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");

        return new MyAnimePageModel(
            client,
            auth,
            Substitute.For<IAiringNotificationService>(),
            new ErrorReportService(NullLogger<ErrorReportService>.Instance),
            preferences,
            Substitute.For<INavigationService>(),
            new ScriptedDialogService(),
            new RecordingUserFeedback(),
            new ListEntryStatusFlow(new ScriptedDialogService()),
            new ManualTimeProvider(DateTimeOffset.UnixEpoch),
            NullLogger<MyAnimePageModel>.Instance);
    }

    private sealed record SettingsHarness(SettingsPageModel Model, IAniListClient Client);

    /// <summary>
    /// The toggle handler returns early unless a viewer has loaded, so the model has to be driven
    /// through a real <c>LoadAsync</c> rather than having the property poked directly.
    /// </summary>
    private static async Task<SettingsHarness> BuildLoadedSettings(IPreferences preferences)
    {
        // PopulateFromUser calls AppSettings.SyncFromViewer, which writes through AppSettings.Storage
        // — the real Preferences.Default off-device, which throws. LoadAsync would swallow that and
        // leave _suppressNotificationToggle set, so the toggle below would be silently ignored (#121).
        TestDataBuilder.ResetAppSettings();

        var auth = Substitute.For<IAuthService>();
        auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");

        var client = Substitute.For<IAniListClient>();
        client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(new AniListUser
        {
            Id = 1,
            Name = "tester",
            Options = new UserOptions { AiringNotifications = true },
        });

        // Must grant: a substitute returns false by default, which sends PopulateFromUser down the
        // permission-denied path and flips the toggle off during load — so the test's own
        // "turn it off" would then be a no-op assignment and quietly assert nothing.
        var notifications = Substitute.For<IAiringNotificationService>();
        notifications.RequestPermissionAsync().Returns(true);

        var model = new SettingsPageModel(
            auth,
            client,
            notifications,
            new ErrorReportService(NullLogger<ErrorReportService>.Instance),
            preferences,
            new ImmediateDispatcher(),
            Substitute.For<IAppInfo>(),
            new ScriptedDialogService(),
            new RecordingUserFeedback(),
            Substitute.For<IExternalBrowser>(),
            NullLogger<SettingsPageModel>.Instance);

        await model.LoadAsync();
        await FlushAsync();

        // Guards the setup rather than the subject: if the load left the toggle off, the "turn it
        // off" below would change nothing and the assertions would pass for the wrong reason.
        Assert.True(model.AiringNotifications, "setup: expected notifications enabled after load");

        return new SettingsHarness(model, client);
    }

    /// <summary>The toggle handler is fire-and-forget; let its continuations run.</summary>
    private static async Task FlushAsync()
    {
        for (var i = 0; i < 8; i++)
        {
            await Task.Yield();
        }
    }
}
