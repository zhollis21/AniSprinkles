using System.Globalization;
using IconFont.Maui.FluentIcons;

namespace AniSprinkles.Converters;

/// <summary>
/// Converts an AniList media format string to the corresponding Fluent Icon glyph.
/// </summary>
public class MediaFormatIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "TV" or "TV_SHORT" => FluentIconsFilled.VideoClip24,
            "MOVIE" => FluentIconsFilled.Filmstrip24,
            "OVA" or "ONA" or "SPECIAL" => FluentIconsFilled.PlayCircle24,
            "MUSIC" => FluentIconsFilled.MusicNote224,
            "MANGA" => FluentIconsFilled.BookOpen24,
            "NOVEL" => FluentIconsFilled.Book24,
            "ONE_SHOT" => FluentIconsFilled.DocumentOnePage24,
            _ => null,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
