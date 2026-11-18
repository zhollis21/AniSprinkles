<#
.SYNOPSIS
  Build / boot / install / launch AniSprinkles on an Android emulator, and send it the
  app-specific broadcasts no general tool provides.

.DESCRIPTION
  Agent-facing harness for the MAUI Android app. Every command is a subcommand:

      pwsh .claude/skills/run-app/driver.ps1 <command> [args]

  Run `driver.ps1 help` for the full command list.

  Reading and driving the UI is MAUI DevFlow's job, not this script's (#170): the
  `maui devflow` CLI and its MCP server talk to an agent inside every Debug build.
  `launch` starts DevFlow's broker and the adb reverse it needs, so the agent registers.
  What stays here is what DevFlow cannot do: device and APK lifecycle, Android key
  events, screenshots that include system UI, and the broadcasts (fault injection,
  token seeding, notifications, the airing worker, deep links).

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
$Apk      = Join-Path $RepoRoot 'src\AniSprinkles\bin\Debug\net11.0-android\com.RainbowSprinkles.AniSprinkles-Signed.apk'

# ---------------------------------------------------------------- SDK plumbing

function Resolve-Sdk {
    # The SDK is Visual Studio's, C:\Program Files (x86)\Android\android-sdk, and ANDROID_HOME
    # points at it. Android Studio's %LOCALAPPDATA%\Android\Sdk was a second root on PATH until
    # 2026-11-12 (adb 34, no API 36 image; its emulator PANICs with "Cannot find AVD system
    # path"). It stays a last-resort candidate; the check below still picks the root that
    # actually has system images, whatever ANDROID_HOME says.
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
# which reads like a driver bug rather than an ambiguity. Resolve-Target fills it in — from
# ANDROID_SERIAL, this worktree's claim, or a free device — so it starts empty here.
$script:Serial = $null

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

# `serial` prints just the serial on stdout, for `ANDROID_SERIAL=$(driver.ps1 serial)`. Write-Host
# lands on stdout under `pwsh -File`, so while it runs the narration goes to stderr instead.
$script:SayToStderr = $false
function Say($msg) {
    if ($script:SayToStderr) { [Console]::Error.WriteLine("[driver] $msg") } else { Write-Host "[driver] $msg" }
}

function Get-AttachedDevices {
    # Only devices in `device` state — `offline` and `unauthorized` cannot be driven.
    @(& $Adb devices | ForEach-Object { if ($_ -match '^(\S+)\s+device\s*$') { $Matches[1] } })
}

function Get-ListedDevices {
    # Every serial adb lists, in any state. Claim liveness uses this rather than
    # Get-AttachedDevices: an emulator that blips to `offline` is still somebody's device.
    @(& $Adb devices | ForEach-Object { if ($_ -match '^(\S+)\s+(device|offline|unauthorized|authorizing|connecting|recovery|sideload|bootloader)\s*$') { $Matches[1] } })
}

function Get-AvdName {
    param([string]$DeviceSerial)
    # `adb -s <serial> emu avd name` prints the AVD name, then a line reading OK.
    # Physical devices have no console and just error, so treat failure as "no AVD".
    $out = & $Adb -s $DeviceSerial emu avd name 2>$null
    if (-not $out) { return $null }
    ($out | Where-Object { $_ -and $_.Trim() -and $_.Trim() -ne 'OK' } | Select-Object -First 1).Trim()
}

# ---------------------------------------------------------------- device claims (#203)
# Several worktrees, each with its own Claude Code session, share one machine's emulators.
# Without coordination the second session's `up` attached to the first session's emulator and
# force-stopped its app mid-verification. A claim is one file per serial, host-wide, recording
# which worktree owns the device. Plain lock files, not a heartbeated lease: whether a claim is
# stale can be read directly from the worktree folder and `adb devices`, so nothing has to keep
# it fresh, and an idle session keeps its device until it says `release`.

$DefaultAvd       = 'pixel_10_pro_xl_-_api_37'
$ClaimDir         = Join-Path $env:LOCALAPPDATA 'AniSprinkles\device-claims'
# This worktree's DevFlow binding: which serial its agent runs on, and on which broker port.
# Worktree-local rather than in the claim, because ANDROID_SERIAL can target a device this
# worktree does not hold, and the binding is still this worktree's.
$BindingFile      = Join-Path $RepoRoot 'tmp\driver-binding.json'
# Before auto-booting another instance. The AVD is hw.ramSize=6144, and one in use measured a
# 4.3 GB working set on this 32 GB machine, so 6 GB leaves room for it without paging the rest.
$MinFreeRamGb     = 6
# Cold launch is ~19 s and the agent registers with the broker at startup; 60 s is ~3x that.
$AgentWaitSeconds = 60
# Even console ports the driver hands to the emulators it boots: emulator-5554 … emulator-5584,
# the range adb scans for emulators by default.
$ConsolePorts     = 5554..5584 | Where-Object { $_ % 2 -eq 0 }

function Normalize-Path([string]$Path) {
    if (-not $Path) { return '' }
    [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

$script:Worktree = Normalize-Path $RepoRoot

function Get-ClaimPath([string]$DeviceSerial) {
    # Serials are filename-safe for emulators (emulator-5554); a network device is host:port.
    Join-Path $ClaimDir (($DeviceSerial -replace '[^\w.-]', '_') + '.json')
}

function Read-ClaimFile([string]$Path) {
    try { Get-Content $Path -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop } catch { $null }
}

function Test-ProcessAlive($ProcessId) {
    if (-not $ProcessId) { return $false }
    [bool](Get-Process -Id ([int]$ProcessId) -ErrorAction SilentlyContinue)
}

function Get-LivenessContext {
    # What Test-ClaimLive checks against, read once per pass rather than once per claim.
    [pscustomobject]@{ Listed = @(Get-ListedDevices); Qemu = @(Get-QemuInstances) }
}

function Test-ClaimLive {
    # A claim counts while its worktree folder exists and its device is still there. "Still there"
    # is deliberately generous, because a wrongly-dropped claim hands a device in use to the next
    # session: adb listing it in any state, or — for an emulator — its qemu process still running
    # on that console port, which covers an adb server restart that briefly lists nothing. A claim
    # taken ahead of a boot has no device yet, so it counts while its emulator process is alive, or,
    # before the process has even been started, for a minute after it was written.
    param($Claim, $Context)
    if (-not $Claim -or -not $Claim.serial -or -not $Claim.worktree) { return $false }
    if (-not (Test-Path -LiteralPath $Claim.worktree)) { return $false }
    if ($Context.Listed -contains $Claim.serial) { return $true }
    if ($Claim.serial -match '^emulator-(\d+)$') {
        $port = [int]$Matches[1]
        if ($Context.Qemu | Where-Object Port -eq $port) { return $true }
    }
    if ($Claim.state -eq 'booting') {
        if ($Claim.emulatorPid) { return (Test-ProcessAlive $Claim.emulatorPid) }
        # ToUniversalTime on both sides: claimedAt can come back from ConvertFrom-Json as a
        # DateTime or a string depending on the PowerShell version, and a [datetime] cast of
        # the string yields local time. Pinning both to UTC keeps the window independent of that.
        try { return ((Get-Date).ToUniversalTime() - ([datetime]$Claim.claimedAt).ToUniversalTime()).TotalSeconds -lt 60 } catch { return $false }
    }
    return $false
}

function Test-FreshFile([string]$Path) {
    # New-Claim creates the file and then writes it, so for an instant another process can read
    # it empty. Such a file is a claim being made, not a corrupt one to clear away.
    try { ((Get-Date) - (Get-Item -LiteralPath $Path -ErrorAction Stop).LastWriteTime).TotalSeconds -lt 10 } catch { $false }
}

function Get-Claims {
    # Every live claim, with Mine worked out. Stale files are deleted on the way past, so a dead
    # worktree or a shut-down emulator never blocks anyone.
    if (-not (Test-Path $ClaimDir)) { return @() }
    $context = Get-LivenessContext
    $claims = foreach ($f in Get-ChildItem $ClaimDir -Filter *.json -File) {
        $c = Read-ClaimFile $f.FullName
        if (-not $c -and (Test-FreshFile $f.FullName)) { continue }
        $live = Test-ClaimLive $c $context
        if (-not $live) { Remove-Item -LiteralPath $f.FullName -Force -ErrorAction SilentlyContinue; continue }
        [pscustomobject]@{
            Serial   = $c.serial
            Worktree = $c.worktree
            Avd      = $c.avd
            State    = $c.state
            Claimed  = $c.claimedAt
            Mine     = ((Normalize-Path $c.worktree) -eq $script:Worktree)
        }
    }
    @($claims)
}

function Write-Claim([string]$DeviceSerial, [hashtable]$Fields) {
    $data = [ordered]@{
        serial    = $DeviceSerial
        worktree  = $script:Worktree
        avd       = $null
        state     = 'ready'
        claimedAt = (Get-Date).ToUniversalTime().ToString('o')
    }
    foreach ($k in $Fields.Keys) { $data[$k] = $Fields[$k] }
    ($data | ConvertTo-Json)
}

function New-Claim {
    # Atomically take $DeviceSerial for this worktree. FileMode.CreateNew is the lock: when two
    # sessions race for the same free device, exactly one create succeeds. Returns $true when this
    # worktree now holds the claim (including when it already did).
    param([string]$DeviceSerial, [hashtable]$Fields = @{})
    New-Item -ItemType Directory -Force -Path $ClaimDir | Out-Null
    $path = Get-ClaimPath $DeviceSerial
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        try {
            $fs = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try {
                $bytes = [Text.Encoding]::UTF8.GetBytes((Write-Claim $DeviceSerial $Fields))
                $fs.Write($bytes, 0, $bytes.Length)
            } finally { $fs.Dispose() }
            # One device per worktree: taking a new one gives the old one back, so "this
            # worktree's device" never has two answers.
            foreach ($old in Get-Claims | Where-Object { $_.Mine -and $_.Serial -ne $DeviceSerial }) {
                Remove-Item -LiteralPath (Get-ClaimPath $old.Serial) -Force -ErrorAction SilentlyContinue
                Say "released previous claim on $($old.Serial)"
            }
            return $true
        } catch [IO.IOException] {
            $existing = Read-ClaimFile $path
            if ($existing -and (Normalize-Path $existing.worktree) -eq $script:Worktree) {
                Update-Claim $DeviceSerial $Fields
                return $true
            }
            if (-not $existing -and (Test-FreshFile $path)) { return $false }
            if (Test-ClaimLive $existing (Get-LivenessContext)) { return $false }
            # Stale: its worktree is gone or its device left adb. Clear it and try once more.
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        }
    }
    return $false
}

function Update-Claim([string]$DeviceSerial, [hashtable]$Fields) {
    $path = Get-ClaimPath $DeviceSerial
    $c = Read-ClaimFile $path
    if (-not $c -or (Normalize-Path $c.worktree) -ne $script:Worktree) { return }
    $merged = @{}
    foreach ($p in $c.PSObject.Properties) { $merged[$p.Name] = $p.Value }
    foreach ($k in $Fields.Keys) { $merged[$k] = $Fields[$k] }
    Set-Content -LiteralPath $path -Value (Write-Claim $DeviceSerial $merged) -Encoding utf8
}

function Remove-OwnClaims {
    $removed = @()
    foreach ($c in Get-Claims | Where-Object Mine) {
        Remove-Item -LiteralPath (Get-ClaimPath $c.Serial) -Force -ErrorAction SilentlyContinue
        $removed += $c.Serial
    }
    Remove-Item -LiteralPath $BindingFile -Force -ErrorAction SilentlyContinue
    @($removed)
}

function Get-QemuInstances {
    # The emulator's qemu processes, parsed from their command lines. This is the only place the
    # read-only flag is visible: a writable instance of an AVD makes the emulator refuse every
    # -read-only boot of it ("run all emulators with -read-only flag"), so `boot` checks first
    # rather than waiting out the attach timeout.
    @(Get-Process qemu-system-x86_64 -ErrorAction SilentlyContinue | ForEach-Object {
        $line = $_.CommandLine
        if (-not $line) { return }
        [pscustomobject]@{
            ProcessId = $_.Id
            Avd       = if ($line -match '-avd\s+(\S+)') { $Matches[1].Trim('"') } else { $null }
            Port      = if ($line -match '-port\s+(\d+)') { [int]$Matches[1] } else { $null }
            ReadOnly  = $line -match '(^|\s)-read-only(\s|$)'
        }
    })
}

function Test-ReadOnlySerial([string]$DeviceSerial, $Qemu = (Get-QemuInstances)) {
    if ($DeviceSerial -notmatch '^emulator-(\d+)$') { return $false }
    $port = [int]$Matches[1]
    [bool]($Qemu | Where-Object { $_.ReadOnly -and $_.Port -eq $port })
}

function Format-Devices([string[]]$Devices, $Claims) {
    $lines = foreach ($d in $Devices) {
        $c = $Claims | Where-Object Serial -eq $d | Select-Object -First 1
        $owner = if (-not $c) { 'free' } elseif ($c.Mine) { 'claimed by this worktree' } else { "claimed by $($c.Worktree)" }
        "  $d  (avd: $(Get-AvdName $d); $owner)"
    }
    $lines -join "`n"
}

function Resolve-Target {
    # Pick the device every later command will talk to, in this order:
    #   1. ANDROID_SERIAL — the explicit override CI and humans already use. Always honoured, and
    #      it leaves claims alone: a one-off `ANDROID_SERIAL=<phone> driver.ps1 shot` must not cost
    #      this worktree the emulator it holds. A warning names the owner when another live
    #      worktree holds the device.
    #   2. This worktree's live claim.
    #   3. A free device, which this worktree then claims. For `boot`/`up` that means an instance
    #      of the requested AVD (default: $DefaultAvd, plus physical devices — a plugged-in phone
    #      is used, not booted around). For every other command, a lone free device of any kind,
    #      which keeps the zero-setup single-emulator flow working as it always has.
    # Returns $null when nothing fits and -Required is off; `boot` then starts a new instance.
    param([switch]$Required, [switch]$ForBoot, [string]$Avd, [switch]$NoClaim)

    if ($script:Serial) { return $script:Serial }
    $attached = @(Get-AttachedDevices)
    $claims = @(Get-Claims)

    if ($env:ANDROID_SERIAL) {
        $script:Serial = $env:ANDROID_SERIAL
        $other = $claims | Where-Object { $_.Serial -eq $script:Serial -and -not $_.Mine } | Select-Object -First 1
        if ($other) {
            Say "WARNING: $($script:Serial) is claimed by $($other.Worktree) — ANDROID_SERIAL overrides that, so this session may disturb it"
        }
        return $script:Serial
    }

    $qemu = Get-QemuInstances
    # A live claim whose device is not in `device` state right now (offline blip, still booting
    # in another call) is still this worktree's. Falling through would claim some other device,
    # which releases this one; stop and say so instead.
    $waiting = $claims | Where-Object { $_.Mine -and $attached -notcontains $_.Serial } | Select-Object -First 1
    if ($waiting -and -not $NoClaim) {
        throw "This worktree's device $($waiting.Serial) is claimed but not online (state: $($waiting.State)). Wait for it, or driver.ps1 release."
    }
    $mine = $claims | Where-Object { $_.Mine -and $attached -contains $_.Serial } | Select-Object -First 1
    # `boot`/`up` hold their own claim to the same rule as a free device: a writable emulator
    # this worktree picked up through a plain command is still not one `up` builds on.
    $mineFits = $mine -and (-not $Avd -or $mine.Avd -eq $Avd) -and
                (-not $ForBoot -or -not $mine.Avd -or (Test-ReadOnlySerial $mine.Serial $qemu))
    if ($mineFits) { $script:Serial = $mine.Serial; return $script:Serial }

    $taken = @($claims | Where-Object { -not $_.Mine } | ForEach-Object Serial)
    $free = @($attached | Where-Object { $taken -notcontains $_ })

    if ($ForBoot) {
        $want = if ($Avd) { $Avd } else { $DefaultAvd }
        # A writable emulator is never picked up implicitly: it may be one somebody started by
        # hand, and it is the reuse that let two sessions collide in the first place. Only an
        # instance this driver booted read-only, or a physical device, is fair game.
        $candidates = @($free | Where-Object {
            $name = Get-AvdName $_
            ($name -eq $want -and (Test-ReadOnlySerial $_ $qemu)) -or (-not $Avd -and -not $name)
        })
    } else {
        $candidates = if ($free.Count -eq 1) { @($free) } else { @() }
    }

    foreach ($c in $candidates) {
        if ($NoClaim) { $script:Serial = $c; return $script:Serial }
        if (New-Claim $c @{ avd = (Get-AvdName $c) }) {
            Say "claimed $c for this worktree"
            $script:Serial = $c
            return $script:Serial
        }
    }

    if ($Required) {
        if (-not $attached) { throw 'No device attached. Run: driver.ps1 up' }
        $detail = Format-Devices $attached $claims
        if ($free.Count -gt 1) { throw "More than one free device — set ANDROID_SERIAL to choose, or run driver.ps1 up to claim one:`n$detail" }
        throw "Every attached device is claimed by another worktree. Run driver.ps1 up to boot your own, or set ANDROID_SERIAL:`n$detail"
    }
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

# ------------------------------------------------------------------ MAUI DevFlow

function Start-DevFlowBroker {
    # The DevFlow agent in a Debug build registers with a broker on the host (port 19223)
    # and is then reachable through `maui devflow …` and its MCP server (#170). Without the
    # broker it still starts, on a fallback port, but nothing discovers it — so `launch`
    # makes sure both halves are in place before the app starts. Best-effort: a machine
    # without the `maui` CLI still gets a launched app, just no DevFlow.
    if (-not (Get-Command maui -ErrorAction SilentlyContinue)) {
        Say 'DevFlow: `maui` CLI not on PATH (dotnet tool install -g Microsoft.Maui.Cli --prerelease) — skipping broker'
        return
    }
    # The agent reaches the host broker through this reverse. It is per-device and lost
    # when the emulator restarts, so it is re-applied on every launch.
    Adb reverse tcp:19223 tcp:19223 *> $null

    $running = $false
    try { $running = [bool]((maui devflow broker status --json 2>$null | ConvertFrom-Json).running) } catch { }
    if (-not $running) {
        # `broker start` leaves a daemon behind that inherits the caller's stdout, so run it
        # detached; capturing its output would block until the daemon exits.
        Start-Process maui -ArgumentList 'devflow', 'broker', 'start' -WindowStyle Hidden | Out-Null
        for ($i = 0; $i -lt 20 -and -not $running; $i++) {
            Start-Sleep -Milliseconds 500
            try { $running = [bool]((maui devflow broker status --json 2>$null | ConvertFrom-Json).running) } catch { }
        }
    }
    Say "DevFlow: broker $(if ($running) { 'running on 19223' } else { 'did not start — the agent will fall back to port 9223' })"
}

function Read-Binding {
    if (-not (Test-Path $BindingFile)) { return $null }
    Read-ClaimFile $BindingFile
}

function Set-AgentForward([int]$AgentPort) {
    # The broker hands every agent a unique port, but the host side of that port is an
    # `adb forward`, and a host port forwards to exactly one device. Nothing in DevFlow ties it to
    # the agent's own device: on 2026-11-17 port 10243 forwarded to emulator-5556 while its agent
    # ran on emulator-5554, so even an explicit --agent-port reached nothing. Re-pointing it at
    # this worktree's device is cheap, so it is done on every bind and every passthrough call.
    Adb forward "tcp:$AgentPort" "tcp:$AgentPort" *> $null
}

function Get-AgentStatus([int]$AgentPort) {
    try { maui devflow agent status --agent-port $AgentPort --json 2>$null | ConvertFrom-Json } catch { $null }
}

function Bind-DevFlowAgent {
    # Tie this worktree to the agent inside the app it just launched. No DevFlow field names the
    # device (agent status reports platform, idiom and window size, not a serial), so the link is
    # the app's process id: `pidof` on this serial, matched to `processId` in `maui devflow list`.
    param([string]$AppPid, [datetime]$SinceUtc)
    if (-not (Get-Command maui -ErrorAction SilentlyContinue)) { return }

    $agent = $null
    for ($i = 0; $i -lt $AgentWaitSeconds -and -not $agent; $i++) {
        $agents = @(try { maui devflow list --json 2>$null | ConvertFrom-Json } catch { @() })
        # connectedAt filters out an agent left registered by an earlier run that happened to
        # have the same pid; the 5 s slack absorbs clock skew between the device and the host.
        $agent = $agents | Where-Object {
            "$($_.processId)" -eq $AppPid -and
            (-not $_.connectedAt -or ([datetime]$_.connectedAt).ToUniversalTime() -ge $SinceUtc.AddSeconds(-5))
        } | Sort-Object { [datetime]$_.connectedAt } -Descending | Select-Object -First 1
        if (-not $agent) { Start-Sleep -Seconds 1 }
    }
    if (-not $agent) {
        Say "DevFlow: no agent with pid $AppPid registered within ${AgentWaitSeconds}s — driver.ps1 devflow will not work until the next launch"
        Remove-Item -LiteralPath $BindingFile -Force -ErrorAction SilentlyContinue
        return
    }

    $port = [int]$agent.port
    Set-AgentForward $port
    # Pids are per device, so two emulators can each run the app under the same one. Confirm
    # through the forward just set that the agent answering is the one in THIS device's app.
    $status = Get-AgentStatus $port
    if (-not $status -or "$($status.app.processId)" -ne $AppPid) {
        Say "DevFlow: port $port did not answer as pid $AppPid through $($script:Serial) — not bound"
        Remove-Item -LiteralPath $BindingFile -Force -ErrorAction SilentlyContinue
        return
    }

    New-Item -ItemType Directory -Force -Path (Split-Path $BindingFile) | Out-Null
    [ordered]@{ serial = $script:Serial; agentPort = $port; appPid = $AppPid; boundAt = (Get-Date).ToUniversalTime().ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath $BindingFile -Encoding utf8
    Say "DevFlow: agent on port $port bound to $($script:Serial)"
    Say "  CLI: driver.ps1 devflow <args>    MCP: call maui_select_agent with agentPort $port first"
}

function Get-BoundAgentPort {
    $b = Read-Binding
    if (-not $b -or -not $b.agentPort) { throw 'No DevFlow agent is bound to this worktree. Run: driver.ps1 launch' }
    if ($b.serial -ne $script:Serial) {
        throw "This worktree's DevFlow agent is bound to $($b.serial), not $($script:Serial). Run: driver.ps1 launch"
    }
    [int]$b.agentPort
}

function Cmd-DevFlow {
    # `maui devflow <args>` against this worktree's own app. The CLI's auto-discovery picks any
    # agent the broker knows, and the broker is host-wide, so a session whose app is not up yet
    # silently drives another worktree's. Passing --agent-port, through a forward pointed at the
    # claimed device, is what keeps it on its own.
    param([string[]]$Argv)
    $port = Get-BoundAgentPort
    Set-AgentForward $port
    $flowArgs = @($Argv) + @('--agent-port', "$port")
    # Only `batch` reads stdin. An agent harness often runs commands with stdin redirected but
    # never closed, so reading it for every verb could block forever on `ui tree`.
    if ($Argv.Count -ge 1 -and $Argv[0] -eq 'batch' -and [Console]::IsInputRedirected) {
        # `printf ... | driver.ps1 devflow batch`: hand stdin to maui unchanged.
        $stdin = [Console]::In.ReadToEnd()
        $stdin | & maui devflow @flowArgs
    } else {
        & maui devflow @flowArgs
    }
    exit $LASTEXITCODE
}

function Cmd-AdbPassthrough {
    # Raw adb against this worktree's device. A bare `adb` only follows ANDROID_SERIAL, which does
    # not survive from one agent tool call to the next, so it would hit whichever device adb
    # defaults to — or fail outright with two attached.
    param([string[]]$Argv)
    if (-not $Argv) { throw 'adb needs arguments, e.g. driver.ps1 adb shell pm list packages' }
    Adb @Argv
    exit $LASTEXITCODE
}

function Cmd-Claims {
    $attached = @(Get-AttachedDevices)
    $claims = @(Get-Claims)
    Say "claims in $ClaimDir"
    if (-not $claims) { Say '  (none)' }
    foreach ($c in $claims) {
        Say "  $($c.Serial)  avd=$($c.Avd)  state=$($c.State)  $(if ($c.Mine) { '<- this worktree' } else { $c.Worktree })"
    }
    $unclaimed = @($attached | Where-Object { ($claims | ForEach-Object Serial) -notcontains $_ })
    foreach ($d in $unclaimed) { Say "  $d  avd=$(Get-AvdName $d)  unclaimed" }
}

function Cmd-Release {
    $removed = Remove-OwnClaims
    if ($removed) { Say "released $($removed -join ', ') (the emulator keeps running; kill-emu shuts it down)" }
    else { Say 'this worktree holds no claim' }
}

# ---------------------------------------------------------------- commands

function Cmd-Help {
@"
AniSprinkles driver — pwsh .claude/skills/run-app/driver.ps1 <command>

  env                       Show resolved SDK / adb / device / claim / DevFlow state
  boot [avd]                Claim a device for this worktree, booting a new -read-only
                            instance if none is free (default AVD: pixel_10_pro_xl_-_api_37;
                            with no avd a free phone counts too). Blocks until boot completes.
  build                     dotnet build -p:CiBuild=true (stub auth + stub API data)
  install                   adb install -r -d the signed debug APK
  launch                    Start DevFlow's broker + adb reverse, force-restart the app
                            (cold, ~20s), wait for its process, and bind its DevFlow agent
  resume                    Bring the app back to the foreground without restarting
                            (use after a stray back press drops you to the launcher)
  up [avd]                  boot + install + launch
  stop                      Force-stop the app
  kill-emu                  Shut this worktree's emulator down and release its claim

  Parallel sessions (#203) — each worktree claims its own device:
  claims                    List every device claim on this machine, and unclaimed devices
  release                   Give this worktree's claim back (the emulator keeps running)
  serial                    Print this worktree's serial, for tools that read ANDROID_SERIAL
  adb <args>                adb -s <this worktree's serial> <args>
  devflow <args>            maui devflow <args> --agent-port <this worktree's agent>;
                            stdin passes through, so `... | driver.ps1 devflow batch` works
  ANDROID_SERIAL still overrides everything, with a warning if another worktree holds it.

  shot <name>               Screenshot -> tmp/driver-screens/<name>.png (whole screen,
                            system UI included; maui devflow ui screenshot is the app only)
  key <KEYCODE>             e.g. BACK, ENTER, HOME — any Android key event
  back                      KEYCODE_BACK

  Reading, tapping, typing, scrolling and waiting are MAUI DevFlow's (#170):
    maui devflow ui tree | query | tap | fill | scroll | gesture | hit-test | screenshot
  The old dump / xml / tap / tap-desc / tap-prefix / longpress / type / clear / swipe /
  scroll-to / wait-for / goto / search verbs are gone — /run-app has the mapping.

  logcat [lines]            App-PID logcat tail (default 200)
  applog                    Pull the on-device rotating file log

  seed-token [test|real]    Sign a REAL-AUTH Debug build in over adb, no OAuth tapping (#160)
                            test  : ANILIST_RECORDER_TOKEN (CI test account; same var the
                                    fixture recorder uses) — the default
                            real  : ANISPRINKLES_DEV_TOKEN (your own account)
                            clear : sign out
                            -var <NAME> reads any other environment variable instead.
                            Does nothing on a -p:CiBuild=true build, where sign-in is stubbed.

  dump-token                Read the signed-in token back OUT of a real-auth build (#160).
                            Sign in on the emulator normally, then run this — the app has
                            already done the OAuth flow that is awkward to do by hand.
                            Writes to tmp/dev-token.txt (gitignored) and prints only the
                            length; the on-device copy is deleted immediately after.

  fault <op> <kind> [scope] Arm fault injection on the RUNNING app — no rebuild (#125)
                            op    : 'any', or a prefix whose meaning depends on -layer:
                                      -layer client : IAniListClient method name
                                                      e.g. GetStudio, LoadMediaCharactersPage
                                      -layer http   : GraphQL operationName
                                                      e.g. Studio, Media, MediaCharactersPage
                                    These are NOT interchangeable — GetMediaListAsync is
                                    'MediaListCollection' over the wire, SearchMediaPageAsync
                                    is 'Search'. A prefix that misses fires nothing; the http
                                    handler logs the operation it saw so you can see why.
                            kind  : ServiceOutage|Network|Authentication|RateLimited|
                                    NotFound|Unknown, or 'delay' for latency without failure
                            scope : next (default) | always | firstn:N | everynth:N
                            -delay <ms>     Latency before the call resolves. For RateLimited
                                            at -layer http this becomes Retry-After instead.
                            -layer client|http
                                    client (default) decorates IAniListClient and composes
                                    with the CI stubs — a real screen loads, the next call
                                    breaks. http injects into the pipeline so the rate-limit
                                    handler and error classifier actually run (needs a real
                                    signed-in session; does NOT work with -p:CiBuild=true).
                            -graphql        Answer HTTP 200 with a GraphQL errors array
                                            instead of a status code (http layer only)
  fault clear               Disarm. Faults are disarmed at every app start.
  notify [media] [ep] [title]
                            Post a REAL airing notification via NotificationHelper.Show
                            (default 21 1050 "ONE PIECE"). Debug-only receiver; the CI
                            notification stub stays a no-op, so screenshots are unaffected.
                            Tap it to exercise the production PendingIntent (#111).
  deeplink <media> [nonce]  Fire the deep-link intent directly, no notification. Covers
                            MainActivity/AppShell/PendingDeepLink. Repeat the same nonce to
                            check replay rejection; omit it for a fresh random one.
"@ | Write-Host
}

function Cmd-Env {
    Say "SDK      : $Sdk"
    Say "adb      : $Adb  ($(((& $Adb version) | Select-Object -Index 1)))"
    Say "emulator : $Emu"
    Say "repo     : $RepoRoot"
    Say "apk      : $Apk  (exists: $(Test-Path $Apk))"
    Say "worktree : $($script:Worktree)"
    $attached = @(Get-AttachedDevices)
    $claims = @(Get-Claims)
    Say 'devices  :'
    foreach ($line in (Format-Devices $attached $claims) -split "`n") { if ($line) { Say $line } }
    # Deliberately non-fatal, and -NoClaim: `env` is what you run to diagnose "no device", so it
    # reports what would be picked rather than throwing on it or claiming anything.
    $t = try { Resolve-Target -NoClaim } catch { $null }
    Say "target   : $(if ($t) { $t } else { '(none resolved)' })"
    $b = Read-Binding
    Say "devflow  : $(if ($b) { "agent port $($b.agentPort) on $($b.serial) (app pid $($b.appPid))" } else { '(no agent bound — driver.ps1 launch)' })"
}

function Get-FreeConsolePorts {
    # Even console ports no emulator, claim or listener is using, lowest first. Choosing the port
    # here, rather than letting the emulator pick, is what lets the claim be taken BEFORE the boot:
    # the serial is known up front, so two sessions booting at once can never adopt each other's
    # new instance. The caller claims the first one it can; another session may take one between
    # this check and that claim, which New-Claim's CreateNew settles.
    param([string[]]$Attached, $Qemu)
    $listening = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
                   ForEach-Object Port)
    $claimed = @(Get-Claims | ForEach-Object Serial)
    @(foreach ($p in $ConsolePorts) {
        $s = "emulator-$p"
        if ($Attached -contains $s -or $claimed -contains $s) { continue }
        if ($Qemu | Where-Object Port -eq $p) { continue }
        if ($listening -contains $p -or $listening -contains ($p + 1)) { continue }
        $p
    })
}

function Stop-BootedEmulator([int]$Port, $Launcher) {
    # A boot that never attached leaves emulator.exe and its qemu child behind, and each retry
    # would add another pair. Take down exactly the ones this boot started.
    if ($Launcher -and -not $Launcher.HasExited) { Stop-Process -Id $Launcher.Id -Force -ErrorAction SilentlyContinue }
    Get-QemuInstances | Where-Object Port -eq $Port |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

function Cmd-Boot {
    # $Avd is $null when none was named: that reuses this worktree's device whatever it is, and
    # lets a free phone count. Naming an AVD asks for exactly that AVD.
    param([string]$Avd)

    $existing = Resolve-Target -ForBoot -Avd $Avd
    $want = if ($Avd) { $Avd } else { $DefaultAvd }
    if ($existing) {
        # ANDROID_SERIAL is returned whether or not that device is up; without this a typo'd
        # serial would sit through the whole boot-completed timeout before failing.
        if (@(Get-AttachedDevices) -notcontains $existing) { throw "ANDROID_SERIAL=$existing is not attached. Unset it to let boot pick or start a device." }
        Say "using $existing (avd: $(Get-AvdName $existing))"
    } else {
        $attached = @(Get-AttachedDevices)
        $qemu = Get-QemuInstances

        # Every driver boot is -read-only, so N instances of one AVD can run side by side. The
        # emulator refuses any -read-only boot while a WRITABLE instance of the same AVD is up,
        # after hanging for ~2 minutes, so say which one is in the way instead.
        $writable = $qemu | Where-Object { $_.Avd -eq $want -and -not $_.ReadOnly } | Select-Object -First 1
        if ($writable) {
            $roPorts = @($qemu | Where-Object ReadOnly | ForEach-Object Port)
            $serial = $attached | Where-Object {
                (Get-AvdName $_) -eq $want -and ($_ -notmatch '^emulator-(\d+)$' -or $roPorts -notcontains [int]$Matches[1])
            } | Select-Object -First 1
            throw ("A writable instance of $want is running (qemu pid $($writable.ProcessId)$(if ($serial) { ", $serial" })). " +
                   "It blocks every -read-only boot of that AVD. Shut it down (driver.ps1 kill-emu with " +
                   "ANDROID_SERIAL=$(if ($serial) { $serial } else { '<serial>' })), or use it directly with ANDROID_SERIAL.")
        }

        $freeGb = [math]::Round((Get-CimInstance Win32_OperatingSystem).FreePhysicalMemory / 1MB, 1)
        if ($freeGb -lt $MinFreeRamGb) {
            throw "Only $freeGb GB of RAM free; booting another emulator needs $MinFreeRamGb GB. Free some memory, or run driver.ps1 claims to see who holds what."
        }

        $port = $null
        foreach ($p in Get-FreeConsolePorts -Attached $attached -Qemu $qemu) {
            if (New-Claim "emulator-$p" @{ avd = $want; state = 'booting' }) { $port = $p; break }
        }
        if (-not $port) { throw "No free emulator console port in $($ConsolePorts[0])-$($ConsolePorts[-1])" }
        $serial = "emulator-$port"

        $others = @($attached)
        if ($others) { Say "note: $($others.Count) other device(s) attached; this worktree gets its own" }
        Say "starting AVD $want read-only at $serial ($freeGb GB RAM free)"
        # ANDROID_SDK_ROOT must match the emulator we picked, or it re-searches PATH's SDK.
        $env:ANDROID_SDK_ROOT = $Sdk
        $env:ANDROID_HOME = $Sdk
        # -read-only: nothing is written back to the AVD, so the installed APK and any seed-token
        # sign-in are gone at shutdown. `up` reinstalls anyway; -no-snapshot-save already meant
        # nothing was kept from one boot to the next.
        $launcher = Start-Process -FilePath $Emu -PassThru `
            -ArgumentList @('-avd', $want, '-no-boot-anim', '-no-snapshot-save', '-read-only', '-port', "$port") `
            -WindowStyle Minimized
        Update-Claim $serial @{ emulatorPid = $launcher.Id }

        Say 'waiting for the new emulator to attach'
        for ($i = 0; $i -lt 180; $i++) {
            if (@(Get-AttachedDevices) -contains $serial) { $script:Serial = $serial; Say "attached at $serial after ${i}s"; break }
            if ($launcher.HasExited) { break }
            Start-Sleep -Seconds 1
        }
        if (-not $script:Serial) {
            Stop-BootedEmulator $port $launcher
            Remove-Item -LiteralPath (Get-ClaimPath $serial) -Force -ErrorAction SilentlyContinue
            throw "AVD $want never attached to adb at $serial$(if ($launcher.HasExited) { " (emulator exited with $($launcher.ExitCode))" })"
        }
        Update-Claim $serial @{ state = 'ready' }
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
    # CiBuild=true swaps in CIAuthService / CIAiringNotificationService and replays recorded
    # AniList responses at the HTTP layer (#134),
    # so the app launches already "signed in" with fixture data. No OAuth, no AniList calls,
    # no rate-limit budget spent. EmbedAssembliesIntoApk makes the APK self-contained.
    Say 'dotnet build (Debug, CI stubs)'
    Push-Location $RepoRoot
    try {
        & dotnet build src/AniSprinkles/AniSprinkles.csproj -c Debug -f net11.0-android `
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
    # Before the process starts: the agent looks for the broker once, at startup.
    if (-not $Resume) { Start-DevFlowBroker }
    $startedUtc = (Get-Date).ToUniversalTime()
    Say "am start $activity$(if ($Resume) { ' (warm)' })"
    if ($Resume) { Adb shell am start -W -n $activity }
    else         { Adb shell am start -W -S -n $activity }
    for ($i = 0; $i -lt 40; $i++) {
        # First token: pidof lists every match, and a dying process can briefly overlap its successor.
        $appPid = (((AdbOut pidof $Package) -join ' ').Trim() -split '\s+')[0]
        if ($appPid -match '^\d+$') {
            Say "app up after ${i}s"
            # A warm resume keeps the same process and so the same agent; re-point its forward,
            # which an adb restart or another session may have moved.
            if ($Resume) {
                $b = Read-Binding
                if ($b -and $b.serial -eq $script:Serial -and "$($b.appPid)" -eq $appPid) { Set-AgentForward ([int]$b.agentPort) }
            } else {
                Bind-DevFlowAgent -AppPid $appPid -SinceUtc $startedUtc
            }
            return
        }
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

function Cmd-Logcat {
    param([int]$Lines = 200)
    $pidText = ((AdbOut pidof $Package) -join '').Trim()
    if (-not $pidText) { throw 'app is not running' }
    AdbOut logcat -d --pid $pidText -t $Lines
}

function Cmd-AppLog {
    AdbOut run-as $Package cat files/logs/anisprinkles.log
}

# Arms runtime fault injection on the already-running app (#125). Replaces the old
# -p:ErrorSim=true build flag, which meant a ~3 minute rebuild per error kind and produced a
# build where EVERY call failed, so the screen you wanted to break was unreachable.
#
# The receiver is targeted by explicit component rather than by action alone: an implicit
# broadcast to a manifest-declared receiver is not delivered reliably on modern Android, and
# the component name is pinned in C# precisely so it can be named here.
function Cmd-Fault {
    param([string[]]$Argv)

    $component = "$Package/.FaultReceiver"
    $action    = 'com.RainbowSprinkles.FAULT'

    if ($Argv.Count -ge 1 -and $Argv[0].ToLowerInvariant() -eq 'clear') {
        Adb shell am broadcast -n $component -a $action --ez clear true *> $null
        Say 'fault cleared'
        return
    }

    # Pull the optional flags out, leaving <op> <kind> <scope> as positionals.
    $positional = @()
    $delay = 0
    $layer = 'client'
    $graphql = $false
    for ($i = 0; $i -lt $Argv.Count; $i++) {
        $token = $Argv[$i]
        if ($token -match '^-{1,2}delay$')        { $i++; $delay = [int]$Argv[$i] }
        elseif ($token -match '^-{1,2}layer$')    { $i++; $layer = $Argv[$i] }
        elseif ($token -match '^-{1,2}graphql$')  { $graphql = $true }
        else                                     { $positional += $token }
    }

    if ($positional.Count -lt 1) {
        throw 'fault needs an op, e.g. driver.ps1 fault GetStudio NotFound next (or: fault clear)'
    }

    $op    = $positional[0]
    $kind  = if ($positional.Count -ge 2) { $positional[1] } else { 'delay' }
    $scope = if ($positional.Count -ge 3) { $positional[2] } else { 'next' }

    $cmd = @('shell', 'am', 'broadcast', '-n', $component, '-a', $action)
    # 'any' and 'delay' are the driver's spelling for "omit this extra" — the receiver reads an
    # absent op as "match everything" and an absent kind as "delay without failing".
    if ($op -ne 'any')       { $cmd += @('--es', 'op', $op) }
    if ($kind -ne 'delay')   { $cmd += @('--es', 'kind', $kind) }
    $cmd += @('--es', 'scope', $scope)
    $cmd += @('--es', 'layer', $layer)
    if ($delay -gt 0)        { $cmd += @('--ei', 'delay', "$delay") }
    if ($graphql)            { $cmd += @('--ez', 'graphql', 'true') }

    Adb @cmd *> $null
    Say "fault armed: op=$op kind=$kind scope=$scope delay=${delay}ms layer=$layer graphql=$graphql"
}

# Posts one real airing notification on the running app (#111). Goes through the actual
# NotificationHelper.Show, so the PendingIntent a tap follows is the production one — that path has
# no unit-test reach at all, and is where the per-media request-code bug lived.
#
# CIAiringNotificationService stays a no-op, so this changes nothing about the screenshot job: it
# bypasses the service entirely and nothing broadcasts here during CI.
# Signs a real-auth Debug build in over adb, so a device pass against real AniList data does not
# start with tapping through WebAuthenticator (#160 was found needing exactly that). The CI stubs
# are the only scriptable way past sign-in, which is what made real-auth passes manual until now.
#
# The token is read from the environment and never echoed: it is passed straight to adb and only
# its length is reported back. Note that it does travel on an adb command line, where `am` may
# echo it into logcat — fine for a throwaway account on a local emulator, not a pattern to reuse.
function Cmd-SeedToken {
    param([string[]]$Argv)

    # 'seed-token' with no arguments is the common case, and the dispatch above builds $a as
    # @($Args) — which for no arguments is an array holding a single $null, not an empty array. So
    # Count is 1 and $Argv[0] is null, and the first .ToLowerInvariant() below would throw. Strip
    # the nulls rather than testing for them at each use.
    $Argv = @($Argv | Where-Object { $null -ne $_ })

    $component = "$Package/.TokenReceiver"
    $action    = 'com.RainbowSprinkles.TOKEN'

    if ($Argv.Count -ge 1 -and $Argv[0].ToLowerInvariant() -eq 'clear') {
        Adb shell am broadcast -n $component -a $action --ez clear true *> $null
        Say 'token cleared — the app is now signed out'
        return
    }

    # Which account to sign in as. 'test' reuses the variable the fixture recorder already needs
    # (tools/record-anilist-fixtures.cs), so the common case manages one credential rather than two.
    $which = if ($Argv.Count -ge 1) { $Argv[0].ToLowerInvariant() } else { 'test' }
    $varName = $null
    for ($i = 0; $i -lt $Argv.Count; $i++) {
        if ($Argv[$i] -match '^-{1,2}var$') { $i++; $varName = $Argv[$i] }
    }

    if (-not $varName) {
        $varName = switch ($which) {
            'test'  { 'ANILIST_RECORDER_TOKEN' }
            'real'  { 'ANISPRINKLES_DEV_TOKEN' }
            default { throw "seed-token takes 'test', 'real', 'clear', or -var <NAME> — got '$which'" }
        }
    }

    $token = [Environment]::GetEnvironmentVariable($varName)
    if ([string]::IsNullOrWhiteSpace($token)) {
        # Guidance to the host, then a one-line throw: PowerShell renders a multi-line throw as one
        # unreadable blob, and every other error in this script is a single sentence.
        Write-Host ''
        Write-Host "  test  -> ANILIST_RECORDER_TOKEN   the CI test account, shared with tools/record-anilist-fixtures.cs"
        Write-Host "  real  -> ANISPRINKLES_DEV_TOKEN   your own account, for reproducing real-world data bugs"
        Write-Host ''
        Write-Host "  Set it in the shell, not as an argument, so it stays out of shell history:"
        Write-Host "      `$env:$varName = `"<access-token>`""
        Write-Host ''
        throw "$varName is not set, so there is no token to seed."
    }

    Adb shell am broadcast -n $component -a $action --es token "$token" *> $null
    Say "token seeded from $varName ($($token.Length) chars) — pull to refresh or relaunch to pick it up"
}

# Reads the signed-in token back off the device, so the app's own OAuth flow can be the way you
# obtain a token for the fixture tooling — sign in on the emulator normally, then pull it.
#
# AniList's implicit grant returns the token in the fragment of a redirect to anisprinkles://auth,
# which a desktop browser cannot follow, so getting one by hand is awkward. The app already does
# that flow correctly; this just hands the result over.
#
# The value is never printed. It goes to a gitignored file and only its length is reported, so a
# live credential does not end up in terminal scrollback or in any transcript of this session.
function Cmd-DumpToken {
    $component = "$Package/.TokenReceiver"
    $action    = 'com.RainbowSprinkles.TOKEN'
    $remote    = "files/dev-token.txt"
    $localDir  = Join-Path $RepoRoot 'tmp'
    $local     = Join-Path $localDir 'dev-token.txt'

    # Clear any stale copy first, or a failed dump silently returns the previous run's token.
    Adb shell run-as $Package rm -f $remote *> $null
    Adb shell am broadcast -n $component -a $action --ez dump true *> $null

    # The receiver writes asynchronously, so poll rather than assume it has landed.
    $content = $null
    for ($i = 0; $i -lt 20; $i++) {
        Start-Sleep -Milliseconds 250
        # AdbOut already prepends exec-out, which runs the command on the device directly — adding
        # 'shell' here nests it wrongly and returns a truncated, mangled result rather than failing.
        # (Cmd-AppLog is the reference form.) exec-out is also required rather than incidental: plain
        # adb shell rewrites LF to CRLF, which would corrupt the token on its way out.
        $content = (AdbOut run-as $Package cat $remote) -join ''
        if (-not [string]::IsNullOrWhiteSpace($content)) { break }
    }

    # Always remove the on-device copy, whatever happened — it holds a live credential.
    Adb shell run-as $Package rm -f $remote *> $null

    if ([string]::IsNullOrWhiteSpace($content)) {
        throw 'No token was written. Is this a CI-stub build, or is the app not running? Check driver.ps1 logcat.'
    }

    $content = $content.Trim()
    if ($content.StartsWith('<')) {
        throw "The app has no usable token to dump (state: $content). Sign in on the emulator first."
    }

    New-Item -ItemType Directory -Force $localDir | Out-Null
    Set-Content -Path $local -Value $content -NoNewline -Encoding ascii

    Say "token written to tmp/dev-token.txt ($($content.Length) chars) — gitignored, not printed"
    Write-Host ''
    Write-Host '  Load it into this shell:'
    Write-Host '      $env:ANILIST_RECORDER_TOKEN = (Get-Content tmp/dev-token.txt -Raw).Trim()'
    Write-Host ''
}

function Cmd-Notify {
    param([string[]]$Argv)

    $mediaId = if ($Argv.Count -ge 1) { [int]$Argv[0] } else { 21 }
    $episode = if ($Argv.Count -ge 2) { [int]$Argv[1] } else { 1050 }
    $title   = if ($Argv.Count -ge 3) { ($Argv[2..($Argv.Count - 1)] -join ' ') } else { $null }

    $cmd = @('shell', 'am', 'broadcast',
             '-n', "$Package/.NotifyReceiver",
             '-a', 'com.RainbowSprinkles.NOTIFY',
             '--ei', 'mediaId', "$mediaId",
             '--ei', 'episode', "$episode")
    # Quoted for the *device* shell, not PowerShell: `am` runs through sh on the device, so an
    # unquoted "Shingeki no Kyojin" arrives as three arguments and the title silently truncates.
    if ($title) { $cmd += @('--es', 'title', "`"$title`"") }

    Adb @cmd *> $null
    Say "notification posted: media=$mediaId episode=$episode$(if ($title) { " title=$title" })"
    Say "  open the shade with: driver.ps1 key KEYCODE_NOTIFICATION"
}

# Runs the REAL AiringCheckWorker once, so DoWork -> AiringCheckRunner -> AiringScheduleFetcher ->
# notification executes end to end (#141). A CI build stubs the notification service, so the worker
# is otherwise never scheduled and this whole path never runs on device.
#
# NOTE: this makes a real unauthenticated AniList request (one per page). Manual and deliberate —
# nothing broadcasts here during CI.
function Cmd-WorkerRun {
    param([string[]]$Argv)

    $mediaIds = if ($Argv.Count -ge 1) { $Argv[0] } else { '21,16498,101922' }
    $hours    = if ($Argv.Count -ge 2) { [int]$Argv[1] } else { 168 }

    Adb shell am broadcast -n "$Package/.NotifyReceiver" -a com.RainbowSprinkles.NOTIFY `
        --ez run true --es mediaIds "`"$mediaIds`"" --ei lookbackHours $hours *> $null

    Say "worker run enqueued: mediaIds=$mediaIds lookback=${hours}h"
    Say "  watch it with: driver.ps1 logcat 200   (tag AiringCheckWorker)"
}

# Fires the deep-link intent directly, without a notification (#111). Covers MainActivity, AppShell
# and PendingDeepLink; use 'notify' plus a real tap to also cover the PendingIntent construction.
#
# 0x34000000 = NEW_TASK | CLEAR_TOP | SINGLE_TOP, matching NotificationHelper.Show.
function Cmd-DeepLink {
    param([string[]]$Argv)

    if ($Argv.Count -lt 1) {
        throw 'deeplink needs a mediaId, e.g. driver.ps1 deeplink 21 [nonce]'
    }

    $mediaId = [int]$Argv[0]
    # A distinct default nonce per run, so a repeat is not mistaken for a replay of the last one.
    $nonce = if ($Argv.Count -ge 2) { [int]$Argv[1] } else { [int](Get-Random -Minimum 1 -Maximum 2147483647) }

    Adb shell am start -W -n "$Package/crc6418a5ac36641a60eb.MainActivity" -f 0x34000000 `
        --ei anisprinkles.deeplink.mediaId $mediaId `
        --ei anisprinkles.deeplink.nonce $nonce *> $null
    Say "deep link fired: media=$mediaId nonce=$nonce"
}

# --------------------------------------------------------------- dispatch

# No arguments arrives as an array holding one $null, not an empty one (see Cmd-SeedToken).
$a = @($Args | Where-Object { $null -ne $_ })
$cmd = $Command.ToLowerInvariant()
if ($cmd -eq 'serial') { $script:SayToStderr = $true }

# Anything that talks to a device needs an unambiguous target resolved up front,
# so ambiguity surfaces as "set ANDROID_SERIAL to choose" rather than adb's bare
# "more than one device/emulator" halfway through a flow. `boot` and `up` pick
# their own target from the requested AVD; the rest need one already attached.
if ($cmd -notin @('help', 'env', 'build', 'boot', 'up', 'claims', 'release')) {
    Resolve-Target -Required | Out-Null
}

switch ($cmd) {
    'help'       { Cmd-Help }
    'env'        { Cmd-Env }
    'boot'       { Cmd-Boot $a[0] }
    'build'      { Cmd-Build }
    'install'    { Cmd-Install }
    'launch'     { Cmd-Launch }
    'resume'     { Cmd-Launch -Resume }
    'up'         { Cmd-Boot $a[0]; Cmd-Install; Cmd-Launch }
    'stop'       { Adb shell am force-stop $Package; Say 'stopped' }
    'kill-emu'   {
        # Release only this worktree's claim, and only on the device being killed: with
        # ANDROID_SERIAL pointing elsewhere, this worktree's own device is still in use, and
        # another worktree's claim is that worktree's to give back.
        $ownsIt = [bool](Get-Claims | Where-Object { $_.Mine -and $_.Serial -eq $script:Serial })
        Adb emu kill
        if ($ownsIt) { Remove-Item -LiteralPath (Get-ClaimPath $script:Serial) -Force -ErrorAction SilentlyContinue }
        $b = Read-Binding
        if ($b -and $b.serial -eq $script:Serial) { Remove-Item -LiteralPath $BindingFile -Force -ErrorAction SilentlyContinue }
        Say "emulator $($script:Serial) killed"
    }
    'claims'     { Cmd-Claims }
    'release'    { Cmd-Release }
    'serial'     { Write-Output $script:Serial }
    'adb'        { Cmd-AdbPassthrough $a }
    'devflow'    { Cmd-DevFlow $a }
    'shot'       { if ($a[0]) { Cmd-Shot $a[0] } else { Cmd-Shot } }
    'key'        { if (-not $a[0]) { throw 'key needs a keycode, e.g. driver.ps1 key ENTER' }
                   Adb shell input keyevent "KEYCODE_$($a[0].ToUpperInvariant())"; Start-Sleep -Milliseconds 800 }
    'back'       { Adb shell input keyevent KEYCODE_BACK; Start-Sleep -Milliseconds 1000 }
    'logcat'     { if ($a[0]) { Cmd-Logcat ([int]$a[0]) } else { Cmd-Logcat } }
    'applog'     { Cmd-AppLog }
    'fault'      { Cmd-Fault $a }
    'seed-token' { Cmd-SeedToken $a }
    'dump-token' { Cmd-DumpToken }
    'notify'     { Cmd-Notify $a }
    'worker-run' { Cmd-WorkerRun $a }
    'deeplink'   { Cmd-DeepLink $a }
    { $_ -in @('dump', 'xml', 'tap', 'tap-prefix', 'tap-desc', 'longpress', 'type', 'clear',
               'swipe', 'scroll-to', 'wait-for', 'goto', 'search') } {
        Write-Host "'$Command' was removed (#170): reading and driving the UI is MAUI DevFlow's now."
        Write-Host '  maui devflow ui tree | query --text/--automationId [--wait-until exists] | tap | fill'
        Write-Host '  maui devflow ui scroll | gesture longpress | hit-test | screenshot'
        Write-Host 'The old-verb mapping is in .claude/skills/run-app/SKILL.md.'
        exit 1
    }
    default      { Write-Host "Unknown command '$Command'`n"; Cmd-Help; exit 1 }
}
