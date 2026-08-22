using System.Globalization;

namespace AniSprinkles.Converters;

/// <summary>
/// Maps the viewer's "is favorited" bool to the heart glyph: the filled heart when favorited,
/// the outline heart when not. Must be paired with <see cref="FavouriteHeartFontConverter"/> on the
/// same <c>FontImageSource</c> — the filled and outline glyphs live in different font files, so the
/// FontFamily has to switch in lock-step with the glyph.
/// </summary>
public sealed class FavouriteHeartGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Glyphs.Filled.Heart24 : Glyphs.Regular.Heart24;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Companion to <see cref="FavouriteHeartGlyphConverter"/>: returns the Filled font family when
/// favorited and the Regular font family when not, so the glyph codepoint resolves correctly.
/// </summary>
public sealed class FavouriteHeartFontConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Glyphs.Filled.FontFamily : Glyphs.Regular.FontFamily;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
