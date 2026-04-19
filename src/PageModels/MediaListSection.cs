using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AniSprinkles.PageModels;

public class MediaListSection : ObservableCollection<MediaListEntry>
{
    private readonly List<MediaListEntry> _allItems = [];
    private bool _isExpanded;
    private bool _suppressNotifications;
    private SortField _sortField = SortField.LastUpdated;
    private bool _sortAscending;
    private string _filterText = string.Empty;
    private int _filteredCount;
    // Bulk-update scope: lets the merger batch many per-section mutations into a single
    // UpdateItems() (and therefore a single NotifyCollectionChanged Reset) on scope exit.
    // Depth counter supports nested scopes; dirty flag skips the final UpdateItems when
    // nothing actually mutated between Begin and End.
    private int _bulkUpdateDepth;
    private bool _bulkUpdateDirty;

    public MediaListSection(string title, bool isExpanded)
    {
        Title = title;
        _isExpanded = isExpanded;
        _filteredCount = 0;
        ToggleCommand = new RelayCommand(ToggleExpanded);
    }

    public string Title { get; }

    public int TotalCount => _allItems.Count;

    /// <summary>
    /// Read-only view of every entry in this section's backing store, regardless of filter or expand state.
    /// Used by the pull-to-refresh merger to diff a section against a fresh AniList response.
    /// </summary>
    internal IReadOnlyList<MediaListEntry> AllItems => _allItems;

    /// <summary>
    /// Checks whether the entry exists in this section's backing store (not just visible items).
    /// </summary>
    public bool ContainsEntry(MediaListEntry entry) => _allItems.Contains(entry);

    /// <summary>
    /// Number of items matching the current filter. Equals <see cref="TotalCount"/> when no filter is active.
    /// </summary>
    public int FilteredCount
    {
        get => _filteredCount;
        private set
        {
            if (_filteredCount == value)
            {
                return;
            }

            _filteredCount = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(FilteredCount)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(DisplayCount)));
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsFiltered)));
        }
    }

    /// <summary>
    /// Display string for the badge: shows "filtered / total" when a filter is active, otherwise just total.
    /// </summary>
    public string DisplayCount => IsFiltered ? $"{FilteredCount}/{TotalCount}" : TotalCount.ToString();

    /// <summary>
    /// Whether the current filter is reducing the visible item count.
    /// </summary>
    public bool IsFiltered => FilteredCount != TotalCount;

    public IRelayCommand ToggleCommand { get; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsExpanded)));
            if (_bulkUpdateDepth > 0)
            {
                _bulkUpdateDirty = true;
            }
            else
            {
                UpdateItems();
            }
        }
    }

    public bool RemoveItem(MediaListEntry entry)
    {
        var removed = _allItems.Remove(entry);
        if (removed)
        {
            OnPropertyChanged(new PropertyChangedEventArgs(nameof(TotalCount)));
            if (_bulkUpdateDepth > 0)
            {
                _bulkUpdateDirty = true;
            }
            else
            {
                UpdateItems();
            }
        }

        return removed;
    }

    public void AddItem(MediaListEntry entry)
    {
        _allItems.Add(entry);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(TotalCount)));

        if (_bulkUpdateDepth > 0)
        {
            _bulkUpdateDirty = true;
            return;
        }

        if (IsExpanded)
        {
            Add(entry);
        }
    }

    public void AddItems(IEnumerable<MediaListEntry> entries)
    {
        var list = entries as IList<MediaListEntry> ?? entries.ToList();
        if (list.Count == 0)
        {
            return;
        }

        _allItems.AddRange(list);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(TotalCount)));

        if (_bulkUpdateDepth > 0)
        {
            _bulkUpdateDirty = true;
            return;
        }

        if (IsExpanded)
        {
            ReplaceItems(_allItems);
        }
    }

    /// <summary>
    /// Begin a bulk-update scope. While the returned scope is alive, calls to
    /// <see cref="AddItem"/>, <see cref="AddItems"/>, <see cref="RemoveItem"/>,
    /// <see cref="ApplySort"/>, and <see cref="ApplyFilter"/> mark the section dirty
    /// but skip the per-call <c>UpdateItems()</c>. On <see cref="IDisposable.Dispose"/>
    /// of the outermost scope, a single <c>UpdateItems()</c> runs — producing one
    /// Reset instead of one per mutation. Nested scopes are supported via a depth
    /// counter; only the outermost Dispose triggers the rebuild.
    /// </summary>
    internal IDisposable BeginBulkUpdate()
    {
        _bulkUpdateDepth++;
        return new BulkUpdateScope(this);
    }

    private void EndBulkUpdate()
    {
        // Guard against Dispose running on a scope whose section was discarded via
        // DiscardBulkUpdate — without this, the depth counter would go negative and
        // the next BeginBulkUpdate would skip its deferred UpdateItems.
        if (_bulkUpdateDepth == 0)
        {
            return;
        }

        if (--_bulkUpdateDepth == 0 && _bulkUpdateDirty)
        {
            _bulkUpdateDirty = false;
            UpdateItems();
        }
    }

    /// <summary>
    /// Abandon any in-flight bulk-update scopes without running the deferred <see cref="UpdateItems"/>.
    /// Use when the section is about to be removed or otherwise discarded — e.g. the merger's Pass 2
    /// drops empty/stale sections, and running the final sort/filter pass on a section that's
    /// leaving the outer collection is pure waste.
    /// </summary>
    internal void DiscardBulkUpdate()
    {
        _bulkUpdateDepth = 0;
        _bulkUpdateDirty = false;
    }

    private sealed class BulkUpdateScope(MediaListSection owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            owner.EndBulkUpdate();
        }
    }

    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }

    private void UpdateItems()
    {
        var items = GetSortedFilteredItems();
        FilteredCount = items.Count;

        if (IsExpanded)
        {
            ReplaceItems(items);
        }
        else
        {
            Clear();
        }
    }

    /// <summary>
    /// Apply a new sort field and direction, then re-render visible items.
    /// </summary>
    public void ApplySort(SortField field, bool ascending)
    {
        _sortField = field;
        _sortAscending = ascending;
        if (_bulkUpdateDepth > 0)
        {
            _bulkUpdateDirty = true;
        }
        else
        {
            UpdateItems();
        }
    }

    /// <summary>
    /// Apply a text filter across display title. Empty string clears the filter.
    /// </summary>
    public void ApplyFilter(string text)
    {
        _filterText = text ?? string.Empty;
        if (_bulkUpdateDepth > 0)
        {
            _bulkUpdateDirty = true;
        }
        else
        {
            UpdateItems();
        }
    }

    private List<MediaListEntry> GetSortedFilteredItems()
    {
        IEnumerable<MediaListEntry> items = _allItems;

        if (!string.IsNullOrWhiteSpace(_filterText))
        {
            items = items.Where(e => MatchesFilter(e, _filterText));
        }

        items = _sortField switch
        {
            SortField.Title => _sortAscending
                ? items.OrderBy(e => e.Media?.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(e => e.Media?.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            SortField.Score => _sortAscending
                ? items.OrderBy(e => e.Score ?? -1)
                : items.OrderByDescending(e => e.Score ?? -1),
            SortField.AverageScore => _sortAscending
                ? items.OrderBy(e => e.Media?.AverageScore ?? -1)
                : items.OrderByDescending(e => e.Media?.AverageScore ?? -1),
            // SortField.LastUpdated
            _ => _sortAscending
                ? items.OrderBy(e => e.UpdatedAt ?? DateTimeOffset.MinValue)
                : items.OrderByDescending(e => e.UpdatedAt ?? DateTimeOffset.MinValue),
        };

        return items.ToList();
    }

    private static bool MatchesFilter(MediaListEntry entry, string text)
    {
        var title = entry.Media?.Title;
        if (title is null)
        {
            return false;
        }

        return title.English?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
            || title.Romaji?.Contains(text, StringComparison.OrdinalIgnoreCase) == true
            || title.Native?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Forces re-render of all visible items by rebuilding from <c>_allItems</c>.
    /// Use after mutating an entry's properties in-place (since entries don't
    /// implement INotifyPropertyChanged). A same-reference Replace via
    /// <c>this[index] = entry</c> can be optimised away by MAUI, so a full
    /// Reset is the reliable approach.
    /// </summary>
    public void RefreshVisibleItems()
    {
        if (IsExpanded)
        {
            var items = GetSortedFilteredItems();
            FilteredCount = items.Count;
            ReplaceItems(items);
        }
    }

    private void ReplaceItems(IEnumerable<MediaListEntry> items)
    {
        _suppressNotifications = true;
        ClearItems();
        foreach (var entry in items)
        {
            Add(entry);
        }

        _suppressNotifications = false;
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_suppressNotifications)
        {
            return;
        }

        base.OnCollectionChanged(e);
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (_suppressNotifications)
        {
            return;
        }

        base.OnPropertyChanged(e);
    }
}
