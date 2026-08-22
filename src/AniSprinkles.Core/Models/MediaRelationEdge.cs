namespace AniSprinkles.Models;

public class MediaRelationEdge
{
    public string? RelationType { get; set; }
    public RelatedMedia? Node { get; set; }

    // The card's metric badge (year), stamped by the PageModel; always shown (— when no year).
    public ItemMetricBadge? MetricBadge { get; set; }
    public bool HasMetricBadge => MetricBadge is not null;
}
