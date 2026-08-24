using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using AniSprinkles.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #118. Flipping the adult-content toggle used to reach <c>AppSettings</c> only once a 1500 ms
/// debounce and an AniList round-trip had completed, so a user who turned 18+ content off and tabbed
/// straight to a browse surface saw it appear with the old policy still in force — and stay that way
/// until a full tab cycle invalidated it.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class AdultContentCommitTests
{
    private readonly FakePreferences _storage;

    public AdultContentCommitTests() => _storage = TestDataBuilder.ResetAppSettings();

    [Fact]
    public async Task TogglingAdultContentOff_CommitsToAppSettingsWithoutWaitingForTheSave()
    {
        var harness = new Harness();
        await harness.LoadSignedInAsync(displayAdultContent: true);
        Assert.True(AppSettings.DisplayAdultContent);

        harness.Model.DisplayAdultContent = false;

        // No await between the toggle and the assertion on purpose: the whole point is that the
        // value is correct before the user can possibly navigate anywhere.
        Assert.False(AppSettings.DisplayAdultContent);

        // And the commit is local, not a fast round-trip — the AniList save is still debounced.
        await harness.Client.DidNotReceive().UpdateUserAsync(
            Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TogglingAdultContentOff_PersistsImmediatelySoAColdStartAgrees()
    {
        var harness = new Harness();
        await harness.LoadSignedInAsync(displayAdultContent: true);

        harness.Model.DisplayAdultContent = false;

        // If the app is killed inside the debounce window, the stored value is what the next launch
        // reads. Committing in memory only would resurrect the old policy on restart.
        Assert.False(_storage.Get("display_adult_content", true));
    }

    [Fact]
    public async Task TogglingAdultContentOn_CommitsJustTheSameWay()
    {
        // The dangerous direction is off, but a one-way commit would be a confusing asymmetry and
        // would leave "on" still waiting a second and a half to take effect.
        var harness = new Harness();
        await harness.LoadSignedInAsync(displayAdultContent: false);
        Assert.False(AppSettings.DisplayAdultContent);

        harness.Model.DisplayAdultContent = true;

        Assert.True(AppSettings.DisplayAdultContent);
        Assert.True(_storage.Get("display_adult_content", false));
    }

    [Fact]
    public async Task LoadingTheProfile_CommitsTheServersValueWithoutSendingItBack()
    {
        // PopulateFromUser assigns DisplayAdultContent, so it runs the same changed-handler. That is
        // correct — it is the server's value — and it must not turn around and PUT it back.
        //
        // Note this asserts no save was *sent*, not that none was queued: the debounce is a raw
        // Task.Delay with no TimeProvider seam, so "nothing pending" is not observable from here.
        // The queued-then-abandoned case is covered by DebouncedSaveAsync re-checking
        // HasUnsavedChanges after the delay, by which point PopulateFromUser has updated the
        // dirty-tracking snapshot.
        var harness = new Harness();
        await harness.LoadSignedInAsync(displayAdultContent: true);

        Assert.True(AppSettings.DisplayAdultContent);
        await harness.Client.DidNotReceive().UpdateUserAsync(
            Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>());
    }

    private sealed class Harness
    {
        public Harness()
        {
            var dialogs = new ScriptedDialogService();

            // Never completes. The debounce is deliberately left pending in these tests — asserting
            // that the commit did not wait for it is the point — and a save that resolved 1.5 s
            // later would write AppSettings statics underneath whatever test ran next.
            Client.UpdateUserAsync(Arg.Any<UpdateUserRequest>(), Arg.Any<CancellationToken>())
                .Returns(new TaskCompletionSource<AniListUser>().Task);

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

        public async Task LoadSignedInAsync(bool displayAdultContent)
        {
            Auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("token");
            Client.GetViewerAsync(Arg.Any<CancellationToken>()).Returns(new AniListUser
            {
                Id = 1,
                Name = "zhollis",
                Options = new UserOptions { DisplayAdultContent = displayAdultContent },
            });

            await Model.LoadAsync();
        }
    }
}

/// <summary>
/// The second half of #118: <c>DiscoverSectionFetch</c> resolved the adult toggle from the static on
/// every page, so a commit landing while Discover was on screen made the next Load More fetch under
/// the new policy and append it onto items fetched under the old one — two policies in one row. The
/// <c>IsBusy || _refreshEvaluating</c> guards from #116 do not cover it, because no reload is
/// involved.
/// </summary>
[Collection(AppSettingsCollection.Name)]
public class DiscoverAdultFilterPinningTests
{
    public DiscoverAdultFilterPinningTests() => TestDataBuilder.ResetAppSettings();

    [Fact]
    public async Task LoadMore_AfterTheSettingCommitsMidSession_KeepsTheSeededPolicy()
    {
        // Seeded with 18+ allowed, so page 1 may hold 18+ items and the query omits the filter.
        AppSettings.DisplayAdultContent = true;

        var harness = new Harness();
        await harness.Model.LoadAsync();

        var row = harness.Model.Rows.First(r => r.CanLoadMore && r.Definition.AdultFilter is null);
        harness.AdultFilters.Clear();

        // The user turns adult content off on Settings; the commit lands while Discover is visible.
        AppSettings.DisplayAdultContent = false;

        await harness.Model.LoadMoreSectionCommand.ExecuteAsync(row.SectionKey);

        // Page 2 must match page 1's policy. Fetching it as SFW appends a filtered page onto an
        // unfiltered one, which is the mixing the issue describes.
        Assert.Equal([null], harness.AdultFilters);
    }

    [Fact]
    public async Task LoadMore_OnAPinned18PlusRow_StillIgnoresTheToggleEntirely()
    {
        // The 18+ sections carry their own AdultFilter, which wins over the toggle by design. That
        // must survive the change — the toggle governs whether the rows are shown, not their query.
        AppSettings.DisplayAdultContent = true;

        var harness = new Harness();
        await harness.Model.LoadAsync();

        var row = harness.Model.Rows.First(r => r.CanLoadMore && r.Definition.AdultFilter is true);
        harness.AdultFilters.Clear();

        await harness.Model.LoadMoreSectionCommand.ExecuteAsync(row.SectionKey);

        Assert.Equal([true], harness.AdultFilters);
    }

    [Fact]
    public async Task AReloadAfterTheSettingChanges_ReSeedsUnderTheNewPolicy()
    {
        // Pinning must not freeze the filter forever: a genuine reload re-reads the setting, which
        // is what the OnAppearing invalidation depends on.
        AppSettings.DisplayAdultContent = true;

        var harness = new Harness();
        await harness.Model.LoadAsync();

        AppSettings.DisplayAdultContent = false;
        await harness.Model.LoadAsync(forceReload: true);

        var row = harness.Model.Rows.First(r => r.CanLoadMore && r.Definition.AdultFilter is null);
        harness.AdultFilters.Clear();

        await harness.Model.LoadMoreSectionCommand.ExecuteAsync(row.SectionKey);

        Assert.Equal([false], harness.AdultFilters);
    }

    private sealed class Harness
    {
        private readonly IAniListClient _client = Substitute.For<IAniListClient>();

        public Harness()
        {
            _client
                .GetDiscoverSectionsAsync(
                    Arg.Any<string>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(),
                    Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(SeededSections()));

            _client
                .BrowseAnimePageAsync(
                    Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<int?>(),
                    Arg.Any<bool?>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    AdultFilters.Add(call.ArgAt<bool?>(4));
                    return Task.FromResult<(IReadOnlyList<BrowseMediaItem>, PageInfo?)>(
                        ([Item(99)], new PageInfo { HasNextPage = true, CurrentPage = 2 }));
                });

            var auth = Substitute.For<IAuthService>();
            auth.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<string?>("token"));

            var dialogs = new ScriptedDialogService();
            Model = new DiscoverPageModel(
                _client,
                auth,
                Substitute.For<INavigationService>(),
                new RecordingUserFeedback(),
                dialogs,
                new ListEntryStatusFlow(dialogs),
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                new ManualTimeProvider(DateTimeOffset.UnixEpoch),
                NullLogger<DiscoverPageModel>.Instance);
        }

        public DiscoverPageModel Model { get; }

        /// <summary>The <c>isAdult</c> argument of every BrowseAnimePage call, in order.</summary>
        public List<bool?> AdultFilters { get; } = [];

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
