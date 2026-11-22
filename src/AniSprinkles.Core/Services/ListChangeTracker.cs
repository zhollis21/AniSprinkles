namespace AniSprinkles.Services;

/// <summary>
/// Counts the list-entry saves and removals that reached AniList, so a page holding a copy of the
/// list can tell its copy has gone stale (#225). The Library skips reloading for a few minutes after
/// a load; without this, a status changed on the details page, Discover, Search or Up Next left the
/// title in its old section until that window ran out, and an edit made from the stale card saved
/// the old status back.
/// <para>
/// <see cref="CachingAniListClient"/> bumps it, not the pages that save: every save goes through
/// that decorator, so a new save path cannot forget to. Readers compare <see cref="Version"/> with
/// the value they last loaded under rather than clearing a flag, so the Anime and Manga lists each
/// notice the same change.
/// </para>
/// </summary>
public sealed class ListChangeTracker
{
    private long _version;

    /// <summary>Goes up by one for every list change that reached AniList.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>Records one list change and returns the new <see cref="Version"/>.</summary>
    public long NotifyChanged() => Interlocked.Increment(ref _version);
}
