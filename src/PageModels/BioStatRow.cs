namespace AniSprinkles.PageModels;

/// <summary>
/// View-ready stat row. <see cref="LabelDisplay"/> / <see cref="ValueDisplay"/> already factor in
/// the spoiler-reveal state — they hold either the real strings or a placeholder bar so the view
/// can bind directly without per-cell converters.
/// </summary>
public sealed class BioStatRow
{
    public required string LabelDisplay { get; init; }
    public required string ValueDisplay { get; init; }
    public required bool IsValueSpoilerHidden { get; init; }
    public required bool IsLabelSpoilerHidden { get; init; }
}
