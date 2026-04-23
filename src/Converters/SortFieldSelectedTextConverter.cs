using System.Globalization;

namespace AniSprinkles.Converters;

/// <summary>
/// Returns White when the bound value matches the <c>ConverterParameter</c> (selected),
/// and the rainbow accent color for that parameter otherwise (unselected).
/// Used for dropdown items where the selected item needs inverted text/icon colors.
/// </summary>
public sealed class SortFieldSelectedTextConverter : IValueConverter
{
    private static readonly RainbowAccentConverter RainbowConverter = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not null && parameter is string name)
        {
            var currentName = value.ToString();
            if (string.Equals(currentName, name, StringComparison.Ordinal))
            {
                return Colors.White;
            }
        }

        // Not selected: use rainbow accent color for this parameter
        if (parameter is string p)
        {
            return RainbowConverter.Convert(p, targetType, null, culture);
        }

        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
