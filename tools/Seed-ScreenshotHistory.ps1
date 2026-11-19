<#
.SYNOPSIS
    One-off: builds the first ci-screenshots/main commit from the screenshot sets
    that main's own history still holds, so history/ starts full rather than empty.

.DESCRIPTION
    Until #212, CI committed its screenshots to every PR branch, so each squash
    merge left a full set in main's history. This walks back from -Ref for the
    newest -Count commits whose tree still has a screenshots/ folder, and lays
    them out exactly as ci-screenshots-on-close.yml does:

        latest/                           the newest set (README.md links here)
        history/<YYYYMMDD-HHMMSS>-pr<N>/  one folder per merge, named from the
                                          commit time (UTC) and the trailing
                                          "(#N)" of its subject

    Each set gets a manifest.txt in the workflow's format, marked source=seed.

    Run it BEFORE main's history is rewritten to drop screenshots/ - afterwards
    there is nothing left to seed from. It refuses to overwrite an existing
    ci-screenshots/main unless -Force is passed, because the close workflow may
    already have promoted a newer set there.

    Without -Push it only builds the commit and prints its SHA, so the layout can
    be inspected with `git ls-tree -r --name-only <sha>` before anything leaves
    the machine.

.PARAMETER Ref
    Where to walk back from. Default origin/main.

.PARAMETER Count
    How many sets to seed. Default 10, matching HISTORY_KEEP in
    ci-screenshots-on-close.yml.

.PARAMETER Push
    Force-push the commit to refs/heads/ci-screenshots/main.

.PARAMETER Force
    Allow -Push to replace an existing ci-screenshots/main.

.EXAMPLE
    pwsh tools/Seed-ScreenshotHistory.ps1
    pwsh tools/Seed-ScreenshotHistory.ps1 -Push
#>
[CmdletBinding()]
param(
    [string] $Ref = 'origin/main',
    [ValidateRange(1, 50)]
    [int] $Count = 10,
    [switch] $Push,
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$branch = 'ci-screenshots/main'

function Invoke-Git {
    $output = & git @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') failed with exit code $LASTEXITCODE" }
    $output
}

# '--' quoted: bare, PowerShell takes it as its own end-of-parameters marker and
# never passes it to the function.
$candidates = Invoke-Git log --format='%H %ct %s' $Ref '--' screenshots/
$sets = foreach ($line in $candidates) {
    $sha, $epoch, $subject = $line -split ' ', 3
    # The commit that deleted screenshots/ (or any without it) has nothing to seed.
    & git rev-parse --verify --quiet "${sha}:screenshots" *> $null
    if ($LASTEXITCODE -ne 0) { continue }

    $prMatches = [regex]::Matches($subject, '\(#(\d+)\)')
    $pr = if ($prMatches.Count -gt 0) { $prMatches[$prMatches.Count - 1].Groups[1].Value } else { 'unknown' }
    $stamp = [DateTimeOffset]::FromUnixTimeSeconds([long]$epoch).UtcDateTime.ToString('yyyyMMdd-HHmmss')

    [pscustomobject]@{ Sha = $sha; Pr = $pr; Entry = "$stamp-pr$pr"; Subject = $subject }
}
$sets = @($sets | Select-Object -First $Count)
if ($sets.Count -eq 0) { throw "No commit reachable from $Ref has a screenshots/ folder - nothing to seed." }

$work = Join-Path ([IO.Path]::GetTempPath()) "seed-screenshots-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $work | Out-Null
$indexFile = Join-Path $work 'seed.index'
$tree = Join-Path $work 'tree'

try {
    foreach ($set in $sets) {
        $dest = Join-Path $tree "history/$($set.Entry)"
        New-Item -ItemType Directory -Path $dest -Force | Out-Null

        # git archive + tar rather than `git show > file`: a PowerShell redirect
        # re-encodes the bytes and corrupts the PNGs.
        $tar = Join-Path $work 'set.tar'
        Invoke-Git archive --format=tar -o $tar "$($set.Sha):screenshots" | Out-Null
        & tar -xf $tar -C $dest
        if ($LASTEXITCODE -ne 0) { throw "tar failed extracting $($set.Sha)" }
        Remove-Item $tar

        $files = @(Get-ChildItem $dest -Filter *.png | ForEach-Object Name)
        # LF, like the manifests CI writes: the close workflow parses these with sed,
        # and a trailing CR would make "complete=true" read as "true\r".
        $manifest = @(
            "pr=$($set.Pr)"
            "head_sha=$($set.Sha)"
            'complete=true'
            'missing='
            'source=seed'
            "subject=$($set.Subject)"
        )
        [IO.File]::WriteAllText((Join-Path $dest 'manifest.txt'), ($manifest -join "`n") + "`n")
        Write-Host "Seeded history/$($set.Entry) ($($files.Count) PNGs) from $($set.Sha.Substring(0, 7))"
    }

    Copy-Item -Path (Join-Path $tree "history/$($sets[0].Entry)") -Destination (Join-Path $tree 'latest') -Recurse

    # Quantize like the CI publish step, when pngquant is available.
    $pngquant = Get-Command pngquant -ErrorAction SilentlyContinue
    $pngs = @(Get-ChildItem $tree -Recurse -Filter *.png)
    $before = ($pngs | Measure-Object Length -Sum).Sum
    if ($pngquant) {
        foreach ($png in $pngs) {
            & $pngquant.Source --force --ext .png --skip-if-larger --strip $png.FullName
            if ($LASTEXITCODE -ne 0) { Write-Host "Kept original: $($png.FullName.Substring($tree.Length + 1))" }
        }
    }
    else {
        Write-Warning 'pngquant not found - seeding unquantized PNGs. They age out of history/ after 10 merges.'
    }
    $after = (Get-ChildItem $tree -Recurse -Filter *.png | Measure-Object Length -Sum).Sum
    Write-Host ("Total: {0:N1} MiB raw, {1:N1} MiB seeded" -f ($before / 1MB), ($after / 1MB))

    # Plumbing against a throwaway index, so this checkout's own index and working
    # tree are never touched.
    $env:GIT_INDEX_FILE = $indexFile
    try {
        Invoke-Git --work-tree=$tree add -A | Out-Null
        $treeSha = Invoke-Git write-tree
    }
    finally {
        Remove-Item Env:GIT_INDEX_FILE
    }
    # Explicit identity, as the workflows pass: a clean machine or runner has none configured.
    $commit = Invoke-Git -c user.name=github-actions[bot] -c user.email=github-actions[bot]@users.noreply.github.com commit-tree $treeSha -m "Seed screenshot history from $Ref ($($sets.Count) sets, #212)"
    Write-Host "Built $commit"

    if (-not $Push) {
        Write-Host "Inspect with: git ls-tree -r --name-only $commit"
        Write-Host 'Re-run with -Push to publish it.'
        return
    }

    & git ls-remote --exit-code --heads origin $branch *> $null
    if ($LASTEXITCODE -eq 0 -and -not $Force) {
        throw "$branch already exists on origin. Pass -Force to replace it (this discards whatever the close workflow promoted there)."
    }
    Invoke-Git push --force origin "${commit}:refs/heads/$branch" | Out-Null
    Write-Host "Pushed $commit to $branch"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
