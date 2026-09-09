---
name: run-anisprinkles
description: "Build, install, launch, and drive the AniSprinkles MAUI Android app on an emulator — tap, type, scroll, screenshot, and read the on-screen text. Use when asked to run or start the app, take a screenshot, reproduce a UI bug, or confirm a change actually works on device rather than only in tests."
allowed-tools: Bash(pwsh *) PowerShell Bash(adb *) Bash(dotnet *) Read Grep Glob
---

# Run AniSprinkles

Android-only .NET MAUI app. There is no desktop target and no web surface — the
only way to see a change is on an emulator or device.

Everything below is driven by **`.claude/skills/run-anisprinkles/driver.ps1`**, a
PowerShell harness that wraps `adb` + `uiautomator`. It resolves the correct
Android SDK, boots the AVD, installs the APK, and gives you `tap` / `type` /
`scroll-to` / `dump` / `shot` so you can drive the UI and read the screen as
text without looking at a single pixel.

Paths below are relative to the repo root.

---

## Prerequisites

Already present on this machine; listed so you can check them if something breaks.

- .NET 10 SDK (`dotnet --version` → `10.0.303`) with the `maui-android` workload
- Android SDK at `C:\Program Files (x86)\Android\android-sdk`
  (platform-tools 36.0.0, emulator 35.5.10, API 36 `google_apis_playstore` x86_64 image)
- An AVD — the driver defaults to `pixel_9_-_api_36`

Check all of it at once:

```bash
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 env
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

Takes **~6 minutes** from cold (`Time Elapsed 00:05:53`) and produces a ~97 MB APK
at `src/AniSprinkles/bin/Debug/net10.0-android/com.RainbowSprinkles.AniSprinkles-Signed.apk`.
Skip it if you have not touched `src/` since the last build.

**Build the Android head exactly one way: `driver.ps1 build`.** It is tempting to run a
plain `dotnet build src/AniSprinkles/AniSprinkles.csproj -c Debug -f net10.0-android` to check for
warnings and then `driver.ps1 build` for the APK — that wastes a second ~6-minute build
*and* leaves a broken APK, because the plain build omits `EmbedAssembliesIntoApk` and
overwrites the deployable ~97 MB APK with a ~19 MB Fast Deployment one (see Gotchas). The
driver's build prints the same warning and error counts, so it covers the review pass too.

`dotnet test` does **not** need any of this — the test project targets plain `net10.0`, so
run it directly and never build the Android head just to run tests.

**Do not chain a build with device driving in one shell call.** `build` alone can run 6+
minutes; adding `install`/`launch`/`tap` behind it means one stalled step burns the whole
timeout and takes the completed steps down with it. Build in its own call (background it if
you have other work), then drive in a second.

---

## Run (agent path)

One-shot — boots the emulator if needed, installs, launches:

```bash
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 up
```

Then drive it. Every command is `driver.ps1 <command> [args]`:

| | |
|---|---|
| `env` | resolved SDK / adb / APK / device state |
| `boot [avd]` | start emulator, block until `sys.boot_completed`, kill animations |
| `build` / `install` / `launch` | individually |
| `resume` | re-foreground the app **without** restarting it (warm, ~3s) |
| `stop` / `kill-emu` | force-stop the app / shut the AVD down |
| `shot <name>` | PNG → `tmp/driver-screens/<name>.png` |
| `dump [filter]` | **print every visible text/content-desc node** — read the screen as text |
| `xml [path]` | raw uiautomator XML when `dump` is not enough |
| `tap <text>` | tap a node by exact visible text |
| `tap-prefix <text>` | tap by text prefix — needed for `"View All ›"` (multibyte chevron) |
| `tap-desc <desc>` | tap by content-desc — the icon buttons (`Toggle favorite`, `Open studio X`) |
| `longpress <text>` | opens the quick-actions sheet on any anime card |
| `type <text>` / `clear [n]` | type into / empty the focused field |
| `key <KEYCODE>` / `back` | `KEYCODE_` is prefixed for you |
| `swipe up\|down\|left\|right` | one content-area swipe |
| `scroll-to <text>` | swipe up until `<text>` is on screen (max 10) |
| `wait-for <text> [secs]` | poll until `<text>` appears (default 30s) |
| `goto <tab>` | switch tabs: `Library`\|`Discover`\|`Search`\|`Feed`\|`Settings` — a plain label tap, so it works from inside a pushed details stack too |
| `search` | shortcut for `goto Search` |
| `logcat [n]` / `applog` | app-PID logcat tail / the on-device rotating file log |
| `fault <op> <kind> [scope]` | arm a fault on the **running** app — no rebuild (#125) |
| `fault clear` | disarm |
| `seed-token [test\|real]` | sign a **real-auth** Debug build in over adb, no OAuth tapping (#160) |
| `dump-token` | read the signed-in token back out to `tmp/dev-token.txt` (gitignored) |
| `notify [media] [ep] [title]` | post a **real** airing notification (#111) — see below |
| `deeplink <media> [nonce]` | fire the notification's deep-link intent without a notification |

`driver.ps1 help` prints the same list.

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

**The loop that works:** `dump` to see what is on screen → `tap`/`tap-desc` the
text you saw → `wait-for` the text you expect next → `shot` → `dump` again.
Never `shot` straight after a `tap` that navigates (see Gotchas).

### Worked example — a full round trip

Every line below was run against the emulator; the output is verbatim.

```bash
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 up
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 wait-for "ONE PIECE" 60
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 tap "ONE PIECE"
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 wait-for "Toggle favorite" 45
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 shot before
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 tap-desc "Toggle favorite"
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 shot after
```

```
[driver] tap text='ONE PIECE' at (286,1000)
[driver] 'Toggle favorite' after 2s
[driver] tap content-desc='Toggle favorite' at (960,379)
[driver] C:\...\tmp\driver-screens\after.png (1192 KB)
```

Then `Read` the two PNGs: heart outline → filled, Favourites `90,457` → `90,458`.

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

**uiautomator cannot see the notification shade**, so `dump` and `tap` are useless there. Expand it
with `adb shell cmd statusbar expand-notifications`, `shot` it, and tap by coordinate off the image.

To force the four arrival states, and the replay case that needs a task restore rather than a plain
relaunch:

| State | Setup |
|---|---|
| Process dead | `adb shell am force-stop <pkg>` first |
| Process alive, activity destroyed | `adb shell settings put global always_finish_activities 1`, HOME, then fire — **set it back to 0 afterwards** |
| Backgrounded | `driver.ps1 key KEYCODE_HOME`, then fire |
| Foreground | fire while it is on screen |
| Task restore replay | `adb shell am kill <pkg>` — **not** `force-stop`, which drops the task — then reopen from recents |

### Fixture data you can drive against

The recorded fixtures (#134) are a real AniList account, so these strings are tappable. They
change whenever the fixtures are re-recorded — if a `tap` fails, `dump` the screen rather than
trusting this list, and check `src/AniSprinkles/Fixtures/AniList/MediaListCollection__*.json`
for what the Library actually holds.

- **Library › Anime** (nav title reads "Library"; sub-tabs are `ANIME` / `MANGA`), 20 titles
  across five sections — `Watching`: `ONE PIECE`, `Gintama.`,
  `Re:Zero kara Hajimeru Isekai Seikatsu 4th Season`; `Completed`: `Sousou no Frieren`,
  `Goblin Slayer`; `Planning`, `Dropped` (`Witch Watch`, `Steins;Gate 0`) and `Paused`
  (`SAKAMOTO DAYS`) are populated too — every status has entries, deliberately.
- **Library › Manga**: `Reading` has `Shingeki no Kyojin` and `ONE PIECE`; `Completed` has
  `Chainsaw Man`.
- **Media details** (ONE PIECE): `Luffy D. Monkey`, `Zoro Roronoa` — family-name-first, because
  names render from `userPreferred` resolved against the account's Staff Name Language (#130).
  Content-descs `Toggle favorite` and `Open studio <name>`.
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
works — that is what the driver above is for.

---

## Gotchas

- **Two Android SDKs are installed and PATH points at the wrong one.**
  `%LOCALAPPDATA%\Android\Sdk` (adb 34, no API 36 system image) shadows
  `C:\Program Files (x86)\Android\android-sdk` (adb 36, has the images). Launching
  the emulator from the PATH copy dies with `PANIC: Cannot find AVD system path`,
  and mixing the two adb binaries makes the daemon restart mid-session. The driver
  picks the root that actually has `system-images/` — **don't call bare `adb`**,
  go through the driver or use the full `Program Files (x86)` path.

- **Git Bash rewrites device paths.** `adb shell df /data` from Bash becomes
  `df 'C:/Program Files/Git/data'`. Prefix with `MSYS_NO_PATHCONV=1`, or just use
  the driver (PowerShell, unaffected). This silently returns empty output rather
  than erroring, which is how it burns you.

- **Never pipe `adb exec-out screencap -p` into a PowerShell redirect** — the PNG
  comes out corrupt (text encoding + CRLF). `driver.ps1 shot` writes on-device and
  `adb pull`s it back, which is byte-exact.

- **`adb shell` mangles output you intend to parse** (LF→CRLF). The driver uses
  `exec-out` everywhere it reads.

- **`shot` immediately after a navigating `tap` captures the loading spinner.**
  Shell transitions plus the details fetch run well past the driver's 1.2s
  post-tap settle. Always `wait-for "<text you expect>"` between them. I caught
  `Loading discover...` and a stale details page this way twice.

- **`SearchPageModel` is a singleton, so the Search tab keeps its query forever** —
  leaving the tab, backing out of the app entirely, and coming back still shows the
  old query and its results. That is deliberate, not a bug. Clear the field (or the
  `clear` command) if a test needs the idle prompt back. Discover no longer has a
  search bar at all, so a `wait-for "Trending Now"` timeout there means something
  else went wrong.

- **The Search tab's keyboard covers the bottom tab bar.** The field auto-focuses
  when the query is empty, so arriving on the tab immediately raises the keyboard and
  hides every tab label — a `goto` straight after typing taps the keyboard instead and
  silently stays put. Send `back` first to dismiss it, then switch tabs.

- **MAUI Shell toolbar items are invisible to uiautomator, and there is no longer an
  anchor to mirror.** The page-level search / sort / layout icons have no node at
  all. This used to be worked around by mirroring the hamburger's x across the
  screen width, but the bottom tab bar (issue #43) removed the hamburger, so that
  trick has no anchor left. Tab **labels** are real nodes, so navigation is fine —
  it is only the in-page toolbar icons (the Library list’s sort / search / layout) that now
  need hand-computed coordinates: they sit in the nav-bar row, right-aligned, about
  `74px` in from each edge at 1080 wide.

- **`back` at a tab root exits the app silently** — no confirm dialog, you just
  land on the launcher and every subsequent `tap` fails with a confusing
  "No node with text=...". `driver.ps1 resume` gets you back in ~3s. Prefer `goto`
  over `back` for switching tabs.

- **`View All ›` carries a multibyte chevron in its text node.** Exact-match `tap`
  never finds it; use `tap-prefix "View All"`.

- **`KEYCODE_CTRL_A` is not select-all on Android.** `driver.ps1 clear` spams
  `KEYCODE_DEL` instead.

- **A plain `dotnet build` silently clobbers the deployable APK.** Running
  `dotnet build src/AniSprinkles/AniSprinkles.csproj -c Debug -f net10.0-android` to check for
  warnings is fine, but it omits `-p:EmbedAssembliesIntoApk=true` and leaves a
  ~19 MB *Fast Deployment* APK where the ~97 MB one was. Installing that gives a
  process that dies instantly, before any managed code, with no .NET stack trace —
  just `SIGABRT` and, buried in logcat:

  ```
  F/monodroid: No assemblies found in '.../files/.__override__/x86_64'
               Assuming this is part of Fast Deployment. Exiting...
  ```

  It reads like a crash in your change and is not one. **Check the APK size** — if
  it is ~19 MB rather than ~97 MB, that is the whole story. Rebuild with
  `driver.ps1 build` (which passes both flags) before installing. A leftover
  `.__override__` directory can also survive an uninstall, so if it persists after
  a correct rebuild, uninstall the package before reinstalling.

- **The debug APK is ~97 MB and installs land on `INSUFFICIENT_STORAGE`** once the
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
  UI has content. `wait-for` is what tells you the page actually rendered.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `PANIC: Cannot find AVD system path. Please define ANDROID_SDK_ROOT` | You launched the PATH emulator. Use `driver.ps1 boot`. |
| `adb server version doesn't match this client` | Two adb versions fighting. `adb kill-server`, then let the driver restart the right one. |
| `INSUFFICIENT_STORAGE: Failed to override installation location` | `driver.ps1 install` handles it; if it still fails, wipe the AVD from Device Manager. |
| `UI dump was empty — is the app foregrounded?` | The app is backgrounded or crashed. `driver.ps1 resume`, then `driver.ps1 logcat`. |
| `No node with text='X'` right after it worked | You probably backed out to the launcher. `driver.ps1 dump` to confirm, then `resume`. |
| `'X' never appeared within Ns` right after typing a query | The keyboard is covering the tab bar — `driver.ps1 back`, then `goto`. |
| `More than one device attached — set ANDROID_SERIAL to choose` | Two emulators/devices online. Pick one with `ANDROID_SERIAL=<serial>`, or shut the other down. `driver.ps1 env` lists them. |
| `sys.boot_completed never reached 1 after 180s` | The emulator attached to adb but never finished booting. Check the emulator window; a wipe-data from Device Manager usually clears it. |
| `AVD <name> never attached to adb`, and `emulator`/`qemu-system-x86_64` are running but idle (well under 1s CPU, no port in the 5554-5600 range listening) | A stale AVD lock. qemu starts, finds the AVD claimed, and hangs instead of exiting — so each retry silently adds another hung pair. Kill every `emulator`/`qemu*` process, then delete `~/.android/avd/<name>.avd/*.lock` (`hardware-qemu.ini.lock`, `multiinstance.lock`) and boot again. Run `emulator -verbose` to confirm: it prints `Running multiple emulators with the same AVD`. Check acceleration first with `emulator -accel-check` to rule out WHPX. |
| `df 'C:/Program Files/Git/data': No such file` | Git Bash path mangling — `MSYS_NO_PATHCONV=1`. |
| App runs but shows the signed-out screen | You built without `-p:CiBuild=true`. Rebuild with `driver.ps1 build`. |
| App dies instantly, `SIGABRT`, no .NET stack | APK is ~19 MB not ~97 MB — a plain `dotnet build` overwrote it. `driver.ps1 build`. |

For deeper on-device diagnostics (ANR / jank / Glide cascades / NAVTRACE timings),
switch to **`/ani-debug`** — it has the full log-collection and interpretation
workflow. `driver.ps1 logcat` and `driver.ps1 applog` are the quick versions.
