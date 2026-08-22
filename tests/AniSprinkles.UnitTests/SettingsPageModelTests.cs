using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 Phase 1 for <see cref="SettingsPageModel"/>: the branches of <c>LoadAsync</c> that decide
/// between the content, unauthenticated and full-page error states.
/// <para>
/// The authenticated happy path is deliberately absent. <c>PopulateFromUser</c> ends in
/// <c>AppSettings.SyncFromViewer</c>, which persists through the static <c>Preferences.Default</c>;
/// that throws <c>NotImplementedInReferenceAssemblyException</c> off-device, so a "successful load"
/// test would actually be asserting on the catch block. Giving <c>AppSettings</c> a preferences seam
/// is the open decision in #52's own comment thread, not something to smuggle in here.
/// </para>
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class SettingsPageModelTests
{
    public SettingsPageModelTests() => TestDataBuilder.ResetAppSettings();

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
