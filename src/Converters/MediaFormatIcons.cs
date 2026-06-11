namespace AniSprinkles.Converters;

/// <summary>
/// Maps an AniList media format string to its Fluent icon glyph. Single source of truth shared by
/// <see cref="MediaFormatIconConverter"/> (the cover overlay badge) and the carousel metric badges
/// (<see cref="AniSprinkles.PageModels.MediaMetricBadges"/>) so the two never drift.
/// </summary>
public static class MediaFormatIcons
{
    public static string? GlyphFor(string? format) => format switch
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
