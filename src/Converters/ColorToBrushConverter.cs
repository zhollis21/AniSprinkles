using System.Collections.Concurrent;
using System.Globalization;

namespace AniSprinkles.Converters;

/// <summary>
/// Wraps a bound <see cref="Color"/> in a <see cref="SolidColorBrush"/> for Brush-typed targets.
/// Needed for Border fills: the app's implicit Border style sets <c>Background</c> (a Brush), and
/// MAUI's Background always wins over BackgroundColor — so dynamic Border colors must bind
/// Background with a Brush value. (Models expose Colors, not Brushes, because Brush types live in
/// MAUI Controls, which the link-compiled unit-test project can't reference.)
/// </summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    // The app uses a small, fixed set of status colors, and this converter runs per pill in
    // scrolling lists — cache one brush per Color so a recycled cell reuses the instance instead
    // of allocating. Color has value equality (RGBA), so it's a sound key. Bound brushes are
    // read-only in MAUI, so sharing an instance across cells is safe.
    private static readonly ConcurrentDictionary<Color, SolidColorBrush> BrushCache = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        // Brush.Transparent is a shared static — avoids a brush for the common no-status path
        // (every card without a list-status pill hits this).
        => value is Color color
            ? BrushCache.GetOrAdd(color, static c => new SolidColorBrush(c))
            : Brush.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
