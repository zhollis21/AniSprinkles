using System.Globalization;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.Converters;

/// <summary>
/// Returns a SolidColorBrush with a rainbow accent color (at reduced opacity) when
/// the bound value matches the <c>ConverterParameter</c>, and a Transparent brush
/// otherwise. Used to highlight the selected item's background in the status dropdown.
/// </summary>
public sealed class SortFieldBackgroundConverter : IValueConverter
{
    private static readonly RainbowAccentConverter RainbowConverter = new();
    private static readonly SolidColorBrush TransparentBrush = new(Colors.Transparent);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not null && parameter is string name)
        {
            var currentName = value.ToString();
            if (string.Equals(currentName, name, StringComparison.Ordinal))
            {
                if (RainbowConverter.Convert(name, targetType, null, culture) is Color c)
                {
                    return new SolidColorBrush(c.WithAlpha(0.10f));
                }
            }
        }

        return TransparentBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
