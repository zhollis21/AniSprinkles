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
    [Theory]
    [InlineData(MediaListStatus.Current, MediaKind.Anime, "Watching")]
    [InlineData(MediaListStatus.Current, MediaKind.Manga, "Reading")]
    [InlineData(MediaListStatus.Planning, MediaKind.Anime, "Plan to Watch")]
    [InlineData(MediaListStatus.Planning, MediaKind.Manga, "Plan to Read")]
    [InlineData(MediaListStatus.Repeating, MediaKind.Anime, "Rewatching")]
    [InlineData(MediaListStatus.Repeating, MediaKind.Manga, "Rereading")]
    // The other three read the same for both types, so they fall through to the enum name.
    [InlineData(MediaListStatus.Completed, MediaKind.Manga, "Completed")]
    [InlineData(MediaListStatus.Paused, MediaKind.Manga, "Paused")]
    [InlineData(MediaListStatus.Dropped, MediaKind.Manga, "Dropped")]
    public void StatusLabel_UsesTheTypesVerb(MediaListStatus status, MediaKind kind, string expected)
        => Assert.Equal(expected, MediaListVocabulary.StatusLabel(status, kind));

    [Theory]
    // The chip has room for one word, so Planning stays short where the picker says "Plan to Read".
    [InlineData(MediaListStatus.Planning, MediaKind.Manga, "Planning")]
    [InlineData(MediaListStatus.Current, MediaKind.Manga, "Reading")]
    [InlineData(MediaListStatus.Repeating, MediaKind.Anime, "Rewatching")]
    public void StatusChipLabel_StaysToOneWord(MediaListStatus status, MediaKind kind, string expected)
        => Assert.Equal(expected, MediaListVocabulary.StatusChipLabel(status, kind));

    [Theory]
    [InlineData(MediaProgressUnit.Episode, "Episode", "episodes", "EP", "watched", "Episodes watched")]
    [InlineData(MediaProgressUnit.Chapter, "Chapter", "chapters", "CH", "read", "Chapters read")]
    [InlineData(MediaProgressUnit.Volume, "Volume", "volumes", "VOL", "read", "Volumes read")]
    public void TheUnitWords_AgreeWithEachOther(
        MediaProgressUnit unit,
        string noun,
        string plural,
        string abbreviation,
        string verb,
        string header)
    {
        Assert.Equal(noun, MediaListVocabulary.UnitNoun(unit));
        Assert.Equal(plural, MediaListVocabulary.UnitNounPlural(unit));
        Assert.Equal(abbreviation, MediaListVocabulary.UnitAbbreviation(unit));
        Assert.Equal(verb, MediaListVocabulary.ConsumedVerb(unit));
        Assert.Equal(header, MediaListVocabulary.UnitProgressHeader(unit));
    }

    [Theory]
    [InlineData("ANIME", MediaKind.Anime)]
    [InlineData("MANGA", MediaKind.Manga)]
    [InlineData("manga", MediaKind.Manga)]
    [InlineData("NOVEL", MediaKind.Anime)]  // A format, not a type — AniList files novels under MANGA.
    [InlineData(null, MediaKind.Anime)]
    public void ParseMediaKind_TreatsAnythingUnrecognisedAsAnime(string? type, MediaKind expected)
        => Assert.Equal(expected, MediaKindExtensions.ParseMediaKind(type));

    [Theory]
    [InlineData(MediaKind.Anime, "ANIME")]
    [InlineData(MediaKind.Manga, "MANGA")]
    public void ToAniListType_RoundTrips(MediaKind kind, string expected)
    {
        Assert.Equal(expected, kind.ToAniListType());
        Assert.Equal(kind, MediaKindExtensions.ParseMediaKind(kind.ToAniListType()));
    }
}
