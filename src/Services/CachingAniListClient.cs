using System.Collections.Concurrent;

namespace AniSprinkles.Services;

/// <summary>
/// An <see cref="IAniListClient"/> decorator that caches the read-only character/staff lookups
/// (and their per-section page fetches) for the lifetime of the app session. AniList character
/// and staff records are effectively static reference data, so caching them:
/// <list type="bullet">
/// <item>eliminates the refetch storm when the user navigates Media → Character → Voice Actor → back;</item>
/// <item>makes toggling a list's sort back to a previously-seen value instant;</item>
/// <item>lets the character page's independent "Appears In" and "Voice Actors" cursors share the
/// same underlying popularity pages instead of double-fetching them.</item>
/// </list>
/// Only the detail-page reads are cached here. Everything else (lists, search, media, mutations)
/// passes straight through — caching those needs proper invalidation and is tracked as a separate
/// follow-up. Concurrent calls for the same key are coalesced (one in-flight fetch), and failures
/// are never cached.
/// </summary>
public sealed class CachingAniListClient : IAniListClient
{
    private readonly IAniListClient _inner;
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _cache = new();

    public CachingAniListClient(IAniListClient inner) => _inner = inner;

    // ---- Cached detail-page reads -------------------------------------------------------------

    public Task<Staff?> GetStaffAsync(
        int id,
        string charactersSort = "FAVOURITES_DESC",
        string mediaSort = "POPULARITY_DESC",
        int charactersPage = 1,
        int mediaPage = 1,
        CancellationToken cancellationToken = default)
        => GetOrAddAsync(
            $"Staff:{id}:{charactersSort}:{mediaSort}:{charactersPage}:{mediaPage}",
            () => _inner.GetStaffAsync(id, charactersSort, mediaSort, charactersPage, mediaPage, cancellationToken));

    public Task<Character?> GetCharacterAsync(
        int id,
        string mediaSort = "POPULARITY_DESC",
        int mediaPage = 1,
        CancellationToken cancellationToken = default)
        => GetOrAddAsync(
            $"Character:{id}:{mediaSort}:{mediaPage}",
            () => _inner.GetCharacterAsync(id, mediaSort, mediaPage, cancellationToken));

    public Task<(IReadOnlyList<StaffCharacterEdge> Items, PageInfo? PageInfo)> LoadStaffCharactersPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GetOrAddAsync(
            $"StaffCharactersPage:{id}:{page}:{sort}:{perPage}",
            () => _inner.LoadStaffCharactersPageAsync(id, page, sort, perPage, cancellationToken));

    public Task<(IReadOnlyList<StaffMediaEdge> Items, PageInfo? PageInfo)> LoadStaffMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GetOrAddAsync(
            $"StaffMediaPage:{id}:{page}:{sort}:{perPage}",
            () => _inner.LoadStaffMediaPageAsync(id, page, sort, perPage, cancellationToken));

    public Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> LoadCharacterMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GetOrAddAsync(
            $"CharacterMediaPage:{id}:{page}:{sort}:{perPage}",
            () => _inner.LoadCharacterMediaPageAsync(id, page, sort, perPage, cancellationToken));

    // ---- Pass-throughs (uncached) -------------------------------------------------------------

    public Task<IReadOnlyList<MediaListEntry>> GetMyAnimeListAsync(CancellationToken cancellationToken = default)
        => _inner.GetMyAnimeListAsync(cancellationToken);

    public Task<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>> GetMyAnimeListGroupedAsync(CancellationToken cancellationToken = default)
        => _inner.GetMyAnimeListGroupedAsync(cancellationToken);

    public Task<IReadOnlyList<Media>> SearchAnimeAsync(string search, int page = 1, int perPage = 20, CancellationToken cancellationToken = default)
        => _inner.SearchAnimeAsync(search, page, perPage, cancellationToken);

    public Task<(Media? Media, MediaListEntry? ListEntry)> GetMediaAsync(int id, CancellationToken cancellationToken = default)
        => _inner.GetMediaAsync(id, cancellationToken);

    public Task<MediaListEntry?> SaveMediaListEntryAsync(MediaListEntry entry, CancellationToken cancellationToken = default)
        => _inner.SaveMediaListEntryAsync(entry, cancellationToken);

    public Task<bool> DeleteMediaListEntryAsync(int entryId, CancellationToken cancellationToken = default)
        => _inner.DeleteMediaListEntryAsync(entryId, cancellationToken);

    public Task<int> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
        => _inner.GetCurrentUserIdAsync(cancellationToken);

    public Task<AniListUser> GetViewerAsync(CancellationToken cancellationToken = default)
        => _inner.GetViewerAsync(cancellationToken);

    public Task<AniListUser> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default)
        => _inner.UpdateUserAsync(request, cancellationToken);

    public Task<IReadOnlyList<AiringScheduleEntry>> GetAiringScheduleAsync(IReadOnlyList<int> mediaIds, int airingAfter, int airingBefore, CancellationToken cancellationToken = default)
        => _inner.GetAiringScheduleAsync(mediaIds, airingAfter, airingBefore, cancellationToken);

    // ---- Cache machinery ----------------------------------------------------------------------

    private async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory)
    {
        // Lazy ensures the factory runs at most once even under concurrent access (coalescing).
        var lazy = _cache.GetOrAdd(
            key,
            _ => new Lazy<Task<object?>>(async () => await factory().ConfigureAwait(false)));

        try
        {
            // result is the boxed return value (null is valid for reference-type T like Character?);
            // the ! only suppresses the nullable-cast warning, the cast itself is safe.
            var result = await lazy.Value.ConfigureAwait(false);
            return (T)result!;
        }
        catch
        {
            // Never cache a failed/cancelled fetch — evict this exact entry so the next call retries.
            _cache.TryRemove(new KeyValuePair<string, Lazy<Task<object?>>>(key, lazy));
            throw;
        }
    }
}
