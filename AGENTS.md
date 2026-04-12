# Agent Instructions

## Session Startup

- Read `README.md` and any task-relevant docs before making changes.
- GitHub Issues track backlog work and future plans; do not expect `docs/TODO.md` or `docs/PLAN.md` to exist.
- Keep `README.md` and this file up to date when repository-wide decisions are made.

## Architecture

- **.NET MAUI Android-only** app (`net10.0-android`, min SDK 31), single project in `src/`. App ID: `com.RainbowSprinkles.AniSprinkles`.
- **MVVM** via CommunityToolkit.Mvvm 8.4: PageModels extend `ObservableObject`, use `[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`. Shell flyout navigation (`my-anime`, `settings`) with a programmatic `media-details` route. Navigate via `Shell.Current.GoToAsync` with lightweight query params — never full objects.
- **DI**: Services and flyout PageModels (`MyAnimePageModel`, `SettingsPageModel`) are singleton. All Pages and `MediaDetailsPageModel` are transient. See `.github/instructions/maui-architecture.instructions.md` for the full DI table and page patterns.
- **Services**: `AuthService` (OAuth + SecureStorage), `AniListClient` (GraphQL + viewer ID cache), `ErrorReportService` (Sentry + `ILogger` + token redaction), `FileLoggerProvider` (rotating async file log, Debug only), `LoggingHandler` (HTTP DelegatingHandler), `AiringNotificationService` (WorkManager periodic check). See `.github/instructions/airing-notifications.instructions.md` for the notification subsystem.
- **Global usings** (`GlobalUsings.cs`): `Models`, `PageModels`, `Pages`, `Services`, `IconFont.Maui.FluentIcons`. `Converters` and `Utilities` require explicit `using`.
- **Key NuGet packages**: `Microsoft.Maui.Controls` 10.0.41, `CommunityToolkit.Mvvm` 8.4.0, `CommunityToolkit.Maui` 14.0.0, `Sentry.Maui` 6.1.0, `Syncfusion.Maui.Toolkit` 1.0.9, `IconFont.Maui.FluentIcons` 1.1.0.

## Integration Points (AniList API)

- **Endpoint**: `https://graphql.anilist.co`. Auth: OAuth implicit grant, redirect URI `anisprinkles://auth`, token in `SecureStorage` keys `anilist_access_token` and `anilist_access_token_expires_at`.
- **Viewer ID caching**: lightweight `Viewer { id }` query cached by token string; invalidates on re-auth.
- **Operations**: `Viewer`, `ViewerFull`, `MediaListCollection`, `Search`, `Media`, `SaveMediaListEntry`, `DeleteMediaListEntry`, `UpdateUser`, `AiringSchedule` (public, no auth required).
- **Rate limiting**: not yet implemented. Planned: `X-RateLimit-Remaining`/`Retry-After`, 30 req/min, exponential backoff on 429.
- **HttpClient**: singleton with `LoggingHandler`. Bearer token attached per-request in `AniListClient.SendAsync`. No timeout, retry, or rate-limit middleware yet.

## Airing Notifications

See `.github/instructions/airing-notifications.instructions.md` for the full subsystem architecture, key files, Preferences keys, and design decisions.

## Build and Test

```powershell
# Debug build
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android

# Release AAB (CI uses this with signing args — see .github/workflows/android-release.yml)
dotnet publish src/AniSprinkles.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=aab -o output

# CI build (activates compile-time stub services — see CI Stubs below)
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android -p:EmbedAssembliesIntoApk=true -p:CiBuild=true
```

- **CI**: GitHub Actions `android-release.yml` triggers on Release publication (or manual `workflow_dispatch`). `promote-release.yml` promotes between Play Console tracks (internal → alpha → beta → production).
- **Version scheme**: `ApplicationDisplayVersion` from release tag semver (`vX.Y.Z` → `X.Y.Z`); `ApplicationVersion` (versionCode) from `YYMMDDNNN` (date + run_number mod 1000).
- **No test suite yet** — `tests/AniSprinkles.UITests/` is scaffolded but empty.

## CI Stubs (`-p:CiBuild=true`)

Passing `-p:CiBuild=true` appends `CI` to `DefineConstants`. This activates `#if CI` blocks that swap in stub services:

- `CIAuthService` (`src/Services/CI/`) — always returns `"ci-stub-token"`; app appears authenticated
- `CIAniListClient` (`src/Services/CI/`) — returns hardcoded anime list and user profile
- `CIAiringNotificationService` (`src/Services/CI/`) — all methods are no-ops; `RequestPermissionAsync` returns `true`

These stubs are **compiled out entirely** in standard Debug and Release builds.

## Build Quality

The project must build with **zero warnings**. Do not introduce new warnings; fix any that appear before committing. CA1822 ("member can be static") is suppressed project-wide in `.editorconfig` — MAUI `{Binding}` requires instance members even when they only read static state, so the analyzer produces false positives.

## Project Conventions

- **MAUI-first guidance**: do not mix WPF/Xamarin.Forms/Blazor/React patterns unless explicitly requested. Verify MAUI APIs against official Microsoft docs and current project code before recommending.
- **Logging**: `ILogger` with structured messages. Debug file logs guarded with `#if DEBUG`. Bearer tokens always redacted.
- **Telemetry**: Sentry crash reporting only (`SendDefaultPii = false`, `TracesSampleRate = 0.0`). Use `ErrorReportService.Record()` for handled exceptions.
- **User prefs**: `AppSettings` (static, `Utilities/`) persists title language, score format, adult content toggle, and section order via `Preferences`.

See `.github/instructions/cs-patterns.instructions.md` for full code style and async rules.
See `.github/instructions/maui-architecture.instructions.md` for page patterns and performance defaults.

## Debugging Workflow

See `.github/prompts/debug.prompt.md` (`/debug`) for the full adb log workflow, crash/jank/NAVTRACE scan commands, and analysis guidelines.

## Local Tools

The `tools/` directory (git-ignored) contains local development scripts:

```powershell
# Pull all open PR comments into a structured Markdown report
pwsh tools/Get-OpenPrComments.ps1
# Output: tools/pr-comments.md
```

Requires `gh` CLI authenticated (or `GH_TOKEN` env var). See `.github/prompts/pr-feedback.prompt.md` (`/pr-feedback`) for the full evaluation workflow.

## Working with AI Agents

### Commits and pushes

Do not commit or push unless explicitly asked (e.g. "commit this", "push it"). The exception is when asked to create a PR — that implies doing everything needed: branch, commits, push, and PR creation.

### Self-review

After writing code, run through the review checklist in `.github/prompts/review.prompt.md` (`/review`). Fix all issues found before presenting. The review loop is internal — do not surface bugs as a list of things found.

### PR feedback review

When asked to pull PR feedback, use `.github/prompts/pr-feedback.prompt.md` (`/pr-feedback`).

### Presenting options

When presenting 2+ approaches to the user, list them clearly with tradeoffs for each. Do not just pick one and proceed without asking.

### Output style

- No AI attribution footers (e.g. "Generated with Claude Code", "Made with Copilot") in PR descriptions, issue bodies, or commit messages

## Security

- **OAuth implicit grant** via `WebAuthenticator`; tokens stored in `SecureStorage`, never logged.
- `ErrorReportService` redacts Bearer tokens from all error output before logging or Sentry capture.
- Sentry: `SendDefaultPii = false`. AniList Client ID (`35674`) is a public app identifier, not a secret.
- Sign-out clears both in-memory token fields and both SecureStorage keys.
- Token expiry is tracked and auto-cleared when expired (`IsExpired()` check on access).
