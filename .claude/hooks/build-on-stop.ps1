# Stop hook: before Claude hands back, the code it changed must build, with zero
# warnings (AGENTS.md, Build Quality). Runs only when something the compiler
# reads has changed since the last run that passed - a fingerprint of those
# files' paths, sizes and write times, kept in .claude/hooks/.last-build, which
# is git-ignored.
#
# What counts as "something the compiler reads" is wider than the source:
#
#   .cs, .xaml                  the source itself.
#   .csproj, .props, .targets   a reference, a constant, a TFM, a warning
#                               setting. None of these is a source file and all
#                               of them change what a build produces.
#   .editorconfig               analyzer severity lives here (CA1822 is
#                               suppressed project-wide in it), so a change can
#                               turn a warning on in a file nobody touched.
#   global.json, nuget.config   the SDK pin and the feed list. Neither exists
#   Directory.*.props/targets   today; they are matched so that adding one does
#                               not silently fall outside the fingerprint.
#   Fixtures/AniList/*.json     embedded as resources under CiBuild, and the
#                               only thing the CiBuild trigger below is about.
#
# AniSprinkles.slnx is deliberately not included: the hook builds the three
# projects directly, so the solution file is not an input to any of them.
#
# Which builds run depends on what changed, because the Android ones cost
# minutes and the plain net10.0 ones cost seconds:
#
#   tests + Core          always, when anything changed. The test project
#                         project-references Core, so one command covers both -
#                         and it is the project that gets missed, since
#                         building the app project does not compile tests/ and
#                         `dotnet test` output filtered for failures hides
#                         warnings entirely.
#   the Android TFM       when anything under src/AniSprinkles/ changed.
#   the CiBuild variant   when a fixture or a `#if CI` file changed, since that
#                         is the only build that compiles those blocks.
#   all three             when a build input changed. Which projects a .csproj
#                         or .editorconfig change reaches is not worth
#                         modelling - Core is referenced by everything, and a
#                         root .editorconfig reaches every file in every
#                         project - so this one is deliberately conservative.
#
# Tests are not run here - that is /ani-review's job. This hook answers one
# question: does it still build clean?
#
# A failed build blocks the stop once, with the diagnostics, so Claude fixes
# them. If it is still failing when Claude stops again (stop_hook_active), the
# hook lets it stop - so Claude can say the build is broken and why - rather
# than trapping it in a loop on an error it cannot fix.

$ErrorActionPreference = 'Stop'
# Run by hand (no payload on stdin) this must still work, so an empty or
# unparseable payload means "not a repeat stop" rather than a crash.
$raw = [Console]::In.ReadToEnd()
$payload = if ($raw.Trim()) { try { $raw | ConvertFrom-Json } catch { $null } } else { $null }
$root = if ($env:CLAUDE_PROJECT_DIR) { $env:CLAUDE_PROJECT_DIR } else { (Get-Location).Path }
$stamp = Join-Path $root '.claude/hooks/.last-build'

$sourceExtensions = @('.cs', '.xaml')
$buildInputExtensions = @('.csproj', '.props', '.targets')
$buildInputNames = @('.editorconfig', 'global.json', 'nuget.config')

function Test-Tracked([string]$path) {
    $name = [System.IO.Path]::GetFileName($path)
    if ($buildInputNames -contains $name) { return $true }
    $extension = [System.IO.Path]::GetExtension($path)
    if ($sourceExtensions -contains $extension) { return $true }
    if ($buildInputExtensions -contains $extension) { return $true }
    # Only the AniList fixtures, not any .json: the app ships appsettings-ish
    # and tooling JSON that no build embeds.
    return $extension -eq '.json' -and $path -match '[\\/]Fixtures[\\/]AniList[\\/]'
}

function Test-BuildInput([string]$path) {
    $name = [System.IO.Path]::GetFileName($path)
    if ($buildInputNames -contains $name) { return $true }
    return $buildInputExtensions -contains [System.IO.Path]::GetExtension($path)
}

# Walks the source trees, pruning obj/ and bin/ as it descends rather than
# filtering them out afterwards. That matters more than it looks: the Android
# obj/ tree holds tens of thousands of files, and a Get-ChildItem -Recurse that
# enumerates them first costs 11 seconds on every stop - including the stops
# where nothing changed and no build runs. Pruning does the same job in 0.06s.
function Get-Sources([string[]]$dirs) {
    $files = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    $pending = [System.Collections.Generic.Queue[string]]::new()
    foreach ($dir in $dirs) {
        $path = Join-Path $root $dir
        if (Test-Path $path) { $pending.Enqueue($path) }
    }
    while ($pending.Count) {
        $current = $pending.Dequeue()
        foreach ($sub in [System.IO.Directory]::EnumerateDirectories($current)) {
            $leaf = [System.IO.Path]::GetFileName($sub)
            if ($leaf -ne 'obj' -and $leaf -ne 'bin') { $pending.Enqueue($sub) }
        }
        foreach ($file in [System.IO.Directory]::EnumerateFiles($current)) {
            if (Test-Tracked $file) { $files.Add([System.IO.FileInfo]::new($file)) }
        }
    }
    # The repo-root build inputs, which live outside src/ and tests/. Matched by
    # name rather than listed, so Directory.Build.props arriving later is picked
    # up without touching this.
    foreach ($file in [System.IO.Directory]::EnumerateFiles($root)) {
        if (Test-BuildInput $file) { $files.Add([System.IO.FileInfo]::new($file)) }
    }
    $files
}

$all = Get-Sources @('src', 'tests')
if (-not $all) { exit 0 }

$fingerprint = ($all | Sort-Object FullName |
    ForEach-Object { "$($_.FullName)|$($_.Length)|$($_.LastWriteTimeUtc.Ticks)" }) -join "`n"
$hash = [BitConverter]::ToString(
    [Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($fingerprint))) -replace '-', ''

if ((Test-Path $stamp) -and (Get-Content $stamp -Raw).Trim() -eq $hash) {
    exit 0
}

# Everything newer than the last passing run decides which builds are needed.
if (Test-Path $stamp) {
    $since = (Get-Item $stamp).LastWriteTimeUtc
    $changed = @($all | Where-Object { $_.LastWriteTimeUtc -gt $since })
}
else {
    # No stamp - a fresh worktree, or the first stop of a session. File times
    # say nothing useful here (a fresh checkout writes every file at once), so
    # ask git what differs from HEAD instead. Running all three builds blind
    # costs about four and a half minutes, which is too much to pay on a stop
    # that may have changed one file in Core.
    $changed = @()
    $gitAnswered = $false
    try {
        $tracked = & git -C $root diff --name-only HEAD 2>$null
        $trackedOk = $LASTEXITCODE -eq 0
        $untracked = & git -C $root ls-files --others --exclude-standard 2>$null
        if ($trackedOk -and $LASTEXITCODE -eq 0) {
            $gitAnswered = $true
            $names = [System.Collections.Generic.HashSet[string]]::new(
                [StringComparer]::OrdinalIgnoreCase)
            foreach ($name in @($tracked) + @($untracked)) {
                if ($name) {
                    [void]$names.Add((Join-Path $root $name.Replace('/', [IO.Path]::DirectorySeparatorChar)))
                }
            }
            $changed = @($all | Where-Object { $names.Contains($_.FullName) })
        }
    }
    catch { $gitAnswered = $false }

    # git looked and found nothing of ours changed, so there is nothing to
    # verify. Record the fingerprint so the next stop can short-circuit.
    if ($gitAnswered -and -not $changed) {
        Set-Content -Path $stamp -Value $hash -NoNewline
        exit 0
    }
}

# The fingerprint moved but nothing identifiable changed (a deletion, or git
# could not answer): build everything rather than guess which half is stale.
if (-not $changed) { $changed = $all }

# A build input reaches further than the project it sits in, so it forces the
# lot rather than being classified.
$touchesBuildInput = [bool]($changed | Where-Object { Test-BuildInput $_.FullName })

# A fixture lives inside the app project but cannot break the build that does
# not embed it, so it is not enough on its own to pay for the plain Android TFM.
$touchesApp = $touchesBuildInput -or [bool]($changed | Where-Object {
        $_.FullName -match '[\\/]src[\\/]AniSprinkles[\\/]' -and
        $_.FullName -notmatch '[\\/]Fixtures[\\/]AniList[\\/]'
    })
$touchesCi = $touchesBuildInput -or [bool]($changed | Where-Object {
        $_.FullName -match '[\\/]Services[\\/]CI[\\/]' -or
        $_.FullName -match '[\\/]Services[\\/]Fixtures[\\/]' -or
        $_.FullName -match '[\\/]Fixtures[\\/]AniList[\\/]'
    })

$builds = [System.Collections.Generic.List[object]]::new()
$builds.Add(@{
        Name = 'tests + Core'
        Args = @('build', (Join-Path $root 'tests/AniSprinkles.UnitTests/AniSprinkles.UnitTests.csproj'), '-c', 'Debug')
    })
if ($touchesApp) {
    $builds.Add(@{
            Name = 'Android (Debug)'
            Args = @('build', (Join-Path $root 'src/AniSprinkles/AniSprinkles.csproj'), '-c', 'Debug', '-f', 'net10.0-android')
        })
}
if ($touchesCi) {
    $builds.Add(@{
            Name = 'Android (CiBuild)'
            Args = @('build', (Join-Path $root 'src/AniSprinkles/AniSprinkles.csproj'), '-c', 'Debug',
                '-f', 'net10.0-android', '-p:EmbedAssembliesIntoApk=true', '-p:CiBuild=true')
        })
}

$failures = [System.Collections.Generic.List[string]]::new()

foreach ($build in $builds) {
    $output = & dotnet @($build.Args + @('-v', 'q', '-nologo')) 2>&1 | Out-String
    $exit = $LASTEXITCODE

    # Zero warnings is the standard here, so a warning fails the gate exactly
    # as an error does. Both are reported, errors first.
    $lines = $output -split "`r?`n"
    $diagnostics = @(
        @($lines | Where-Object { $_ -match ': error ' }) +
        @($lines | Where-Object { $_ -match ': warning ' })
    ) | ForEach-Object { ($_ -replace '\s*\[[^\]]+\.csproj(::\S+)?\]\s*$', '').Trim() } |
        Sort-Object -Unique | Select-Object -First 20

    if ($exit -ne 0 -or $diagnostics) {
        if (-not $diagnostics) {
            $diagnostics = ($lines | Where-Object { $_.Trim() } | Select-Object -Last 20)
        }
        $failures.Add("$($build.Name):`n$($diagnostics -join "`n")")
    }
}

if ($failures.Count -eq 0) {
    Set-Content -Path $stamp -Value $hash -NoNewline
    exit 0
}

$detail = $failures -join "`n`n"
$ran = ($builds | ForEach-Object { $_.Name }) -join ', '

if ($payload.stop_hook_active) {
    @{ systemMessage = "The build is still not clean.`n$detail" } | ConvertTo-Json -Compress
    exit 0
}

@{
    decision = 'block'
    reason   = "The build must be clean before handing back - zero errors and zero warnings, per AGENTS.md. Ran: $ran.`n$detail`nFix these, or ask the repo owner before suppressing anything."
} | ConvertTo-Json -Compress
exit 0
