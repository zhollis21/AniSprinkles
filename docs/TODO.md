# AniSprinkles TODO

Planning
- [ ] Confirm AniList OAuth flow and PKCE support
- [ ] Choose redirect approach (custom scheme vs App Links)
- [ ] Decide app name and verify AniList naming rules
- [ ] Finalize auth decision and document it in PLAN.md
- [ ] Finalize rate limit behavior and document retry rules
- [x] Remove template domain and add AniList model/service scaffolding
- [ ] Replace mock auth and mock AniList client with real GraphQL implementation
- [ ] Create AniList client, set redirect URI, and record client ID

MVP scope
- [ ] Decide initial list view sorting and filters
- [ ] Decide search result layout and metadata shown
- [ ] Decide title details fields and layout
- [ ] Define list edit fields and validation rules

UI
- [ ] Apply Midnight Minimal tokens to base styles
- [ ] Define dominant accent per screen for MVP
- [ ] Add a "celebration" moment style (future Neon Clock theme)
- [x] Add My Anime page with example list

Process
- [ ] Decide where to track future decisions (PLAN.md or ADRs)
- [x] Add reminder for Codex to read docs/PLAN.md and docs/TODO.md (AGENTS.md or README)
