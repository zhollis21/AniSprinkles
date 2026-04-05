# AniSprinkles Plan

Status: auth works and live list loads. Logging UX improved with details/copy/share.

Goals

- First Android app integrating with AniList
- Stack: .NET MAUI (C#)
- Scope: anime only

MVP features

- See my list titles
- Search for new titles
- View title details
- Add titles to my list
- Edit list entries (status, episode progress, rating, etc.)

MVP data contract (screen -> required fields -> source)
List (auth required)

- Media: id, title.romaji/english/native, coverImage.medium/large, format, episodes, status, season, seasonYear, averageScore, popularity
- List entry: status, progress, score, repeat, updatedAt
- Source: MediaListCollection (user anime list)

Search (public)

- Media: id, title.romaji/english/native, coverImage.medium/large, format, episodes, status, season, seasonYear, averageScore, popularity, genres
- Source: Page { media(type: ANIME, search: ...) }

Details (public)

- Media: id, title._, coverImage._, bannerImage, description (HTML), format, status, episodes, duration, season, seasonYear, genres, averageScore, meanScore, popularity, favourites, studios, tags
- Source: Media (by id)

Add/Edit list entry (auth required)

- Media id, status, progress, score, repeat, notes, private, hiddenFromStatusLists
- Source: SaveMediaListEntry mutation (create/update)

Auth visibility

- List and Add/Edit require auth
- Search and Details can be public

Decisions so far

- No backend if possible
- MVP is fully online (no offline caching yet)
- Android-only target for now (iOS/Mac/Windows removed)
- UI direction: simple, clean, standard UX/components, follow UI best practices
- Theme: Midnight Minimal for MVP
- Future: Neon Clock theme (rainbow LED inspiration)
- Replaced mock auth and mock AniList client with real implicit auth and live AniList GraphQL client
- Auth uses MAUI WebAuthenticator with SecureStorage token persistence (Android custom-scheme callback)
- Debugging workflow, best practices, and agent instructions consolidated in AGENTS.md
- Troubleshooting workflow: pull device app logs into `logs/anisprinkles.device.log` before analysis
- Confirmation workflow: include current-process `adb logcat` scan for crashes/exceptions/perf warnings
- Development workflow: follow repository `.editorconfig` standards for style/formatting/naming
- Development workflow: comments on non-obvious logic should explain intent/tradeoffs ("why"), not restate obvious code flow
- CI/CD: GitHub Actions workflow builds signed AAB on release publication
- CI/CD: ApplicationDisplayVersion (versionName) from release tag; ApplicationVersion (versionCode) from UTC timestamp (YYMMDDHHNN)
- CI/CD: versionCode format enables minute-precision builds, auto-increments monotonically (Android requirement)
- CI/CD: CI workflow (`ci.yml`) runs on push to main and pull requests — builds Debug APK, runs unit tests, captures UI screenshots on Android emulator, uploads screenshots as artifacts, and posts a PR comment linking to the artifacts
- CI/CD: CI screenshots use compile-time stub services (`CIAuthService`, `CIAniListClient` in `src/Services/CI/`) activated by `-p:CiBuild=true`. Stubs make the app appear fully authenticated with hardcoded data; no OAuth token or GitHub secret needed. Stubs are compiled out of Debug and Release builds entirely (`#if CI`).
- CI/CD: `CiBuild=true` appends `CI` to `DefineConstants` via a conditional `<PropertyGroup>` in the csproj, preserving `DEBUG` and SDK-injected symbols. `MauiProgram.cs` uses `#if CI / #else / #endif` to swap service registrations.
- CI/CD: UI screenshot job uses `continue-on-error: true` so emulator flakiness does not block the build gate
- Logging upgrade implemented (HTTP logging handler + error details UI)
- Debug file logging implemented (rotating file logger under app data)
- Performance hardening: file logger writes asynchronously and filters noisy framework/Sentry categories
- Performance hardening: Sentry SDK verbose debug mode disabled in Debug builds
- Performance hardening: My Anime and Settings page models use singleton lifetime to avoid repeated expensive reload paths
- Using CommunityToolkit.Maui; My Anime uses a grouped CollectionView for collapsible sections (avoid nested list perf issues)
- Navigation uses a flyout menu with My Anime + Settings; sign-in/out lives in Settings with a sign-in prompt on My Anime
- Telemetry: Sentry crash reporting only, no PII, tracing disabled for now
- Telemetry: add breadcrumbs for navigation, auth, and HTTP requests; capture handled exceptions
- Added a read-only details page for list items (navigated from My Anime)
- Expanded details page to fetch and render richer AniList metadata (release window, airing info, synonyms, tags, rankings, external links, streaming episodes, and trailer/site links)
- Hardened AniList details parsing for mixed scalar types (example: externalLinks.siteId may be numeric)
- Details page perf pass: cache derived lists in page model and remove nested `CollectionView` sections inside page `ScrollView`
- AniList list loading requires viewer context (`userId`), so My Anime now uses `Viewer` + `MediaListCollection` with viewer ID caching to minimize repeat viewer calls
- My Anime section grouping/build is moved off the UI thread before binding
- Debug logging filters now apply globally (including Debug output), and Sentry diagnostic verbosity is set to warning level
- My Anime now uses a 5-minute stale refresh window to avoid frequent reload/rebind work on back navigation
- My Anime preserves section expanded/collapsed state across refreshes and keeps cached sections visible if refresh fails
- My Anime default first-load expansion is now `Watching` only (other sections collapsed) to reduce first-render work
- Details page now uses default Shell back behavior; re-validation confirmed custom Android back override is not required
- Details page model no longer sets partial list media before full fetch, reducing duplicate detail-page rebind/layout work
- AniList client now pre-shapes details collections (tags/rankings/external links/streaming episodes) before UI binding
- Details page now uses an explicit first-load state (top status + centered loading indicator) and keeps heavy scroll content hidden until media data is present
- My Anime details navigation now guards against rapid repeat taps to avoid duplicate route pushes during transition
- Details poster now prefers AniList `coverImage.medium` for the details header image to reduce decode pressure on navigation
- Removed Android custom back callback override in `MainActivity`; app now uses default MAUI/Shell back behavior
- Details first-load UX is spinner-first: the lightweight details shell appears immediately while data fetch runs
- Initial details render now swaps in full content in one step after data is ready (no partial above-the-fold/extended stagger during first load)
- Temporary diagnostics route `media-details-smoke` exists for quick baseline checks of raw Shell navigation latency
- My Anime -> details navigation now passes lightweight route params (`mediaId` + trace ids) instead of full `listEntry` objects
- Details extended metadata sections are now lazy-instantiated via `MediaDetailsExtendedSectionsView` after the initial details frame
- Details page now follows spinner-first flow: lightweight shell page appears immediately, then the full details content view is instantiated and shown after fetch/bind completes
- Details first-load spinner visibility is now based on `Media == null` (and no error/status) so the page never shows a blank pre-fetch frame
- My Anime selection clear now happens after details navigation begins/completes to avoid spending tap-to-route time on source-list reselection layout
- Page constructor DI lookups now fall back to `IPlatformApplication.Current.Services` when `Application.Current.Handler` is not ready, reducing Shell flyout startup null crashes during Android activity lifecycle transitions
- Settings page loading UX fixed: `IsLoading` now wraps entire auth-check + data-fetch flow; login prompt gated behind `ShowLoginPrompt` (= `!IsAuthenticated && !IsLoading`) to prevent login-view flash on first visit
- Both flyout pages (My Anime, Settings) now use a fast-path in `OnAppearing`: when the singleton ViewModel already has cached data (`HasLoadedData`) the content view is rebuilt immediately from existing data instead of showing a blank page with a deferred delay
- Both flyout pages follow a consistent three-branch `OnAppearing` pattern: (1) content-alive → background refresh, (2) content-gone + cached data → immediate rebuild + background refresh, (3) first-ever load → spinner + deferred fetch + content build
- Decided against a shared base page class for now; the pattern is kept consistent across pages for easy extraction if a third flyout page is added
- Flyout root pages (`MyAnimePage`, `SettingsPage`) now resolve view models lazily on handler/appearance instead of constructor time so Shell flyout renderer can initialize even when service-provider wiring is still in progress
- Details fetch is now deferred until after details page `OnAppearing` plus a short first-frame yield delay, so route transition animation can finish before API/binding work starts
- My Anime -> details navigation now uses a non-animated Shell push so the spinner-first details shell appears immediately and avoids intermittent partial-frame transition artifacts
- My Anime now uses the same centered first-load spinner pattern (instead of a top status-row spinner) so loading feedback is consistent and visually anchored in the content area

Auth strategy

- Implicit grant for MVP (no backend)
- Redirect URI: anisprinkles://auth
- AniList client ID: 35674

Rate limit strategy (tentative)

- Use response headers: X-RateLimit-Limit, X-RateLimit-Remaining
- On 429, respect Retry-After and X-RateLimit-Reset
- Start conservative (30 requests/min) until headers indicate higher
- Limit concurrency to avoid burst limiting
- Batch GraphQL queries to reduce total calls

Logging upgrade plan (code)

- Add ILogger injection to services/view models
- Add HttpClient delegating handler to log GraphQL request/response metadata (method, status, latency) with token redaction
- Surface a user-friendly error view with expandable details + copy/share button
- Add in-memory last-error store for quick copy even if UI truncates
- Optionally add file logging in Debug builds for export

Theme tokens (Midnight Minimal, MVP)

- background: #0B0B10
- surface: #12121A
- surface_elevated: #181823
- border: #2A2A35
- text_primary: #E8E8F0
- text_secondary: #A7A7B8
- text_disabled: #5C5C6A
- accent_primary: #25E8FF
- accent_secondary: #7A5CFF
- accent_tertiary: #7CFF3B

Future theme plan (Neon Clock)

- Same neutrals as above
- Accents: cyan, electric blue, violet, magenta, hot pink, lime, yellow, orange, aqua
- Accents should be 10 to 20 percent of any screen
- One dominant accent per screen, plus 1 to 2 secondary accents
- Avoid full rainbow gradients except for celebration moments

Open questions

- App name and store listing constraints with AniList naming rules
