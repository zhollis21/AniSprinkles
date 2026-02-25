using System.Globalization;

namespace AniSprinkles.Converters;

/// <summary>
/// Returns <c>true</c> when the bound value's string representation matches the
/// <c>ConverterParameter</c>. Used to show/hide the sort direction arrow on the active chip.
/// </summary>
public sealed class SortFieldActiveConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not null && parameter is string name)
        {
            return string.Equals(value.ToString(), name, StringComparison.Ordinal);
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
