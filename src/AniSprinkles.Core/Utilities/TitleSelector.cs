namespace AniSprinkles.Utilities;

/// <summary>
/// The title-language fallback chain, in one place (#141).
/// <para>
/// Before this it existed three times: <c>Media.DisplayTitle</c>, <c>RelatedMedia.DisplayTitle</c>,
/// and <c>AiringCheckWorker.SelectTitle</c> — whose doc comment asserted it matched the app UI with
/// nothing enforcing that. Two of the three had already drifted: the models disagreed on the
/// fallback string when a media has no title in any language.
/// </para>
/// <para>
/// The language is a parameter rather than read from <see cref="AppSettings"/> because the airing
/// worker cannot use it. The worker runs post-reboot before the app has launched, so
/// <c>AppSettings.Load()</c> may never have run, and <c>AppSettings.Storage</c> is <c>internal</c> to
/// Core besides. It reads the preference itself and passes the result in.
/// </para>
/// </summary>
public static class TitleSelector
{
    /// <summary>Shown when a media carries no title in any language. AniList allows this.</summary>
    public const string UnknownTitle = "Unknown Title";

    /// <summary>
    /// Picks the title for <paramref name="language"/>, falling back through the other two rather
    /// than showing nothing. The preferred language leads; the remaining order is fixed so the same
    /// media always resolves the same way.
    /// </summary>
    public static string Select(UserTitleLanguage language, string? romaji, string? english, string? native)
        => language switch
        {
            UserTitleLanguage.English => english ?? romaji ?? native ?? UnknownTitle,
            UserTitleLanguage.Native => native ?? romaji ?? english ?? UnknownTitle,
            _ => romaji ?? english ?? native ?? UnknownTitle,
        };
}
