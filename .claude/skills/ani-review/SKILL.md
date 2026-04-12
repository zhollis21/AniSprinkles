---
name: ani-review
description: "Self-review checklist for code written in this session. Use before presenting any implementation to verify async safety, UI-thread correctness, state lifecycle, and API contracts."
---

# Review

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

## Summary Format

Present:

1. What was done and why (brief)
2. Architectural tradeoffs or non-obvious decisions
3. Residual concerns where the right approach is genuinely unclear

Do NOT list bugs found and fixed. Do NOT ask for approval on obvious decisions.
