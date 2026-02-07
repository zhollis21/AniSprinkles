# AniSprinkles Plan

Status: domain cleanup started; template project/task domain removed and Android-only scaffold in place. My Anime page uses mock data.

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
- Theme: Midnight Minimal for MVP
- Future: Neon Clock theme (rainbow LED inspiration)
- Mock auth and mock AniList client in place for early UI work

Auth strategy (tentative)
- Implicit grant for MVP (no backend)
- Redirect approach for MVP: custom URL scheme (exact value TBD)
- Action: create AniList client, set redirect URI, and capture client ID

Rate limit strategy (tentative)
- Use response headers: X-RateLimit-Limit, X-RateLimit-Remaining
- On 429, respect Retry-After and X-RateLimit-Reset
- Start conservative (30 requests/min) until headers indicate higher
- Limit concurrency to avoid burst limiting
- Batch GraphQL queries to reduce total calls

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
- Preferred redirect value for Android (custom scheme)
- App name and store listing constraints with AniList naming rules
