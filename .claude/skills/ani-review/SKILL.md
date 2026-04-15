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

**Comments and docs**

- Match the final code, not an earlier draft

**All callers/call sites**

- Checked existing code that interacts with what changed

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
