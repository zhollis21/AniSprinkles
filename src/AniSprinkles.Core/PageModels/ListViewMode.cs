namespace AniSprinkles.PageModels;

public enum ListViewMode
{
    Standard,
    Large,
    Compact
}

/// <summary>
/// One device-wide list view mode, shared by Library and the media-browse (View All) lists so
/// switching the look on either page carries to the other. Signed-out users simply get the
/// default (Large) until they change it.
/// </summary>
public static class ListViewModePreference
{
    public const string Key = "anime_view_mode";

    public static ListViewMode Load(IPreferences preferences)
    {
        var saved = preferences.Get(Key, nameof(ListViewMode.Large));
        return Enum.TryParse<ListViewMode>(saved, out var mode) ? mode : ListViewMode.Large;
    }

    public static void Save(IPreferences preferences, ListViewMode mode)
        => preferences.Set(Key, mode.ToString());
}
