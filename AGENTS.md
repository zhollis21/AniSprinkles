# Agent Instructions

## Session Startup

- Read `README.md` and any task-relevant docs before making changes.
- GitHub Issues track backlog work and future plans; do not expect `docs/TODO.md` or `docs/PLAN.md` to exist.
- Keep `README.md` and this file up to date when repository-wide decisions are made.

## Architecture

- **.NET MAUI Android-only** app (`net10.0-android`; `SupportedOSPlatformVersion` 31.0), single project in `src/`. App ID: `com.RainbowSprinkles.AniSprinkles`.
- **MVVM** via CommunityToolkit.Mvvm: PageModels extend `ObservableObject`, use `[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`. Shell bottom-tab navigation (`my-anime` with `anime`/`manga` sub-tabs, `discover`, `search`, `feed`, `settings`) with programmatic `media-details`, `staff-details`, `character-details`, `studio-details`, and `media-browse` routes. PageModels navigate through the injected `INavigationService` (routes + lightweight query params — never full objects); persist state via the injected `IPreferences`; marshal pool-thread continuations back to the UI via the injected `IDispatcher`. View-facing UX calls (`Shell.Current.DisplayAlertAsync`, `ShowPopupAsync`, `Browser.Default`) are allowed to keep using MAUI statics because they only run inside view-attached paths.
- **DI**: Services and tab PageModels (`MyAnimePageModel`, `DiscoverPageModel`, `SettingsPageModel`) are singleton. All Pages and details PageModels (`MediaDetailsPageModel`, `StaffDetailsPageModel`, `CharacterDetailsPageModel`, `StudioDetailsPageModel`, `MediaBrowsePageModel`) are transient. See `/project-architecture` for the full DI table and page patterns.
- **Services**: `AuthService` (OAuth + SecureStorage), `AniListClient` (GraphQL + viewer ID cache), `ErrorReportService` (Sentry + `ILogger` + token redaction), `FileLoggerProvider` (rotating async file log, Debug = 1 MB × 3 / Release = 256 KB × 3), `AndroidLogcatLoggerProvider` (bridges `ILogger` → `adb logcat` on Android), `LoggingHandler` (HTTP DelegatingHandler), `AiringNotificationService` (WorkManager periodic check). See `/airing-notifications` for the notification subsystem.
- **Global usings** (`GlobalUsings.cs`): `Models`, `PageModels`, `Pages`, `Services`, `IconFont.Maui.FluentIcons`. `Converters` and `Utilities` require explicit `using`.
- **Key NuGet packages**: `Microsoft.Maui.Controls`, `CommunityToolkit.Mvvm`, `CommunityToolkit.Maui`, `Sentry.Maui`, `Syncfusion.Maui.Toolkit`, `IconFont.Maui.FluentIcons`.

## Integration Points (AniList API)

- **Endpoint**: `https://graphql.anilist.co`. Auth: OAuth implicit grant, redirect URI `anisprinkles://auth`, token in `SecureStorage` keys `anilist_access_token` and `anilist_access_token_expires_at`.
- **Viewer ID caching**: lightweight `Viewer { id }` query cached by token string; invalidates on re-auth.
- **Operations**: `Viewer`, `ViewerFull`, `MediaListCollection`, `Media`, `SaveMediaListEntry`, `DeleteMediaListEntry`, `UpdateUser`, `AiringSchedule` / `Staff` / `Character` / `Studio` / `Search` / `DiscoverSections` / `BrowseAnime` (the latter seven work without auth; the browse/search trio sends the token when present so `mediaListEntry` fills in the viewer's list status). `DiscoverSections` batches every Discover row's first page (+ `pageInfo`) into ONE aliased request — keep it that way (rate-limit budget); scrolling a row past its seed pages it through `BrowseAnime`, one request per row-page, via the shared `DiscoverSectionFetch`.
- **Rate limiting**: `AniListRateLimitHandler` (a `DelegatingHandler`) serializes all requests, reads `X-RateLimit-Remaining`/`X-RateLimit-Reset` for adaptive spacing, and does bounded `Retry-After`-aware retry on 429 (surfacing `ApiErrorKind.RateLimited` when the wait exceeds the cap or retries are exhausted).
- **Transient retry**: `AniListClient.SendAsync` retries once (short delay) on plausibly transient failures — `Network`, `Authentication` (e.g. AniList's sporadic `400 "Invalid token"` on a valid token), and `Unknown` — and **not** `RateLimited` (owned by the handler above), `NotFound`, or `ServiceOutage`. A recovered blip stays a `Warning` breadcrumb and isn't reported to the outage tracker.
- **Caching**: `CachingAniListClient` decorates `IAniListClient`, caching character/staff/studio reads for the session and coalescing concurrent same-key fetches. Other reads (lists, search, media, discover/browse) pass through — broader caching is a planned follow-up. Discover sections have their own ~20-minute TTL cache inside the singleton `DiscoverPageModel` (invalidated by pull-to-refresh, an adult-toggle flip, or an auth change) because the rows carry viewer-relative `mediaListEntry` state.
- **HttpClient**: singleton; pipeline is `AniListRateLimitHandler` → `LoggingHandler` → `HttpClientHandler`. Bearer token attached per-request in `AniListClient.SendAsync`.

## Airing Notifications

See `/airing-notifications` for the full subsystem architecture, key files, Preferences keys, and design decisions.

## Build and Test

```powershell
# Debug build
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android

# Release AAB (CI uses this with signing args — see .github/workflows/android-release.yml)
dotnet publish src/AniSprinkles.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=aab -o output

# CI build (activates compile-time stub services — see CI Stubs below)
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android -p:EmbedAssembliesIntoApk=true -p:CiBuild=true

# Unit tests (pure algorithm tests — no device/emulator required)
dotnet test tests/AniSprinkles.UnitTests/AniSprinkles.UnitTests.csproj -c Debug
```

- **CI**: GitHub Actions `android-release.yml` triggers on Release publication (or manual `workflow_dispatch`). `promote-release.yml` promotes between Play Console tracks (internal → alpha → beta → production).
- **Version scheme**: `ApplicationDisplayVersion` from release tag semver (`vX.Y.Z` → `X.Y.Z`); `ApplicationVersion` (versionCode) from `YYMMDDNNN` (date + run_number mod 1000).
- **Unit tests** — `tests/AniSprinkles.UnitTests/` (xUnit, `net10.0`, NSubstitute). Pure-algorithm tests and repo-level file validation (e.g. `.xaml` well-formedness); the project link-compiles specific source files from `src/` rather than project-referencing the MAUI app (the main `net10.0-android` TFM is not a valid reference target for plain `net10.0`). `Microsoft.Maui.Essentials` and `Microsoft.Maui.Core` resolve on `net10.0` and supply `IPreferences` / `IDispatcher` for test doubles; `Microsoft.Maui.Controls` and `CommunityToolkit.Maui` do not, so PageModel tests require link-compiling a non-UX partial (deferred until PageModels are split or an `AniSprinkles.Core` class library is extracted). `tests/AniSprinkles.UITests/` is still a scaffold for future device tests.

## CI Stubs (`-p:CiBuild=true`)

Passing `-p:CiBuild=true` in a **Debug** build appends `CI` to `DefineConstants`. This activates `#if CI` blocks that swap in stub services. Do **not** use `-p:CiBuild=true` with `-c Release`; the project file only supports this for Debug builds, and Release builds error out.

- `CIAuthService` (`src/Services/CI/`) — always returns `"ci-stub-token"`; app appears authenticated
- `CIAniListClient` (`src/Services/CI/`) — returns hardcoded anime list, user profile, and details-page fixtures
- `CIAiringNotificationService` (`src/Services/CI/`) — all methods are no-ops; `RequestPermissionAsync` returns `true`

These stubs are **compiled out entirely** in standard Debug and Release builds.

## Build Quality

The project must build with **zero warnings**. Do not introduce new warnings; fix any that appear before committing. CA1822 ("member can be static") is suppressed project-wide in `.editorconfig` — MAUI `{Binding}` requires instance members even when they only read static state, so the analyzer produces false positives.

## Project Conventions

- **MAUI-first guidance**: do not mix WPF/Xamarin.Forms/Blazor/React patterns unless explicitly requested. Verify MAUI APIs against official Microsoft docs and current project code before recommending.
- **Logging**: `ILogger<T>` injected via DI is the canonical path for all app code. Use structured messages. Bearer tokens are always redacted.
  - Provider wiring lives in `MauiProgram.cs`. `FileLoggerProvider` runs in both Debug (1 MB × 3 at `LogLevel.Debug`) and Release (256 KB × 3 at `LogLevel.Warning`). `AndroidLogcatLoggerProvider` bridges ILogger output into `adb logcat` on Android in all build configs (`AddDebug()` does not reach logcat on .NET MAUI Android). `AddDebug()` stays Debug-only.
  - Exception: `Platforms/Android/MainActivity.cs` and `Platforms/Android/AiringCheckWorker.cs` call `Android.Util.Log` directly because they run before the MAUI DI container is guaranteed to be built (activity lifecycle callbacks; post-reboot WorkManager). Inline comments document this at the call sites. Do not introduce new direct-`Android.Util.Log` callers elsewhere — use ILogger.
  - On-device log file: `{AppDataDirectory}/logs/anisprinkles.log` (+ `.1`, `.2` archives). Pull via `adb shell run-as com.RainbowSprinkles.AniSprinkles cat files/logs/anisprinkles.log` or use `/ani-debug`.
- **Telemetry**: Sentry crash reporting only (`SendDefaultPii = false`, `TracesSampleRate = 0.0`). Use `ErrorReportService.Record()` for handled exceptions.
- **User prefs**: `AppSettings` (static, `Utilities/`) persists title language, score format, adult content toggle, and section order via `Preferences`.

See `/project-architecture` for page patterns and performance defaults.

## Debugging Workflow

See `.claude/skills/ani-debug/SKILL.md` (`/ani-debug`) for the full adb log workflow, crash/jank/NAVTRACE scan commands, and analysis guidelines.

## Local Tools

The `tools/` directory (git-ignored) contains local development scripts:

```powershell
# Pull all open PR comments into a structured Markdown report
pwsh tools/Get-OpenPrComments.ps1
# Output: tools/pr-comments.md
```

Requires `gh` CLI authenticated (or `GH_TOKEN` env var). See `.claude/skills/ani-pr-feedback/SKILL.md` (`/ani-pr-feedback`) for the full evaluation workflow.

Project slash commands (`/ani-kickoff`, `/run-anisprinkles`, `/ani-debug`, `/ani-review`, `/ani-pr-feedback`, `/project-architecture`, `/airing-notifications`) live in `.claude/skills/`.

## Working with AI Agents

### Commits and pushes

Do not commit or push unless explicitly asked (e.g. "commit this", "push it"). The exception is when asked to create a PR — that implies doing everything needed: branch, commits, push, and PR creation.

### Self-review

After writing code, run through the review checklist in `.claude/skills/ani-review/SKILL.md` (`/ani-review`). Fix all issues found before presenting. The review loop is internal — do not surface bugs as a list of things found.

### PR feedback review

When asked to pull PR feedback, use `.claude/skills/ani-pr-feedback/SKILL.md` (`/ani-pr-feedback`).

### Presenting options

When presenting 2+ approaches to the user, list them clearly with tradeoffs for each. Do not just pick one and proceed without asking. If your client supports a popup/question UI (e.g. `AskUserQuestion`, `vscode_askQuestions`), use it instead of listing options in plain text.

### Output style

- No AI attribution or agent branding anywhere in generated project artifacts. This includes branch names, commit messages, PR titles, PR descriptions, issue bodies, comments, release notes, and footers. Avoid labels such as "codex", "Claude", "Copilot", "AI-generated", or similar unless the user explicitly asks for them.
- Be terse. Skip pleasantries and preamble.

## Security

- **OAuth implicit grant** via `WebAuthenticator`; tokens stored in `SecureStorage`, never logged.
- `ErrorReportService` redacts Bearer tokens from all error output before logging or Sentry capture.
- Sentry: `SendDefaultPii = false`. AniList Client ID (`35674`) is a public app identifier, not a secret.
- Sign-out clears both in-memory token fields and both SecureStorage keys.
- Token expiry is tracked and auto-cleared when expired (`IsExpired()` check on access).
