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

Current details view
- Pulls and displays expanded AniList metadata for titles (release/airing info, scores, tags, rankings, links, trailer, and streaming entries when available)

Planning docs
- `docs/PLAN.md` for decisions, theme tokens, and open questions
- `docs/TODO.md` for the working task list
- `docs/DEBUGGING.md` for Android debugging/log capture workflow

Telemetry
- Sentry crash reporting only (no PII, no performance tracing yet)

Debug logging
- Debug builds write rotating app logs to `files/logs/anisprinkles.log` inside app storage on Android
