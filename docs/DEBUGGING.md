# Debugging (Android, VS 2026)

Quick Start
- Use Visual Studio Output and Android Device Log windows while debugging.
- Keep a terminal running `adb logcat` as a backup in case the debugger detaches.
- When troubleshooting UI slowness or crashes, always capture logcat around the repro and scan for `Skipped frames`, `Davey`, and GC spikes.

Visual Studio Tips
- Open `View` -> `Output` and select `Debug` or `Android Device Log`.
- Open `View` -> `Other Windows` -> `Android Device Logcat`.
- Filter by app name or keywords like `AniSprinkles`, `mono`, or `dotnet`.

Logcat Tips
- Filter by tag and level: `adb logcat AniSprinkles:D *:S`
- Use `adb logcat` to capture crashes even if VS disconnects.
- For UI performance issues: `adb logcat -v time -d -s me.anisprinkles Choreographer HWUI`

Logging Guidance
- Prefer `ILogger` with structured messages over raw strings.
- Keep tokens and PII out of logs (redact `Authorization: Bearer`).
- Use `#if DEBUG` to guard verbose logs.

File Logs (Debug Builds)
- App writes rotating logs to `files/logs/anisprinkles.log` (plus `.1`, `.2`, etc.) inside Android app storage.
- Read latest log:
  `adb -s emulator-5554 shell run-as com.companyname.anisprinkles cat files/logs/anisprinkles.log`
- List all log files:
  `adb -s emulator-5554 shell run-as com.companyname.anisprinkles ls -la files/logs`
- Pull log to local machine:
  `adb -s emulator-5554 exec-out run-as com.companyname.anisprinkles cat files/logs/anisprinkles.log > anisprinkles.log`
- Pull log into repo workspace for quick sharing/review:
  `adb -s emulator-5554 exec-out run-as com.companyname.anisprinkles cat files/logs/anisprinkles.log > logs/anisprinkles.device.log`

Confirmation Pass (Issue Checks)
- Pull app file logs:
  `adb -s emulator-5554 exec-out run-as com.companyname.anisprinkles cat files/logs/anisprinkles.log > logs/anisprinkles.device.log`
- Pull current app-process logcat:
  `$appPid = adb -s emulator-5554 shell pidof com.companyname.anisprinkles`
  `adb -s emulator-5554 logcat -v time --pid $appPid -d > logs/adb.device.pid.log`
- Scan quickly for hard failures:
  `Select-String -Path logs/adb.device.pid.log -Pattern "FATAL EXCEPTION|AndroidRuntime|Unhandled exception|ObjectDisposedException|JavaProxyThrowable" -CaseSensitive:$false`
- Scan quickly for perf/jank warnings:
  `Select-String -Path logs/adb.device.pid.log -Pattern "Skipped [0-9]+ frames|Davey|GC freed" -CaseSensitive:$false`
