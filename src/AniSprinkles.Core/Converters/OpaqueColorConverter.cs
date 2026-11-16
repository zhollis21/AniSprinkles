using System.Globalization;

namespace AniSprinkles.Converters;

/// <summary>
/// The bound colour at full opacity. The Switch style's On state uses it to paint the thumb in the
/// same hue as the switch's own tinted track (#172), so a switch only declares its colour once, on
/// <c>OnColor</c>. A non-colour gives null, which leaves the control on its default.
/// </summary>
public sealed class OpaqueColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Color color ? color.WithAlpha(1f) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
