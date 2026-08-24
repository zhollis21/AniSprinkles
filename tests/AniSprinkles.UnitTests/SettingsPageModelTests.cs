using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 Phase 1 for <see cref="SettingsPageModel"/>: the branches of <c>LoadAsync</c> that decide
/// between the content, unauthenticated and full-page error states.
/// <para>
/// The authenticated happy path used to be unreachable here — <c>PopulateFromUser</c> ends in
/// <c>AppSettings.SyncFromViewer</c>, which persisted through the static <c>Preferences.Default</c>
/// and threw off-device, so a "successful load" test would have been asserting on the catch block.
/// #121 put a seam on that storage, and <c>TestDataBuilder.ResetAppSettings</c> installs a fake, so
/// the load path now runs end to end.
/// </para>
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class SettingsPageModelTests
{
    private readonly FakePreferences _appSettingsStorage;

    public SettingsPageModelTests() => _appSettingsStorage = TestDataBuilder.ResetAppSettings();

    [Fact]
    public async Task LoadAsync_WhenSignedOut_ShowsTheUnauthenticatedStateWithoutCallingTheApi()
    {
        var harness = new Harness();
        harness.SignedOut();

        await harness.Model.LoadAsync();

        Assert.Equal(PageState.Unauthenticated, harness.Model.CurrentState);
        Assert.False(harness.Model.IsAuthenticated);
        await harness.Client.DidNotReceive().GetViewerAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_WhenTheTokenReadThrows_FallsBackToUnauthenticatedRatherThanTheErrorPage()
    {
        // A SecureStorage failure must leave the user somewhere they can retry sign-in from, not on
        // a full-page error with no login card.
        var harness = new Harness();
        harness.AuthThrows(new InvalidOperationException("keystore unavailable"));

        await harness.Model.LoadAsync();

        Assert.Equal(PageState.Unauthenticated, harness.Model.CurrentState);
        Assert.Equal("Failed to load profile.", Assert.Single(harness.Feedback.Snackbars));
    }

    [Fact]
    public async Task LoadAsync_WhenTheViewerFetchFailsWithNothingCached_ShowsTheFullPageError()
    {
        var harness = new Harness();
        harness.SignedIn();
        harness.Client.GetViewerAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AniListUser>(new AniListApiException(ApiErrorKind.ServiceOutage, "down")));

        await harness.Model.LoadAsync();

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.Equal("AniList is Down", harness.Model.ErrorTitle);
        Assert.NotEmpty(harness.Model.ErrorDetails);
    }

    [Fact]
    public async Task LoadAsync_WhileAlreadyInFlight_IsSkipped()
    {
        // OnAppearing and pull-to-refresh can both fire, as can rapid Retry taps. IsBusy is set
        // before the first await precisely so the second caller short-circuits.
        var harness = new Harness();
        var gate = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(_ => gate.Task);

        var first = harness.Model.LoadAsync();
        await harness.Model.LoadAsync();

        gate.SetResult(null);
        await first;

        await harness.Auth.Received(1).GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_WhenAuthenticated_PopulatesTheProfileAndShowsContent()
    {
        // Unreachable before #121: PopulateFromUser ends in AppSettings.SyncFromViewer, so this
        // test would have been asserting on the catch block rather than the happy path.
        var harness = new Harness();
        harness.SignedIn();
        harness.Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(Viewer());

        await harness.Model.LoadAsync();

        Assert.Equal(PageState.Content, harness.Model.CurrentState);
        Assert.True(harness.Model.IsAuthenticated);
        Assert.Equal("zhollis", harness.Model.UserName);
        Assert.Equal("412", harness.Model.TotalAnime);
        Assert.Equal("8.4", harness.Model.MeanScore);
        Assert.Empty(harness.Model.ErrorDetails);

        // The display preferences the rest of the app reads off the statics.
        Assert.Equal(UserTitleLanguage.English, AppSettings.TitleLanguage);
        Assert.Equal(ScoreFormat.Point10Decimal, AppSettings.ScoreFormat);
        Assert.False(AppSettings.DisplayAdultContent);
    }

    [Fact]
    public async Task LoadAsync_WhenAuthenticated_PersistsTheViewerPreferences()
    {
        // SyncFromViewer ends in Save(). Without that write the settings survive only until the
        // process dies, so the next cold start silently reverts to Romaji/Point100.
        var harness = new Harness();
        harness.SignedIn();
        harness.Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(Viewer());

        await harness.Model.LoadAsync();

        Assert.Equal("English", _appSettingsStorage.Get("title_language", string.Empty));
        Assert.Equal("Point10Decimal", _appSettingsStorage.Get("score_format", string.Empty));
        Assert.False(_appSettingsStorage.Get("display_adult_content", true));
    }

    [Fact]
    public async Task LoadAsync_WhenARefreshFailsAfterASuccessfulLoad_KeepsShowingTheCachedProfile()
    {
        // The #52 case that needed the happy path to be reachable first: a pull-to-refresh that
        // fails must leave the profile on screen with a snackbar, not blank the page to an error.
        var harness = new Harness();
        harness.SignedIn();
        harness.Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(Viewer());
        await harness.Model.LoadAsync();

        harness.Client.GetViewerAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromException<AniListUser>(new AniListApiException(ApiErrorKind.Network, "offline")));
        await harness.Model.LoadAsync();

        Assert.Equal(PageState.Content, harness.Model.CurrentState);
        Assert.Equal("zhollis", harness.Model.UserName);
        Assert.Equal("No Internet Connection", Assert.Single(harness.Feedback.Snackbars));
    }

    private static AniListUser Viewer() => new()
    {
        Id = 1,
        Name = "zhollis",
        ScoreFormat = ScoreFormat.Point10Decimal,
        AnimeSectionOrder = ["Watching", "Completed"],
        Options = new UserOptions
        {
            TitleLanguage = UserTitleLanguage.English,
            DisplayAdultContent = false,
            ActivityMergeTime = 30,
        },
        AnimeStatistics = new UserAnimeStatistics
        {
            Count = 412,
            MeanScore = 8.4,
            EpisodesWatched = 6031,
            MinutesWatched = 144_000,
        },
    };

    private sealed class Harness
    {
        public Harness()
        {
            var dialogs = new ScriptedDialogService();
            Model = new SettingsPageModel(
                Auth,
                Client,
                Substitute.For<IAiringNotificationService>(),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                Substitute.For<IPreferences>(),
                new ImmediateDispatcher(),
                Substitute.For<IAppInfo>(),
                dialogs,
                Feedback,
                Substitute.For<IExternalBrowser>(),
                NullLogger<SettingsPageModel>.Instance);
        }

        public IAuthService Auth { get; } = Substitute.For<IAuthService>();

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public RecordingUserFeedback Feedback { get; } = new();

        public SettingsPageModel Model { get; }

        public void SignedIn()
            => Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");

        public void SignedOut()
            => Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns((string?)null);

        public void AuthThrows(Exception exception)
            => Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromException<string?>(exception));
    }
}
