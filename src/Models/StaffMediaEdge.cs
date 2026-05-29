namespace AniSprinkles.Models;

public class StaffMediaEdge
{
    public RelatedMedia? Node { get; set; }
    public string? StaffRole { get; set; }
    public ItemMetricBadge? MetricBadge { get; set; }
    public bool HasMetricBadge => MetricBadge is not null;
}
