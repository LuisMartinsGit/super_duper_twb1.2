# The headless harness

Three files. One runs matches, one joins what they leave behind, one draws it.

| | |
|---|---|
| `twb-run.ps1` | runs matches — single-player batches, lockstep multiplayer, or a forever-loop |
| `twb-match-data.py` | every artefact of every run → one JSON, one record per match |
| `twb-report.py` | that JSON → the Muster Rolls page: a folder (`--folder`) or one self-contained HTML file (`--out`), or `--text` for the console |

`twb-report-page.html` is the page template. `twb-report.py` drops the data
into it at the `/*__TWB_DATA__*/` marker.

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
# a folder: index.html + matches/<key>.json + maps/<map>.png  (publish this)
python tools/twb-report.py "Build/Current/logs" --folder muster-site
# one self-contained file, for opening straight from disk
python tools/twb-report.py "Build/Current/logs" --out twb-report.html
python tools/twb-report.py "Build/Current/logs" --text     # console roll-up
```

**Two shapes of the same page.**

* `--folder DIR` writes `index.html` (the page, the run ledger and a light
  summary per match), one `matches/<key>.json` per match with its full
  record, and the map pictures as `maps/<map>.png`. The page `fetch()`es a
  match's file when it is selected, with a loading state. Nothing is shared
  between matches, so nothing is squeezed between them. A browser will not
  `fetch()` local files from a `file://` page, so view it over http
  (`python -m http.server` in the folder) or publish it — an artifact takes
  16 MB per file, 255 files and 64 MB per version, and the builder prints
  every file's size and warns past those limits. A match file over 15 MB has
  its tracks re-simplified at a larger tolerance until it fits, and says so.
* `--out FILE` (or `--inline`) embeds everything into one HTML file that opens
  from disk. One file has one ceiling (14 MB), so when the page is over it the
  replay tolerance is raised across all matches; only past the last step are
  whole matches dropped, oldest first, and the page lists them.

**The replay is a track per unit (2026-10-03).** Every unit in `MapTrace.txt`
is carried — none is sampled away. Each unit's 1 Hz samples are simplified
with a time-aware Douglas-Peucker: a sample is dropped only if linear
interpolation in time between the kept keyframes passes within 0.75 m of it
(`--track-tol` on the extractor), and every sample where the unit's flags
(moving / in formation / fighting) change is kept, as are its first and last
(spawn and death). Per match the data is a type dictionary plus, per unit,
`[typeIndex, factionIndex, codes]` where `codes` are integer triples
`(frameDelta*8 + flags, dx, dz)` in decimetres, the first absolute, against
`ft` — the frame times in deciseconds, delta-encoded. The page unpacks this
once into typed arrays and draws each unit by interpolating along its own
keyframes, hidden before its first and after its last; the live counts are
the units alive at that instant, and formation glyphs at whole-map zoom are
grouped on the page from every live unit. An hour-long match is 1.5–6 MB.
`?m=&t=&z=&cx=&cz=` deep-links a moment, `&play=8` starts playback, and
`&debug=1` overlays drawn-against-alive counts and the draw cost.

**Territories are the game's own partition (2026-10-04).** The `R` lines are
only seeds; the ground the game plays on is `RegionMap.RegionAt` (authored
outlines with their sliver tolerance, warped Voronoi where a region has none,
no territory on Water / Mountain / Obstacle regions). `MapTrace.txt` now
carries that partition and its owners, and the page tints each territory by
its owner (the curse in its purple, unowned untinted) with crisp borders —
bright where the owners differ — dashed red while contested, and the meter
(`Blue 60%`) under the name while someone is filling it. `TER` / `T` toggles
the layer; a trace from before these lines draws no territory layer.

| Line | When | Fields |
|---|---|---|
| `TG cell x0 z0 w h` | once, after `R` | raster of `RegionAt` at cell centres: cell size (m; 2, doubled until the map is at most 512 cells across), south-west corner on the build grid, size in cells |
| `TR j id:n id:n ...` | once per row | row `j` (0 = south), run-length encoded west to east; id `-1` = no territory |
| `TO t idx owner` | first check, then on change | `TerritoryOwnership.OwnerOf`: faction name, `Border` (curse) or `-`; read when `TerritoryOwnership.Version` moves |
| `TM t idx holder pct contested` | at a sample, on change | the ownership meter: who fills it, 0..100, 1 if hostiles froze it; written on a holder / contested change, a 10-point step, or reaching 0 / 100 |

In the match JSON this is `terr = {cell, x0, z0, w, h, rows, own, meter}`:
`rows` stay run-length encoded (`[id, n, id, n, ...]` per row), `own` and
`meter` are the change events as written.

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
| `Metrics_Income.csv` | per sample (since 2026-10-04): `t,faction,flow,source,supplies,iron,veilstone,veilsteel` — what moved in the period ENDING at `t`. `flow=in` is GROSS income by source (`emptySlot`, `gatherersHut`, `mine`, `veilstoneMine`, `fortressLevel` = the capital-level x2/x4 share of territory yield, `capital`, `buildingPassive`, `trade`, `vault`, `curseKill`, `loot`, `refund`, `grant`, `other`); `flow=out` is spending by category (`units`, `buildings`, `upgrades`, `research`, `ageUp`, `trade`, `repair`, `religion`, `vault`, `overflow` = clamped by the 100k bank cap, `other`). `untracked` (either flow) is the bank delta the ledger did not see — a direct bank write. Only non-zero rows. Fed by `Economy/EconomyLedger.cs`, observation only |

plus `Lockstep.log` (a checksum row per tick, per peer), `Console.log`,
`Perf.log`, `AI_<colour>.log` (the AI's own account of itself) and, when
`-Trace` is on, `MapTrace.txt` — the per-second replay feed with unit
identity that the map replay is actually drawn from (as per-unit tracks; see
"Reading them").
