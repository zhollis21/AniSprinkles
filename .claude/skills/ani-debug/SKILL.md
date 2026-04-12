---
name: ani-debug
description: "Analyze AniSprinkles device logs for on-device issues: crashes, jank, ANRs, and navigation timing problems. Use only for issues observed on device or emulator — not for code logic investigation or GitHub issues."
argument-hint: "Describe the device-observed issue (crash, jank, ANR, or unexpected on-device behavior)"
context: fork
agent: Explore
allowed-tools: Bash(adb *)
---

# Debug

## Device State

Connected devices: !`adb devices -l`
App PID: !`adb -s emulator-5554 shell pidof com.RainbowSprinkles.AniSprinkles 2>/dev/null || echo "not running"`

Pull both logs before analyzing any issue.

## Step 1: Pull Logs

```powershell
# Pull device app log
adb -s emulator-5554 exec-out run-as com.RainbowSprinkles.AniSprinkles cat files/logs/anisprinkles.log > logs/anisprinkles.device.log

# Pull current-process logcat
$appPid = adb -s emulator-5554 shell pidof com.RainbowSprinkles.AniSprinkles
adb -s emulator-5554 logcat -v time --pid $appPid -d > logs/adb.device.pid.log
```

## Step 2: Scan for Crashes

```powershell
Select-String -Path logs/adb.device.pid.log -Pattern "FATAL EXCEPTION|AndroidRuntime|Unhandled exception|ObjectDisposedException|JavaProxyThrowable" -CaseSensitive:$false
```

## Step 3: Scan for Jank

```powershell
Select-String -Path logs/adb.device.pid.log -Pattern "Skipped [0-9]+ frames|Davey|GC freed" -CaseSensitive:$false
```

## Step 4: Scan for Navigation Timing

```powershell
Select-String -Path logs/anisprinkles.device.log -Pattern "NAVTRACE" -CaseSensitive:$false
```

## Additional Commands

```powershell
# List all app log files
adb -s emulator-5554 shell run-as com.RainbowSprinkles.AniSprinkles ls -la files/logs

# Read latest log directly on device
adb -s emulator-5554 shell run-as com.RainbowSprinkles.AniSprinkles cat files/logs/anisprinkles.log

# Filter logcat by tag
adb logcat AniSprinkles:D *:S
```

## Analysis Guidelines

- Correlate `Skipped`/`Davey!` hits with app operations (navigation, list bind, details load).
- Classify jank as CPU/UI-thread bound vs network-bound before proposing architecture changes.
- Validate findings on release-like builds, not only debugger-attached sessions.
- Visual Studio `View → Output (Debug)` and `View → Other Windows → Android Device Logcat` can supplement adb logs.
