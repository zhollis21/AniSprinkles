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

> **Log level note:** the app-log trace lines below (NAVTRACE, LISTTRACE, CACHE, PageState, LOADEDHOST/LOADEDVIEW, AUTH) are `LogInformation`. They land in `anisprinkles.log` on **debug** builds, but on **release** the `FileLoggerProvider` only persists Warning+, so there they appear in **logcat** (`logcat.txt`) instead — not the app log. The collector pulls both; grep whichever matches the build under test.

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

All four details pages emit the same line shape from the shared `DetailsPageModelBase` load (#120): `NAVTRACE MediaDetails|CharacterDetails|StaffDetails|StudioDetails fetch+seed in Nms (<noun> <id>, … counts); UI render follows`, plus `load start` / `not found` / `not found on AniList` / `load failed` / `load cancelled` / `load aborted — invalid <noun> id` variants. Grep the prefix to isolate one page.

MediaDetails additionally emits its own `load#N` lines (`LoadAsync enter`, `skipped because … already busy`, `media fetch completed`, `reused already-loaded media`) and the `DATATRACE load#N` list-entry lines. The `load#N` id correlates those to each other but **not** to the shared NAVTRACE lines above — correlate those by timestamp. That page drops a second load while one is in flight rather than superseding it, so the loads cannot interleave.

### LISTTRACE (app log)
`LISTTRACE <section> · <op> completed in Nms (M loaded); UI render follows` (and `… failed in Nms` on error) brackets a Character/Staff list operation (sort / Load More / check-for-more). The logged ms is **fetch + collection-apply only** — it deliberately excludes the subsequent UI render of the bound list. So if a sort *feels* slow but `LISTTRACE` shows ~150ms, the cost is the list re-render, not the API. (These lists are now virtualized horizontal `CollectionView`s, so that render cost is bounded to on-screen cells; a pre-migration `BindableLayout` re-rendered the whole list and could dominate.) A complete-set sort is reordered in memory and shows ~0–1ms. Compare against the `GraphQL <op> response ok in Nms` line for the network portion.

### CACHE (app log)
`CACHE hit|miss <key>` fires on every `CachingAniListClient` read (`Character:`/`Staff:`/`CharacterMediaPage:`/… keyed by id+page+sort). A `miss` means a real network fetch followed; a `hit` means it was served from the session cache (no HTTP, no rate-limit gate). Use it to confirm whether a slow/again-failing load actually hit the API.

### LIFECYCLE (app log, Android-only)
`LIFECYCLE MainActivity[#<hash>] On<Phase>` marks Android Activity lifecycle transitions. The `#hash` is `GetHashCode()` of the Activity instance — if the hash changes across a background cycle, the process survived but the Activity was destroyed and recreated (the classic trigger for stale `FragmentActivity` captures inside MAUI views). An `OnDestroy (isFinishing=False)` followed by a fresh `OnCreate` with a different hash confirms this.

### LOADEDHOST (app log)
`LOADEDHOST <Page> attach|detach (...)` brackets every write to `LoadedContentHost.Content` on `AnimeLibraryPage` / `MediaDetailsPage` / `SettingsPage` / `CharacterDetailsPage` / `StaffDetailsPage`. Correlate with Glide "destroyed activity" timestamps: the attach immediately preceding the cascade is the one that instantiated the Loaded*ContentView against a stale Activity. Repeated attach→detach→attach within ~1s indicates a state-flip feedback loop (usually auth-related).

### LOADEDVIEW (app log)
`LOADEDVIEW <Page>[#<hash>] constructed | OnHandlerChanged | RecyclerView handler attached (contextHash=#...)`. The `#hash` is per-view-instance; a new hash on each `LOADEDHOST attach` means the view is being fully re-materialized (cheap to log, expensive at runtime — triggers InitializeComponent, CollectionView rebind, font-icon Glide loads). The `RecyclerView contextHash` is the FragmentActivity Glide will capture — compare against the current `LIFECYCLE MainActivity` hash: mismatch confirms stale capture.

### AUTH token-check (app log)
`AUTH token-check: absent | expired, signing out | valid` fires at every `AuthService.GetAccessTokenAsync` call. The "expired, signing out" path wipes SecureStorage and flips `IsAuthenticated` to false — a routine token-refresh check becoming a full sign-out. On resume after background, this is the common trigger for `PageState: Content → Unauthenticated → InitialLoading → Content`.

### FAULT (app log / logcat, Debug only)
`FAULT armed op=… kind=… scope=…(N) delay=…ms layer=… graphql=…` on arming (logcat only — the
receiver logs through `Android.Util.Log` because a broadcast can precede DI), then
`FAULT delaying <Operation> by <ms>ms` and `FAULT failing <Operation> as <Kind>` per affected call.
`FAULT http answering <Operation> with <status>` is the HTTP seam.

**Read these before diagnosing anything else in a session where faults were armed** — an injected
failure looks identical to a real one in every downstream trace (`PageState`, `LISTTRACE`, the outage
banner), because that is precisely the point. A `FAULT cleared` line, or an app restart, disarms.

Absence of `FAULT` lines in a Debug build is normal: injection ships disarmed. See `/run-anisprinkles`
for the `driver.ps1 fault` verbs.

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
