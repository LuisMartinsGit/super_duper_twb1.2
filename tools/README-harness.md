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

### AI difficulty and run labels (single-player batches)

```powershell
# every Red AI at Expert, everyone else at the default (Normal)
.\tools\twb-run.ps1 -Exe ".\Build\Current\The Waning Border.exe" `
    -Mode single -Matches 6 -FactionDifficulty "Red=Expert" -Label "Red = Expert"

# every AI at Expert
.\tools\twb-run.ps1 -Exe ".\Build\Current\The Waning Border.exe" `
    -Mode single -Matches 6 -Difficulty Expert -Label "All Expert"

# combined: everyone Hard except Red Expert and Blue Easy
.\tools\twb-run.ps1 -Exe ".\Build\Current\The Waning Border.exe" `
    -Mode single -Difficulty Hard -FactionDifficulty "Red=Expert,Blue=Easy" -Label "Hard, Red up, Blue down"
```

| runner | game flag (`HeadlessBatch`) | meaning |
|---|---|---|
| `-Difficulty <L>` | `-twbDifficulty <L>` | every AI slot at `Easy`, `Normal`, `Hard` or `Expert` |
| `-FactionDifficulty "F=L,F=L"` | `-twbFactionDifficulty F=L,F=L` | per colour (Blue, Red, Green, Yellow, Purple, Orange, Teal, White); overrides `-Difficulty` for the colours it names |
| `-Label "<text>"` | `-twbLabel "<text>"` | free-text run label |

Omitted, nothing changes: every AI is Normal and there is no label. The
difficulty is written to the same lobby-slot field the skirmish lobby sets,
so `AIBootstrap` reads it exactly as for a hand-started match. A bad level or
a colour that is not an AI slot in that match is logged as an error and
skipped, never guessed.

The game writes the label and the **resolved** roster (difficulty plus the
personality `AIBootstrap` will actually build, colour default included) into
the match header in `Console.log` and into `Summary.txt`:

```
Label       : Red = Expert
AI          : Blue Normal Turtle, Red Expert Rush, Green Normal Economic, Yellow Normal TechBoom
```

The Muster Rolls page puts `[label]` in front of the map name in the run
picker, the header eyebrow and the ledger, and shows each faction's
difficulty and personality beside its swatch. Logs from before the flags
existed simply show no label.

These flags are read by the single-player batch only (`-Mode single`, and
`-Mode loop` when it runs single); lockstep matches (`HeadlessMp`) ignore
them. The runner quotes every value it forwards, so a label with spaces
survives PowerShell 5.1's `Start-Process -ArgumentList`, which joins items
with bare spaces.

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

**The score (2026-10-05, [docs/Design/Score.md](../docs/Design/Score.md)).**
The game writes `Metrics_Score.csv` every 15 s sample, one row per faction:
`t,faction,score,economy,strategy,military,earned,incomePerMin,territories,fortresses,techs,levels,kills,deaths,razed,kd`,
with `score = economy + strategy + military` and `kd = kills / max(1, deaths)`
(Summary.txt repeats the final line; the CSV is the source). The extractor
carries it as `score = {ts, f: {faction: {s, e, st, m, kd}}, final: {faction:
{...the last row}}}` — one shared time axis, thinned like every series, with
`null` where a faction had no row — plus `scoreLeader = [faction, score]`,
which also rides in the light summary and the run ledger. The page shows a
**Score** section under the header (standings ranked by final score with the
three parts, K/D, territories, Fortresses and techs, and a score-over-time
line per faction), names the leader in the spec line, and marks the
winner-by-score on each run picker tile and ledger row. A match recorded
before the file existed has no section. Live mode carries the latest row per
faction as `score.final` and the page grows the series one point per snapshot.

## Live mode: watching a match that is still running (2026-10-05)

The folder page can follow ONE running match in place, without reloading,
through the artifact `db` capability. The page subscribes to a single
document, **`live/state`**; whenever it is rewritten the page redraws.

`twb-live-snapshot.py` builds that document from the logs the match is
writing:

```powershell
python tools/twb-live-snapshot.py "Build/Headless36/logs/<stamp>_<Map>" live.json
# 2026-10-05_13-06-50_SunderedCrown  t=2051.4  179 units, 669 buildings, 4 factions  30.7 KB  (0.15 s)
```

It reads `MapTrace.txt` (every non-position line from the whole file in one
regex pass, positions from the tail only), the last `Metrics_Faction.csv` row
per faction, the `Console.log` header (label, AI roster, world extent) and
`Summary.txt` (present once the match has ended). It is safe against a file
mid-write: a last line with no newline is ignored, and while the match runs
the frame BEFORE the newest is used (the newest may be half written). A 75 MB
trace takes about a second.

The document: `matchKey` (the log folder name), `map`, `label`, `aiRoster`,
`t` (game seconds), `seq` (t in deciseconds; the page redraws only when it
changes), `wrote` (epoch ms, for "updated N s ago"), `finished`, `outcome`,
`world`, `facs`, `factions` (units, army, pop/popMax, the four banks,
territories, buildings), `utypes` + `units` (`[facIdx, x2, z2, typeIdx,
flags]`), `btypes` + `buildings` (`[facIdx, typeIdx, level, site, x2, z2, w2,
h2, yaw, gx?, gz?, gw?, gh?]`), `terrOwner`, `meter`, `terr` (the partition,
run-length rows), `regions`, `nodes`, `curse`. Coordinates are half-metre
integers. 15-50 KB in practice; a `db` document holds 256 KiB, and the script
keeps it under 200 KB by thinning units evenly (it says so in `cap` and on the
page; `--max-units N` forces a cap).

**Publishing, once:** build the folder as usual and publish `index.html`
(with `matches/` and `maps/` as its files) with `capabilities: {db: {}}`.
The default rules let anyone admitted read and Contributors and up write; to
let only editors write the feed, declare
`{db: {rules: [{path: "", read: "view", write: "admin"}]}}` instead.

**Every update (every 20-30 s while the match runs):**

1. `python tools/twb-live-snapshot.py <match-log-dir> live.json`
2. write it with the ArtifactData tool: action **`set`** (never `update` --
   `update` merges objects, so a territory that changed hands would keep its
   old owner), `collection: "live"`, `doc_id: "state"`, `file_path` =
   `live.json`, `url` = the published page, and `if_version` = the `version`
   the previous `set` returned (omit it only on the very first write, when the
   document does not exist yet; if a write is refused for a version mismatch,
   `get` the document once and retry with the version it shows).

Skip the write when the printed `t=` has not moved (the page ignores a
rewrite with the same `seq` anyway). Stop when the line ends `FINISHED`; the
page then shows ENDED. To follow another match, just write its snapshot: a
new `matchKey` replaces the live entry.

**What the page does.** With no `db` (signed out, not granted, a local file,
`python -m http.server`) or no `live/state` document, nothing changes: it is
the replay viewer. With one, a **LIVE** entry appears first in the run strip
(even when the match is not in the index yet; its map picture is taken from
`maps/<map>.png` when the folder has one) and a bar above the map shows the
badge (LIVE; STALE after 5 minutes without a write; ENDED once `Summary.txt`
exists), the game time, "updated N s ago" and the Follow toggle. The page
opens on the live match when it is running and no `?m=` deep link chose a
replay. While following, each new snapshot redraws in place: every unit at
the snapshot second with the replay's symbols and formation glyphs, every
standing building with its level pips and dashed sites, the territory tint
and meters, the standing table, composition, structures and roster; the
economy charts grow by one point per snapshot for as long as the page is
open. Pan and zoom are kept across updates. "Follow live" off freezes the
view (the bar says how far behind it is); choosing any other match is the
normal replay, and new snapshots never pull the reader away from it. The
replay-only sections (transport, waves, research, income, kills, curse
story) are hidden on the live entry.

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
| `Metrics_Score.csv` | per sample (since 2026-10-05): `t,faction,score,economy,strategy,military,earned,incomePerMin,territories,fortresses,techs,levels,kills,deaths,razed,kd` — the per-faction score and its three parts ([docs/Design/Score.md](../docs/Design/Score.md)); `score = economy + strategy + military`, `kd = kills / max(1, deaths)` |
| `Metrics_Income.csv` | per sample (since 2026-10-04): `t,faction,flow,source,supplies,iron,veilstone,veilsteel` — what moved in the period ENDING at `t`. `flow=in` is GROSS income by source (`emptySlot`, `gatherersHut`, `mine`, `veilstoneMine`, `fortressLevel` = the capital-level x2/x4 share of territory yield, `capital`, `buildingPassive`, `trade`, `vault`, `curseKill`, `loot`, `refund`, `grant`, `other`); `flow=out` is spending by category (`units`, `buildings`, `upgrades`, `research`, `ageUp`, `trade`, `repair`, `religion`, `vault`, `overflow` = clamped by the 100k bank cap, `other`). `untracked` (either flow) is the bank delta the ledger did not see — a direct bank write. Only non-zero rows. Fed by `Economy/EconomyLedger.cs`, observation only |

plus `Lockstep.log` (a checksum row per tick, per peer), `Console.log`,
`Perf.log`, `AI_<colour>.log` (the AI's own account of itself) and, when
`-Trace` is on, `MapTrace.txt` — the per-second replay feed with unit
identity that the map replay is actually drawn from (as per-unit tracks; see
"Reading them").
