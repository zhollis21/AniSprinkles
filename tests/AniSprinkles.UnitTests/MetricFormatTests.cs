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
    public void Compact_formats_counts(int? value, string expected)
        => Assert.Equal(expected, MetricFormat.Compact(value));
}
