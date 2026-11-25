namespace AniSprinkles.Models;

/// <summary>
/// A character's or staff member's height, from AniList's <c>height</c> field (#238).
/// </summary>
public class Height
{
    /// <summary>
    /// The height when first introduced, for someone whose height changes later. AniList leaves
    /// <see cref="Min"/> and <see cref="Max"/> null when this is set.
    /// </summary>
    public HeightValue? Initial { get; set; }

    /// <summary>The lowest height. Equal to <see cref="Max"/> when there is only one.</summary>
    public HeightValue? Min { get; set; }

    /// <summary>The highest height. Equal to <see cref="Min"/> when there is only one.</summary>
    public HeightValue? Max { get; set; }
}

public class HeightValue
{
    /// <summary>Centimetres, which can be fractional (169.5).</summary>
    public double? Metric { get; set; }

    /// <summary>Feet and inches as AniList words them (5'7").</summary>
    public string? Imperial { get; set; }
}
