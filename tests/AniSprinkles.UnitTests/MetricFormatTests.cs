namespace AniSprinkles.UnitTests;

public class MetricFormatTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData(0, "")]
    [InlineData(-5, "")]
    [InlineData(1, "1")]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(1200, "1.2k")]
    [InlineData(1250, "1.3k")]   // rounds to one decimal
    [InlineData(90457, "90.5k")]
    [InlineData(999_949, "999.9k")] // last value the k tier can render without rounding to "1000k"
    [InlineData(999_950, "1M")]     // promoted early — the k tier would round it to "1000k"
    [InlineData(1_000_000, "1M")]
    [InlineData(1_010_268, "1M")]   // the All Time Popular case that showed as "1010.3k"
    [InlineData(1_250_000, "1.3M")] // rounds to one decimal, like the k tier
    [InlineData(2_100_000, "2.1M")]
    public void Compact_formats_counts(int? value, string expected)
        => Assert.Equal(expected, MetricFormat.Compact(value));
}
