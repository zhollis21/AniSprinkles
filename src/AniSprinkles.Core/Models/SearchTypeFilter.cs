namespace AniSprinkles.Models;

/// <summary>
/// What the Search tab's type pills are set to (#12). Deliberately NOT <see cref="MediaKind"/> with
/// an extra value: a piece of media is anime or manga and never "all", and <c>MediaKind</c> is also
/// what <c>RelatedMedia.Kind</c> and <c>MediaListVocabulary</c> answer questions with. This is a
/// filter, so it lives in its own type and converts at the client boundary.
/// </summary>
public enum SearchTypeFilter
{
    /// <summary>Both types. AniList ranks them together under <c>SEARCH_MATCH</c>.</summary>
    All,
    Anime,
    Manga,
}

public static class SearchTypeFilterExtensions
{
    /// <summary>
    /// The kind to query, or <c>null</c> for both.
    /// <para>
    /// Null has to reach AniList as an <em>absent</em> argument, not an explicit null: verified
    /// against the live API, <c>media(type: null, search: "berserk")</c> returns an empty list while
    /// omitting the argument returns anime and manga together. The client's serializer drops null
    /// variables (<c>JsonIgnoreCondition.WhenWritingNull</c>), which is what makes the absent form
    /// happen — the same mechanism the <c>isAdult</c> filter relies on.
    /// </para>
    /// </summary>
    public static MediaKind? ToMediaKind(this SearchTypeFilter filter) => filter switch
    {
        SearchTypeFilter.Anime => MediaKind.Anime,
        SearchTypeFilter.Manga => MediaKind.Manga,
        _ => null,
    };

    /// <summary>Parses a persisted value; anything unrecognised falls back to <see cref="SearchTypeFilter.All"/>.</summary>
    public static SearchTypeFilter ParseSearchTypeFilter(string? value) =>
        Enum.TryParse<SearchTypeFilter>(value, ignoreCase: true, out var parsed) ? parsed : SearchTypeFilter.All;
}
