# AniSprinkles

AniSprinkles is a .NET MAUI Android app for managing your AniList anime library.

## Current Features

- **My Anime list** — grouped by status (Watching, Rewatching, Planning, Completed, Paused, Dropped) with collapsible sections and pull-to-refresh
- **Media details** — full AniList metadata: description with Read more/Show less, scores, airing schedule, genres, tags, rankings, studios, staff, related media, external links, and trailer
- **AniList OAuth** — sign in via AniList's OAuth implicit grant flow; token stored in Android SecureStorage
- **Settings** — title language, score format, adult content toggle, sign out; settings synced from and saved to AniList account
- **Airing notifications** — background WorkManager job polls AniList's public AiringSchedule API every 15 minutes and posts local notifications when tracked episodes air

## Planned / In Progress

- Search for new titles
- Add/edit list entries (status, episode progress, score)
- Rate limiting and retry logic for AniList API

## Build

```powershell
# Debug APK
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android

# Release AAB
dotnet publish src/AniSprinkles.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=aab -o output

# CI build (compile-time stub services — no OAuth token required)
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android -p:EmbedAssembliesIntoApk=true -p:CiBuild=true
```

Requires .NET 10 and the `net10.0-android` workload.

## Release & CI

- GitHub Actions `android-release.yml` builds a signed AAB on Release publication (or manual `workflow_dispatch`)
- `ApplicationDisplayVersion` extracted from the release tag (`v1.2.3` → `1.2.3`)
- `ApplicationVersion` (versionCode) generated from `YYMMDDNNN` (date + run number)
- Signed AAB and ProGuard mapping uploaded as artifacts (90-day retention)
- `promote-release.yml` promotes between Play Console tracks (internal → alpha → beta → production)
- CI uses compile-time stub services (`-p:CiBuild=true`) to render authenticated UI screenshots without an OAuth token

## Architecture

- **.NET MAUI Android-only** (`net10.0-android`, min SDK 31), single project in `src/`
- **MVVM** via CommunityToolkit.Mvvm 8.4
- **Navigation** — Shell flyout (`my-anime`, `settings`) + programmatic route for `media-details`
- **Auth** — AniList OAuth implicit grant; redirect URI `anisprinkles://auth`; token in `SecureStorage`
- **Telemetry** — Sentry crash reporting only (`SendDefaultPii = false`, no performance tracing)

## Planning Docs

- `docs/PLAN.md` — architectural decisions and open questions
- `docs/TODO.md` — working task list
- `AGENTS.md` — agent/AI coding instructions, debugging workflow, architecture reference
