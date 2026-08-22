using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// State-machine tests for <see cref="SearchPageModel"/>: debounce, context invalidation, paging
/// interlocks and adult-filter pinning.
///
/// These are the defects PR #116 found across six review rounds — three of them regressions
/// introduced by the previous round's fix. None were reachable before #62 moved the page models
/// into a plain <c>net10.0</c> library, and none are reachable on device either: each needs a
/// specific interleaving (a fetch failing at a precise moment, a superseded continuation resuming
/// after a newer one, a debounced settings write landing between two reads).
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class SearchPageModelTests
{
    private static readonly TimeSpan PastTheDebounce = TimeSpan.FromMilliseconds(700);

    public SearchPageModelTests() => TestDataBuilder.ResetAppSettings();

    [Fact]
    public async Task LoadMore_AfterAPageOneFailureOnANewQuery_DoesNotPageOntoThePreviousQuerysResults()
    {
        // #116 round 2. Load More was gated only on the in-flight window. A failed page 1 clears
        // IsSearching in its finally (that is what stops the spinner), which re-armed Load More
        // while the section still held the PREVIOUS query's items and HasNextPage — so scrolling
        // appended page 2 of the new query onto the old query's results, one list silently mixing
        // two searches.
        var harness = new Harness();
        harness.RespondToQuery("naruto", Page(mediaId: 1, hasNextPage: true));
        harness.FailQuery("bleach", new InvalidOperationException("page 1 exploded"));

        await harness.SearchAsync("naruto");
        Assert.Single(harness.Model.SearchSection.Items);
        Assert.True(harness.Model.SearchSection.CanLoadMore);

        await harness.SearchAsync("bleach");

        Assert.Empty(harness.Model.SearchSection.Items);
        Assert.False(harness.Model.SearchSection.CanLoadMore);

        var callsBefore = harness.Calls.Count;
        await harness.Model.LoadMoreSearchResultsCommand.ExecuteAsync(null);
        Assert.Equal(callsBefore, harness.Calls.Count);
    }

    [Fact]
    public async Task LoadMore_AfterTheAdultSettingCommits_PagesUnderTheFilterPageOneUsed()
    {
        // #116 round 5. The adult filter was read live, per page. Settings debounces 1500 ms and
        // only applies the value from the server response, so a user could toggle, return to Search
        // inside that window (nothing detected as changed), and have the new value land in time for
        // Load More to page a different policy onto results already on screen. Turning adult
        // content OFF was the bad direction: 18+ page-1 items left visible above SFW pages.
        AppSettings.DisplayAdultContent = false;

        var harness = new Harness();
        harness.RespondToQuery("one piece", Page(mediaId: 1, hasNextPage: true));

        await harness.SearchAsync("one piece");
        Assert.Equal(false, harness.Calls[0].Adult);

        // The debounced Settings commit lands while the results are on screen.
        AppSettings.DisplayAdultContent = true;

        await harness.Model.LoadMoreSearchResultsCommand.ExecuteAsync(null);

        Assert.Equal(2, harness.Calls.Count);
        Assert.Equal(false, harness.Calls[1].Adult);
    }

    [Fact]
    public async Task OnAppearing_WhileTheFirstSearchIsStillInFlight_TreatsAnAdultFlipAsAnInvalidation()
    {
        // #116 rounds 1 and 3. The search context was recorded on fetch SUCCESS rather than at issue
        // time, so a first search still in flight left "nothing searched yet" set. A user who tabbed
        // away, flipped the adult toggle and came back hit the "nothing to invalidate" branch, which
        // baselined the NEW context WITHOUT cancelling the old request — whose response then landed
        // and marked itself current. No later appearance would ever invalidate it.
        AppSettings.DisplayAdultContent = false;

        var harness = new Harness();
        var inFlight = harness.GateQuery("naruto");

        await harness.SearchAsync("naruto", waitForFetch: false);
        await inFlight.Requested;
        Assert.Single(harness.Calls);

        // Tab away, flip the toggle, come back.
        AppSettings.DisplayAdultContent = true;
        harness.RespondToQuery("naruto", Page(mediaId: 1, hasNextPage: false));
        await harness.Model.OnAppearingAsync();

        // The re-run is issued through the same debounce path as a keystroke.
        await harness.AdvancePastDebounceAsync();
        await harness.WaitUntilAsync(() => harness.Calls.Count == 2);

        // The second request must carry the new policy; the first carried the old one.
        Assert.Equal(false, harness.Calls[0].Adult);
        Assert.Null(harness.Calls[1].Adult);
    }

    [Fact]
    public async Task SupersededSearch_ResumingAfterANewerOne_DoesNotClearTheNewerSearchsSpinner()
    {
        // #116 round 3's neighbour: a cancelled search can reach its finally BEFORE its successor's
        // debounce sets _activeSearchQuery. Without the token check there it would clear the spinner
        // the successor's keystroke had just turned on, leaving a live search looking settled.
        var harness = new Harness();
        var first = harness.GateQuery("aa");
        harness.RespondToQuery("bb", Page(mediaId: 2, hasNextPage: false));

        await harness.SearchAsync("aa", waitForFetch: false);
        await first.Requested;

        // A new keystroke supersedes it, then the old fetch finally observes its cancellation.
        harness.Model.SearchText = "bb";
        Assert.True(harness.Model.IsSearching);
        first.Release();

        await harness.WaitUntilAsync(() => first.Completed);
        Assert.True(harness.Model.IsSearching);
    }

    [Fact]
    public async Task SearchText_BelowTheMinimumLength_IssuesNoRequest()
    {
        // The rate-limit guard: AniList reads are expensive, so a single character never queries.
        var harness = new Harness();

        harness.Model.SearchText = "n";
        await harness.AdvancePastDebounceAsync();

        Assert.Empty(harness.Calls);
        Assert.True(harness.Model.IsIdle);
    }

    [Fact]
    public async Task SearchFailure_KeepsTheUserInformedAndRecordsTheError()
    {
        var harness = new Harness();
        harness.FailQuery("naruto", new AniListApiException(ApiErrorKind.Network, "offline"));

        await harness.SearchAsync("naruto");

        Assert.Equal("No Internet Connection", Assert.Single(harness.Feedback.Snackbars));
        Assert.False(harness.Model.IsSearching);
    }

    private static (IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo) Page(int mediaId, bool hasNextPage)
        => ([new BrowseMediaItem { Node = new RelatedMedia { Id = mediaId } }],
            new PageInfo { HasNextPage = hasNextPage, CurrentPage = 1 });

    /// <summary>A request the test holds open, so it can act while the search is genuinely in flight.</summary>
    private sealed class GatedRequest
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Requested => _requested.Task;

        public bool Completed { get; private set; }

        public void MarkRequested() => _requested.TrySetResult();

        public void Release() => _release.TrySetResult();

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                Completed = true;
            }
        }
    }

    private sealed class Harness
    {
        private readonly Dictionary<string, Func<CancellationToken, Task<(IReadOnlyList<BrowseMediaItem>, PageInfo?)>>> _responses = new(StringComparer.Ordinal);
        private readonly ManualTimeProvider _time = new(DateTimeOffset.UnixEpoch);
        private readonly List<(string Query, bool? Adult, int Page)> _calls = [];

        public Harness()
        {
            var client = Substitute.For<IAniListClient>();
            client
                .SearchAnimePageAsync(
                    Arg.Any<string>(), Arg.Any<bool?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var query = call.Arg<string>();
                    lock (_calls)
                    {
                        _calls.Add((query, call.ArgAt<bool?>(1), call.ArgAt<int>(2)));
                    }

                    return _responses.TryGetValue(query, out var responder)
                        ? responder(call.ArgAt<CancellationToken>(4))
                        : Task.FromResult<(IReadOnlyList<BrowseMediaItem>, PageInfo?)>(([], new PageInfo()));
                });

            var auth = Substitute.For<IAuthService>();
            auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");

            Dialogs = new ScriptedDialogService();
            Model = new SearchPageModel(
                client,
                auth,
                Substitute.For<INavigationService>(),
                Feedback,
                Dialogs,
                new ListEntryStatusFlow(Dialogs),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                _time,
                NullLogger<SearchPageModel>.Instance);
        }

        public SearchPageModel Model { get; }

        public RecordingUserFeedback Feedback { get; } = new();

        public ScriptedDialogService Dialogs { get; }

        public IReadOnlyList<(string Query, bool? Adult, int Page)> Calls
        {
            get
            {
                lock (_calls)
                {
                    return _calls.ToList();
                }
            }
        }

        public void RespondToQuery(string query, (IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo) page)
            => _responses[query] = _ => Task.FromResult((page.Items, page.PageInfo));

        public void FailQuery(string query, Exception exception)
            => _responses[query] = _ => Task.FromException<(IReadOnlyList<BrowseMediaItem>, PageInfo?)>(exception);

        /// <summary>Holds the query's page 1 open until <see cref="GatedRequest.Release"/> is called.</summary>
        public GatedRequest GateQuery(string query)
        {
            var gate = new GatedRequest();
            _responses[query] = async ct =>
            {
                gate.MarkRequested();
                await gate.WaitAsync(ct);
                return ([], new PageInfo());
            };

            return gate;
        }

        public async Task SearchAsync(string query, bool waitForFetch = true)
        {
            var before = Calls.Count;
            Model.SearchText = query;
            await AdvancePastDebounceAsync();

            if (waitForFetch)
            {
                await WaitUntilAsync(() => Calls.Count > before && !Model.IsSearching);
            }
        }

        /// <summary>
        /// Moves the debounce timer past its delay. The keystroke handler starts the debounce as
        /// fire-and-forget, so yield first to let it actually register the timer.
        /// </summary>
        public async Task AdvancePastDebounceAsync()
        {
            await YieldAsync();
            _time.Advance(PastTheDebounce);
            await YieldAsync();
        }

        public async Task WaitUntilAsync(Func<bool> condition)
        {
            // The page model's search runs as an un-awaited continuation chain, so there is no task
            // to await. Poll rather than sleeping a fixed amount: passing cases finish in a
            // millisecond or two, and a genuine failure still reports rather than hanging.
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                await Task.Delay(1);
            }

            Assert.True(condition(), "Timed out waiting for the search state to settle.");
        }

        private static async Task YieldAsync()
        {
            for (var i = 0; i < 8; i++)
            {
                await Task.Yield();
            }
        }
    }
}
