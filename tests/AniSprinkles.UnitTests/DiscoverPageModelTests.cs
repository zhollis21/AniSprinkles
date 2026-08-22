using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The Discover paging interlock from PR #116's last round. Unreachable before #62 for the same
/// reason as everything in <see cref="SearchPageModelTests"/>, and unreachable on device because it
/// needs a Load More to land inside a specific suspension point of a refresh.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class DiscoverPageModelTests
{
    public DiscoverPageModelTests() => TestDataBuilder.ResetAppSettings();

    [Fact]
    public async Task LoadMore_WhileARefreshIsStillReadingAuth_IsRefused()
    {
        // #116 round 6. LoadMoreSection's guard covered only the window after IsBusy went up, but
        // LoadCoreAsync awaits the auth read BEFORE setting it. A row near its scroll threshold
        // could start a Load More inside that suspension, fetch under the filter the refresh was
        // about to replace, and append it onto rows still holding the old context. DiscoverSectionFetch
        // reads AppSettings.DisplayAdultContent live per page, so that mixes 18+ items into a row
        // the user had just made SFW — and on a FAILED refresh nothing re-seeds it away.
        var harness = new Harness();

        await harness.Model.LoadAsync();
        var row = harness.Model.Rows.First(r => r.CanLoadMore);
        Assert.False(harness.Model.IsBusy);

        // Second refresh, held at the auth read — before IsBusy goes up.
        var authGate = harness.GateNextAuthRead();
        var refresh = harness.Model.LoadAsync(forceReload: true);
        await authGate.Requested;
        Assert.False(harness.Model.IsBusy);

        var pagesBefore = harness.BrowsePageCalls;
        await harness.Model.LoadMoreSectionCommand.ExecuteAsync(row.SectionKey);
        Assert.Equal(pagesBefore, harness.BrowsePageCalls);

        authGate.Release();
        await refresh;
    }

    [Fact]
    public async Task LoadAsync_WhenTheFetchFailsWithNothingCached_ShowsTheErrorState()
    {
        var harness = new Harness();
        harness.FailSections(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        await harness.Model.LoadAsync();

        Assert.Equal(PageState.Error, harness.Model.CurrentState);
        Assert.Equal("AniList is Down", harness.Model.ErrorTitle);
        Assert.Empty(harness.Feedback.Snackbars);
    }

    [Fact]
    public async Task LoadAsync_WhenARefreshFailsOverCachedRows_KeepsContentAndSnackbars()
    {
        // Stale rows beat a blank page; the snackbar is the only failure signal.
        var harness = new Harness();
        await harness.Model.LoadAsync();
        Assert.Equal(PageState.Content, harness.Model.CurrentState);

        harness.FailSections(new AniListApiException(ApiErrorKind.Network, "offline"));
        await harness.Model.LoadAsync(forceReload: true);

        Assert.Equal(PageState.Content, harness.Model.CurrentState);
        Assert.Equal("No Internet Connection", Assert.Single(harness.Feedback.Snackbars));
        Assert.NotEmpty(harness.Model.Rows.First(r => r.HasItems).Items);
    }

    private sealed class AuthGate
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Requested => _requested.Task;

        public void Release() => _release.TrySetResult();

        public async Task<string> WaitAsync()
        {
            _requested.TrySetResult();
            await _release.Task;
            return "token";
        }
    }

    private sealed class Harness
    {
        private readonly IAniListClient _client = Substitute.For<IAniListClient>();
        private Exception? _sectionsFailure;
        private AuthGate? _pendingAuthGate;
        private int _browsePageCalls;

        public Harness()
        {
            _client
                .GetDiscoverSectionsAsync(
                    Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(),
                    Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(_ => _sectionsFailure is null
                    ? Task.FromResult(SeededSections())
                    : Task.FromException<DiscoverSections>(_sectionsFailure));

            _client
                .BrowseAnimePageAsync(
                    Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
                    Arg.Any<bool?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    Interlocked.Increment(ref _browsePageCalls);
                    return Task.FromResult<(IReadOnlyList<BrowseMediaItem>, PageInfo?)>(
                        ([Item(99)], new PageInfo { HasNextPage = true, CurrentPage = 2 }));
                });

            var auth = Substitute.For<IAuthService>();
            auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            {
                var gate = Interlocked.Exchange(ref _pendingAuthGate, null);
                return gate is null ? Task.FromResult<string?>("token") : gate.WaitAsync()!;
            });

            var dialogs = new ScriptedDialogService();
            Model = new DiscoverPageModel(
                _client,
                auth,
                Substitute.For<INavigationService>(),
                Feedback,
                dialogs,
                new ListEntryStatusFlow(dialogs),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                new ManualTimeProvider(DateTimeOffset.UnixEpoch),
                NullLogger<DiscoverPageModel>.Instance);
        }

        public DiscoverPageModel Model { get; }

        public RecordingUserFeedback Feedback { get; } = new();

        public int BrowsePageCalls => Volatile.Read(ref _browsePageCalls);

        public void FailSections(Exception exception) => _sectionsFailure = exception;

        public AuthGate GateNextAuthRead()
        {
            var gate = new AuthGate();
            Interlocked.Exchange(ref _pendingAuthGate, gate);
            return gate;
        }

        private static BrowseMediaItem Item(int id) => new() { Node = new RelatedMedia { Id = id } };

        private static DiscoverSectionPage Page(int id)
            => new([Item(id)], new PageInfo { HasNextPage = true, CurrentPage = 1 });

        private static DiscoverSections SeededSections() => new()
        {
            Airing = Page(1),
            Trending = Page(2),
            Top = Page(3),
            TopMovies = Page(4),
            AllTimePopular = Page(5),
            Upcoming = Page(6),
            PopularAdult = Page(7),
            TopRatedAdult = Page(8),
        };
    }
}
