namespace AniSprinkles.UnitTests;

public class AniListSeasonTests
{
    [Theory]
    [InlineData(1, "WINTER")]
    [InlineData(3, "WINTER")]
    [InlineData(4, "SPRING")]
    [InlineData(6, "SPRING")]
    [InlineData(7, "SUMMER")]
    [InlineData(9, "SUMMER")]
    [InlineData(10, "FALL")]
    [InlineData(12, "FALL")]
    public void Current_maps_month_to_season(int month, string expected)
    {
        var (season, year) = AniListSeason.Current(new DateTimeOffset(2026, month, 15, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(expected, season);
        Assert.Equal(2026, year);
    }

    [Theory]
    [InlineData(1, "SPRING", 2026)]
    [InlineData(3, "SPRING", 2026)]  // last month of WINTER still points at SPRING
    [InlineData(4, "SUMMER", 2026)]
    [InlineData(6, "SUMMER", 2026)]
    [InlineData(7, "FALL", 2026)]
    [InlineData(9, "FALL", 2026)]
    [InlineData(10, "WINTER", 2027)] // FALL wraps to next year's WINTER
    [InlineData(12, "WINTER", 2027)]
    public void Next_steps_one_season_and_wraps_the_year(int month, string expectedSeason, int expectedYear)
    {
        var (season, year) = AniListSeason.Next(new DateTimeOffset(2026, month, 15, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal(expectedSeason, season);
        Assert.Equal(expectedYear, year);
    }
}
