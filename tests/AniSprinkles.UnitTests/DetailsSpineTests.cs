using AniSprinkles.Icons;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The shared spine of the four details page models, tested once. Each concrete subclass below
/// supplies only what its page does differently — how its entity is built, which client method
/// fetches it, and how its public load entry point is called.
///
/// <para>Before #120 this was three near-identical harnesses' worth of tests that nobody wrote, which
/// is precisely why the extraction came first.</para>
/// </summary>
public abstract class DetailsSpineTests<TEntity>
    where TEntity : class, IFavouritable
{
    // ---- What each page supplies ------------------------------------------------------------------

    protected abstract Harness CreateHarness();

    /// <summary>Builds a stub entity. <paramref name="siteUrl"/> null means "this one has no link".</summary>
    protected abstract TEntity NewEntity(int id, string? siteUrl = "https://anilist.co/x/1", int? favourites = null);

    /// <summary>Stubs the page's fetch to return <paramref name="entity"/> (null takes the not-found path).</summary>
    protected abstract void Returns(Harness harness, TEntity? entity);

    /// <summary>Stubs the page's fetch to fail.</summary>
    protected abstract void Throws(Harness harness, Exception exception);

    /// <summary>Stubs the page's fetch to hand back the cancellation token it was given.</summary>
    protected abstract void CapturesToken(Harness harness, Action<CancellationToken> capture);

    /// <summary>Stubs the page's fetch to block until <paramref name="gate"/> completes, observing
    /// cancellation while it waits.</summary>
    protected abstract void ReturnsWhenSignalled(Harness harness, Task gate);

    /// <summary>Invokes the page's public load entry point.</summary>
    protected abstract Task LoadAsync(Harness harness, int id);

    /// <summary>How many times the page's fetch was called.</summary>
    protected abstract int FetchCount(Harness harness);

    /// <summary>Whether the page currently holds an entity — each page names this differently.</summary>
    protected abstract bool HasEntity(Harness harness);

    /// <summary>The context string this page passes to <c>ErrorReportService.Record</c>.</summary>
    protected abstract string ErrorContext { get; }

    /// <summary>MediaDetails treats an empty result as retryable; the other three treat it as final.</summary>
    protected virtual bool NullResultIsRetryable => false;

    /// <summary>Whether a second load supersedes an in-flight one (the three reference-data pages) or
    /// is dropped at an in-flight guard (MediaDetails).</summary>
    protected virtual bool SupersedesConcurrentLoads => true;

    // ---- Load -------------------------------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_HappyPath_ShowsContent()
    {
        var harness = CreateHarness();
        Returns(harness, NewEntity(42));

        await LoadAsync(harness, 42);

        Assert.Equal(PageState.Content, harness.Model.CurrentState);
        Assert.True(HasEntity(harness));
        Assert.False(harness.Model.IsBusy);
    }

    [Fact]
    public async Task LoadAsync_WithANonPositiveId_ErrorsWithoutRetryAndWithoutCallingTheApi()
    {
        var harness = CreateHarness();
        Returns(harness, NewEntity(42));

        await LoadAsync(harness, 0);

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.False(harness.Model.CanRetry);
        Assert.Equal(0, FetchCount(harness));
    }

    [Fact]
    public async Task LoadAsync_WhenTheFetchReturnsNothing_ErrorsWithTheConfiguredRetryability()
    {
        var harness = CreateHarness();
        Returns(harness, null);

        await LoadAsync(harness, 42);

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.Equal(NullResultIsRetryable, harness.Model.CanRetry);
        Assert.False(HasEntity(harness));
    }

    [Fact]
    public async Task LoadAsync_WhenTheEntityIsNotFound_HidesRetryAndKeepsErrorDetailsEmpty()
    {
        var harness = CreateHarness();
        Throws(harness, new AniListApiException(ApiErrorKind.NotFound, "gone"));

        await LoadAsync(harness, 42);

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.False(harness.Model.CanRetry);
        // NotFound is deliberately kept out of Sentry, so there is no report id to show.
        Assert.Equal(string.Empty, harness.Model.ErrorDetails);
    }

    [Fact]
    public async Task LoadAsync_WhenTheFetchThrows_KeepsRetryAndRecordsAReport()
    {
        var harness = CreateHarness();
        Throws(harness, new InvalidOperationException("boom"));

        await LoadAsync(harness, 42);

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.True(harness.Model.CanRetry);
        // #120 unified this: all four details pages now surface an ErrorReportService report rather
        // than three of them showing a bare exception message.
        Assert.Contains(ErrorContext, harness.Model.ErrorDetails, StringComparison.Ordinal);
        Assert.Contains("boom", harness.Model.ErrorDetails, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_WhenCancelled_LeavesTheLoadingStateAloneRatherThanErroring()
    {
        var harness = CreateHarness();
        Throws(harness, new OperationCanceledException());

        await LoadAsync(harness, 42);

        // Navigating away mid-load is not a failure: no error UI, and OnAppearing reloads on return.
        Assert.Equal(PageState.InitialLoading, harness.Model.CurrentState);
        Assert.False(harness.Model.IsBusy);
    }

    [Fact]
    public async Task LoadAsync_ForTheSameEntity_ReusesItWithoutASecondRequest()
    {
        var harness = CreateHarness();
        Returns(harness, NewEntity(42));

        await LoadAsync(harness, 42);
        await LoadAsync(harness, 42);

        // The sort popup's OnAppearing re-entry depends on this: a second fetch would reset the sort
        // the user just picked.
        Assert.Equal(1, FetchCount(harness));
        Assert.Equal(PageState.Content, harness.Model.CurrentState);
    }

    [Fact]
    public async Task LoadAsync_AfterAFailedLoad_DoesNotReuseTheEntityItWasDisplayingBefore()
    {
        var harness = CreateHarness();
        Returns(harness, NewEntity(1));
        await LoadAsync(harness, 1);

        Throws(harness, new InvalidOperationException("boom"));
        await LoadAsync(harness, 2);

        // Before #120 the failed load left entity #1 in place with its sections already reset, so
        // navigating back to #1 hit the same-id guard and showed Content over empty sections.
        Assert.False(HasEntity(harness));

        Returns(harness, NewEntity(1));
        await LoadAsync(harness, 1);

        Assert.Equal(PageState.Content, harness.Model.CurrentState);
        Assert.True(HasEntity(harness));
    }

    [Fact]
    public async Task RetryLoad_ReInvokesWithTheLastRequestedId()
    {
        var harness = CreateHarness();
        Throws(harness, new InvalidOperationException("boom"));
        await LoadAsync(harness, 42);

        Returns(harness, NewEntity(42));
        await harness.Model.RetryLoadCommand.ExecuteAsync(null);

        Assert.Equal(PageState.Content, harness.Model.CurrentState);
        Assert.True(HasEntity(harness));
    }

    [Fact]
    public async Task LoadAsync_WhileAnotherLoadIsStillInFlight_LeavesIsBusySetUntilTheLiveOneFinishes()
    {
        var harness = CreateHarness();
        var gate = new TaskCompletionSource();
        ReturnsWhenSignalled(harness, gate.Task);

        var first = LoadAsync(harness, 1);
        var second = LoadAsync(harness, 2);

        // Which load survives differs by page, but the invariant does not: while one is still in
        // flight, IsBusy must stay set. The loser finishes first in both shapes — a superseded load
        // returns as soon as its token is cancelled, and a dropped one returns at the guard.
        var (loser, winner) = SupersedesConcurrentLoads ? (first, second) : (second, first);
        await loser;

        Assert.True(harness.Model.IsBusy);

        gate.SetResult();
        await winner;

        Assert.False(harness.Model.IsBusy);
    }

    [Fact]
    public async Task CancelInFlight_CancelsTheTokenTheFetchWasGiven()
    {
        var harness = CreateHarness();
        CancellationToken captured = default;
        CapturesToken(harness, token => captured = token);

        await LoadAsync(harness, 42);
        Assert.False(captured.IsCancellationRequested);

        harness.Model.CancelInFlight();

        Assert.True(captured.IsCancellationRequested);
    }

    // ---- Error state ------------------------------------------------------------------------------

    [Fact]
    public void ShowError_PopulatesTheErrorStateForThePageToRender()
    {
        var harness = CreateHarness();

        harness.Model.ShowError("Title", "Subtitle", canRetry: false, details: "why", iconGlyph: "glyph");

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.Equal("Title", harness.Model.ErrorTitle);
        Assert.Equal("Subtitle", harness.Model.ErrorSubtitle);
        Assert.Equal("why", harness.Model.ErrorDetails);
        Assert.Equal("glyph", harness.Model.ErrorIconGlyph);
        Assert.False(harness.Model.CanRetry);
    }

    [Fact]
    public void ShowError_WithoutAGlyph_FallsBackToTheGenericErrorIcon()
    {
        var harness = CreateHarness();

        harness.Model.ShowError("Title", "Subtitle", canRetry: true);

        Assert.Equal(Glyphs.Regular.ErrorCircle24, harness.Model.ErrorIconGlyph);
    }

    // ---- Favourites -------------------------------------------------------------------------------

    [Fact]
    public async Task ToggleFavourite_WhenSignedOut_IsNotOffered()
    {
        var harness = CreateHarness();
        harness.SignedOut();
        Returns(harness, NewEntity(42));

        await LoadAsync(harness, 42);

        Assert.False(harness.Model.CanToggleFavourite);
    }

    [Fact]
    public async Task ToggleFavourite_WhenItSucceeds_FlipsTheHeartAndBumpsTheCount()
    {
        var harness = CreateHarness();
        harness.SignedIn();
        Returns(harness, NewEntity(42, favourites: 10));
        harness.Client.ToggleFavouriteAsync(Arg.Any<FavouriteKind>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));

        await LoadAsync(harness, 42);
        Assert.True(harness.Model.CanToggleFavourite);

        await harness.Model.ToggleFavouriteCommand.ExecuteAsync(null);

        Assert.True(harness.Model.IsFavourite);
        Assert.Equal("11", harness.Model.FavouritesDisplay);
        Assert.Empty(harness.Feedback.Snackbars);
    }

    [Fact]
    public async Task ToggleFavourite_WhenItFails_RollsBackAndOffersRetry()
    {
        var harness = CreateHarness();
        harness.SignedIn();
        Returns(harness, NewEntity(42, favourites: 10));
        harness.Client.ToggleFavouriteAsync(Arg.Any<FavouriteKind>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<bool>(new InvalidOperationException("nope")));

        await LoadAsync(harness, 42);
        await harness.Model.ToggleFavouriteCommand.ExecuteAsync(null);

        Assert.False(harness.Model.IsFavourite);
        Assert.Equal("10", harness.Model.FavouritesDisplay);
        Assert.Single(harness.Feedback.Snackbars);
        Assert.NotNull(harness.Feedback.LastSnackbarAction);
    }

    [Fact]
    public async Task FavouritesDisplay_UsesTheCompactFormatIncludingTheMillionsTier()
    {
        var harness = CreateHarness();
        Returns(harness, NewEntity(42, favourites: 1_200_000));

        await LoadAsync(harness, 42);

        // #120 folded three separate favourites formatters into MetricFormat.Compact. Two of them had
        // no M tier, so this used to render "1200k".
        Assert.Equal("1.2M", harness.Model.FavouritesDisplay);
    }

    [Fact]
    public async Task FavouritesDisplay_WithNoFavourites_IsBlank()
    {
        var harness = CreateHarness();
        Returns(harness, NewEntity(42, favourites: null));

        await LoadAsync(harness, 42);

        Assert.Equal(string.Empty, harness.Model.FavouritesDisplay);
        Assert.False(harness.Model.HasFavourites);
    }

    // ---- External link ----------------------------------------------------------------------------

    [Fact]
    public async Task OpenSiteUrl_OpensTheEntityUrl()
    {
        var harness = CreateHarness();
        Returns(harness, NewEntity(42, siteUrl: "https://anilist.co/thing/42"));

        await LoadAsync(harness, 42);
        Assert.True(harness.Model.HasSiteUrl);

        await harness.Model.OpenSiteUrlCommand.ExecuteAsync(null);

        Assert.Equal(new Uri("https://anilist.co/thing/42"), harness.Browser.LastOpened);
    }

    [Fact]
    public async Task OpenSiteUrl_WithNoUrl_DoesNothing()
    {
        var harness = CreateHarness();
        Returns(harness, NewEntity(42, siteUrl: null));

        await LoadAsync(harness, 42);
        Assert.False(harness.Model.HasSiteUrl);

        await harness.Model.OpenSiteUrlCommand.ExecuteAsync(null);

        Assert.Empty(harness.Browser.Opened);
    }

    // ---- Navigate to media ------------------------------------------------------------------------

    [Fact]
    public async Task NavigateToMedia_ForAnAnimeEntry_NavigatesWithItsId()
    {
        var harness = CreateHarness();

        await harness.Model.NavigateToMediaCommand.ExecuteAsync(new RelatedMedia { Id = 7, Type = "ANIME" });

        await harness.Navigation.Received(1).GoToAsync(
            "media-details",
            false,
            Arg.Is<IDictionary<string, object>>(d => (int)d["mediaId"] == 7));
    }

    [Fact]
    public async Task NavigateToMedia_ForAMangaEntry_ToastsInsteadOfNavigating()
    {
        var harness = CreateHarness();

        await harness.Model.NavigateToMediaCommand.ExecuteAsync(new RelatedMedia { Id = 7, Type = "MANGA" });

        // The details screen queries Media(type: ANIME), so a manga id would 404.
        Assert.Single(harness.Feedback.Toasts);
        await harness.Navigation.DidNotReceive().GoToAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>());
    }

    [Fact]
    public async Task NavigateToMedia_WithNoEntry_DoesNothing()
    {
        var harness = CreateHarness();

        await harness.Model.NavigateToMediaCommand.ExecuteAsync(null);

        Assert.Empty(harness.Feedback.Toasts);
        await harness.Navigation.DidNotReceive().GoToAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>());
    }

    // ---- Harness ----------------------------------------------------------------------------------

    protected sealed class Harness
    {
        /// <param name="factory">Builds the page model from this harness's doubles. Auto-property
        /// initializers have already run by the time it is called, so they are all available.</param>
        public Harness(Func<Harness, DetailsPageModelBase<TEntity>> factory) => Model = factory(this);

        public DetailsPageModelBase<TEntity> Model { get; }

        /// <summary>Counted by the fetch stubs so tests can assert "no second request".</summary>
        public int Fetches { get; set; }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public IAuthService Auth { get; } = Substitute.For<IAuthService>();

        public INavigationService Navigation { get; } = Substitute.For<INavigationService>();

        public RecordingUserFeedback Feedback { get; } = new();

        public RecordingExternalBrowser Browser { get; } = new();

        public ErrorReportService ErrorReports { get; } = new(NullLogger<ErrorReportService>.Instance);

        public void SignedIn()
            => Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("token"));

        public void SignedOut()
            => Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>(null));
    }
}
