namespace AniSprinkles.PageModels;

public enum DiscoverSection
{
    Airing,
    Trending,
    Top,
    TopMovies,
    AllTimePopular,
    Upcoming,
    PopularAdult,
    TopRatedAdult,
}

public enum DiscoverSeasonKind
{
    None,
    Current,
    Next,
}

/// <summary>
/// Everything that varies per Discover section. <see cref="Sort"/> is both the AniList primary
/// sort and the <see cref="MediaMetricBadges.ForMediaSort"/> badge key, so each row's cards show
/// the metric the row is sorted by. <see cref="AdultFilter"/>: false = SFW-only, true = 18+-only,
/// null = follow the user's adult-content toggle (omit the filter when the toggle is on).
/// <see cref="Format"/> pins an AniList MediaFormat (Top Movies); null = all formats.
/// </summary>
public sealed record DiscoverSectionDefinition(
    DiscoverSection Section,
    string Title,
    string Sort,
    string? Status,
    DiscoverSeasonKind SeasonKind,
    bool ShowsRank,
    bool? AdultFilter,
    string? Format = null);

/// <summary>
/// Single source of truth for the Discover rows and their View All pages — only the
/// <see cref="DiscoverSection"/> name travels through the Shell route; everything else
/// is derived here. The order below is the on-page row order.
/// </summary>
public static class DiscoverSectionDefinitions
{
    public static readonly IReadOnlyList<DiscoverSectionDefinition> All =
    [
        new(DiscoverSection.Airing, "Currently Airing", "POPULARITY_DESC", "RELEASING", DiscoverSeasonKind.Current, ShowsRank: false, AdultFilter: null),
        new(DiscoverSection.Trending, "Trending Now", "TRENDING_DESC", null, DiscoverSeasonKind.None, ShowsRank: false, AdultFilter: null),
        new(DiscoverSection.Top, "Top Anime", "SCORE_DESC", null, DiscoverSeasonKind.None, ShowsRank: true, AdultFilter: null),
        new(DiscoverSection.TopMovies, "Top Movies", "SCORE_DESC", null, DiscoverSeasonKind.None, ShowsRank: true, AdultFilter: null, Format: "MOVIE"),
        new(DiscoverSection.AllTimePopular, "All Time Popular", "POPULARITY_DESC", null, DiscoverSeasonKind.None, ShowsRank: false, AdultFilter: null),
        new(DiscoverSection.Upcoming, "Upcoming Next Season", "POPULARITY_DESC", "NOT_YET_RELEASED", DiscoverSeasonKind.Next, ShowsRank: false, AdultFilter: null),
        // The 18+ pair only exists (row, View All, query alias) when the adult toggle is on.
        new(DiscoverSection.PopularAdult, "Popular 18+", "POPULARITY_DESC", null, DiscoverSeasonKind.None, ShowsRank: false, AdultFilter: true),
        new(DiscoverSection.TopRatedAdult, "Top Rated 18+", "SCORE_DESC", null, DiscoverSeasonKind.None, ShowsRank: true, AdultFilter: true),
    ];

    public static DiscoverSectionDefinition Get(DiscoverSection section) =>
        All.First(definition => definition.Section == section);
}
