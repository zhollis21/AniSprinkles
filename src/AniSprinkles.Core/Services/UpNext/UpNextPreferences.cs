namespace AniSprinkles.Services;

/// <summary>
/// Device-local settings for the up-next offer (#103). Not AniList settings, so they are written
/// straight to <see cref="IPreferences"/> and never go through the Settings save to AniList.
/// </summary>
public static class UpNextPreferences
{
    public const string EnabledKey = "up_next_enabled";
    public const string StatusKey = "up_next_status";

    /// <summary>On until the user turns it off.</summary>
    public static bool IsEnabled(IPreferences preferences) => preferences.Get(EnabledKey, true);

    public static void SetEnabled(IPreferences preferences, bool enabled) => preferences.Set(EnabledKey, enabled);

    /// <summary>
    /// The list related entries go to: Watching/Reading (the default) or Planning. Anything else stored
    /// under the key reads as the default rather than letting a stray value add titles as Dropped.
    /// </summary>
    public static MediaListStatus DefaultStatus(IPreferences preferences)
        => preferences.Get(StatusKey, nameof(MediaListStatus.Current)) == nameof(MediaListStatus.Planning)
            ? MediaListStatus.Planning
            : MediaListStatus.Current;

    public static void SetDefaultStatus(IPreferences preferences, MediaListStatus status)
    {
        if (status is not (MediaListStatus.Current or MediaListStatus.Planning))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Up next adds to Watching or Planning only.");
        }

        preferences.Set(StatusKey, status.ToString());
    }
}
