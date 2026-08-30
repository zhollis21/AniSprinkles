#if DEBUG
using Microsoft.Extensions.Logging;

namespace AniSprinkles.Services.FaultInjection;

/// <summary>
/// Decorates whichever <see cref="IAniListClient"/> is registered — the CI stub or the real caching
/// client — and decides per call whether to delay it, fail it, or pass it straight through (#125).
/// <para>
/// A decorator rather than a replacement, which is the whole point. <c>FailingAniListClient</c>
/// answered all 23 methods with <c>throw</c>, so every call failed and you could never <em>reach</em>
/// the screen whose error state you wanted to see — the details-page error and retry states were
/// unverifiable on device for exactly that reason. Wrapping instead means a real screen loads from
/// the fixtures and then the next call breaks.
/// </para>
/// <para>
/// Disarmed by default: with nothing armed, <see cref="FaultState.Decide"/> returns pass-through and
/// this costs one uncontended lock acquisition per call and nothing else. That is what let
/// <c>-p:ErrorSim=true</c> and its Release guard be retired — a fault build is no longer a build
/// that is useless for anything else.
/// </para>
/// <para>
/// Sits above the HTTP pipeline, so <c>AniListRateLimitHandler</c>, <c>LoggingHandler</c>,
/// <c>AniListClient.SendAsync</c> retry-once and <c>AniListErrorClassifier</c> do <em>not</em> run
/// for an injected fault. That is the trade this seam makes in exchange for composing with the
/// fixtures; <c>FaultInjectingHttpHandler</c> is the seam that covers the other side.
/// </para>
/// </summary>
public sealed class FaultInjectingAniListClient(
    IAniListClient inner,
    FaultState state,
    IOutageStateService outageState,
    ILogger<FaultInjectingAniListClient> logger) : IAniListClient
{
    /// <summary>
    /// Whether <em>this</em> decorator has pushed the outage banner on. Only then does a subsequent
    /// success clear it — see <see cref="ClearInjectedOutage"/>.
    /// </summary>
    private volatile bool _reportedOutage;

    /// <summary>
    /// The one place a fault is applied. Every interface member below is a one-line delegation
    /// through here, so the 23 overrides stay readable rather than becoming 23 copies of this logic.
    /// </summary>
    private async Task<T> GateAsync<T>(
        string operation,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        var decision = state.Decide(operation, FaultLayer.Client);
        if (decision.IsPassThrough)
        {
            var passThrough = await call(cancellationToken).ConfigureAwait(false);
            ClearInjectedOutage();
            return passThrough;
        }

        if (decision.Delay > TimeSpan.Zero)
        {
            logger.LogInformation(
                "FAULT delaying {Operation} by {DelayMs}ms", operation, decision.Delay.TotalMilliseconds);

            // Deliberately honours the token: a delayed call the user navigates away from must
            // cancel, because "does navigating away actually stop this?" is the question the delay
            // was armed to answer (#132).
            await Task.Delay(decision.Delay, cancellationToken).ConfigureAwait(false);
        }

        if (decision.Kind is not { } kind)
        {
            var delayed = await call(cancellationToken).ConfigureAwait(false);
            ClearInjectedOutage();
            return delayed;
        }

        throw BuildFailure(operation, kind);
    }

    private AniListApiException BuildFailure(string operation, ApiErrorKind kind)
    {
        var ex = Describe(kind);
        logger.LogWarning("FAULT failing {Operation} as {Kind}", operation, kind);

        // Mirror what AniListClient does on a real failure. Injecting above it bypasses its own
        // ReportFailure, so without this the global outage banner and the differentiated snackbars —
        // the things a ServiceOutage fault is usually armed to look at — would never light up.
        if (kind == ApiErrorKind.ServiceOutage)
        {
            _reportedOutage = true;
        }

        outageState.ReportFailure(ex);
        return ex;
    }

    /// <summary>
    /// Clears an outage banner <em>this decorator</em> raised, once a call succeeds again.
    /// <para>
    /// The banner is sticky by design and normally clears via <c>AniListClient.SendAsync</c>'s
    /// <c>ReportSuccess</c> on the next real round-trip. That never happens behind the CI fixtures —
    /// there is no real client — so without this an injected <c>ServiceOutage</c> would pin the
    /// banner for the rest of the session, and recovery, the scenario this whole seam exists to make
    /// testable, would look broken. Observed on device: Retry restored the page to Content with the
    /// banner still up.
    /// </para>
    /// <para>
    /// Gated on having raised it, rather than reporting success unconditionally. This decorator sits
    /// <em>above</em> <c>CachingAniListClient</c>, so it cannot tell a cache hit from a round-trip;
    /// reporting success on every pass-through would let a cache hit clear a genuine outage banner
    /// in an ordinary Debug session, which is a real behaviour change for a debugging convenience.
    /// </para>
    /// </summary>
    private void ClearInjectedOutage()
    {
        if (!_reportedOutage)
        {
            return;
        }

        _reportedOutage = false;
        outageState.ReportSuccess();
    }

    private static AniListApiException Describe(ApiErrorKind kind) => kind switch
    {
        ApiErrorKind.ServiceOutage => new(
            ApiErrorKind.ServiceOutage, "AniList API has been temporarily disabled due to stability issues."),
        ApiErrorKind.Network => new(
            ApiErrorKind.Network, "Network error during request.",
            new HttpRequestException("No route to host")),
        ApiErrorKind.Authentication => new(ApiErrorKind.Authentication, "Invalid token"),
        ApiErrorKind.RateLimited => new(ApiErrorKind.RateLimited, "Too Many Requests"),
        ApiErrorKind.NotFound => new(ApiErrorKind.NotFound, "Not Found."),
        _ => new(ApiErrorKind.Unknown, "Something unexpected happened."),
    };

    // ── IAniListClient ────────────────────────────────────────────────────────────────────────────
    // Operation names are the method names, and FaultProfile matches them by prefix, so
    // `--es op GetStudio` arms GetStudioAsync without anyone having to spell out the suffix.

    /// <inheritdoc />
    /// <remarks>Straight delegation — cache invalidation is not a call worth faulting.</remarks>
    public void InvalidateEntityCache() => inner.InvalidateEntityCache();

    public Task<bool> ToggleFavouriteAsync(FavouriteKind kind, int id, CancellationToken cancellationToken = default)
        => GateAsync(nameof(ToggleFavouriteAsync), ct => inner.ToggleFavouriteAsync(kind, id, ct), cancellationToken);

    public Task<IReadOnlyList<MediaListEntry>> GetMyAnimeListAsync(CancellationToken cancellationToken = default)
        => GateAsync(nameof(GetMyAnimeListAsync), inner.GetMyAnimeListAsync, cancellationToken);

    public Task<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>> GetMyAnimeListGroupedAsync(
        CancellationToken cancellationToken = default)
        => GateAsync(nameof(GetMyAnimeListGroupedAsync), inner.GetMyAnimeListGroupedAsync, cancellationToken);

    public Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> SearchMediaPageAsync(
        string search, MediaKind? kind, bool? isAdult = false, int page = 1, int perPage = 20, CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(SearchMediaPageAsync),
            ct => inner.SearchMediaPageAsync(search, kind, isAdult, page, perPage, ct),
            cancellationToken);

    public Task<DiscoverSections> GetDiscoverSectionsAsync(
        string currentSeason,
        int currentSeasonYear,
        string nextSeason,
        int nextSeasonYear,
        bool filterAdult,
        bool includeAdultSections,
        int perPage = 20,
        CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(GetDiscoverSectionsAsync),
            ct => inner.GetDiscoverSectionsAsync(
                currentSeason, currentSeasonYear, nextSeason, nextSeasonYear,
                filterAdult, includeAdultSections, perPage, ct),
            cancellationToken);

    public Task<(IReadOnlyList<BrowseMediaItem> Items, PageInfo? PageInfo)> BrowseAnimePageAsync(
        string sort,
        string? status = null,
        string? season = null,
        int? seasonYear = null,
        bool? isAdult = null,
        string? format = null,
        int page = 1,
        int perPage = 25,
        CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(BrowseAnimePageAsync),
            ct => inner.BrowseAnimePageAsync(sort, status, season, seasonYear, isAdult, format, page, perPage, ct),
            cancellationToken);

    public Task<(Media? Media, MediaListEntry? ListEntry)> GetMediaAsync(int id, CancellationToken cancellationToken = default)
        => GateAsync(nameof(GetMediaAsync), ct => inner.GetMediaAsync(id, ct), cancellationToken);

    public Task<MediaListEntry?> SaveMediaListEntryAsync(MediaListEntry entry, CancellationToken cancellationToken = default)
        => GateAsync(nameof(SaveMediaListEntryAsync), ct => inner.SaveMediaListEntryAsync(entry, ct), cancellationToken);

    public Task<bool> DeleteMediaListEntryAsync(int entryId, CancellationToken cancellationToken = default)
        => GateAsync(nameof(DeleteMediaListEntryAsync), ct => inner.DeleteMediaListEntryAsync(entryId, ct), cancellationToken);

    public Task<int> GetCurrentUserIdAsync(CancellationToken cancellationToken = default)
        => GateAsync(nameof(GetCurrentUserIdAsync), inner.GetCurrentUserIdAsync, cancellationToken);

    public Task<AniListUser> GetViewerAsync(CancellationToken cancellationToken = default)
        => GateAsync(nameof(GetViewerAsync), inner.GetViewerAsync, cancellationToken);

    public Task<AniListUser> UpdateUserAsync(UpdateUserRequest request, CancellationToken cancellationToken = default)
        => GateAsync(nameof(UpdateUserAsync), ct => inner.UpdateUserAsync(request, ct), cancellationToken);

    public Task<IReadOnlyList<AiringScheduleEntry>> GetAiringScheduleAsync(
        IReadOnlyList<int> mediaIds, int airingAfter, int airingBefore, CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(GetAiringScheduleAsync),
            ct => inner.GetAiringScheduleAsync(mediaIds, airingAfter, airingBefore, ct),
            cancellationToken);

    public Task<Staff?> GetStaffAsync(
        int id,
        string charactersSort = "FAVOURITES_DESC",
        string mediaSort = "POPULARITY_DESC",
        int charactersPage = 1,
        int mediaPage = 1,
        CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(GetStaffAsync),
            ct => inner.GetStaffAsync(id, charactersSort, mediaSort, charactersPage, mediaPage, ct),
            cancellationToken);

    public Task<Character?> GetCharacterAsync(
        int id,
        string mediaSort = "POPULARITY_DESC",
        int mediaPage = 1,
        CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(GetCharacterAsync),
            ct => inner.GetCharacterAsync(id, mediaSort, mediaPage, ct),
            cancellationToken);

    public Task<Studio?> GetStudioAsync(
        int id,
        string mediaSort = "POPULARITY_DESC",
        int mediaPage = 1,
        int mediaPerPage = 25,
        CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(GetStudioAsync),
            ct => inner.GetStudioAsync(id, mediaSort, mediaPage, mediaPerPage, ct),
            cancellationToken);

    public Task<(IReadOnlyList<StaffCharacterEdge> Items, PageInfo? PageInfo)> LoadStaffCharactersPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(LoadStaffCharactersPageAsync),
            ct => inner.LoadStaffCharactersPageAsync(id, page, sort, perPage, ct),
            cancellationToken);

    public Task<(IReadOnlyList<StaffMediaEdge> Items, PageInfo? PageInfo)> LoadStaffMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(LoadStaffMediaPageAsync),
            ct => inner.LoadStaffMediaPageAsync(id, page, sort, perPage, ct),
            cancellationToken);

    public Task<(IReadOnlyList<CharacterMediaEdge> Items, PageInfo? PageInfo)> LoadCharacterMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(LoadCharacterMediaPageAsync),
            ct => inner.LoadCharacterMediaPageAsync(id, page, sort, perPage, ct),
            cancellationToken);

    public Task<(IReadOnlyList<StudioMediaEdge> Items, PageInfo? PageInfo)> LoadStudioMediaPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(LoadStudioMediaPageAsync),
            ct => inner.LoadStudioMediaPageAsync(id, page, sort, perPage, ct),
            cancellationToken);

    public Task<(IReadOnlyList<CharacterEdge> Items, PageInfo? PageInfo)> LoadMediaCharactersPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(LoadMediaCharactersPageAsync),
            ct => inner.LoadMediaCharactersPageAsync(id, page, sort, perPage, ct),
            cancellationToken);

    public Task<(IReadOnlyList<StaffEdge> Items, PageInfo? PageInfo)> LoadMediaStaffPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(LoadMediaStaffPageAsync),
            ct => inner.LoadMediaStaffPageAsync(id, page, sort, perPage, ct),
            cancellationToken);

    public Task<(IReadOnlyList<MediaRecommendationNode> Items, PageInfo? PageInfo)> LoadMediaRecommendationsPageAsync(
        int id, int page, string sort, int perPage = 25, CancellationToken cancellationToken = default)
        => GateAsync(
            nameof(LoadMediaRecommendationsPageAsync),
            ct => inner.LoadMediaRecommendationsPageAsync(id, page, sort, perPage, ct),
            cancellationToken);
}
#endif
