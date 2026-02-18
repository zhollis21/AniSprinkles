# MAUI Android Best Practices

Purpose
- Keep one verified, source-backed reference for AniSprinkles engineering decisions.
- Prioritize official Android + .NET MAUI guidance, with .NET MAUI 10 links where available.

Scope
- Android app behavior only (startup, rendering, loading UX, navigation, API calls, diagnostics).
- MAUI-specific implementation patterns.

## 1. Core performance rules

- Keep heavy work off the UI thread.
- Avoid long synchronous work in page lifecycle methods (`OnAppearing`, constructors, binding callbacks).
- Keep first frame and startup path as small as possible.
- Treat jank (`Skipped frames`, `Davey!`, frozen frames) as a release blocker.
- Treat ANR risk as primary: Android ANRs are often caused by main-thread blocking.

Why this matters
- Android reports poor rendering and ANRs in Play Vitals and can down-rank apps.
- `Skipped` and `Davey!` logs are strong signals of frame deadline misses.

## 2. Loading UX and spinners

Use a staged loading model
- Stage 1: show shell/page scaffold immediately.
- Stage 2: show lightweight placeholders/skeleton rows.
- Stage 3: bind real data incrementally.

Spinner rules
- Prefer inline loading indicators over full-screen blocks when content already exists.
- Use full-screen loading only for first-load or truly blocking transitions.
- Add timeout or retry affordance for slow operations. Do not leave indefinite spinner-only UI.
- Keep loading state machine explicit (`Idle`, `Loading`, `Refreshing`, `Error`, `Loaded`).

MAUI controls
- Use `RefreshView` for pull-to-refresh instead of custom gesture logic.
- Use `ActivityIndicator` sparingly; avoid stacking multiple busy indicators for the same operation.

## 3. Navigation best practices

Use Shell routing consistently
- Register routes once, navigate with typed/validated parameters.
- Avoid duplicate navigation triggers from both tap gestures and selection handlers.
- Keep back navigation simple and predictable. Avoid custom back overrides unless required.

State preservation
- Preserve list state (expanded sections, scroll position, selected filter) when navigating to details and back.
- Avoid forcing a full reload on every `OnAppearing` after back navigation.
- Prefer cache + stale-while-revalidate behavior for list screens.

## 4. API and networking

Use `HttpClientFactory`
- Reuse configured clients through DI.
- Centralize timeout, retry, auth headers, and logging handlers.
- Avoid creating/disposing `HttpClient` per request.

Request patterns
- Minimize sequential "waterfall" requests on initial page load.
- Fetch in parallel where safe, then bind once.
- Debounce user-triggered fetches (search, rapid tab/list interactions).
- Cancel outdated requests when user navigates away or starts a new query.

Response handling
- Parse resiliently for schema drift/mixed scalars.
- Validate required fields before binding.
- Normalize API models into UI models before rendering.

AniList-specific notes
- Respect AniList rate limit headers and retry hints.
- On `429`, use `Retry-After` and exponential backoff.
- Avoid redundant `Viewer` queries by caching viewer identity during session.

## 5. List and rendering patterns (MAUI)

Collection and virtualization
- Prefer `CollectionView` for long lists.
- Do not nest `CollectionView` inside `ScrollView` for large datasets.
- Avoid forcing full list resets when only one group/section changed.
- Keep item templates shallow; reduce deep nested layouts.

Threading
- Build/transform data off-thread.
- Apply final collection updates on UI thread.
- Follow MAUI guidance that `ItemsSource` updates must happen on UI thread.

Bindings and XAML
- Use compiled bindings (`x:DataType`) to reduce runtime binding overhead.
- Keep converters and multi-bindings lightweight on frequently re-rendered elements.

Images
- Load appropriately sized images.
- Avoid re-decoding large images during fast scroll.
- Cache remote image requests where practical.

## 6. Startup and lifecycle

Startup budget
- Keep DI graph and startup service initialization minimal.
- Defer non-critical setup (secondary telemetry, optional caches) until after first render.
- Avoid blocking startup on network calls.

Lifecycle
- Handle app resume without redoing full initialization.
- Distinguish first launch vs foreground resume vs back navigation.

Android/.NET knobs
- Review .NET for Android build/runtime properties for startup and interop performance.
- Validate startup behavior on release-like builds, not only debugger-attached sessions.

## 7. Logging, telemetry, and diagnostics

Logging rules
- Logging must never crash app flow.
- File logger writes should be non-blocking and exception-safe.
- Keep debug verbosity bounded; filter noisy framework categories.

Telemetry rules
- Capture actionable breadcrumbs: navigation, API request start/stop, retries, user actions.
- Capture handled and unhandled exceptions with enough context to reproduce.
- Avoid high-frequency noisy breadcrumbs that mask real issues.

ANR and jank diagnosis
- Always collect filtered `adb logcat` around repro window.
- Correlate `Skipped`/`Davey!` with app operations (navigation, list bind, details load).
- Verify if jank is CPU/UI-thread bound vs network-bound before changing architecture.

## 8. Recommended defaults for AniSprinkles

Loading and list screens
- Keep My Anime cached in-memory with timestamp.
- Reload policy: show cached list immediately, refresh in background when stale or user refreshes.
- Preserve expanded states and scroll position when returning from details.

API
- Use cancellation tokens per navigation context.
- Coalesce duplicate in-flight calls for same endpoint + params.
- Add bounded retry only for transient failures and rate limits.

UI performance
- Keep details page above-the-fold small, lazy-load optional sections below.
- Avoid large numbers of interactive controls in first render pass.
- Use one primary spinner per screen state.

Diagnostics workflow
- For every jank bug: capture logs, classify (startup, bind, navigation, network), then fix one hot path at a time.

## 9. References (official)

Android
- https://developer.android.com/topic/performance/vitals/render
- https://developer.android.com/topic/performance/vitals/anr
- https://developer.android.com/topic/performance/anrs/diagnose-and-fix-anrs
- https://developer.android.com/topic/performance/threads
- https://developer.android.com/topic/performance/appstartup/best-practices
- https://developer.android.com/topic/performance/baselineprofiles/overview

.NET MAUI / .NET for Android
- https://learn.microsoft.com/en-us/dotnet/maui/whats-new/dotnet-10?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/shell/lifecycle?view=net-maui-10.0 (Appearing fires before platform makes page visible)
- https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/shell/pages?view=net-maui-10.0 (DataTemplate lazy page creation, avoid pre-creating pages)
- https://learn.microsoft.com/en-us/dotnet/maui/fundamentals/shell/navigation?view=net-maui-10.0 (GoToAsync completes after animation, PresentationMode)
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/appmodel/main-thread?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/collectionview/?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/refreshview?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/user-interface/controls/activityindicator?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/communication/authentication?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/maui/platform-integration/storage/secure-storage?view=net-maui-10.0
- https://learn.microsoft.com/en-us/dotnet/core/extensions/httpclient-factory
- https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/http/httpclient-guidelines
- https://learn.microsoft.com/en-us/dotnet/android/building-apps/build-properties
- https://devblogs.microsoft.com/dotnet/performance-improvements-in-dotnet-maui/ (defer init, lazy loggers, reduce Shell startup)

Last updated
- 2026-02-15
