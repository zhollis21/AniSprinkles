using Microsoft.Maui.Graphics;

namespace AniSprinkles.PageModels;

/// <summary>
/// Builds the metric badge shown on a media list card for the active media sort. Shared by every
/// details list whose items are <see cref="RelatedMedia"/> sorted by the standard media sorts —
/// Studio productions, Staff production roles, and Character appearances.
///
/// When the active sort IS a metric, the badge always renders (with a 0/— fallback) so missing data
/// doesn't look broken; only non-metric sorts (e.g. Title) show no badge.
/// </summary>
public static class MediaMetricBadges
{
    public static ItemMetricBadge? ForMediaSort(RelatedMedia? media, string sort)
    {
        if (media is null)
        {
            return null;
        }

        return sort switch
        {
            "POPULARITY_DESC" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.People24,
                IconColor = Color.FromArgb("#FF9500"),
                Text = media.PopularityOrZero,
            },
            "SCORE_DESC" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Star24,
                IconColor = Color.FromArgb("#FFCC00"),
                Text = media.ScoreOrDash,
            },
            "FAVOURITES_DESC" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Heart24,
                IconColor = Color.FromArgb("#FF2D95"),
                Text = media.FavouritesOrZero,
            },
            "START_DATE_DESC" or "START_DATE" => new ItemMetricBadge
            {
                Glyph = FluentIconsRegular.Calendar24,
                IconColor = Color.FromArgb("#00C2FF"),
                Text = media.YearOrDash,
            },
            _ => null,
        };
    }
}
