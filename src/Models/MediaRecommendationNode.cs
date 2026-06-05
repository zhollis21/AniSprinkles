using AniSprinkles.Utilities;

namespace AniSprinkles.Models;

public class MediaRecommendationNode
{
    public int? Rating { get; set; }
    public RelatedMedia? MediaRecommendation { get; set; }

    public bool HasRating => Rating is > 0;
    public string RatingDisplay => MetricFormat.Compact(Rating);

    // The card's metric badge (recommendation rating), stamped by the PageModel; null when absent.
    public ItemMetricBadge? MetricBadge { get; set; }
    public bool HasMetricBadge => MetricBadge is not null;
}
