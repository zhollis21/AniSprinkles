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
            UpdateItems();
        }
    }

    public void AddItem(MediaListEntry entry)
    {
        _allItems.Add(entry);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(TotalCount)));

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

        if (IsExpanded)
        {
            ReplaceItems(_allItems);
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
        UpdateItems();
    }

    /// <summary>
    /// Apply a text filter across display title. Empty string clears the filter.
    /// </summary>
    public void ApplyFilter(string text)
    {
        _filterText = text ?? string.Empty;
        UpdateItems();
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(TotalCount)));
    }

    private List<MediaListEntry> GetSortedFilteredItems()
    {
        IEnumerable<MediaListEntry> items = _allItems;

        if (!string.IsNullOrWhiteSpace(_filterText))
        {
            items = items.Where(e =>
                e.Media?.DisplayTitle?.Contains(_filterText, StringComparison.OrdinalIgnoreCase) == true);
        }

        items = _sortField switch
        {
            SortField.Title => _sortAscending
                ? items.OrderBy(e => e.Media?.DisplayTitle, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(e => e.Media?.DisplayTitle, StringComparer.OrdinalIgnoreCase),
            SortField.Score => _sortAscending
                ? items.OrderBy(e => e.Score ?? -1)
                : items.OrderByDescending(e => e.Score ?? -1),
            SortField.Progress => _sortAscending
                ? items.OrderBy(e => e.Progress ?? -1)
                : items.OrderByDescending(e => e.Progress ?? -1),
            // SortField.LastUpdated
            _ => _sortAscending
                ? items.OrderBy(e => e.UpdatedAt ?? DateTimeOffset.MinValue)
                : items.OrderByDescending(e => e.UpdatedAt ?? DateTimeOffset.MinValue),
        };

        return items.ToList();
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
