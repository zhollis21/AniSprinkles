namespace AniSprinkles.Models;

/// <summary>
/// AniList's <c>MediaType</c>, as the two values this app distinguishes (#12). Light novels and
/// one-shots are <see cref="Manga"/> — AniList files all three under <c>MANGA</c> and separates
/// them only by <c>format</c>.
/// </summary>
public enum MediaKind
{
    Anime,
    Manga,
}

public static class MediaKindExtensions
{
    /// <summary>The value AniList's <c>MediaType</c> enum expects.</summary>
    public static string ToAniListType(this MediaKind kind) => kind == MediaKind.Manga ? "MANGA" : "ANIME";

    /// <summary>Parses AniList's <c>MediaType</c>. Anything unrecognised — including null, which is
    /// what queries that never selected <c>type</c> leave behind — reads as anime.</summary>
    public static MediaKind ParseMediaKind(string? type) =>
        string.Equals(type, "MANGA", StringComparison.OrdinalIgnoreCase) ? MediaKind.Manga : MediaKind.Anime;
}
