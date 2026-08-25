using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Models;

public partial class StudioMediaEdge : ObservableObject, IDisplayProjection
{
    public RelatedMedia? Node { get; set; }

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
