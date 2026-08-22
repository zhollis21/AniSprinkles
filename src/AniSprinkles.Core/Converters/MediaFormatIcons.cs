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
        "TV" or "TV_SHORT" => Glyphs.Regular.Tv24,
        "MOVIE" => Glyphs.Regular.MoviesAndTv24,
        "OVA" => Glyphs.Regular.VideoShort24,
        "ONA" => Glyphs.Regular.GlobeVideo24,
        "SPECIAL" => Glyphs.Regular.Sparkle24,
        "MUSIC" => Glyphs.Regular.MusicNote224,
        "MANGA" => Glyphs.Regular.BookOpen24,
        "NOVEL" => Glyphs.Regular.Book24,
        "ONE_SHOT" => Glyphs.Regular.DocumentOnePage24,
        _ => null,
    };
}
