using AniSprinkles.Utilities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AniSprinkles.Models;

public class MediaRecommendationNode : ObservableObject, IDisplayProjection
{
    public int? Rating { get; set; }
    public RelatedMedia? MediaRecommendation { get; set; }

    public bool HasRating => Rating is > 0;
    public string RatingDisplay => MetricFormat.Compact(Rating);

    // The card's metric badge (recommendation rating), stamped by the PageModel; always shown (0 when none).
    public ItemMetricBadge? MetricBadge { get; set; }
    public bool HasMetricBadge => MetricBadge is not null;

    /// <inheritdoc />
    /// <remarks>
    /// Re-raising <c>MediaRecommendation</c> is what makes the nested
    /// <c>MediaRecommendation.DisplayTitle</c> binding re-resolve; <c>RelatedMedia</c> is a plain
    /// class and cannot notify for itself.
    /// </remarks>
    public void RefreshDisplayProjections() => OnPropertyChanged(nameof(MediaRecommendation));
}
