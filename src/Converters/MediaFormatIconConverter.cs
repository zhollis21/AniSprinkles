using System.Globalization;

namespace AniSprinkles.Converters;

/// <summary>
/// Converts an AniList media format string to the corresponding Fluent Icon glyph.
/// </summary>
public class MediaFormatIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => MediaFormatIcons.GlyphFor(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
