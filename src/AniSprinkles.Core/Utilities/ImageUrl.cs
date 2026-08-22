namespace AniSprinkles.Utilities;

public static class ImageUrl
{
    // AniList serves a default placeholder image (a URL ending in "default.jpg") for characters/staff/media
    // that have no real photo or cover, rather than returning null. Treat those — and null/empty — as "no
    // image" so the app shows its own placeholder instead of AniList's grey "no image" graphic.
    public static bool IsReal(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && !url.EndsWith("default.jpg", StringComparison.OrdinalIgnoreCase);
}
