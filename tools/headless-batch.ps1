<#
.SYNOPSIS
    Run headless AI-only matches in a parallel worker pool until a deadline,
    and collect their metrics.

.DESCRIPTION
    Matches are independent processes, so they parallelise cleanly. Measured on
    this machine: one match costs ~13% CPU and 1.13 GB, against 16 logical
    cores and ~16 GB free -- so six at a time fits with real headroom and turns
    a three-hour sequential batch into roughly forty minutes.

    NO CODE CHANGE IS NEEDED for concurrency. LogPaths already claims a
    per-process instance slot via a .instance<N>.lock file, and
    MatchLogSession.Begin appends that slot to the folder name, so workers
    write to <stamp>_SunderedCrown, <stamp>_SunderedCrown-2, and so on. That
    machinery was built for the multiplayer testbed and happens to be exactly
    what a worker pool needs.

    ACCELERATION HAS A CEILING, AND CONTENTION LOWERS IT. Everything integrates
    on deltaTime -- steering, arrival, separation, the formation wheel -- so a
    frame stretched by CPU contention is a coarser simulation, not a faster
    one. Six workers measured ~2.9x each in isolation; expect less under load.
    If parallel results diverge from sequential ones, fewer workers is the fix,
    not more speed.

.PARAMETER DeadlineMin
    Stop LAUNCHING new matches after this many minutes. Runs already in flight
    are allowed to finish, so the real end is up to one match later.

.EXAMPLE
    .\tools\headless-batch.ps1 -Exe ".\Build\Headless\The Waning Border.exe" `
        -Workers 6 -DeadlineMin 170 -Limit 1800
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Workers = 6,
    [int]$DeadlineMin = 170,
    [int]$MaxRuns = 500,
    [int]$Players = 4,
    [int]$Limit = 1800,      # simulated seconds per match
    [int]$Speed = 3,
    [int]$Seed = 20260900,
    [int]$TimeoutMin = 40,   # wall-clock guard per run, generous under contention
    [switch]$Rich,           # pin every faction at the resource cap (diagnostic)
    [string]$Map = ""        # scene name for -twbMap (default: first Build Settings map)
)

if (-not (Test-Path $Exe)) { Write-Error "Player not found: $Exe"; exit 1 }
$exePath = (Resolve-Path $Exe).Path
$logRoot = Join-Path (Split-Path -Parent $exePath) "logs"

$started  = Get-Date
$deadline = $started.AddMinutes($DeadlineMin)
$running  = @{}          # pid -> @{ Proc; Seed; Start }
$launched = 0; $ok = 0; $failed = 0; $killed = 0

Write-Host ("Pool: {0} workers, {1} AI, {2}s each at {3}x, launching until {4:HH:mm}" -f `
    $Workers, $Players, $Limit, $Speed, $deadline) -ForegroundColor Cyan

while ($true) {
    # Reap finished workers first, so a slot frees the moment a match ends.
    foreach ($id in @($running.Keys)) {
        $w = $running[$id]
        if ($w.Proc.HasExited) {
            $secs = [int]((Get-Date) - $w.Start).TotalSeconds
            if ($w.Proc.ExitCode -eq 0) {
                $ok++;     Write-Host ("  done  seed {0}  {1}s" -f $w.Seed, $secs) -ForegroundColor Green
            } else {
                $failed++; Write-Host ("  FAIL  seed {0}  exit {1} after {2}s" -f $w.Seed, $w.Proc.ExitCode, $secs) -ForegroundColor Yellow
            }
            $running.Remove($id)
        }
        elseif (((Get-Date) - $w.Start).TotalMinutes -ge $TimeoutMin) {
            # A hung run must not hold a worker slot for the whole batch.
            try { $w.Proc.Kill() } catch {}
            $killed++; Write-Host ("  KILL  seed {0}  timeout" -f $w.Seed) -ForegroundColor Red
            $running.Remove($id)
        }
    }

    $past = (Get-Date) -ge $deadline
    if ($past -and $running.Count -eq 0) { break }
    if ($launched -ge $MaxRuns -and $running.Count -eq 0) { break }

    while (-not $past -and $running.Count -lt $Workers -and $launched -lt $MaxRuns) {
        $launched++
        $runSeed = $Seed + $launched
        # Build the argument array FIRST. Appending to it inline after
        # -ArgumentList mis-parses and Start-Process silently launches with
        # nothing usable: a run did 12 launches in 0 minutes, 0 ok.
        $argList = @(
            "-batchmode", "-nographics", "-twbHeadless",
            "-twbPlayers", $Players, "-twbLimit", $Limit,
            "-twbSpeed", $Speed, "-twbSeed", $runSeed
        )
        if ($Rich) { $argList += "-twbRich" }
        if ($Map)  { $argList += @("-twbMap", $Map) }
        $p = Start-Process -FilePath $exePath -PassThru -ArgumentList $argList
        $running[$p.Id] = @{ Proc = $p; Seed = $runSeed; Start = Get-Date }
        Write-Host ("  start seed {0}  ({1} in flight, {2} launched)" -f $runSeed, $running.Count, $launched)
        Start-Sleep -Milliseconds 1200   # stagger so two workers never claim a slot in the same instant
    }

    Start-Sleep -Seconds 5
}

$mins = [int]((Get-Date) - $started).TotalMinutes
Write-Host ("`n{0} launched / {1} ok / {2} failed / {3} killed in {4} min" -f `
    $launched, $ok, $failed, $killed, $mins) -ForegroundColor Cyan
Write-Host "Sessions: $logRoot" -ForegroundColor Cyan
Write-Host "Aggregate: python tools/aggregate-metrics.py `"$logRoot`"" -ForegroundColor Cyan
