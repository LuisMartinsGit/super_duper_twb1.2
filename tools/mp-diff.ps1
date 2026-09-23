<#
.SYNOPSIS
    Diff every peer's Lockstep.log against the host's, tick by tick.

.DESCRIPTION
    THIRTY TIMES FINER THAN THE DESYNC FLAG. A peer only puts a checksum on
    the wire every 30 ticks (LockstepManager's sync cadence), so a fork that
    heals within the interval -- or one that happens after the last SYNC --
    never raises a DESYNC and the run exits 0. Every peer nonetheless WRITES
    a `sum` line for EVERY tick into its own Lockstep.log. Diffing those
    files compares all 9000 ticks and all fourteen columns, not 300 ticks of
    one number.

    Trailing ticks are not a fork: peers stop a tick or two apart at the
    limit, so the host's log routinely runs longer than a client's. Only
    ticks BOTH peers recorded are compared, and the first differing one is
    reported with both rows so the column that forked is visible at a glance.

.EXAMPLE
    .\tools\mp-diff.ps1 -LogRoot ".\Build\HeadlessMpHunt\logs"
    Check the newest match's peer set.

.EXAMPLE
    .\tools\mp-diff.ps1 -LogRoot ".\Build\HeadlessMpHunt\logs" -All
    Check every match in the folder.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$LogRoot,
    [switch]$All
)

# Fail loud. The whole point of this tool is to notice something; a silent
# error inside it reads exactly like a clean run.
$ErrorActionPreference = "Stop"

# How many tick rows were actually compared across every peer of every match.
# Zero must never render as a pass: an empty check reads exactly like a clean
# one, which is the failure mode this whole tool exists to stop.
$script:ComparedTicks = 0

# Read a log the peers may still have OPEN. [IO.File]::ReadLines takes an
# exclusive-ish handle and throws "used by another process" against a live
# match; FileShare.ReadWrite is what lets the tool run mid-run.
function Read-SharedLines {
    param([string]$Path)
    $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    try {
        $sr = New-Object System.IO.StreamReader($fs)
        try   { while (($l = $sr.ReadLine()) -ne $null) { $l } }
        finally { $sr.Dispose() }
    }
    finally { $fs.Dispose() }
}

function Compare-PeerSet {
    param([System.IO.DirectoryInfo]$HostDir, [System.IO.DirectoryInfo[]]$ClientDirs)

    $hostLog = Join-Path $HostDir.FullName "Lockstep.log"
    if (-not (Test-Path $hostLog)) { Write-Host "  no Lockstep.log in $($HostDir.Name)" -ForegroundColor Yellow; return $true }

    # tick -> the whole sum line, host side.
    #
    # PARSE STRICTLY. The first version indexed the tick with a hardcoded
    # Substring(9,6), which lands one character early ("=00012") and threw on
    # every line. PowerShell's default Continue then left $t at $null, the
    # ContainsKey guard threw too, and the shared-tick counter incremented
    # unconditionally -- so the tool printed "agrees on all 9002 shared ticks"
    # while comparing precisely nothing. A checking tool that cannot fail
    # loudly is worse than no tool.
    $rx = [regex]'^sum\s+tick=(\d+) '
    $hostRows = @{}
    foreach ($line in Read-SharedLines $hostLog) {
        if (-not $line.StartsWith("sum")) { continue }
        $m = $rx.Match($line)
        if (-not $m.Success) { throw "unparsable sum line in $hostLog : $line" }
        $hostRows[[int]$m.Groups[1].Value] = $line
    }
    $hostTicks = @($hostRows.Keys).Count
    if ($hostTicks -eq 0) {
        Write-Host "  host log holds no sum lines yet (match still running?)" -ForegroundColor Yellow
        return $true
    }
    Write-Host ("  host    {0}: {1} ticks" -f $HostDir.Name, $hostTicks)

    $clean = $true
    foreach ($c in $ClientDirs) {
        $cl = Join-Path $c.FullName "Lockstep.log"
        if (-not (Test-Path $cl)) { Write-Host "    no Lockstep.log in $($c.Name)" -ForegroundColor Yellow; continue }

        $shared = 0; $firstBad = -1; $badLocal = ""; $badHost = ""
        foreach ($line in Read-SharedLines $cl) {
            if (-not $line.StartsWith("sum")) { continue }
            $m = $rx.Match($line)
            if (-not $m.Success) { throw "unparsable sum line in $cl : $line" }
            $t = [int]$m.Groups[1].Value
            if (-not $hostRows.ContainsKey($t)) { continue }   # trailing tick, not a fork
            $shared++
            if ($firstBad -lt 0 -and $hostRows[$t] -ne $line) {
                $firstBad = $t; $badLocal = $line; $badHost = $hostRows[$t]
            }
        }

        if ($firstBad -ge 0) {
            $clean = $false
            Write-Host ("    {0}: FORK at tick {1} ({2} shared ticks)" -f $c.Name, $firstBad, $shared) -ForegroundColor Red
            Write-Host ("      host   {0}" -f $badHost)
            Write-Host ("      peer   {0}" -f $badLocal)
        }
        else {
            Write-Host ("    {0}: agrees on all {1} shared ticks" -f $c.Name, $shared) -ForegroundColor Green
            $script:ComparedTicks += $shared
        }
    }
    return $clean
}

$root = (Resolve-Path $LogRoot).Path
$hosts = Get-ChildItem $root -Directory -Filter "*_host" | Sort-Object LastWriteTime -Descending
if (-not $All) { $hosts = $hosts | Select-Object -First 1 }
if (-not $hosts) { Write-Error "no *_host match folder under $root"; exit 1 }

$anyFork = $false
foreach ($h in $hosts) {
    # Peers of one match share the stamp prefix up to the minute; pair them by
    # the map name and a launch time within two minutes of the host's.
    $stamp = $h.Name -replace "_host$", ""
    $map   = ($stamp -split "_")[-1]
    $peers = Get-ChildItem $root -Directory -Filter ("*_{0}_client*" -f $map) |
             Where-Object { [math]::Abs(($_.CreationTime - $h.CreationTime).TotalSeconds) -lt 120 }
    Write-Host ("match {0}" -f $h.Name) -ForegroundColor Cyan
    if (-not (Compare-PeerSet -HostDir $h -ClientDirs $peers)) { $anyFork = $true }
}

if ($anyFork) { Write-Host "`nFORK(S) FOUND" -ForegroundColor Red; exit 42 }
if ($script:ComparedTicks -eq 0) {
    Write-Host "`nNOTHING WAS COMPARED - this is not a pass." -ForegroundColor Yellow
    Write-Host "  (no peer logs with sum lines were readable under $root)" -ForegroundColor Yellow
    exit 1
}
Write-Host ("`nevery peer agreed on every shared tick ({0} row comparisons)" -f $script:ComparedTicks) -ForegroundColor Green
exit 0
