using AniSprinkles.Models;

namespace AniSprinkles.UnitTests;

public class RelatedMediaTests
{
    // Kind drives the type-dependent wording on a card (the list-status chip) now that carousels
    // can navigate to either type. Anything unrecognised reads as anime, which is what every query
    // that never selected `type` was fetching anyway.
    [TestCase("ANIME", MediaKind.Anime)]
    [TestCase("MANGA", MediaKind.Manga)]
    [TestCase("manga", MediaKind.Manga)]  // AniList sends upper-case; the parse is case-insensitive by design.
    [TestCase("NOVEL", MediaKind.Anime)]  // Not a MediaType — NOVEL is a *format* under type MANGA.
    [TestCase("", MediaKind.Anime)]
    [TestCase(null, MediaKind.Anime)]
    public void Kind_ReflectsType(string? type, MediaKind expected)
    {
        var media = new RelatedMedia { Id = 1, Type = type };

        Assert.That(media.Kind, Is.EqualTo(expected));
    }

    [TestCase("ANIME", MediaListStatus.Current, "Watching")]
    [TestCase("MANGA", MediaListStatus.Current, "Reading")]
    [TestCase("ANIME", MediaListStatus.Repeating, "Rewatching")]
    [TestCase("MANGA", MediaListStatus.Repeating, "Rereading")]
    // Planning stays the short enum name on a chip for both types: the cover-art pill has room for
    // one word, and "Plan to Watch" is the picker's wording, not the chip's.
    [TestCase("ANIME", MediaListStatus.Planning, "Planning")]
    [TestCase("MANGA", MediaListStatus.Planning, "Planning")]
    [TestCase("MANGA", MediaListStatus.Completed, "Completed")]
    public void ListStatusDisplay_UsesTheTypesVocabulary(string type, MediaListStatus status, string expected)
    {
        var media = new RelatedMedia { Id = 1, Type = type, ListStatus = status };

        Assert.That(media.ListStatusDisplay, Is.EqualTo(expected));
    }

    [TestCase("RELEASING", "Releasing")]
    [TestCase("NOT_YET_RELEASED", "Not Yet Released")]
    [TestCase("FINISHED", "Finished")]
    [TestCase(null, "")]
    [TestCase("", "")]
    public void MediaStatusDisplay_TitleCasesAniListStatus(string? status, string expected)
        => Assert.That(new RelatedMedia { Status = status }.MediaStatusDisplay, Is.EqualTo(expected));

    [Test]
    public void BrowseMetaDisplay_JoinsPresentPartsOnly()
    {
        var full = new RelatedMedia { Format = "TV", StartDate = new MediaDate { Year = 2026 }, Status = "RELEASING" };
        Assert.That(full.BrowseMetaDisplay, Is.EqualTo("TV · 2026 · Releasing"));

        var sparse = new RelatedMedia { Format = "MOVIE" };
        Assert.That(sparse.BrowseMetaDisplay, Is.EqualTo("MOVIE"));

        Assert.That(new RelatedMedia().BrowseMetaDisplay, Is.EqualTo(string.Empty));
    }

    // Chip labels use the friendly names ("Watching"/"Rewatching"), matching the Library sections.
    [TestCase(MediaListStatus.Current, "Watching")]
    [TestCase(MediaListStatus.Repeating, "Rewatching")]
    [TestCase(MediaListStatus.Planning, "Planning")]
    [TestCase(MediaListStatus.Completed, "Completed")]
    [TestCase(null, "")]
    public void ListStatusDisplay_UsesFriendlyNames(MediaListStatus? status, string expected)
    {
        var media = new RelatedMedia { ListStatus = status };

        Assert.That(media.ListStatusDisplay, Is.EqualTo(expected));
        Assert.That(media.HasListStatus, Is.EqualTo(status is not null));
    }

    // Sort-metric fallbacks: when a list is sorted by a metric, the card's badge must always show — never
    // blank, which reads as broken. Counts fall back to "0"; year/rating fall back to "—" (0 would lie).
    [TestCase(null, "0")]
    [TestCase(0, "0")]
    [TestCase(7, "7")]
    [TestCase(1500, "1.5k")]
    public void FavouritesOrZero_ShowsZeroWhenMissing(int? favourites, string expected)
        => Assert.That(new RelatedMedia { Favourites = favourites }.FavouritesOrZero, Is.EqualTo(expected));

    [TestCase(null, "0")]
    [TestCase(0, "0")]
    [TestCase(2300, "2.3k")]
    public void PopularityOrZero_ShowsZeroWhenMissing(int? popularity, string expected)
        => Assert.That(new RelatedMedia { Popularity = popularity }.PopularityOrZero, Is.EqualTo(expected));

    [TestCase(null, "0")]
    [TestCase(0, "0")]
    [TestCase(411, "411")]
    public void TrendingOrZero_ShowsZeroWhenMissing(int? trending, string expected)
        => Assert.That(new RelatedMedia { Trending = trending }.TrendingOrZero, Is.EqualTo(expected));

    [TestCase(null, "—")]
    [TestCase(0, "—")]
    [TestCase(85, "8.5")]
    public void ScoreOrDash_ShowsDashWhenMissing(int? score, string expected)
        => Assert.That(new RelatedMedia { WeightedScore = score }.ScoreOrDash, Is.EqualTo(expected));

    [Test]
    public void YearOrDash_ShowsDashWhenMissing()
    {
        Assert.That(new RelatedMedia().YearOrDash, Is.EqualTo("—"));
        Assert.That(new RelatedMedia { StartDate = new MediaDate { Year = null } }.YearOrDash, Is.EqualTo("—"));
    }

    [Test]
    public void YearOrDash_ShowsYearWhenPresent()
        => Assert.That(new RelatedMedia { StartDate = new MediaDate { Year = 2014 } }.YearOrDash, Is.EqualTo("2014"));

    // FormatDisplay prettifies AniList's enum for card labels (used by the Title-sort metric badge).
    [TestCase("TV", "TV")]
    [TestCase("MOVIE", "MOVIE")]
    [TestCase("TV_SHORT", "TV SHORT")]
    [TestCase("ONE_SHOT", "ONE SHOT")]
    [TestCase(null, "")]
    public void FormatDisplay_ReplacesUnderscores(string? format, string expected)
        => Assert.That(new RelatedMedia { Format = format }.FormatDisplay, Is.EqualTo(expected));
}
