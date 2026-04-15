#!/usr/bin/env bash
# One-shot AniSprinkles device diagnostic collector.
# Emits a compact report to stdout. Leaves raw files in $OUTDIR for follow-up.
# Safe to run when no device / app isn't running — degrades gracefully.

set -u

PKG="com.RainbowSprinkles.AniSprinkles"
OUTDIR="/tmp/anidebug"
mkdir -p "$OUTDIR"

if ! command -v adb >/dev/null 2>&1; then
  echo "adb not found on PATH. Install Android platform-tools (or add it to PATH) and retry."
  exit 0
fi
APPLOG="$OUTDIR/anisprinkles.log"
APPLOG_PREV="$OUTDIR/anisprinkles.log.1"
LOGCAT="$OUTDIR/logcat.txt"
# ANR events are logged by system_server (not the app's PID), so a PID-filtered
# logcat drops them. Keep a separate slice scoped to package-name mentions.
ANRLOG="$OUTDIR/logcat.anr.txt"

echo "## Device"
DEVICES=$(adb devices 2>/dev/null | awk 'NR>1 && $2=="device" {print $1}')
if [ -z "$DEVICES" ]; then
  echo "No adb devices in 'device' state. Start an emulator or connect a device and retry."
  exit 0
fi
for d in $DEVICES; do echo "- $d"; done
# With >1 device, unqualified adb commands fail. Respect a caller-supplied
# ANDROID_SERIAL when present; otherwise pin to the first detected device.
DEVICE_COUNT=$(printf '%s\n' "$DEVICES" | wc -l | tr -d ' ')
if [ -n "${ANDROID_SERIAL:-}" ]; then
  if ! printf '%s\n' "$DEVICES" | grep -Fxq "$ANDROID_SERIAL"; then
    echo "ANDROID_SERIAL '$ANDROID_SERIAL' is not in the detected device list."
    exit 1
  fi
  if [ "$DEVICE_COUNT" -gt 1 ]; then
    echo "(multiple devices; using caller-supplied ANDROID_SERIAL=$ANDROID_SERIAL)"
  fi
else
  export ANDROID_SERIAL="$(printf '%s\n' "$DEVICES" | head -n1)"
  if [ "$DEVICE_COUNT" -gt 1 ]; then
    echo "(multiple devices; pinned to $ANDROID_SERIAL — override with ANDROID_SERIAL=...)"
  fi
fi

PID=$(adb shell pidof "$PKG" 2>/dev/null | tr -d '\r' | awk '{print $1}')
if [ -z "$PID" ]; then
  echo "App PID: (not running)"
else
  echo "App PID: $PID"
fi
echo "Output dir: $OUTDIR"
echo

# App log (persists across process restarts; contains PageState/NAVTRACE/Errors).
if adb exec-out run-as "$PKG" cat files/logs/anisprinkles.log > "$APPLOG" 2>/dev/null && [ -s "$APPLOG" ]; then
  APP_LINES=$(wc -l < "$APPLOG" | tr -d ' ')
  echo "App log: $APPLOG ($APP_LINES lines)"
  # Pull the rotated predecessor too — useful when the issue straddled a restart.
  adb exec-out run-as "$PKG" cat files/logs/anisprinkles.log.1 > "$APPLOG_PREV" 2>/dev/null || true
else
  echo "App log: (unavailable — app may not be debuggable or logs dir is empty)"
  : > "$APPLOG"
fi

# Logcat — filtered to app PID when possible. -d dumps and exits.
if [ -n "$PID" ]; then
  adb logcat -d -v time --pid "$PID" > "$LOGCAT" 2>/dev/null
  LOGCAT_MODE="PID-filtered"
else
  adb logcat -d -v time > "$LOGCAT" 2>/dev/null
  LOGCAT_MODE="unfiltered (app not running)"
fi
LC_LINES=$(wc -l < "$LOGCAT" | tr -d ' ')
echo "Logcat: $LOGCAT ($LC_LINES lines, $LOGCAT_MODE)"

# Capture ANR-related cross-PID lines (system_server logs these).
adb logcat -d -v time 2>/dev/null \
  | grep -E "RainbowSprinkles\\.AniSprinkles|Waited [0-9]+ms for|ANR in |Input dispatching timed out" \
  > "$ANRLOG" 2>/dev/null || true
ANR_LINES=$(wc -l < "$ANRLOG" | tr -d ' ')
echo "ANR slice: $ANRLOG ($ANR_LINES lines)"
echo

count_and_sample() {
  local label="$1" file="$2" pattern="$3" n="${4:-3}"
  if [ ! -s "$file" ]; then
    echo "- $label: 0 (no log)"
    return
  fi
  # grep -c always prints a number on stdout; exit status is nonzero on 0 matches.
  local c
  c=$(grep -cE "$pattern" "$file" 2>/dev/null) || true
  c=${c:-0}
  echo "- $label: $c"
  if [ "$c" -gt 0 ]; then
    grep -nE "$pattern" "$file" 2>/dev/null | tail -n "$n" | sed 's/^/    /'
  fi
}

section_or_none() {
  local label="$1" file="$2" pattern="$3" n="${4:-10}"
  echo "## $label (last $n)"
  if [ ! -s "$file" ]; then
    echo "    (no log)"
    return
  fi
  local out
  out=$(grep -E "$pattern" "$file" 2>/dev/null | tail -n "$n")
  if [ -z "$out" ]; then
    echo "    (none)"
  else
    printf '%s\n' "$out" | sed 's/^/    /'
  fi
}

echo "## Signals — logcat"
count_and_sample "ANR / input dispatch timeouts (cross-PID)" "$ANRLOG" "ANR in |not responding|Input dispatching timed out" 3
count_and_sample "FATAL / AndroidRuntime crashes" "$LOGCAT" "FATAL EXCEPTION|AndroidRuntime: Process" 3
count_and_sample "ObjectDisposedException" "$LOGCAT" "ObjectDisposedException" 3
count_and_sample "JavaProxyThrowable" "$LOGCAT" "JavaProxyThrowable" 3
count_and_sample "Skipped frames / Davey" "$LOGCAT" "Skipped [0-9]+ frames|Davey!" 3

echo
echo "## Signals — app log"
count_and_sample "Glide: destroyed activity (MAUI RecyclerView symptom)" "$APPLOG" "destroyed activity" 3
count_and_sample "ObjectDisposedException" "$APPLOG" "ObjectDisposedException" 3
count_and_sample "[Error] entries" "$APPLOG" "\\[Error\\]" 5
count_and_sample "[Warning] entries (sample)" "$APPLOG" "\\[Warning\\]" 3

echo
section_or_none "Recent PageState transitions" "$APPLOG" "PageState: " 10
echo
section_or_none "Recent NAVTRACE entries" "$APPLOG" "NAVTRACE" 10
echo
echo "## Raw files"
echo "- App log:      $APPLOG"
echo "- Logcat:       $LOGCAT ($LOGCAT_MODE)"
echo "- ANR slice:    $ANRLOG"
echo "- Prev app log: $APPLOG_PREV (if rotated)"
