using AniSprinkles.Converters;
using AniSprinkles.Icons;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.UnitTests;

/// <summary>
/// #52 small-helpers pass for <see cref="MediaMetricBadges.ForMediaSort"/>, the badge builder shared
/// by every details list of <see cref="RelatedMedia"/> — Studio productions, Staff production roles
/// and Character appearances.
/// <para>
/// The invariant worth pinning is the one its summary states: when the active sort <i>is</i> a
/// metric the badge always renders, falling back to "0" for counts and "—" for a score or year.
/// A blank badge on a list sorted by that very metric reads as broken data, so "missing" has to
/// look deliberate.
/// </para>
/// </summary>
public class MediaMetricBadgesTests
{
    private static RelatedMedia Media(
        int? popularity = null,
        int? trending = null,
        int? averageScore = null,
        int? favourites = null,
        int? year = null,
        string? format = null)
        => new()
        {
            Id = 1,
            Popularity = popularity,
            Trending = trending,
            AverageScore = averageScore,
            Favourites = favourites,
            StartDate = year is null ? null : new MediaDate { Year = year },
            Format = format,
        };

    [Fact]
    public void ANullMedia_HasNoBadge()
        => Assert.Null(MediaMetricBadges.ForMediaSort(null, "POPULARITY_DESC"));

    [Fact]
    public void AnUnrecognizedSort_HasNoBadge()
        => Assert.Null(MediaMetricBadges.ForMediaSort(Media(popularity: 5000), "EPISODES_DESC"));

    // ── The metric sorts: glyph, colour and text ─────────────────────

    [Fact]
    public void PopularitySort_ShowsThePeopleGlyphAndTheCompactCount()
    {
        var badge = MediaMetricBadges.ForMediaSort(Media(popularity: 90_457), "POPULARITY_DESC");

        Assert.NotNull(badge);
        Assert.Equal(Glyphs.Regular.People24, badge.Glyph);
        Assert.Equal(Color.FromArgb("#FF9500"), badge.IconColor);
        Assert.Equal("90.5k", badge.Text);
    }

    [Fact]
    public void TrendingSort_ShowsTheFlameGlyphAndTheTrendingCount()
    {
        // Trending deliberately does not reuse the popularity glyph: a Trending row sitting next to
        // popularity-sorted rows would otherwise read identically while showing a different number.
        var badge = MediaMetricBadges.ForMediaSort(Media(popularity: 90_457, trending: 120), "TRENDING_DESC");

        Assert.NotNull(badge);
        Assert.Equal(Glyphs.Regular.Fire24, badge.Glyph);
        Assert.Equal(Color.FromArgb("#FF3B30"), badge.IconColor);
        Assert.Equal("120", badge.Text);
    }

    [Fact]
    public void ScoreSort_ShowsTheStarGlyphAndTheTenPointRating()
    {
        var badge = MediaMetricBadges.ForMediaSort(Media(averageScore: 84), "SCORE_DESC");

        Assert.NotNull(badge);
        Assert.Equal(Glyphs.Regular.Star24, badge.Glyph);
        Assert.Equal(Color.FromArgb("#FFCC00"), badge.IconColor);
        Assert.Equal("8.4", badge.Text);
    }

    [Fact]
    public void FavouritesSort_ShowsTheHeartGlyphAndTheCompactCount()
    {
        var badge = MediaMetricBadges.ForMediaSort(Media(favourites: 1200), "FAVOURITES_DESC");

        Assert.NotNull(badge);
        Assert.Equal(Glyphs.Regular.Heart24, badge.Glyph);
        Assert.Equal(Color.FromArgb("#FF2D95"), badge.IconColor);
        Assert.Equal("1.2k", badge.Text);
    }

    [Theory]
    [InlineData("START_DATE_DESC")]
    [InlineData("START_DATE")]
    public void BothStartDateSorts_ShowTheCalendarGlyphAndTheYear(string sort)
    {
        // Ascending and descending share a badge: only the ordering differs, not the metric.
        var badge = MediaMetricBadges.ForMediaSort(Media(year: 2011), sort);

        Assert.NotNull(badge);
        Assert.Equal(Glyphs.Regular.Calendar24, badge.Glyph);
        Assert.Equal(Color.FromArgb("#00C2FF"), badge.IconColor);
        Assert.Equal("2011", badge.Text);
    }

    [Fact]
    public void EachMetricSort_UsesADistinctGlyph()
    {
        // The whole point of a per-sort badge is that the reader can tell which metric they are
        // looking at without reading the sort control, so two sorts sharing a glyph is a defect.
        var media = Media(popularity: 1, trending: 1, averageScore: 1, favourites: 1, year: 2000);
        string[] sorts = ["POPULARITY_DESC", "TRENDING_DESC", "SCORE_DESC", "FAVOURITES_DESC", "START_DATE_DESC"];

        var glyphs = sorts
            .Select(sort => MediaMetricBadges.ForMediaSort(media, sort)!.Glyph)
            .ToList();

        Assert.Equal(glyphs.Count, glyphs.Distinct().Count());
    }

    // ── The always-render fallbacks ──────────────────────────────────

    [Theory]
    [InlineData("POPULARITY_DESC")]
    [InlineData("TRENDING_DESC")]
    [InlineData("FAVOURITES_DESC")]
    public void ACountSortWithNoData_StillRendersReadingZero(string sort)
    {
        // Blank would look like a rendering bug on a list sorted by this very metric.
        var badge = MediaMetricBadges.ForMediaSort(Media(), sort);

        Assert.NotNull(badge);
        Assert.Equal("0", badge.Text);
    }

    [Theory]
    [InlineData("SCORE_DESC")]
    [InlineData("START_DATE_DESC")]
    public void AScoreOrYearWithNoData_StillRendersReadingDash(string sort)
    {
        // A dash rather than "0": an unscored show is not a show that scored zero, and year 0
        // would be nonsense.
        var badge = MediaMetricBadges.ForMediaSort(Media(), sort);

        Assert.NotNull(badge);
        Assert.Equal("—", badge.Text);
    }

    [Fact]
    public void AZeroScore_CountsAsMissingRatherThanAsAScoreOfZero()
    {
        // AniList sends 0 for "not yet scored", which HasScore treats as absent.
        var badge = MediaMetricBadges.ForMediaSort(Media(averageScore: 0), "SCORE_DESC");

        Assert.NotNull(badge);
        Assert.Equal("—", badge.Text);
    }

    // ── Title sort falls back to the media format ────────────────────

    [Fact]
    public void TitleSort_HasNoNumericMetric_SoItShowsTheFormatInstead()
    {
        var badge = MediaMetricBadges.ForMediaSort(Media(format: "TV_SHORT"), "TITLE_ROMAJI");

        Assert.NotNull(badge);
        Assert.Equal(MediaFormatIcons.GlyphFor("TV_SHORT"), badge.Glyph);
        Assert.Equal(Color.FromArgb("#AF52DE"), badge.IconColor);
        Assert.Equal("TV SHORT", badge.Text);
    }

    [Fact]
    public void TitleSort_WithAFormatTheIconMapDoesNotKnow_FallsBackToTheGenericGlyph()
    {
        // A new AniList format should still render its name rather than dropping the badge.
        var badge = MediaMetricBadges.ForMediaSort(Media(format: "AUDIO_DRAMA"), "TITLE_ROMAJI");

        Assert.NotNull(badge);
        Assert.Equal(Glyphs.Regular.MoviesAndTv24, badge.Glyph);
        Assert.Equal("AUDIO DRAMA", badge.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TitleSort_WithNoFormatAtAll_ShowsNoBadge(string? format)
    {
        // The one case that is genuinely empty: there is no metric and nothing to fall back to.
        Assert.Null(MediaMetricBadges.ForMediaSort(Media(format: format), "TITLE_ROMAJI"));
    }
}
