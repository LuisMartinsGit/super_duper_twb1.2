<#
.SYNOPSIS
    Prove the desync detector can still detect a desync.

.DESCRIPTION
    Runs one short lockstep match with a deliberate fault: at a chosen tick,
    ONE peer moves one entity one millimetre. Nothing else differs.

    THE PASS CONDITION IS A DESYNC. If the run comes back clean, the checksum
    has stopped covering position and every clean run since is worthless.

    This exists because on 2026-09-12 three separate pieces of this toolchain
    were caught reporting success on evidence they never gathered: a
    multiplayer match with no AI in it, a per-tick diff that compared nothing,
    and a cumulative counter that forgot. A green run is worth exactly what
    the detector behind it is worth, and nothing was checking the detector.

    Run it after any change to LockstepStateHash, and before trusting a long
    clean streak.

.EXAMPLE
    .\tools\mp-selftest.ps1 -Exe ".\Build\HeadlessHunt4\The Waning Border.exe"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Peers = 2,
    [int]$Tick = 900,          # 30 s in: past world-ready, before anything dies
    [int]$LimitSec = 120,
    [int]$BasePort = 18300,    # clear of both hunt lanes
    [string]$Map = "HollowTable",
    [int]$Seed = 777001
)

$ErrorActionPreference = "Continue"
if (-not (Test-Path $Exe)) { Write-Error "Player not found: $Exe"; exit 1 }
$exePath = (Resolve-Path $Exe).Path
$logRoot = Join-Path (Split-Path -Parent $exePath) "logs"

Write-Host "SELF-TEST: $Peers peers on $Map, 1 mm nudge on peer $($Peers-1) at tick $Tick" -ForegroundColor Cyan
Write-Host "expecting a DESYNC; a clean run is the failure." -ForegroundColor Yellow

Get-ChildItem $logRoot -Filter "MpVerdict_p*.txt" -ErrorAction SilentlyContinue | Remove-Item -Force

$procs = @()
for ($i = 0; $i -lt $Peers; $i++) {
    $argList = @(
        "-batchmode", "-nographics", "-twbMp",
        "-twbMpPeer", $i, "-twbMpPeers", $Peers,
        "-twbMpPort", $BasePort,
        "-twbPlayers", $Peers,
        "-twbSeed", $Seed, "-twbLimit", $LimitSec,
        "-twbMap", $Map,
        "-twbMpInject", $Tick, "-twbMpInjectPeer", ($Peers - 1)
    )
    $procs += Start-Process -FilePath $exePath -PassThru -ArgumentList $argList
    Start-Sleep -Milliseconds 800
}

$deadline = (Get-Date).AddSeconds($LimitSec * 2 + 420)
foreach ($p in $procs) {
    $ms = [int]($deadline - (Get-Date)).TotalMilliseconds
    if ($ms -lt 1000) { $ms = 1000 }
    if (-not $p.WaitForExit($ms)) { try { $p.Kill() } catch {} }
}

$codes = $procs | ForEach-Object { $_.ExitCode }
for ($i = 0; $i -lt $Peers; $i++) {
    $vf = Join-Path $logRoot ("MpVerdict_p{0}.txt" -f $i)
    if (Test-Path $vf) {
        $vm = Select-String -Path $vf -Pattern '^exit=(\d+)' | Select-Object -First 1
        if ($vm) { $codes[$i] = [int]$vm.Matches[0].Groups[1].Value }
    }
}
Write-Host ("  exit codes: {0}" -f ($codes -join ", "))

# The per-tick diff must see it too -- it is the finer of the two detectors
# and the one that would hide a fork inside the 30-tick sync interval.
$diff = Join-Path $PSScriptRoot "mp-diff.ps1"
$diffSaw = $false
if (Test-Path $diff) {
    & $diff -LogRoot $logRoot | Out-Host
    if ($LASTEXITCODE -eq 42) { $diffSaw = $true }
}

$syncSaw = ($codes -contains 42)
Write-Host ""
Write-Host ("  SYNC checksum   : {0}" -f $(if ($syncSaw) { "caught it" } else { "MISSED IT" })) `
    -ForegroundColor $(if ($syncSaw) { "Green" } else { "Red" })
Write-Host ("  per-tick diff   : {0}" -f $(if ($diffSaw) { "caught it" } else { "MISSED IT" })) `
    -ForegroundColor $(if ($diffSaw) { "Green" } else { "Red" })

if ($syncSaw -and $diffSaw) {
    Write-Host "`nSELF-TEST PASSED - both detectors see a 1 mm divergence." -ForegroundColor Green
    exit 0
}
Write-Host "`nSELF-TEST FAILED - the hunt cannot be trusted until this passes." -ForegroundColor Red
exit 1
