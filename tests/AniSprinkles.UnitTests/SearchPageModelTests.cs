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

    // ── Media-type toggle (#12) ──────────────────────────────────────

    [Fact]
    public async Task TheSearchKind_IsSentWithTheQuery()
    {
        var harness = new Harness();
        harness.RespondToQuery("berserk", Page(mediaId: 1, hasNextPage: false));

        await harness.SearchAsync("berserk");
        Assert.Equal(MediaKind.Anime, harness.Calls[^1].Kind);

        harness.Model.SelectSearchKindCommand.Execute("Manga");
        await harness.WaitForRerunAsync();

        Assert.Equal(MediaKind.Manga, harness.Calls[^1].Kind);
    }

    [Fact]
    public async Task FlippingTheKind_ReRunsWhateverIsAlreadyTyped()
    {
        // Otherwise the user retypes their query to see it under the other type, or worse, sits
        // looking at anime results with the Manga pill lit.
        var harness = new Harness();
        harness.RespondToQuery("berserk", Page(mediaId: 1, hasNextPage: false));

        await harness.SearchAsync("berserk");
        var before = harness.Calls.Count;

        harness.Model.SelectSearchKindCommand.Execute("Manga");
        await harness.WaitForRerunAsync();

        Assert.True(harness.Calls.Count > before);
        Assert.Equal("berserk", harness.Calls[^1].Query);
    }

    [Fact]
    public void FlippingToTheKindAlreadySelected_DoesNothing()
    {
        var harness = new Harness();

        harness.Model.SelectSearchKindCommand.Execute("Anime");

        Assert.Empty(harness.Calls);
        Assert.Equal(MediaKind.Anime, harness.Model.SearchKind);
    }

    [Fact]
    public async Task LaterPages_UseTheKindPageOneWasSeededUnder()
    {
        // The same pin the adult filter needs, for the same reason: flipping the toggle while a
        // Load More is in flight would otherwise append manga pages beneath anime results.
        var harness = new Harness();
        harness.RespondToQuery("berserk", Page(mediaId: 1, hasNextPage: true));

        await harness.SearchAsync("berserk");
        Assert.Equal(MediaKind.Anime, harness.Calls[^1].Kind);

        // Reach into the property directly rather than the command, which would re-run the query
        // and re-seed the pin — the race being modelled is a change that lands mid-page.
        harness.Model.SearchKind = MediaKind.Manga;
        await harness.Model.LoadMoreSearchResultsCommand.ExecuteAsync(null);

        Assert.Equal(MediaKind.Anime, harness.Calls[^1].Kind);
        Assert.Equal(2, harness.Calls[^1].Page);
    }

    [Fact]
    public async Task AKindFlipMidFetch_DiscardsTheInFlightResultsForTheOtherType()
    {
        // The interleaving the query-string guard alone cannot catch: flipping Anime → Manga keeps
        // the SAME query text, so `_activeSearchQuery == query` still holds when the stale anime
        // fetch resumes. Only the cancellation token separates them, and #116 is a standing lesson
        // in what happens when a superseded continuation is allowed to seed.
        var harness = new Harness();
        var animeFetch = harness.GateQuery("berserk");

        await harness.SearchAsync("berserk", waitForFetch: false);
        await animeFetch.Requested;
        Assert.Equal(MediaKind.Anime, harness.Calls[^1].Kind);

        // The flip re-runs the same text; point the responder at a distinguishable manga page.
        harness.Model.SelectSearchKindCommand.Execute("Manga");
        harness.RespondToQuery("berserk", Page(mediaId: 99, hasNextPage: false));
        await harness.WaitForRerunAsync();

        // Now the stale anime request finally completes. It must not touch the section.
        animeFetch.Release();
        await harness.WaitUntilAsync(() => animeFetch.Completed);

        Assert.Equal(MediaKind.Manga, harness.Calls[^1].Kind);
        Assert.Equal(99, Assert.Single(harness.Model.SearchSection.Items).Node?.Id);
    }

    [Fact]
    public void TheSelectedKind_IsPersistedAndRestored()
    {
        var harness = new Harness();

        harness.Model.SelectSearchKindCommand.Execute("Manga");
        Assert.Equal("MANGA", harness.Preferences.Get("search_media_kind", string.Empty));

        // A second page model over the same preferences — what happens on the next app launch.
        var restored = new Harness(harness.Preferences);
        Assert.Equal(MediaKind.Manga, restored.Model.SearchKind);
    }

    [Theory]
    [InlineData(MediaKind.Anime, "Search all anime...", "Search all anime", "No anime found")]
    [InlineData(MediaKind.Manga, "Search all manga...", "Search all manga", "No manga found")]
    public void TheCopy_FollowsTheSelectedKind(
        MediaKind kind, string placeholder, string idle, string noResults)
    {
        var harness = new Harness();
        harness.Model.SearchKind = kind;

        Assert.Equal(placeholder, harness.Model.SearchPlaceholder);
        Assert.Equal(idle, harness.Model.IdlePrompt);
        Assert.Equal(noResults, harness.Model.NoResultsMessage);
        Assert.Equal(kind.ToString(), harness.Model.SearchKindKey);
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
        private readonly List<(string Query, MediaKind Kind, bool? Adult, int Page)> _calls = [];

        public Harness(FakePreferences? preferences = null)
        {
            Preferences = preferences ?? new FakePreferences();

            var client = Substitute.For<IAniListClient>();
            client
                .SearchMediaPageAsync(
                    Arg.Any<string>(), Arg.Any<MediaKind>(), Arg.Any<bool?>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var query = call.Arg<string>();
                    lock (_calls)
                    {
                        _calls.Add((query, call.ArgAt<MediaKind>(1), call.ArgAt<bool?>(2), call.ArgAt<int>(3)));
                    }

                    return _responses.TryGetValue(query, out var responder)
                        ? responder(call.ArgAt<CancellationToken>(5))
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
                Preferences,
                _time,
                NullLogger<SearchPageModel>.Instance);
        }

        public SearchPageModel Model { get; }

        public RecordingUserFeedback Feedback { get; } = new();

        public ScriptedDialogService Dialogs { get; }

        public FakePreferences Preferences { get; }

        public IReadOnlyList<(string Query, MediaKind Kind, bool? Adult, int Page)> Calls
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

        /// <summary>
        /// Waits out the re-run a kind flip triggers (#12). SelectSearchKind clears and restores
        /// SearchText, which routes through the same debounce a keystroke does.
        /// </summary>
        public async Task WaitForRerunAsync()
        {
            var before = Calls.Count;
            await AdvancePastDebounceAsync();
            await WaitUntilAsync(() => Calls.Count > before && !Model.IsSearching);
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
