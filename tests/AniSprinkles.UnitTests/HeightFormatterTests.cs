using System.Globalization;

namespace AniSprinkles.UnitTests;

public class HeightFormatterTests
{
    [Test]
    public void ToStatRow_EqualMinAndMax_IsASingleValue()
    {
        var row = HeightFormatter.ToStatRow(Between(Value(172, "5'8\""), Value(172, "5'8\"")));

        Assert.That(row?.Label, Is.EqualTo("Height"));
        Assert.That(row?.Value, Is.EqualTo("172 cm (5'8\")"));
        Assert.That(row?.IsRowSpoiler, Is.False);
        Assert.That(row?.IsValueSpoiler, Is.False);
    }

    [Test]
    public void ToStatRow_DifferentMinAndMax_IsARangeInBothUnits()
        // Eren Yeager, and how anilist.co words it.
        => Assert.That(
            HeightFormatter.ToStatRow(Between(Value(170, "5'7\""), Value(183, "6'0\"")))?.Value,
            Is.EqualTo("170 - 183 cm (5'7\" - 6'0\")"));

    [Test]
    public void ToStatRow_RangeTooNarrowForFeetAndInches_CollapsesOnlyTheImperialHalf()
        // Imperial is rounded to the nearest inch, so a real metric range can share one value.
        => Assert.That(
            HeightFormatter.ToStatRow(Between(Value(172, "5'8\""), Value(173, "5'8\"")))?.Value,
            Is.EqualTo("172 - 173 cm (5'8\")"));

    [Test]
    public void ToStatRow_Initial_SaysSoInTheLabel()
    {
        var row = HeightFormatter.ToStatRow(new Height { Initial = Value(145, "4'9\"") });

        Assert.That(row?.Label, Is.EqualTo("Initial Height"));
        Assert.That(row?.Value, Is.EqualTo("145 cm (4'9\")"));
    }

    [TestCase("en-US")]
    [TestCase("de-DE")]
    public void ToStatRow_FractionalCentimetres_KeepTheirPointInEveryCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            Assert.That(
                HeightFormatter.ToStatRow(Between(Value(99.5, "3'3\""), Value(99.5, "3'3\"")))?.Value,
                Is.EqualTo("99.5 cm (3'3\")"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Test]
    public void ToStatRow_NoImperial_IsMetricOnly()
        => Assert.That(
            HeightFormatter.ToStatRow(Between(Value(172, null), Value(172, null)))?.Value,
            Is.EqualTo("172 cm"));

    [Test]
    public void ToStatRow_ImperialOnOneEndOnly_DoesNotPassItOffAsTheRange()
        => Assert.That(
            HeightFormatter.ToStatRow(Between(Value(170, "5'7\""), Value(183, null)))?.Value,
            Is.EqualTo("170 - 183 cm"));

    [Test]
    public void ToStatRow_OnlyOneEnd_IsASingleValue()
    {
        Assert.That(
            HeightFormatter.ToStatRow(new Height { Min = Value(172, "5'8\"") })?.Value,
            Is.EqualTo("172 cm (5'8\")"));
        Assert.That(
            HeightFormatter.ToStatRow(new Height { Max = Value(172, "5'8\"") })?.Value,
            Is.EqualTo("172 cm (5'8\")"));
    }

    [Test]
    public void ToStatRow_NothingToShow_IsNull()
    {
        Assert.That(HeightFormatter.ToStatRow(null), Is.Null);
        Assert.That(HeightFormatter.ToStatRow(new Height()), Is.Null);
        Assert.That(HeightFormatter.ToStatRow(Between(Value(null, null), Value(null, null))), Is.Null);
    }

    private static Height Between(HeightValue min, HeightValue max) => new() { Min = min, Max = max };

    private static HeightValue Value(double? metric, string? imperial)
        => new() { Metric = metric, Imperial = imperial };
}
