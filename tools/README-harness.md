# The headless harness

Three files. One runs matches, one joins what they leave behind, one draws it.

| | |
|---|---|
| `twb-run.ps1` | runs matches — single-player batches, lockstep multiplayer, or a forever-loop |
| `twb-match-data.py` | every artefact of every run → one JSON, one record per match |
| `twb-report.py` | that JSON → one self-contained HTML page (or `--text` for the console) |

`twb-report-page.html` is the page template. `twb-report.py` drops the data
into it at the `/*__TWB_DATA__*/` marker, so the result carries its own data
and opens anywhere with no server behind it.

## Running matches

```powershell
# six AI matches at a time, nothing killed for taking too long
.\tools\twb-run.ps1 -Exe ".\Build\Current\The Waning Border.exe" `
    -Mode single -Workers 6 -Matches 6 -NoTimeout

# three lockstep matches, four peers each, fork-diffed per tick
.\tools\twb-run.ps1 -Exe ".\Build\Current\The Waning Border.exe" `
    -Mode mp -Peers 4 -Matches 3 -Limit 900

# forever, rotating map / seed / warm-up / monkey until the stop file appears
.\tools\twb-run.ps1 -Exe ".\Build\Current\The Waning Border.exe" `
    -Mode loop -Workers 6 -NoTimeout -StopFile logs\STOP
```

**Matches are independent processes, so they parallelise cleanly.** No code
change is needed for concurrency: `LogPaths` claims a per-process instance
slot via a `.instance<N>.lock` file and `MatchLogSession.Begin` appends that
slot to the folder name, so six workers write to `<stamp>_Veilmarch`,
`<stamp>_Veilmarch-2` … and never collide.

Measured on this machine: one match is ~13 % CPU and 1.13 GB against 16
logical cores and ~16 GB free, so **six at a time fits with real headroom**.

**Acceleration has a ceiling, and contention lowers it.** Everything
integrates on `deltaTime` — steering, arrival, separation, the formation
wheel — so a frame stretched by CPU contention is a *coarser* simulation, not
a faster one. `-Speed 3` holds; past 3–4× you are measuring the accelerant. If
parallel results diverge from sequential ones, fewer workers is the fix, not
more speed.

**`-NoTimeout` means no match is ever killed for taking too long.** Without it
a worker that outlives `-TimeoutMin` is killed so it cannot hold a slot for
the whole batch. With it, only the match's own `-Limit` (simulated seconds)
ends a match — which is what you want when the question is "how does a real
game end" rather than "how many can I get through".

## Reading them

```powershell
python tools/twb-report.py "Build/Current/logs" --out twb-report.html
python tools/twb-report.py "Build/Current/logs" --text     # console roll-up
```

Pass as many log roots as you like; they are joined on the match. Peers of one
lockstep match are folded into ONE record — a four-peer match is one match
seen four times — matched by map name and a start time within two minutes.

The page carries: the run ledger with every match's desync verdict, the map
replay, army composition, standing buildings and where they were built,
research taken per faction, the economy series, kills and deaths per minute,
the curse's story, and how each faction finished.

## What was here before

Nine files did this work and overlapped badly. They are gone (2026-09-24):

| Deleted | Why |
|---|---|
| `headless-batch.ps1` | single-player pool → `twb-run.ps1 -Mode single` |
| `mp-batch.ps1` | lockstep peers + fork diff → `twb-run.ps1 -Mode mp` |
| `twb-hunt-loop.ps1` | rotating forever-loop → `twb-run.ps1 -Mode loop` |
| `aggregate-metrics.py` | console batch economy → `twb-report.py --text` |
| `batch-dashboard.py` | live batch HTML → the one page |
| `batch-report.py` | finished batch HTML → the one page |
| `twb-hunt-page.html` | second page template for the same data |
| `twb-hunt-refresh.sh` | a hard-coded list of log roots → arguments |
| `rebuild-lanes.sh` | rebuild-preserving-logs, one install pair only |

All three reports read the same CSVs out of the same folders and drew
overlapping panels; the three runners shared one worker pool, one argument
vocabulary and one idea of a finished match, and had drifted apart at all
three.

## What is NOT part of this, on purpose

| | |
|---|---|
| `mp-diff.ps1` | the per-tick cross-peer fork diff. A component — `twb-run.ps1 -Mode mp` calls it after every match |
| `mp-trace-diff.py` | diffs two peers' `Desync_*_trace.log` files once a fork is found |
| `mp-selftest.ps1` | proves the detector can still detect. The pass condition is a DESYNC |
| `mp-testbed.ps1` | two **windowed** instances for a human to play against themselves |
| `fetch-logs.ps1` | pulls testers' uploaded logs out of R2 |

## Where the data comes from

`MatchMetrics` writes these per match, into `logs/<session>/`:

| File | What |
|---|---|
| `Metrics_Faction.csv` | per sample: population, bank, territories, unit and building totals |
| `Metrics_Units.csv` | per sample: unit id → count, per faction |
| `Metrics_Buildings.csv` | per sample: building id → count, per faction |
| `Metrics_Research.csv` | end of match: every completed tech, per faction |
| `Metrics_Placement.csv` | end of match: every building's id, position and territory |
| `Metrics_Combat.csv` | end of match: kills and deaths per faction, by minute |
| `Metrics_Deaths.csv` | every death as an event — where the battles were |
| `Metrics_UnitPositions.csv` | positions, sampled — the match unfolding on the map |

plus `Lockstep.log` (a checksum row per tick, per peer), `Console.log`,
`Perf.log`, `AI_<colour>.log` (the AI's own account of itself) and, when
`-Trace` is on, `MapTrace.txt` — the per-second replay feed with unit
identity that the map replay is actually drawn from.
