using AniSprinkles.Models;

namespace AniSprinkles.UnitTests;

public class RelatedMediaTests
{
    // IsAnime gates detail-page navigation: the Media query is anime-only, so tapping a
    // non-anime tile (manga/novel) must short-circuit to a toast instead of a 404'ing fetch.
    [Theory]
    [InlineData("ANIME", true)]
    [InlineData("anime", true)]   // AniList sends upper-case, but guard is case-insensitive by design.
    [InlineData("MANGA", false)]
    [InlineData("NOVEL", false)]
    [InlineData("ONE_SHOT", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsAnime_ReflectsType(string? type, bool expected)
    {
        var media = new RelatedMedia { Id = 1, Type = type };

        Assert.Equal(expected, media.IsAnime);
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

    // Chip labels use the friendly names ("Watching"/"Rewatching"), matching the My Anime sections.
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
