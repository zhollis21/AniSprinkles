using System.Globalization;
using IconFont.Maui.FluentIcons;

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
        => value is true ? FluentIconsFilled.Heart24 : FluentIconsRegular.Heart24;

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
        => value is true ? FluentIconsFilled.FontFamily : FluentIconsRegular.FontFamily;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
