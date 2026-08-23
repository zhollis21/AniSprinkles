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
    [Fact]
    public async Task LoadAsync_SeedsProductionsAndTheirPagingCursor()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: true, 1, 2));

        await harness.Model.LoadAsync(42);

        Assert.Equal(2, harness.Model.DisplayedProductions.Count);
        Assert.True(harness.Model.HasProductions);
        Assert.True(harness.Model.ShowProductionsSection);
        Assert.False(harness.Model.ShowProductionsEmptyState);
        Assert.True(harness.Model.LoadMoreProductionsCommand.CanExecute(null));
    }

    [Fact]
    public async Task LoadAsync_ForAStudioWithNoProductions_ShowsTheEmptyStateRatherThanABlankSection()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: false));

        await harness.Model.LoadAsync(42);

        Assert.False(harness.Model.HasProductions);
        Assert.True(harness.Model.ShowProductionsEmptyState);
        Assert.True(harness.Model.ShowProductionsSection);
        Assert.False(harness.Model.LoadMoreProductionsCommand.CanExecute(null));
    }

    [Fact]
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

        Assert.Equal(3, harness.Model.DisplayedProductions.Count);
        Assert.False(harness.Model.LoadMoreProductionsCommand.CanExecute(null));
    }

    [Fact]
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

    [Fact]
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
        Assert.Equal("SCORE_DESC", harness.Model.ProductionsSort);
        Assert.Single(harness.Model.DisplayedProductions);
        Assert.True(harness.Model.ProductionsSortOptions.Single(o => o.Code == "SCORE_DESC").IsSelected);
        Assert.False(harness.Model.ProductionsSortOptions.Single(o => o.Code == "POPULARITY_DESC").IsSelected);
    }

    [Fact]
    public async Task SelectProductionsSort_WithTheCompleteList_ReordersInMemoryWithoutAnApiCall()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: false, 1, 2));
        await harness.Model.LoadAsync(42);

        await harness.Model.SelectProductionsSortCommand.ExecuteAsync("TITLE_ROMAJI");

        // Once the whole set is loaded the server can't know anything we don't — spending a
        // rate-limited request to re-sort it would be waste.
        Assert.Equal("TITLE_ROMAJI", harness.Model.ProductionsSort);
        Assert.Equal(2, harness.Model.DisplayedProductions.Count);
        Assert.True(harness.Model.ProductionsSortOptions.Single(o => o.Code == "TITLE_ROMAJI").IsSelected);
        await harness.Client.DidNotReceive().LoadStudioMediaPageAsync(
            Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_ForANewStudio_ResetsTheSortSelection()
    {
        var harness = new Harness();
        harness.Returns(StudioWith(hasNextPage: false, 1));
        await harness.Model.LoadAsync(1);

        harness.Client
            .LoadStudioMediaPageAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<(IReadOnlyList<StudioMediaEdge>, PageInfo?)>(([], null)));
        await harness.Model.SelectProductionsSortCommand.ExecuteAsync("TITLE_ROMAJI");
        Assert.Equal("TITLE_ROMAJI", harness.Model.ProductionsSort);

        harness.Returns(StudioWith(hasNextPage: false, 1));
        await harness.Model.LoadAsync(2);

        Assert.Equal("POPULARITY_DESC", harness.Model.ProductionsSort);
        Assert.True(harness.Model.ProductionsSortOptions.Single(o => o.Code == "POPULARITY_DESC").IsSelected);
    }

    [Fact]
    public async Task PageTitle_FallsBackWhenNoStudioIsLoaded()
    {
        var harness = new Harness();
        Assert.Equal("Studio", harness.Model.PageTitle);

        harness.Returns(new Studio { Id = 42, Name = "Bones" });
        await harness.Model.LoadAsync(42);

        Assert.Equal("Bones", harness.Model.PageTitle);
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
