namespace AniSprinkles.UnitTests;

public class MetricFormatTests
{
    [TestCase(null, "")]
    [TestCase(0, "")]
    [TestCase(-5, "")]
    [TestCase(1, "1")]
    [TestCase(999, "999")]
    [TestCase(1000, "1k")]
    [TestCase(1200, "1.2k")]
    [TestCase(1250, "1.3k")]   // rounds to one decimal
    [TestCase(90457, "90.5k")]
    [TestCase(999_949, "999.9k")] // last value the k tier can render without rounding to "1000k"
    [TestCase(999_950, "1M")]     // promoted early — the k tier would round it to "1000k"
    [TestCase(1_000_000, "1M")]
    [TestCase(1_010_268, "1M")]   // the All Time Popular case that showed as "1010.3k"
    [TestCase(1_250_000, "1.3M")] // rounds to one decimal, like the k tier
    [TestCase(2_100_000, "2.1M")]
    public void Compact_formats_counts(int? value, string expected)
        => Assert.That(MetricFormat.Compact(value), Is.EqualTo(expected));
}
