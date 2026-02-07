# AniSprinkles TODO

Planning
- [ ] Confirm AniList OAuth flow and PKCE support
- [x] Choose redirect approach (custom scheme)
- [ ] Decide app name and verify AniList naming rules
- [x] Finalize auth decision and document it in PLAN.md
- [ ] Finalize rate limit behavior and document retry rules
- [x] Remove template domain and add AniList model/service scaffolding
- [x] Replace mock auth and mock AniList client with real GraphQL implementation
- [x] Create AniList client, set redirect URI, and record client ID
- [x] Fix live list 400 error ("No query or mutation") and confirm list loads

MVP scope
- [ ] Decide initial list view sorting and filters
- [x] Group My Anime list by status with collapsible sections
- [ ] Decide search result layout and metadata shown
- [x] Add read-only details page for list items (using current authenticated list)
- [ ] Define list edit fields and validation rules

UI
- [ ] Apply Midnight Minimal tokens to base styles
- [ ] Define dominant accent per screen for MVP
- [ ] Add a "celebration" moment style (future Neon Clock theme)
- [x] Add My Anime page with example list
- [x] Use grouped CollectionView for collapsible My Anime sections (avoid nested list perf issues)
- [x] Add Settings page with sign-in/out and flyout navigation

Process
- [ ] Decide where to track future decisions (PLAN.md or ADRs)
- [x] Add reminder for Codex to read docs/PLAN.md and docs/TODO.md (AGENTS.md or README)
- [x] Document MAUI Android logging and debugging workflow
- [ ] Investigate and fix debug session crash (collect repro + logs)
- [x] Improve failure logging UX (capture full error, copy/share, no truncated messages)
- [ ] Decide telemetry opt-in/out and add a Settings toggle
- [ ] When adding new features/refactors, add/verify telemetry breadcrumbs and exception capture
