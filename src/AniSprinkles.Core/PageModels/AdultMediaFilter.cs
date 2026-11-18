using AniSprinkles.Models;

namespace AniSprinkles.PageModels;

/// <summary>
/// Keeps 18+ titles off the details pages while Display Adult Content is off (#199).
/// <para>
/// AniList returns adult titles in every nested media connection (voice roles, production roles,
/// appearances, studio productions, relations and recommendations), signed in or not, and none of
/// those connections take an <c>isAdult</c> argument. Browse, Search and Discover can ask the server
/// to filter; these cannot, so the app does it, the way <see cref="MediaListSectionsMerger"/> does
/// for the Library.
/// </para>
/// <para>
/// The setting is passed in rather than read from <c>AppSettings</c>, so a page judges every one of
/// its pages under the value its first was loaded with: a Load More must not mix two policies in one
/// list. <c>DetailsPageModelBase</c> rebuilds the page when the setting moves.
/// </para>
/// </summary>
public static class AdultMediaFilter
{
    /// <summary>
    /// True when <paramref name="media"/> must not be shown. Only an explicit 18+ flag hides a title:
    /// a query that does not ask for the flag reads as not adult.
    /// </summary>
    public static bool Hides(RelatedMedia? media, bool displayAdultContent)
        => media?.IsAdult == true && !displayAdultContent;

    /// <summary><paramref name="item"/>, or null when the title it shows is hidden.</summary>
    public static T? Keep<T>(T item, RelatedMedia? media, bool displayAdultContent)
        where T : class
        => Hides(media, displayAdultContent) ? null : item;

    /// <summary>
    /// A voice role as it should be shown: with its first title that is not hidden, or null when
    /// every title it has is.
    /// <para>
    /// Returns a copy rather than rewriting <paramref name="edge"/>. Staff reads are cached for the
    /// session (<c>CachingAniListClient</c>), so the edge is the cached one, and rewriting it would
    /// leave the non-adult title in place after adult content is turned back on.
    /// </para>
    /// </summary>
    public static StaffCharacterEdge? VoiceRole(StaffCharacterEdge edge, bool displayAdultContent)
    {
        if (!Hides(edge.Media, displayAdultContent))
        {
            return edge;
        }

        return edge.AllMedia.FirstOrDefault(m => !Hides(m, displayAdultContent)) is { } shown
            ? new StaffCharacterEdge { Node = edge.Node, Role = edge.Role, AllMedia = edge.AllMedia, Media = shown }
            : null;
    }
}
