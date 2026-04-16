---
name: ani-debug
description: "Collect and interpret AniSprinkles on-device diagnostics in one shot (crash/ANR/jank scans, Glide destroyed-activity detection, PageState transitions, NAVTRACE). Use when investigating any issue observed on device or emulator — not for code-logic questions or GitHub issue triage."
argument-hint: "[describe the observed issue]"
allowed-tools: Bash(adb *) Bash(bash *collect.sh) Bash(grep *) Bash(unzip *) Read Grep
---

# Ani-debug

Investigating: $ARGUMENTS

## Device report

The block below runs at skill invocation. Raw logs are written to `/tmp/anidebug/` so you can drill in with `Grep`/`Read` without re-running the collector.

!`bash ${CLAUDE_SKILL_DIR}/scripts/collect.sh`

## How to interpret

Read the **Signals** counters first, then correlate hits with the PageState / NAVTRACE timeline.

### ANR / input dispatch timeouts (cross-PID logcat)
The main thread was blocked >5s. `ANR in <pkg>` is logged by system_server (not the app PID), so this counter uses the separate cross-PID slice. Find the hit timestamp, then look ~5–10s earlier in the app log for the operation that started the block. For the main-thread stack, use the bugreport command in **Drill-in** — skipped by default because it pulls ~80MB.

### Glide "destroyed activity" (app log)
Stack will show `GroupableItemsViewAdapter_2.onBindViewHolder` → `Glide.with(...)` → `assertNotDestroyed`. This means a MAUI RecyclerView tried to bind an image cell with a stale `FragmentActivity` reference. Common triggers:
- **StateContainer reparenting**: when a `toolkit:StateContainer.CurrentState` flips in/out of `Content`, the default child is detached/reattached, forcing a full adapter rebind cycle on the CollectionView inside. If the activity was recreated while backgrounded, every bind throws.
- **Backgrounding + auth recheck**: the sequence `Content → Unauthenticated → InitialLoading → Content` after returning from background is a known repro — two full detach/reattach cycles in a row.

A single cascade (dozens of hits in ~1s) is usually enough to starve the main thread past the 5s input-dispatch threshold → ANR.

### ObjectDisposedException (app log/logcat)
A disposed ViewModel or HttpClient used from an async continuation. Search for the nearest `OnDisappearing` / `Dispose` earlier in the timeline. Common cause: `CancellationToken` not propagated into a fire-and-forget task.

### Skipped frames / Davey (logcat)
`Choreographer: Skipped N frames` is UI-thread-bound jank. N > 60 is significant; N > 200 almost always overlaps with an ANR or heavy layout/bind pass. Correlate with NAVTRACE durations to classify CPU vs network.

### PageState transitions
Look for rapid back-and-forth flips (sub-second Content↔InitialLoading, or Content→Unauthenticated→Content). These indicate an auth race or feedback loop. Each non-Content↔Content flip is expensive under the StateContainer model — it detaches the heavy content host.

### NAVTRACE
Navigation phase timings. `details load finished in Nms` > ~1500ms indicates the details-page fetch is blocking visible rendering. `ApplyQueryAttributes → OnAppearing` gap > 300ms suggests Shell transition contention.

### LIFECYCLE (app log, Android-only)
`LIFECYCLE MainActivity[#<hash>] On<Phase>` marks Android Activity lifecycle transitions. The `#hash` is `GetHashCode()` of the Activity instance — if the hash changes across a background cycle, the process survived but the Activity was destroyed and recreated (the classic trigger for stale `FragmentActivity` captures inside MAUI views). An `OnDestroy (isFinishing=False)` followed by a fresh `OnCreate` with a different hash confirms this.

### LOADEDHOST (app log)
`LOADEDHOST <Page> attach|detach (...)` brackets every write to `LoadedContentHost.Content` on `MyAnimePage` / `MediaDetailsPage` / `SettingsPage`. Correlate with Glide "destroyed activity" timestamps: the attach immediately preceding the cascade is the one that instantiated the Loaded*ContentView against a stale Activity. Repeated attach→detach→attach within ~1s indicates a state-flip feedback loop (usually auth-related).

### LOADEDVIEW (app log)
`LOADEDVIEW <Page>[#<hash>] constructed | OnHandlerChanged | RecyclerView handler attached (contextHash=#...)`. The `#hash` is per-view-instance; a new hash on each `LOADEDHOST attach` means the view is being fully re-materialized (cheap to log, expensive at runtime — triggers InitializeComponent, CollectionView rebind, font-icon Glide loads). The `RecyclerView contextHash` is the FragmentActivity Glide will capture — compare against the current `LIFECYCLE MainActivity` hash: mismatch confirms stale capture.

### AUTH token-check (app log)
`AUTH token-check: absent | expired, signing out | valid` fires at every `AuthService.GetAccessTokenAsync` call. The "expired, signing out" path wipes SecureStorage and flips `IsAuthenticated` to false — a routine token-refresh check becoming a full sign-out. On resume after background, this is the common trigger for `PageState: Content → Unauthenticated → InitialLoading → Content`.

## Drill-in

When the summary flags something, go deeper without re-running the collector:

- Grep tool on `/tmp/anidebug/anisprinkles.log` or `/tmp/anidebug/logcat.txt` with a narrower pattern.
- Read the raw files directly for a timestamp window.
- ANR main-thread stack (slow — only when stack is required):
  ```
  adb bugreport /tmp/br.zip && unzip -o /tmp/br.zip -d /tmp/br && grep -l "com.RainbowSprinkles.AniSprinkles" /tmp/br/FS/data/anr/*
  ```
- Live tail: `adb logcat --pid $(adb shell pidof com.RainbowSprinkles.AniSprinkles)`.

## Analysis guidelines

- Validate findings against release-like builds, not just debugger-attached sessions.
- Before proposing architecture changes, classify jank as UI-thread-bound (Skipped frames / Davey) vs network-bound (NAVTRACE durations).
- If the issue is reproducible: clear logs first with `adb logcat -c`, repro, then invoke this skill — the counters map cleanly to the repro window.
