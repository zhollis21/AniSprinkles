namespace AniSprinkles.Services;

/// <summary>Entity type for the AniList <c>ToggleFavourite</c> mutation.</summary>
public enum FavouriteKind
{
    Anime,
    Manga,
    Character,
    Staff,
    Studio,
}

public interface IAniListClient
{
    /// <summary>
    /// Drops any cached character/staff/studio reads so the next fetch comes from AniList (#130).
    /// <para>
    /// Needed because names render from <c>userPreferred</c>, which AniList resolves server-side
    /// against the viewer's Staff Name Language and which is therefore fixed at fetch time. Without
    /// this, changing the setting would appear to do nothing for the rest of the session:
    /// <see cref="CachingAniListClient"/> holds those reads for the process lifetime, so even
    /// navigating away and back would re-serve the old rendering.
    /// </para>
    /// <para>
    /// Invalidate-only, deliberately — nothing is refetched here. Pages reload as the user navigates
    /// to them, which spreads the cost over screens actually visited instead of firing a burst the
    /// moment a setting flips.
    /// </para>
    /// </summary>
    void InvalidateEntityCache();

    /// <summary>
    /// Toggles the signed-in viewer's favorite state for the given entity via AniList's
    /// <c>ToggleFavourite</c> mutation. Requires authentication. Returns true when the mutation
    /// succeeds (callers drive the on/off state optimistically).
    /// </summary>
    Task<bool> ToggleFavouriteAsync(FavouriteKind kind, int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MediaListEntry>> GetMyAnimeListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>> GetMyAnimeListGroupedAsync(CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> SearchMediaPageAsync(
        string search, MediaKind? kind, bool? isAdult = false, int page = 1, int perPage = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// All Discover sections' first pages in one aliased request (rate-limit friendly). Seasons are
    /// computed by the caller via <c>AniListSeason</c>. <paramref name="filterAdult"/> false omits
    /// the isAdult filter so 18+ titles may mix into the general sections;
    /// <paramref name="includeAdultSections"/> requests the 18+ aliases (adult toggle on).
    /// </summary>
    Task<DiscoverSections> GetDiscoverSectionsAsync(
        string currentSeason,
        int currentSeasonYear,
        string nextSeason,
        int nextSeasonYear,
        bool filterAdult,
        bool includeAdultSections,
        int perPage = 20,
        CancellationToken cancellationToken = default);

    /// <summary>One browse page; powers the View All lists and the Discover rows' Load More.
    /// Null filters are omitted, not sent as null.</summary>
    Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> BrowseAnimePageAsync(
        string sort,
        string? status = null,
        string? season = null,
        int? seasonYear = null,
        bool? isAdult = null,
        string? format = null,
        int page = 1,
        int perPage = 25,
        CancellationToken cancellationToken = default);
    Task<(Media? Media, MediaListEntry? ListEntry)> GetMediaAsync(int id, CancellationToken cancellationToken = default);
    Task<MediaListEntry?> SaveMediaListEntryAsync(MediaListEntry entry, CancellationToken cancellationToken = default);
    Task<bool> DeleteMediaListEntryAsync(int entryId, CancellationToken cancellationToken = default);
    Task<int> GetCurrentUserIdAsync(CancellationToken cancellationToken = default);
    Task<AniListUser> GetViewerAsync(CancellationToken cancellationToken = default);
    Task<AniListUser> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AiringScheduleEntry>> GetAiringScheduleAsync(IReadOnlyList<int> mediaIds, int airingAfter, int airingBefore, CancellationToken cancellationToken = default);
    Task<Staff?> GetStaffAsync(
        int id,
        string charactersSort = "FAVOURITES_DESC",
        string mediaSort = "POPULARITY_DESC",
        int charactersPage = 1,
        int mediaPage = 1,
        CancellationToken cancellationToken = default);

    Task<Character?> GetCharacterAsync(
        int id,
        string mediaSort = "POPULARITY_DESC",
        int mediaPage = 1,
        CancellationToken cancellationToken = default);

    Task<Studio?> GetStudioAsync(
        int id,
        string mediaSort = "POPULARITY_DESC",
        int mediaPage = 1,
        int mediaPerPage = 25,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<StaffCharacterEdge> Items, PageInfo? PageInfo)> LoadStaffCharactersPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<StaffMediaEdge> Items, PageInfo? PageInfo)> LoadStaffMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> LoadCharacterMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<StudioMediaEdge> Items, PageInfo? PageInfo)> LoadStudioMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<CharacterEdge> Items, PageInfo? PageInfo)> LoadMediaCharactersPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<StaffEdge> Items, PageInfo? PageInfo)> LoadMediaStaffPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<MediaRecommendationNode> Items, PageInfo? PageInfo)> LoadMediaRecommendationsPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default);
}
