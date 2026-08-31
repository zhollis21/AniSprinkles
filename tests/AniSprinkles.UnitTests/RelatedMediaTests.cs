using AniSprinkles.Models;

namespace AniSprinkles.UnitTests;

public class RelatedMediaTests
{
    // Kind drives the type-dependent wording on a card (the list-status chip) now that carousels
    // can navigate to either type. Anything unrecognised reads as anime, which is what every query
    // that never selected `type` was fetching anyway.
    [Theory]
    [InlineData("ANIME", MediaKind.Anime)]
    [InlineData("MANGA", MediaKind.Manga)]
    [InlineData("manga", MediaKind.Manga)]  // AniList sends upper-case; the parse is case-insensitive by design.
    [InlineData("NOVEL", MediaKind.Anime)]  // Not a MediaType — NOVEL is a *format* under type MANGA.
    [InlineData("", MediaKind.Anime)]
    [InlineData(null, MediaKind.Anime)]
    public void Kind_ReflectsType(string? type, MediaKind expected)
    {
        var media = new RelatedMedia { Id = 1, Type = type };

        Assert.Equal(expected, media.Kind);
    }

    [Theory]
    [InlineData("ANIME", MediaListStatus.Current, "Watching")]
    [InlineData("MANGA", MediaListStatus.Current, "Reading")]
    [InlineData("ANIME", MediaListStatus.Repeating, "Rewatching")]
    [InlineData("MANGA", MediaListStatus.Repeating, "Rereading")]
    // Planning stays the short enum name on a chip for both types: the cover-art pill has room for
    // one word, and "Plan to Watch" is the picker's wording, not the chip's.
    [InlineData("ANIME", MediaListStatus.Planning, "Planning")]
    [InlineData("MANGA", MediaListStatus.Planning, "Planning")]
    [InlineData("MANGA", MediaListStatus.Completed, "Completed")]
    public void ListStatusDisplay_UsesTheTypesVocabulary(string type, MediaListStatus status, string expected)
    {
        var media = new RelatedMedia { Id = 1, Type = type, ListStatus = status };

        Assert.Equal(expected, media.ListStatusDisplay);
    }

    [Theory]
    [InlineData("RELEASING", "Releasing")]
    [InlineData("NOT_YET_RELEASED", "Not Yet Released")]
    [InlineData("FINISHED", "Finished")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void MediaStatusDisplay_TitleCasesAniListStatus(string? status, string expected)
        => Assert.Equal(expected, new RelatedMedia { Status = status }.MediaStatusDisplay);

    [Fact]
    public void BrowseMetaDisplay_JoinsPresentPartsOnly()
    {
        var full = new RelatedMedia { Format = "TV", StartDate = new MediaDate { Year = 2026 }, Status = "RELEASING" };
        Assert.Equal("TV · 2026 · Releasing", full.BrowseMetaDisplay);

        var sparse = new RelatedMedia { Format = "MOVIE" };
        Assert.Equal("MOVIE", sparse.BrowseMetaDisplay);

        Assert.Equal(string.Empty, new RelatedMedia().BrowseMetaDisplay);
    }

    // Chip labels use the friendly names ("Watching"/"Rewatching"), matching the Library sections.
    [Theory]
    [InlineData(MediaListStatus.Current, "Watching")]
    [InlineData(MediaListStatus.Repeating, "Rewatching")]
    [InlineData(MediaListStatus.Planning, "Planning")]
    [InlineData(MediaListStatus.Completed, "Completed")]
    [InlineData(null, "")]
    public void ListStatusDisplay_UsesFriendlyNames(MediaListStatus? status, string expected)
    {
        var media = new RelatedMedia { ListStatus = status };

        Assert.Equal(expected, media.ListStatusDisplay);
        Assert.Equal(status is not null, media.HasListStatus);
    }

    // Sort-metric fallbacks: when a list is sorted by a metric, the card's badge must always show — never
    // blank, which reads as broken. Counts fall back to "0"; year/rating fall back to "—" (0 would lie).
    [Theory]
    [InlineData(null, "0")]
    [InlineData(0, "0")]
    [InlineData(7, "7")]
    [InlineData(1500, "1.5k")]
    public void FavouritesOrZero_ShowsZeroWhenMissing(int? favourites, string expected)
        => Assert.Equal(expected, new RelatedMedia { Favourites = favourites }.FavouritesOrZero);

    [Theory]
    [InlineData(null, "0")]
    [InlineData(0, "0")]
    [InlineData(2300, "2.3k")]
    public void PopularityOrZero_ShowsZeroWhenMissing(int? popularity, string expected)
        => Assert.Equal(expected, new RelatedMedia { Popularity = popularity }.PopularityOrZero);

    [Theory]
    [InlineData(null, "0")]
    [InlineData(0, "0")]
    [InlineData(411, "411")]
    public void TrendingOrZero_ShowsZeroWhenMissing(int? trending, string expected)
        => Assert.Equal(expected, new RelatedMedia { Trending = trending }.TrendingOrZero);

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0, "—")]
    [InlineData(85, "8.5")]
    public void ScoreOrDash_ShowsDashWhenMissing(int? score, string expected)
        => Assert.Equal(expected, new RelatedMedia { AverageScore = score }.ScoreOrDash);

    [Fact]
    public void YearOrDash_ShowsDashWhenMissing()
    {
        Assert.Equal("—", new RelatedMedia().YearOrDash);
        Assert.Equal("—", new RelatedMedia { StartDate = new MediaDate { Year = null } }.YearOrDash);
    }

    [Fact]
    public void YearOrDash_ShowsYearWhenPresent()
        => Assert.Equal("2014", new RelatedMedia { StartDate = new MediaDate { Year = 2014 } }.YearOrDash);

    // FormatDisplay prettifies AniList's enum for card labels (used by the Title-sort metric badge).
    [Theory]
    [InlineData("TV", "TV")]
    [InlineData("MOVIE", "MOVIE")]
    [InlineData("TV_SHORT", "TV SHORT")]
    [InlineData("ONE_SHOT", "ONE SHOT")]
    [InlineData(null, "")]
    public void FormatDisplay_ReplacesUnderscores(string? format, string expected)
        => Assert.Equal(expected, new RelatedMedia { Format = format }.FormatDisplay);
}
