namespace AniSprinkles.UnitTests;

// ─────────────────────────────────────────────────────────────────────────────
// SpoilerHtmlProcessor
// ─────────────────────────────────────────────────────────────────────────────

public class SpoilerHtmlProcessorTests
{
    [Fact]
    public void Process_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, SpoilerHtmlProcessor.Process(null, reveal: false));
        Assert.Equal(string.Empty, SpoilerHtmlProcessor.Process("", reveal: true));
    }

    [Fact]
    public void Process_NoSpoilers_ReturnsInputUnchanged()
    {
        var html = "<p>Just a regular description.</p>";
        Assert.Equal(html, SpoilerHtmlProcessor.Process(html, reveal: false));
        Assert.Equal(html, SpoilerHtmlProcessor.Process(html, reveal: true));
    }

    [Fact]
    public void Process_Reveal_StripsMarkers()
    {
        var input = "Setup. ~!The villain is the butler.!~ End.";
        var revealed = SpoilerHtmlProcessor.Process(input, reveal: true);

        Assert.Contains("The villain is the butler.", revealed);
        Assert.DoesNotContain("~!", revealed);
        Assert.DoesNotContain("!~", revealed);
    }

    [Fact]
    public void Process_Hide_RedactsSpoilerContent()
    {
        var input = "Setup. ~!The villain is the butler.!~ End.";
        var hidden = SpoilerHtmlProcessor.Process(input, reveal: false);

        Assert.DoesNotContain("villain", hidden);
        Assert.DoesNotContain("butler", hidden);
        Assert.DoesNotContain("~!", hidden);
        Assert.Contains("Setup.", hidden);
        Assert.Contains("End.", hidden);
    }

    [Fact]
    public void Process_MultipleSpoilers_HandledIndependently()
    {
        var input = "A ~!secret one!~ middle ~!secret two!~ end";
        var revealed = SpoilerHtmlProcessor.Process(input, reveal: true);

        Assert.Contains("secret one", revealed);
        Assert.Contains("secret two", revealed);
        Assert.Contains("middle", revealed);
        Assert.DoesNotContain("~!", revealed);
    }

    [Fact]
    public void Process_SpoilerSpansNewlines_HiddenCorrectly()
    {
        var input = "Start ~!line one\nline two!~ end";
        var hidden = SpoilerHtmlProcessor.Process(input, reveal: false);

        Assert.DoesNotContain("line one", hidden);
        Assert.DoesNotContain("line two", hidden);
    }

    [Fact]
    public void ContainsSpoilers_DetectsPresence()
    {
        Assert.False(SpoilerHtmlProcessor.ContainsSpoilers(null));
        Assert.False(SpoilerHtmlProcessor.ContainsSpoilers(""));
        Assert.False(SpoilerHtmlProcessor.ContainsSpoilers("plain text"));
        Assert.True(SpoilerHtmlProcessor.ContainsSpoilers("text ~!hidden!~ more"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// FuzzyDateFormatter
// ─────────────────────────────────────────────────────────────────────────────

public class FuzzyDateFormatterTests
{
    [Fact]
    public void Format_Null_ReturnsNull()
    {
        Assert.Null(FuzzyDateFormatter.Format(null));
    }

    [Fact]
    public void Format_FullDate_RendersFullForm()
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Year = 1989, Month = 7, Day = 15 });
        Assert.Equal("Jul 15, 1989", result);
    }

    [Fact]
    public void Format_FullDate_NoYear_RendersMonthDay()
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Year = 1989, Month = 7, Day = 15 }, includeYear: false);
        Assert.Equal("Jul 15", result);
    }

    [Fact]
    public void Format_MonthAndDayOnly_RendersMonthDay()
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Month = 3, Day = 15 });
        Assert.Equal("Mar 15", result);
    }

    [Fact]
    public void Format_YearAndMonthOnly_RendersMonthYear()
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Year = 2003, Month = 11 });
        Assert.Equal("Nov 2003", result);
    }

    [Fact]
    public void Format_YearOnly_RendersYear()
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Year = 2003 });
        Assert.Equal("2003", result);
    }

    [Fact]
    public void Format_AllNull_ReturnsNull()
    {
        Assert.Null(FuzzyDateFormatter.Format(new MediaDate()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(13)]
    public void Format_OutOfRangeMonth_IgnoresMonth(int month)
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Year = 1990, Month = month });
        Assert.Equal("1990", result);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// YearsActiveFormatter
// ─────────────────────────────────────────────────────────────────────────────

public class YearsActiveFormatterTests
{
    [Fact]
    public void Format_NullEverything_ReturnsNull()
    {
        Assert.Null(YearsActiveFormatter.Format(null, null));
    }

    [Fact]
    public void Format_EmptyEverything_ReturnsNull()
    {
        Assert.Null(YearsActiveFormatter.Format([], null));
    }

    [Fact]
    public void Format_SingleYear_ActivePresent()
    {
        Assert.Equal("1986–present", YearsActiveFormatter.Format([1986], null));
    }

    [Fact]
    public void Format_TwoYears_ClosedRange()
    {
        Assert.Equal("1986–2019", YearsActiveFormatter.Format([1986, 2019], null));
    }

    [Fact]
    public void Format_StartYear_PlusDateOfDeath_ClosedRange()
    {
        var dod = new MediaDate { Year = 2019 };
        Assert.Equal("1986–2019", YearsActiveFormatter.Format([1986], dod));
    }

    [Fact]
    public void Format_NoYearsActive_DateOfDeathOnly_RendersDeceased()
    {
        var dod = new MediaDate { Year = 2019 };
        Assert.Equal("(deceased 2019)", YearsActiveFormatter.Format([], dod));
    }

    [Fact]
    public void Format_TwoYears_OverridesDateOfDeath()
    {
        // Trust yearsActive[1] over dateOfDeath when both set; AniList sometimes has a retirement
        // year before death year and we want the active range, not the lifespan.
        var dod = new MediaDate { Year = 2019 };
        Assert.Equal("1986–2010", YearsActiveFormatter.Format([1986, 2010], dod));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BirthdayChecker
// ─────────────────────────────────────────────────────────────────────────────

public class BirthdayCheckerTests
{
    [Fact]
    public void IsBirthdayToday_Null_False()
    {
        Assert.False(BirthdayChecker.IsBirthdayToday(null, new DateTime(2026, 4, 30)));
    }

    [Fact]
    public void IsBirthdayToday_NoMonthOrDay_False()
    {
        Assert.False(BirthdayChecker.IsBirthdayToday(new MediaDate { Year = 1990 }, new DateTime(2026, 4, 30)));
    }

    [Fact]
    public void IsBirthdayToday_MatchingMonthDay_True()
    {
        var dob = new MediaDate { Year = 1989, Month = 4, Day = 30 };
        Assert.True(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2026, 4, 30)));
    }

    [Fact]
    public void IsBirthdayToday_MatchingMonthDay_NoYear_True()
    {
        var dob = new MediaDate { Month = 4, Day = 30 };
        Assert.True(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2026, 4, 30)));
    }

    [Fact]
    public void IsBirthdayToday_DifferentDay_False()
    {
        var dob = new MediaDate { Month = 4, Day = 29 };
        Assert.False(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2026, 4, 30)));
    }

    [Fact]
    public void IsBirthdayToday_Feb29_LeapYear_MatchesFeb29()
    {
        var dob = new MediaDate { Month = 2, Day = 29 };
        Assert.True(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2024, 2, 29)));
    }

    [Fact]
    public void IsBirthdayToday_Feb29_NonLeapYear_FallsBackToFeb28()
    {
        var dob = new MediaDate { Month = 2, Day = 29 };
        Assert.True(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2026, 2, 28)));
        Assert.False(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2026, 3, 1)));
    }
}
