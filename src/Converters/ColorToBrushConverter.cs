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
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color color ? new SolidColorBrush(color) : new SolidColorBrush(Colors.Transparent);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
