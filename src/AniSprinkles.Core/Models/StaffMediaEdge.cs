using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Models;

public partial class StaffMediaEdge : ObservableObject, IDisplayProjection
{
    public RelatedMedia? Node { get; set; }
    public string? StaffRole { get; set; }

    // Observable: this list's badge is sort-dependent and gets re-stamped in place on a local-sort change.
    // Without change notification the CollectionView's recycled cells keep the previous sort's badge.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetricBadge))]
    private ItemMetricBadge? _metricBadge;

    public bool HasMetricBadge => MetricBadge is not null;

    /// <inheritdoc />
    /// <remarks>
    /// Re-raising <c>Node</c> is what makes the nested <c>Node.DisplayTitle</c> binding re-resolve;
    /// <c>RelatedMedia</c> is a plain class and cannot notify for itself.
    /// </remarks>
    public void RefreshDisplayProjections() => OnPropertyChanged(nameof(Node));
}
