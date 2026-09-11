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

.EXAMPLE
    .\tools\mp-batch.ps1 -Exe ".\Build\Headless\The Waning Border.exe" -Warm 60 -LimitSec 300
    WARM peers: every process first plays a local skirmish (a different map /
    seed / faction count per peer) for 60 wall-seconds, tears it down the way
    a quit-to-menu does, and only THEN boots the lockstep match - in the same
    process, system objects and statics intact. This is the only way to
    reach the "second match in a process" desync class (2026-09-10, tick 150:
    a client that had played a skirmish carried its income carry, RNG
    streams and clock anchors into the MP match). -WarmPeers "1,3" limits
    the warm-up to those peer indices; -WarmMaps rotates maps per peer.
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
    [switch]$Monkey,           # command-coverage monkey: clients rotate every command type
    [int]$Warm = 0,            # >0: warm-up skirmish for this many wall-seconds before the MP match
    [string]$WarmPeers = "",   # "1,3": only these peer indices warm up (default: all)
    [string]$WarmMaps = "SunderedCrown,HollowTable,Veilmarch"   # rotated per peer index
)

if (-not (Test-Path $Exe)) { Write-Error "Player not found: $Exe"; exit 1 }
$exePath = (Resolve-Path $Exe).Path
$logRoot = Join-Path (Split-Path -Parent $exePath) "logs"
if ($Factions -le 0) { $Factions = $Peers }

$desyncs = 0; $ok = 0; $failed = 0

for ($m = 0; $m -lt $Matches; $m++) {
    $runSeed = $Seed + $m
    $warmNote = ""
    if ($Warm -gt 0) { $warmNote = " (warm-up ${Warm}s first)" }
    Write-Host ("match {0}/{1}: {2} peers, {3} factions, seed {4}, {5}s on {6}{7}" -f `
        ($m + 1), $Matches, $Peers, $Factions, $runSeed, $LimitSec, $Map, $warmNote) -ForegroundColor Cyan

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
            "-twbMap", $Map
        )
        # PS 5.1: a conditional inline element evaluates to $null when the
        # switch is off, and Start-Process refuses null ArgumentList items.
        # Append instead.
        if ($Monkey) { $argList += "-twbMpMonkey" }
        if ($Warm -gt 0) {
            $warmThis = $true
            if ($WarmPeers -ne "") {
                $warmThis = ($WarmPeers -split ",") -contains ([string]$i)
            }
            # EVERY peer gets the deadline. A peer that is meant to stay fresh
            # idles to it ("none") instead of booting at once - a fresh host
            # reaches world-ready in ~7 s and drops the still-warming clients
            # as lost after 15 s of silence.
            $wm = "none"
            if ($warmThis) {
                $maps = $WarmMaps -split ","
                $wm = $maps[$i % $maps.Count].Trim()
            }
            # The warm-up runs at 4x, so 60 wall-seconds is roughly a
            # 3-minute skirmish minus boot: enough for income ticks, AI
            # purchases, curse waves and RNG draws to leave residue.
            $argList += @("-twbMpWarm", $Warm, "-twbMpWarmMap", $wm)
        }
        $p = Start-Process -FilePath $exePath -PassThru -ArgumentList $argList
        $procs += $p
        Write-Host ("  peer {0} pid {1}{2}" -f $i, $p.Id, $(if ($i -eq 0) { " (host)" } else { "" }))
        Start-Sleep -Milliseconds 800   # stagger instance-slot claims
    }

    # Wall-clock guard mirrors HeadlessMp's own (limit*2 + 300) plus slack,
    # plus the warm-up window (HeadlessMp restarts its own guard after it).
    $deadline = (Get-Date).AddSeconds($LimitSec * 2 + 420 + $Warm)
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

    # PREFER THE VERDICT FILES over raw process exit codes: a peer that logs
    # its verdict ("exit=0 ... no desync") and then crashes in Unity's own
    # teardown (query-registry access violation during Application.Quit)
    # otherwise fails a match every checksum agreed on. The verdict is
    # written BEFORE quit, so it is the truth about the MATCH; the exit code
    # only about the shutdown.
    for ($i = 0; $i -lt $Peers; $i++) {
        $vf = Join-Path $logRoot ("MpVerdict_p{0}.txt" -f $i)
        if (Test-Path $vf) {
            # NOT $m: that is the match loop counter, and clobbering it with
            # a MatchInfo broke every -Matches run past the first.
            $vm = Select-String -Path $vf -Pattern '^exit=(\d+)' | Select-Object -First 1
            if ($vm) {
                $v = [int]$vm.Matches[0].Groups[1].Value
                if ($codes[$i] -ne $v) {
                    Write-Host ("  peer {0}: exit code {1} but verdict says {2} - trusting the verdict (teardown crash)" -f $i, $codes[$i], $v) -ForegroundColor Yellow
                    $codes[$i] = $v
                }
            }
        }
    }

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
