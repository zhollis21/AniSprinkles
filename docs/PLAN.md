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
- Media: id, title.*, coverImage.*, bannerImage, description (HTML), format, status, episodes, duration, season, seasonYear, genres, averageScore, meanScore, popularity, favourites, studios, tags
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
- Debugging workflow documented in docs/DEBUGGING.md
- Logging upgrade implemented (HTTP logging handler + error details UI)
- File logging deferred (optional if debug session instability persists)
- Using CommunityToolkit.Maui; My Anime uses a grouped CollectionView for collapsible sections (avoid nested list perf issues)
- Navigation uses a flyout menu with My Anime + Settings; sign-in/out lives in Settings with a sign-in prompt on My Anime

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
