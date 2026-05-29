namespace AniSprinkles.Utilities;

public static class BirthdayChecker
{
    public static bool IsBirthdayToday(MediaDate? date, DateTime today)
    {
        if (date is null || date.Month is null || date.Day is null)
        {
            return false;
        }

        var month = date.Month.Value;
        var day = date.Day.Value;

        // Feb 29 birthdays celebrate on Feb 28 in non-leap years (Microsoft Calendar / iOS Reminders both do this).
        if (month == 2 && day == 29 && !DateTime.IsLeapYear(today.Year))
        {
            return today.Month == 2 && today.Day == 28;
        }

        return today.Month == month && today.Day == day;
    }
}
