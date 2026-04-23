using System.Globalization;

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
            "TV" or "TV_SHORT" => FluentIconsRegular.Tv24,
            "MOVIE" => FluentIconsRegular.MoviesAndTv24,
            "OVA" => FluentIconsRegular.VideoShort24,
            "ONA" => FluentIconsRegular.GlobeVideo24,
            "SPECIAL" => FluentIconsRegular.Sparkle24,
            "MUSIC" => FluentIconsRegular.MusicNote224,
            "MANGA" => FluentIconsRegular.BookOpen24,
            "NOVEL" => FluentIconsRegular.Book24,
            "ONE_SHOT" => FluentIconsRegular.DocumentOnePage24,
            _ => null,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
