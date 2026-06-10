using AniSprinkles.Models;
using AniSprinkles.PageModels;

namespace AniSprinkles.UnitTests;

public class VoiceActorAggregatorTests
{
    private static VoiceActor Actor(int id, string language, int favourites = 0) =>
        new() { Id = id, Language = language, Favourites = favourites, Name = new CharacterName { Full = $"Actor {id}" } };

    private static CharacterMediaEdge Edge(params VoiceActor[] actors) =>
        new() { VoiceActors = actors.ToList() };

    private static PageInfo Page(int current, bool hasNext) =>
        new() { CurrentPage = current, HasNextPage = hasNext };

    private static (IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo) Result(
        PageInfo info, params CharacterMediaEdge[] edges) => (edges.ToList(), info);

    [Fact]
    public void Seed_SameActorAcrossEdges_DedupedByStaffId()
    {
        var aggregator = new VoiceActorAggregator((_, _) => throw new InvalidOperationException());

        aggregator.Seed([Edge(Actor(1, "Japanese")), Edge(Actor(1, "Japanese"))], Page(1, hasNext: false));

        Assert.Single(aggregator.Items);
        Assert.Equal(1, aggregator.Items[0].Id);
    }

    [Fact]
    public void Seed_MixedLanguages_OrdersJapaneseFirst()
    {
        var aggregator = new VoiceActorAggregator((_, _) => throw new InvalidOperationException());

        // English has far more favourites but Japanese (the original seiyuu) must still sort first.
        aggregator.Seed([Edge(Actor(1, "English", favourites: 9999), Actor(2, "Japanese", favourites: 5))], Page(1, hasNext: false));

        Assert.Equal("Japanese", aggregator.Items[0].Language);
        Assert.Equal("English", aggregator.Items[1].Language);
    }

    [Fact]
    public void Seed_NoVoiceActors_IsEmpty()
    {
        var aggregator = new VoiceActorAggregator((_, _) => throw new InvalidOperationException());

        aggregator.Seed([Edge()], Page(1, hasNext: false));

        Assert.True(aggregator.IsEmpty);
        Assert.False(aggregator.HasMore);
    }

    [Fact]
    public async Task CheckForMoreAsync_NextPageHasNewActor_AppendsIt()
    {
        var aggregator = new VoiceActorAggregator((page, _) =>
            Task.FromResult(Result(Page(2, hasNext: false), Edge(Actor(2, "English")))));
        aggregator.Seed([Edge(Actor(1, "Japanese"))], Page(1, hasNext: true));

        await aggregator.CheckForMoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 2], aggregator.Items.Select(a => a.Id));
        Assert.False(aggregator.HasMore);
    }

    [Fact]
    public async Task CheckForMoreAsync_PagesRepeatSeenActors_WalksUntilNewFound()
    {
        var pages = new Dictionary<int, (IReadOnlyList<CharacterMediaEdge>, PageInfo?)>
        {
            [2] = Result(Page(2, hasNext: true), Edge(Actor(1, "Japanese"))), // already seen — no new
            [3] = Result(Page(3, hasNext: true), Edge(Actor(2, "English"))),  // finally a new one
        };
        var fetchedPages = new List<int>();
        var aggregator = new VoiceActorAggregator((page, _) =>
        {
            fetchedPages.Add(page);
            return Task.FromResult(pages[page]);
        });
        aggregator.Seed([Edge(Actor(1, "Japanese"))], Page(1, hasNext: true));

        await aggregator.CheckForMoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2, 3], fetchedPages);
        Assert.Equal([1, 2], aggregator.Items.Select(a => a.Id));
    }

    [Fact]
    public async Task CheckForMoreAsync_NoNewActorsWithinCap_StopsAtPageCap()
    {
        var fetchedPages = new List<int>();
        var aggregator = new VoiceActorAggregator(
            (page, _) =>
            {
                fetchedPages.Add(page);
                return Task.FromResult(Result(Page(page, hasNext: true), Edge(Actor(1, "Japanese"))));
            },
            maxPagesPerCheck: 2);
        aggregator.Seed([Edge(Actor(1, "Japanese"))], Page(1, hasNext: true));

        await aggregator.CheckForMoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2, 3], fetchedPages); // walked exactly the cap
        Assert.Single(aggregator.Items);     // surfaced nobody new
        Assert.True(aggregator.HasMore);     // more remains to search next tap
    }

    [Fact]
    public async Task CheckForMoreAsync_MediaExhausted_SetsHasMoreFalse()
    {
        var aggregator = new VoiceActorAggregator((page, _) =>
            Task.FromResult(Result(Page(2, hasNext: false), Edge(Actor(1, "Japanese"))))); // repeat, last page
        aggregator.Seed([Edge(Actor(1, "Japanese"))], Page(1, hasNext: true));

        await aggregator.CheckForMoreAsync(TestContext.Current.CancellationToken);

        Assert.False(aggregator.HasMore);
    }

    [Fact]
    public async Task CheckForMoreAsync_NoNextPage_DoesNotFetch()
    {
        var fetched = false;
        var aggregator = new VoiceActorAggregator((page, _) =>
        {
            fetched = true;
            return Task.FromResult(Result(Page(2, hasNext: false), Edge(Actor(9, "English"))));
        });
        aggregator.Seed([Edge(Actor(1, "Japanese"))], Page(1, hasNext: false));

        await aggregator.CheckForMoreAsync(TestContext.Current.CancellationToken);

        Assert.False(fetched);
    }

    [Fact]
    public void Reset_AfterSeed_ClearsItemsAndPagingState()
    {
        var aggregator = new VoiceActorAggregator((_, _) => throw new InvalidOperationException());
        aggregator.Seed([Edge(Actor(1, "Japanese"))], Page(1, hasNext: true));

        aggregator.Reset();

        Assert.Empty(aggregator.Items);
        Assert.False(aggregator.HasMore);
    }
}
