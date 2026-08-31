---
name: ani-review
description: "Run automatically after any code changes in this session. Iterate: fix issues found, then re-run this review, until the review passes clean. Present the summary only when the review is clean. After 5 passes without a clean result, stop and ask the user. Ask the user immediately at any genuine decision point or unexpected discovery before proceeding."
allowed-tools: Read Glob Grep
---

# Review

If issues are found: fix them, then re-run this review from the top. Do not present the summary until the review passes with no issues. Maximum 5 passes — if not clean after 5, stop and ask the user. If at any point you reach a decision where the correct fix is unclear or has significant consequences, stop and ask the user before continuing.

Review all code written in this session against the checklist below. Fix every issue found before presenting. Do not surface the list of bugs found — present only the clean summary.

## Checklist

**Async paths**

- No fire-and-forget where the result matters
- No redundant awaits
- No unobserved task exceptions on background paths

**Concurrent paths**

- When background tasks exist, trace each concurrent execution path
- Ask: "if path A skips work because path B started, is the data path B produces guaranteed to be ready before path A uses it?"

**UI-thread safety**

- Every `[ObservableProperty]` or bound property is set from the UI thread
- After `await` with `ConfigureAwait(false)`, continuation may be on a pool thread — check all failure/revert paths that set bound properties

**XAML / styling**

- Implicit styles (`<Style TargetType="X">` with no `x:Key`) apply to **every** matching element — a locally-set property can be silently overridden. Verify each local style value actually takes effect.
- **MAUI `Background` (Brush) always wins over `BackgroundColor` (Color).** The app's implicit `<Style TargetType="Border">` sets `Background`, so any `Border` needing a custom fill must set `Background` (e.g. a `SolidColorBrush`), **not** `BackgroundColor` — otherwise the accent silently renders as the default card background. For dynamic colors, the binding/converter must produce a `Brush`.
- New views are not in the CI screenshot set by default — visual-only regressions (wrong/missing accent colors) won't be caught by screenshots, so check styling by reading, not just by trusting the build.

**Execution trace**

- Walk the happy path end-to-end
- Walk every failure path end-to-end

**State lifecycle**

- State written on happy path (caches, Preferences, files) is cleaned up on sign-out, user switch, and toggle-off
- No orphaned visible state (e.g. posted notifications remaining in the shade after sign-out)

**Resource cleanup**

- All resources written on the happy path are handled in cleanup paths
- Cleanup not gated behind `if (changed)` when pruning/expiry should be unconditional

**Populate-from-server guards**

- Loading server state into bound properties uses a suppress flag to prevent side effects during population
- Side effects (permission dialogs, scheduling) handled explicitly after population completes

**API contracts**

- Verified what exceptions methods actually throw before catching them

**Public API surface**

- Changes to public/internal types considered for callers inside and outside the session's scope
- Breaking changes to shared types flagged explicitly

**Error handling**

- Failures visible to the user (via `ErrorReportService` / Sentry / UI state) rather than silently swallowed
- `catch { }` and `catch (Exception) { logger.LogDebug(...) }` patterns scrutinized — is swallowing actually correct here?

**Logging**

- New error paths have log statements at the right level
- No logging inside tight loops or hot paths that could flood logcat/file log
- Log messages use structured parameters, not string interpolation

**Sentry breadcrumbs**

- Breadcrumbs are added at decision points and major state transitions (auth, navigation, list mutations, settings changes, notification scheduling), not on every method entry
- Auth and permission decisions trace **both** branches of the dialog — confirmed and canceled — because "did the user actually sign out / grant permission" is high-value context for crash reports and support
- Reversible or repeatable user actions (delete-from-list, mark-complete) breadcrumb the **confirmed** branch only — skip the cancel breadcrumb to avoid breadcrumb buffer noise from accidental opens
- Categories are consistent with existing usage: `auth`, `navigation`, `settings`, `notification`, `list`, `http`, `state`
- Type tag matches the source: `user` for user-initiated actions, `state` for system/automatic transitions, `http` for network
- Breadcrumb messages include enough identifying context to be useful in a trace (e.g. entry id, media id) without leaking PII (no titles, no usernames in the message)

**Tests**

- New behavior has new tests; changed behavior has updated tests
- Bug fixes include a regression test that fails without the fix
- Tests cover failure paths, not just the happy path
- **Ask what is untested that could have been testable, not just whether what you wrote is tested.**
  `tests/` references `src/AniSprinkles.Core/` only, so anything left in the MAUI app project is
  unreachable — and that is where bugs hide, because nothing is watching. For each piece of new or
  moved logic sitting on the app side, ask whether it is genuinely platform-bound (WorkManager, a
  `PendingIntent`, an Activity callback) or merely *next to* something that is. Parsing, windowing,
  branching and failure classification are almost never platform-bound, and pulling them into Core
  behind a delegate or an `HttpMessageHandler` seam is usually a few lines. Existing fakes —
  `ScriptedGraphQlHandler`, `FakePreferences`, `ManualTimeProvider` — mean the seam is normally the
  only work.
- **Check the Core-side call sites of anything you changed, not only the code you wrote.** A page
  model that writes a preference, or a service wired through DI, is testable even when the thing
  consuming it is not — and those wiring tests are what stop a rename passing green while the
  feature is dead.

**Build & test health**

- `dotnet build` passes with zero warnings
- `dotnet test` green locally before presenting
- **No `#pragma warning disable`, `#nullable disable` or `!` null-suppressions — at all, without explicit approval.** A justifying comment is not enough; ask first. Restructure instead: an early return, an `is not X y` pattern, or a guard clause almost always removes the need.

**Dead code**

- After refactors: removed usings, unreferenced private members, unused parameters, orphaned files
- No commented-out code left behind

**Scope discipline**

- Change stays within what the task required — no opportunistic renames, reorders, or unrelated fixups bundled in. If unrelated issues are spotted during the work, note them and consider filing an issue rather than fixing them in the same change
- No half-finished work (stubbed methods, `throw new NotImplementedException()`, empty branches) unless tracked by an issue referenced in a comment

**Conventions**

- New code matches surrounding style (naming, file organization, access modifiers, async patterns)
- New abstractions follow existing patterns rather than introducing parallel ones

**Comments and docs**

- Match the final code, not an earlier draft

**TODOs**

- No new `TODO` / `FIXME` / `HACK` left behind unless tracked in an issue (reference the issue number)

**All callers/call sites**

- Checked existing code that interacts with what changed

**Commit-readiness**

- If multiple commits are planned, each builds and tests green on its own

**Documentation**

- Check all repo docs for sections that need updating based on the changes: `README.md`, `AGENTS.md`, `CLAUDE.md`, and any relevant skill files under `.claude/skills/` (e.g. `project-architecture`, `airing-notifications`, `ani-debug`)
- Look for stale references to architecture, services, conventions, build steps, or patterns touched in this session
- If updates are needed, make them before presenting the summary

## Summary Format

Present:

1. What was done and why (brief)
2. Architectural tradeoffs or non-obvious decisions
3. Residual concerns where the right approach is genuinely unclear

Do NOT list bugs found and fixed. Do NOT ask for approval on obvious decisions.
