using AniSprinkles.Models;
using AniSprinkles.PageModels;

namespace AniSprinkles.UnitTests;

public class PaginatedSectionTests
{
    private sealed record Item(int Id);

    private static PageInfo Page(int current, bool hasNext) =>
        new() { CurrentPage = current, HasNextPage = hasNext };

    private static (IReadOnlyList<Item> Items, PageInfo? PageInfo) Result(PageInfo info, params int[] ids) =>
        (ids.Select(i => new Item(i)).ToList(), info);

    private static PaginatedSection<Item> Section(
        PaginatedSection<Item>.FetchPageDelegate fetch,
        Action<IReadOnlyList<Item>, string>? onItemsAdded = null,
        PaginatedSection<Item>.LocalSortDelegate? localSort = null)
        => new("POPULARITY_DESC", fetch, item => item.Id, onItemsAdded, localSort);

    [Test]
    public void Seed_WithItemsAndPageInfo_PopulatesItemsAndPagingState()
    {
        var section = Section((_, _, _) => throw new InvalidOperationException("should not fetch"));

        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));

        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([1, 2]));
        Assert.That(section.HasNextPage, Is.True);
    }

    [Test]
    public async Task LoadMoreAsync_NextPageOverlapsSeed_AppendsDeduped()
    {
        var section = Section((page, _, _) =>
            Task.FromResult(Result(Page(2, hasNext: false), 2, 3))); // 2 overlaps the seed
        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));

        await section.LoadMoreAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([1, 2, 3]));
        Assert.That(section.HasNextPage, Is.False);
    }

    [Test]
    public async Task LoadMoreAsync_WithNoNextPage_DoesNotFetch()
    {
        var fetched = false;
        var section = Section((_, _, _) =>
        {
            fetched = true;
            return Task.FromResult(Result(Page(2, hasNext: false), 9));
        });
        section.Seed([new Item(1)], Page(1, hasNext: false));

        await section.LoadMoreAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(fetched, Is.False);
        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([1]));
    }

    [Test]
    public async Task LoadMoreAsync_FetchCancelled_DropsWithoutAppendingOrThrowing()
    {
        // A page-scoped cancellation (navigate-away) surfaces as OperationCanceledException from the fetch.
        // The whole cancel-on-nav-away feature relies on this dropping cleanly instead of propagating.
        var section = Section((_, _, _) => throw new OperationCanceledException());
        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));

        await section.LoadMoreAsync(TestContext.CurrentContext.CancellationToken); // must not throw

        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([1, 2])); // nothing appended
        Assert.That(section.IsLoadingMore, Is.False);                   // busy state reset
        Assert.That(section.HasNextPage, Is.True);                      // paging cursor untouched
    }

    [Test]
    public async Task ChangeSortAsync_FetchCancelled_KeepsCurrentSortAndItems()
    {
        var section = Section((_, _, _) => throw new OperationCanceledException());
        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));

        await section.ChangeSortAsync("SCORE_DESC", TestContext.CurrentContext.CancellationToken); // must not throw

        Assert.That(section.Sort, Is.EqualTo("POPULARITY_DESC"));         // sort NOT committed on a cancelled refetch
        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([1, 2])); // items unchanged
        Assert.That(section.IsChangingSort, Is.False);                  // busy state reset
    }

    [Test]
    public async Task ChangeSortAsync_NewSort_RefetchesPageOneAndReplaces()
    {
        var section = Section((page, sort, _) =>
            Task.FromResult(Result(Page(1, hasNext: false), 5, 6)));
        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));

        await section.ChangeSortAsync("SCORE_DESC", TestContext.CurrentContext.CancellationToken);

        Assert.That(section.Sort, Is.EqualTo("SCORE_DESC"));
        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([5, 6]));
        Assert.That(section.HasNextPage, Is.False);
    }

    [Test]
    public async Task ChangeSortAsync_SupersededBySecondChange_DropsStaleResponse()
    {
        var gateA = new TaskCompletionSource<(IReadOnlyList<Item>, PageInfo?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var section = Section((page, sort, _) => sort == "A"
            ? gateA.Task
            : Task.FromResult(Result(Page(1, hasNext: false), 10, 11)));
        section.Seed([new Item(1)], Page(1, hasNext: true));

        var slow = section.ChangeSortAsync("A", TestContext.CurrentContext.CancellationToken); // parks on gateA
        await section.ChangeSortAsync("B", TestContext.CurrentContext.CancellationToken);      // supersedes A

        Assert.That(section.Sort, Is.EqualTo("B"));
        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([10, 11]));

        gateA.SetResult(Result(Page(1, hasNext: true), 99));
        await slow; // resumes, sees it was superseded, applies nothing

        Assert.That(section.Sort, Is.EqualTo("B"));
        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([10, 11]));
    }

    [Test]
    public async Task LoadMoreAsync_SupersededBySortChange_DropsResult()
    {
        var gate = new TaskCompletionSource<(IReadOnlyList<Item>, PageInfo?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var section = Section((page, sort, _) => page == 2
            ? gate.Task
            : Task.FromResult(Result(Page(1, hasNext: false), 20, 21)));
        section.Seed([new Item(1)], Page(1, hasNext: true));

        var slowLoad = section.LoadMoreAsync(TestContext.CurrentContext.CancellationToken); // page 2, parked
        await section.ChangeSortAsync("B", TestContext.CurrentContext.CancellationToken);     // supersedes the load

        gate.SetResult(Result(Page(2, hasNext: true), 2, 3));
        await slowLoad;

        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([20, 21])); // page-2 items never appended
    }

    [Test]
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

        var first = section.LoadMoreAsync(TestContext.CurrentContext.CancellationToken);
        var second = section.LoadMoreAsync(TestContext.CurrentContext.CancellationToken); // ignored while the first is in flight
        await second;

        Assert.That(calls, Is.EqualTo(1));

        gate.SetResult(Result(Page(2, hasNext: false), 2));
        await first;
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void Reset_AfterSeed_ClearsItemsAndPagingState()
    {
        var section = Section((_, _, _) => throw new InvalidOperationException());
        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));

        section.Reset();

        Assert.That(section.Items, Is.Empty);
        Assert.That(section.HasNextPage, Is.False);
    }

    [Test]
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

        await section.ChangeSortAsync("SCORE_DESC", TestContext.CurrentContext.CancellationToken);

        Assert.That(fetchCalls, Is.EqualTo(0));                                  // no API call
        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([3, 2, 1]));     // reordered by the local sorter
        Assert.That(section.Sort, Is.EqualTo("SCORE_DESC"));                     // sort committed
        Assert.That(section.IsBusy, Is.False);                                 // never shows a spinner
        Assert.That(stamped, Is.EqualTo(["SCORE_DESC"]));                        // badges re-stamped for the new sort
    }

    [Test]
    public async Task ChangeSortAsync_OnPartialSet_RefetchesEvenWhenLocalSorterPresent()
    {
        var fetchedSorts = new List<string>();
        var section = Section(
            (page, sort, _) => { fetchedSorts.Add(sort); return Task.FromResult(Result(Page(1, hasNext: false), 5, 6)); },
            localSort: (sort, items) => items.Reverse().ToList());
        section.Seed([new Item(1)], Page(1, hasNext: true)); // partial — more pages exist on the server

        await section.ChangeSortAsync("SCORE_DESC", TestContext.CurrentContext.CancellationToken);

        Assert.That(fetchedSorts, Is.EqualTo(["SCORE_DESC"]));                   // server refetch, not a local reorder
        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([5, 6]));
    }

    [Test]
    public async Task ChangeSortAsync_CompleteSetButNoLocalSorter_FallsBackToRefetch()
    {
        var fetchCalls = 0;
        var section = Section((page, sort, _) => { fetchCalls++; return Task.FromResult(Result(Page(1, hasNext: false), 5, 6)); });
        section.Seed([new Item(1)], Page(1, hasNext: false));

        await section.ChangeSortAsync("SCORE_DESC", TestContext.CurrentContext.CancellationToken);

        Assert.That(fetchCalls, Is.EqualTo(1)); // without a local sorter, a complete set still refetches
        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([5, 6]));
    }

    [Test]
    public async Task Reset_AfterSortChange_RestoresInitialSort()
    {
        var section = Section((page, sort, _) => Task.FromResult(Result(Page(1, hasNext: false), 5, 6)));
        section.Seed([new Item(1)], Page(1, hasNext: true));
        await section.ChangeSortAsync("SCORE_DESC", TestContext.CurrentContext.CancellationToken);
        Assert.That(section.Sort, Is.EqualTo("SCORE_DESC"));

        section.Reset(); // e.g. a reused section loading a new entity

        // Cursor must return to the default so it matches the default-sorted re-seed + reset chip.
        Assert.That(section.Sort, Is.EqualTo("POPULARITY_DESC"));
    }

    [Test]
    public async Task LoadMoreAsync_WithStampHook_InvokesHookWithAddedItems()
    {
        var stamped = new List<(int Count, string Sort)>();
        var section = Section(
            (page, sort, _) => Task.FromResult(Result(Page(2, hasNext: false), 3)),
            onItemsAdded: (items, sort) => stamped.Add((items.Count, sort)));

        section.Seed([new Item(1), new Item(2)], Page(1, hasNext: true));
        await section.LoadMoreAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(stamped.Count, Is.EqualTo(2));
        Assert.That(stamped[0], Is.EqualTo((2, "POPULARITY_DESC")));
        Assert.That(stamped[1], Is.EqualTo((1, "POPULARITY_DESC")));
    }

    [Test]
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

        var sortTask = section.ChangeSortAsync("SCORE_DESC", TestContext.CurrentContext.CancellationToken); // in flight
        Assert.That(section.IsBusy, Is.True);

        await section.LoadMoreAsync(TestContext.CurrentContext.CancellationToken); // must be a no-op during the sort

        Assert.That(fetchedPages, Does.Not.Contain(2)); // Load More never fetched the next page

        sortGate.SetResult(Result(Page(1, hasNext: false), 5, 6));
        await sortTask;

        Assert.That(section.Items.Select(i => i.Id), Is.EqualTo([5, 6])); // only the new-sort page, no stale items
    }

    [Test]
    public async Task ChangeSortAsync_WhileRefetching_ReportsBusyThenClears()
    {
        var gate = new TaskCompletionSource<(IReadOnlyList<Item>, PageInfo?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var section = Section((page, sort, _) => gate.Task);
        section.Seed([new Item(1)], Page(1, hasNext: true));
        Assert.That(section.IsBusy, Is.False);

        var changing = section.ChangeSortAsync("SCORE_DESC", TestContext.CurrentContext.CancellationToken);
        Assert.That(section.IsBusy, Is.True); // spinner should be showing during the refetch

        gate.SetResult(Result(Page(1, hasNext: false), 2));
        await changing;

        Assert.That(section.IsBusy, Is.False);
    }

    // CanLoadMore is the LoadMore command's CanExecute guard: it must be true only when another page
    // exists and no fetch/sort is in flight, so the CollectionView's scroll threshold can't re-invoke
    // Load More while busy or once fully paged.

    [Test]
    public void CanLoadMore_AfterSeedWithNextPage_IsTrue()
    {
        var section = Section((_, _, _) => throw new InvalidOperationException("should not fetch"));

        section.Seed([new Item(1)], Page(1, hasNext: true));

        Assert.That(section.CanLoadMore, Is.True);
    }

    [Test]
    public void CanLoadMore_AfterSeedWithoutNextPage_IsFalse()
    {
        var section = Section((_, _, _) => throw new InvalidOperationException("should not fetch"));

        section.Seed([new Item(1)], Page(1, hasNext: false));

        Assert.That(section.CanLoadMore, Is.False);
    }

    [Test]
    public async Task CanLoadMore_AfterLastPageLoaded_IsFalse()
    {
        var section = Section((page, _, _) => Task.FromResult(Result(Page(2, hasNext: false), 2)));
        section.Seed([new Item(1)], Page(1, hasNext: true));
        Assert.That(section.CanLoadMore, Is.True);

        await section.LoadMoreAsync(TestContext.CurrentContext.CancellationToken);

        Assert.That(section.CanLoadMore, Is.False); // server is exhausted
    }

    [Test]
    public async Task CanLoadMore_WhileLoadMoreInFlight_IsFalseThenTrueAfter()
    {
        var gate = new TaskCompletionSource<(IReadOnlyList<Item>, PageInfo?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var section = Section((page, _, _) => gate.Task);
        section.Seed([new Item(1)], Page(1, hasNext: true));

        var load = section.LoadMoreAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(section.CanLoadMore, Is.False); // busy fetching the next page

        gate.SetResult(Result(Page(2, hasNext: true), 2));
        await load;

        Assert.That(section.CanLoadMore, Is.True); // free again, and another page still exists
    }

    [Test]
    public async Task CanLoadMore_WhileSortChangeInFlight_IsFalse()
    {
        var gate = new TaskCompletionSource<(IReadOnlyList<Item>, PageInfo?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var section = Section((page, _, _) => gate.Task);
        section.Seed([new Item(1)], Page(1, hasNext: true));

        var sort = section.ChangeSortAsync("SCORE_DESC", TestContext.CurrentContext.CancellationToken);
        Assert.That(section.CanLoadMore, Is.False); // busy on the sort refetch

        gate.SetResult(Result(Page(1, hasNext: true), 2));
        await sort;

        Assert.That(section.CanLoadMore, Is.True);
    }
}
