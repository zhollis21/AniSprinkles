namespace AniSprinkles.Utilities;

/// <summary>
/// Computes AniList's calendar-quarter seasons (WINTER Jan–Mar, SPRING Apr–Jun, SUMMER Jul–Sep,
/// FALL Oct–Dec) for the Discover queries, which must never hardcode season/year literals.
/// Callers pass the DI <see cref="TimeProvider"/>'s local now so tests can pin dates.
/// </summary>
public static class AniListSeason
{
    public static (string Season, int Year) Current(DateTimeOffset now) => (SeasonOf(now.Month), now.Year);

    public static (string Season, int Year) Next(DateTimeOffset now)
    {
        // Step to the first month of the next quarter; FALL rolls into next year's WINTER.
        var firstMonthOfNextQuarter = now.Month + (3 - (now.Month - 1) % 3);
        return firstMonthOfNextQuarter > 12
            ? ("WINTER", now.Year + 1)
            : (SeasonOf(firstMonthOfNextQuarter), now.Year);
    }

    private static string SeasonOf(int month) => month switch
    {
        <= 3 => "WINTER",
        <= 6 => "SPRING",
        <= 9 => "SUMMER",
        _ => "FALL",
    };
}
