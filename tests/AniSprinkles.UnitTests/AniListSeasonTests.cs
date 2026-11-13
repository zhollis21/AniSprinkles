namespace AniSprinkles.UnitTests;

public class AniListSeasonTests
{
    [TestCase(1, "WINTER")]
    [TestCase(3, "WINTER")]
    [TestCase(4, "SPRING")]
    [TestCase(6, "SPRING")]
    [TestCase(7, "SUMMER")]
    [TestCase(9, "SUMMER")]
    [TestCase(10, "FALL")]
    [TestCase(12, "FALL")]
    public void Current_maps_month_to_season(int month, string expected)
    {
        var (season, year) = AniListSeason.Current(new DateTimeOffset(2026, month, 15, 0, 0, 0, TimeSpan.Zero));

        Assert.That(season, Is.EqualTo(expected));
        Assert.That(year, Is.EqualTo(2026));
    }

    [TestCase(1, "SPRING", 2026)]
    [TestCase(3, "SPRING", 2026)]  // last month of WINTER still points at SPRING
    [TestCase(4, "SUMMER", 2026)]
    [TestCase(6, "SUMMER", 2026)]
    [TestCase(7, "FALL", 2026)]
    [TestCase(9, "FALL", 2026)]
    [TestCase(10, "WINTER", 2027)] // FALL wraps to next year's WINTER
    [TestCase(12, "WINTER", 2027)]
    public void Next_steps_one_season_and_wraps_the_year(int month, string expectedSeason, int expectedYear)
    {
        var (season, year) = AniListSeason.Next(new DateTimeOffset(2026, month, 15, 0, 0, 0, TimeSpan.Zero));

        Assert.That(season, Is.EqualTo(expectedSeason));
        Assert.That(year, Is.EqualTo(expectedYear));
    }
}
