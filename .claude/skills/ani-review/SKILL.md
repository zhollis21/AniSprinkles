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

**Tests**

- New behavior has new tests; changed behavior has updated tests
- Bug fixes include a regression test that fails without the fix
- Tests cover failure paths, not just the happy path

**Build & test health**

- `dotnet build` passes with zero warnings
- `dotnet test` green locally before presenting
- No `#pragma warning disable` or `!` null-suppressions added without a comment explaining why

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
