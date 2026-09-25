<#
.SYNOPSIS
    THE headless match runner. Single-player batches, lockstep multiplayer
    matches, and the forever-loop that rotates both.

.DESCRIPTION
    One script, three modes. It replaces headless-batch.ps1 (the parallel
    AI-only pool), mp-batch.ps1 (lockstep peers + the per-tick fork diff) and
    twb-hunt-loop.ps1 (the rotating forever-loop), which were three programs
    sharing one worker pool, one argument vocabulary and one idea of what a
    finished match looks like -- and drifting apart at all three.

      -Mode single   N matches at a time, each its own process, AI only.
      -Mode mp       one match at a time, P peers of one lockstep session,
                     then mp-diff.ps1 over every tick of every peer's log.
      -Mode loop     single or mp forever, rotating map / seed / warm-up /
                     monkey, until the stop file appears.

    MATCHES ARE INDEPENDENT PROCESSES, so they parallelise cleanly. Measured
    on this machine: one match costs ~13% CPU and 1.13 GB against 16 logical
    cores and ~16 GB free, so six at a time fits with real headroom.

    NO CODE CHANGE IS NEEDED FOR CONCURRENCY. LogPaths claims a per-process
    instance slot via a .instance<N>.lock file and MatchLogSession.Begin
    appends that slot to the folder name, so workers write to
    <stamp>_Veilmarch, <stamp>_Veilmarch-2, and so on.

    ACCELERATION HAS A CEILING, AND CONTENTION LOWERS IT. Everything
    integrates on deltaTime -- steering, arrival, separation, the formation
    wheel -- so a frame stretched by CPU contention is a coarser simulation,
    not a faster one. If parallel results diverge from sequential ones, fewer
    workers is the fix, not more speed.

.PARAMETER NoTimeout
    Let a match run as long as it takes. By default a worker that outlives
    -TimeoutMin is killed so it cannot hold a slot for the whole batch; with
    this set nothing is ever killed for taking too long, and only the match's
    own -Limit ends it. Use it when the question is "how does a real game
    end", not "how many can I get through".

.PARAMETER Limit
    Simulated seconds before a match ends itself and dumps. A match should
    end because someone WON; this is a ceiling, not a target.

    0 means NO CEILING AT ALL: the match runs until it is decided -- an
    elimination or a well domination -- however long that takes. Pair it
    with -NoTimeout, or the runner's own wall-clock guard kills what the
    match limit no longer does.

.EXAMPLE
    .\tools\twb-run.ps1 -Exe ".\Build\Current\The Waning Border.exe" `
        -Mode single -Workers 6 -Matches 6 -NoTimeout

.EXAMPLE
    .\tools\twb-run.ps1 -Exe ".\Build\Current\The Waning Border.exe" `
        -Mode mp -Peers 4 -Matches 3 -Limit 900

.EXAMPLE
    .\tools\twb-run.ps1 -Exe ".\Build\Current\The Waning Border.exe" `
        -Mode loop -Workers 6 -NoTimeout -StopFile logs\STOP
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [ValidateSet("single", "mp", "loop")][string]$Mode = "single",

    # ── how many, how long ────────────────────────────────────────────────
    [int]$Workers = 6,          # single/loop: matches in flight at once
    [int]$Matches = 6,          # 0 = unbounded (loop mode's default)
    # 0 = NO LIMIT: the match ends when it is DECIDED and not before. That
    # is the only way to measure how long a match takes to resolve; a cap
    # can only ever report "at least this long".
    [int]$Limit = 1800,
    [int]$Speed = 3,            # Time.timeScale; past 3-4x you measure the accelerant
    [int]$Seed = 20260924,
    [switch]$NoTimeout,         # never kill a slow match
    [int]$TimeoutMin = 60,      # wall-clock guard per match, ignored by -NoTimeout

    # ── what kind of game ─────────────────────────────────────────────────
    # 0 = AS MANY AS THE MAP HAS STARTS. -twbPlayers is NOT clamped by the
    # game: ask for four on Hollow Table, which authors two, and the two
    # extra factions fall through to procedural placement on a hand-built
    # map. Auto reads the count off the map instead, so a rotation across
    # maps of different sizes stays a fair test on every one of them.
    [int]$Players = 0,
    [string]$Map = "",          # "" = rotate MapRotation; else pin one
    [string[]]$MapRotation = @("Veilmarch", "SunderedCrown", "HollowTable", "TwinSpans", "SunderedReach"),
    [int]$Age = 0,
    [switch]$Rich,              # every faction pinned at the resource cap (diagnostic)
    [switch]$Trace,             # single mode: write MapTrace.txt for the replay

    # ── multiplayer only ──────────────────────────────────────────────────
    [int]$Peers = 4,
    [int]$Factions = 0,         # 0 = same as Peers; more adds host-lobby AI slots
    [int]$BasePort = 17980,
    [switch]$Monkey,            # clients rotate every command type
    [int]$Warm = 0,             # >0: warm-up skirmish for this many wall-seconds first
    [string]$WarmPeers = "",
    [string]$WarmMaps = "SunderedCrown,HollowTable,Veilmarch",

    # ── loop only ─────────────────────────────────────────────────────────
    [string]$StopFile = "",
    [int]$Phase = 0             # rotation offset, so two lanes never sit on one combination
)

$ErrorActionPreference = "Continue"
if (-not (Test-Path $Exe)) { Write-Error "Player not found: $Exe"; exit 1 }
$exePath = (Resolve-Path $Exe).Path
$installDir = Split-Path -Parent $exePath
$logRoot = Join-Path $installDir "logs"
if ($Factions -le 0) { $Factions = $(if ($Players -gt 0) { $Players } else { $Peers }) }
if ($StopFile -eq "") { $StopFile = Join-Path $logRoot ("STOP_" + $Mode) }

function MapFor([int]$n) {
    if ($Map -ne "") { return $Map }
    return $MapRotation[($n + $Phase) % $MapRotation.Count]
}

# Player starts authored per map (MapInfo.PlayerCount, which the lobby
# treats as a MAXIMUM). Keep in step with the MapInfo assets.
$MapPlayers = @{
    "HollowTable"   = 2
    "SunderedReach" = 3
    "SunderedCrown" = 4
    "TwinSpans"     = 6
    "Veilmarch"     = 8
}

function PlayersFor([string]$m) {
    if ($Players -gt 0) { return $Players }
    if ($MapPlayers.ContainsKey($m)) { return $MapPlayers[$m] }
    return 4
}

function Banner($text) { Write-Host $text -ForegroundColor Cyan }

# ═══════════════════════════════════════════════════════════════════════════
# SINGLE — a worker pool of independent AI-only matches
# ═══════════════════════════════════════════════════════════════════════════
function Invoke-Single {
    param([int]$Want, [datetime]$Until)

    $running = @{}                        # pid -> @{ Proc; Seed; Map; Start }
    $launched = 0; $ok = 0; $failed = 0; $killed = 0
    $started = Get-Date

    Banner ("single: {0} workers, {1} AI, {2} at {3}x{4}" -f `
        $Workers, $(if ($Players -gt 0) { $Players } else { "per-map" }), `
        $(if ($Limit -le 0) { "NO LIMIT (until decided)" } else { "${Limit}s" }), $Speed, `
        $(if ($NoTimeout) { ", no timeout" } else { ", kill after ${TimeoutMin}m" }))

    while ($true) {
        # Reap first, so a slot frees the moment a match ends.
        foreach ($id in @($running.Keys)) {
            $w = $running[$id]
            if ($w.Proc.HasExited) {
                $secs = [int]((Get-Date) - $w.Start).TotalSeconds
                if ($w.Proc.ExitCode -eq 0) {
                    $ok++
                    Write-Host ("  done  seed {0} {1}  {2}s" -f $w.Seed, $w.Map, $secs) -ForegroundColor Green
                } else {
                    $failed++
                    Write-Host ("  FAIL  seed {0} {1}  exit {2} after {3}s" -f $w.Seed, $w.Map, $w.Proc.ExitCode, $secs) -ForegroundColor Yellow
                }
                $running.Remove($id)
            }
            elseif (-not $NoTimeout -and ((Get-Date) - $w.Start).TotalMinutes -ge $TimeoutMin) {
                # A hung run must not hold a worker slot for the whole batch.
                try { $w.Proc.Kill() } catch {}
                $killed++
                Write-Host ("  KILL  seed {0} {1}  timeout" -f $w.Seed, $w.Map) -ForegroundColor Red
                $running.Remove($id)
            }
        }

        $done = ($Want -gt 0 -and $launched -ge $Want) -or ((Get-Date) -ge $Until)
        if ($done -and $running.Count -eq 0) { break }
        if ((Test-Path $StopFile) -and $running.Count -eq 0) { break }

        while (-not $done -and -not (Test-Path $StopFile) -and
               $running.Count -lt $Workers -and ($Want -le 0 -or $launched -lt $Want)) {
            $launched++
            $runSeed = $Seed + $launched
            $thisMap = MapFor $launched
            # Build the argument ARRAY first. Appending inline after
            # -ArgumentList mis-parses and Start-Process silently launches
            # with nothing usable: a run once did 12 launches in 0 minutes.
            $thisPlayers = PlayersFor $thisMap
            $argList = @(
                "-batchmode", "-nographics", "-twbHeadless",
                "-twbPlayers", $thisPlayers, "-twbLimit", $Limit,
                "-twbSpeed", $Speed, "-twbSeed", $runSeed,
                "-twbMap", $thisMap
            )
            if ($Rich) { $argList += "-twbRich" }
            if ($Trace) {
                # A LONG MATCH NEEDS A LONGER PERIOD. MapTrace's 400 MB cap
                # stops the file rather than truncating the match, so the
                # sample period is scaled off the ceiling -- and an UNLIMITED
                # match is the longest of all, so it takes the coarser period.
                $tp = if ($Limit -le 0 -or $Limit -gt 3600) { 2 } else { 1 }
                $argList += @("-twbTrace", "-twbTracePeriod", $tp)
            }
            $p = Start-Process -FilePath $exePath -PassThru -ArgumentList $argList
            $running[$p.Id] = @{ Proc = $p; Seed = $runSeed; Map = $thisMap; Start = Get-Date }
            Write-Host ("  start seed {0} {1} x{2}  ({3} in flight, {4} launched)" -f $runSeed, $thisMap, $thisPlayers, $running.Count, $launched)
            Start-Sleep -Milliseconds 1200   # stagger so two workers never claim a slot in the same instant
        }

        Start-Sleep -Seconds 5
    }

    $mins = [int]((Get-Date) - $started).TotalMinutes
    Banner ("`n{0} launched / {1} ok / {2} failed / {3} killed in {4} min" -f $launched, $ok, $failed, $killed, $mins)
    return @{ ok = $ok; failed = $failed; killed = $killed; launched = $launched }
}

# ═══════════════════════════════════════════════════════════════════════════
# MP — one lockstep session of P peers, then the per-tick fork diff
# ═══════════════════════════════════════════════════════════════════════════
function Invoke-Mp {
    param([int]$Index, [int]$RunSeed, [string]$ThisMap)

    $ageNote = if ($Age -gt 0) { ", Age $Age start" } else { "" }
    if ($Rich) { $ageNote += ", rich" }
    if ($Warm -gt 0) { $ageNote += " (warm-up ${Warm}s first)" }
    Banner ("mp match {0}: {1} peers, {2} factions, seed {3}, {4}s on {5}{6}" -f `
        $Index, $Peers, $Factions, $RunSeed, $Limit, $ThisMap, $ageNote)

    # Stale verdicts must not shadow this run's.
    Get-ChildItem $logRoot -Filter "MpVerdict_p*.txt" -ErrorAction SilentlyContinue | Remove-Item -Force

    $procs = @()
    for ($i = 0; $i -lt $Peers; $i++) {
        $argList = @(
            "-batchmode", "-nographics", "-twbMp",
            "-twbMpPeer", $i, "-twbMpPeers", $Peers,
            "-twbMpPort", $BasePort,
            "-twbPlayers", $Factions,
            "-twbSeed", $RunSeed, "-twbLimit", $Limit,
            "-twbMap", $ThisMap,
            "-twbAge", $Age
        )
        # PS 5.1: a conditional inline element evaluates to $null when the
        # switch is off, and Start-Process refuses null ArgumentList items.
        if ($Monkey) { $argList += "-twbMpMonkey" }
        if ($Rich)   { $argList += "-twbRich" }
        # THE REPLAY FEED, HOST ONLY. MapTrace.txt is what the report's map
        # replay is drawn from: every unit's position, name and state once a
        # second, plus buildings with real footprints, the region partition
        # and every resource node. In a synchronised match every peer would
        # write a byte-identical file, so four peers buy nothing but four
        # times the disk. Peer 0 is the host and is guaranteed to exist.
        if ($i -eq 0) {
            $tp = if ($Limit -gt 3600) { 2 } else { 1 }
            $argList += @("-twbTrace", "-twbTracePeriod", $tp)
        }
        if ($Warm -gt 0) {
            $warmThis = $true
            if ($WarmPeers -ne "") { $warmThis = ($WarmPeers -split ",") -contains ([string]$i) }
            # EVERY peer gets the deadline. A peer meant to stay fresh idles
            # to it ("none") instead of booting at once: a fresh host reaches
            # world-ready in ~7 s and drops the still-warming clients as lost
            # after 15 s of silence.
            $wm = "none"
            if ($warmThis) {
                $maps = $WarmMaps -split ","
                $wm = $maps[$i % $maps.Count].Trim()
            }
            $argList += @("-twbMpWarm", $Warm, "-twbMpWarmMap", $wm)
        }
        $p = Start-Process -FilePath $exePath -PassThru -ArgumentList $argList
        $procs += $p
        Write-Host ("  peer {0} pid {1}{2}" -f $i, $p.Id, $(if ($i -eq 0) { " (host)" } else { "" }))
        Start-Sleep -Milliseconds 800   # stagger instance-slot claims
    }

    # Wall-clock guard mirrors HeadlessMp's own (limit*2 + 300) plus slack and
    # the warm-up window. -NoTimeout waits indefinitely instead.
    if ($NoTimeout -or $Limit -le 0) {
        foreach ($p in $procs) { $p.WaitForExit() }
    } else {
        $deadline = (Get-Date).AddSeconds($Limit * 2 + 420 + $Warm)
        foreach ($p in $procs) {
            $remaining = [int]($deadline - (Get-Date)).TotalMilliseconds
            if ($remaining -lt 1000) { $remaining = 1000 }
            if (-not $p.WaitForExit($remaining)) {
                try { $p.Kill() } catch {}
                Write-Host "  KILLED a wedged peer (pid $($p.Id))" -ForegroundColor Red
            }
        }
    }

    $codes = $procs | ForEach-Object { $_.ExitCode }
    Write-Host ("  exit codes: {0}" -f ($codes -join ", "))

    # PREFER THE VERDICT FILES over raw exit codes: a peer that logs its
    # verdict and then crashes in Unity's teardown otherwise fails a match
    # every checksum agreed on. The verdict is written BEFORE quit, so it is
    # the truth about the MATCH; the exit code only about the shutdown.
    for ($i = 0; $i -lt $Peers; $i++) {
        $vf = Join-Path $logRoot ("MpVerdict_p{0}.txt" -f $i)
        if (Test-Path $vf) {
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

    # PER-TICK CROSS-PEER CHECK, always. The wire only carries a checksum
    # every 30 ticks, but every peer WRITES one per tick; a fork that heals
    # inside the interval, or one after the last SYNC, exits 0 and would
    # otherwise be recorded as a clean run.
    $diffScript = Join-Path $PSScriptRoot "mp-diff.ps1"
    $forkFound = $false
    if (Test-Path $diffScript) {
        & $diffScript -LogRoot $logRoot | Out-Host
        if ($LASTEXITCODE -eq 42) { $forkFound = $true }
    }

    if (($codes -contains 42) -or $forkFound) {
        if ($forkFound -and -not ($codes -contains 42)) {
            Write-Host "  DESYNC found by the PER-TICK diff only - the 30-tick SYNC cadence missed it." -ForegroundColor Red
        }
        Write-Host "  DESYNC - evidence in the newest match folders:" -ForegroundColor Red
        Get-ChildItem $logRoot -Directory |
            Sort-Object LastWriteTime -Descending | Select-Object -First $Peers |
            ForEach-Object {
                $d = Get-ChildItem $_.FullName -Filter "Desync_*" -ErrorAction SilentlyContinue
                Write-Host ("    {0}  ({1} desync file(s))" -f $_.FullName, @($d).Count)
            }
        return "desync"
    }
    if (($codes | Where-Object { $_ -ne 0 }).Count -gt 0) {
        Write-Host "  FAILED (non-desync): a peer exited abnormally" -ForegroundColor Yellow
        return "failed"
    }
    Write-Host "  OK - full run, every checksum agreed" -ForegroundColor Green
    return "ok"
}

# ═══════════════════════════════════════════════════════════════════════════
# Dispatch
# ═══════════════════════════════════════════════════════════════════════════
Write-Host ("exe   : {0}" -f $exePath)
Write-Host ("logs  : {0}" -f $logRoot)
Write-Host ("stop  : {0}" -f $StopFile)
if (Test-Path $StopFile) { Remove-Item $StopFile -Force }

switch ($Mode) {
    "single" {
        $r = Invoke-Single -Want $Matches -Until ([datetime]::MaxValue)
        Banner "Report: python tools/twb-report.py `"$logRoot`" --out twb-report.html"
        exit $(if ($r.failed -gt 0 -or $r.killed -gt 0) { 1 } else { 0 })
    }

    "mp" {
        $ok = 0; $desyncs = 0; $failed = 0
        for ($m = 0; $m -lt [Math]::Max(1, $Matches); $m++) {
            if (Test-Path $StopFile) { break }
            switch (Invoke-Mp -Index ($m + 1) -RunSeed ($Seed + $m) -ThisMap (MapFor $m)) {
                "ok"     { $ok++ }
                "desync" { $desyncs++ }
                default  { $failed++ }
            }
        }
        Write-Host ""
        Write-Host ("{0} matches: {1} ok / {2} DESYNC / {3} failed" -f ($ok + $desyncs + $failed), $ok, $desyncs, $failed) `
            -ForegroundColor $(if ($desyncs -gt 0) { "Red" } elseif ($failed -gt 0) { "Yellow" } else { "Green" })
        Banner "Report: python tools/twb-report.py `"$logRoot`" --out twb-report.html"
        exit $(if ($desyncs -gt 0) { 42 } elseif ($failed -gt 0) { 1 } else { 0 })
    }

    "loop" {
        # A search that runs the same match over and over only ever proves
        # that one match is clean. Rotate the axes that have actually
        # produced forks: map, warm-up residue, the command monkey, seed.
        $cycle = 0
        while (-not (Test-Path $StopFile)) {
            $cycle++
            $thisMap = MapFor $cycle
            $script:Monkey = [bool](($cycle + $Phase) % 2)
            $script:Warm = if ((($cycle + $Phase) % 4) -ge 2) { 60 } else { 0 }
            Banner ("cycle {0}: map {1}, monkey {2}, warm {3}s" -f $cycle, $thisMap, $Monkey, $Warm)
            if ($Peers -gt 1 -and $Workers -le 1) {
                Invoke-Mp -Index $cycle -RunSeed ($Seed + $cycle * 17) -ThisMap $thisMap | Out-Null
            } else {
                Invoke-Single -Want $Workers -Until ([datetime]::MaxValue) | Out-Null
            }
        }
        Banner "stop file seen; loop ended after $cycle cycle(s)"
        exit 0
    }
}
