namespace AniSprinkles.Models;

/// <summary>
/// The unit a list entry's progress counter is currently measured in (#12).
/// <para>
/// Anime is always <see cref="Episode"/>. Manga is <see cref="Chapter"/> or <see cref="Volume"/>
/// depending on which counter the entry actually uses — see
/// <see cref="MediaListEntry.UsesVolumeProgress"/>. Every progress surface reads this rather than
/// assuming episodes, so the noun on screen matches the number next to it.
/// </para>
/// </summary>
public enum MediaProgressUnit
{
    Episode,
    Chapter,
    Volume,
}
