using System.Globalization;
using AniSprinkles.PageModels;
using Microsoft.Maui.Graphics;

namespace AniSprinkles.Converters;

/// <summary>
/// Returns a rainbow accent color when the bound <see cref="SortField"/> matches the
/// <c>ConverterParameter</c>, and Gray600 otherwise. Used to highlight the active sort chip's border.
/// </summary>
public sealed class SortFieldStrokeConverter : IValueConverter
{
    private static readonly RainbowAccentConverter RainbowConverter = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SortField current
            && parameter is string name
            && Enum.TryParse<SortField>(name, out var target))
        {
            if (current == target)
            {
                // Selected: use rainbow accent color
                return RainbowConverter.Convert(name, targetType, null, culture);
            }
        }

        // Not selected: use Gray600
        if (Application.Current?.Resources.TryGetValue("Gray600", out var gray) == true && gray is Color c)
        {
            return c;
        }

        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
