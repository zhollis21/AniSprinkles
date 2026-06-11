using AniSprinkles.Models;

namespace AniSprinkles.PageModels;

/// <summary>
/// Client-side comparers for the details-page lists, used by <see cref="PaginatedSection{T}"/>'s
/// local-sort fast path: when a section already holds the complete set (<c>HasNextPage == false</c>)
/// a sort change is pure in-memory reordering, with no API round-trip.
///
/// Every comparer applies a deterministic final tiebreak on the entity id so the order is stable and
/// toggle-consistent (A→B→A returns the same order) regardless of the list's current order. The codes
/// mirror the AniList <c>MediaSort</c> / <c>CharacterSort</c> values the UI offers; any unrecognized
/// code falls back to the section's primary sort.
/// </summary>
public static class DetailsListSorters
{
    public static IReadOnlyList<CharacterMediaEdge> SortAppearances(string sort, IReadOnlyList<CharacterMediaEdge> items)
        => SortByMedia(sort, items, e => e.Node, e => e.Node?.Id ?? 0);

    public static IReadOnlyList<StaffMediaEdge> SortProductionRoles(string sort, IReadOnlyList<StaffMediaEdge> items)
        => SortByMedia(sort, items, e => e.Node, e => e.Node?.Id ?? 0);

    public static IReadOnlyList<StudioMediaEdge> SortStudioProductions(string sort, IReadOnlyList<StudioMediaEdge> items)
        => SortByMedia(sort, items, e => e.Node, e => e.Node?.Id ?? 0);

    public static IReadOnlyList<StaffCharacterEdge> SortVoiceRoles(string sort, IReadOnlyList<StaffCharacterEdge> items)
    {
        // Null-node edges always sort after real voice roles, before applying the active key — a
        // null node otherwise ties on a zero key and wins the id=0 tiebreak.
        var ordered = items.OrderBy(e => e.Node is null);
        var withKey = sort switch
        {
            "ROLE" => ordered.ThenBy(e => RolePriority(e.Role)).ThenByDescending(e => e.Node?.Favourites ?? 0),
            // FAVOURITES_DESC (default)
            _ => ordered.ThenByDescending(e => e.Node?.Favourites ?? 0),
        };
        return withKey.ThenBy(e => e.Node?.Id ?? 0).ToList();
    }

    /// <summary>
    /// Orders a media's relations entirely client-side (AniList exposes no relation sort enum). The
    /// default groups by relation type in a curated narrative order (Sequel → Prequel → Side Story → …);
    /// YEAR_DESC/YEAR_ASC and TITLE sort across the whole small set. Matches on the formatted
    /// <c>RelationType</c> string (<c>SIDE_STORY</c> → "Side Story") that the mapper already stored.
    /// </summary>
    public static IReadOnlyList<MediaRelationEdge> SortRelations(string sort, IReadOnlyList<MediaRelationEdge> items)
    {
        // Null-node edges always sort after real relations, regardless of the active key.
        var ordered = items.OrderBy(e => e.Node is null);
        var withKey = sort switch
        {
            // Undated first in BOTH directions, then by full date — same rule as the productions lists
            // (SortByMedia) so every date sort in the app behaves identically.
            "YEAR_DESC" => ordered
                .ThenBy(e => e.Node?.StartDate?.Year is not null)
                .ThenByDescending(e => DateKey(e.Node)),
            "YEAR_ASC" => ordered
                .ThenBy(e => e.Node?.StartDate?.Year is not null)
                .ThenBy(e => DateKey(e.Node)),
            // Untitled relations sort last (an empty string would otherwise win the A–Z ordering).
            "TITLE" => ordered
                .ThenBy(e => string.IsNullOrEmpty(e.Node?.Title?.Romaji))
                .ThenBy(e => e.Node?.Title?.Romaji ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            // RELATION (default): bucket by relation type, then a stable id tiebreak within each bucket.
            _ => ordered.ThenBy(e => RelationTypePriority(e.RelationType)),
        };
        return withKey.ThenBy(e => e.Node?.Id ?? 0).ToList();
    }

    // Normalize underscores so this matches both the mapper's display form ("Side Story") and any raw
    // AniList enum that slips through ("SIDE_STORY") — e.g. the CI stub builds edges with raw values.
    private static int RelationTypePriority(string? relationType) => relationType?.Replace('_', ' ').ToLowerInvariant() switch
    {
        "sequel" => 0,
        "prequel" => 1,
        "side story" => 2,
        "parent" => 3,
        "adaptation" => 4,
        "spin off" => 5,
        "alternative" => 6,
        _ => 7,
    };

    private static IReadOnlyList<T> SortByMedia<T>(
        string sort, IReadOnlyList<T> items, Func<T, RelatedMedia?> node, Func<T, int> id)
    {
        // Null-node edges always sort after real media, regardless of the active key.
        var ordered = items.OrderBy(e => node(e) is null);
        var withKey = sort switch
        {
            "SCORE_DESC" => ordered.ThenByDescending(e => node(e)?.AverageScore ?? 0),
            "FAVOURITES_DESC" => ordered.ThenByDescending(e => node(e)?.Favourites ?? 0),
            // Match AniList's server date order so small (client-sorted) and large (server-sorted) lists
            // agree: undated entries sort FIRST in BOTH directions, then by full date (year→month→day).
            "START_DATE_DESC" => ordered
                .ThenBy(e => node(e)?.StartDate?.Year is not null)
                .ThenByDescending(e => DateKey(node(e))),
            "START_DATE" => ordered
                .ThenBy(e => node(e)?.StartDate?.Year is not null)
                .ThenBy(e => DateKey(node(e))),
            // Untitled sorts last (empty string would otherwise win A–Z) — same as SortRelations' TITLE.
            "TITLE_ROMAJI" => ordered
                .ThenBy(e => string.IsNullOrEmpty(node(e)?.Title?.Romaji))
                .ThenBy(e => node(e)?.Title?.Romaji ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            // POPULARITY_DESC (default)
            _ => ordered.ThenByDescending(e => node(e)?.Popularity ?? 0),
        };
        // Final id tiebreak keeps the order stable/toggle-consistent. AniList's own among-equal order
        // isn't the id we pass and isn't reproducible, but those differences are not visible.
        return withKey.ThenBy(id).ToList();
    }

    // Full start date as one sortable int (year→month→day); missing parts count as 0. Undated media
    // (year null → 0) is grouped separately by the date arms, so its key value is never compared.
    private static int DateKey(RelatedMedia? media)
        => (media?.StartDate?.Year ?? 0) * 10000 + (media?.StartDate?.Month ?? 0) * 100 + (media?.StartDate?.Day ?? 0);

    private static int RolePriority(string? role) => role switch
    {
        "MAIN" => 0,
        "SUPPORTING" => 1,
        "BACKGROUND" => 2,
        _ => 3,
    };
}
