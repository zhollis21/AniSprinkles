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
            // Newest first; relations with no start year sort last (int.MinValue under descending).
            "YEAR_DESC" => ordered.ThenByDescending(e => e.Node?.StartDate?.Year ?? int.MinValue),
            // Oldest first; undated relations sort last (int.MaxValue) so they don't masquerade as ancient.
            "YEAR_ASC" => ordered.ThenBy(e => e.Node?.StartDate?.Year ?? int.MaxValue),
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
            "START_DATE_DESC" => ordered.ThenByDescending(e => node(e)?.StartDate?.Year ?? 0),
            // Oldest first: missing years sort last so undated media doesn't masquerade as ancient.
            "START_DATE" => ordered.ThenBy(e => node(e)?.StartDate?.Year ?? int.MaxValue),
            "TITLE_ROMAJI" => ordered.ThenBy(e => node(e)?.Title?.Romaji ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            // POPULARITY_DESC (default)
            _ => ordered.ThenByDescending(e => node(e)?.Popularity ?? 0),
        };
        return withKey.ThenBy(id).ToList();
    }

    private static int RolePriority(string? role) => role switch
    {
        "MAIN" => 0,
        "SUPPORTING" => 1,
        "BACKGROUND" => 2,
        _ => 3,
    };
}
