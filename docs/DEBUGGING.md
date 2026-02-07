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
