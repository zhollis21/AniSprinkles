# Developer Notes

## Simulating AniList Being Down

The app has classified error handling (`AniListApiException` with `ApiErrorKind`) that shows
different error views for outages, network failures, and auth errors. Here's how to trigger
each scenario locally.

### Option 1: Quick DI swap (recommended)

The app uses `IAniListClient` via DI (`MauiProgram.cs`). Create a throwing stub and register it
temporarily:

```csharp
// In MauiProgram.cs, replace the real client registration temporarily:
// builder.Services.AddSingleton<IAniListClient, AniListClient>();
builder.Services.AddSingleton<IAniListClient, FailingAniListClient>();
```

Example stub (drop this anywhere under `src/Services/` during testing):

```csharp
// DO NOT CHECK IN — local testing only
internal sealed class FailingAniListClient : IAniListClient
{
    // Change this to test different error views:
    //   ApiErrorKind.ServiceOutage  → "AniList is Temporarily Down" (cloud icon)
    //   ApiErrorKind.Network        → "No Internet Connection" (wifi-off icon)
    //   ApiErrorKind.Authentication → "Session Expired" (lock icon)
    //   ApiErrorKind.Unknown        → "Something Went Wrong" (error icon)
    private static readonly ApiErrorKind SimulatedError = ApiErrorKind.ServiceOutage;

    private static AniListApiException Fail() => SimulatedError switch
    {
        ApiErrorKind.ServiceOutage => new(ApiErrorKind.ServiceOutage,
            "AniList API has been temporarily disabled due to stability issues."),
        ApiErrorKind.Network => new(ApiErrorKind.Network,
            "Network error during GetMyAnimeListGrouped.",
            new HttpRequestException("No route to host")),
        ApiErrorKind.Authentication => new(ApiErrorKind.Authentication,
            "Invalid token"),
        _ => new(ApiErrorKind.Unknown, "Something unexpected happened."),
    };

    public Task<IReadOnlyList<(string, IReadOnlyList<MediaListEntry>)>>
        GetMyAnimeListGroupedAsync(CancellationToken ct = default) => throw Fail();
    public Task<IReadOnlyList<MediaListEntry>>
        GetMyAnimeListAsync(CancellationToken ct = default) => throw Fail();
    public Task<(Media?, MediaListEntry?)>
        GetMediaAsync(int id, CancellationToken ct = default) => throw Fail();
    public Task<IReadOnlyList<Media>>
        SearchAnimeAsync(string s, int p = 1, int pp = 20, CancellationToken ct = default)
            => throw Fail();
    public Task<MediaListEntry?>
        SaveMediaListEntryAsync(MediaListEntry e, CancellationToken ct = default) => throw Fail();
    public Task<bool>
        DeleteMediaListEntryAsync(int id, CancellationToken ct = default) => throw Fail();
    public Task<int>
        GetCurrentUserIdAsync(CancellationToken ct = default) => throw Fail();
    public Task<AniListUser>
        GetViewerAsync(CancellationToken ct = default) => throw Fail();
    public Task<AniListUser>
        UpdateUserAsync(UpdateUserRequest req, CancellationToken ct = default) => throw Fail();
}
```

Change `SimulatedError` to cycle through each error kind and verify all three pages
(My Anime, Media Details, Settings) show the correct icon, title, subtitle, and retry button.

### Option 2: Block the endpoint at the network level

Turn on airplane mode on your device/emulator, or block the API host:

- **Android emulator**: `adb shell settings put global airplane_mode_on 1`
- **Windows/Mac**: Add `127.0.0.1 graphql.anilist.co` to your hosts file
- **Charles/Fiddler proxy**: Map `graphql.anilist.co` to return HTTP 503

This triggers `ApiErrorKind.Network` (connection failure) or `ApiErrorKind.ServiceOutage`
(if the proxy returns 503).

### Option 3: Inject a delay + failure in SendAsync

For a more surgical test, add a temporary `#if DEBUG` block at the top of
`AniListClient.SendAsync`:

```csharp
#if DEBUG
// Simulate outage — remove before committing
await Task.Delay(800, cancellationToken); // realistic latency
throw new AniListApiException(ApiErrorKind.ServiceOutage,
    "AniList API has been temporarily disabled due to stability issues.");
#endif
```

### What to verify

When simulating errors, check each page:

| Page | Expected behavior |
|------|------------------|
| **My Anime** | Full-page `ErrorStateView` with retry button. Stale cached data shows instead if a previous load succeeded. |
| **Media Details** | Full-page `ErrorStateView` with retry button. Spinner should NOT be visible alongside the error. |
| **Settings** | Full-page `ErrorStateView` with retry button. Login prompt should NOT overlap with error view. |

For each page, also verify:

- Tapping **Try Again** clears the error and re-attempts the load
- **Show technical details** expands to show the API message (not a stack trace)
- **Copy** and **Share** buttons in the details section work
- The correct icon appears per error kind:

| `ApiErrorKind` | Icon | Title |
|----------------|------|-------|
| `ServiceOutage` | `CloudDismiss24` | "AniList is Temporarily Down" |
| `Network` | `WifiOff24` | "No Internet Connection" |
| `Authentication` | `LockClosed24` | "Session Expired" |
| `Unknown` | `ErrorCircle24` | "Something Went Wrong" |
