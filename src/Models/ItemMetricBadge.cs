using Microsoft.Maui.Graphics;

namespace AniSprinkles.Models;

// Pre-resolved metric badge attached to a list item.
// Built once per item per sort change in the PageModel so the XAML can use plain bindings
// instead of cross-template DataTriggers (which scale poorly across many cards).
public sealed class ItemMetricBadge
{
    public required string Glyph { get; init; }
    public required Color IconColor { get; init; }
    public required string Text { get; init; }
}
