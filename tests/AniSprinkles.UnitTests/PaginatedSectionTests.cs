using AniSprinkles.Models;
using AniSprinkles.PageModels;

namespace AniSprinkles.UnitTests;

public class PaginatedSectionTests
{
    private sealed record Item(int Id);

    private static PageInfo Page(int current, bool hasNext) =>
        new() { CurrentPage = current, HasNextPage = hasNext, LastPage = hasNext ? current + 1 : current };

    private static (IReadOnlyList<Item> Items, PageInfo? PageInfo) Result(PageInfo info, params int[] ids) =>
        (ids.Select(i => new Item(i)).ToList(), info);

    private static PaginatedSection<Item> Section(
        PaginatedSection<Item>.FetchPageDelegate fetch,
        Action<IReadOnlyList<Item>, string>? onItemsAdded = null)
        => new("POPULARITY_DESC", fetch, item => item.Id, onItemsAdded);

    [Fact]
    public void Seed_WithItemsAndPageInfo_PopulatesItemsAndPagingState()
    {
        var section = Section((_, _, _) => throw new InvalidOperationException("should not fetch"));

        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));

        Assert.Equal([1, 2], section.Items.Select(i => i.Id));
        Assert.True(section.HasNextPage);
    }

    [Fact]
    public async Task LoadMoreAsync_NextPageOverlapsSeed_AppendsDeduped()
    {
        var section = Section((page, _, _) =>
            Task.FromResult(Result(Page(2, hasNext: false), 2, 3))); // 2 overlaps the seed
        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));

        await section.LoadMoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal([1, 2, 3], section.Items.Select(i => i.Id));
        Assert.False(section.HasNextPage);
    }

    [Fact]
    public async Task LoadMoreAsync_WithNoNextPage_DoesNotFetch()
    {
        var fetched = false;
        var section = Section((_, _, _) =>
        {
            fetched = true;
            return Task.FromResult(Result(Page(2, hasNext: false), 9));
        });
        section.Seed([new Item(1)], Page(1, hasNext: false));

        await section.LoadMoreAsync(TestContext.Current.CancellationToken);

        Assert.False(fetched);
        Assert.Equal([1], section.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task ChangeSortAsync_NewSort_RefetchesPageOneAndReplaces()
    {
        var section = Section((page, sort, _) =>
            Task.FromResult(Result(Page(1, hasNext: false), 5, 6)));
        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));

        await section.ChangeSortAsync("SCORE_DESC", TestContext.Current.CancellationToken);

        Assert.Equal("SCORE_DESC", section.Sort);
        Assert.Equal([5, 6], section.Items.Select(i => i.Id));
        Assert.False(section.HasNextPage);
    }

    [Fact]
    public async Task ChangeSortAsync_SupersededBySecondChange_DropsStaleResponse()
    {
        var gateA = new TaskCompletionSource<(IReadOnlyList<Item>, PageInfo?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var section = Section((page, sort, _) => sort == "A"
            ? gateA.Task
            : Task.FromResult(Result(Page(1, hasNext: false), 10, 11)));
        section.Seed([new Item(1)], Page(1, hasNext: true));

        var slow = section.ChangeSortAsync("A", TestContext.Current.CancellationToken); // parks on gateA
        await section.ChangeSortAsync("B", TestContext.Current.CancellationToken);      // supersedes A

        Assert.Equal("B", section.Sort);
        Assert.Equal([10, 11], section.Items.Select(i => i.Id));

        gateA.SetResult(Result(Page(1, hasNext: true), 99));
        await slow; // resumes, sees it was superseded, applies nothing

        Assert.Equal("B", section.Sort);
        Assert.Equal([10, 11], section.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task LoadMoreAsync_SupersededBySortChange_DropsResult()
    {
        var gate = new TaskCompletionSource<(IReadOnlyList<Item>, PageInfo?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var section = Section((page, sort, _) => page == 2
            ? gate.Task
            : Task.FromResult(Result(Page(1, hasNext: false), 20, 21)));
        section.Seed([new Item(1)], Page(1, hasNext: true));

        var slowLoad = section.LoadMoreAsync(TestContext.Current.CancellationToken); // page 2, parked
        await section.ChangeSortAsync("B", TestContext.Current.CancellationToken);     // supersedes the load

        gate.SetResult(Result(Page(2, hasNext: true), 2, 3));
        await slowLoad;

        Assert.Equal([20, 21], section.Items.Select(i => i.Id)); // page-2 items never appended
    }

    [Fact]
    public async Task LoadMoreAsync_CalledWhileLoading_IgnoresSecondCall()
    {
        var calls = 0;
        var gate = new TaskCompletionSource<(IReadOnlyList<Item>, PageInfo?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var section = Section((page, sort, _) =>
        {
            calls++;
            return gate.Task;
        });
        section.Seed([new Item(1)], Page(1, hasNext: true));

        var first = section.LoadMoreAsync(TestContext.Current.CancellationToken);
        var second = section.LoadMoreAsync(TestContext.Current.CancellationToken); // ignored while the first is in flight
        await second;

        Assert.Equal(1, calls);

        gate.SetResult(Result(Page(2, hasNext: false), 2));
        await first;
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Reset_AfterSeed_ClearsItemsAndPagingState()
    {
        var section = Section((_, _, _) => throw new InvalidOperationException());
        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));

        section.Reset();

        Assert.Empty(section.Items);
        Assert.False(section.HasNextPage);
    }

    [Fact]
    public async Task LoadMoreAsync_WithStampHook_InvokesHookWithAddedItems()
    {
        var stamped = new List<(int Count, string Sort)>();
        var section = Section(
            (page, sort, _) => Task.FromResult(Result(Page(2, hasNext: false), 3)),
            onItemsAdded: (items, sort) => stamped.Add((items.Count, sort)));

        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));
        await section.LoadMoreAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, stamped.Count);
        Assert.Equal((2, "POPULARITY_DESC"), stamped[0]);
        Assert.Equal((1, "POPULARITY_DESC"), stamped[1]);
    }
}
