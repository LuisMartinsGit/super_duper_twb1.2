<#
.SYNOPSIS
    Run desync-hunt matches back to back, forever, varying the setup.

.DESCRIPTION
    The hunt is a search, and a search that runs the same match over and over
    only ever proves that one match is clean. This rotates the axes that have
    actually produced forks before:

      map      Veilmarch / HollowTable / SunderedCrown -- different extents,
               region counts and well layouts
      warm-up  every other cycle, so half the runs carry a previous match's
               residue into the lockstep one (the second-match-in-a-process
               class, 2026-09-10)
      monkey   every other cycle, so the client->host->relay path carries
               each command type in turn
      seed     stepped every match, so the AI plays a different game

    Each match writes its own log folder and mp-batch runs the per-tick
    cross-peer diff on it. Stop it by creating the stop file (default
    logs\HUNT_STOP); the loop finishes the match in flight and exits.

    TWO TRAPS PAID FOR ON 2026-09-12, both silent:

      * SPLAT A HASHTABLE, NEVER AN ARRAY. `& script.ps1 @array` passes the
        elements POSITIONALLY, so "-Exe" landed on -Peers and every call died
        in parameter binding -- on the error stream, which `| Out-Host` does
        not carry. The loop spun 21,000 times in an hour running nothing at
        all, and looked busy the whole time.
      * PowerShell variable names are CASE-INSENSITIVE, so a local $seed IS
        the parameter $Seed. `$seed = $Seed + $cycle` compounds every pass;
        the seed had reached 241,654,403 by cycle 21,042.

.EXAMPLE
    .\tools\twb-hunt-loop.ps1 -Exe ".\Build\HeadlessHunt2\The Waning Border.exe"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    # THREE HOURS (2026-09-12, operator directive). A match should end
    # because someone WON, not because a stopwatch ran out; HeadlessMp
    # already finishes on the global verdict, so the limit is only a ceiling.
    # It also reaches game states a six-minute match never sees -- the older
    # of the two forks on record was at tick 43,800, twenty-four minutes in.
    [int]$LimitSec = 10800,
    [int]$BaseSeed = 20261000,
    [int]$BasePort = 17980,
    # Rotation offset. Two lanes running at once must not sit on the same
    # combination at the same moment, or the second lane buys nothing but
    # heat. Give lane B -Phase 1 and it is always a map, an age and a length
    # away from lane A.
    [int]$Phase = 0,
    [string]$Lane = "A",
    [string]$StopFile = ""
)

$ErrorActionPreference = "Continue"
$projectRoot = Split-Path -Parent $PSScriptRoot
# PER LANE. One shared stop file cannot restart lane A while lane B is
# eighteen minutes into a long match: clearing it to free A also cancels B's
# pending exit. Each lane watches its own.
if ($StopFile -eq "") { $StopFile = Join-Path $projectRoot "logs\HUNT_STOP_$Lane" }
if (-not (Test-Path $Exe)) { Write-Error "Player not found: $Exe"; exit 1 }

$maps  = @("Veilmarch", "HollowTable", "SunderedCrown")

# HOW MANY PLAYERS THE MAP WAS AUTHORED FOR, read from its MapInfo asset.
#
# Running more factions than a map has PlayerStartMarkers does not fail; it
# falls back to a procedural layout, logs a warning nobody reads, and can put
# a start FOUR METRES from an authored one. On Hollow Table -- a 1v1 duel map
# with two starts -- a four-peer run spawned Blue's fortress 4.5 m from
# Green's, and both finished the match with zero units and zero territories
# while Red and Yellow played a normal game. Every Hollow Table run in this
# hunt before 2026-09-12 was two-thirds of a match.
function Get-MapPlayerCount {
    param([string]$MapName)
    $root = Join-Path $projectRoot "Assets\GameData\Scenes\Maps"
    if (-not (Test-Path $root)) { return 4 }
    foreach ($info in Get-ChildItem $root -Recurse -Filter "*MapInfo.asset" -ErrorAction SilentlyContinue) {
        # Folder names carry a space ("Hollow Table"); scene names do not.
        $folder = ($info.Directory.Name -replace "\s", "")
        if ($folder -ne $MapName) { continue }
        $n = 0; $inStarts = $false
        foreach ($line in [System.IO.File]::ReadAllLines($info.FullName)) {
            if ($line -match "^\s*PlayerStarts:") { $inStarts = $true; continue }
            if ($inStarts) {
                if ($line -match "^\s*-\s*\{") { $n++ } else { break }
            }
        }
        if ($n -ge 2) { return $n }
    }
    return 4
}
$cycle = 0
$ran = 0; $forks = 0; $failed = 0

Write-Host "hunt loop lane ${Lane}: $Exe (port $BasePort, phase $Phase)"
Write-Host "stop this lane by creating $StopFile"

while (-not (Test-Path $StopFile)) {
    $k       = $cycle + $Phase
    $map     = $maps[$k % $maps.Count]
    $warmFor = if ($k % 2 -eq 0) { 60 } else { 0 }
    $monkey  = ($k % 2 -eq 1)
    $runSeed = $BaseSeed + $cycle
    # START AGE alternates on a 3-cycle beat, out of phase with map and
    # monkey so the combinations do not lock together. Age 1 is the only way
    # the hunt reaches culture buildings, the wall set, sects and hero
    # levels -- until 2026-09-12 no headless match had ever started there.
    $age     = if ($k % 3 -eq 2) { 1 } else { 0 }
    # Every fourth cycle runs LONG. The two forks this harness has on record
    # landed at tick 6,120 and tick 43,800 -- the second is 24 minutes in, and
    # a diet of six-minute matches can never reach it. Long runs also let the
    # AI age up on its own, mass an army and actually fight.
    # The 3x long-run beat is capped at the ceiling: three hours is already
    # the longest match anyone will sit through.
    $secs    = if ($k % 4 -eq 3) { [Math]::Min(10800, $LimitSec * 3) } else { $LimitSec }
    # RICH on a 5-beat: prime to the 2-, 3- and 4-beats above, so every
    # combination of warm / age / length eventually meets both economies.
    $rich    = ($k % 5 -eq 4)

    $mapPeers = [Math]::Min(4, (Get-MapPlayerCount $map))

    $label = "lane $Lane cycle $cycle : $map ${mapPeers}p seed $runSeed"
    if ($warmFor -gt 0) { $label += " warm" }
    if ($monkey)        { $label += " monkey" }
    if ($age -gt 0)     { $label += " age$age" }
    if ($rich)          { $label += " rich" }
    $label += " ${secs}s"
    Write-Host ""
    Write-Host "=== $label ===" -ForegroundColor Cyan

    # HASHTABLE. See the header.
    $p = @{
        Exe      = $Exe
        Peers    = $mapPeers
        Matches  = 1
        LimitSec = $secs
        Map      = $map
        Seed     = $runSeed
        Age      = $age
        BasePort = $BasePort
    }
    if ($warmFor -gt 0) {
        $p.Warm = $warmFor
        $p.WarmMaps = "SunderedCrown,HollowTable,Veilmarch"
    }
    if ($monkey) { $p.Monkey = $true }
    if ($rich)   { $p.Rich = $true }

    $code = 0
    try {
        & (Join-Path $PSScriptRoot "mp-batch.ps1") @p 2>&1 | Out-Host
        $code = $LASTEXITCODE
    }
    catch {
        # A binding or launch failure must be LOUD. The whole point of this
        # loop is that nobody is watching it.
        Write-Host "  CYCLE FAILED TO LAUNCH: $($_.Exception.Message)" -ForegroundColor Red
        $code = -1
    }

    $ran++
    if     ($code -eq 42) { $forks++ }
    elseif ($code -ne 0)  { $failed++ }
    Write-Host ("  running total: {0} matches, {1} DESYNC, {2} failed" -f $ran, $forks, $failed)

    if ($code -eq -1) {
        # Never spin. A loop that cannot launch anything should idle visibly
        # rather than burn the machine pretending to hunt.
        Start-Sleep -Seconds 30
    }

    $cycle++
}

Write-Host ("hunt loop: stop file seen after {0} cycle(s) - {1} matches, {2} DESYNC, {3} failed." `
            -f $cycle, $ran, $forks, $failed)
