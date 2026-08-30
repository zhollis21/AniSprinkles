using AniSprinkles.Models;

namespace AniSprinkles.Utilities;

/// <summary>
/// The per-media-type wording for list surfaces (#12) — one table rather than a switch at each
/// call site, because the same six statuses are labelled on the details page, the status picker,
/// the long-press menu, the list-status chips and (from Phase 2) the Library sections, and they
/// drifted before now even with only anime to describe.
/// <para>
/// AniList's own vocabulary is split the same way: one <c>MediaListStatus</c> enum whose schema
/// descriptions read "Currently watching/reading", with the client choosing a word.
/// </para>
/// </summary>
public static class MediaListVocabulary
{
    /// <summary>
    /// The label for a status, e.g. Current → "Watching" for anime and "Reading" for manga.
    /// <para>
    /// These are the <em>action</em> labels the user picks from, not AniList's list-section names —
    /// the API groups a manga list under "Reading"/"Planning", where the picker says "Reading" and
    /// "Plan to Read". Section names come from the API and are never generated here.
    /// </para>
    /// </summary>
    public static string StatusLabel(MediaListStatus status, MediaKind kind) => kind switch
    {
        MediaKind.Manga => status switch
        {
            MediaListStatus.Current => "Reading",
            MediaListStatus.Planning => "Plan to Read",
            MediaListStatus.Repeating => "Rereading",
            _ => status.ToString(),
        },
        _ => status switch
        {
            MediaListStatus.Current => "Watching",
            MediaListStatus.Planning => "Plan to Watch",
            MediaListStatus.Repeating => "Rewatching",
            _ => status.ToString(),
        },
    };

    /// <summary>
    /// The compact label for the cover-art status chips, which have room for one word. Only the two
    /// statuses whose natural word differs by type are translated; the rest keep the enum name, so
    /// an anime chip still reads "Planning" rather than growing into "Plan to Watch".
    /// </summary>
    public static string StatusChipLabel(MediaListStatus status, MediaKind kind) => status switch
    {
        MediaListStatus.Current => kind == MediaKind.Manga ? "Reading" : "Watching",
        MediaListStatus.Repeating => kind == MediaKind.Manga ? "Rereading" : "Rewatching",
        _ => status.ToString(),
    };

    /// <summary>Singular noun for a progress unit: "Episode", "Chapter", "Volume".</summary>
    public static string UnitNoun(MediaProgressUnit unit) => unit switch
    {
        MediaProgressUnit.Chapter => "Chapter",
        MediaProgressUnit.Volume => "Volume",
        _ => "Episode",
    };

    /// <summary>Plural noun for a progress unit, for counts and totals.</summary>
    public static string UnitNounPlural(MediaProgressUnit unit) => unit switch
    {
        MediaProgressUnit.Chapter => "chapters",
        MediaProgressUnit.Volume => "volumes",
        _ => "episodes",
    };

    /// <summary>Short form for the +1 pill, where there is room for two or three characters.</summary>
    public static string UnitAbbreviation(MediaProgressUnit unit) => unit switch
    {
        MediaProgressUnit.Chapter => "CH",
        MediaProgressUnit.Volume => "VOL",
        _ => "EP",
    };

    /// <summary>Past participle used by the completion prompt: "watched" / "read".</summary>
    public static string ConsumedVerb(MediaProgressUnit unit) =>
        unit == MediaProgressUnit.Episode ? "watched" : "read";

    /// <summary>Header for the progress editor: "Episodes watched", "Chapters read", "Volumes read".</summary>
    public static string UnitProgressHeader(MediaProgressUnit unit) => unit switch
    {
        MediaProgressUnit.Chapter => "Chapters read",
        MediaProgressUnit.Volume => "Volumes read",
        _ => "Episodes watched",
    };
}
