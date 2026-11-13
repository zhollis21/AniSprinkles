using AniSprinkles.Services.Abstractions;
using AniSprinkles.UnitTests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AniSprinkles.UnitTests;

/// <summary>
/// What the studio page does beyond the shared spine (see <see cref="DetailsSpineTests{TEntity}"/>):
/// one paginated productions list with a server-side sort.
/// </summary>
public class StudioDetailsPageModelTests
{
    [Test]
    public async Task LoadAsync_SeedsProductionsAndTheirPagingCursor()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: true, 1, 2));

        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.DisplayedProductions.Count, Is.EqualTo(2));
        Assert.That(harness.Model.HasProductions, Is.True);
        Assert.That(harness.Model.ShowProductionsSection, Is.True);
        Assert.That(harness.Model.ShowProductionsEmptyState, Is.False);
        Assert.That(harness.Model.LoadMoreProductionsCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task LoadAsync_ForAStudioWithNoProductions_ShowsTheEmptyStateRatherThanABlankSection()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: false));

        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.HasProductions, Is.False);
        Assert.That(harness.Model.ShowProductionsEmptyState, Is.True);
        Assert.That(harness.Model.ShowProductionsSection, Is.True);
        Assert.That(harness.Model.LoadMoreProductionsCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task LoadMoreProductions_AppendsTheNextPageWithoutDuplicating()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: true, 1, 2));
        await harness.Model.LoadAsync(42);

        // Page two repeats item 2 — the server does this when the underlying set shifts between pages.
        harness.Client
            .LoadStudioMediaPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<StudioMediaEdge>, PageInfo?)>(
                ([Production(2), Production(3)], new PageInfo { CurrentPage = 2, HasNextPage = false })));

        await harness.Model.LoadMoreProductionsCommand.ExecuteAsync(null);

        Assert.That(harness.Model.DisplayedProductions.Count, Is.EqualTo(3));
        Assert.That(harness.Model.LoadMoreProductionsCommand.CanExecute(null), Is.False);
    }

    [Test]
    public async Task LoadMoreProductions_PagesAgainstTheLoadedStudio()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: true, 1));
        await harness.Model.LoadAsync(42);

        harness.Client
            .LoadStudioMediaPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<StudioMediaEdge>, PageInfo?)>(([], null)));

        await harness.Model.LoadMoreProductionsCommand.ExecuteAsync(null);

        // The section fetcher reads the base's LoadedId; a zero here would page the wrong studio.
        await harness.Client.Received(1).LoadStudioMediaPageAsync(
            42, Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task SelectProductionsSort_WithAPartialList_RefetchesFromTheServer()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: true, 1, 2));
        await harness.Model.LoadAsync(42);

        harness.Client
            .LoadStudioMediaPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<StudioMediaEdge>, PageInfo?)>(
                ([Production(9)], new PageInfo { CurrentPage = 1, HasNextPage = false })));

        await harness.Model.SelectProductionsSortCommand.ExecuteAsync("SCORE_DESC");

        // The server sorts across pages we have not loaded, so a partial list has to go back for page 1.
        Assert.That(harness.Model.ProductionsSort, Is.EqualTo("SCORE_DESC"));
        Assert.That(harness.Model.DisplayedProductions, Has.Exactly(1).Items);
        Assert.That(harness.Model.ProductionsSortOptions.Single(o => o.Code == "SCORE_DESC").IsSelected, Is.True);
        Assert.That(harness.Model.ProductionsSortOptions.Single(o => o.Code == "POPULARITY_DESC").IsSelected, Is.False);
    }

    [Test]
    public async Task SelectProductionsSort_WithTheCompleteList_ReordersInMemoryWithoutAnApiCall()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: false, 1, 2));
        await harness.Model.LoadAsync(42);

        await harness.Model.SelectProductionsSortCommand.ExecuteAsync("TITLE_ROMAJI");

        // Once the whole set is loaded the server can't know anything we don't — spending a
        // rate-limited request to re-sort it would be waste.
        Assert.That(harness.Model.ProductionsSort, Is.EqualTo("TITLE_ROMAJI"));
        Assert.That(harness.Model.DisplayedProductions.Count, Is.EqualTo(2));
        Assert.That(harness.Model.ProductionsSortOptions.Single(o => o.Code == "TITLE_ROMAJI").IsSelected, Is.True);
        await harness.Client.DidNotReceive().LoadStudioMediaPageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task LoadAsync_ForANewStudio_ResetsTheSortSelection()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: false, 1));
        await harness.Model.LoadAsync(1);

        harness.Client
            .LoadStudioMediaPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<StudioMediaEdge>, PageInfo?)>(([], null)));
        await harness.Model.SelectProductionsSortCommand.ExecuteAsync("TITLE_ROMAJI");
        Assert.That(harness.Model.ProductionsSort, Is.EqualTo("TITLE_ROMAJI"));

        harness.Returns(StudioWith(hasNextPage: false, 1));
        await harness.Model.LoadAsync(2);

        Assert.That(harness.Model.ProductionsSort, Is.EqualTo("POPULARITY_DESC"));
        Assert.That(harness.Model.ProductionsSortOptions.Single(o => o.Code == "POPULARITY_DESC").IsSelected, Is.True);
    }

    [Test]
    public async Task PageTitle_FallsBackWhenNoStudioIsLoaded()
    {
        var harness = new Harness();
        Assert.That(harness.Model.PageTitle, Is.EqualTo("Studio"));

        harness.Returns(new Studio { Id = 42, Name = "Bones" });
        await harness.Model.LoadAsync(42);

        Assert.That(harness.Model.PageTitle, Is.EqualTo("Bones"));
    }

    // ---- Helpers ----------------------------------------------------------------------------------

    private static StudioMediaEdge Production(int mediaId)
        => new()
        {
            Node = new RelatedMedia { Id = mediaId, Type = "ANIME", Title = new MediaTitle { Romaji = $"Show {mediaId}" } },
        };

    private static Studio StudioWith(bool hasNextPage, params int[] mediaIds)
        => new()
        {
            Id = 42,
            Name = "Studio",
            Media = [.. mediaIds.Select(Production)],
            MediaPageInfo = new PageInfo { CurrentPage = 1, HasNextPage = hasNextPage },
        };

    private sealed class Harness
    {
        public Harness()
            => Model = new StudioDetailsPageModel(
                Client, Auth, Navigation, Feedback, Browser,
                new ErrorReportService(NullLogger<ErrorReportService>.Instance),
                NullLogger<StudioDetailsPageModel>.Instance);

        public StudioDetailsPageModel Model { get; }

        public IAniListClient Client { get; } = Substitute.For<IAniListClient>();

        public IAuthService Auth { get; } = Substitute.For<IAuthService>();

        public INavigationService Navigation { get; } = Substitute.For<INavigationService>();

        public RecordingUserFeedback Feedback { get; } = new();

        public RecordingExternalBrowser Browser { get; } = new();

        public void Returns(Studio? studio)
            => Client.GetStudioAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(studio));
    }
}
