using AniSprinkles.Icons;
using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 Phase 1 for <see cref="MediaBrowsePageModel"/> — the "View All" page behind every Discover
/// row, and the last page model in Core with no coverage.
/// <para>
/// Three of its invariants are the kind that fail silently on device: the badge sort (the section is
/// seeded with a placeholder sort, so badging off <c>PaginatedSection.Sort</c> would label every
/// section "popularity"), the adult-content pin across Load More (#118), and the revisit
/// short-circuit, which has to refetch when sign-in or the adult toggle moved but not otherwise.
/// </para>
/// </summary>
[NonParallelizable]
public class MediaBrowsePageModelTests
{
    public MediaBrowsePageModelTests() => TestDataBuilder.ResetAppSettings();

    // ── Section resolution and the error states ──────────────────────

    [Test]
    public async Task LoadAsync_WithNoSection_ShowsAnUnretryableNotFound()
    {
        // The route carries only the enum name, so an unparseable one lands here. Retry cannot help.
        var harness = new Harness();

        await harness.Model.LoadAsync(null);

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Error));
        Assert.That(harness.Model.ErrorTitle, Is.EqualTo("Not Found"));
        Assert.That(harness.Model.CanRetry, Is.False);
        Assert.That(harness.PageCalls, Is.EqualTo(0));
    }

    [Test]
    public async Task LoadAsync_TakesItsTitleFromTheSectionDefinition()
    {
        var harness = new Harness();

        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.Model.PageTitle, Is.EqualTo("Top Anime"));
        Assert.That(harness.Model.HasSection, Is.True);
        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Content));
        Assert.That(harness.Model.IsBusy, Is.False);
    }

    [Test]
    public async Task LoadAsync_WhenTheFetchFails_ShowsTheApiErrorTextAndOffersRetry()
    {
        var harness = new Harness();
        harness.FailNextPages(new AniListApiException(ApiErrorKind.ServiceOutage, "down"));

        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Error));
        Assert.That(harness.Model.ErrorTitle, Is.EqualTo("AniList is Down"));
        Assert.That(harness.Model.CanRetry, Is.True);
        Assert.That(harness.Model.IsBusy, Is.False);
    }

    [Test]
    public async Task LoadAsync_WhenCancelled_StaysSilentInsteadOfShowingAnError()
    {
        // Navigating away mid-fetch is not a failure, and an error page would flash behind the
        // outgoing transition.
        var harness = new Harness();
        harness.FailNextPages(new OperationCanceledException());

        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.Model.CurrentState, Is.Not.EqualTo(PageState.Error));
        Assert.That(harness.Model.ErrorTitle, Is.EqualTo(string.Empty));
        Assert.That(harness.Model.IsBusy, Is.False);
    }

    [Test]
    public async Task LoadAsync_WithNoResults_ShowsTheEmptyState()
    {
        var harness = new Harness();
        harness.ReturnPage([], hasNextPage: false);

        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Content));
        Assert.That(harness.Model.HasItems, Is.False);
        Assert.That(harness.Model.ShowEmptyState, Is.True);
    }

    [Test]
    public async Task RetryLoad_AfterAFailure_RefetchesTheSameSection()
    {
        var harness = new Harness();
        harness.FailNextPages(new AniListApiException(ApiErrorKind.Network, "offline"));
        await harness.Model.LoadAsync(DiscoverSection.Top);
        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Error));

        harness.StopFailing();
        await harness.Model.RetryLoadCommand.ExecuteAsync(null);

        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Content));
        Assert.That(harness.Model.HasItems, Is.True);
    }

    [Test]
    public async Task RetryLoad_AfterASuccessfulLoad_StillRefetches()
    {
        // Retry clears the definition precisely so it bypasses the revisit short-circuit — a user
        // tapping Retry wants new data, not the cached page they are already looking at.
        var harness = new Harness();
        await harness.Model.LoadAsync(DiscoverSection.Top);
        var before = harness.PageCalls;

        await harness.Model.RetryLoadCommand.ExecuteAsync(null);

        Assert.That(harness.PageCalls, Is.EqualTo(before + 1));
    }

    // ── Badges and ranks ─────────────────────────────────────────────

    [Test]
    public async Task Badges_UseTheSectionsSortRatherThanThePaginatedSectionPlaceholder()
    {
        // The section is constructed with a POPULARITY_DESC placeholder and never re-seeds it (there
        // is no sort picker here), so badging off PaginatedSection.Sort would show the popularity
        // glyph and count on every section, including this one.
        var harness = new Harness();

        await harness.Model.LoadAsync(DiscoverSection.Trending);

        var badge = harness.Model.Items[0].MetricBadge;
        Assert.That(badge, Is.Not.Null);
        Assert.That(badge.Glyph, Is.EqualTo(Glyphs.Regular.Fire24));
        Assert.That(badge.Glyph, Is.Not.EqualTo(Glyphs.Regular.People24));
    }

    [Test]
    public async Task ARankedSection_NumbersItsItemsFromOne()
    {
        var harness = new Harness();
        harness.ReturnPage([Harness.Item(1), Harness.Item(2), Harness.Item(3)], hasNextPage: false);

        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.Model.Items.Select(i => i.Rank), Is.EqualTo([1, 2, 3]));
    }

    [Test]
    public async Task AnUnrankedSection_LeavesRanksUnset()
    {
        var harness = new Harness();
        harness.ReturnPage([Harness.Item(1), Harness.Item(2)], hasNextPage: false);

        await harness.Model.LoadAsync(DiscoverSection.Trending);

        Assert.Multiple(() =>
        {
            foreach (var item in harness.Model.Items)
            {
                Assert.That(item.Rank, Is.EqualTo(0));
            }
        });
    }

    [Test]
    public async Task RanksContinueAcrossPages()
    {
        // Rank is stamped from the pre-append count, so page 2 has to carry on from page 1 rather
        // than restarting — a restart is very visible on the Top Anime list.
        var harness = new Harness();
        harness.ReturnPage([Harness.Item(1), Harness.Item(2)], hasNextPage: true);
        await harness.Model.LoadAsync(DiscoverSection.Top);

        harness.ReturnPage([Harness.Item(3), Harness.Item(4)], hasNextPage: false);
        await harness.Model.LoadMoreCommand.ExecuteAsync(null);

        Assert.That(harness.Model.Items.Select(i => i.Rank), Is.EqualTo([1, 2, 3, 4]));
    }

    // ── Paging ───────────────────────────────────────────────────────

    [Test]
    public async Task LoadMore_AppendsTheNextPage()
    {
        var harness = new Harness();
        harness.ReturnPage([Harness.Item(1)], hasNextPage: true);
        await harness.Model.LoadAsync(DiscoverSection.Top);

        harness.ReturnPage([Harness.Item(2)], hasNextPage: false);
        await harness.Model.LoadMoreCommand.ExecuteAsync(null);

        Assert.That(harness.Model.Items.Select(i => i.Node!.Id), Is.EqualTo([1, 2]));
        Assert.That(harness.LastCall.Page, Is.EqualTo(2));
    }

    [Test]
    public async Task LoadMore_OnTheLastPage_IsNotOffered()
    {
        var harness = new Harness();
        harness.ReturnPage([Harness.Item(1)], hasNextPage: false);

        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.Model.LoadMoreCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task LoadMore_WhileAReloadIsInFlight_IsRefused()
    {
        // The page load scope does not cover this: Begin() cancels the PREVIOUS token, so a Load
        // More started after the reload began shares the reload's token and would survive it,
        // appending items fetched under whatever policy the reload was about to replace.
        var harness = new Harness();
        harness.ReturnPage([Harness.Item(1)], hasNextPage: true);
        await harness.Model.LoadAsync(DiscoverSection.Top);

        var gate = harness.GateNextPage();
        var reload = harness.Model.RetryLoadCommand.ExecuteAsync(null);
        await gate.Requested;
        Assert.That(harness.Model.IsBusy, Is.True);

        var before = harness.PageCalls;
        await harness.Model.LoadMoreCommand.ExecuteAsync(null);
        Assert.That(harness.PageCalls, Is.EqualTo(before));

        gate.Release();
        await reload;
    }

    // ── The adult-content pin (#118) ─────────────────────────────────

    [Test]
    public async Task ASfwSeededList_KeepsAskingForSfwOnLoadMore_EvenAfterTheToggleFlips()
    {
        // The bug this pins: DiscoverSectionFetch used to read AppSettings live per page, so a
        // toggle committed mid-session made the next Load More fetch 18+ items and append them
        // onto a list the user had just made safe.
        AppSettings.DisplayAdultContent = false;
        var harness = new Harness();
        harness.ReturnPage([Harness.Item(1)], hasNextPage: true);
        await harness.Model.LoadAsync(DiscoverSection.Top);
        Assert.That(harness.LastCall.IsAdult, Is.EqualTo(false));

        AppSettings.DisplayAdultContent = true;
        harness.ReturnPage([Harness.Item(2)], hasNextPage: false);
        await harness.Model.LoadMoreCommand.ExecuteAsync(null);

        Assert.That(harness.LastCall.IsAdult, Is.EqualTo(false));
    }

    [Test]
    public async Task AnAdultPinnedSection_IgnoresTheUsersToggleEntirely()
    {
        // The 18+ pair is defined as adult-only; the toggle governs whether the row exists at all,
        // not what it contains.
        AppSettings.DisplayAdultContent = false;
        var harness = new Harness();

        await harness.Model.LoadAsync(DiscoverSection.PopularAdult);

        Assert.That(harness.LastCall.IsAdult, Is.EqualTo(true));
    }

    [Test]
    public async Task WithAdultContentOn_TheFilterIsOmittedSoEverythingMixesIn()
    {
        AppSettings.DisplayAdultContent = true;
        var harness = new Harness();

        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.LastCall.IsAdult, Is.Null);
    }

    [Test]
    public async Task ASectionPinsItsFormat()
    {
        var harness = new Harness();

        await harness.Model.LoadAsync(DiscoverSection.TopMovies);

        Assert.That(harness.LastCall.Format, Is.EqualTo("MOVIE"));
        Assert.That(harness.LastCall.Sort, Is.EqualTo("SCORE_DESC"));
    }

    // ── The revisit short-circuit ────────────────────────────────────

    [Test]
    public async Task RevisitingTheSameSection_ServesTheCachedItemsWithoutRefetching()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync(DiscoverSection.Top);
        var before = harness.PageCalls;

        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.PageCalls, Is.EqualTo(before));
        Assert.That(harness.Model.CurrentState, Is.EqualTo(PageState.Content));
    }

    [Test]
    public async Task RevisitingAfterTheAdultToggleFlipped_Refetches()
    {
        // Showing the cached set here can leave 18+ cards on screen after the user turned them off.
        AppSettings.DisplayAdultContent = true;
        var harness = new Harness();
        await harness.Model.LoadAsync(DiscoverSection.Top);
        var before = harness.PageCalls;

        AppSettings.DisplayAdultContent = false;
        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.PageCalls, Is.EqualTo(before + 1));
    }

    [Test]
    public async Task RevisitingAfterSigningOut_Refetches()
    {
        // The cached cards carry mediaListEntry chips that no longer belong to anyone.
        var harness = new Harness();
        await harness.Model.LoadAsync(DiscoverSection.Top);
        var before = harness.PageCalls;

        harness.SignOut();
        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.PageCalls, Is.EqualTo(before + 1));
    }

    [Test]
    public async Task NavigatingToADifferentSection_Refetches()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync(DiscoverSection.Top);
        var before = harness.PageCalls;

        await harness.Model.LoadAsync(DiscoverSection.Trending);

        Assert.That(harness.PageCalls, Is.EqualTo(before + 1));
        Assert.That(harness.Model.PageTitle, Is.EqualTo("Trending Now"));
    }

    // ── Title re-projection (#127) ───────────────────────────────────

    [Test]
    public async Task RefreshDisplaySettings_AfterTheTitleLanguageMoved_ReprojectsTheCards()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync(DiscoverSection.Top);
        var reprojected = harness.WatchForReprojection();

        AppSettings.TitleLanguage = UserTitleLanguage.English;
        harness.Model.RefreshDisplaySettings();

        Assert.That(reprojected(), Is.True);
    }

    [Test]
    public async Task RefreshDisplaySettings_WithNothingChanged_LeavesTheCardsAlone()
    {
        var harness = new Harness();
        await harness.Model.LoadAsync(DiscoverSection.Top);
        var reprojected = harness.WatchForReprojection();

        harness.Model.RefreshDisplaySettings();

        Assert.That(reprojected(), Is.False);
    }

    [Test]
    public async Task ARevisitThatShortCircuits_StillReprojectsForANewTitleLanguage()
    {
        // The #127 case exactly: View All is pushed onto a tab's stack, so it gets no OnAppearing,
        // and the re-projection has to happen ahead of the short-circuit or it never runs at all.
        var harness = new Harness();
        await harness.Model.LoadAsync(DiscoverSection.Top);
        var before = harness.PageCalls;
        var reprojected = harness.WatchForReprojection();

        AppSettings.TitleLanguage = UserTitleLanguage.Native;
        await harness.Model.LoadAsync(DiscoverSection.Top);

        Assert.That(harness.PageCalls, Is.EqualTo(before));
        Assert.That(reprojected(), Is.True);
    }

    // ── View mode ────────────────────────────────────────────────────

    [Test]
    public void TheViewMode_OpensInWhateverWasLastPickedAnywhere()
    {
        // Shared with Library through one preference key, so switching the look on either page
        // carries to the other.
        var preferences = new FakePreferences();
        ListViewModePreference.Save(preferences, ListViewMode.Compact);

        var harness = new Harness(preferences);

        Assert.That(harness.Model.CurrentViewMode, Is.EqualTo(ListViewMode.Compact));
    }

    [Test]
    public void ConstructingThePage_DoesNotWriteTheViewModeBack()
    {
        // Loading is a read; a write here would churn the shared key on every navigation.
        var preferences = new FakePreferences();
        ListViewModePreference.Save(preferences, ListViewMode.Compact);
        var writesAfterSeeding = preferences.SetCount;

        _ = new Harness(preferences);

        Assert.That(preferences.SetCount, Is.EqualTo(writesAfterSeeding));
    }

    [TestCase(ListViewMode.Standard, ListViewMode.Large)]
    [TestCase(ListViewMode.Large, ListViewMode.Compact)]
    [TestCase(ListViewMode.Compact, ListViewMode.Standard)]
    public void CycleViewMode_AdvancesAndPersists(ListViewMode from, ListViewMode expected)
    {
        var preferences = new FakePreferences();
        ListViewModePreference.Save(preferences, from);
        var harness = new Harness(preferences);

        harness.Model.CycleViewModeCommand.Execute(null);

        Assert.That(harness.Model.CurrentViewMode, Is.EqualTo(expected));
        Assert.That(ListViewModePreference.Load(preferences), Is.EqualTo(expected));
    }

    [Test]
    public void EachViewMode_HasItsOwnSwitcherGlyph()
    {
        var harness = new Harness();
        var glyphs = new List<string>();

        foreach (var _ in Enumerable.Range(0, 3))
        {
            glyphs.Add(harness.Model.ViewModeIconGlyph);
            harness.Model.CycleViewModeCommand.Execute(null);
        }

        Assert.That(glyphs.Distinct().Count(), Is.EqualTo(glyphs.Count));
    }

    // ── Navigation and long-press ────────────────────────────────────

    [Test]
    public async Task TappingAnAnimeCard_NavigatesToItsDetails()
    {
        var harness = new Harness();
        var item = Harness.Item(42);

        await harness.Model.NavigateToMediaCommand.ExecuteAsync(item);

        await harness.Navigation.Received(1).GoToAsync(
            "media-details",
            Arg.Any<bool>(),
            Arg.Is<IDictionary<string, object>>(p => (int)p["mediaId"] == 42));
    }

    [Test]
    public async Task TappingAMangaCard_NavigatesLikeAnyOtherCard()
    {
        // Toasted "not supported yet" until #12, when Media details still queried type: ANIME and a
        // manga id would have 404'd behind the transition. View All is anime-only today, so this is
        // a guard against the type ever mattering here again rather than a reachable path.
        var harness = new Harness();
        var item = Harness.Item(42, type: "MANGA");

        await harness.Model.NavigateToMediaCommand.ExecuteAsync(item);

        Assert.That(harness.Feedback.Toasts, Is.Empty);
        await harness.Navigation.Received(1).GoToAsync(
            "media-details",
            Arg.Any<bool>(),
            Arg.Is<IDictionary<string, object>>(p => (int)p["mediaId"] == 42));
    }

    [Test]
    public async Task TappingACardWithNoMedia_DoesNothing()
    {
        var harness = new Harness();

        await harness.Model.NavigateToMediaCommand.ExecuteAsync(null);

        await harness.Navigation.DidNotReceive().GoToAsync(
            Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IDictionary<string, object>>());
        Assert.That(harness.Feedback.Toasts, Is.Empty);
    }

    [Test]
    public async Task LongPressingWhileSignedOut_AsksTheUserToSignIn()
    {
        var harness = new Harness();
        harness.SignOut();

        await harness.Model.ShowItemActionsCommand.ExecuteAsync(Harness.Item(42));

        Assert.That(harness.Feedback.Toasts, Has.Exactly(1).Items);
        Assert.That(harness.Feedback.Toasts.Single(), Does.Contain("Sign in"));
    }

    [Test]
    public async Task LongPressingACardWithNoMedia_DoesNothing()
    {
        var harness = new Harness();

        await harness.Model.ShowItemActionsCommand.ExecuteAsync(new BrowseMediaItem());

        Assert.That(harness.Feedback.Toasts, Is.Empty);
    }

    private sealed class Harness
    {
        private readonly IAniListClient _client = Substitute.For<IAniListClient>();
        private IReadOnlyList<BrowseMediaItem> _pageItems = [Item(1)];
        private bool _hasNextPage;
        private Exception? _failure;
        private string? _token = "token";
        private PageGate? _pendingGate;
        private int _pageCalls;

        public Harness(FakePreferences? preferences = null)
        {
            _client
                .BrowseAnimePageAsync(
                    Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
                    Arg.Any<bool?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Interlocked.Increment(ref _pageCalls);
                    LastCall = new Call(
                        call.ArgAt<string>(0),
                        call.ArgAt<string?>(1),
                        call.ArgAt<bool?>(4),
                        call.ArgAt<string?>(5),
                        call.ArgAt<int>(6));

                    if (_failure is not null)
                    {
                        return Task.FromException<(IReadOnlyList<BrowseMediaItem>, PageInfo?)>(_failure);
                    }

                    (IReadOnlyList<BrowseMediaItem>, PageInfo?) result = (_pageItems, new PageInfo
                    {
                        HasNextPage = _hasNextPage,
                        CurrentPage = call.ArgAt<int>(6),
                    });

                    var gate = Interlocked.Exchange(ref _pendingGate, null);
                    return gate is null ? Task.FromResult(result) : gate.WaitAsync(result);
                });

            var auth = Substitute.For<IAuthService>();
            auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(_ => _token);

            var dialogs = new ScriptedDialogService();
            Model = new MediaBrowsePageModel(
                _client,
                auth,
                Navigation,
                Feedback,
                dialogs,
                new ListEntryStatusFlow(dialogs),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                new ManualTimeProvider(DateTimeOffset.UnixEpoch),
                preferences ?? new FakePreferences(),
                NullLogger<MediaBrowsePageModel>.Instance);
        }

        public MediaBrowsePageModel Model { get; }

        public INavigationService Navigation { get; } = Substitute.For<INavigationService>();

        public RecordingUserFeedback Feedback { get; } = new();

        public int PageCalls => Volatile.Read(ref _pageCalls);

        public Call LastCall { get; private set; } = new("", null, null, null, 0);

        public void ReturnPage(IReadOnlyList<BrowseMediaItem> items, bool hasNextPage)
        {
            _pageItems = items;
            _hasNextPage = hasNextPage;
        }

        public void FailNextPages(Exception exception) => _failure = exception;

        public void StopFailing() => _failure = null;

        public void SignOut() => _token = null;

        public PageGate GateNextPage()
        {
            var gate = new PageGate();
            Interlocked.Exchange(ref _pendingGate, gate);
            return gate;
        }

        /// <summary>
        /// Watches every card on screen for the <c>Node</c> re-raise that makes the nested
        /// <c>Node.DisplayTitle</c> binding re-resolve. Returns a probe rather than a bool so the
        /// subscription is in place before the act step.
        /// </summary>
        public Func<bool> WatchForReprojection()
        {
            var seen = false;
            foreach (var item in Model.Items)
            {
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(BrowseMediaItem.Node))
                    {
                        seen = true;
                    }
                };
            }

            return () => seen;
        }

        public static BrowseMediaItem Item(int id, string type = "ANIME")
            => new() { Node = new RelatedMedia { Id = id, Type = type, Popularity = 100, Trending = 50 } };

        public sealed record Call(string Sort, string? Status, bool? IsAdult, string? Format, int Page);

        public sealed class PageGate
        {
            private readonly TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task Requested => _requested.Task;

            public void Release() => _released.TrySetResult();

            public async Task<(IReadOnlyList<BrowseMediaItem>, PageInfo?)> WaitAsync(
                (IReadOnlyList<BrowseMediaItem>, PageInfo?) result)
            {
                _requested.TrySetResult();
                await _released.Task;
                return result;
            }
        }
    }
}
