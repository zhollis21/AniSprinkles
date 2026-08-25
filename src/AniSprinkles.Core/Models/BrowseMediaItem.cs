using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.Models;

/// <summary>
/// One media item in a Discover carousel, View All list, or search-results list. Mirrors
/// <see cref="StudioMediaEdge"/> (a <see cref="RelatedMedia"/> node plus a presentation layer),
/// adding the Top-Anime rank and item-level list-status notification: <see cref="RelatedMedia"/>
/// is deliberately not observable, so after a long-press mutation the page model rewrites the
/// node's list fields and calls <see cref="NotifyListEntryChanged"/> to refresh the bound chip.
/// </summary>
public partial class BrowseMediaItem : ObservableObject, IDisplayProjection
{
    public RelatedMedia? Node { get; set; }

    /// <summary>1-based rank shown on the Top Anime View All list; 0 everywhere else (hidden).</summary>
    public int Rank { get; set; }

    public bool HasRank => Rank > 0;

    public string RankDisplay => HasRank ? $"#{Rank}" : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetricBadge))]
    private ItemMetricBadge? _metricBadge;

    public bool HasMetricBadge => MetricBadge is not null;

    public bool HasListStatus => Node?.HasListStatus is true;

    public string ListStatusDisplay => Node?.ListStatusDisplay ?? string.Empty;

    public Color ListStatusColor => Node?.ListStatusColor ?? Colors.Transparent;

    /// <summary>Raises change notifications for the list-status chip after the node's list fields are rewritten.</summary>
    public void NotifyListEntryChanged()
    {
        OnPropertyChanged(nameof(HasListStatus));
        OnPropertyChanged(nameof(ListStatusDisplay));
        OnPropertyChanged(nameof(ListStatusColor));
    }

    /// <summary>
    /// Reshapes the node's list snapshot into the <see cref="MediaListEntry"/> the long-press flows
    /// operate on. Id is 0 for not-on-list media (the add-to-list save creates the entry and adopts
    /// the server id). Media carries just enough for the popups: title, episode cap, format.
    /// </summary>
    public MediaListEntry ToListEntry()
    {
        var node = Node ?? throw new InvalidOperationException("BrowseMediaItem has no node.");
        return new MediaListEntry
        {
            Id = node.ListEntryId ?? 0,
            MediaId = node.Id,
            Status = node.ListStatus,
            Progress = node.ListProgress,
            Score = node.ListScore,
            Media = new Media
            {
                Id = node.Id,
                Title = node.Title,
                Format = node.Format,
                Status = node.Status,
                Episodes = node.Episodes,
                CoverImage = node.CoverImage,
            },
        };
    }

    /// <summary>Copies a successfully saved entry back onto the node and refreshes the chip.</summary>
    public void ApplyListEntry(MediaListEntry entry)
    {
        if (Node is null)
        {
            return;
        }

        Node.ListEntryId = entry.Id > 0 ? entry.Id : Node.ListEntryId;
        Node.ListStatus = entry.Status;
        Node.ListProgress = entry.Progress;
        Node.ListScore = entry.Score;
        NotifyListEntryChanged();
    }

    /// <summary>Clears the node's list snapshot after a delete and refreshes the chip.</summary>
    public void ClearListEntry()
    {
        if (Node is null)
        {
            return;
        }

        Node.ListEntryId = null;
        Node.ListStatus = null;
        Node.ListProgress = null;
        Node.ListScore = null;
        NotifyListEntryChanged();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Re-raising <c>Node</c> is what makes the nested <c>Node.DisplayTitle</c> binding re-resolve;
    /// <c>RelatedMedia</c> is a plain class and cannot notify for itself.
    /// </remarks>
    public void RefreshDisplayProjections() => OnPropertyChanged(nameof(Node));
}
