using System.Collections.ObjectModel;
using AniSprinkles.Models;

namespace AniSprinkles.PageModels;

/// <summary>
/// A lazy, server-side-sorted, paginated list section — the shared engine behind the details
/// pages' "Appears In", "Voice Roles", and "Production Roles" lists. It owns the loaded items,
/// the active sort, and the paging cursor, and exposes three operations: <see cref="Seed"/>
/// (from the heavy first-page query), <see cref="LoadMoreAsync"/>, and <see cref="ChangeSortAsync"/>.
///
/// It is deliberately MAUI-free (only BCL + models) so it can be link-compiled and unit-tested.
/// All the gnarly state lives here: dedup-on-append, re-entrancy guarding, stale-response dropping
/// (a sort change supersedes any in-flight fetch), and full reset. The owning page model is a thin
/// shell that binds to <see cref="Items"/> and reacts to <see cref="Changed"/>.
/// </summary>
public sealed class PaginatedSection<T>
{
    public delegate Task<(IReadOnlyList<T> Items, PageInfo? PageInfo)> FetchPageDelegate(
        int page, string sort, CancellationToken cancellationToken);

    /// <summary>Reorders an already-complete in-memory set for a new sort code (no API call).</summary>
    public delegate IReadOnlyList<T> LocalSortDelegate(string sort, IReadOnlyList<T> items);

    private readonly FetchPageDelegate _fetchPage;
    private readonly Func<T, object> _keySelector;
    private readonly Action<IReadOnlyList<T>, string>? _onItemsAdded;
    private readonly LocalSortDelegate? _localSort;
    private readonly string _initialSort;

    private readonly HashSet<object> _seenKeys = [];
    private int _currentPage;
    private int _generation;

    public PaginatedSection(
        string initialSort,
        FetchPageDelegate fetchPage,
        Func<T, object> keySelector,
        Action<IReadOnlyList<T>, string>? onItemsAdded = null,
        LocalSortDelegate? localSort = null)
    {
        _initialSort = initialSort;
        Sort = initialSort;
        _fetchPage = fetchPage;
        _keySelector = keySelector;
        _onItemsAdded = onItemsAdded;
        _localSort = localSort;
    }

    /// <summary>The items loaded so far. Bind XAML to this; the instance is stable for the section's life.</summary>
    public ObservableCollection<T> Items { get; } = [];

    public string Sort { get; private set; }

    public bool HasNextPage { get; private set; }

    public bool IsLoadingMore { get; private set; }

    public bool IsChangingSort { get; private set; }

    /// <summary>True while a sort refetch or Load More round-trip is in flight — bind a spinner to this.</summary>
    public bool IsBusy => IsLoadingMore || IsChangingSort;

    /// <summary>Raised after any change to <see cref="Sort"/>, <see cref="HasNextPage"/>, <see cref="IsLoadingMore"/>, or <see cref="IsChangingSort"/>.</summary>
    public event Action? Changed;

    /// <summary>Seeds the section with the first page returned by the heavy Character/Staff query.</summary>
    public void Seed(IReadOnlyList<T> items, PageInfo? pageInfo)
    {
        ResetState();
        AppendDeduped(items);
        _currentPage = pageInfo?.CurrentPage ?? 1;
        HasNextPage = pageInfo?.HasNextPage ?? false;
        Notify();
    }

    /// <summary>Fetches and appends the next page. No-op while busy (a Load More or a sort refetch is
    /// in flight) or fully paged.</summary>
    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        // Must include IsChangingSort (via IsBusy): a sort refetch reads the current page with the
        // OLD Sort, and since the sort already bumped the generation, a concurrent Load More would
        // pass its stale-check and append old-sort items into the freshly sorted list.
        if (IsBusy || !HasNextPage)
        {
            return;
        }

        IsLoadingMore = true;
        Notify();

        var generation = _generation;
        var nextPage = _currentPage + 1;
        try
        {
            var (items, pageInfo) = await _fetchPage(nextPage, Sort, cancellationToken).ConfigureAwait(true);
            if (generation != _generation)
            {
                return; // a sort change or reset superseded this fetch
            }

            AppendDeduped(items);
            _currentPage = nextPage;
            HasNextPage = pageInfo?.HasNextPage ?? false;
        }
        catch (OperationCanceledException)
        {
            // Page torn down mid-fetch — drop silently.
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoadingMore = false;
                Notify();
            }
        }
    }

    /// <summary>
    /// Changes the sort. When the whole set is already in memory (<see cref="HasNextPage"/> is false)
    /// this reorders <see cref="Items"/> locally — instant, no API call, no spinner. Otherwise it
    /// re-fetches page 1 with the new server-side sort; the new sort is only committed once that
    /// fetch succeeds, so a failed sort change leaves the existing list intact.
    /// </summary>
    public async Task ChangeSortAsync(string sort, CancellationToken cancellationToken = default)
    {
        if (string.Equals(sort, Sort, StringComparison.Ordinal))
        {
            return;
        }

        // Fast path: the complete set is loaded, so the server can't know anything we don't — reorder
        // in memory. (A partial set must refetch: the server sorts across pages we haven't loaded.)
        if (!HasNextPage && _localSort is not null)
        {
            ApplyLocalSort(sort);
            return;
        }

        var generation = ++_generation; // supersede any in-flight LoadMore
        IsLoadingMore = false;
        IsChangingSort = true;
        Notify();

        try
        {
            var (items, pageInfo) = await _fetchPage(1, sort, cancellationToken).ConfigureAwait(true);
            if (generation != _generation)
            {
                return; // a newer sort change won — it owns the busy state now
            }

            Sort = sort;
            ResetItems();
            AppendDeduped(items);
            _currentPage = pageInfo?.CurrentPage ?? 1;
            HasNextPage = pageInfo?.HasNextPage ?? false;
        }
        catch (OperationCanceledException)
        {
            // Page torn down mid-fetch — drop silently.
        }
        finally
        {
            if (generation == _generation)
            {
                IsChangingSort = false;
                Notify();
            }
        }
    }

    private void ApplyLocalSort(string sort)
    {
        // Synchronous, no fetch. Bump the generation to supersede any (defensive) in-flight op, then
        // reorder the in-memory set. _seenKeys is preserved — these are the same already-seen items.
        _generation++;
        IsLoadingMore = false;
        IsChangingSort = false;
        Sort = sort;

        var sorted = _localSort!(sort, Items.ToList());

        // Stamp badges BEFORE re-adding: the edge types don't raise PropertyChanged for MetricBadge,
        // so a binding that materializes on CollectionChanged(Add) would otherwise read the previous
        // sort's (stale) badge.
        _onItemsAdded?.Invoke(sorted, sort);
        Items.Clear();
        foreach (var item in sorted)
        {
            Items.Add(item);
        }

        Notify();
    }

    /// <summary>Clears everything and supersedes any in-flight fetch (used when loading a new id).</summary>
    public void Reset()
    {
        ResetState();
        Notify();
    }

    private void ResetState()
    {
        _generation++;
        ResetItems();
        _currentPage = 0;
        HasNextPage = false;
        IsLoadingMore = false;
        IsChangingSort = false;
        // Restore the default sort: a reused section is re-seeded with default-sorted page 1 and the
        // page model resets the chip to default, so the data cursor must match or Load More would
        // fetch with the previous entity's stale sort.
        Sort = _initialSort;
    }

    private void ResetItems()
    {
        Items.Clear();
        _seenKeys.Clear();
    }

    private void AppendDeduped(IReadOnlyList<T> items)
    {
        var added = new List<T>();
        foreach (var item in items)
        {
            if (_seenKeys.Add(_keySelector(item)))
            {
                added.Add(item);
            }
        }

        if (added.Count == 0)
        {
            return;
        }

        // Stamp badges before adding to the collection (see ApplyLocalSort): MetricBadge isn't an
        // observable property, so a binding must see the correct badge at CollectionChanged(Add) time.
        _onItemsAdded?.Invoke(added, Sort);
        foreach (var item in added)
        {
            Items.Add(item);
        }
    }

    private void Notify() => Changed?.Invoke();
}
