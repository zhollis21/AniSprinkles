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
        Action<IReadOnlyList<Item>, string>? onItemsAdded = null,
        PaginatedSection<Item>.LocalSortDelegate? localSort = null)
        => new("POPULARITY_DESC", fetch, item => item.Id, onItemsAdded, localSort);

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
    public async Task ChangeSortAsync_OnCompleteSet_SortsLocallyWithoutFetching()
    {
        var fetchCalls = 0;
        var stamped = new List<string>();
        var section = Section(
            (page, sort, _) => { fetchCalls++; return Task.FromResult(Result(Page(1, hasNext: false), 99)); },
            onItemsAdded: (items, sort) => stamped.Add(sort),
            localSort: (sort, items) => items.Reverse().ToList());
        section.Seed([new Item(1), new Item(2), new Item(3)], Page(1, hasNext: false)); // complete set
        stamped.Clear(); // drop the seed stamp

        await section.ChangeSortAsync("SCORE_DESC", TestContext.Current.CancellationToken);

        Assert.Equal(0, fetchCalls);                                  // no API call
        Assert.Equal([3, 2, 1], section.Items.Select(i => i.Id));     // reordered by the local sorter
        Assert.Equal("SCORE_DESC", section.Sort);                     // sort committed
        Assert.False(section.IsBusy);                                 // never shows a spinner
        Assert.Equal(["SCORE_DESC"], stamped);                        // badges re-stamped for the new sort
    }

    [Fact]
    public async Task ChangeSortAsync_OnPartialSet_RefetchesEvenWhenLocalSorterPresent()
    {
        var fetchedSorts = new List<string>();
        var section = Section(
            (page, sort, _) => { fetchedSorts.Add(sort); return Task.FromResult(Result(Page(1, hasNext: false), 5, 6)); },
            localSort: (sort, items) => items.Reverse().ToList());
        section.Seed([new Item(1)], Page(1, hasNext: true)); // partial — more pages exist on the server

        await section.ChangeSortAsync("SCORE_DESC", TestContext.Current.CancellationToken);

        Assert.Equal(["SCORE_DESC"], fetchedSorts);                   // server refetch, not a local reorder
        Assert.Equal([5, 6], section.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task ChangeSortAsync_CompleteSetButNoLocalSorter_FallsBackToRefetch()
    {
        var fetchCalls = 0;
        var section = Section((page, sort, _) => { fetchCalls++; return Task.FromResult(Result(Page(1, hasNext: false), 5, 6)); });
        section.Seed([new Item(1)], Page(1, hasNext: false));

        await section.ChangeSortAsync("SCORE_DESC", TestContext.Current.CancellationToken);

        Assert.Equal(1, fetchCalls); // without a local sorter, a complete set still refetches
        Assert.Equal([5, 6], section.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Reset_AfterSortChange_RestoresInitialSort()
    {
        var section = Section((page, sort, _) => Task.FromResult(Result(Page(1, hasNext: false), 5, 6)));
        section.Seed([new Item(1)], Page(1, hasNext: true));
        await section.ChangeSortAsync("SCORE_DESC", TestContext.Current.CancellationToken);
        Assert.Equal("SCORE_DESC", section.Sort);

        section.Reset(); // e.g. a reused section loading a new entity

        // Cursor must return to the default so it matches the default-sorted re-seed + reset chip.
        Assert.Equal("POPULARITY_DESC", section.Sort);
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

    [Fact]
    public async Task LoadMoreAsync_WhileSortChangeInFlight_DoesNotFetchOrAppendStaleItems()
    {
        var sortGate = new TaskCompletionSource<(IReadOnlyList<Item>, PageInfo?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fetchedPages = new List<int>();
        var section = Section((page, sort, _) =>
        {
            fetchedPages.Add(page);
            // Page 1 = the sort refetch (held open); any other page = a Load More fetch.
            return page == 1 ? sortGate.Task : Task.FromResult(Result(Page(page, hasNext: false), 99));
        });
        section.Seed([new Item(1)], Page(1, hasNext: true));

        var sortTask = section.ChangeSortAsync("SCORE_DESC", TestContext.Current.CancellationToken); // in flight
        Assert.True(section.IsBusy);

        await section.LoadMoreAsync(TestContext.Current.CancellationToken); // must be a no-op during the sort

        Assert.DoesNotContain(2, fetchedPages); // Load More never fetched the next page

        sortGate.SetResult(Result(Page(1, hasNext: false), 5, 6));
        await sortTask;

        Assert.Equal([5, 6], section.Items.Select(i => i.Id)); // only the new-sort page, no stale items
    }

    [Fact]
    public async Task ChangeSortAsync_WhileRefetching_ReportsBusyThenClears()
    {
        var gate = new TaskCompletionSource<(IReadOnlyList<Item>, PageInfo?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var section = Section((page, sort, _) => gate.Task);
        section.Seed([new Item(1)], Page(1, hasNext: true));
        Assert.False(section.IsBusy);

        var changing = section.ChangeSortAsync("SCORE_DESC", TestContext.Current.CancellationToken);
        Assert.True(section.IsBusy); // spinner should be showing during the refetch

        gate.SetResult(Result(Page(1, hasNext: false), 2));
        await changing;

        Assert.False(section.IsBusy);
    }
}
