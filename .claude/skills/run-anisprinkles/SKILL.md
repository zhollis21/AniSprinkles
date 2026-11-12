---
name: run-anisprinkles
description: "Build, install, launch, and drive the AniSprinkles MAUI Android app on an emulator — read the MAUI visual tree, tap, type, scroll, screenshot, and inspect logs and network through MAUI DevFlow. Use when asked to run or start the app, take a screenshot, reproduce a UI bug, or confirm a change actually works on device rather than only in tests."
allowed-tools: Bash(pwsh *) PowerShell Bash(adb *) Bash(dotnet *) Bash(maui *) Read Grep Glob
---

# Run AniSprinkles

Android-only .NET MAUI app. There is no desktop target and no web surface — the
only way to see a change is on an emulator or device.

Two tools, split by what each is for (#170):

- **`.claude/skills/run-anisprinkles/driver.ps1`** — the device and APK lifecycle (SDK
  resolution, boot, build, install, launch), Android key events, whole-screen screenshots,
  and the app-specific broadcasts: fault injection, token seeding, notifications, the airing
  worker, deep links.
- **MAUI DevFlow** — everything about the UI. An agent inside every Debug build (CI builds
  included; never Release) serves the *MAUI* visual tree — types, `AutomationId`s, bound
  property values — plus taps, text entry, scrolling, gestures, screenshots, the app's
  `ILogger` output, captured network traffic, and preferences / secure storage. Drive it with
  the `maui devflow …` CLI, or the `maui-devflow` MCP server that `.mcp.json` registers for
  agent sessions. The `maui-devflow-*` skills are its own reference.

Paths below are relative to the repo root.

---

## Prerequisites

Already present on this machine; listed so you can check them if something breaks.

- .NET 11 SDK (`11.0.100` or any later 11.0 SDK — `global.json` sets that as a floor and rolls forward to the newest installed 11.0 feature band) with the `maui-android` workload
- Android SDK at `C:\Program Files (x86)\Android\android-sdk`
  (platform-tools 37.0.1, emulator 37.2.13, API 36 `google_apis_playstore` x86_64 image)
- An AVD — the driver defaults to `pixel_10_pro_xl_-_api_36`
- The `maui` CLI as a global tool, on the same version as the `Microsoft.Maui.DevFlow.Agent`
  package in `Directory.Packages.props` (both preview, both from `dotnet/maui-labs`):
  `dotnet tool install -g Microsoft.Maui.Cli --version <that version>`

Check all of it at once:

```bash
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 env
```

```bash
maui doctor
```

---

## Build

Always build with `-p:CiBuild=true`. That swaps in `CIAuthService` /
`CIAiringNotificationService` and puts `FixtureReplayHandler` at the bottom of the HTTP
pipeline (`src/AniSprinkles/Services/CI/`), so the app launches **already signed in** against
recorded AniList responses (#134): no OAuth round-trip, no real AniList traffic, no
rate-limit budget spent, and a deterministic list every run. Unlike the old
`CIAniListClient`, replay sits *below* the client, so the real `AniListClient` and its
caching decorator run — which is why a CI build now behaves like the app rather than
like a stub.

A plain Debug build drops you on the signed-out screen. `driver.ps1 seed-token` gets you
past that without tapping through `WebAuthenticator` (#160) when you need *real* AniList
data rather than fixtures.

```bash
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 build
```

Takes **about a minute and a half** (78 s measured from a wiped `bin`/`obj` on .NET 11, and much the
same warm — packaging dominates, not compiling) and produces a ~150 MB APK
at `src/AniSprinkles/bin/Debug/net11.0-android/com.RainbowSprinkles.AniSprinkles-Signed.apk`.
Skip it if you have not touched `src/` since the last build.

**Build the Android head exactly one way: `driver.ps1 build`.** It is tempting to run a
plain `dotnet build src/AniSprinkles/AniSprinkles.csproj -c Debug -f net11.0-android` to check for
warnings and then `driver.ps1 build` for the APK — that wastes a second build
*and* leaves a broken APK, because the plain build omits `EmbedAssembliesIntoApk` and
overwrites the deployable ~150 MB APK with a ~22 MB Fast Deployment one (see Gotchas). The
driver's build prints the same warning and error counts, so it covers the review pass too.

`dotnet test` does **not** need any of this — the test project targets plain `net11.0`, so
run it directly and never build the Android head just to run tests.

**Do not chain a build with device driving in one shell call.** `build` alone can run 6+
minutes; adding `install`/`launch` and DevFlow calls behind it means one stalled step burns the whole
timeout and takes the completed steps down with it. Build in its own call (background it if
you have other work), then drive in a second.

---

## Run (agent path)

One-shot — boots the emulator if needed, installs, starts DevFlow's broker, launches:

```bash
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 up
```

`launch` (and so `up`) runs `adb reverse tcp:19223 tcp:19223` and starts the broker before the
app starts, because the agent looks for the broker once, at startup. Confirm it registered:

```bash
maui devflow list
```

### The device — `driver.ps1 <command> [args]`

| | |
|---|---|
| `env` | resolved SDK / adb / APK / device state |
| `boot [avd]` | start emulator, block until `sys.boot_completed`, kill animations |
| `build` / `install` / `launch` | individually |
| `resume` | re-foreground the app **without** restarting it (warm, ~3s) |
| `stop` / `kill-emu` | force-stop the app / shut the AVD down |
| `shot <name>` | whole-screen PNG → `tmp/driver-screens/<name>.png`, system UI included (notification shade, permission dialogs) |
| `key <KEYCODE>` / `back` | any Android key event; `KEYCODE_` is prefixed for you |
| `logcat [n]` / `applog` | app-PID logcat tail / the on-device rotating file log |
| `fault <op> <kind> [scope]` | arm a fault on the **running** app — no rebuild (#125) |
| `fault clear` | disarm |
| `seed-token [test\|real]` | sign a **real-auth** Debug build in over adb, no OAuth tapping (#160) |
| `dump-token` | read the signed-in token back out to `tmp/dev-token.txt` (gitignored) |
| `notify [media] [ep] [title]` | post a **real** airing notification (#111) — see below |
| `deeplink <media> [nonce]` | fire the notification's deep-link intent without a notification |

`driver.ps1 help` prints the same list.

### The UI — `maui devflow …`

| | |
|---|---|
| `ui tree [--depth N]` | the MAUI visual tree as JSON — types, ids, `AutomationId`, text, bounds |
| `ui query --automationId X` / `--text X` / `--type X` / `--selector CSS` | find elements; `--text` is a substring match, `--selector 'Label[Text^="View All"]'` takes CSS |
| `… --wait-until exists\|gone --timeout N` | add to `ui query` to poll until it appears / disappears |
| `ui tap <elementId>` / `--automationId X [--index N]` / `--text X` | tap the element itself — see the tappability gotcha |
| `ui fill --automationId X <text>` / `ui clear --automationId X` | set / empty an `Entry` |
| `ui scroll --element <id> [--item-index N]` / `--dy N` | scroll an element into view, a `CollectionView` to an item, or by delta |
| `ui gesture longpress --automationId X [--duration ms]` | long press (and swipe / pan / pinch / doubletap) |
| `ui hit-test <x> <y>` | every element at a point, innermost first — how you find who owns a gesture |
| `ui screenshot --output <path>` | the app's window only (use `driver.ps1 shot` for system UI) |
| `ui navigate <route>` | `Shell.GoToAsync` by route |
| `batch` | run several `ui …` lines from stdin in **one** session — required for back-to-back taps (see Gotchas). `MAUI <verb>` is an alias for `ui <verb>` (what `maui-devflow-debug/references/batch.md` shows); `MAUI ui <verb>` is an error |
| `logs [--limit N] [-f]` | the app's `ILogger` output — `NAVTRACE`, `PageState`, `HTTP` |
| `network list` / `network detail <id>` | captured AniList requests, headers and bodies included |
| `storage preferences list` / `storage secure-storage get <key>` | app storage (writes need the MCP server — see Gotchas) |

From an agent session the same operations are MCP tools (`maui_tree`, `maui_query`, `maui_tap`,
`maui_back`, `maui_secure_storage_set`, … — 86 in all), served by `maui devflow mcp` through
`.mcp.json`. The MCP server holds one session, so it has none of the CLI's lease problem.

**Ids to drive by** (`AutomationId`, so `ui … --automationId`; also the uiautomator
`resource-id`, so #162's CI script can use the same ones):

| `AutomationId` | Element |
|---|---|
| `MediaCard` | a Library card, in all three layouts (`--index N` picks one) |
| `IncrementProgressPill` | the `+1 EP` / `+1` pill on a Watching / Rewatching card |
| `FavoriteToggle` | the heart on the media, character, staff and studio details pages |
| `RetryButton` | Try Again in `ErrorStateView`, on every page that shows one |
| `SearchEntry` | the Search tab's text field |

Shell tabs need no `AutomationId`: DevFlow names them from their routes — `Tab_library`,
`Tab_discover`, `Tab_search`, `Tab_feed`, `Tab_settings`. Those are **element ids**, so it is
`ui tap Tab_search`, not `--automationId Tab_search`.

Old driver verbs, for muscle memory: `dump` → `ui tree` / `ui query`; `tap "X"` →
`ui tap --text X` (or an id); `tap-desc` → `--automationId`; `tap-prefix` → `--text` (already a
substring match); `wait-for` → `ui query … --wait-until exists`; `scroll-to` → `ui scroll`;
`longpress` → `ui gesture longpress`; `type` / `clear` → `ui fill` / `ui clear`; `goto Tab` →
`ui tap Tab_<route>`; `xml` → `ui tree`.

### Driving error states — `fault`

Error and retry states used to be unreachable on device: the old `-p:ErrorSim=true` build failed
*every* call, so Library and Discover died before you could navigate to the page you wanted to
break. `fault` decorates the client instead of replacing it, so it composes with the CI fixtures —
a real screen loads, then the next call fails.

```powershell
driver.ps1 fault GetMedia ServiceOutage next   # details page → error; Try Again then SUCCEEDS
driver.ps1 fault GetStudio NotFound always     # studio page stays broken until cleared
driver.ps1 fault any delay next -delay 4000    # 4s latency, no failure — opens timing windows
driver.ps1 fault clear
```

- `op` — `any`, or a prefix whose meaning depends on the layer:
  - `-layer client` (default) — an `IAniListClient` method name; `GetStudio` matches `GetStudioAsync`
  - `-layer http` — the GraphQL `operationName`; use `Studio`, `Media`, `MediaCharactersPage`

  **They are not interchangeable, and a miss is silent.** `fault GetStudio … -layer http` matches
  nothing, because over the wire that operation is called `Studio`. Nor does stripping `Get`/`Async`
  save you: `GetMediaListAsync` is `MediaListCollection` and `SearchMediaPageAsync` is `Search`.
  When a targeted http profile misses, the handler logs `FAULT http no match` naming the operation it
  actually saw — grep for that if an armed fault seems to do nothing.
- `kind` — `ServiceOutage` | `Network` | `Authentication` | `RateLimited` | `NotFound` | `Unknown`,
  or `delay` for latency without failure
- `scope` — `next` (default) | `always` | `firstn:N` | `everynth:N`, all deterministic

**`scope next` is the one that proves recovery**, because the fault is spent on the first call and
**Try Again** genuinely succeeds. That path — on all four details pages, plus `FavouriteToggleRunner`
and `ListOperationRunner`'s rollback snackbars — had never run on a device before this existed.

**`-delay` with no kind is how you reproduce lifecycle and debounce bugs.** The CI stubs return
instantly, so cancellation and `IsBusy`/`CanLoadMore` windows are zero-width on device; a delay is
what opens them. Arm one, navigate away mid-load, and watch whether `LoggingHandler`'s `HTTP POST` /
`HTTP 200` lines stop (#132). An abandoned request logs `HTTP cancelled` rather than `HTTP failed` —
on Android it surfaces as `WebException("Socket closed")`, so the token is what tells them apart.

`-layer http` moves injection inside the `HttpClient` pipeline so `AniListRateLimitHandler`,
`SendAsync`'s retry-once and `AniListErrorClassifier` run for real — but it needs a real signed-in
session and does **not** work with a `-p:CiBuild=true` build. Add `-graphql` there to answer HTTP 200
with a GraphQL `errors` array, which is how AniList reports many failures.

Faults are disarmed at every app start, and none of this exists in a Release build.

**The loop that works:** `ui query` (or `ui tree`) to see what is there → `ui tap` an id →
`ui query … --wait-until exists` for what you expect next → `ui screenshot` → query again. Run
the mutating steps of one flow as a single `batch`. Never screenshot straight after a tap that
navigates without the wait between them — the page is still loading.

### Worked example — a full round trip

Run against the emulator on a `driver.ps1 build` (fixtures) on 2026-11-12, output trimmed to the
per-line results. One `batch`, so the two taps share a session:

```bash
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 up
```

```bash
printf '%s\n' \
  'ui query --automationId MediaCard --wait-until exists --timeout 60 --format compact' \
  'ui tap --automationId MediaCard --index 0' \
  'ui query --automationId FavoriteToggle --wait-until exists --timeout 45 --format compact' \
  'ui screenshot --output tmp/driver-screens/before.png' \
  'ui tap --automationId FavoriteToggle' \
  'ui screenshot --output tmp/driver-screens/after.png' \
  | maui devflow batch
```

```
{"command":"ui tap --automationId MediaCard --index 0","exit_code":0,"output":"{\"success\":true,\"action\":\"Tapped\",\"elementId\":\"MediaCard\"}"}
{"command":"ui tap --automationId FavoriteToggle","exit_code":0,"output":"{\"success\":true,\"action\":\"Tapped\",\"elementId\":\"FavoriteToggle\"}"}
```

Then `Read` the two PNGs: the first card (*That Time I Got Reincarnated as a Slime Season 4* in
the current fixtures) with the heart outlined, then filled. Its Favourites count is `5.6k`, so the
`+1` does not show in the number. `batch` stops at the first line that fails and reports its
`exit_code`, so a missing element surfaces as the line that broke rather than as a later
screenshot of the wrong page.

### Driving airing notifications — `notify` and `deeplink`

`CIAiringNotificationService` is a no-op, so a CI build never schedules the worker and never posts
anything. These two commands reach the notification path anyway, without changing that stub and
without any AniList traffic:

```powershell
driver.ps1 notify 16498 25 "Shingeki no Kyojin"   # posts a real notification
driver.ps1 deeplink 21                            # fires the tap's intent, no notification
driver.ps1 deeplink 21 4242                       # same nonce twice = replay; expect it ignored
```

`notify` goes through the production `NotificationHelper.Show`, so tapping the result exercises the
real `PendingIntent`. `deeplink` skips the notification and covers `MainActivity` / `AppShell` /
`PendingDeepLink` only — use it for the state matrix, and `notify` + a real tap to cover the
PendingIntent construction, which has no unit-test reach at all.

**Grant POST_NOTIFICATIONS first, or `notify` silently does nothing.** The CI stub's
`RequestPermissionAsync` returns true without ever asking, so on API 33+ the runtime permission is
never granted and `NotificationManagerCompat.Notify` no-ops — the app logs `NOTIFY posted` and no
notification appears:

```bash
adb shell pm grant com.RainbowSprinkles.AniSprinkles android.permission.POST_NOTIFICATIONS
```

**DevFlow cannot see the notification shade** — it is not the app's window. Expand it with
`adb shell cmd statusbar expand-notifications`, `driver.ps1 shot` it, and tap by coordinate off the
image with `adb shell input tap <x> <y>`.

To force the four arrival states, and the replay case that needs a task restore rather than a plain
relaunch:

| State | Setup |
|---|---|
| Process dead | `adb shell am force-stop <pkg>` first |
| Process alive, activity destroyed | `adb shell settings put global always_finish_activities 1`, HOME, then fire — **set it back to 0 afterwards** |
| Backgrounded | `driver.ps1 key HOME`, then fire |
| Foreground | fire while it is on screen |
| Task restore replay | `adb shell am kill <pkg>` — **not** `force-stop`, which drops the task — then reopen from recents |

### Fixture data you can drive against

The recorded fixtures (#134) are a real AniList account, so these strings are findable with
`ui query --text`. They change whenever the fixtures are re-recorded — if a lookup fails, query
the screen rather than trusting this list, and check
`src/AniSprinkles/Fixtures/AniList/MediaListCollection__*.json` for what the Library actually holds.
Prefer the `AutomationId`s above where one exists: they survive a re-recording, and titles do not.

- **Library › Anime** (nav title reads "Library"; sub-tabs are `ANIME` / `MANGA`) — `Watching`
  (5, as of 2026-11-12): `That Time I Got Reincarnated as a Slime Season 4`,
  `The Qwaser of Stigmata II`, `Sword Art Online`, `ONE PIECE`, `Gintama Season 4`. `Completed`,
  `Planning`, `Dropped` and `Paused` are populated too — every status has entries, deliberately.
- **Library › Manga**: `Reading` has `Shingeki no Kyojin` and `ONE PIECE`; `Completed` has
  `Chainsaw Man`.
- **Media details** (ONE PIECE): `Luffy D. Monkey`, `Zoro Roronoa` — family-name-first, because
  names render from `userPreferred` resolved against the account's Staff Name Language (#130).
  The heart is `FavoriteToggle`; studio chips carry the accessibility description
  `Open studio <name>`.
- **Discover**: `Currently Airing`, `Trending Now`, `Top Anime`, `View All ›`
- **Search tab**: only recorded queries resolve — `no`, `one`, `ka`. Anything else is a
  `FIXTURE MISS`, not a stub quirk: replay has no recording for it and says so in logcat.
- **Manga** (#12) — also reachable by `driver.ps1 deeplink <id>`, which is the fastest route:

  | id | What it is | Why it exists |
  |---|---|---|
  | `53390` | Shingeki no Kyojin manga, 141ch / 34vol, chapter-tracked | The rich one — tags, rankings, external links, stats, characters, staff, and a relation back to the anime. Drives the completion flow. |
  | `30013` | ONE PIECE manga, RELEASING, volume-tracked | Chapters and volumes both null, which is what AniList returns for *every* publishing series: no cap, no progress bar, unbounded +1, prompt reads "Enter volume". |
  | `105778` | Chainsaw Man, Completed, both counters set | Chapters win over volumes — the case that proves the unit is inferred, not just "has volumes". |
  | `85199` | AoT: No Regrets, off-list | No list entry, so this is the only way to reach the details page's **Add to List**. |
  | `85476` | No Regrets Prologue, ONE_SHOT, 1ch / no volumes | The singular chip (`1 Chapter`) and a Chapters chip with no Volumes chip beside it. |
  | `85470` | Mushoku Tensei, NOVEL, 334ch / 26vol | AniList files novels under type MANGA; proves the page keys off type, not format. |

  Manga search honours `isAdult` exactly as anime search does. The 18+ canary is synthetic and
  spliced into replayed responses by `AdultCanary` (#134), not a recorded fixture.

---

## Run (human path)

Deploy from Visual Studio / `dotnet build -t:Run`, or just `driver.ps1 up` and
look at the emulator window. Useful for OAuth-dependent work — the CI stubs are
the only scriptable way past sign-in, so testing the *real* auth flow means
building without `-p:CiBuild=true` and tapping through the browser yourself.

---

## Test

```bash
dotnet test tests/AniSprinkles.UnitTests/AniSprinkles.UnitTests.csproj -c Debug
```

Pure-algorithm + XAML-well-formedness tests only; no device needed. They do **not**
cover PageModels or any UI, so a green run says nothing about whether the app
works — that is what the driver and DevFlow above are for.

---

## Gotchas

- **One Android SDK, and everything points at it.** The build and the driver use
  `C:\Program Files (x86)\Android\android-sdk`. `ANDROID_HOME`, PATH's
  `platform-tools` and `JAVA_HOME` (`C:\Program Files\Android\openjdk\jdk-21.0.8`)
  are set to the same install, because that is what the `maui` CLI and a bare `adb`
  read. A second SDK anywhere on PATH means two adb versions, and each one kills the
  other's server on start — dropping every `adb reverse` / `forward` mid-session,
  DevFlow's included. Android Studio's `%LOCALAPPDATA%\Android\Sdk` was exactly that
  until it was removed on 2026-11-12. `maui doctor` shows which SDK and JDK it
  resolved; `driver.ps1 env` shows the driver's.

- **Git Bash rewrites device paths.** `adb shell df /data` from Bash becomes
  `df 'C:/Program Files/Git/data'`. Prefix with `MSYS_NO_PATHCONV=1`, or just use
  the driver (PowerShell, unaffected). This silently returns empty output rather
  than erroring, which is how it burns you.

- **Never pipe `adb exec-out screencap -p` into a PowerShell redirect** — the PNG
  comes out corrupt (text encoding + CRLF). `driver.ps1 shot` writes on-device and
  `adb pull`s it back, which is byte-exact.

- **`adb shell` mangles output you intend to parse** (LF→CRLF). The driver uses
  `exec-out` everywhere it reads.

- **Separate `maui devflow` calls trip a ~10 s mutation lease.** Every CLI invocation is a new
  session, and a tap takes a short lease on the app; the *next* process to mutate inside that
  window is refused with `Another DevFlow session is driving this app (MAUI CLI)` (exit 1,
  `retryable: false`). Put consecutive taps / fills in one `maui devflow batch`, or use the MCP
  server, which is one session. Read-only calls (`ui tree`, `ui query`, `logs`, `network list`)
  never take the lease.

- **`batch` only runs `ui` and `webview` lines.** `storage … set` / `delete` are refused there
  and refused one-shot even with no lease held — secure-storage and preference *writes* go through
  the MCP tools (`maui_secure_storage_set`, `maui_preferences_set`). Reads work from the CLI.

- **Tap the element that owns the gesture.** DevFlow invokes the element's recognizer, not a
  screen coordinate, so a `Label` inside a card answers `400 … not tappable`. Library cards carry
  `MediaCard` on the element with the `TapGestureRecognizer`; anything else, `ui hit-test x y`
  lists the stack at that point and the owner is one of the outer entries.

- **`Tab_<route>` ids are element ids, not `AutomationId`s.** `ui tap Tab_search` works;
  `ui tap --automationId Tab_search` finds nothing. Tab taps go through MAUI, so the Search tab's
  keyboard (which covers the tab bar) does not get in their way.

- **Query output is JSON, and `--format compact` keeps it to id / type / text / automationId /
  bounds.** `bounds` are relative to the parent; `windowBounds` (in the full form) are on screen.

- **`SearchPageModel` is a singleton, so the Search tab keeps its query forever** —
  leaving the tab, backing out of the app entirely, and coming back still shows the
  old query and its results. That is deliberate, not a bug. `ui clear --automationId
  SearchEntry` if a test needs the idle prompt back. Discover no longer has a
  search bar at all, so a `--wait-until` on `Trending Now` timing out there means something
  else went wrong.

- **`back` at a tab root exits the app silently** — no confirm dialog, you just
  land on the launcher and every subsequent lookup fails. `driver.ps1 resume` gets you back in
  ~3s. Prefer `ui tap Tab_<route>` over `back` for switching tabs.

- **Network capture records the `Authorization` header verbatim** — a documented exception to
  "bearer tokens are always redacted" (`AGENTS.md`), as is `storage secure-storage get` on the
  token's key. In a fixtures build that is
  `CIAuthService`'s fake token. In a real-auth Debug build (after `seed-token`) `network detail`
  shows your real AniList token — readable from this PC only (the agent and its forward are on
  loopback; the broker listens on every interface but answers `403 loopback required` to anything
  else, a faked `Host: localhost` included; browser origins are refused). Anything an agent
  session reads, though, is sent to the model with that session.

- **`dotnet watch` hot reload works on small pages only.** A static `Label.Text` edit on
  `FeedPage.xaml` applied in ~5 s and refreshed the open page; the same edit on the 1,330-line
  `MediaDetailsLoadedContentView.xaml` never applies — Roslyn's closure analysis pins a core for
  minutes and the watcher stops picking up saves. Use `driver.ps1 build` for that page. Details on
  #170.

- **A plain `dotnet build` silently clobbers the deployable APK.** Running
  `dotnet build src/AniSprinkles/AniSprinkles.csproj -c Debug -f net11.0-android` to check for
  warnings is fine, but it omits `-p:EmbedAssembliesIntoApk=true` and leaves a
  ~22 MB *Fast Deployment* APK where the ~150 MB one was. Installing that gives a
  process that dies instantly, before any managed code, with no .NET stack trace —
  just `SIGABRT` and, buried in logcat:

  ```
  E monodroid: CLR error: Failed to load System.Private.CoreLib.dll (error code 0x80070002)
  F monodroid: Failed to initialize CoreCLR. Error code: 80070002
  ```

  It reads like a crash in your change and is not one. **Check the APK size** — if
  it is ~22 MB rather than ~150 MB, that is the whole story. Rebuild with
  `driver.ps1 build` (which passes both flags) before installing. Fast Deployment
  keeps its files outside the app (`/data/local/tmp/fastdeploy2/<package>` on .NET 11), so if the
  failure persists after a correct rebuild, uninstall the package before reinstalling.

- **The debug APK is ~150 MB and installs land on `INSUFFICIENT_STORAGE`** once the
  AVD's `/data` gets tight (mine had 570 MB free and still failed). `driver.ps1
  install` detects it, uninstalls the old copy, and retries. Note that `adb install`
  prints `Failure [...]` and can still exit 0 — the driver parses the output text,
  so don't trust a bare `adb install`'s exit code.

- **With two devices attached, pick one explicitly.** Every driver command targets
  a single device via `adb -s`. With one device it resolves automatically; with
  more than one it stops and lists them rather than letting `adb` fail with a bare
  "more than one device/emulator" partway through a flow. Set `ANDROID_SERIAL` to
  choose:

  ```bash
  ANDROID_SERIAL=emulator-5554 pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 shot home
  ```

  `driver.ps1 env` lists every attached device with its AVD name and shows which
  one is currently resolved.

- **`boot` reuses an emulator only if it is the AVD you asked for.** A different
  AVD already running does not count, so `boot tablet_h-dpi_13_5in_-_api_36_0`
  starts that tablet even with a Pixel online. It also waits for
  `sys.boot_completed` on reuse, because `device` state only means adb can talk to
  the emulator — not that Android finished booting.

- **Cold launch is ~19s** (`TotalTime: 18697`) — Debug MAUI with embedded
  assemblies. `driver.ps1 launch` polls for the process, so it returns before the
  UI has content. `ui query … --wait-until exists` is what tells you the page actually rendered.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `PANIC: Cannot find AVD system path. Please define ANDROID_SDK_ROOT` | You launched the PATH emulator. Use `driver.ps1 boot`. |
| `adb server version doesn't match this client` | Two adb versions fighting. `adb kill-server`, then let the driver restart the right one. |
| `INSUFFICIENT_STORAGE: Failed to override installation location` | `driver.ps1 install` handles it; if it still fails, wipe the AVD from Device Manager. |
| `Another DevFlow session is driving this app (MAUI CLI)` | Two mutating CLI calls inside the lease window. Put them in one `maui devflow batch`, or wait ~10 s. |
| `… not tappable` (HTTP 400) on a `ui tap` | You tapped a child of the element with the gesture. `ui hit-test x y` and tap the owner. |
| `maui devflow list` shows no agent | The broker was not up when the app started. `driver.ps1 launch` (it starts the broker and the `adb reverse` first); without a broker the agent still answers on `--agent-port 9223` after `adb forward tcp:9223 tcp:9223`. |
| `No elements found matching automationId=…` right after it worked | You probably backed out to the launcher, or are on another page. `ui tree --depth 3` to see where you are, then `driver.ps1 resume`. |
| `maui android sdk …` / `maui doctor --fix` fail with `exit code 255` | The cmdline-tools 23.0 `sdkmanager.bat` shim breaks on the `(x86)` in the SDK path. It was patched locally on 2026-11-12, and a cmdline-tools update reverts the patch. Re-check with `maui android sdk list`. DevFlow itself does not use it. |
| `More than one device attached — set ANDROID_SERIAL to choose` | Two emulators/devices online. Pick one with `ANDROID_SERIAL=<serial>`, or shut the other down. `driver.ps1 env` lists them. |
| `sys.boot_completed never reached 1 after 180s` | The emulator attached to adb but never finished booting. Check the emulator window; a wipe-data from Device Manager usually clears it. |
| `AVD <name> never attached to adb`, and `emulator`/`qemu-system-x86_64` are running but idle (well under 1s CPU, no port in the 5554-5600 range listening) | A stale AVD lock. qemu starts, finds the AVD claimed, and hangs instead of exiting — so each retry silently adds another hung pair. Kill every `emulator`/`qemu*` process, then delete `~/.android/avd/<name>.avd/*.lock` (`hardware-qemu.ini.lock`, `multiinstance.lock`) and boot again. Run `emulator -verbose` to confirm: it prints `Running multiple emulators with the same AVD`. Check acceleration first with `emulator -accel-check` to rule out WHPX. |
| `df 'C:/Program Files/Git/data': No such file` | Git Bash path mangling — `MSYS_NO_PATHCONV=1`. |
| App runs but shows the signed-out screen | You built without `-p:CiBuild=true`. Rebuild with `driver.ps1 build`. |
| App dies instantly, `SIGABRT`, no .NET stack | APK is ~22 MB not ~150 MB — a plain `dotnet build` overwrote it. `driver.ps1 build`. |

For deeper on-device diagnostics (ANR / jank / Glide cascades / NAVTRACE timings),
switch to **`/ani-debug`** — it has the full log-collection and interpretation
workflow. `driver.ps1 logcat` and `driver.ps1 applog` are the quick versions.
