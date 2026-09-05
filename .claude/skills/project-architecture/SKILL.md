---
name: project-architecture
description: "AniSprinkles project architecture reference: DI lifetimes, page/PageModel binding pattern, OnAppearing three-branch, details page flow, Shell navigation routes, and project-specific performance defaults. Use when working on pages, page models, navigation, DI registration, or performance."
---

# Project Architecture

## Project Split (`src/`)

Two **sibling** projects since #62:

```
src/AniSprinkles/          net10.0-android   the MAUI app
src/AniSprinkles.Core/     net10.0           models, services, page models
tests/AniSprinkles.UnitTests/                project-references Core
```

- **`src/AniSprinkles.Core/`** — `Models/`, `Utilities/`, non-platform `Services/`, `Services/Abstractions/`, `Converters/`, `Icons/`, and **all** `PageModels/`. Plain TFM so the unit tests can project-reference it. References neither `CommunityToolkit.Maui` nor `IconFont.Maui.FluentIcons`, on purpose.
- **`src/AniSprinkles/`** — `Pages/`, `Views/`, `Behaviors/`, `Platforms/`, `MauiProgram.cs`, `AuthService`, `AiringNotificationService`, the CI stubs, the Android fault-injection receiver, and the `Services/Maui/` adapters implementing Core's abstractions.

**Keep them siblings — don't nest a project inside another project's folder.** Core briefly lived at `src/AniSprinkles.Core/` while the app was still `src/AniSprinkles.csproj`, and the app's default globs swept up 602 of Core's sources plus its `obj/` generated assembly attributes. It fails as `CS0579: Duplicate 'TargetFrameworkAttribute'`, which reads like a corrupt build rather than a layout problem. `<DefaultItemExcludes>` does suppress it (one property, honoured by `Compile`/`None`/`EmbeddedResource`/`MauiXaml` alike) if you ever need the trick elsewhere — but the sibling layout means nothing needs suppressing.

A PageModel must never touch `Shell.Current`, a popup type, `Browser`, `AppInfo`, `Preferences` or `MainThread` directly. Use `INavigationService`, `IDialogService`, `IUserFeedback`, `IExternalBrowser`, `IAppInfo`, `IPreferences`, `IDispatcher`. Off-device `Shell.Current` and `Application.Current` are **null** rather than throwing, so a direct call silently no-ops and its test passes without exercising anything.

XAML referencing a Core type needs `;assembly=AniSprinkles.Core` on the `clr-namespace:` — XamlC catches a miss at build time.

## DI Lifetimes (`MauiProgram.cs`)

| Registration                                                                                                              | Lifetime                                                       |
| ------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| `ErrorReportService`, `HttpClient`, `IAuthService`, `IAniListClient`, `IAiringNotificationService`, `IOutageStateService`, `INavigationService`, `IUserFeedback`, `IDialogService`, `IExternalBrowser`, `ISecureTokenStorage`, `TokenStore`, `ListEntryStatusFlow` | Singleton                                                      |
| `IPreferences`, `IAppInfo`, `TimeProvider` (`TryAddSingleton` — MAUI exposes the first two only as statics, so DI has no default) | Singleton                                                      |
| `AnimeLibraryPageModel`, `MangaLibraryPageModel`, `DiscoverPageModel`, `SearchPageModel`, `SettingsPageModel`                       | **Singleton** (survive page recreation across tab switches) |
| `LoggingHandler`, `AniListRateLimitHandler`                                                                               | Transient                                                      |

`AuthService` keeps only the platform half of auth — the `WebAuthenticator` round trip and the Android WebView cookie store — and delegates token state to Core's `TokenStore`, which owns the in-memory token, single-flights the first read of `ISecureTokenStorage`, and answers `Absent` / `Expired` / `Valid` (#119). `TokenStore` must stay singleton: a transient one would give every caller its own gate and its own copy, which is the race it exists to fix.

`IAniListClient` resolves to `CachingAniListClient` wrapping the concrete `AniListClient` (session-lifetime in-memory cache of character/staff reads, with request coalescing). The shared `HttpClient` pipeline is `AniListRateLimitHandler` → `LoggingHandler` → `HttpClientHandler`, so every AniList call is serialized and 429/`Retry-After`-aware app-wide.
| `AnimeLibraryPage`, `DiscoverPage`, `SearchPage`, `FeedPage`, `MangaLibraryPage`, `SettingsPage`, `MediaDetailsPageModel`, `MediaDetailsPage`, `StaffDetailsPageModel`, `StaffDetailsPage`, `CharacterDetailsPageModel`, `CharacterDetailsPage`, `StudioDetailsPageModel`, `StudioDetailsPage`, `MediaBrowsePageModel`, `MediaBrowsePage` | Transient                                                      |

## Page ↔ PageModel Binding

Two-constructor pattern: parameterless (for XAML tooling) + DI constructor. `ServiceProviderHelper` provides `IServiceProvider` fallback via `IPlatformApplication.Current.Services` when `Application.Current.Handler` is not ready during Shell startup.

## OnAppearing Three-Branch Pattern (all tab pages)

1. Content alive → background refresh
2. Content gone + `HasLoadedData` → immediate rebuild + background refresh
3. First load → spinner + deferred fetch

See `AnimeLibraryPage.xaml.cs` and `SettingsPage.xaml.cs` for reference implementations. `DiscoverPage.xaml.cs` is the auth-free variant (no Unauthenticated/AuthenticationPending states); its singleton page model doubles as a ~20-minute TTL cache for the Discover sections (bypassed by pull-to-refresh, invalidated by adult-toggle or auth changes).

## Discover / Browse / Search

- `DiscoverPageModel` (singleton): one aliased `DiscoverSections` request seeds every row (a `DiscoverRow` wrapping a `PaginatedSection<BrowseMediaItem>`); rows then page themselves horizontally through `BrowseAnimePageAsync` (one request per row-page) via the shared `DiscoverSectionFetch` helper. Card badges follow the row's sort via `MediaMetricBadges.ForMediaSort`. Discover holds no search — it moved to its own tab in #43.
- `SearchPageModel` (singleton): global search on the Search tab, extracted from `DiscoverPageModel`. All/Anime/Manga pills pick the type (#12), defaulting to All — persisted to `search_type_filter`, pinned per result set like the adult filter, and a flip re-runs whatever is typed. The pin is a `SearchTypeFilter?` rather than a `MediaKind?` precisely because All’s own value is null, which would otherwise be indistinguishable from “nothing seeded yet”. Debounced (600 ms, 2+ chars) queries seed a `PaginatedSection<BrowseMediaItem>` via `SearchMediaPageAsync`; a keystroke cancels the pending or in-flight fetch, and `_activeSearchQuery` guards a superseded response from landing. Four visibility states: idle prompt / spinner / results / no matches. The singleton keeps the query across tab switches on purpose; `OnAppearingAsync` re-runs it when an auth or adult-toggle flip has changed what the results should contain. Long-press goes through its own `EntryActionCoordinator`, writing mutations back onto matching results.
- `MediaBrowsePage` ("View All", transient): `DeferredContentLoader` + one `PaginatedSection<BrowseMediaItem>` over the same `DiscoverSectionFetch`; `DiscoverSectionDefinitions` is the single source of truth for section title/sort/filters/format/rank. Has the Library view-mode switcher; the mode persists to the shared `ListViewModePreference` key, so both Library halves and View All always match (Library re-syncs in `OnAppearing`).
- `BrowseTemplates.xaml` (merged in App.xaml) holds the shared templates: `BrowseMediaRowTemplate` (Standard, also used by search results), `BrowseMediaLargeTemplate` (2-col grid), `BrowseMediaCompactTemplate`, plus `ListStatusPillStyle` — the ONE on-list status pill style. Rows/grid cards overlay it inline (their own layout); the 130×170 carousel cards get it for free from `CoverImage` (`ListStatusText`/`ListStatusColor` props), which is the shared cover used by the Discover carousel AND every detail-page media carousel (relations, recommendations, studio productions, staff production roles, character appearances). Those detail pages send the viewer token so `mediaListEntry` is populated. Tap commands resolve from the hosting CollectionView's BindingContext (`NavigateToMediaCommand`).
- `EntryActionCoordinator` (`PageModels/`) owns the long-press action flows (menu, add/move/rate/edit-progress/complete/remove, persistence, toasts/snackbars) for both Library halves AND the browse surfaces; page models supply `EntryActionHost` callbacks (optimistic removal, reload vs in-place chip update, error details). `CollectionViewLongPress` (`Views/`) is the reusable Android RecyclerView long-press hook for flat CollectionViews; navigate commands call `ShouldSuppressTap()` to swallow the synthetic tap that follows a long press.

## Details Page UX

Spinner-first flow: lightweight shell page appears immediately, full content view instantiated and shown after fetch/bind completes. Extended metadata sections lazy-instantiated via `MediaDetailsExtendedSectionsView`. Details fetch deferred until after `OnAppearing` + first-frame yield (avoids transition hitching). Navigation is non-animated to prevent partial-frame artifacts.

**Shared details-page machinery** (all four detail pages — Media/Character/Staff/Studio):

- `DeferredContentLoader` (`Utilities/`) owns the deferred-load version sequencing and the loaded-content-host swap (lazy view creation + handler-disconnect teardown + render-error fallback). Each page composes one and supplies entity-specific bits (query key, `shouldShowContent`, view factory, `onRenderError`) via callbacks; `QueryAttributeParser.ParseInt` reads the int route param. Don't re-implement this per page.
- `ListOperationRunner` (`PageModels/`, MAUI-free, unit-tested) runs Load More / sort ops with the shared LISTTRACE timing + swallow-and-snackbar-on-failure contract. Page models inject `IUserFeedback` (snackbar/toast seam over CommunityToolkit.Maui, registered in `MauiProgram`) and call `_listOps.RunAsync(...)` rather than hand-rolling the trace/feedback flow.
- `FavouriteToggleRunner` (`PageModels/`, MAUI-free, unit-tested) owns the details-page favorite heart toggle: optimistic flip of `IFavouritable.IsFavourite` + count bump ±1, the `ToggleFavourite` mutation, rollback + retry snackbar on failure, and an in-flight guard for rapid taps. `DetailsPageModelBase` holds the one instance and exposes `ToggleFavouriteCommand`; the favourites stat/pill is the tap target (gated by `CanToggleFavourite` = signed-in + not busy), and the heart swaps outline↔filled glyph via `FavouriteHeartGlyphConverter`/`FavouriteHeartFontConverter`.
- `MediaMetricBadges.ForMediaSort` builds the per-sort metric badge for `RelatedMedia` list cards (Studio productions, Staff production roles, Character appearances).
- `DetailsPageModelBase<TEntity>` (`PageModels/`, `where TEntity : class, IFavouritable`) is the spine all four page models extend (#120). It owns the `PageLoadScope` + `CancelInFlight`, the `PageState`/`CurrentStateKey`/`IsBusy` block, the five error properties and a **public** `ShowError` (the pages call it from `DeferredContentLoader`'s `onRenderError`), `RetryLoad`, the favourite toggle, `OpenSiteUrl`, `NavigateToMedia`, and a templated load: invalid-id → same-id reuse → drop the old entity → fetch → seed → Content, with the error path recording through `ErrorReportService`.

  Each page model keeps its own `[ObservableProperty]` entity field — so its bindings and `[NotifyPropertyChangedFor]` lists stay put — and satisfies `protected abstract TEntity? Entity { get; set; }`. It then supplies `FetchAsync`, `SeedSections`, `EntityNoun`/`TracePrefix`, `FavouriteKind`, `SiteUrl`, and overrides only the hooks it needs: `ResetForNewEntity`, `DescribeSeededSections`, `OnFavouriteChanged`, `OnEntityReused`, `OnLoadStarting`, `OnAuthenticationResolved`, `NullResultIsRetryable`, `RetryLoadCore`, and the three error-copy pairs.

  MediaDetails is the outlier that shapes most of those hooks: it merges a navigation-supplied `MediaListEntry` on reuse (`OnEntityReused`) and treats an empty result as retryable (`NullResultIsRetryable`). It adds user-mutable list-entry state (save/delete) and a richer retry-action snackbar on top.

  Its in-flight guard is **not** a hook: `MediaDetailsPageModel.LoadAsync` drops a second load itself, before calling `LoadCoreAsync`, rather than superseding the first the way the other three do. That placement is load-bearing — the guard has to run before anything the load owns (`_lastRequestedListEntry`, the `load#N` trace id) is written, or a dropped load hands the wrong list context to the one still running and renumbers its trace lines. Both are covered by regression tests in `MediaDetailsPageModelTests`.

- Favourite counts render through `IFavouritable.FavouritesDisplay` → `MetricFormat.Compact` on every details page. Don't add a per-page formatter: three of them had drifted apart before #120, two without an M tier.

## Navigation

Shell bottom tab bar (`library`, `discover`, `search`, `feed`, `settings`). The Library tab holds two `ShellContent`s (`anime`, `manga`), which Shell renders as a native swipeable top tab strip. Placeholder pages: `feed` pending #14. Details routes registered in `AppShell.xaml.cs`: `media-details` → `MediaDetailsPage`, `staff-details` → `StaffDetailsPage`, `character-details` → `CharacterDetailsPage`, `studio-details` → `StudioDetailsPage`, `media-browse` → `MediaBrowsePage` (Discover "View All"; takes a `section` enum-name param decoded against `DiscoverSectionDefinitions`). Navigate via `Shell.Current.GoToAsync` (or the injected `INavigationService`) with lightweight query params (`mediaId` / `staffId` / `characterId` / `studioId` / `section` + trace IDs) — never pass full model objects. Rapid-tap prevention on the Library lists to details. Default Shell back behavior — no custom Android back overrides.

## Performance Defaults

- 5-minute stale refresh window for tab pages.
- Compiled XAML bindings enabled (`MauiEnableXamlCBindingWithSourceCompilation`).
- Details page: keep above-the-fold small; lazy-load extended sections below; one primary spinner per screen state.
- Library selection clear deferred until after navigation begins.

## Loading UX

Spinner-first for first loads; inline refresh for cached content. Details page hides heavy scroll content until media data is present.

## AppSettings

Static class (`Utilities/`). Persists title language, score format, adult content toggle, and section order. Loaded at app start, synced from AniList Viewer when authenticated, cleared on sign-out.

Storage goes through `AppSettings.Storage`, an `internal static IPreferences` defaulting to `Preferences.Default` (#121). It exists so the persistence paths are reachable from `tests/` — the static `Preferences.Default` throws `NotImplementedInReferenceAssemblyException` on the plain `net10.0` TFM. `TestDataBuilder.ResetAppSettings()` installs a `FakePreferences` and returns it. Nothing reassigns it in production, so the field initializer *is* the shipping behaviour. It stays static rather than becoming an injected `IAppSettings` because `Media.DisplayTitle` and `MediaListEntry.ScoreDisplay` consult it and DI never constructs those POCOs; full injection is still open in #52.

`DisplayAdultContent` has its own committing setter, `SetDisplayAdultContent`, called the moment the Settings toggle flips — ahead of the 1500 ms debounce that saves the profile to AniList (#118). Every browse surface filters on this value and only re-checks it when the page appears, so committing late left 18+ results on screen after the user turned them off. Paging must be pinned to the value its page 1 was seeded under (`_seededDisplayAdult` / `_loadedWithAdultContent`, passed into `DiscoverSectionFetch.PageAsync`) rather than re-reading the static per page, or one result set can hold two policies.

**Four surfaces compare it, not three.** Discover, Search and View All each check it against the value their results were loaded under. Library does too, on both halves (`MediaListPageModel._loadedWithAdultContent`), and needs to: its refresh short-circuit is a five-minute *time* window, so without the comparison a change would not clear even by tabbing away and back.

**A local change outranks the server's copy until confirmed.** `SetDisplayAdultContent` raises a pending flag; while it is set, `SyncFromViewer` keeps the local value for that one field whenever the server disagrees, and clears the flag exactly when the server reports the value we are holding. `Clear` drops it on sign-out. Confirmation is deliberately *not* "any viewer response" — a fresh load is not a confirmation, it just asks a server that may still be behind us or may never have received the save; treating it as one reverted the user's choice on the next visit to Settings. `PopulateFromUser` routes the bound property through `ResolveDisplayAdultContent` for the same reason: that assignment writes through to `AppSettings`, so a stale viewer would otherwise overwrite the pending value before `SyncFromViewer`'s guard saw it. Without any of this, a Library refresh inside the debounce window reads the server's stale copy and reverts the toggle app-wide. `SettingsPage.OnDisappearing` also calls `SettingsPageModel.FlushPendingSaveAsync` to send a pending change on navigate-away — that narrows the window and stops a change being lost if the app is killed, but it cannot close it on its own: Shell does not guarantee OnDisappearing precedes the next page's OnAppearing, and a save can fail.

## AniSprinkles-Specific Defaults

**List screens:** Cache each Library list in-memory with a timestamp. Show cached list immediately, refresh in background when stale (>5 min) or user pulls to refresh. Preserve expanded section states and scroll position when returning from details.

**API:** Use cancellation tokens per navigation context. Duplicate in-flight character/staff reads are coalesced and cached by `CachingAniListClient`. Rate-limit handling is centralized in `AniListRateLimitHandler` (serialized requests, `Retry-After`-aware bounded retry, `ApiErrorKind.RateLimited` when exhausted) — do **not** add per-call **429/rate-limit** retry loops. Separately, `AniListClient.SendAsync` does a single short-delay retry for *transient* failures only (`Network`, `Authentication`, `Unknown` — e.g. AniList's sporadic `400 "Invalid token"`); it deliberately excludes `RateLimited` (the handler owns that), `NotFound` (usually deterministic — but see #158: the error card still offers a manual Retry, because we could not tell a real 404 from a transient failure misfiled as one), and `ServiceOutage`. A recovered blip stays a `Warning` breadcrumb and isn't reported to the outage tracker. Character/staff detail lists are lazily paged with server-side sort (`PaginatedSection<T>`); a character's voice actors are deduped/grouped by `VoiceActorAggregator` walking a fixed popularity cursor independent of the Appears-In sort.

**API — `pageInfo` counts are unreliable (AniList quirk):** AniList documents that `pageInfo.total` and `pageInfo.lastPage` are **not accurate** ("limited due to performance issues") — `total` only matches reality when every item fits on the requested page, otherwise it returns a bogus/capped value (e.g. **500** for any studio's `media` past one page, even tiny ones). **Only `hasNextPage` is trustworthy.** So `PaginatedSection`/paging keys off `hasNextPage` (+ `currentPage` for seeding) and never `total`/`lastPage`. Do **not** surface a server "N total" count (we tried a studio "N productions" badge and it showed 500 for everyone). The `PageInfo` model therefore carries only `HasNextPage` + `CurrentPage` (no `total`/`lastPage`), and queries don't request them. Refs: [PageInfo](https://docs.anilist.co/reference/object/pageinfo), [Pagination](https://docs.anilist.co/guide/graphql/pagination).

**API — date sorts float undated entries first:** AniList's `START_DATE`/`START_DATE_DESC` put media with a null start date **first in both directions**, then order by full date (year→month→day). Detail lists seed/page server-sorted, so to keep the client local-sort fast path consistent with big (server-sorted) lists, every client date comparer in `DetailsListSorters` — `SortByMedia` (productions/appearances) **and** `SortRelations` (relations are client-only but follow the same rule for app-wide consistency) — puts undated first, then orders by `DateKey` (year→month→day). Hence the queries fetch `startDate { year month day }`. Metric sorts already match (AniList treats null metrics as 0 = last in desc); AniList's exact among-equal/among-null order isn't the `ID` tiebreak we pass and isn't reproducible, but those differences aren't visible.

**Diagnostics workflow:** For every jank bug — pull both logs (`/ani-debug`), classify as startup/bind/navigation/network, fix one hot path at a time. Validate on release-like builds, not only debugger-attached sessions.
