# AniSprinkles

AniSprinkles is a .NET MAUI Android app that integrates with AniList to manage anime lists, inspired by the iOS app MyAniList.

Scope

- Anime only
- No backend for MVP (client-only auth if possible)
- Fully online for MVP

MVP features

- See my list titles
- Search for new titles
- View title details
- Add titles to my list
- Edit list entries (status, episode progress, rating, etc.)

Build & Release

- GitHub Actions workflow builds signed AAB on release publication
- ApplicationDisplayVersion (versionName) extracted from release tag (e.g., v1.2.3 → 1.2.3)
- ApplicationVersion (versionCode) auto-generated from UTC timestamp (YYMMDDHHNN format)
- Signed AAB and ProGuard mapping uploaded as artifacts (90-day retention)

Current details view

- Pulls and displays expanded AniList metadata for titles (release/airing info, scores, tags, rankings, links, trailer, and streaming entries when available)
- My Anime list loading uses AniList `Viewer` + `MediaListCollection` (AniList requires user context); viewer ID is cached in-memory between loads

Planning docs

- `docs/PLAN.md` for decisions, theme tokens, and open questions
- `docs/TODO.md` for the working task list
- `docs/DEBUGGING.md` for Android debugging/log capture workflow
- `docs/BestPractices-MAUI-Android.md` for verified Android + MAUI 10 implementation patterns

Telemetry

- Sentry crash reporting only (no PII, no performance tracing yet)

Debug logging

- Debug builds write rotating app logs to `files/logs/anisprinkles.log` inside app storage on Android
- File logging is asynchronous and filtered to reduce UI-thread jank from noisy framework/Sentry categories
- Global Debug logging filters reduce noisy framework/Sentry output during troubleshooting sessions
