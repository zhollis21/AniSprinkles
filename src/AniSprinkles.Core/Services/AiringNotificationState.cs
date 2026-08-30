using System.Text.Json;

namespace AniSprinkles.Services;

/// <summary>
/// The airing-notification subsystem's persisted state, and the only place its preference keys are
/// spelled (#141).
/// <para>
/// The keys used to be independent literals and private consts across three files spanning both
/// projects — written in <c>MyAnimePageModel</c>, read in <c>AiringCheckWorker</c>, removed in
/// <c>AiringNotificationService</c> and <c>SettingsPageModel</c>. Renaming one left the code
/// compiling, the tests green, and airing notifications silently dead, because no test could reach
/// both halves: <c>tests/</c> references Core only, and the worker lives in the MAUI app project.
/// </para>
/// <para>
/// Everything here is static over an injected <see cref="IPreferences"/>, following
/// <c>ListViewModePreference</c>. That keeps it usable from the worker, which must run without MAUI
/// DI so notifications still work after a reboot the app hasn't been launched since — it passes
/// <c>Preferences.Default</c> directly.
/// </para>
/// </summary>
public static class AiringNotificationState
{
    /// <summary>Comma-separated RELEASING media IDs, cached by <c>MyAnimePageModel</c> after a list load.</summary>
    public const string MediaIdsKey = "airing_media_ids";

    /// <summary>Unix seconds of the last fully successful check. See <c>AiringCheckRunner</c> for why it only advances on success.</summary>
    public const string LastCheckKey = "airing_last_check";

    /// <summary>JSON dictionary of <c>"mediaId:episode"</c> → unix seconds, the dedup set.</summary>
    public const string NotifiedKey = "airing_notified";

    /// <summary>Set once the My Anime permission prompt has been shown, so a denial isn't re-prompted on every load.</summary>
    public const string PermissionPromptedKey = "airing_permission_prompted";

    /// <summary>
    /// How long a notified-episode entry is kept before pruning. Bounds the stored blob's growth;
    /// see #144 for the case where the check window can outrun this.
    /// </summary>
    public const int StaleEntryDays = 7;

    private const int SecondsPerDay = 86400;

    /// <summary>The dedup key for one aired episode. Also the input to <see cref="NotificationId"/>.</summary>
    public static string DedupKey(int mediaId, int episode) => $"{mediaId}:{episode}";

    /// <summary>
    /// A stable notification id for one episode.
    /// <para>
    /// Was <c>HashCode.Combine(mediaId, episode)</c>, which is seeded randomly per process — so the
    /// same episode got a different id after a restart and Android could not update the posted
    /// notification in place. FNV-1a over the dedup key is deterministic across processes and
    /// builds. Arithmetic like <c>mediaId * 1000 + episode</c> would not do: One Piece is past
    /// episode 1100.
    /// </para>
    /// <para>
    /// Masked to 31 bits so the result is always non-negative — Android notification ids are
    /// <c>int</c>, and a negative one is legal but needlessly confusing in a bug report.
    /// </para>
    /// </summary>
    public static int NotificationId(int mediaId, int episode)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        uint hash = offsetBasis;
        foreach (char c in DedupKey(mediaId, episode))
        {
            hash = (hash ^ c) * prime;
        }

        return (int)(hash & 0x7FFFFFFF);
    }

    // ── Media IDs ───────────────────────────────────────────────────

    /// <summary>Reads the cached RELEASING media IDs, skipping any entry that isn't an integer.</summary>
    public static List<int> ReadMediaIds(IPreferences preferences)
    {
        string raw = preferences.Get(MediaIdsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var ids = new List<int>();
        foreach (string part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public static void WriteMediaIds(IPreferences preferences, IEnumerable<int> mediaIds)
        => preferences.Set(MediaIdsKey, string.Join(",", mediaIds));

    // ── Checkpoint ──────────────────────────────────────────────────

    /// <summary>
    /// The start of the window to check. Defaults to 30 minutes ago when unset, so a first run
    /// notifies only for what has just aired rather than replaying history.
    /// </summary>
    public static long ReadCheckpoint(IPreferences preferences, long nowUnix)
        => preferences.Get(LastCheckKey, nowUnix - 1800);

    public static void AdvanceCheckpoint(IPreferences preferences, long nowUnix)
        => preferences.Set(LastCheckKey, nowUnix);

    /// <summary>
    /// Drops the checkpoint so the next run starts fresh. Used when notifications are switched off,
    /// so re-enabling notifies only for new episodes rather than everything aired while disabled.
    /// </summary>
    public static void ResetCheckpoint(IPreferences preferences)
        => preferences.Remove(LastCheckKey);

    // ── Notified set ────────────────────────────────────────────────

    /// <summary>
    /// Reads the dedup set. Corrupt stored JSON degrades to empty rather than throwing — this runs
    /// inside a background worker, where an exception is invisible and costs the whole run.
    /// </summary>
    public static Dictionary<string, long> ReadNotifiedSet(IPreferences preferences)
    {
        string raw = preferences.Get(NotifiedKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, long>>(raw) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // JsonException covers a corrupt or wrong-shaped blob. NotSupportedException is the
            // trimmed Release build's failure mode — this deserializer is reflection-based, and
            // Debug sets AndroidLinkMode=None, so that path never appears in the dev loop.
            //
            // Both degrade to empty rather than propagating, which costs at most a repeat
            // notification. Letting either escape would abort the whole run from inside a
            // background worker, where nothing surfaces it.
            return [];
        }
    }

    /// <summary>
    /// Drops entries older than <see cref="StaleEntryDays"/> and persists, writing only when
    /// something actually changed — <paramref name="hasNewEntries"/> for additions the caller made,
    /// or a non-empty prune here.
    /// </summary>
    public static void PruneAndSave(IPreferences preferences, Dictionary<string, long> notifiedSet, long nowUnix, bool hasNewEntries)
    {
        long cutoff = nowUnix - (StaleEntryDays * SecondsPerDay);
        var staleKeys = notifiedSet.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
        foreach (string key in staleKeys)
        {
            notifiedSet.Remove(key);
        }

        if (hasNewEntries || staleKeys.Count > 0)
        {
            preferences.Set(NotifiedKey, JsonSerializer.Serialize(notifiedSet));
        }
    }

    // ── Permission prompt ───────────────────────────────────────────

    public static bool HasPromptedForPermission(IPreferences preferences)
        => preferences.Get(PermissionPromptedKey, false);

    public static void MarkPromptedForPermission(IPreferences preferences)
        => preferences.Set(PermissionPromptedKey, true);

    // ── Sign-out ────────────────────────────────────────────────────

    /// <summary>
    /// Clears every key this subsystem owns. Called on sign-out so a different user never sees the
    /// previous user's episodes. Callers on the platform side also dismiss what is already posted.
    /// </summary>
    public static void ClearAll(IPreferences preferences)
    {
        preferences.Remove(MediaIdsKey);
        preferences.Remove(LastCheckKey);
        preferences.Remove(NotifiedKey);
        preferences.Remove(PermissionPromptedKey);
    }
}
