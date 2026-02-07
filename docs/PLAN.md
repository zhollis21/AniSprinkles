# AniSprinkles Plan

Status: planning only, no app code changes yet.

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

Decisions so far
- No backend if possible
- MVP is fully online (no offline caching yet)
- Theme: Midnight Minimal for MVP
- Future: Neon Clock theme (rainbow LED inspiration)

Auth strategy (tentative)
- Prefer Authorization Code + PKCE if AniList supports public clients
- Fallback to implicit flow if PKCE is not supported
- Redirect approach for MVP: custom URL scheme unless we decide App Links are required
- Action: confirm AniList OAuth requirements and exact redirect constraints

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
- Does AniList support Authorization Code + PKCE for public clients?
- If not, is implicit flow acceptable for the MVP?
- Preferred redirect type for Android: custom scheme or App Links?
- App name and store listing constraints with AniList naming rules