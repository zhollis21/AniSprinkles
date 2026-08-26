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
`CIAniListClient` / `CIAiringNotificationService` (`src/AniSprinkles/Services/CI/`), so the app
launches **already signed in** against hardcoded fixtures: no OAuth round-trip, no
real AniList traffic, no rate-limit budget spent, and a deterministic list every
run. A plain Debug build drops you on the signed-out screen and there is no
scriptable way past `WebAuthenticator`.

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

`driver.ps1 help` prints the same list.

### Driving error states — `fault`

Error and retry states used to be unreachable on device: the old `-p:ErrorSim=true` build failed
*every* call, so My Anime and Discover died before you could navigate to the page you wanted to
break. `fault` decorates the client instead of replacing it, so it composes with the CI fixtures —
a real screen loads, then the next call fails.

```powershell
driver.ps1 fault GetMedia ServiceOutage next   # details page → error; Try Again then SUCCEEDS
driver.ps1 fault GetStudio NotFound always     # studio page stays broken until cleared
driver.ps1 fault any delay next -delay 4000    # 4s latency, no failure — opens timing windows
driver.ps1 fault clear
```

- `op` — an `IAniListClient` method prefix (`GetStudio` matches `GetStudioAsync`), or `any`
- `kind` — `ServiceOutage` | `Network` | `Authentication` | `RateLimited` | `NotFound` | `Unknown`,
  or `delay` for latency without failure
- `scope` — `next` (default) | `always` | `firstn:N` | `everynth:N`, all deterministic

**`scope next` is the one that proves recovery**, because the fault is spent on the first call and
**Try Again** genuinely succeeds. That path — on all four details pages, plus `FavouriteToggleRunner`
and `ListOperationRunner`'s rollback snackbars — had never run on a device before this existed.

**`-delay` with no kind is how you reproduce lifecycle and debounce bugs.** The CI stubs return
instantly, so cancellation and `IsBusy`/`CanLoadMore` windows are zero-width on device; a delay is
what opens them. Arm one, navigate away mid-load, and watch whether `LoggingHandler`'s `HTTP POST` /
`HTTP 200` lines stop (#132).

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

### Fixture data you can drive against

`CIAniListClient` serves a fixed set, so these strings are always tappable:

- **Library › Anime** (nav title reads "Library"; sub-tabs are `ANIME` / `MANGA`): `ONE PIECE`, `Shingeki no Kyojin`, `Jujutsu Kaisen`, `HUNTER×HUNTER (2011)`;
  section headers `Watching` / `Planning` / `Completed`
- **Media details** (ONE PIECE): `Monkey D. Luffy`, `Roronoa Zoro`, `Nami`,
  studios `Toei Animation` / `Madhouse` / `Studio Pierrot`; content-descs
  `Toggle favorite` and `Open studio Toei Animation`
- **Discover**: `Currently Airing`, `Trending Now`, `Top Anime`, `View All ›`
- **Search tab** filters that same fixture list client-side — `one` matches `ONE PIECE`;
  anything else (e.g. `cowboy bebop`) correctly renders `No anime found`. That is
  the stub, not a bug.

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
  it is only the in-page toolbar icons (My Anime's sort / search / layout) that now
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
