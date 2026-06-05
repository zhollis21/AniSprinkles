namespace AniSprinkles.Models;

public class MediaRelationEdge
{
    public string? RelationType { get; set; }
    public RelatedMedia? Node { get; set; }

    // The card's metric badge (year), stamped by the PageModel; null when absent.
    public ItemMetricBadge? MetricBadge { get; set; }
    public bool HasMetricBadge => MetricBadge is not null;
}
