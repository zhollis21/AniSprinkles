namespace AniSprinkles.UnitTests;

// ─────────────────────────────────────────────────────────────────────────────
// SpoilerHtmlProcessor
// ─────────────────────────────────────────────────────────────────────────────

public class SpoilerHtmlProcessorTests
{
    [Test]
    public void Process_NullOrEmpty_ReturnsEmpty()
    {
        Assert.That(SpoilerHtmlProcessor.Process(null, reveal: false), Is.EqualTo(string.Empty));
        Assert.That(SpoilerHtmlProcessor.Process("", reveal: true), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Process_NoSpoilers_ReturnsInputUnchanged()
    {
        var html = "<p>Just a regular description.</p>";
        Assert.That(SpoilerHtmlProcessor.Process(html, reveal: false), Is.EqualTo(html));
        Assert.That(SpoilerHtmlProcessor.Process(html, reveal: true), Is.EqualTo(html));
    }

    [Test]
    public void Process_Reveal_StripsMarkers()
    {
        var input = "Setup. ~!The villain is the butler.!~ End.";
        var revealed = SpoilerHtmlProcessor.Process(input, reveal: true);

        Assert.That(revealed, Does.Contain("The villain is the butler."));
        Assert.That(revealed, Does.Not.Contain("~!"));
        Assert.That(revealed, Does.Not.Contain("!~"));
    }

    [Test]
    public void Process_Hide_RedactsSpoilerContent()
    {
        var input = "Setup. ~!The villain is the butler.!~ End.";
        var hidden = SpoilerHtmlProcessor.Process(input, reveal: false);

        Assert.That(hidden, Does.Not.Contain("villain"));
        Assert.That(hidden, Does.Not.Contain("butler"));
        Assert.That(hidden, Does.Not.Contain("~!"));
        Assert.That(hidden, Does.Contain("Setup."));
        Assert.That(hidden, Does.Contain("End."));
    }

    [Test]
    public void Process_MultipleSpoilers_HandledIndependently()
    {
        var input = "A ~!secret one!~ middle ~!secret two!~ end";
        var revealed = SpoilerHtmlProcessor.Process(input, reveal: true);

        Assert.That(revealed, Does.Contain("secret one"));
        Assert.That(revealed, Does.Contain("secret two"));
        Assert.That(revealed, Does.Contain("middle"));
        Assert.That(revealed, Does.Not.Contain("~!"));
    }

    [Test]
    public void Process_SpoilerSpansNewlines_HiddenCorrectly()
    {
        var input = "Start ~!line one\nline two!~ end";
        var hidden = SpoilerHtmlProcessor.Process(input, reveal: false);

        Assert.That(hidden, Does.Not.Contain("line one"));
        Assert.That(hidden, Does.Not.Contain("line two"));
    }

    [Test]
    public void ContainsSpoilers_DetectsPresence()
    {
        Assert.That(SpoilerHtmlProcessor.ContainsSpoilers(null), Is.False);
        Assert.That(SpoilerHtmlProcessor.ContainsSpoilers(""), Is.False);
        Assert.That(SpoilerHtmlProcessor.ContainsSpoilers("plain text"), Is.False);
        Assert.That(SpoilerHtmlProcessor.ContainsSpoilers("text ~!hidden!~ more"), Is.True);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// FuzzyDateFormatter
// ─────────────────────────────────────────────────────────────────────────────

public class FuzzyDateFormatterTests
{
    [Test]
    public void Format_Null_ReturnsNull()
    {
        Assert.That(FuzzyDateFormatter.Format(null), Is.Null);
    }

    [Test]
    public void Format_FullDate_RendersFullForm()
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Year = 1989, Month = 7, Day = 15 });
        Assert.That(result, Is.EqualTo("Jul 15, 1989"));
    }

    [Test]
    public void Format_FullDate_NoYear_RendersMonthDay()
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Year = 1989, Month = 7, Day = 15 }, includeYear: false);
        Assert.That(result, Is.EqualTo("Jul 15"));
    }

    [Test]
    public void Format_MonthAndDayOnly_RendersMonthDay()
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Month = 3, Day = 15 });
        Assert.That(result, Is.EqualTo("Mar 15"));
    }

    [Test]
    public void Format_YearAndMonthOnly_RendersMonthYear()
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Year = 2003, Month = 11 });
        Assert.That(result, Is.EqualTo("Nov 2003"));
    }

    [Test]
    public void Format_YearOnly_RendersYear()
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Year = 2003 });
        Assert.That(result, Is.EqualTo("2003"));
    }

    [Test]
    public void Format_AllNull_ReturnsNull()
    {
        Assert.That(FuzzyDateFormatter.Format(new MediaDate()), Is.Null);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase(13)]
    public void Format_OutOfRangeMonth_IgnoresMonth(int month)
    {
        var result = FuzzyDateFormatter.Format(new MediaDate { Year = 1990, Month = month });
        Assert.That(result, Is.EqualTo("1990"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// YearsActiveFormatter
// ─────────────────────────────────────────────────────────────────────────────

public class YearsActiveFormatterTests
{
    [Test]
    public void Format_NullEverything_ReturnsNull()
    {
        Assert.That(YearsActiveFormatter.Format(null, null), Is.Null);
    }

    [Test]
    public void Format_EmptyEverything_ReturnsNull()
    {
        Assert.That(YearsActiveFormatter.Format([], null), Is.Null);
    }

    [Test]
    public void Format_SingleYear_ActivePresent()
    {
        Assert.That(YearsActiveFormatter.Format([1986], null), Is.EqualTo("1986–present"));
    }

    [Test]
    public void Format_TwoYears_ClosedRange()
    {
        Assert.That(YearsActiveFormatter.Format([1986, 2019], null), Is.EqualTo("1986–2019"));
    }

    [Test]
    public void Format_StartYear_PlusDateOfDeath_ClosedRange()
    {
        var dod = new MediaDate { Year = 2019 };
        Assert.That(YearsActiveFormatter.Format([1986], dod), Is.EqualTo("1986–2019"));
    }

    [Test]
    public void Format_NoYearsActive_DateOfDeathOnly_RendersDeceased()
    {
        var dod = new MediaDate { Year = 2019 };
        Assert.That(YearsActiveFormatter.Format([], dod), Is.EqualTo("(deceased 2019)"));
    }

    [Test]
    public void Format_TwoYears_OverridesDateOfDeath()
    {
        // Trust yearsActive[1] over dateOfDeath when both set; AniList sometimes has a retirement
        // year before death year and we want the active range, not the lifespan.
        var dod = new MediaDate { Year = 2019 };
        Assert.That(YearsActiveFormatter.Format([1986, 2010], dod), Is.EqualTo("1986–2010"));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// BirthdayChecker
// ─────────────────────────────────────────────────────────────────────────────

public class BirthdayCheckerTests
{
    [Test]
    public void IsBirthdayToday_Null_False()
    {
        Assert.That(BirthdayChecker.IsBirthdayToday(null, new DateTime(2026, 4, 30)), Is.False);
    }

    [Test]
    public void IsBirthdayToday_NoMonthOrDay_False()
    {
        Assert.That(BirthdayChecker.IsBirthdayToday(new MediaDate { Year = 1990 }, new DateTime(2026, 4, 30)), Is.False);
    }

    [Test]
    public void IsBirthdayToday_MatchingMonthDay_True()
    {
        var dob = new MediaDate { Year = 1989, Month = 4, Day = 30 };
        Assert.That(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2026, 4, 30)), Is.True);
    }

    [Test]
    public void IsBirthdayToday_MatchingMonthDay_NoYear_True()
    {
        var dob = new MediaDate { Month = 4, Day = 30 };
        Assert.That(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2026, 4, 30)), Is.True);
    }

    [Test]
    public void IsBirthdayToday_DifferentDay_False()
    {
        var dob = new MediaDate { Month = 4, Day = 29 };
        Assert.That(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2026, 4, 30)), Is.False);
    }

    [Test]
    public void IsBirthdayToday_Feb29_LeapYear_MatchesFeb29()
    {
        var dob = new MediaDate { Month = 2, Day = 29 };
        Assert.That(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2024, 2, 29)), Is.True);
    }

    [Test]
    public void IsBirthdayToday_Feb29_NonLeapYear_FallsBackToFeb28()
    {
        var dob = new MediaDate { Month = 2, Day = 29 };
        Assert.That(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2026, 2, 28)), Is.True);
        Assert.That(BirthdayChecker.IsBirthdayToday(dob, new DateTime(2026, 3, 1)), Is.False);
    }
}
