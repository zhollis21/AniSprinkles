using System.Globalization;

namespace AniSprinkles.Converters;

/// <summary>
/// Returns full opacity (1.0) when the bound value's string representation matches the
/// <c>ConverterParameter</c>, and a dimmed value (0.6) otherwise. Falls back to 0.4
/// when value is null. Used to visually highlight the active chip.
/// </summary>
public sealed class SortFieldOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not null && parameter is string name)
        {
            var currentName = value.ToString();
            return string.Equals(currentName, name, StringComparison.Ordinal) ? 1.0 : 0.6;
        }

        return 0.4;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
