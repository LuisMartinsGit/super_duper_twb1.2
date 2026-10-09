# The Muster Rolls — post-game screen

*2026-10-08.* The match report that used to exist only as a browser page
(`tools/twb-report-page.html`, fed by `tools/twb-match-data.py` from the
log files a **headless** batch run writes) is now a screen in the game,
shown after **every** match. The browser page stays as the tool for batch
runs; this page describes the in-game one.

## What it shows

Opened from the victory / defeat panel's **Muster Rolls** button (or by any
code through `MusterRollsPanel.Open()`, which is how a replay that ends opens
it). Three tabs; **Close** returns to whatever was underneath.

| Tab | Shows |
|---|---|
| **Standings** | one row per faction ranked by final Score: Score, Economy, Strategy, Military (docs/Design/Score.md), K/D, kills, deaths, territories, Fortresses, techs; the local player is marked, and the winner; below it the **Score over time** |
| **Charts** | Economy / Strategy / Military score, K/D, the four banks (supplies, iron, veilstone, veilsteel), army units, population, buildings and territories over match time, one line per faction in its player colour; clicking a faction in the legend hides or shows it on every chart |
| **Map** | the recorded match replayed from above: the map's lobby picture, territories tinted by their owner at the playhead (the curse in purple), resource nodes, buildings on their real footprints (sites translucent, level pips), units as class symbols (melee circle, ranged triangle, cavalry diamond, siege square, worker ring, hero and curse star; a fighting unit is backed in red), and a fading ring wherever a unit died. Play / Pause, the speeds on the config asset, a timeline scrubber, mouse-wheel zoom about the cursor and drag to pan. A sidebar shows the clock and each faction's units and buildings at the playhead |

The map is the post-game view: it shows everything, with no fog of war.

## Where the data comes from

`MatchRecorder` (`Assets/Scripts/Core/Diagnostics/MatchRecording/`) records
every match — player, multiplayer, replay and headless alike — **in memory**,
on **sim time** (the lockstep tick clock, else `SimClock`), so a replay of a
match produces the same record as the match itself. Nothing is written to
disk, and it only reads the world, so it cannot affect lockstep.

- **Series** — per faction, every series interval: banks, population,
  army / workers / buildings, territories, and the latest `MatchScore` row
  (Score and its parts, kills, deaths, K/D, Fortresses, techs).
- **Map** — world bounds, the lobby thumbnail, region names, the territory
  partition as the game plays it (`RegionMap.RegionAt` on the build grid)
  and every **owner change** as an event; the resource nodes.
- **Buildings** — born / completed / destroyed, faction, type, footprint,
  facing, and level changes as events.
- **Units** — born / died, class and kind (cavalry, hero, curse), and
  position samples stored **only when the unit moved or changed state**, in
  decimetres. Past the memory cap every unit keeps every other sample and
  the sampling period doubles: a long match thins, it never stops recording.
- **Deaths** — time, victim faction and position, from `DeathSystem`.

The record starts fresh each match (`MatchLifecycle.MatchEpoch`) and stops at
the moment the match is decided (`MatchLifecycle.MatchDecided`), so it shows
the match and not the board the simulation keeps running under the victory
screen. `MatchRecord.Current` is the read-only result.

## Config

| Asset | Holds |
|---|---|
| `Assets/Scripts/Core/Diagnostics/MatchRecording/MatchRecorder.asset` | series interval, position interval, the minimum move that is worth storing, the memory cap in unit samples, the territory raster's largest side |
| `Assets/GameSystems/Presentation/UI/Ingame/Panels/MusterRolls/MusterRollsPanel.asset` | replay speeds and the default one, how long a death ring lasts, the map picture's and territories' opacity, unit symbol size, maximum zoom, chart line width and decimation, the curse's colour |

Both are listed in `Resources/ComponentConfigCatalog.asset`.

## Extension point

`VictoryPanel.ExtraButtons` (an `Action<Transform>`) is raised every time the
victory panel opens with the container that extra buttons go into, between
**Muster Rolls** and **Return to Main Menu**; `VictoryPanel.AddButton` makes
a button in the panel's style. `MusterRollsPanel.Closed` is raised when the
screen is closed.
