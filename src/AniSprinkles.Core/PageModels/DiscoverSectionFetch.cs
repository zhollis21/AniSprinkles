using AniSprinkles.Utilities;

namespace AniSprinkles.PageModels;

/// <summary>
/// Fetches one BrowseAnime page for a Discover section — the single place that turns a
/// <see cref="DiscoverSectionDefinition"/> into query arguments (season math, adult-toggle
/// resolution, format pin). Shared by the Discover rows' Load More and the View All page so
/// the two paging paths can never drift apart.
/// </summary>
public static class DiscoverSectionFetch
{
    public static Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> PageAsync(
        IAniListClient client,
        TimeProvider timeProvider,
        DiscoverSectionDefinition definition,
        int page,
        int perPage,
        CancellationToken cancellationToken)
    {
        string? season = null;
        int? seasonYear = null;
        if (definition.SeasonKind != DiscoverSeasonKind.None)
        {
            var localNow = timeProvider.GetLocalNow();
            (season, seasonYear) = definition.SeasonKind == DiscoverSeasonKind.Current
                ? AniListSeason.Current(localNow)
                : AniListSeason.Next(localNow);
        }

        // Section-pinned filter (the 18+ pair) wins; otherwise follow the adult-content toggle
        // (false = SFW only, null = filter omitted so 18+ may mix in).
        var isAdult = definition.AdultFilter ?? (AppSettings.DisplayAdultContent ? null : (bool?)false);

        return client.BrowseAnimePageAsync(
            definition.Sort, definition.Status, season, seasonYear, isAdult, definition.Format,
            page, perPage, cancellationToken);
    }
}
