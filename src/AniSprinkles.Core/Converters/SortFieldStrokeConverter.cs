using System.Globalization;

namespace AniSprinkles.Converters;

/// <summary>
/// Returns a rainbow accent color when the bound value's string representation matches the
/// <c>ConverterParameter</c>, and a neutral gray otherwise. Used to mark the active chip in a
/// selectable chip row: its border, and (through a second instance with
/// <see cref="UnselectedColor"/> set) its text (#195).
/// </summary>
public sealed class SortFieldStrokeConverter : IValueConverter
{
    private static readonly RainbowAccentConverter RainbowConverter = new();

    /// <summary>
    /// The color for a chip that is not selected. Unset, it is the app's Gray400, which is what a
    /// border wants; chip text sets a lighter gray so the unselected choices stay readable.
    /// </summary>
    public Color? UnselectedColor { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not null && parameter is string name)
        {
            var currentName = value.ToString();
            if (string.Equals(currentName, name, StringComparison.Ordinal))
            {
                // Selected: use rainbow accent color
                return RainbowConverter.Convert(name, targetType, null, culture);
            }
        }

        if (UnselectedColor is not null)
        {
            return UnselectedColor;
        }

        // Not selected: use Gray400 for readable contrast on dark backgrounds
        if (Application.Current?.Resources.TryGetValue("Gray400", out var gray) == true && gray is Color c)
        {
            return c;
        }

        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
