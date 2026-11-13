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

    [Test]
    public void Seed_SameActorAcrossEdges_DedupedByStaffId()
    {
        var aggregator = new VoiceActorAggregator((_, _) => throw new InvalidOperationException());

        aggregator.Seed([Edge(Actor(1, "Japanese")), Edge(Actor(1, "Japanese"))], Page(1, hasNext: false));

        Assert.That(aggregator.Items, Has.Exactly(1).Items);
        Assert.That(aggregator.Items[0].Id, Is.EqualTo(1));
    }

    [Test]
    public void Seed_MixedLanguages_OrdersJapaneseFirst()
    {
        var aggregator = new VoiceActorAggregator((_, _) => throw new InvalidOperationException());

        // English has far more favourites but Japanese (the original seiyuu) must still sort first.
        aggregator.Seed([Edge(Actor(1, "English", favourites: 9999), Actor(2, "Japanese", favourites: 5))], Page(1, hasNext: false));

        Assert.That(aggregator.Items[0].Language, Is.EqualTo("Japanese"));
        Assert.That(aggregator.Items[1].Language, Is.EqualTo("English"));
    }

    [Test]
    public void Seed_NoVoiceActors_IsEmpty()
    {
        var aggregator = new VoiceActorAggregator((_, _) => throw new InvalidOperationException());

        aggregator.Seed([Edge()], Page(1, hasNext: false));

        Assert.That(aggregator.IsEmpty, Is.True);
        Assert.That(aggregator.HasMore, Is.False);
    }

    [Test]
    public async Task CheckForMoreAsync_NextPageHasNewActor_AppendsIt()
    {
        var aggregator = new VoiceActorAggregator((page, _) =>
            Task.FromResult(Result(Page(2, hasNext: false), Edge(Actor(2, "English")))));
        aggregator.Seed([Edge(Actor(1, "Japanese"))], Page(1, hasNext: true));

        await aggregator.CheckForMoreAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(aggregator.Items.Select(a => a.Id), Is.EqualTo([1, 2]));
        Assert.That(aggregator.HasMore, Is.False);
    }

    [Test]
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

        await aggregator.CheckForMoreAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(fetchedPages, Is.EqualTo([2, 3]));
        Assert.That(aggregator.Items.Select(a => a.Id), Is.EqualTo([1, 2]));
    }

    [Test]
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

        await aggregator.CheckForMoreAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(fetchedPages, Is.EqualTo([2, 3])); // walked exactly the cap
        Assert.That(aggregator.Items, Has.Exactly(1).Items);     // surfaced nobody new
        Assert.That(aggregator.HasMore, Is.True);     // more remains to search next tap
    }

    [Test]
    public async Task CheckForMoreAsync_MediaExhausted_SetsHasMoreFalse()
    {
        var aggregator = new VoiceActorAggregator((page, _) =>
            Task.FromResult(Result(Page(2, hasNext: false), Edge(Actor(1, "Japanese"))))); // repeat, last page
        aggregator.Seed([Edge(Actor(1, "Japanese"))], Page(1, hasNext: true));

        await aggregator.CheckForMoreAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(aggregator.HasMore, Is.False);
    }

    [Test]
    public async Task CheckForMoreAsync_NoNextPage_DoesNotFetch()
    {
        var fetched = false;
        var aggregator = new VoiceActorAggregator((page, _) =>
        {
            fetched = true;
            return Task.FromResult(Result(Page(2, hasNext: false), Edge(Actor(9, "English"))));
        });
        aggregator.Seed([Edge(Actor(1, "Japanese"))], Page(1, hasNext: false));

        await aggregator.CheckForMoreAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(fetched, Is.False);
    }

    [Test]
    public void Reset_AfterSeed_ClearsItemsAndPagingState()
    {
        var aggregator = new VoiceActorAggregator((_, _) => throw new InvalidOperationException());
        aggregator.Seed([Edge(Actor(1, "Japanese"))], Page(1, hasNext: true));

        aggregator.Reset();

        Assert.That(aggregator.Items, Is.Empty);
        Assert.That(aggregator.HasMore, Is.False);
    }
}
