using System.Globalization;
using AniSprinkles.PageModels;

namespace AniSprinkles.Converters;

/// <summary>
/// Returns full opacity (1.0) when the bound <see cref="SortField"/> matches the
/// <c>ConverterParameter</c> (parsed as a <see cref="SortField"/> name), and a
/// dimmed value (0.4) otherwise.  Used to visually highlight the active sort chip.
/// </summary>
public sealed class SortFieldOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SortField current
            && parameter is string name
            && Enum.TryParse<SortField>(name, out var target))
        {
            return current == target ? 1.0 : 0.6;
        }

        return 0.4;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
