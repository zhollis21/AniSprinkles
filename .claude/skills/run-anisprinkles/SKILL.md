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
`CIAniListClient` / `CIAiringNotificationService` (`src/Services/CI/`), so the app
launches **already signed in** against hardcoded fixtures: no OAuth round-trip, no
real AniList traffic, no rate-limit budget spent, and a deterministic list every
run. A plain Debug build drops you on the signed-out screen and there is no
scriptable way past `WebAuthenticator`.

```bash
pwsh -NoProfile -File .claude/skills/run-anisprinkles/driver.ps1 build
```

Takes **~6 minutes** from cold (`Time Elapsed 00:05:53`) and produces a ~97 MB APK
at `src/bin/Debug/net10.0-android/com.RainbowSprinkles.AniSprinkles-Signed.apk`.
Skip it if you have not touched `src/` since the last build.

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
| `flyout` / `goto <page>` | open the drawer / open the drawer and pick `My Anime`\|`Discover`\|`Settings` |
| `search` | tap the Discover toolbar search icon (see Gotchas) |
| `logcat [n]` / `applog` | app-PID logcat tail / the on-device rotating file log |

`driver.ps1 help` prints the same list.

**The loop that works:** `dump` to see what is on screen → `tap`/`tap-desc` the
text you saw → `wait-for` the text you expect next → `shot` → `dump` again.
Never `shot` straight after a `tap` that navigates (see Gotchas).

### Worked example — verify the favorite toggle (this branch's feature)

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

- **My Anime**: `ONE PIECE`, `Shingeki no Kyojin`, `Jujutsu Kaisen`, `HUNTER×HUNTER (2011)`;
  section headers `Watching` / `Planning` / `Completed`
- **Media details** (ONE PIECE): `Monkey D. Luffy`, `Roronoa Zoro`, `Nami`,
  studios `Toei Animation` / `Madhouse` / `Studio Pierrot`; content-descs
  `Toggle favorite` and `Open studio Toei Animation`
- **Discover**: `Currently Airing`, `Trending Now`, `Top Anime`, `View All ›`
- **Search** filters that same fixture list client-side — `one` matches `ONE PIECE`;
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

- **`DiscoverPageModel` is a singleton, so the search bar keeps its text forever** —
  navigating away, backing out of the app entirely, and coming back still shows the
  old query and its results, not the section rows. If `wait-for "Trending Now"`
  times out on Discover, the search bar is open: `driver.ps1 search` toggles it
  shut and the sections come back.

- **MAUI Shell toolbar items are invisible to uiautomator.** Only the hamburger has
  a content-desc; the search / sort / layout icons have no node at all, so there is
  nothing to `tap-desc`. `driver.ps1 search` works around it by mirroring the
  hamburger's x across the screen width (`1080 - 74 = 1006`), which is
  resolution-independent.

- **`back` at a flyout root exits the app silently** — no confirm dialog, you just
  land on the launcher and every subsequent `tap` fails with a confusing
  "No node with text=...". `driver.ps1 resume` gets you back in ~3s. Prefer `goto`
  over `back` for switching pages.

- **`View All ›` carries a multibyte chevron in its text node.** Exact-match `tap`
  never finds it; use `tap-prefix "View All"`.

- **`KEYCODE_CTRL_A` is not select-all on Android.** `driver.ps1 clear` spams
  `KEYCODE_DEL` instead.

- **The debug APK is ~97 MB and installs land on `INSUFFICIENT_STORAGE`** once the
  AVD's `/data` gets tight (mine had 570 MB free and still failed). `driver.ps1
  install` detects it, uninstalls the old copy, and retries. Note that `adb install`
  prints `Failure [...]` and can still exit 0 — the driver parses the output text,
  so don't trust a bare `adb install`'s exit code.

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
| `'X' never appeared within Ns` on Discover | Search bar is still open with an old query — `driver.ps1 search`. |
| `df 'C:/Program Files/Git/data': No such file` | Git Bash path mangling — `MSYS_NO_PATHCONV=1`. |
| App runs but shows the signed-out screen | You built without `-p:CiBuild=true`. Rebuild with `driver.ps1 build`. |

For deeper on-device diagnostics (ANR / jank / Glide cascades / NAVTRACE timings),
switch to **`/ani-debug`** — it has the full log-collection and interpretation
workflow. `driver.ps1 logcat` and `driver.ps1 applog` are the quick versions.
