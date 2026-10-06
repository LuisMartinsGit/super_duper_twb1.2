# The Score

*2026-10-05.* One number per faction that says how the match is going for
it, for the post-match screen, the stats board and the Muster Rolls viewer.
It is **derived only**: nothing in the simulation reads a score back, so it
is never part of the lockstep checksum and can be retuned freely.

## The rule

The Score is the sum of three parts, each a plain weighted count of things
the faction has done. The weights live on `MatchScoreSystem.asset`
(`Assets/Scripts/Systems/Core/`, read by `MatchScoreSystem`); this page
states the shape, not the numbers.

| Part | Counts | Why |
|---|---|---|
| **Economy** | resources **earned** over the match (every ledger income source but refunds and the untracked residual), weighted per resource, plus the current weighted **income per minute** | earned is the economy that happened; the rate shows a growing economy before it has paid |
| **Strategy** | **territories** held, **Fortresses** standing (not under construction), **techs** researched, **building levels** reached (sum of every building's level), and an **age-up bonus** that starts at a maximum and loses a fixed amount for every minute the age-up took (never below zero; nothing before the age-up) | territory is the game's economy, Fortresses lock it, levels and techs are the long game, and an early age-up is the opening done right |
| **Military** | **kills** and **razed buildings**, together scaled by the **kill/death ratio** clamped to a range | volume times quality: a side that trades badly earns half for the same kills, one that trades well earns double |

`Score = Economy + Strategy + Military`. A faction with nothing earned,
nothing held and nothing fought is not in the match and has no row.

## Where it shows

- **In the game:** `MatchScoreSystem` samples every `sampleIntervalSeconds`
  of sim time and publishes to `MatchScore` (`Core/Diagnostics/`), which the
  development stats board draws as `Score (eco/strat/mil)  K/D`.
- **In the logs:** `Metrics_Score.csv` (one row per faction per sample) and
  a `Score :` line in `Summary.txt`.
- **In the viewer:** Muster Rolls shows a standings table ranked by final
  Score with its three parts and K/D, and Score over time.

## What feeds it

- Kills, deaths and razed buildings come from `DeathSystem` as events
  (`MatchScore.NoteUnitDeath` / `NoteBuildingRazed`), credited to the faction
  that last damaged the victim when that is known.
- Earned income is `EconomyLedger`'s **lifetime** income, which the ledger
  keeps whether or not the match metrics are recording.
- Territories are `TerritoryOwnership`, techs `FactionResearchState`, levels
  `BuildingUpgradeState`, the age-up `FactionEra`.
