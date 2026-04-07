#if ERROR_SIM
namespace AniSprinkles.Services;

/// <summary>
/// Error-simulation stub that throws a classified AniListApiException on every
/// data call so all three pages (My Anime, Media Details, Settings) hit their
/// error state. Change <see cref="SimulatedError"/> to cycle through error kinds.
/// Compiled out of all builds except when -p:ErrorSim=true is passed.
/// </summary>
internal sealed class FailingAniListClient : IAniListClient
{
    // ── Change this to test different error views ────────────────────
    //   ApiErrorKind.ServiceOutage  → "AniList is Temporarily Down"  (CloudDismiss24)
    //   ApiErrorKind.Network        → "No Internet Connection"        (WifiOff24)
    //   ApiErrorKind.Authentication → "Session Expired"               (LockClosed24)
    //   ApiErrorKind.Unknown        → "Something Went Wrong"          (ErrorCircle24)
    private const ApiErrorKind SimulatedError = ApiErrorKind.ServiceOutage;

    private static AniListApiException Fail() => SimulatedError switch
    {
        ApiErrorKind.ServiceOutage => new(ApiErrorKind.ServiceOutage,
            "AniList API has been temporarily disabled due to stability issues."),
        ApiErrorKind.Network => new(ApiErrorKind.Network,
            "Network error during request.",
            new System.Net.Http.HttpRequestException("No route to host")),
        ApiErrorKind.Authentication => new(ApiErrorKind.Authentication,
            "Invalid token"),
        _ => new(ApiErrorKind.Unknown, "Something unexpected happened."),
    };

    public Task<IReadOnlyList<(string Name, IReadOnlyList<MediaListEntry> Entries)>>
        GetMyAnimeListGroupedAsync(CancellationToken ct = default) => throw Fail();

    public Task<IReadOnlyList<MediaListEntry>>
        GetMyAnimeListAsync(CancellationToken ct = default) => throw Fail();

    public Task<(Media? Media, MediaListEntry? ListEntry)>
        GetMediaAsync(int id, CancellationToken ct = default) => throw Fail();

    public Task<IReadOnlyList<Media>>
        SearchAnimeAsync(string search, int page = 1, int perPage = 20, CancellationToken ct = default)
            => throw Fail();

    public Task<MediaListEntry?>
        SaveMediaListEntryAsync(MediaListEntry entry, CancellationToken ct = default) => throw Fail();

    public Task<bool>
        DeleteMediaListEntryAsync(int entryId, CancellationToken ct = default) => throw Fail();

    public Task<int>
        GetCurrentUserIdAsync(CancellationToken ct = default) => throw Fail();

    public Task<AniListUser>
        GetViewerAsync(CancellationToken ct = default) => throw Fail();

    public Task<AniListUser>
        UpdateUserAsync(UpdateUserRequest request, CancellationToken ct = default) => throw Fail();
}
#endif
