using System.Globalization;

namespace AniSprinkles.Utilities;

public static class FuzzyDateFormatter
{
    public static string? Format(MediaDate? date, bool includeYear = true)
    {
        if (date is null)
        {
            return null;
        }

        var hasYear = date.Year is > 0;
        var hasMonth = date.Month is >= 1 and <= 12;
        var hasDay = date.Day is >= 1 and <= 31;

        if (!hasYear && !hasMonth)
        {
            return null;
        }

        if (hasMonth && hasDay)
        {
            // Year is filler when omitted — the formatter only reads month/day.
            var stamp = new DateOnly(hasYear ? date.Year!.Value : 2000, date.Month!.Value, date.Day!.Value);
            return hasYear && includeYear
                ? stamp.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)
                : stamp.ToString("MMM d", CultureInfo.InvariantCulture);
        }

        if (hasMonth)
        {
            var stamp = new DateOnly(hasYear ? date.Year!.Value : 2000, date.Month!.Value, 1);
            return hasYear && includeYear
                ? stamp.ToString("MMM yyyy", CultureInfo.InvariantCulture)
                : stamp.ToString("MMM", CultureInfo.InvariantCulture);
        }

        return includeYear ? date.Year!.Value.ToString(CultureInfo.InvariantCulture) : null;
    }
}
