<div align="center">
  <img src="StoreImages/AppIcon.png" width="140" alt="AniSprinkles app icon — a ginger cat sleeping on a pile of rainbow sprinkles" />

# AniSprinkles

A colourful .NET MAUI Android app for tracking your anime with [AniList](https://anilist.co).

[![CI Build & UI Preview](https://github.com/zhollis21/AniSprinkles/actions/workflows/ci-build-and-preview.yml/badge.svg)](https://github.com/zhollis21/AniSprinkles/actions/workflows/ci-build-and-preview.yml)
[![Android Release](https://github.com/zhollis21/AniSprinkles/actions/workflows/android-release.yml/badge.svg)](https://github.com/zhollis21/AniSprinkles/actions/workflows/android-release.yml)
![Platform](https://img.shields.io/badge/platform-Android-3ddc84?logo=android&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-10-512bd4?logo=dotnet&logoColor=white)

</div>

---

## Screenshots

|                 My Anime                 |                       Media Details                        |                 Settings                 |
| :--------------------------------------: | :--------------------------------------------------------: | :--------------------------------------: |
| ![My Anime](screenshots/01_my_anime.png) | ![One Piece Details](screenshots/02_one_piece_details.png) | ![Settings](screenshots/03_settings.png) |

> Screenshots generated automatically by CI using compile-time stub services — no OAuth token required.

---

## Features

- **My Anime list** — library grouped by status (Watching, Rewatching, Planning, Completed, Paused, Dropped) with collapsible sections, sort controls, and pull-to-refresh
- **Media details** — full AniList metadata: synopsis with Read more/Show less, scores, airing schedule, genres, tags, rankings, studios, staff, related media, external links, and trailer
- **List entry editing** — update watch progress, score, and status directly from the details page; changes sync back to AniList
- **AniList sign-in** — OAuth implicit grant via the system browser; token stored in Android SecureStorage
- **Settings** — title language, score format, adult content toggle, notification preferences; settings synced from and saved to your AniList account
- **Airing notifications** — background WorkManager job polls AniList's public airing schedule every 15 minutes and posts local notifications when tracked episodes air
- **Error handling** — classified error views with retry across all pages; technical details toggle for debugging

---

## Planned

See the [open feature backlog](https://github.com/zhollis21/AniSprinkles/issues?q=is%3Aopen+label%3Afeature+sort%3Areactions-desc) on GitHub Issues.

---

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the `maui-android` workload:

```powershell
dotnet workload install maui-android
```

```powershell
# Debug APK
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android

# Release AAB
dotnet publish src/AniSprinkles.csproj -c Release -f net10.0-android -p:AndroidPackageFormat=aab -o output

# CI build — compile-time stub services, no OAuth token required
dotnet build src/AniSprinkles.csproj -c Debug -f net10.0-android -p:EmbedAssembliesIntoApk=true -p:CiBuild=true
```

---

## Architecture

| Concern    | Choice                                                                                     |
| ---------- | ------------------------------------------------------------------------------------------ |
| Platform   | .NET MAUI Android-only (`net10.0-android`, min SDK 31)                                     |
| Pattern    | MVVM — CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`)                    |
| Navigation | Shell flyout + programmatic `media-details` route                                          |
| Auth       | AniList OAuth implicit grant; redirect URI `anisprinkles://auth`; token in `SecureStorage` |
| HTTP       | Singleton `HttpClient` with `LoggingHandler` (Bearer token redaction)                      |
| Background | WorkManager periodic job for airing notifications                                          |
| Telemetry  | Sentry crash reporting (`SendDefaultPii = false`, no performance tracing)                  |
| Logging    | `ILogger` + rotating async file log (Debug only); minimum level `Information`              |

---

## CI & Release

- **`ci-build-and-preview.yml`** — runs on every PR: builds with `-p:CiBuild=true`, captures and commits UI screenshots
- **`android-release.yml`** — triggers on GitHub Release publication: builds a signed AAB, uploads artifact and ProGuard mapping
- **`promote-release.yml`** — promotes between Play Console tracks (internal → alpha → beta → production)
- Version scheme: `ApplicationDisplayVersion` from release tag (`v1.2.3` → `1.2.3`); `ApplicationVersion` (versionCode) from `YYMMDDNNN`

---

## Docs

| File                                       | Purpose                                                                                                                    |
| ------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------- |
| [`AGENTS.md`](AGENTS.md)                   | Architecture reference, conventions, build commands, and AI agent instructions                                             |
| [`DEVELOPER_NOTES.md`](DEVELOPER_NOTES.md) | Local dev notes: error simulation, troubleshooting tips                                                                    |
| [`.claude/skills/`](.claude/skills/)       | Workflow slash commands: `/ani-debug`, `/ani-review`, `/ani-pr-feedback`, `/project-architecture`, `/airing-notifications` |
