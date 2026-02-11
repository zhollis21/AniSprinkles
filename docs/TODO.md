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
- [x] Expand details page metadata coverage (release/airing, synonyms, tags, rankings, links)
- [x] Harden details JSON parsing for mixed AniList scalar types (`externalLinks.siteId`)
- [x] Perf pass on details page (cache derived lists, simplify nested list UI)
- [x] Move section grouping off the UI thread
- [x] Restore required AniList viewer-context flow (`Viewer` + `MediaListCollection`) and cache viewer ID for repeated list loads
- [x] Reduce repeat list reload work on navigation (stale-window cache + cached fallback on refresh errors)
- [x] Preserve My Anime section expand/collapse state across refreshes
- [x] Reduce details-page duplicate bind work (remove prefetch media set + client-side metadata shaping)
- [x] Re-evaluate custom details back-button override after reproducing prior crash path
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
- [x] Add reminder for Codex to read docs/DEBUGGING.md and pull device logs during troubleshooting
- [x] Add reminder for Codex to read docs/BestPractices-MAUI-Android.md at session start
- [x] Add reminder to include `adb logcat` scan as part of troubleshooting confirmations
- [x] Add reminder to follow repository `.editorconfig` standards
- [x] Clarify comment guidance: prefer concise "why/tradeoff" comments for non-obvious logic
- [x] Add MAUI/Android best-practices reference doc with official source links
- [x] Document MAUI Android logging and debugging workflow
- [x] Add debug file logging for app output (rotating log files)
- [x] Reduce debug logging overhead (async file sink + category filters + quieter Sentry debug setup)
- [x] Apply global debug-log category filters (Sentry/Microsoft/System) to reduce noisy output cost
- [ ] Investigate and fix debug session crash (collect repro + logs)
- [x] Improve failure logging UX (capture full error, copy/share, no truncated messages)
- [ ] Decide telemetry opt-in/out and add a Settings toggle
- [ ] Investigate Sentry Android warning/error logs (transport EOF/response -1, cache/delete noise) and tune SDK config
- [ ] When adding new features/refactors, add/verify telemetry breadcrumbs and exception capture
- [ ] Validate latest performance pass with post-change device logs (`logs/anisprinkles.device.log` + `logs/adb.device.pid.log` + skipped-frame scan)
