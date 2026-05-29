namespace AniSprinkles.Utilities;

public static class YearsActiveFormatter
{
    public static string? Format(IReadOnlyList<int>? yearsActive, MediaDate? dateOfDeath)
    {
        var deathYear = dateOfDeath?.Year is > 0 ? dateOfDeath.Year : (int?)null;
        var startYear = yearsActive is { Count: > 0 } && yearsActive[0] > 0 ? yearsActive[0] : (int?)null;
        var endYear = yearsActive is { Count: > 1 } && yearsActive[1] > 0 ? yearsActive[1] : (int?)null;

        // Fold dateOfDeath into the end-year when AniList didn't fill yearsActive[1] itself —
        // a deceased staff is by definition no longer "present".
        endYear ??= deathYear;

        if (startYear is null && deathYear is null)
        {
            return null;
        }

        if (startYear is null)
        {
            return $"(deceased {deathYear})";
        }

        if (endYear is null)
        {
            return $"{startYear}–present";
        }

        return $"{startYear}–{endYear}";
    }
}
