---
name: project-architecture
description: "AniSprinkles project architecture reference: DI lifetimes, page/PageModel binding pattern, OnAppearing three-branch, details page flow, Shell navigation routes, and project-specific performance defaults. Use when working on pages, page models, navigation, DI registration, or performance."
---

# Project Architecture

## DI Lifetimes (`MauiProgram.cs`)

| Registration                                                                                                              | Lifetime                                                       |
| ------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| `ErrorReportService`, `HttpClient`, `IAuthService`, `IAniListClient`, `IAiringNotificationService`, `IOutageStateService` | Singleton                                                      |
| `MyAnimePageModel`, `SettingsPageModel`                                                                                   | **Singleton** (survive page recreation across flyout switches) |
| `LoggingHandler`                                                                                                          | Transient                                                      |
| `MyAnimePage`, `SettingsPage`, `MediaDetailsPageModel`, `MediaDetailsPage`                                                | Transient                                                      |

## Page ↔ PageModel Binding

Two-constructor pattern: parameterless (for XAML tooling) + DI constructor. `ServiceProviderHelper` provides `IServiceProvider` fallback via `IPlatformApplication.Current.Services` when `Application.Current.Handler` is not ready during Shell startup.

## OnAppearing Three-Branch Pattern (both flyout pages)

1. Content alive → background refresh
2. Content gone + `HasLoadedData` → immediate rebuild + background refresh
3. First load → spinner + deferred fetch

See `MyAnimePage.xaml.cs` and `SettingsPage.xaml.cs` for reference implementations.

## Details Page UX

Spinner-first flow: lightweight shell page appears immediately, full content view instantiated and shown after fetch/bind completes. Extended metadata sections lazy-instantiated via `MediaDetailsExtendedSectionsView`. Details fetch deferred until after `OnAppearing` + first-frame yield (avoids transition hitching). Navigation is non-animated to prevent partial-frame artifacts.

## Navigation

Shell flyout (`my-anime`, `settings`). Details route: `Routing.RegisterRoute("media-details", typeof(MediaDetailsPage))` in `AppShell.xaml.cs`. Navigate via `Shell.Current.GoToAsync` with lightweight query params (`mediaId` + trace IDs) — never pass full model objects. Rapid-tap prevention on My Anime → details. Default Shell back behavior — no custom Android back overrides.

## Performance Defaults

- 5-minute stale refresh window for flyout pages.
- Compiled XAML bindings enabled (`MauiEnableXamlCBindingWithSourceCompilation`).
- Details page: keep above-the-fold small; lazy-load extended sections below; one primary spinner per screen state.
- My Anime selection clear deferred until after navigation begins.

## Loading UX

Spinner-first for first loads; inline refresh for cached content. Details page hides heavy scroll content until media data is present.

## AppSettings

Static class (`Utilities/`). Persists title language, score format, adult content toggle, and section order via `Preferences`. Loaded at app start, synced from AniList Viewer when authenticated.

## AniSprinkles-Specific Defaults

**List screens:** Cache My Anime in-memory with timestamp. Show cached list immediately, refresh in background when stale (>5 min) or user pulls to refresh. Preserve expanded section states and scroll position when returning from details.

**API:** Use cancellation tokens per navigation context. Coalesce duplicate in-flight calls for same endpoint + params. Add bounded retry only for transient failures and rate limits (not yet implemented).

**Diagnostics workflow:** For every jank bug — pull both logs (`/ani-debug`), classify as startup/bind/navigation/network, fix one hot path at a time. Validate on release-like builds, not only debugger-attached sessions.
