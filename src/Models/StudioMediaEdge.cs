using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Models;

public partial class StudioMediaEdge : ObservableObject
{
    public RelatedMedia? Node { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetricBadge))]
    private ItemMetricBadge? _metricBadge;

    public bool HasMetricBadge => MetricBadge is not null;
}
