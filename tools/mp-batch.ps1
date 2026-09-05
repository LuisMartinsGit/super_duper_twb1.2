<#
.SYNOPSIS
    A real N-peer multiplayer match with no humans: one host + N-1 client
    processes on this machine, full lockstep over UDP, DeterministicLockstep
    on, every tick checksummed. The desync hunt, automated.

.DESCRIPTION
    Each process runs HeadlessMp (Assets/Scripts/Bootstrap/HeadlessMp.cs):
    identical match settings from the command line (no lobby), fixed lockstep
    ports per player index, host-side AI driving every faction through the
    lockstep command stream, clients injecting periodic real orders so the
    clienttohosttorelay path carries traffic too.

    Exit codes per peer: 0 ok, 42 DESYNC, 43 peer lost, 44 hang guard.
    Any 42 anywhere = the run found a desync; the per-peer match log folders
    (<install>\logs\<stamp>_<map>_host / _client1 / _client2 / _client3)
    then hold Lockstep.log + Desync_tick*_p*.log + *_trace.log on EVERY peer,
    ready to diff. The DESYNC console line itself names the forked
    subsystem(s) since the SYNC v2 wire format.

    MULTIPLAYER RUNS AT WALL-CLOCK SPEED (timeScale is pinned to 1 in MP and
    peers must stay in step), so budget -LimitSec accordingly.

.EXAMPLE
    .\tools\mp-batch.ps1 -Exe ".\Build\Headless\The Waning Border.exe" -LimitSec 900
    One 4-peer match, 15 minutes.

.EXAMPLE
    .\tools\mp-batch.ps1 -Exe ".\Build\Headless\The Waning Border.exe" -Matches 4 -LimitSec 600
    Four consecutive matches with stepped seeds.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Peers = 4,
    [int]$Factions = 0,        # 0 = same as Peers; more adds host-lobby AI slots
    [int]$Matches = 1,
    [int]$LimitSec = 900,
    [int]$Seed = 20260910,
    [int]$BasePort = 17980,
    [string]$Map = "Veilmarch",
    [switch]$Monkey            # command-coverage monkey: clients rotate every command type
)

if (-not (Test-Path $Exe)) { Write-Error "Player not found: $Exe"; exit 1 }
$exePath = (Resolve-Path $Exe).Path
$logRoot = Join-Path (Split-Path -Parent $exePath) "logs"
if ($Factions -le 0) { $Factions = $Peers }

$desyncs = 0; $ok = 0; $failed = 0

for ($m = 0; $m -lt $Matches; $m++) {
    $runSeed = $Seed + $m
    Write-Host ("match {0}/{1}: {2} peers, {3} factions, seed {4}, {5}s on {6}" -f `
        ($m + 1), $Matches, $Peers, $Factions, $runSeed, $LimitSec, $Map) -ForegroundColor Cyan

    # Stale verdicts must not shadow this run's.
    Get-ChildItem $logRoot -Filter "MpVerdict_p*.txt" -ErrorAction SilentlyContinue | Remove-Item -Force

    $procs = @()
    for ($i = 0; $i -lt $Peers; $i++) {
        $argList = @(
            "-batchmode", "-nographics", "-twbMp",
            "-twbMpPeer", $i, "-twbMpPeers", $Peers,
            "-twbMpPort", $BasePort,
            "-twbPlayers", $Factions,
            "-twbSeed", $runSeed, "-twbLimit", $LimitSec,
            $(if ($Monkey) { "-twbMpMonkey" }),
            "-twbMap", $Map
        )
        $p = Start-Process -FilePath $exePath -PassThru -ArgumentList $argList
        $procs += $p
        Write-Host ("  peer {0} pid {1}{2}" -f $i, $p.Id, $(if ($i -eq 0) { " (host)" } else { "" }))
        Start-Sleep -Milliseconds 800   # stagger instance-slot claims
    }

    # Wall-clock guard mirrors HeadlessMp's own (limit*2 + 300) plus slack.
    $deadline = (Get-Date).AddSeconds($LimitSec * 2 + 420)
    foreach ($p in $procs) {
        $remaining = [int]($deadline - (Get-Date)).TotalMilliseconds
        if ($remaining -lt 1000) { $remaining = 1000 }
        if (-not $p.WaitForExit($remaining)) {
            try { $p.Kill() } catch {}
            Write-Host "  KILLED a wedged peer (pid $($p.Id))" -ForegroundColor Red
        }
    }

    $codes = $procs | ForEach-Object { $_.ExitCode }
    Write-Host ("  exit codes: {0}" -f ($codes -join ", "))

    if ($codes -contains 42) {
        $desyncs++
        Write-Host "  DESYNC - evidence in the newest match folders:" -ForegroundColor Red
        Get-ChildItem $logRoot -Directory |
            Sort-Object LastWriteTime -Descending | Select-Object -First $Peers |
            ForEach-Object {
                $d = Get-ChildItem $_.FullName -Filter "Desync_*" -ErrorAction SilentlyContinue
                Write-Host ("    {0}  ({1} desync file(s))" -f $_.FullName, @($d).Count)
            }
    }
    elseif (($codes | Where-Object { $_ -ne 0 }).Count -gt 0) {
        $failed++
        Write-Host "  FAILED (non-desync): a peer exited abnormally" -ForegroundColor Yellow
    }
    else {
        $ok++
        Write-Host "  OK - full run, every checksum agreed" -ForegroundColor Green
    }
}

Write-Host ""
Write-Host ("{0} matches: {1} ok / {2} DESYNC / {3} failed" -f $Matches, $ok, $desyncs, $failed) `
    -ForegroundColor $(if ($desyncs -gt 0) { "Red" } elseif ($failed -gt 0) { "Yellow" } else { "Green" })
Write-Host "Logs: $logRoot"
if ($desyncs -gt 0) { exit 42 } elseif ($failed -gt 0) { exit 1 } else { exit 0 }
