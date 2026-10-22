# The Score

*2026-10-22 (replaces the 2026-10-05 earned / held / fought model).* One
number per faction that says how the match is going for it, for the
post-match screen, the stats board and the Muster Rolls viewer. It is
**derived only**: nothing in the simulation reads a score back, so it is
never part of the lockstep checksum and can be retuned freely.

## The rule

**The Score is the price of what the faction owns right now.** Train a
soldier and its price joins your Score; lose it and the price leaves. Every
price is converted to points with one resource ladder, which lives on
`MatchScoreSystem.asset` (`Assets/Scripts/Systems/Core/`, read by
`MatchScoreSystem`):

| Resource | Points per unit |
|---|---|
| Supplies | 1 |
| Iron | 3 |
| Veilstone | 5 |
| Veilsteel | 10 |

`Score = Units + Buildings + Stockpile`, where

| Part | Counts | Notes |
|---|---|---|
| **Units** | every LIVING unit the faction owns, at its SO price **scaled by its stats**: `price x (combat now / combat on the SO)`, where combat is the Power number's own strength term ([Unit_Power.md](Unit_Power.md): `sqrt(offence x effective HP) x reach`, from live damage, attack cooldown, max HP, armour and range) | this is how research shows in the Score: a tech that raises a unit's damage or armour raises every such unit's value for as long as it lives, and is worth nothing once they are gone. Ranks and hero levels count the same way. Wounds do not: max HP is read, not current. A unit the Power number cannot measure (no attack, heal or build output) counts at its plain price; garrisoned men count, a unit awaiting its death animation does not |
| **Buildings** | every STANDING building, at what it was paid (`PaidBuildCost`, else the price table, else the SO), plus the faction-wide **level-ups** of each type the faction still owns (the level SOs' upgrade costs, paid once per type) | plans and sites count nothing until they finish — the money is in the ground, not in an asset; a wall counts hub by hub and module by module at the curtain price |
| **Stockpile** | the bank, at the ladder above | unspent resources are still assets |

Research has no part of its own: its price is sunk, and what it bought shows
through the units it improved. **A defeated faction scores 0** — the moment
the elimination record names it, every part reads zero, whatever it still
has on the map.

Kills, deaths, the kill/death ratio, razed buildings and the tech count are
still counted and shown (stats board, Muster Rolls, `Metrics_Score.csv`) but
**do not feed the Score**. The earlier model paid every kill the same,
credited a tower's kills to its owner and multiplied the lot by the
kill/death ratio, so a walled-in faction farming curse waves outscored the
faction that held the map; that is what this replaces.

## Where it shows

- **In the game:** `MatchScoreSystem` samples every `sampleIntervalSeconds`
  of sim time and publishes to `MatchScore` (`Core/Diagnostics/`), which the
  development stats board draws as `Score (units/bld/stock)  K/D`.
- **On the HUD (2026-10-11):** every player's Score in the top-right corner,
  highest first, one line each: `<player name>: <score>`. A human is named by
  their player name, an AI as `AI <Difficulty>` (its lobby difficulty); the
  score is bold, in the player's colour. `ScoreBoardHUD`
  (`UI/Ingame/Panels/`, placement in `ScoreBoardHUD.asset`).
- **In the logs:** `Metrics_Score.csv` (one row per faction per sample) and
  a `Score :` line in `Summary.txt`.
- **In the viewer:** Muster Rolls shows a standings table ranked by final
  Score with its three parts and K/D, and Score over time.

## What feeds it

- Unit prices are the unit SOs (`UnitTypeId` names the SO); the strength
  ratio is `UnitPower.CombatOf` on the entity's `Damage`, `AttackCooldown`,
  `Health.Max`, `Defense` and `ArcherState.MaxRange` (the components the tech
  effects write) over the same formula on the SO. Building prices are
  `BuildCosts.TryGetPaid` (what the executor charged, the table or the SO),
  wall modules `CommandRouter.WallModuleCost`; level-ups are the
  `BuildingLevelDefSO` upgrade costs for every level above 1 in
  `FactionBuildingLevels`.
- Elimination is the `EliminatedFactionRecord` buffer on the match verdict
  (`EliminationSystem`).
- Kills, deaths and razed buildings come from `DeathSystem` as events
  (`MatchScore.NoteUnitDeath` / `NoteBuildingRazed`), credited to the faction
  that last damaged the victim when that is known.
