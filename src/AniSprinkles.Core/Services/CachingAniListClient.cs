using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services;

/// <summary>
/// An <see cref="IAniListClient"/> decorator that caches the read-only character/staff/studio lookups
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
    /// <inheritdoc />
    public void InvalidateEntityCache() => _cache.Clear();

    private readonly IAniListClient _inner;
    private readonly ILogger<CachingAniListClient>? _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _cache = new();

    /// <remarks>
    /// The page size baked into the per-page seed keys. It MUST match the embedded
    /// <c>perPage: 25</c> in <c>StaffQuery</c>/<c>CharacterQuery</c> (the data we're seeding) and
    /// the <c>LoadXxxPageAsync</c> default <c>perPage</c> / page-model <c>PageSize</c> (the key a
    /// later sort-toggle looks up). The composite query's page size is the source of truth; if it
    /// changes, change this too or the seeded entry won't be found.
    /// </remarks>
    private const int SeededPerPage = 25;

    public CachingAniListClient(IAniListClient inner, ILogger<CachingAniListClient>? logger = null)
    {
        _inner = inner;
        _logger = logger;
    }

    // ---- Cached detail-page reads -------------------------------------------------------------

    public async Task<Staff?> GetStaffAsync(
        int id,
        string charactersSort = "FAVOURITES_DESC",
        string mediaSort = "POPULARITY_DESC",
        int charactersPage = 1,
        int mediaPage = 1,
        CancellationToken cancellationToken = default)
    {
        var staff = await GetOrAddAsync(
            $"Staff:{id}:{charactersSort}:{mediaSort}:{charactersPage}:{mediaPage}",
            () => _inner.GetStaffAsync(id, charactersSort, mediaSort, charactersPage, mediaPage, cancellationToken))
            .ConfigureAwait(false);

        if (staff is not null)
        {
            // The composite query already returns each section's requested page embedded in the
            // payload, so pre-seed the per-page caches with it. Without this, toggling a list's sort
            // away and back to the seeded sort would re-fetch page 1 over the network even though we
            // already hold that exact data. PerPage is the SeededPerPage const — see its remarks.
            SeedPageCache(
                $"StaffCharactersPage:{id}:{charactersPage}:{charactersSort}:{SeededPerPage}",
                () => ((IReadOnlyList<StaffCharacterEdge>)staff.Characters.ToList(), staff.CharactersPageInfo));
            SeedPageCache(
                $"StaffMediaPage:{id}:{mediaPage}:{mediaSort}:{SeededPerPage}",
                () => ((IReadOnlyList<StaffMediaEdge>)staff.StaffMedia.ToList(), staff.StaffMediaPageInfo));
        }

        return staff;
    }

    public async Task<Character?> GetCharacterAsync(
        int id,
        string mediaSort = "POPULARITY_DESC",
        int mediaPage = 1,
        CancellationToken cancellationToken = default)
    {
        var character = await GetOrAddAsync(
            $"Character:{id}:{mediaSort}:{mediaPage}",
            () => _inner.GetCharacterAsync(id, mediaSort, mediaPage, cancellationToken))
            .ConfigureAwait(false);

        if (character is not null)
        {
            // See GetStaffAsync: seed the embedded first page so a sort toggle back to it is a hit.
            SeedPageCache(
                $"CharacterMediaPage:{id}:{mediaPage}:{mediaSort}:{SeededPerPage}",
                () => ((IReadOnlyList<CharacterMediaEdge>)character.Media.ToList(), character.MediaPageInfo));
        }

        return character;
    }

    public async Task<Studio?> GetStudioAsync(
        int id,
        string mediaSort = "POPULARITY_DESC",
        int mediaPage = 1,
        int mediaPerPage = 25,
        CancellationToken cancellationToken = default)
    {
        var studio = await GetOrAddAsync(
            $"Studio:{id}:{mediaSort}:{mediaPage}:{mediaPerPage}",
            () => _inner.GetStudioAsync(id, mediaSort, mediaPage, mediaPerPage, cancellationToken))
            .ConfigureAwait(false);

        if (studio is not null)
        {
            // Key off the perPage GetStudioAsync was asked for (sourced from the page model's PageSize),
            // so the seed matches the LoadStudioMediaPageAsync lookup for page 1 even if PageSize changes.
            SeedPageCache(
                $"StudioMediaPage:{id}:{mediaPage}:{mediaSort}:{mediaPerPage}",
                () => ((IReadOnlyList<StudioMediaEdge>)studio.Media.ToList(), studio.MediaPageInfo));
        }

        return studio;
    }

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

    public Task<(IReadOnlyList<StudioMediaEdge> Items, PageInfo? PageInfo)> LoadStudioMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GetOrAddAsync(
            $"StudioMediaPage:{id}:{page}:{sort}:{perPage}",
            () => _inner.LoadStudioMediaPageAsync(id, page, sort, perPage, cancellationToken));

    public Task<(IReadOnlyList<CharacterEdge> Items, PageInfo? PageInfo)> LoadMediaCharactersPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GetOrAddAsync(
            $"MediaCharactersPage:{id}:{page}:{sort}:{perPage}",
            () => _inner.LoadMediaCharactersPageAsync(id, page, sort, perPage, cancellationToken));

    public Task<(IReadOnlyList<StaffEdge> Items, PageInfo? PageInfo)> LoadMediaStaffPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GetOrAddAsync(
            $"MediaStaffPage:{id}:{page}:{sort}:{perPage}",
            () => _inner.LoadMediaStaffPageAsync(id, page, sort, perPage, cancellationToken));

    public Task<(IReadOnlyList<MediaRecommendationNode> Items, PageInfo? PageInfo)> LoadMediaRecommendationsPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GetOrAddAsync(
            $"MediaRecommendationsPage:{id}:{page}:{sort}:{perPage}",
            () => _inner.LoadMediaRecommendationsPageAsync(id, page, sort, perPage, cancellationToken));

    // ---- Pass-throughs (uncached) -------------------------------------------------------------

    public Task<IReadOnlyList<MediaListEntry>> GetMyAnimeListAsync(CancellationToken cancellationToken = default)
        => _inner.GetMyAnimeListAsync(cancellationToken);

    public Task<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>> GetMyAnimeListGroupedAsync(CancellationToken cancellationToken = default)
        => _inner.GetMyAnimeListGroupedAsync(cancellationToken);

    public Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> SearchMediaPageAsync(
        string search, MediaKind? kind, bool? isAdult = false, int page = 1, int perPage = 20, CancellationToken cancellationToken = default)
        => _inner.SearchMediaPageAsync(search, kind, isAdult, page, perPage, cancellationToken);

    // Discover/browse stay uncached here: they carry the viewer's mediaListEntry state (mutations
    // would stale it) and Discover already has its own TTL cache in DiscoverPageModel.
    public Task<DiscoverSections> GetDiscoverSectionsAsync(
        string currentSeason, int currentSeasonYear, string nextSeason, int nextSeasonYear,
        bool filterAdult, bool includeAdultSections, int perPage = 20, CancellationToken cancellationToken = default)
        => _inner.GetDiscoverSectionsAsync(currentSeason, currentSeasonYear, nextSeason, nextSeasonYear, filterAdult, includeAdultSections, perPage, cancellationToken);

    public Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> BrowseAnimePageAsync(
        string sort, string? status = null, string? season = null, int? seasonYear = null, bool? isAdult = null,
        string? format = null, int page = 1, int perPage = 25, CancellationToken cancellationToken = default)
        => _inner.BrowseAnimePageAsync(sort, status, season, seasonYear, isAdult, format, page, perPage, cancellationToken);

    public async Task<(Media? Media, MediaListEntry? ListEntry)> GetMediaAsync(int id, CancellationToken cancellationToken = default)
    {
        // Media itself isn't cached — a list-entry mutation would stale it. But the heavy MediaQuery
        // embeds the first page (perPage 25) of each sortable section, so seed those per-section page
        // caches: a sort toggle back to the default, or a Load More the section already holds, becomes a
        // hit. The sort codes MUST match MediaDetailsPageModel's section defaults and SeededPerPage MUST
        // match the MediaQuery perPage (see its remarks).
        var result = await _inner.GetMediaAsync(id, cancellationToken).ConfigureAwait(false);

        if (result.Media is { } media)
        {
            SeedPageCache(
                $"MediaCharactersPage:{id}:1:ROLE:{SeededPerPage}",
                () => ((IReadOnlyList<CharacterEdge>)media.Characters.ToList(), media.CharactersPageInfo));
            SeedPageCache(
                $"MediaStaffPage:{id}:1:RELEVANCE:{SeededPerPage}",
                () => ((IReadOnlyList<StaffEdge>)media.Staff.ToList(), media.StaffPageInfo));
            SeedPageCache(
                $"MediaRecommendationsPage:{id}:1:RATING_DESC:{SeededPerPage}",
                () => ((IReadOnlyList<MediaRecommendationNode>)media.Recommendations.ToList(), media.RecommendationsPageInfo));
        }

        return result;
    }

    public Task<MediaListEntry?> SaveMediaListEntryAsync(MediaListEntry entry, CancellationToken cancellationToken = default)
        => _inner.SaveMediaListEntryAsync(entry, cancellationToken);

    public Task<bool> DeleteMediaListEntryAsync(int entryId, CancellationToken cancellationToken = default)
        => _inner.DeleteMediaListEntryAsync(entryId, cancellationToken);

    public Task<bool> ToggleFavouriteAsync(FavouriteKind kind, int id, CancellationToken cancellationToken = default)
        => _inner.ToggleFavouriteAsync(kind, id, cancellationToken);

    public Task<int> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
        => _inner.GetCurrentUserIdAsync(cancellationToken);

    public Task<AniListUser> GetViewerAsync(CancellationToken cancellationToken = default)
        => _inner.GetViewerAsync(cancellationToken);

    public Task<AniListUser> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default)
        => _inner.UpdateUserAsync(request, cancellationToken);

    public Task<IReadOnlyList<AiringScheduleEntry>> GetAiringScheduleAsync(IReadOnlyList<int> mediaIds, int airingAfter, int airingBefore, CancellationToken cancellationToken = default)
        => _inner.GetAiringScheduleAsync(mediaIds, airingAfter, airingBefore, cancellationToken);

    // ---- Cache machinery ----------------------------------------------------------------------

    /// <summary>
    /// Pre-populates a cache entry with an already-resolved value (no fetch). The value is built via a
    /// factory so we skip snapshotting the model's collection when the key is already present — the
    /// common case on a composite cache hit (back-navigation), where seeding would otherwise allocate
    /// a throwaway list only for <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> to discard it.
    /// <see cref="ConcurrentDictionary{TKey,TValue}.TryAdd"/> stays as the correctness backstop for the
    /// ContainsKey→TryAdd race: if the user already fetched this page directly, keep that entry
    /// untouched. A later read of a seeded entry through <see cref="GetOrAddAsync"/> logs a CACHE hit.
    /// </summary>
    private void SeedPageCache<T>(string key, Func<T> valueFactory)
    {
        if (_cache.ContainsKey(key))
        {
            return;
        }

        var value = valueFactory();
        _cache.TryAdd(key, new Lazy<Task<object?>>(() => Task.FromResult<object?>(value)));
    }

    private async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory)
    {
        // Log the miss *inside* the Lazy factory: it runs exactly once (ExecutionAndPublication), so
        // "miss" tracks an actual network fetch. Then compare GetOrAdd's result to our candidate by
        // reference: if it isn't ours, the entry already existed (or we lost the create race) and this
        // read is served without a fetch — a hit. This logs hit/miss correctly for every caller,
        // including concurrent ones, and never over-reports misses.
        var candidate = new Lazy<Task<object?>>(async () =>
        {
            _logger?.LogInformation("CACHE miss {Key}", key);
            return await factory().ConfigureAwait(false);
        });

        var lazy = _cache.GetOrAdd(key, candidate);
        if (!ReferenceEquals(lazy, candidate))
        {
            _logger?.LogInformation("CACHE hit {Key}", key);
        }

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
