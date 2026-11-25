using System.Globalization;

namespace AniSprinkles.Utilities;

/// <summary>
/// Words AniList's <c>height</c> field as a bio stat row, the way anilist.co does:
/// <c>172 cm (5'8")</c>, or <c>170 - 183 cm (5'7" - 6'0")</c> for a range (#238).
/// </summary>
public static class HeightFormatter
{
    public const string Label = "Height";

    /// <summary>For someone whose height changes after they are introduced.</summary>
    public const string InitialLabel = "Initial Height";

    public static DescriptionStatRow? ToStatRow(Height? height)
    {
        if (height is null)
        {
            return null;
        }

        var label = Label;
        var min = height.Min ?? height.Max;
        var max = height.Max ?? height.Min;
        if (height.Initial is not null)
        {
            label = InitialLabel;
            min = max = height.Initial;
        }

        var metric = Range(Centimetres(min?.Metric), Centimetres(max?.Metric));
        if (metric is null)
        {
            return null;
        }

        var imperial = Range(min?.Imperial, max?.Imperial);

        return new DescriptionStatRow
        {
            Label = label,
            Value = imperial is null ? $"{metric} cm" : $"{metric} cm ({imperial})",
        };
    }

    // AniList reports a single height as an equal min and max. Half a range is dropped rather than
    // shown, because on its own it reads as the whole answer.
    private static string? Range(string? min, string? max)
    {
        if (string.IsNullOrWhiteSpace(min) || string.IsNullOrWhiteSpace(max))
        {
            return null;
        }

        return string.Equals(min, max, StringComparison.Ordinal) ? min : $"{min} - {max}";
    }

    private static string? Centimetres(double? value)
        => value?.ToString(CultureInfo.InvariantCulture);
}
