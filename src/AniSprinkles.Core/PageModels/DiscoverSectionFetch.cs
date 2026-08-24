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
    /// <param name="displayAdultContent">
    /// The adult-content policy the caller's current result set was seeded under — NOT a fresh read
    /// of <c>AppSettings</c> (#118). This used to resolve the static here, per page, so a commit
    /// landing mid-session made the next Load More fetch under the new policy and append it onto
    /// items fetched under the old one. Callers pin it at seed time, the way
    /// <c>SearchPageModel</c> does with <c>_seededDisplayAdult</c>, so one result set can never
    /// hold two policies.
    /// </param>
    public static Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> PageAsync(
        IAniListClient client,
        TimeProvider timeProvider,
        DiscoverSectionDefinition definition,
        bool displayAdultContent,
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

        // Section-pinned filter (the 18+ pair) wins; otherwise follow the seeded adult-content
        // policy (false = SFW only, null = filter omitted so 18+ may mix in).
        var isAdult = definition.AdultFilter ?? (displayAdultContent ? null : (bool?)false);

        return client.BrowseAnimePageAsync(
            definition.Sort, definition.Status, season, seasonYear, isAdult, definition.Format,
            page, perPage, cancellationToken);
    }
}
