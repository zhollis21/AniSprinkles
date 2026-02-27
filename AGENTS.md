# Agent Instructions

## Session Startup

- Read `docs/TODO.md` and `docs/PLAN.md` before making changes. Those docs track the working task list and architectural decisions.
- Keep `docs/PLAN.md`, `docs/TODO.md`, `README.md`, and this file up to date when decisions are made.
- Keep changes aligned with the planning docs; ask before deviating.

## Code Style

- Follow `.editorconfig` — key rules: 4-space indent, CRLF, Allman braces (`csharp_new_line_before_open_brace = all`), file-scoped namespaces, braces always required.
- `var` only when type is apparent; use explicit types for built-in types and unclear types.
- Expression-bodied members for properties/accessors/lambdas; **not** for constructors/methods/local-functions.
- Primary constructors preferred.
- Private fields: `_camelCase`. Constants/static readonly: `PascalCase`. Interfaces: `I`-prefixed.
- Every async service method takes `CancellationToken cancellationToken = default`; use `ConfigureAwait(false)` in service-layer awaits.
- Nullable context is enabled project-wide (`<Nullable>enable</Nullable>`). Use nullable annotations (`string?`, `Media?`) appropriately.
- Comments explain **why** (intent, tradeoffs, guardrails), not what the code does. Use `///` XML doc on public APIs.
- Modifier order: `public, private, protected, internal, file, static, extern, new, virtual, abstract, sealed, override, readonly, unsafe, required, volatile, async`.

## Architecture

- **.NET MAUI Android-only** app (`net10.0-android`, min SDK 31), single project in `src/`. App ID: `com.RainbowSprinkles.AniSprinkles`.
- **MVVM** via CommunityToolkit.Mvvm 8.4: PageModels extend `ObservableObject`, use `[ObservableProperty]`, `[RelayCommand]`, and `[NotifyPropertyChangedFor]`.
- **Shell navigation** with flyout (`my-anime`, `settings`). Details route registered programmatically: `Routing.RegisterRoute("media-details", typeof(MediaDetailsPage))` in `AppShell.xaml.cs`. Navigation uses `Shell.Current.GoToAsync` with lightweight query params (`mediaId` + trace IDs), not full objects.
- **DI registration** (`MauiProgram.cs`):
  | Registration | Lifetime |
  |---|---|
  | `ErrorReportService`, `HttpClient`, `IAuthService`, `IAniListClient` | Singleton |
  | `MyAnimePageModel`, `SettingsPageModel` | **Singleton** (survive page recreation across flyout switches) |
  | `LoggingHandler` | Transient |
  | `MyAnimePage`, `SettingsPage`, `MediaDetailsPageModel`, `MediaDetailsPage` | Transient |
- **Page ↔ PageModel binding**: two-constructor pattern (parameterless + DI constructor). `ServiceProviderHelper` provides `IServiceProvider` fallback via `IPlatformApplication.Current.Services` when `Application.Current.Handler` is not ready during Shell startup.
- **OnAppearing three-branch pattern** on both flyout pages: (1) content alive → background refresh, (2) content gone + cached ViewModel data (`HasLoadedData`) → immediate rebuild + background refresh, (3) first load → spinner + deferred fetch. See `MyAnimePage.xaml.cs` and `SettingsPage.xaml.cs`.
- **Details page UX**: spinner-first flow — lightweight shell page appears immediately, full content view instantiated and shown after fetch/bind completes. Extended metadata sections are lazy-instantiated via `MediaDetailsExtendedSectionsView`. Details fetch is deferred until after `OnAppearing` + first-frame yield to avoid transition hitching. Navigation is non-animated to prevent partial-frame artifacts.
- **Services**:
  | Service | Purpose |
  |---|---|
  | `AuthService` | OAuth implicit grant, SecureStorage token persistence, expiry tracking |
  | `AniListClient` | AniList GraphQL client (queries + mutations), viewer ID caching |
  | `ErrorReportService` | Sentry capture + `ILogger` error recording + Bearer token redaction |
  | `FileLoggerProvider` | Rotating async file logger via `Channel<string>` (Debug builds only) |
  | `LoggingHandler` | HTTP `DelegatingHandler` — logs request/response + Sentry breadcrumbs |
- **Global usings** (`GlobalUsings.cs`): `Models`, `PageModels`, `Pages`, `Services`, `IconFont.Maui.FluentIcons`. The `Converters` and `Utilities` namespaces require explicit `using` statements.
- **Key NuGet packages**: `Microsoft.Maui.Controls` 10.0.41, `CommunityToolkit.Mvvm` 8.4.0, `CommunityToolkit.Maui` 14.0.0, `Sentry.Maui` 6.1.0, `Syncfusion.Maui.Toolkit` 1.0.9, `IconFont.Maui.FluentIcons` 1.1.0.

## Integration Points (AniList API)

- **Endpoint**: `https://graphql.anilist.co` (set as `HttpClient.BaseAddress` in `AniListClient` constructor).
- **Auth**: OAuth implicit grant (`response_type=token`). Authorize URL: `https://anilist.co/api/v2/oauth/authorize?client_id=35674&response_type=token`. Redirect URI: `anisprinkles://auth`. Token extracted from `WebAuthenticator` result, stored in `SecureStorage` keys `anilist_access_token` and `anilist_access_token_expires_at`.
- **Viewer context**: `GetMyAnimeListAsync` requires a userId. The client sends a lightweight `Viewer { id }` query, caches the result keyed by token string, then uses that ID for `MediaListCollection`. Cache naturally invalidates on re-auth.
- **GraphQL operations**:
  | Operation | Type | Auth | Purpose |
  |---|---|---|---|
  | `Viewer` | Query | Required | Lightweight userId fetch for caching |
  | `ViewerFull` | Query | Required | Full user profile (options, stats) |
  | `MediaListCollection` | Query | Required | Full anime list grouped by status |
  | `Search` (Page/media) | Query | Optional | Paginated anime search by title |
  | `Media` (by id) | Query | Optional | Full detail for single anime (includes `mediaListEntry` if authed) |
  | `SaveMediaListEntry` | Mutation | Required | Create or update a list entry |
  | `DeleteMediaListEntry` | Mutation | Required | Delete a list entry by ID |
  | `UpdateUser` | Mutation | Required | Update user settings (title language, score format, etc.) |
- **Rate limiting**: Planned but **not yet implemented**. Tentative strategy: respect `X-RateLimit-Remaining`/`Retry-After` headers, 30 req/min conservative start, exponential backoff on 429. Currently `SendAsync` throws `HttpRequestException` on any non-success status.
- **HttpClient**: singleton with `LoggingHandler` (DelegatingHandler wrapping `HttpClientHandler`). Bearer token attached per-request in `AniListClient.SendAsync`. No timeout, retry, or rate-limit middleware configured yet.

## Build and Test

```powershell
# Debug build
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android

# Release AAB (CI uses this with signing args — see .github/workflows/android-release.yml)
dotnet publish src/AniSprinkles.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=aab -o output
```

- **CI**: GitHub Actions `android-release.yml` triggers on Release publication (or manual `workflow_dispatch`). `promote-release.yml` promotes between Play Console tracks (internal → alpha → beta → production).
- **Version scheme**: `ApplicationDisplayVersion` from release tag semver (`vX.Y.Z` → `X.Y.Z`); `ApplicationVersion` (versionCode) from `YYMMDDNNN` (date + run_number mod 1000).
- **Debug config**: `AndroidLinkMode=None`, `EmbedAssembliesIntoApk=False`, `UseInterpreter=true`.
- **Release config**: `PublishTrimmed=true`, AOT (`AotAssemblies=True`), R8 linker, native debug symbols for crash reporting.
- **No test suite yet** — `tests/AniSprinkles.UITests/` is scaffolded but empty.

## Project Conventions

- **MAUI-first guidance**: do not mix WPF/Xamarin.Forms/Blazor/React patterns unless explicitly requested. Verify MAUI APIs against official Microsoft docs and current project code before recommending.
- **Logging**: `ILogger` with structured messages (`"HTTP {Method} {Uri}"`). Debug file logs guarded with `#if DEBUG`, written via `FileLoggerProvider` (rotating files at `files/logs/anisprinkles.log`). Category filters: `Microsoft`/`System`/`Sentry` → Warning; app code → Information. Bearer tokens are always redacted.
- **Telemetry**: Sentry crash reporting only (`SendDefaultPii = false`, `TracesSampleRate = 0.0`, `Debug = false`, `DiagnosticLevel = Warning`). Breadcrumbs for navigation, auth, HTTP, and list loads. Use `ErrorReportService.Record()` for handled exceptions. Global unhandled-exception handlers in `App.xaml.cs` catch `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`.
- **User prefs**: `AppSettings` (static, `Utilities/`) persists title language, score format, adult content toggle, and section order via `Preferences`. Loaded at app start, synced from AniList Viewer when authenticated.
- **Performance patterns**: singleton PageModels for flyout pages, 5-minute stale refresh window, lazy-instantiated extended detail sections, deferred API loads after page appears, section grouping off UI thread, non-animated details navigation, My Anime selection clear deferred until after navigation begins. Compiled XAML bindings enabled (`MauiEnableXamlCBindingWithSourceCompilation`).
- **Loading UX**: spinner-first for first loads; inline refresh for cached content. One spinner per screen. Details page hides heavy scroll content until media data is present.
- **Navigation guards**: rapid tap prevention on My Anime → details. Default Shell back behavior (no custom Android back overrides).

## Debugging Workflow

Always pull **both** logs before analyzing any issue.

```powershell
# Pull device app log
adb -s emulator-5554 exec-out run-as com.companyname.anisprinkles cat files/logs/anisprinkles.log > logs/anisprinkles.device.log

# Pull current-process logcat
$appPid = adb -s emulator-5554 shell pidof com.companyname.anisprinkles
adb -s emulator-5554 logcat -v time --pid $appPid -d > logs/adb.device.pid.log

# Scan for crashes
Select-String -Path logs/adb.device.pid.log -Pattern "FATAL EXCEPTION|AndroidRuntime|Unhandled exception|ObjectDisposedException|JavaProxyThrowable" -CaseSensitive:$false

# Scan for perf/jank
Select-String -Path logs/adb.device.pid.log -Pattern "Skipped [0-9]+ frames|Davey|GC freed" -CaseSensitive:$false

# Scan for navigation timing traces
Select-String -Path logs/anisprinkles.device.log -Pattern "NAVTRACE" -CaseSensitive:$false
```

Additional debug commands:

```powershell
# List all app log files
adb -s emulator-5554 shell run-as com.companyname.anisprinkles ls -la files/logs

# Read latest log directly on device
adb -s emulator-5554 shell run-as com.companyname.anisprinkles cat files/logs/anisprinkles.log

# Filter logcat by tag
adb logcat AniSprinkles:D *:S
```

- Use Visual Studio `View` → `Output` (Debug) and `View` → `Other Windows` → `Android Device Logcat` while debugging.
- Keep a terminal running `adb logcat` as backup in case the debugger detaches.
- For jank: classify as CPU/UI-thread bound vs network-bound before changing architecture. Correlate `Skipped`/`Davey!` with app operations.
- Validate performance on release-like builds, not only debugger-attached sessions.

## Security

- **OAuth implicit grant** via `WebAuthenticator`; tokens stored in `SecureStorage`, never logged.
- `ErrorReportService` redacts Bearer tokens from all error output before logging or Sentry capture.
- Sentry: `SendDefaultPii = false`. AniList Client ID (`35674`) is a public app identifier, not a secret.
- Sign-out clears both in-memory token fields and both SecureStorage keys.
- Token expiry is tracked and auto-cleared when expired (`IsExpired()` check on access).
