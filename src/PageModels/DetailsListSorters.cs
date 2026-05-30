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
        => sort switch
        {
            "ROLE" => items
                .OrderBy(e => RolePriority(e.Role))
                .ThenByDescending(e => e.Node?.Favourites ?? 0)
                .ThenBy(e => e.Node?.Id ?? 0)
                .ToList(),
            // FAVOURITES_DESC (default)
            _ => items
                .OrderByDescending(e => e.Node?.Favourites ?? 0)
                .ThenBy(e => e.Node?.Id ?? 0)
                .ToList(),
        };

    private static IReadOnlyList<T> SortByMedia<T>(
        string sort, IReadOnlyList<T> items, Func<T, RelatedMedia?> node, Func<T, int> id)
        => sort switch
        {
            "SCORE_DESC" => items.OrderByDescending(e => node(e)?.AverageScore ?? 0).ThenBy(id).ToList(),
            "FAVOURITES_DESC" => items.OrderByDescending(e => node(e)?.Favourites ?? 0).ThenBy(id).ToList(),
            "START_DATE_DESC" => items.OrderByDescending(e => node(e)?.StartDate?.Year ?? 0).ThenBy(id).ToList(),
            // Oldest first: missing years sort last so undated media doesn't masquerade as ancient.
            "START_DATE" => items.OrderBy(e => node(e)?.StartDate?.Year ?? int.MaxValue).ThenBy(id).ToList(),
            "TITLE_ROMAJI" => items.OrderBy(e => node(e)?.Title?.Romaji ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(id).ToList(),
            // POPULARITY_DESC (default)
            _ => items.OrderByDescending(e => node(e)?.Popularity ?? 0).ThenBy(id).ToList(),
        };

    private static int RolePriority(string? role) => role switch
    {
        "MAIN" => 0,
        "SUPPORTING" => 1,
        "BACKGROUND" => 2,
        _ => 3,
    };
}
