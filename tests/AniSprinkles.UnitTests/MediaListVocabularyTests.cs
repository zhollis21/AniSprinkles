using AniSprinkles.Utilities;

namespace AniSprinkles.UnitTests;

/// <summary>
/// The per-type wording table (#12). Worth pinning directly rather than only through its callers:
/// the strings reach four surfaces that don't share a test — the details status picker, the
/// bottom-sheet status picker, the long-press toasts and the progress popup — and two of those
/// live in the MAUI project where nothing can assert on them.
/// </summary>
public class MediaListVocabularyTests
{
    [TestCase(MediaListStatus.Current, MediaKind.Anime, "Watching")]
    [TestCase(MediaListStatus.Current, MediaKind.Manga, "Reading")]
    [TestCase(MediaListStatus.Planning, MediaKind.Anime, "Plan to Watch")]
    [TestCase(MediaListStatus.Planning, MediaKind.Manga, "Plan to Read")]
    [TestCase(MediaListStatus.Repeating, MediaKind.Anime, "Rewatching")]
    [TestCase(MediaListStatus.Repeating, MediaKind.Manga, "Rereading")]
    // The other three read the same for both types, so they fall through to the enum name.
    [TestCase(MediaListStatus.Completed, MediaKind.Manga, "Completed")]
    [TestCase(MediaListStatus.Paused, MediaKind.Manga, "Paused")]
    [TestCase(MediaListStatus.Dropped, MediaKind.Manga, "Dropped")]
    public void StatusLabel_UsesTheTypesVerb(MediaListStatus status, MediaKind kind, string expected)
        => Assert.That(MediaListVocabulary.StatusLabel(status, kind), Is.EqualTo(expected));

    [TestCase(MediaListStatus.Planning, MediaKind.Manga, "Planning")]
    [TestCase(MediaListStatus.Current, MediaKind.Manga, "Reading")]
    [TestCase(MediaListStatus.Repeating, MediaKind.Anime, "Rewatching")]
    public void StatusChipLabel_StaysToOneWord(MediaListStatus status, MediaKind kind, string expected)
        => Assert.That(MediaListVocabulary.StatusChipLabel(status, kind), Is.EqualTo(expected));

    [TestCase(MediaProgressUnit.Episode, "Episode", "episodes", "EP", "watched", "Episodes watched")]
    [TestCase(MediaProgressUnit.Chapter, "Chapter", "chapters", "CH", "read", "Chapters read")]
    [TestCase(MediaProgressUnit.Volume, "Volume", "volumes", "VOL", "read", "Volumes read")]
    public void TheUnitWords_AgreeWithEachOther(
        MediaProgressUnit unit,
        string noun,
        string plural,
        string abbreviation,
        string verb,
        string header)
    {
        Assert.That(MediaListVocabulary.UnitNoun(unit), Is.EqualTo(noun));
        Assert.That(MediaListVocabulary.UnitNounPlural(unit), Is.EqualTo(plural));
        Assert.That(MediaListVocabulary.UnitAbbreviation(unit), Is.EqualTo(abbreviation));
        Assert.That(MediaListVocabulary.ConsumedVerb(unit), Is.EqualTo(verb));
        Assert.That(MediaListVocabulary.UnitProgressHeader(unit), Is.EqualTo(header));
    }

    [TestCase("ANIME", MediaKind.Anime)]
    [TestCase("MANGA", MediaKind.Manga)]
    [TestCase("manga", MediaKind.Manga)]
    [TestCase("NOVEL", MediaKind.Anime)]  // A format, not a type — AniList files novels under MANGA.
    [TestCase(null, MediaKind.Anime)]
    public void ParseMediaKind_TreatsAnythingUnrecognisedAsAnime(string? type, MediaKind expected)
        => Assert.That(MediaKindExtensions.ParseMediaKind(type), Is.EqualTo(expected));

    [TestCase(MediaKind.Anime, "ANIME")]
    [TestCase(MediaKind.Manga, "MANGA")]
    public void ToAniListType_RoundTrips(MediaKind kind, string expected)
    {
        Assert.That(kind.ToAniListType(), Is.EqualTo(expected));
        Assert.That(MediaKindExtensions.ParseMediaKind(kind.ToAniListType()), Is.EqualTo(kind));
    }
}
