<#
.SYNOPSIS
  Build / launch / drive AniSprinkles on an Android emulator from the command line.

.DESCRIPTION
  Agent-facing harness for the MAUI Android app. Every command is a subcommand:

      pwsh .claude/skills/run-anisprinkles/driver.ps1 <command> [args]

  Run `driver.ps1 help` for the full command list.

  Written for Windows + PowerShell 7. It pins itself to the Android SDK that
  actually has the AVD system images (see Resolve-Sdk) so it does not pick up a
  stale adb from PATH.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Command = 'help',

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$Args
)

$ErrorActionPreference = 'Stop'

$Package  = 'com.RainbowSprinkles.AniSprinkles'
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$ShotDir  = Join-Path $RepoRoot 'tmp\driver-screens'
$Apk      = Join-Path $RepoRoot 'src\AniSprinkles\bin\Debug\net10.0-android\com.RainbowSprinkles.AniSprinkles-Signed.apk'

# ---------------------------------------------------------------- SDK plumbing

function Resolve-Sdk {
    # This machine has TWO Android SDK roots:
    #   C:\Program Files (x86)\Android\android-sdk   (VS/Xamarin — has the API 36 images, adb 36)
    #   %LOCALAPPDATA%\Android\Sdk                   (partial Android Studio — adb 34, no API 36 image)
    # PATH points at the second one, whose emulator PANICs with
    # "Cannot find AVD system path". Pick the root that actually has system images.
    $candidates = @()
    if ($env:ANDROID_SDK_ROOT) { $candidates += $env:ANDROID_SDK_ROOT }
    if ($env:ANDROID_HOME)     { $candidates += $env:ANDROID_HOME }
    $candidates += "${env:ProgramFiles(x86)}\Android\android-sdk"
    $candidates += "$env:LOCALAPPDATA\Android\Sdk"

    foreach ($c in $candidates) {
        if (-not $c) { continue }
        $adb = Join-Path $c 'platform-tools\adb.exe'
        $emu = Join-Path $c 'emulator\emulator.exe'
        $img = Join-Path $c 'system-images'
        if ((Test-Path $adb) -and (Test-Path $emu) -and
            (Test-Path $img) -and (Get-ChildItem $img -Directory -EA SilentlyContinue)) {
            return $c
        }
    }
    throw "No Android SDK root with emulator + system-images found. Tried:`n  $($candidates -join "`n  ")"
}

$Sdk = Resolve-Sdk
$Adb = Join-Path $Sdk 'platform-tools\adb.exe'
$Emu = Join-Path $Sdk 'emulator\emulator.exe'

# ---------------------------------------------------------------- device target
# Every adb call routes through Adb/AdbOut so a single `-s <serial>` is applied
# consistently. Without it a second attached device (a phone plugged in, a
# leftover AVD) makes every command die with "more than one device/emulator",
# which reads like a driver bug rather than an ambiguity.
$script:Serial = $env:ANDROID_SERIAL

function Adb {
    if ($script:Serial) { & $Adb -s $script:Serial @args } else { & $Adb @args }
}

# `adb shell` translates LF -> CRLF and mangles binary. `exec-out` does not.
# Always use this for anything you intend to parse or write to a file.
function AdbOut {
    if ($script:Serial) { & $Adb -s $script:Serial exec-out @args } else { & $Adb exec-out @args }
}

# Free 1K-blocks on /data, or $null if the output could not be parsed. `df` prints a header
# plus one row; column 4 is "Available". Returns $null rather than throwing so callers can
# treat "unknown" as "proceed and let the install tell us".
function FreeDataKb {
    $row = @(AdbOut df /data 2>$null) | Where-Object { $_ -match '^\S+\s+\d+' } | Select-Object -Last 1
    if (-not $row) { return $null }
    $cols = @(($row -split '\s+') | Where-Object { $_ })
    if ($cols.Count -ge 4 -and $cols[3] -match '^\d+$') { return [int64]$cols[3] }
    return $null
}

function Say($msg) { Write-Host "[driver] $msg" }

function Get-AttachedDevices {
    # Only devices in `device` state — `offline` and `unauthorized` cannot be driven.
    @(& $Adb devices | ForEach-Object { if ($_ -match '^(\S+)\s+device\s*$') { $Matches[1] } })
}

function Get-AvdName {
    param([string]$DeviceSerial)
    # `adb -s <serial> emu avd name` prints the AVD name, then a line reading OK.
    # Physical devices have no console and just error, so treat failure as "no AVD".
    $out = & $Adb -s $DeviceSerial emu avd name 2>$null
    if (-not $out) { return $null }
    ($out | Where-Object { $_ -and $_.Trim() -and $_.Trim() -ne 'OK' } | Select-Object -First 1).Trim()
}

function Resolve-Target {
    # Pick the device every later command will talk to. Explicit ANDROID_SERIAL wins;
    # otherwise a lone device is unambiguous, and anything else needs the user to say.
    param([switch]$Required)
    if ($script:Serial) { return $script:Serial }
    # @() is required at the CALL site, not just inside the function: PowerShell
    # unrolls a one-element array on return, so without it $devices is a bare
    # string and $devices[0] silently yields its first character.
    $devices = @(Get-AttachedDevices)
    if ($devices.Count -eq 1) { $script:Serial = $devices[0]; return $script:Serial }
    if ($devices.Count -gt 1) {
        $detail = ($devices | ForEach-Object { "  $_  (avd: $(Get-AvdName $_))" }) -join "`n"
        throw "More than one device attached — set ANDROID_SERIAL to choose:`n$detail"
    }
    if ($Required) { throw 'No device attached. Run: driver.ps1 boot' }
    return $null
}

function Wait-BootCompleted {
    param([int]$Seconds = 180)
    for ($i = 0; $i -lt $Seconds; $i++) {
        $b = (AdbOut getprop sys.boot_completed) -join '' -replace '\s', ''
        if ($b -eq '1') { Say "boot complete after ${i}s"; return }
        Start-Sleep -Seconds 1
    }
    # Previously this loop just fell through, so a device that never finished
    # booting was reported as ready and the next command failed confusingly.
    throw "sys.boot_completed never reached 1 after ${Seconds}s"
}

# ------------------------------------------------------------------- UI probing

function Get-UiDump {
    # uiautomator writes to the device, then we stream it back with exec-out.
    # Do NOT try `uiautomator dump /dev/tty` — it interleaves the tool's own
    # "UI hierarchy dumped to:" status line into the XML.
    Adb shell rm -f /sdcard/ui.xml *> $null
    Adb shell uiautomator dump /sdcard/ui.xml *> $null
    $xml = AdbOut cat /sdcard/ui.xml
    if (-not $xml) { throw 'UI dump was empty — is the app foregrounded?' }
    return ($xml -join '')
}

function Find-Bounds {
    param([string]$Xml, [string]$Attr, [string]$Value, [switch]$Prefix)

    # Split on '>' so each node is its own line; otherwise a greedy match can
    # pick up the bounds of a LATER node than the one whose text matched.
    $nodes = $Xml -split '>'
    $esc = [regex]::Escape($Value)
    $pattern = if ($Prefix) { "$Attr=`"$esc[^`"]*`"" } else { "$Attr=`"$esc`"" }

    foreach ($n in $nodes) {
        if ($n -match $pattern -and $n -match 'bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"') {
            return [pscustomobject]@{
                L = [int]$Matches[1]; T = [int]$Matches[2]
                R = [int]$Matches[3]; B = [int]$Matches[4]
                CX = [int](([int]$Matches[1] + [int]$Matches[3]) / 2)
                CY = [int](([int]$Matches[2] + [int]$Matches[4]) / 2)
            }
        }
    }
    return $null
}

function Tap-By {
    param([string]$Attr, [string]$Value, [switch]$Long, [switch]$Prefix)
    # An empty value would happily match the app's many empty text="" nodes and tap
    # something arbitrary — fail loudly instead.
    if (-not $Value) { throw "no $Attr given, e.g. driver.ps1 tap `"ONE PIECE`"" }
    $b = Find-Bounds -Xml (Get-UiDump) -Attr $Attr -Value $Value -Prefix:$Prefix
    if (-not $b) { throw "No node with $Attr='$Value'" }
    if ($Long) {
        Say "long-press $Attr='$Value' at ($($b.CX),$($b.CY))"
        Adb shell input swipe $b.CX $b.CY $b.CX $b.CY 800 *> $null
    } else {
        Say "tap $Attr='$Value' at ($($b.CX),$($b.CY))"
        Adb shell input tap $b.CX $b.CY *> $null
    }
    Start-Sleep -Milliseconds 1200
}

function Get-ScreenSize {
    $s = (AdbOut wm size) -join ''
    if ($s -match '(\d+)x(\d+)') { return @([int]$Matches[1], [int]$Matches[2]) }
    throw "Could not read screen size from '$s'"
}

# ---------------------------------------------------------------- commands

function Cmd-Help {
@"
AniSprinkles driver — pwsh .claude/skills/run-anisprinkles/driver.ps1 <command>

  env                       Show resolved SDK / adb / device state
  boot [avd]                Start the emulator and block until boot completes
                            (default AVD: pixel_9_-_api_36)
  build                     dotnet build -p:CiBuild=true (stub auth + stub API data)
  install                   adb install -r -d the signed debug APK
  launch                    Force-restart the app (cold, ~20s) and wait for its process
  resume                    Bring the app back to the foreground without restarting
                            (use after a stray `back` drops you to the launcher)
  up [avd]                  boot + install + launch
  stop                      Force-stop the app
  kill-emu                  Shut the emulator down

  shot <name>               Screenshot -> tmp/driver-screens/<name>.png
  dump [filter]             Print visible text/content-desc nodes (optionally grepped)
  xml [path]                Save the raw uiautomator XML (default tmp/driver-screens/ui.xml)

  tap <text>                Tap a node by exact visible text
  tap-prefix <text>         Tap a node whose text STARTS WITH <text>   (e.g. "View All")
  tap-desc <desc>           Tap a node by content-desc (icon buttons)
  longpress <text>          Long-press a node by visible text (opens quick-actions)
  type <text>               Type into the focused field
  clear [n]                 Delete n chars (default 60) from the focused field
  key <KEYCODE>             e.g. BACK, ENTER, HOME
  back                      KEYCODE_BACK
  swipe up|down|left|right  One content-area swipe
  scroll-to <text>          Swipe up until <text> appears (max 10)
  wait-for <text> [secs]    Poll the UI until <text> appears (default 30s)
  goto <tab>                Switch tabs: Library|Discover|Search|Feed|Settings
  search                    Shortcut for `goto Search`

  logcat [lines]            App-PID logcat tail (default 200)
  applog                    Pull the on-device rotating file log
"@ | Write-Host
}

function Cmd-Env {
    Say "SDK      : $Sdk"
    Say "adb      : $Adb  ($(((& $Adb version) | Select-Object -Index 1)))"
    Say "emulator : $Emu"
    Say "repo     : $RepoRoot"
    Say "apk      : $Apk  (exists: $(Test-Path $Apk))"
    Say 'devices  :'
    foreach ($d in Get-AttachedDevices) { Say "  $d  (avd: $(Get-AvdName $d))" }
    # Deliberately non-fatal: `env` is what you run to diagnose "no device", so it
    # must report the situation rather than throw on it.
    $t = try { Resolve-Target } catch { $null }
    Say "target   : $(if ($t) { $t } else { '(none resolved)' })"
}

function Cmd-Boot {
    param([string]$Avd = 'pixel_9_-_api_36')

    # Reuse an emulator only if it is the AVD that was actually asked for. Matching
    # on "any emulator is online" meant `boot <other-avd>` silently did nothing and
    # every subsequent command drove the wrong device.
    $existing = Get-AttachedDevices | Where-Object { (Get-AvdName $_) -eq $Avd } | Select-Object -First 1
    if ($existing) {
        $script:Serial = $existing
        Say "AVD $Avd already online at $existing"
    } else {
        $others = @(Get-AttachedDevices)
        if ($others) { Say "note: $($others.Count) other device(s) attached; targeting $Avd once it boots" }

        Say "starting AVD $Avd"
        # ANDROID_SDK_ROOT must match the emulator we picked, or it re-searches PATH's SDK.
        $env:ANDROID_SDK_ROOT = $Sdk
        $env:ANDROID_HOME = $Sdk
        Start-Process -FilePath $Emu `
            -ArgumentList @('-avd', $Avd, '-no-boot-anim', '-no-snapshot-save') `
            -WindowStyle Minimized

        Say 'waiting for the new emulator to attach'
        for ($i = 0; $i -lt 180; $i++) {
            $match = Get-AttachedDevices | Where-Object { (Get-AvdName $_) -eq $Avd } | Select-Object -First 1
            if ($match) { $script:Serial = $match; Say "attached at $match after ${i}s"; break }
            Start-Sleep -Seconds 1
        }
        if (-not $script:Serial) { throw "AVD $Avd never attached to adb" }
    }

    # Run this even when reusing a device: `device` state only means adb can talk
    # to it, not that Android has finished booting.
    Wait-BootCompleted
    Adb shell wm dismiss-keyguard *> $null
    Adb shell settings put global window_animation_scale 0 *> $null
    Adb shell settings put global transition_animation_scale 0 *> $null
    Adb shell settings put global animator_duration_scale 0 *> $null
    Say 'ready (animations disabled)'
}

function Cmd-Build {
    # CiBuild=true swaps in CIAuthService / CIAniListClient / CIAiringNotificationService,
    # so the app launches already "signed in" with fixture data. No OAuth, no AniList calls,
    # no rate-limit budget spent. EmbedAssembliesIntoApk makes the APK self-contained.
    Say 'dotnet build (Debug, CI stubs)'
    Push-Location $RepoRoot
    try {
        & dotnet build src/AniSprinkles/AniSprinkles.csproj -c Debug -f net10.0-android `
            -p:EmbedAssembliesIntoApk=true -p:CiBuild=true
        if ($LASTEXITCODE -ne 0) { throw "build failed ($LASTEXITCODE)" }
    } finally { Pop-Location }
    Say "apk: $Apk"
}

function Cmd-Install {
    if (-not (Test-Path $Apk)) { throw "APK missing — run: driver.ps1 build" }
    $mb = [math]::Round((Get-Item $Apk).Length / 1MB)
    Say "installing $(Split-Path $Apk -Leaf) (${mb} MB)"

    # Android wants several times the APK size free during install, and the default AVD's
    # 6 GB data partition sits close to full with just this app on it. Drop the old copy up
    # front when space is short rather than discovering it through a failed install: the
    # uninstall costs seconds, the failed install costs a whole retry cycle.
    $freeKb = FreeDataKb
    $needKb = [int64]((Get-Item $Apk).Length / 1KB) * 4
    if ($null -ne $freeKb -and $freeKb -lt $needKb) {
        Say "only $([math]::Round($freeKb / 1024)) MB free on /data — removing the old copy first"
        Adb uninstall $Package *> $null
    }

    # Every adb call here goes through the `Adb` wrapper, not `& $Adb`: the wrapper adds
    # `-s $script:Serial` from Resolve-Target. Calling the binary directly ignores that, so with
    # ANDROID_SERIAL set or two devices attached the free-space check could read one device while
    # the install targeted another — or adb would just fail on "more than one device".
    #
    # NOTE: `adb install` prints "Failure [...]" to STDOUT and STILL exits 0 in some
    # adb builds, and a stale build of the package left installed would make a
    # naive `pm list packages` check pass. Parse the output text instead.
    $out = @(Adb install -r -d $Apk 2>&1) -join "`n"
    Write-Host $out

    if ($out -match 'INSUFFICIENT_STORAGE') {
        # Space was fine (or unreadable) going in but the install still did not fit.
        Say 'insufficient storage — uninstalling old copy and retrying'
        Adb uninstall $Package *> $null
        $out = @(Adb install $Apk 2>&1) -join "`n"
        Write-Host $out
    }

    if ($out -notmatch 'Success') {
        # @() around every capture: AdbOut returns $null when the device is gone, and `-join`
        # on $null throws "You cannot call a method on a null-valued expression", which used to
        # replace this diagnostic with a PowerShell error that said nothing about the install.
        $df = @(AdbOut df -h /data 2>$null) -join "`n"
        if (-not $df) { $df = '(could not read free space — device gone?)' }
        throw "install failed.`nadb said:`n$out`n`nFree space on the AVD:`n$df"
    }
    Say 'installed'
}

function Cmd-Launch {
    param([switch]$Resume)
    Adb shell wm dismiss-keyguard *> $null
    # Resolve the launcher activity instead of hardcoding it: MAUI generates a
    # crc64-suffixed class name that changes if the namespace/assembly is renamed.
    $activity = ((AdbOut cmd package resolve-activity --brief -c android.intent.category.LAUNCHER $Package) |
                 Where-Object { $_ -match '/' } | Select-Object -Last 1).Trim()
    if (-not $activity) { throw "could not resolve launcher activity for $Package" }
    Say "am start $activity$(if ($Resume) { ' (warm)' })"
    if ($Resume) { Adb shell am start -W -n $activity }
    else         { Adb shell am start -W -S -n $activity }
    for ($i = 0; $i -lt 40; $i++) {
        if ((AdbOut pidof $Package) -join '' -match '\d') { Say "app up after ${i}s"; return }
        Start-Sleep -Seconds 1
    }
    throw 'app process never appeared — check: driver.ps1 logcat'
}

function Cmd-Shot {
    param([string]$Name = 'shot')
    New-Item -ItemType Directory -Force -Path $ShotDir *> $null
    $out = Join-Path $ShotDir "$Name.png"
    # Capture on-device then pull. Piping `exec-out screencap -p` into a PowerShell
    # redirect corrupts the PNG (text encoding + CRLF); `adb pull` is byte-exact.
    Adb shell screencap -p /sdcard/shot.png *> $null
    Adb pull /sdcard/shot.png $out *> $null
    if (-not (Test-Path $out)) { throw 'screencap/pull produced no file' }
    Say "$out ($([math]::Round((Get-Item $out).Length / 1KB)) KB)"
    return $out
}

function Cmd-Dump {
    param([string]$Filter)
    $xml = Get-UiDump
    $lines = foreach ($n in ($xml -split '>')) {
        $t = if ($n -match 'text="([^"]*)"') { $Matches[1] } else { '' }
        $d = if ($n -match 'content-desc="([^"]*)"') { $Matches[1] } else { '' }
        if ($t -or $d) {
            $bits = @()
            if ($t) { $bits += "text=`"$t`"" }
            if ($d) { $bits += "desc=`"$d`"" }
            $bits -join '  '
        }
    }
    $lines = $lines | Select-Object -Unique
    if ($Filter) { $lines = $lines | Where-Object { $_ -like "*$Filter*" } }
    $lines | Write-Host
}

function Cmd-Xml {
    param([string]$Path)
    New-Item -ItemType Directory -Force -Path $ShotDir *> $null
    if (-not $Path) { $Path = Join-Path $ShotDir 'ui.xml' }
    Get-UiDump | Set-Content -Path $Path -Encoding utf8
    Say $Path
}

function Cmd-Swipe {
    param([string]$Dir = 'up')
    $w, $h = Get-ScreenSize
    $cx = [int]($w / 2)
    switch ($Dir) {
        'up'    { Adb shell input swipe $cx ([int]($h * 0.75)) $cx ([int]($h * 0.28)) 300 *> $null }
        'down'  { Adb shell input swipe $cx ([int]($h * 0.28)) $cx ([int]($h * 0.75)) 300 *> $null }
        'left'  { Adb shell input swipe ([int]($w * 0.85)) ([int]($h / 2)) ([int]($w * 0.15)) ([int]($h / 2)) 300 *> $null }
        'right' { Adb shell input swipe ([int]($w * 0.15)) ([int]($h / 2)) ([int]($w * 0.85)) ([int]($h / 2)) 300 *> $null }
        default { throw "swipe direction must be up|down|left|right" }
    }
    Start-Sleep -Milliseconds 900
}

function Cmd-ScrollTo {
    param([string]$Text, [int]$Max = 10)
    for ($i = 0; $i -le $Max; $i++) {
        if ((Get-UiDump) -match [regex]::Escape("text=`"$Text`"")) {
            Say "found '$Text' after $i swipe(s)"; return
        }
        Cmd-Swipe up
    }
    throw "'$Text' not found after $Max swipes"
}

function Cmd-WaitFor {
    param([string]$Text, [int]$Seconds = 30)
    for ($i = 0; $i -lt $Seconds; $i++) {
        try { if ((Get-UiDump) -match [regex]::Escape($Text)) { Say "'$Text' after ${i}s"; return } } catch { }
        Start-Sleep -Seconds 1
    }
    throw "'$Text' never appeared within ${Seconds}s"
}

function Cmd-Goto {
    param([string]$Page)
    # Bottom tab bar (issue #43): every tab label is a real uiautomator node carrying
    # both text and content-desc, so a tab switch is a plain text tap. This replaced
    # the old open-the-drawer-then-pick dance, and works from anywhere — including
    # partway down a pushed details stack.
    Tap-By -Attr 'text' -Value $Page
    Start-Sleep -Seconds 2
}

function Cmd-Search {
    # Search is its own tab now, so this is just a tab switch.
    Cmd-Goto 'Search'
}

function Cmd-Logcat {
    param([int]$Lines = 200)
    $pidText = ((AdbOut pidof $Package) -join '').Trim()
    if (-not $pidText) { throw 'app is not running' }
    AdbOut logcat -d --pid $pidText -t $Lines
}

function Cmd-AppLog {
    AdbOut run-as $Package cat files/logs/anisprinkles.log
}

# --------------------------------------------------------------- dispatch

$a = @($Args)
$cmd = $Command.ToLowerInvariant()

# Anything that talks to a device needs an unambiguous target resolved up front,
# so ambiguity surfaces as "set ANDROID_SERIAL to choose" rather than adb's bare
# "more than one device/emulator" halfway through a flow. `boot` and `up` pick
# their own target from the requested AVD; the rest need one already attached.
if ($cmd -notin @('help', 'env', 'build', 'boot', 'up')) {
    Resolve-Target -Required | Out-Null
}

switch ($cmd) {
    'help'       { Cmd-Help }
    'env'        { Cmd-Env }
    'boot'       { if ($a[0]) { Cmd-Boot $a[0] } else { Cmd-Boot } }
    'build'      { Cmd-Build }
    'install'    { Cmd-Install }
    'launch'     { Cmd-Launch }
    'resume'     { Cmd-Launch -Resume }
    'up'         { if ($a[0]) { Cmd-Boot $a[0] } else { Cmd-Boot }; Cmd-Install; Cmd-Launch }
    'stop'       { Adb shell am force-stop $Package; Say 'stopped' }
    'kill-emu'   { Adb emu kill; Say 'emulator killed' }
    'shot'       { if ($a[0]) { Cmd-Shot $a[0] } else { Cmd-Shot } }
    'dump'       { Cmd-Dump ($a -join ' ') }
    'xml'        { Cmd-Xml ($a[0]) }
    'tap'        { Tap-By -Attr 'text' -Value ($a -join ' ') }
    'tap-prefix' { Tap-By -Attr 'text' -Value ($a -join ' ') -Prefix }
    'tap-desc'   { Tap-By -Attr 'content-desc' -Value ($a -join ' ') }
    'longpress'  { Tap-By -Attr 'text' -Value ($a -join ' ') -Long }
    'type'       { Adb shell input text (($a -join ' ') -replace ' ', '%s'); Start-Sleep -Milliseconds 600 }
    'clear'      { # KEYCODE_CTRL_A is not select-all on Android — spam DEL instead.
                   $n = if ($a[0] -match '^\d+$') { [int]$a[0] } else { 60 }
                   Adb shell input keyevent KEYCODE_MOVE_END *> $null
                   Adb shell input keyevent (@('KEYCODE_DEL') * $n) *> $null
                   Start-Sleep -Milliseconds 600 }
    'key'        { if (-not $a[0]) { throw 'key needs a keycode, e.g. driver.ps1 key ENTER' }
                   Adb shell input keyevent "KEYCODE_$($a[0].ToUpperInvariant())"; Start-Sleep -Milliseconds 800 }
    'back'       { Adb shell input keyevent KEYCODE_BACK; Start-Sleep -Milliseconds 1000 }
    'swipe'      { if ($a[0]) { Cmd-Swipe $a[0] } else { Cmd-Swipe } }
    'scroll-to'  { Cmd-ScrollTo ($a -join ' ') }
    'wait-for'   { $secs = 30; $txt = $a -join ' '
                   if ($a.Count -gt 1 -and $a[-1] -match '^\d+$') { $secs = [int]$a[-1]; $txt = ($a[0..($a.Count - 2)] -join ' ') }
                   Cmd-WaitFor $txt $secs }
    'search'     { Cmd-Search }
    'goto'       { Cmd-Goto ($a -join ' ') }
    'logcat'     { if ($a[0]) { Cmd-Logcat ([int]$a[0]) } else { Cmd-Logcat } }
    'applog'     { Cmd-AppLog }
    default      { Write-Host "Unknown command '$Command'`n"; Cmd-Help; exit 1 }
}
