using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// Two halves of the same race, found while fixing Library's share of #118.
/// <para>
/// A settings change lives only in a 1500 ms debounce until it is sent. Navigating away inside that
/// window meant the change was still unsent — and if the app was killed there, lost outright. Worse,
/// <c>MyAnimePageModel.LoadAsync</c> syncs display preferences from the viewer, so a Library refresh
/// inside the window read the server's stale copy and reverted the toggle the user had just flipped.
/// </para>
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class SettingsFlushTests
{
    public SettingsFlushTests() => TestDataBuilder.ResetAppSettings();

    [Fact]
    public async Task FlushPendingSave_SendsAChangeStillSittingInTheDebounce()
    {
        var harness = new Harness();
        await harness.LoadAsync(displayAdultContent: true);
        harness.Model.DisplayAdultContent = false;

        // Standing in for SettingsPage.OnDisappearing — the user tabbed away well inside the 1500 ms.
        await harness.Model.FlushPendingSaveAsync();

        await harness.Client.Received(1).UpdateUserAsync(
            Arg.Is<UpdateUserRequest>(r => r.DisplayAdultContent == false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlushPendingSave_WithNothingPending_SendsNothing()
    {
        // OnDisappearing fires on every tab away, most of which changed nothing. Flushing must not
        // turn tab switching into an AniList write.
        var harness = new Harness();
        await harness.LoadAsync(displayAdultContent: true);

        await harness.Model.FlushPendingSaveAsync();

        await harness.Client.DidNotReceive().UpdateUserAsync(
            Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlushPendingSave_AfterAPlainLoad_SendsNothingEvenWhenTheViewerHasNoNotificationOptions()
    {
        // Guards the CI screenshot job. It opens Settings and then tabs to Discover, which now runs
        // the flush — and CIAniListClient's viewer has AiringNotifications on with an EMPTY
        // NotificationOptions list. If PopulateFromUser left the model looking dirty against that
        // shape, every CI run would fire an UpdateUser and raise a "Settings saved" toast that could
        // land in the next screenshot. Nothing was touched here, so nothing may be sent.
        var harness = new Harness();
        await harness.LoadCiShapedViewerAsync();

        await harness.Model.FlushPendingSaveAsync();

        Assert.False(harness.Model.HasUnsavedChanges);
        await harness.Client.DidNotReceive().UpdateUserAsync(
            Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlushPendingSave_AfterAPermissionDenialRevertedTheToggle_StillSendsTheRevert()
    {
        // The other side of the branch above. When notification permission is denied, the model
        // silently reverts AiringNotifications and explicitly calls TriggerAutoSave — the comment on
        // that path notes that without the save the reverted value never reaches AniList and the
        // next profile load re-enables the toggle. So here the flush must send, not stay quiet.
        var harness = new Harness();
        harness.Notifications.RequestPermissionAsync().Returns(false);
        await harness.LoadCiShapedViewerAsync();

        await harness.Model.FlushPendingSaveAsync();

        await harness.Client.Received(1).UpdateUserAsync(
            Arg.Is<UpdateUserRequest>(r => r.AiringNotifications == false), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FlushPendingSave_CancelsTheDebounceSoTheChangeIsNotSentTwice()
    {
        var harness = new Harness();
        await harness.LoadAsync(displayAdultContent: true);
        harness.Model.DisplayAdultContent = false;

        await harness.Model.FlushPendingSaveAsync();
        await harness.Model.FlushPendingSaveAsync();

        await harness.Client.Received(1).UpdateUserAsync(
            Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        public Harness()
        {
            var dialogs = new ScriptedDialogService();

            // Mirrors CIAiringNotificationService, and the normal on-device case: permission granted.
            // A substitute left at its default returns false, which silently exercises the DENIED
            // path instead — that reverts the airing toggle and deliberately queues a save, so a
            // test meaning to assert "nothing pending" would be asserting against the wrong branch.
            Notifications.RequestPermissionAsync().Returns(true);

            Model = new SettingsPageModel(
                Auth,
                Client,
                Notifications,
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                Substitute.For<IPreferences>(),
                new ImmediateDispatcher(),
                Substitute.For<IAppInfo>(),
                dialogs,
                new RecordingUserFeedback(),
                Substitute.For<IExternalBrowser>(),
                NullLogger<SettingsPageModel>.Instance);
        }

        public IAuthService Auth { get; } = Substitute.For<IAuthService>();

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public IAiringNotificationService Notifications { get; } = Substitute.For<IAiringNotificationService>();

        public SettingsPageModel Model { get; }

        /// <summary>Mirrors <c>CIAniListClient.StubData.Viewer</c>: airing notifications on, and no
        /// per-type notification options at all.</summary>
        public async Task LoadCiShapedViewerAsync()
        {
            Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");
            Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(new AniListUser
            {
                Id = 999999,
                Name = "CIUser",
                ScoreFormat = ScoreFormat.Point10Decimal,
                AnimeSectionOrder = ["Watching", "Planning", "Completed"],
                Options = new UserOptions
                {
                    TitleLanguage = UserTitleLanguage.Romaji,
                    AiringNotifications = true,
                    ProfileColor = "blue",
                    NotificationOptions = [],
                },
            });

            await Model.LoadAsync();
        }

        public async Task LoadAsync(bool displayAdultContent)
        {
            Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");
            Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(Viewer(displayAdultContent));
            Client.UpdateUserAsync(Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(Viewer(call.Arg<UpdateUserRequest>().DisplayAdultContent ?? false)));

            await Model.LoadAsync();
        }

        private static AniListUser Viewer(bool displayAdultContent) => new()
        {
            Id = 1,
            Name = "zhollis",
            Options = new UserOptions { DisplayAdultContent = displayAdultContent },
        };
    }
}

/// <summary>
/// The flush narrows the window; it cannot close it. MAUI Shell does not guarantee the outgoing
/// page's OnDisappearing runs before the incoming page's OnAppearing, and a save can simply fail —
/// in both cases the server still holds the old value when another surface syncs from it. The
/// pending flag encodes the invariant that actually matters: a locally committed change outranks the
/// server's copy until the server confirms it.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class AppSettingsPendingAdultContentTests
{
    public AppSettingsPendingAdultContentTests() => TestDataBuilder.ResetAppSettings();

    [Fact]
    public void SyncFromViewer_DoesNotRevertALocalChangeTheServerHasNotSeenYet()
    {
        AppSettings.SetDisplayAdultContent(false);

        // A Library refresh landing inside the debounce window: the server still reports the old on.
        AppSettings.SyncFromViewer(Viewer(displayAdultContent: true, titleLanguage: UserTitleLanguage.English));

        Assert.False(AppSettings.DisplayAdultContent);
    }

    [Fact]
    public void SyncFromViewer_StillAppliesEveryOtherPreference()
    {
        // The guard is one field wide. A cross-device title-language change must still land.
        AppSettings.SetDisplayAdultContent(false);

        AppSettings.SyncFromViewer(Viewer(displayAdultContent: true, titleLanguage: UserTitleLanguage.English));

        Assert.Equal(UserTitleLanguage.English, AppSettings.TitleLanguage);
    }

    [Fact]
    public void SyncFromViewer_ResumesFollowingTheServerOnceTheChangeIsConfirmed()
    {
        AppSettings.SetDisplayAdultContent(false);
        AppSettings.ConfirmDisplayAdultContentSaved();

        // A genuine cross-device change — made on the AniList website — must now be honoured.
        AppSettings.SyncFromViewer(Viewer(displayAdultContent: true, titleLanguage: UserTitleLanguage.Romaji));

        Assert.True(AppSettings.DisplayAdultContent);
    }

    [Fact]
    public void SyncFromViewer_WithNoLocalChangePending_FollowsTheServer()
    {
        AppSettings.SyncFromViewer(Viewer(displayAdultContent: true, titleLanguage: UserTitleLanguage.Romaji));

        Assert.True(AppSettings.DisplayAdultContent);
    }

    [Fact]
    public void Clear_DropsThePendingMarkerSoTheNextAccountStartsClean()
    {
        // Sign-out must not leave the previous user's unconfirmed change shadowing the next viewer.
        AppSettings.SetDisplayAdultContent(false);
        AppSettings.Clear();

        AppSettings.SyncFromViewer(Viewer(displayAdultContent: true, titleLanguage: UserTitleLanguage.Romaji));

        Assert.True(AppSettings.DisplayAdultContent);
    }

    private static AniListUser Viewer(bool displayAdultContent, UserTitleLanguage titleLanguage) => new()
    {
        Id = 1,
        Name = "zhollis",
        Options = new UserOptions
        {
            DisplayAdultContent = displayAdultContent,
            TitleLanguage = titleLanguage,
        },
    };
}
