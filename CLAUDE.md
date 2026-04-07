# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Before Making Changes

Read `docs/TODO.md` and `docs/PLAN.md` before making changes — they track the working task list and architectural decisions. Keep them (and `AGENTS.md`, `README.md`) up to date when decisions are made. Ask before deviating from the plan.

## Build Commands

```powershell
# Debug APK
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android

# Release AAB
dotnet publish src/AniSprinkles.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=aab -o output

# CI build (activates compile-time stub services — see CI Stubs below)
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android -p:EmbedAssembliesIntoApk=true -p:CiBuild=true
```

No runnable test suite yet — `tests/AniSprinkles.UITests/` is currently a placeholder folder with no test project file. Tests are auto-detected by CI once test `.csproj` files are added.

## Architecture

.NET MAUI Android-only app (`net10.0-android`, min SDK 31). Single project in `src/`. MVVM via CommunityToolkit.Mvvm 8.4.

**Layer flow:** Pages (XAML) → PageModels (`ObservableObject`) → Services (`IAniListClient`, `IAuthService`) → Models

**DI lifetimes** (`MauiProgram.cs`):

- Singleton: `HttpClient`, `IAuthService`, `IAniListClient`, `ErrorReportService`, `MyAnimePageModel`, `SettingsPageModel`
- Transient: `LoggingHandler`, all Pages, `MediaDetailsPageModel`

`MyAnimePageModel` and `SettingsPageModel` are **singleton** so they survive page recreation across flyout navigation.

**Navigation:** Shell flyout with routes `my-anime` and `settings`. Details route `media-details` registered via `Routing.RegisterRoute` in `AppShell.xaml.cs`. Navigate with `Shell.Current.GoToAsync` using lightweight query params (`mediaId` + trace IDs) — never pass full model objects.

**Page ↔ PageModel binding:** two-constructor pattern (parameterless + DI). `ServiceProviderHelper` provides `IServiceProvider` fallback via `IPlatformApplication.Current.Services` when `Application.Current.Handler` isn't ready during Shell startup.

**OnAppearing pattern** (both flyout pages): (1) content alive → background refresh; (2) content gone but `HasLoadedData` → immediate rebuild + background refresh; (3) first load → spinner + deferred fetch.

**Details page:** spinner-first — shell page appears immediately, full content view added after fetch. Extended sections lazy-instantiated via `MediaDetailsExtendedSectionsView`. Fetch deferred until after `OnAppearing` + first-frame yield. Navigation is non-animated to prevent partial-frame artifacts.

**Global usings** (`GlobalUsings.cs`): `Models`, `PageModels`, `Pages`, `Services`, `IconFont.Maui.FluentIcons`. `Converters` and `Utilities` require explicit `using`.

**`AppSettings`** (static, `Utilities/`): persists title language, score format, adult content toggle, and section order via `Preferences`. Loaded at app start, synced from AniList Viewer when authenticated.

## AniList Integration

- **GraphQL endpoint:** `https://graphql.anilist.co`
- **Auth:** OAuth implicit grant. Authorize URL: `https://anilist.co/api/v2/oauth/authorize?client_id=35674&response_type=token`. Redirect URI: `anisprinkles://auth`. Token stored in `SecureStorage` keys `anilist_access_token` and `anilist_access_token_expires_at`.
- **Viewer ID caching:** `AniListClient` sends a lightweight `Viewer { id }` query, caches result keyed by token string, then reuses it for `MediaListCollection`. Invalidates on re-auth.
- **Rate limiting:** not yet implemented. Planned: respect `X-RateLimit-Remaining`/`Retry-After`, 30 req/min, exponential backoff on 429.

## CI Stubs (`-p:CiBuild=true`)

Passing `-p:CiBuild=true` appends `CI` to `DefineConstants` (preserving `DEBUG` and SDK-injected symbols). This activates `#if CI` blocks that swap in stub services:

- `CIAuthService` (`src/Services/CI/`) — always returns `"ci-stub-token"`; app appears authenticated
- `CIAniListClient` (`src/Services/CI/`) — returns hardcoded anime list and user profile

These stubs are **compiled out entirely** in standard Debug and Release builds. No GitHub secret or OAuth token is needed for CI screenshots.

## Build Quality

The project must build with **zero warnings**. Do not introduce new warnings; fix any that appear before committing. CA1822 ("member can be static") is suppressed project-wide in `.editorconfig` — MAUI `{Binding}` requires instance members even when they only read static state, so the analyzer produces false positives.

## Code Style

Follow `.editorconfig`. Key rules:

- 4-space indent, CRLF, Allman braces, file-scoped namespaces, braces always required
- `var` only when type is apparent; explicit types for built-ins and unclear types
- Expression-bodied members for properties/accessors/lambdas; not for constructors/methods/local functions
- Primary constructors preferred
- Private fields: `_camelCase`; constants/static readonly: `PascalCase`
- Every async service method takes `CancellationToken cancellationToken = default`; use `ConfigureAwait(false)` in service-layer awaits
- Comments explain **why**, not what. `///` XML doc on public APIs
- MAUI-only patterns — do not mix WPF/Xamarin.Forms/Blazor/React idioms

## Debugging

Always pull both logs before analyzing an issue:

```powershell
# App file log
adb -s emulator-5554 exec-out run-as com.RainbowSprinkles.AniSprinkles cat files/logs/anisprinkles.log > logs/anisprinkles.device.log

# Logcat for the app process
$appPid = adb -s emulator-5554 shell pidof com.RainbowSprinkles.AniSprinkles
adb -s emulator-5554 logcat -v time --pid $appPid -d > logs/adb.device.pid.log

# Scan for crashes
Select-String -Path logs/adb.device.pid.log -Pattern "FATAL EXCEPTION|AndroidRuntime|Unhandled exception|ObjectDisposedException|JavaProxyThrowable" -CaseSensitive:$false

# Scan for jank
Select-String -Path logs/adb.device.pid.log -Pattern "Skipped [0-9]+ frames|Davey|GC freed" -CaseSensitive:$false

# Navigation timing traces
Select-String -Path logs/anisprinkles.device.log -Pattern "NAVTRACE" -CaseSensitive:$false
```

Validate performance on release-like builds, not only debugger-attached sessions. Classify jank as CPU/UI-thread bound vs network-bound before changing architecture.

## Local Tools

The `tools/` directory (git-ignored) contains local development scripts:

```powershell
# Pull all open PR comments into a structured Markdown report
pwsh tools/Get-OpenPrComments.ps1
# Output: tools/pr-comments.md
```

Requires `gh` CLI authenticated (or `GH_TOKEN` env var). The report includes PR overviews, review summaries, inline code comments with thread resolution status, and general comments.
