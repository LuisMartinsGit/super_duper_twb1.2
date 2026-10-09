# The Waning Border 1.2 - Claude Code Instructions

## Project Overview
Unity 6 (6000.0.37f1) RTS game using DOTS/ECS (Entities 1.3.14) with hybrid MonoBehaviour UI.
C# source code lives in `Assets/Scripts/` organized by domain modules.

## Game-design truth source

**Two sources, split by kind (2026-10-03,
[Unification_Decisions_2026-10-03.md](docs/Design/Unification_Decisions_2026-10-03.md)):**

- **[docs/Design/](docs/Design/Overview.md) is canonical for RULES and
  REASONS** — what exists, what unlocks what, which level gates which tech or
  unit, how a mechanic behaves and why: units and buildings as roles, techs
  as effects, factions, ages, the curse, territory, religion, sects, walls,
  Runai trade lanes, Feraldis raider houses.
- **The SO assets (`Assets/GameData/TechTree/**/*.asset`) are the SINGLE
  source of game DATA** — every cost, HP, damage, armour, range, line of sight,
  speed, train / build / research / upgrade time, population, footprint,
  income rate, interest and multiplier, and every roster (`trains[]`,
  `researchAt`, `minBuildingLevel`, tags). **On a numeric conflict the SO
  wins.** There is no JSON at all (`Resources/TechTree.json` is deleted;
  sects live on `SectDefinition` SOs in `SectDatabase`), and no code-side stat
  table where an SO field exists. A missing SO or catalog entry is a loud
  load-time error, never a silent fallback.
- **Design docs must not restate stat numbers.** Where a doc needs a value it
  points at the SO, or at the calculator `tools/calculator/TechTree.html`,
  which is **generated from the SOs**: `python tools/gen_calculator.py` reads
  `Resources/TechTreeCatalog.asset` + `AbilityCatalog.asset` (Age 0 + Alanthor
  scope) and writes `tools/calculator/techtree.json` and the DEFAULT blocks in
  `TechTree.html` / `.jsx`. The page is read-only and every load starts from
  the SO data — it is a view, never a source, and is never hand edited. The
  old SO generators (`tools/gen_stat_sos.py`, `TechTreeParser`,
  `TechTreeJsonDtos`, `TechTreeSOGenerator`, the "Generate Stat SOs" menu) are
  deleted; `tools/rebuild_catalog.py` and `tools/patch_age0_buildings.py` are
  retired (they point at dead paths). Numbers a doc may keep are rule-level ones (level gates, counts,
  grid cell size, meter definitions) and named config values (they say which
  config asset holds them).

When the **code's behaviour** and a Design **rule** disagree, the rule wins and
the code is aligned through tasks in [.deft/tasks/](.deft/tasks/). When a
number disagrees, the SO wins and the doc is wrong for stating it. Do **not**
introduce new mechanics or design changes without updating the Design folder
first, and do not introduce a balance value anywhere but its SO.

| Doc | Scope |
|-----|-------|
| [docs/Design/Unification_Decisions_2026-10-03.md](docs/Design/Unification_Decisions_2026-10-03.md) | **The game-data unification (Age 0 + Alanthor), numbered decisions newest-first** — SOs are the single data source, docs stop stating numbers, the calculator is generated from the SOs; the Hall and King's Court deleted (the capital is the Shelter, which becomes the Fortress at age-up), the Smelter / Holy Scholar / Retaliatory Measures / Research_Era2 / Shrine of Ridan cut, starting army 5 Spearmen + 1 Scout + 3 Workers, Mine and Veilstone Mine Age 0 for everyone, tech gates = SO `minBuildingLevel` only, L1 free at age-up, no Temple levels, per-battalion upgrades dropped. Read it before touching Age 0 / Alanthor data |
| [docs/Design/Overview.md](docs/Design/Overview.md) | Cross-faction framing — two-age structure, movement axis, age-up transformations, per-battalion upgrades, religious-unit tier, population model, caravan-death rule, Petriarchy. (Glow economy: **superseded** — see Curse_And_Shardroot.md) |
| [docs/Design/Curse_And_Shardroot.md](docs/Design/Curse_And_Shardroot.md) | **The curse & Shardroot loop** (replaces the old Border design AND the Glow economy): N wells, per-culture verbs (destroy/pacify/purify) with 10-min holds + tempo refresh, **well-domination victory** (all N wells yours at once), veilstone-only-from-curse, the Shardroot power artifact (One-Ring model, three Shardbound Heroes, first verb on the host well claims it) |
| [docs/Design/Territory_Claims.md](docs/Design/Territory_Claims.md) | **Territory & the curse, fourth model (2026-09-29)** — one ownership meter per territory filled by standing military units (population-weighted), frozen while hostiles share it, decaying 3/s when empty and unbuilt; buildings HOLD, node buildings and Fortresses LOCK, everything collapses when ownership is lost; build only on owned ground. **No Hall** (the Fortress inherits it and is buildable). The curse claims at double weight while standing, builds destructible nodes on any resource node, cursed ground is a radius around nodes, and it hunts only the Shardroot holder. **No wells, verbs or well victory — elimination only.** **§10 (2026-10-04): NO territory cap — a faction holds what it can defend (the meter); claims open at age-up; only ground bordering Fortress-connected territory may be taken; cut-off ground wears down in ~4 min. §11: territory TYPES (Start / Normal / +iron / +veilstone / Empty / Veilstone rich (cursed) / Iron rich / Sanctum +1 RP/min) generate the nodes — scene node markers are inert.** Supersedes Regions.md §2-3 and most of Curse_And_Shardroot.md |
| [docs/Design/Veilstone_Economy.md](docs/Design/Veilstone_Economy.md) | **Where veilstone and veilsteel come from (2026-10-01)** — veilstone outcrops are Inactive / Cursed / Depleted; a held node pays nothing by itself; Feraldis and Runai mine Inactive outcrops (Feraldis fast and finite), **Alanthor never mine veilstone (iron Mines stay; their Age 0 Veilstone Mines become Trading Outposts at age-up) — the Trading Outpost (up to four per outcrop, on its N/E/S/W side slots; each further post on the same outcrop costs more to build and level, per the SO ramp; levels L1-L3; the Age 0 Veilstone Mine becomes the outcrop's first post at age-up — 2026-10-04) buys veilstone with supplies + iron or (toggled) forges veilstone into veilsteel**; veilsteel is made, never mined (no veilsteel nodes; the Alanthor Smelter is removed); **Guild Surveys are the deliberate exception** that lets Alanthor huts produce veilstone / veilsteel; income per source is the SO slot-income ladders (§5); destroying a curse node pacifies its outcrop and pays Feraldis veilsteel; the curse refills depleted outcrops. Runai Sanctuary / Feraldis raiding not yet implemented |
| [docs/Design/Planned_Buildings.md](docs/Design/Planned_Buildings.md) | **Build orders create PLANS (2026-10-01)** — a white owner-only preview, paid, with no BuildingTag/Health (invisible to enemies, no collision role, does nothing) that reserves only its owner's tiles; it becomes the real site when a worker breaks ground, cancelling and refunding any other faction's overlapping plan. The **Delete** button/key cancels plans and sites (full refund), demolishes buildings, kills units |
| [docs/Design/Religion.md](docs/Design/Religion.md) | **Religion points (2026-09-29)** — pts from curse kills (last hit is paid, escalating pts-per-RP), Tithe buys RP, Age 0 Temple for 1 RP (Shrine of Ridan cut, Litharch trains there), chapels 2 / 3 RP, powers and chapel levels bought with RP, sect units are heroes (limit 1, 1 RP). Supersedes Sects.md §3 and Overview's RP rules |
| [docs/Design/Tech_Tree.md](docs/Design/Tech_Tree.md) | At-a-glance Mermaid charts of every building, unit, and tech across Age 0 and the three cultures |
| [docs/Design/Combat_Pacing.md](docs/Design/Combat_Pacing.md) | Match pacing — the five meta beats, the counter RELATIONSHIPS (which tag each unit's `bonusVsTags` targets; the amounts are on the unit SOs), armour ordering, weight tags, the siege-only wall rule |
| [docs/Design/Replays_And_Saves.md](docs/Design/Replays_And_Saves.md) | **Replays and saved games (2026-10-08)** — single-player skirmishes run through **solo lockstep** (the multiplayer driver with no sockets: every order is a command one tick ahead, fixed 30 Hz step); every lockstep match records a replay (`Replays/`, `.twbr`: header + every command + a state hash per second, self-verifying on playback); a saved game is a SNAPSHOT (`Saves/`, `.twbsave` zip: the ECS world via SerializeUtility + statics and every sim system's fields by reflection + the replay so far) and loading restores only the snapshot, never re-simulating; a resumed game's replay embeds that snapshot (`snap` line) so watching it reproduces the load. `GameSettings.UsesLockstep` (not `IsMultiplayer`) decides whether an order must travel as a command; `GameSettings.IsSpectating` (not `IsObserver`) is the presentation's spectator test. Build-bound. Headless `-twbReplay <file>` verifies a replay (exit 0 / 42) |
| [docs/Design/Muster_Rolls_PostGame.md](docs/Design/Muster_Rolls_PostGame.md) | **The post-game Muster Rolls screen (2026-10-08)** — standings, charts and the animated map of the match, opened from the end-of-match panel; recorded in memory for every match and replay on sim time by `MatchRecorder` |
| [docs/Design/Alpha_Build.md](docs/Design/Alpha_Build.md) | **The alpha test build** — which menu entries the shipped player hides (Campaign / Multiplayer / Scenarios / Load Game) and what every match records into the `logs` folder beside the exe |
| [docs/Design/Lobby_Setup.md](docs/Design/Lobby_Setup.md) | **Lobby colour + start-position picking** — 12-swatch colour picker (taken colours locked out), click-a-row-then-click-the-map start assignment, and the `StartIndex` contract binding `MapInfo.PlayerStarts` to `MapMarkerRegistry` (re-bake required) |
| [docs/Design/Teams.md](docs/Design/Teams.md) | **Teams** — lobby team assignment (or no team), shared line of sight, allies cannot damage each other, allied heals/buffs apply but never stack, last-team-standing victory. `Alliances.AreHostile` is the only valid hostility test |
| [docs/Design/Build_Grid.md](docs/Design/Build_Grid.md) | **The 2 m build grid** — cell size and snap rule, per-building footprints in cells (Hut = 1 cell), one-cell impassable resource/curse/tree nodes, footprint-shaped outlines, wall hubs snap / segments freeform. Supersedes every earlier footprint number |
| [docs/Design/Age_0.md](docs/Design/Age_0.md) | Pre-culture Age 0 — every building / unit / tech / cost |
| [docs/Design/Age_1_Alanthor.md](docs/Design/Age_1_Alanthor.md) | Alanthor (defense focus) Age 1 tree |
| [docs/Design/Age_1_Runai.md](docs/Design/Age_1_Runai.md) | Runai (economy / movement focus) Age 1 tree |
| [docs/Design/Age_1_Feraldis.md](docs/Design/Age_1_Feraldis.md) | Feraldis (military focus) Age 1 tree |
| [docs/Design/Regions.md](docs/Design/Regions.md) | **Regions** - the map is cut into nearest-seed (Voronoi) regions authored as RegionSeedMarkers. **Age 0 you hold ONLY your start region and can build nowhere else, and it shows no culture aesthetics; age-up is what lets you claim outward** - so title and appearance are separate halves. From Age 1 a region flips to whoever dominates it on the EXISTING influence map (0.6 claim / 0.4 release + 20 s dwell), the curse included. **The build gate now applies to EVERY culture, not just Alanthor** (Gatherer's Hut / Watch Tower exemptions kept, or expansion is impossible). Supersedes two Overview.md statements - see its §4. Every map needs regions or it has no legal build space: `Waning Border > Maps > Seed Regions For Open Scene` |
| [docs/Design/Territory_And_Nature.md](docs/Design/Territory_And_Nature.md) | **Nature regions as the territory readout** - impassable forests that change appearance with the owning influence channel (Wild / Blighted / Cultivated / Stilled / Ashen), the hysteresis + dwell rules that stop them flickering, the fog rule that keeps them from leaking influence, the Feraldis burn-down exception (the only state that changes passability), and the **author-once substitution model** - the map is built in its mint natural state and a TerrainAnalogueSet maps each natural asset to its per-owner analogue (dirt -> tiles / sandstone / ash), transferring splat weight per layer so the authored PATTERN survives; analogues are SHADER OVERLAYS (TWBTerrainOverlays.hlsl + InfluenceMaskTexture's 128² culture mask), NOT terrain layers - adding a culture look costs zero splat layers, and InfluenceTerrainPainter's runtime SetAlphamaps path is superseded. In-world borders stay lines-only per Overview.md |
| [docs/Design/Fire.md](docs/Design/Fire.md) | **Fire** — the four ground states (Fuel/Burning/Ash/Bare), slow orthogonal spread, burn DOT, the blood chain-ignition rule (one blood tile lit = every blood tile lit), ash reverting to its original terrain, and the two effect languages |
| [docs/Design/Score.md](docs/Design/Score.md) | **The Score** — one derived number per faction: Economy (resources earned + income rate) + Strategy (territories, Fortresses, techs, levels, age-up timing) + Military (kills and razed buildings scaled by the clamped K/D). Weights on `MatchScoreSystem.asset`; computed by `MatchScoreSystem`, written to `Metrics_Score.csv` and `Summary.txt`, shown on the stats board and in Muster Rolls. Never read by the simulation |
| [docs/Design/Unit_Power.md](docs/Design/Unit_Power.md) | **The Power number** — one derived statistic per unit (combat output per resource invested, ~100 = par) for comparing and balancing them. Purely computed from stats the unit already has, so it can never disagree with the SO; `UnitPower.cs` is the only implementation |
| [docs/Design/Heroes.md](docs/Design/Heroes.md) | **Heroes** — hero levels 1-10 earned from KILLS ONLY (never bought, unlike every other progression in the game), the XP curve, abilities that unlock at a level, King Lexor's **Honour thy Pledge** temporary army, and the death/revival CHOICE (Rally the Oath at a lower level for base cost vs Full Honours at the same level for a level-scaled price). Supersedes `HeroTrainLimit`'s flat +15%-per-death respawn tax |
| [docs/Design/Sects.md](docs/Design/Sects.md) | **The 12 sects** — 3 active powers (levels I-III), 1 passive, 1 unit, 1 research each; the four fixed casting radii; the adoption-timing level rule; **no chapel auras**. Supersedes the sect sections of task-063 and the shipped `SectLeverEffects` numbers. Visualization: [docs/SectReference.html](docs/SectReference.html) |
| [docs/Design/Art_Direction.md](docs/Design/Art_Direction.md) | **The look** — dusk mood, the one-tint lighting recipe (`DayNightCycle` start values), locked palettes, the emissive HDR ladder, **roofs are dark slate and the player colour lives in lantern/window glow + cloth** (supersedes `BuildingFactionColorMarker` rule 1 and the atlas-blue roofs), purple = curse / cyan = veilstone, what every ground decoration MEANS, fog-of-war as navy mist, and the four-pass implementation plan. Shipped 2026-09-18: the grade in `DayNightCycle.asset` (tune it in Play mode, it persists), Hollow Table terrain remap, interim roof darkening, `BuildingLanternLight` on large buildings, and `EmissiveLadder` (the HDR glow table every glowing visual reads) with veilstone cyan / curse purple applied; and (§6.4) the **curse veil** — cursed territories draw no border line, their edge is an aurora arch + wisps built by `CurseBarrierVfx` from `TerritoryBorderCurves.TryGetLoops`; the rest is not implemented |
| [docs/Design/Stances.md](docs/Design/Stances.md) | **Unit stances** — Aggressive (default for every unit, 2026-09-29) / Defensive / Hold Position; acquire radius per stance (LoS / reach+margin+retaliation / reach only — no 1.5× LoS bonus), chase leash on AUTO-acquired targets only (30 / 8 / none m from the guard point, then walk home), guard point = arrival point, stance survives Stop, everyone defaults Aggressive, G / D / H hotkeys, `SetStance` lockstep command. Tunables in `UnitStanceSystem.asset` |
| [docs/Design/Roads.md](docs/Design/Roads.md) | **The procedural road network** — sites (outcrops, supply spots, curse nodes, buildings), per-territory relative-neighbourhood graphs that merge only where one faction has built on both sides of a border, A*-routed roads, footprint-sized plazas, and the per-pixel look rule (earthen under curse / Age 0 / construction, culture paving on finished Age 1 ground). **Paving is network-only** — supersedes the blanket culture ground of Territory_And_Nature.md §8. Rebuilt on construction events, never per frame; `Scripts/World/Roads/` + `_TWB_RoadMask`. Shipped 2026-09-18; the Age 0 earthen network is verified on screen, the Age 1 paving path is not yet |

Player-facing UX (controls, hotkeys, AI personalities, multiplayer) lives
in [GAME_MANUAL.md](GAME_MANUAL.md). Code-level runtime reference (what
the code currently does, often pre-design-pass) lives in
[docs/Technical_Reference.md](docs/Technical_Reference.md).

## Architecture

### ECS (Data-Oriented)
- **The TechTree is inverted: age/civ FIRST, then Buildings, then the building's
  own Research and Units.** Restructured 2026-08-27. The top level is:

  ```
  TechTree/
    Age0/Buildings/<Building>/{Research,Units,Abilities}/   Fortress (the capital),
    Age0/Units/                                             Barracks, Hut, GatherersHut,
                                                            Mine, VeilstoneMine, Palisade,
                                                            Wall, TempleOfRidan,
                                                            VaultOfAlmierra, FiendstoneKeep
    Civs/<Culture>/Buildings/<Building>/{Research,Units,Abilities}/
    Civs/<Culture>/Units/<Unit>/{Abilities}/   units no building trains
    Sects/<Sect>/{Abilities,Units,Buildings}/      unchanged — see the sect branch
    Border/{LargeNode,SmallNode,Units}/            the curse; no age, no civ
    ResourceNodes/<Node>/
    Shared/{Buildings,Abilities}/                  all-building / all-ability code
  ```

  **A unit folder lives under the building that trains it**, read from that
  building's `trains[]` — never hand-placed. Where a unit has two trainers the
  first wins (`Feraldis_Berserker` is under WarHall, not Longhouse). Units no
  building trains sit in `Civs/<Culture>/Units/`.
- **Age0/ holds only what exists in Age 0** (2026-10-02). A post-age-up tech,
  level or piece of a cultured building files under that culture, even when
  the entity it acts on is an Age 0 one: the Alanthor-gated Barracks techs
  are in `Civs/Alanthor/Buildings/Garrison/Research/` (still `researchAt:
  Barracks` — the Garrison IS the Barracks).
- **Building LEVELS are assets** (2026-10-02): one `BuildingLevelDefSO` per
  level, in the culture's folder under the cultured name
  (`Civs/Alanthor/Buildings/House/House_Lvl1..3.asset` for the `Hut`), listed
  in `TechTreeCatalog.buildingLevels`. They carry the level's name, upgrade
  cost/time, stat multipliers, population, authored attack and model;
  `BuildingUpgradeConfig` reads them first and keeps its code tables only for
  cultures not yet migrated (Runai, Feraldis). The HUD shows
  `<name> - Lvl N`. docs/Design/Age_1_Alanthor.md § Building levels.
- **The capital is `Age0/Buildings/Fortress/`** (2026-10-03). The Hall is
  deleted: every faction starts with the **Shelter** (the `Fortress` SO's
  Age 0 display name), which becomes the **Fortress** automatically at
  age-up. There is no King's Court and no Town Hall; the capital's levels are
  `Civs/Alanthor/Buildings/Fortress/Fortress_Lvl1..3`. The Worker, Scout,
  Ledger and King Lexor all train there (its `trains[]`), and its techs
  (`researchAt: Fortress`) are in `Fortress/Research/` (Age 0) and
  `Civs/Alanthor/Buildings/Fortress/Research/` (Alanthor). `HallTag` survives
  as an internal component name only — do not read it as a building.
- **The Mine and Veilstone Mine are Age 0 buildings** (`Age0/Buildings/Mine/`,
  `Age0/Buildings/VeilstoneMine/`), for every culture. The Gatherer's Hut has
  no Age 0 research: the Alanthor Guild line is in
  `Civs/Alanthor/Buildings/Guild/Research/` and the Feraldis raiding line in
  `Civs/Feraldis/Buildings/RaiderCamp/Research/` (both still
  `researchAt: GatherersHut`, culture-gated). The **Archery Range is an
  Alanthor Age 1 building** (`Civs/Alanthor/Buildings/ArcheryRange/`).
  The Alanthor **Smelter is removed** (`SmelterTag` stays only for the Runai
  Foundry), and the Holy Scholar is cut.
- **The Shrine of Ridan is gone** (2026-10-02; cut by Religion.md). Its
  Litharch trains at the Temple (`Age0/Buildings/TempleOfRidan/Units/Litharch/`),
  its heal aura is `TempleHealSystem`, and its materials (used by the Temple
  and chapel prefabs) live in `TempleOfRidan/Materials/`.
- **A cultured building is the SAME entity renamed, so its folder is the
  CULTURED name.** Alanthor units trained at the Age 0 `Barracks` live under
  `Civs/Alanthor/Buildings/Garrison/Units/`, because for an Alanthor player
  that building *is* the Garrison. (The Archery Range is no longer an example:
  it is an Age 1 Alanthor building, `Civs/Alanthor/Buildings/ArcheryRange/`,
  with no cultured rename.) This is why an Age 0 building's
  `trains[]` legitimately lists `Alanthor_*` and `Feraldis_*` ids — the roster is
  culture-gated by id prefix at runtime, per
  [Age_1_Feraldis.md](docs/Design/Age_1_Feraldis.md) ("the Age 0 Barracks entity,
  renamed at age-up… the roster lives in the Barracks def's trains list").
  Do **not** "fix" that by splitting the building into per-culture ids.
- **Per-entity code is co-located with its data** in the entity's folder above:
  the factory, the entity's components file, any single-entity systems and
  visuals, next to its SO/prefab. Kept in the runtime assembly via
  `Assets/GameData/TechTree/TheWaningBorder.Runtime.asmref`.
- **Set-level code sits one level above**: culture-wide code at `Civs/<Culture>/`
  (e.g. `AlanthorCombatPassives.cs` + its system), building-wide code at
  `Civs/<Culture>/Buildings/` (e.g. `AlanthorBuildingComponents.cs`), all-building
  / all-unit code at `Shared/Buildings/` and `Age0/Units/`.
- **A component file lives with the system it was split out of.** Several
  `*Components.cs` files were lifted out of their systems "so the simulation's
  vocabulary lives in one place" and then filed under `Scripts/Components/<Culture>/`,
  a completely different tree from the system that owned them. Alanthor's three were
  reunited 2026-08-27 (`AlanthorCombatPassives`, `LayeredMoveComponents`,
  `WallGarrisonComponents`). **Feraldis (8 files), Runai, Border and Age 0 still have
  the split** under `Scripts/Components/{Buildings,Units}/` — same treatment applies
  when those cultures get their pass. Only genuinely cross-domain components stay in
  `Scripts/Components/` root.
- **The wall set is one folder, and it is an AGE 0 folder**:
  `Age0/Buildings/Wall/` holds the wall itself (SO, FBX, prefab, factory,
  tiers, curve mesh, systems), its two conversion-only forms in `Gate/` and
  `Tower/`, and the two-layer `LayeredMove*` pair. They are one BFME2-style
  hub-and-segment system — the gate and the wall tower are conversion-only
  from a wall instance and cannot be placed directly — so they are filed as a
  set, not as sibling buildings.
  **It moved out of `Civs/Alanthor/Buildings/Walls/` on 2026-09-21**: the
  wall's first level is a timber palisade every culture builds from Age 0
  (docs/Design/Age_0.md § Wooden Wall), so it is no longer Alanthor content.
  What is still Alanthor's is the LEVEL — stone at age-up, raised further by
  research at the Wall Hub — not the building. The id stays `Alanthor_Wall`
  and the factory class stays `AlanthorWall`: renaming either ripples through
  the recipe table, the name resolver and the AI, and `Wall` as a type name
  would shadow far too much.
  **2026-10-02: the palisade is its own building, `Palisade`** (its hub SO in
  `Age0/Buildings/Palisade/`), running on the SAME hub/segment/cell machinery —
  every palisade piece carries `PalisadeTag`, and `AlanthorWall.IsPalisade` is
  the one test. `Alanthor_Wall` is now the Alanthor-only Stone Wall. The two
  never join (snap, auto-segment, branch) — every hub/cell finder takes the
  kind. docs/Design/Age_1_Alanthor.md § The stone wall.
  **2026-10-02, the Stone Wall's CONTENT moved to `Civs/Alanthor/Buildings/Wall/`**
  (`WallHub.asset`, `WallSegment.asset`, `Tower/`, the `Stone/` kit prefabs
  and the `Battlements` / `ShieldedRamparts` research), because Age0/ holds
  only what exists in Age 0. The shared hub/segment CODE, the Gate (a
  palisade takes gates too), the timber art and `WallModuleArt` stay in
  `Age0/Buildings/Wall/` — one machine, two kinds of content.
  **The stone wall's levels are level SOs** (2026-10-03):
  `Civs/Alanthor/Buildings/Wall/Wall_Lvl1..3`, carrying the level name and
  the wall HP multiplier the code used to hard-code.
  `Civs/Alanthor/Buildings/Tower/` is NOT part of the set: the watch tower is
  a stand-alone building from the Age 0 hut conversion.
- **Cross-domain components** (CoreComponents, CombatComponents, etc.) stay in `Scripts/Components/`; **cross-domain systems** (Combat, Navigation, Work, Training, AI, Border) stay in `Scripts/Systems/` by domain.
- **`Scripts/<Domain>/` vs `Scripts/Systems/<Domain>/`** — **AI no longer
  appears in either** (2026-09-03): both halves were unified into
  `Assets/GameSystems/AI/`, one folder per class, each with its config asset —
  see the AI bullet below. World and Economy still appear in both places, and
  the rule for them is:
  - `Scripts/<Domain>/` holds the domain's **state, policy and helpers** used from anywhere — `AIBrain`, `TargetScorer`, `FactionResources`, `TerrainUtility`, `PassabilityGrid`.
  - `Scripts/Systems/<Domain>/` holds its **ECS systems**, plus helpers used *only* by them (`AIEndgameCommon`, `AIPivotalReserve`).

  Audited 2026-08-26: the rule held in every file — no ECS system sat in a domain folder, and no domain-wide helper sat under `Systems/`. **The AI domain was
  then deliberately unified anyway** (2026-09-03), on the view that one folder
  per class beats splitting a domain's state from its systems; the two-place
  rule above is now World and Economy only.
  **`Systems/<Domain>/` is not ECS-only**, though: `Systems/Core/VictoryConditionSystem`,
  `Systems/Research/TechEffectSystem` and `Systems/Navigation/Debug/` are
  MonoBehaviours, and `Systems/Audio/MusicManager` joined them on 2026-08-28.
  "System" here means a domain's behaviour, not the ECS base class. It looked like the same domain scattered across two folders, which is why it is written down now; splitting a domain's state from its systems is deliberate, not drift. **Abilities was the fourth such domain and no longer is** — it left `Scripts/` entirely on 2026-08-27 (next bullet but one).
- **Shared factories are DISPATCH ONLY**: `UnitFactory.cs` in `Entities/Units/` and `BuildingFactory.cs` in `Entities/Buildings/` hold the id→recipe table and the cross-entity queries; the per-entity creation code lives in that entity's GameData folder as its own static class (`Fortress.Create`, `Barracks.Create`, …, both an `EntityManager` and an `EntityCommandBuffer` overload). Adding a building = write its class in its folder, add one row to the recipe table.
- **An ability lives with whatever OWNS it**, in an `Abilities/<Ability>/`
  folder one level down — never in a shared ability pool. There is no
  `Age0/Abilities/` or `Civs/<Culture>/Abilities/` any more (flattened
  2026-08-27):
  - the **unit that casts it** — `Age0/Buildings/Fortress/Units/Scout/Abilities/{ScoutSight,UseCelestar}/`,
    `Civs/Alanthor/Units/KingLexor/Abilities/{KingsCall,LiquidCourage,VeilshiftWithdrawal,LifeCling}/`
    (an aftermath ability files under the caster of the ability that chains
    into it), `Civs/Alanthor/Units/Ledger/Abilities/{AutomateFacility,UnderAutomation}/`
  - the **building whose research grants it**, when the grant spans a whole
    roster rather than one unit — `Civs/Alanthor/Buildings/RoyalStable/Abilities/{WarHorn,FullGallop}/`,
    granted to every cavalry unit by the Royal Stable techs of the same name
  - the **sect that sells it**, which outranks both of the above —
    `Sects/Renewal/Abilities/DeployFieldHospital/`. The Litharch casts it, but
    it is the Sect of Renewal's `[RESEARCH]`, so it files under the sect and not
    under the Age 0 unit. **The building an ability conjures files with the
    ability**, not under `Buildings/`: the temporary Field Hospital's four
    `FieldHospital*.cs` live in that same folder, because it exists only as the
    ability's payload and is not the sect's `[BUILDING]` slot (that is the
    Mending Hall).

  Each folder carries one `AbilityDefSO` plus its icon/VFX-prefab slots (the
  `AbilityCatalog` code seed is the runtime fallback). Sect data — god powers
  included — lives on `SectDefinition` SOs (`SectDatabase`); there is no JSON
  anywhere (2026-10-03). The generic ability **engine** is no
  longer under `Scripts/` at all: see the next bullet.
- **`TechTree/` holds only what the player directly interacts with** — buildings,
  units, research and abilities. It is a CONTENT branch, not a code branch.
  Game systems that merely happen to act on that content live elsewhere; the
  Presentation pipeline used to sit at the TechTree top layer and was moved out
  on 2026-08-27 for exactly this reason. Do not add a system folder back under
  `TechTree/`.
- **AI is `Assets/GameSystems/AI/`, one folder per class** (2026-09-03).
  `Scripts/AI/` (state and policy) and `Scripts/Systems/AI/` (the ECS systems)
  were two halves of one domain; they are now 20 sibling folders, each holding
  `<Class>.cs` (plus its partials), `<Class>Config.cs` and `<Class>.asset` —
  the same shape `GameSystems/Presentation/Input/` uses. Namespace stays
  `TheWaningBorder.AI` and the tree compiles into Runtime through the
  `GameSystems/` asmref, so the move changed no assembly and no namespace.

  **138 tuning constants moved from code into those 13 assets** — think
  intervals, army sizes, resource reserves, radii, scoring weights. What did
  NOT move is the carve-out CLAUDE.md already had: array dimensions and loop
  resolution (`AIBudget.Categories/Resources`, `ThreatMaps.MaxFactions` and its
  fixed-point `DecayNum/Den`, `AIWallPlanner.Bearings`/`ShelterRunSamples`/
  `MaxCorridors`, `SimpleAISystem.BuildAngleSamples`) size data structures
  rather than tune behaviour, and stay `const`.

  **`AICommon/` is the AI's shared toolbox** (2026-09-03) — what SimpleAISystem
  shares with the endgame systems, one level above `AIEndgameCommon` (which is
  only what the two ENDGAME systems share). It holds the worker dispatch and
  idle-worker test, `IsUnitQueued`, `ToCost`, and `AIQueryCache` for the
  generic and runtime-typed query shapes. Every one of those was a copy-paste
  pair that had drifted; the worst had the endgame calling a worker "idle"
  while it carried an in-flight `BuildCommand`, so it re-dispatched workers
  SimpleAISystem had just sent somewhere else, in the same frame.

  **No AI code calls `EntityManager.CreateEntityQuery`.** All 104 call sites
  were per-tick, and each one permanently registers a query with the world —
  the leak `Core/CachedEntityQuery.cs` exists to prevent. They hold static
  `CachedEntityQuery` fields now (`QC_*` / `QT_*` per shape), with
  `AIQueryCache` covering the generic helpers (one query per constructed `T`)
  and the extractor's runtime-typed node query. Do not reintroduce a bare
  `CreateEntityQuery` in this tree.

  **The difficulty ladder is four assets**, not a switch (2026-09-03):
  `AISimpleDifficulty/Profiles/{Easy,Normal,Hard,Expert}.asset`, each an
  `AIDifficultyProfileSO`. They are deliberately NOT `IComponentConfig` — that
  contract is one asset per type and a designer wants one per TIER — so the
  single config `AISimpleDifficulty.asset` holds the four references and is
  what the catalog loads. `AISimpleDifficulty.GetProfile` is the only thing
  that converts the asset into the runtime struct.

  Three shapes a `const` supported that an asset field cannot, and how each is
  handled — do not "simplify" these back:
  - a **public const other classes read** becomes a `public static X => Cfg.x;`
    shim on the same class, so call sites are unchanged (8 of them).
  - a **const derived from another** (`WallMaxGapSpan = HubSpacing * 2f + 8f`)
    becomes a static property, since its input is now a runtime value.
  - a **default parameter value** must be a compile-time constant, so
    `AIEngagement.Assess(..., float radius = DefaultAssessRadius)` takes
    `float.NaN` as its literal default and resolves it in the body.

- **Nothing calls `EntityManager.CreateEntityQuery` on a repeating path.**
  `CreateEntityQuery` permanently registers a query with the world and matching
  one walks every archetype, so a call in `Update`, in an order helper, or in a
  per-unit loop bloats the registry until every later query AND every structural
  change crawls — the documented "skirmish starts smooth, sinks to 15 FPS"
  curve. 205 such call sites were migrated on 2026-09-03; the remaining 46 are
  either one-time setup (`OnCreate`/`Awake`) or genuinely disposed.

  The pattern is a `static readonly ComponentType[] QT_<shape>` beside a
  `static CachedEntityQuery QC_<shape>`, read as `QC_x.Get(em, QT_x)`. Two
  variants exist for what a plain static cannot hold: a **generic** helper
  needs one query per constructed `T`, so the cache is a nested
  `static class Foo<T>` (statics in a generic class are per-type —
  `AIQueryCache`, `CommandRouter.TaggedFaction<T>`, `BuildingFactory.FactionBuildings<T>`);
  a **runtime-typed** query is keyed (`AIQueryCache.NodeAt`).

  **Never `Dispose()` a cached query.** It is shared, so disposing kills it for
  every later caller — an `ObjectDisposedException` out of
  `ToComponentDataArray`. Sites that legitimately disposed their own
  freshly-created query were left alone rather than converted.

  An `EntityQueryDesc` with `All` + `None` is the same query as the array form
  with `ComponentType.Exclude<>()`, which is why those converted too.

- **`Assets/GameSystems/` is where a game system lives** — code that acts on
  TechTree content without being content itself. Two of them today:
  `Presentation/` and `Abilities/`.
- **The ability engine is a game system**: `Assets/GameSystems/Abilities/`
  holds the whole generic engine — `AbilityRuntimeComponents`,
  `AbilityEffectExecutor`, `AbilityDamageHooks`, `AbilityAssignment`,
  `AbilityQuery`, the two ECS systems (`AbilityAuraSystem`,
  `AbilityLifecycleSystem`) and the spell-VFX authoring layer in `Vfx/`
  (namespace `TheWaningBorder.Abilities.Vfx`). Moved out of
  `Scripts/{Abilities,Systems/Abilities}/` on 2026-08-27. The whole folder is
  one namespace, `TheWaningBorder.Abilities` — the two systems used to declare
  `TheWaningBorder.Systems.Abilities`, which matched no folder once
  `Scripts/Systems/Abilities/` was gone. **`AlanthorCombatPassiveSystem.cs` at
  `Civs/Alanthor/` still declares that dead namespace** and is the last file
  that does; it is the namespace-lies trap, not a surviving folder.
  **The ability DATA MODEL went the other way**, to
  `GameData/TechTree/Shared/Abilities/` — `AbilityCard.cs` (the card shape +
  `AbilityEffectKind`) and `AbilityCatalog.cs` (the card library and its code
  seed), next to the `AbilityDefSO` / `AbilityCatalogSO` already there. The
  split is the same one the whole tree uses: what an ability *is* is content,
  what *runs* it is a system. Adding an ability that reuses existing effect
  kinds touches only the GameData side.
- **`Assets/Scripts/` no longer holds the UI** (2026-08-28). UI and Input moved
  to `Assets/GameSystems/Presentation/`; Scripts/ keeps Core, Components,
  Systems, Entities, Data, Economy, AI, World, Multiplayer, Influence, Bootstrap
  and Editor. It is **not** "simulation only" — `Systems/Audio/MusicManager`
  and the `Systems/Navigation/Debug/` overlays are player-facing MonoBehaviours
  that live there on purpose. The line that moved was the UI, not everything the
  player perceives.
- **Rendering is a game system**: `Assets/GameSystems/Rendering/{Spawn,Buildings,Units,Border,Vfx,Procedural}/`
  holds the shared visual pipeline (PresentationSpawnSystem core,
  EntityViewManager, all-building/all-unit visual systems), in namespace
  `TheWaningBorder.Rendering`. `Assets/GameSystems/` carries its own
  `TheWaningBorder.Runtime.asmref`, so it compiles into the runtime assembly
  exactly as the TechTree branch does.
- **UI + Input are `Assets/GameSystems/Presentation/`**, the
  `TheWaningBorder.Presentation` assembly, namespaces `TheWaningBorder.UI.*` /
  `TheWaningBorder.Input`. Its own `.asmdef` sits at that folder and **overrides
  the parent `GameSystems/` asmref** — that nesting is what keeps UI+Input a
  SEPARATE assembly rather than being swallowed into Runtime, which is the only
  thing stopping simulation code from referencing the UI again.

  **The two "Presentation" things are now one.** Until 2026-08-28 the ECS visual
  pipeline sat in `GameSystems/Presentation/` under namespace
  `TheWaningBorder.Presentation`, while an ASSEMBLY of that same name lived at
  `Assets/Scripts/Presentation/` holding UI+Input — a name meaning two
  unrelated things. The ECS half was renamed folder AND namespace to
  `Rendering` (76 files) and UI+Input took the freed name. Do not reintroduce a
  `TheWaningBorder.Presentation` namespace.

  Note the namespace still does not match the folder for the **34 entity-visual
  files** that declare `TheWaningBorder.Rendering` from inside their entity's
  own `GameData/TechTree/` folder. That is the co-location rule winning over
  folder/namespace symmetry on purpose, not drift.
  Entity-specific visuals still live in that entity's TechTree folder — including
  the `PresentationSpawnSystem.<Entity>.cs` partials (Vault of Almierra,
  Alanthor Wall, Border LargeNode, and the ResourceNodes — the Smelter's went
  with the Smelter). They
  MUST stay in the runtime assembly: a partial class cannot span assemblies.
- **Shared art the presentation code paints with** lives at
  `Assets/GameData/Art/{Atlases,Placeholders}/` — the building texture atlases
  (referenced by FBX importer material remaps, not by prefabs) and the six
  `PLACEHOLDER_*.mat` materials used by the procedural placeholder visuals. Art
  belongs under `GameData/`, never under `Assets/Scripts/` or `Assets/GameSystems/`.
- **Resource nodes follow the same convention**: `Assets/GameData/TechTree/ResourceNodes/{VeilstoneOutcropping,VeilsteelDeposit,IronDeposit}/` carry each node's factory, bootstrap, map marker and visual code (the veilstone gem-cluster prefab cache lives in VeilstoneOutcropping and is shared by the well and veilsteel visuals). The branch is named `ResourceNodes`, **not** `Resources`, on purpose: a folder called `Resources` anywhere in `Assets/` is a Unity magic folder, so every asset under it would be force-included in builds and `Resources.Load`-able. Do not rename it back.
- **Every entity stat comes from the SO. Factories hold no numbers.** A factory
  reads `TechCatalog.Unit(id)` / `TechCatalog.Building(id)` — never-null
  accessors — and assigns straight from the def:

  ```csharp
  var def = TechCatalog.Unit("Spearman");
  float hp = def.hp;
  float radius = def.radius;
  ```

  **Do not reintroduce a `private const float DefaultHP = 800f` ladder, and do
  not write `if (def.hp > 0) hp = def.hp;`.** That guard is a magic number in
  disguise: it makes the SO authoritative only when it happens to be filled in,
  and a C# constant authoritative — silently — whenever it is not. 74 factories
  carried that pattern until 2026-08-27; 50 stat fields were living in code
  where no designer could find them, and the Caravan's SO had drifted a full
  rebalance behind the constants that actually shipped.
  A missing or zero stat is a DATA bug, caught loudly at load by the stat audit
  in `TechCatalog.ValidateCrossReferences()`, not a runtime branch in 74 files.
  `UnitDef` gained `radius` / `aimTime` / `healRange`; `BuildingDef` gained
  `buildTime` / `populationProvided` / `suppliesPerTick` / `suppliesInterval` /
  `maxIron` / `maxVeilstone` / `segmentHp` / `segmentLineOfSight`, so there is a
  home for every number a factory used to hold.
  Genuinely engine-side constants still belong in code — projectile aim
  handling, steering, lockstep timing, AI patrol caps. The test is whether a
  designer would ever want to tune it per entity.
- **Inspector chrome comes from an SO too. No `[Header]` / `[Tooltip]` in
  source.** A field's section label and hover text are DATA, filed in an
  `InspectorMetadataSO` at `Assets/GameData/InspectorMetadata/` (three assets,
  split by area — Scripts / GameSystems / GameData), keyed by `Type.FullName`
  plus field name. 467 attributes across 68 files were migrated out on
  2026-09-01; the same reasoning as the stat rule above applies — documentation
  a designer reads does not belong interleaved with the code, and being
  editor-only these strings no longer ship in the player build at all.

  The drawing side is `Assets/Scripts/Editor/Inspector/`, in
  `TheWaningBorder.Editor`: `MetadataInspector` registers as a catch-all
  `[CustomEditor]` for MonoBehaviour and ScriptableObject and injects the
  chrome. **The breadth is safe and deliberate** — a type with no authored
  entry falls through to `DrawDefaultInspector()`, which is exactly what Unity
  would have drawn, so vendor and package components are untouched, and
  anything with its own `[CustomEditor]` is more derived and still wins.

  Adding chrome for a new field = add a row to the SO. Nothing in the source.
  Two cases need more:
  - a nested `[Serializable]` type reached as a **list/array element** needs a
    `NestedMetadataDrawer` subclass (`NestedMetadataDrawers.cs`), because
    hand-drawing the list to reach its elements would cost the reorderable
    list UI. Four exist today.
  - a nested type held as a **single field** needs nothing — `MetadataInspector`
    recurses into it.

  `PlasterChannelDrawer` sits in its own `TWB.PlasterScenario.Editor` assembly
  rather than beside the other three, because its type pulls in
  Adobe.Substance and `TheWaningBorder.Editor` carries the release pipeline —
  if that assembly stops compiling, builds stop.

  **The one thing this loses:** an attribute moved with its field on a rename;
  an SO row does not, and nothing fails to compile when it is orphaned. Run
  `Waning Border > Inspector > Validate Metadata` after renaming a serialized
  field — it reports every entry that no longer matches code.
- **A component's VALUES come from an SO named after it, sitting beside it.**
  The chrome rule above was the first half; this is the rest. A configurable
  component carries no field initialisers at all — `public float panSpeed = 1f;`
  is the same magic number the factories were purged of, just wearing a
  MonoBehaviour. The numbers live in `<Component>.asset` in the SAME FOLDER as
  `<Component>.cs`, read through a `<Component>Config : ScriptableObject,
  IComponentConfig`.

  `CameraController` is the worked example (2026-09-01): 19 tunables moved to
  `Assets/GameSystems/Presentation/Input/CameraController.asset`, and the class
  reads them through `Cfg.<name>`, resolved once via
  `ComponentConfig.Require<CameraControllerConfig>()`.

  **`Require` has no fallback on purpose.** A missing config is a DATA bug,
  logged loudly, exactly as a missing stat is — never a silent code-side default.

  Loading mirrors `TechCatalog`: the config assets have to live beside their
  code, where `Resources.Load` cannot reach, so
  `Resources/ComponentConfigCatalog.asset` references them all and is the only
  thing in the magic folder. Rebuild it with
  `Waning Border > Component Config > Rebuild Catalog`, which also ENFORCES the
  naming — it reports any config whose asset is misnamed or not beside its class.

  **Three things are not config and must not move to the asset:**
  - a scene/runtime object reference the component adopts or creates
    (`CameraController.mainCamera`)
  - state computed at runtime (`worldMin`/`worldMax` come from the active
    Terrain in `Start`; they were public with placeholder defaults, and are now
    private)
  - **per-instance authored data.** The nine `MapMarker` subclasses
    (`PlayerStartMarker.faction`, `RegionSeedMarker.displayName`,
    `NatureRegionMarker.radius`, the five node markers) differ per object in the
    scene — that IS the data, and one shared asset per class cannot express it.
    The rule applies to components with ONE global instance, not to authored
    scene objects.
- **Technologies are SOs, filed under the building that researches them**: one
  `TechDefSO` per tech at `Age0/Buildings/<Building>/Research/<Tech>.asset` or `Civs/<Culture>/Buildings/<Building>/Research/<Tech>.asset` (a sect building's is at `Sects/<Sect>/Buildings/<Building>/Research/`)
  (e.g. `Civs/Alanthor/Buildings/ArcheryRange/Research/Fletching.asset`), carrying its costs,
  prerequisites, culture gate, level gate and both effect models. `TechTreeCatalog.asset` holds the
  references so they load without a magic `Resources/` folder. **There is no JSON at all**
  (2026-10-03): `Resources/TechTree.json` is deleted, for units, buildings and techs alike,
  and a missing SO or catalog entry is a loud load-time error.
  **`TechDefSO.minBuildingLevel` is the ONLY level gate on a tech** (decision 21) — no
  faction-age gate, no code table.
  **`TechDefSO.researchAt` is the single source of truth for the research host.**
  `TechCatalog.RebuildResearchLists()` derives every `BuildingDef.research[]` from it at
  load. Do not hand-author a building's research array -- set `researchAt` on the tech and
  move its asset into that building's `Research/` folder. The two used to be authored
  separately (the player grid read the building list, the AI read `researchAt`), and they
  disagreed; deriving one from the other is what keeps them honest.

- **A sect owns everything that is its own**: `Assets/GameData/TechTree/Sects/<Sect>/` holds that
  sect's `Abilities/` (mechanic systems + its components), `Units/<Unit>/` and
  `Buildings/<Building>/` — and the building keeps its `Research/` folder, so the sect's
  research rides along inside it:

  ```
  Sects/Antiquity/Abilities/SectAntiquityMechanics.cs
  Sects/Antiquity/Units/Lorekeeper/
  Sects/Antiquity/Buildings/Reliquary/Research/RoyalIndex.asset
  ```

  Sect-to-unit mapping is `SectConfig.UnitIdFor` — the file tree follows it, never the
  reverse. Three things sit outside the twelve sect folders on purpose:
  `Sects/Shared/` (Chapel — the adoption marker for every sect, ids `Chapel_<SectId>`),
  `Sects/Cultures/{Alanthor,Feraldis}/` (culture-wide sect code, including the Feraldis
  blood-pool layer — sects group four per culture, so a culture folder in the roster reads
  like a 13th sect), and `Sects/*.cs` (`SectConfig`, `SectAdoption`, `SectQuery`,
  `SectLeverEffects`, `SectInfo`, `SectDefinition`, … — set-level, one level above).
  `Sects/Retired/` parks units of sects that no longer exist but are still registered in
  `UnitFactory`. Nothing sect-related remains in `Scripts/`.
- **The curse's code is all under `Buildings/Border/`**: set-level components/settings/construction/death-drop at the root, well code in `LargeNode/` (factory, bootstrap, marker, node-state, verb/victory/income systems, extinction), pocket code in `SmallNode/` (factory, bootstrap, marker, pocket system). The map-wide veil *field* simulation stays in `Scripts/Systems/Border/` — it is a grid, not a structure.
- All player commands route through `Core/Commands/CommandRouter.cs`

### Managed (MonoBehaviour)
- **UI is four folders, one meaning each** (2026-08-28; `Menus/` left on
  2026-09-03). `GameUI/`, `HUD/` and
  `Panels/` were consolidated: they were not three views of one thing, they were
  three different things with names that hid it — `Panels/` held almost no
  panels (6 of 7 files were the 2,700-line `EntityExtractors` view-model layer)
  while the real panels sat in `GameUI/Panels/`, and `HUD/` mixed world-space
  overlays with screen widgets.

  | Folder | Namespace | Holds |
  |--------|-----------|-------|
  | `UI/Ingame/` | `TheWaningBorder.UI.Ingame` | the in-match screen interface — GameUIManager/Kit/Catalog, tooltip, tutorial, clock, stats board, notifications, floating health bars & damage numbers, and `Panels/` (the 16 binders) |
  | `UI/World/` | `TheWaningBorder.UI.World` | things drawn ON the world — build footprint & grid, ground targeting, movement lines, rally points, unit indicators, placement overlay |
  | `UI/Data/` | `TheWaningBorder.UI.Data` | entity → view-model. `EntityExtractors.*` and `BuildingActionLayouts`. No rendering |
  | `UI/Common/` | `TheWaningBorder.UI.Common` | styles, helpers, palette, and `SimSignalPump` (the sim→UI bridge, which is not a widget) |

  **An AUTHORED panel's binder lives with its prefab, not in `UI/`** (2026-09-01).
  Same co-location rule the TechTree uses: `Assets/GameData/Scenes/Menus/GameUI/<Feature>/`
  holds the prefab and the code that drives it, and that tree carries a
  `TheWaningBorder.Presentation.asmref` so the binders compile into the UI
  assembly. Without that asmref they would land in Assembly-CSharp, which
  nothing references — `GameUIManager` could not see them.
  Moved so far: `Minimap/` (MinimapPanelBinder, MinimapPings), `Objectives/`,
  `Religion/`, `SelectionUI/` (ActionsPanelPrefabBinder, ProductionQueueStrip,
  UnitRosterPanelBinder, UnitStatsPanelBinder, BuildingUpgradeAction).
  They keep namespace `TheWaningBorder.UI.Ingame` — co-location wins over
  folder/namespace symmetry here, exactly as it does for the 34 entity-visual
  files that declare `TheWaningBorder.Rendering` from inside `GameData/TechTree`.
  **`UI/Menus/` finished that journey and is gone** (2026-09-03). Every
  front-end screen has an authored scene, so all 19 files graduated to
  `Assets/GameData/Scenes/Menus/<Screen>/` beside the `.unity` that hosts
  them — the same one-folder-per-thing shape the TechTree uses for units and
  buildings:

  ```
  Scenes/Menus/
    MainMenu/       MainMenu.unity + its 8 runtime scene hooks
                    (SceneNames.Menu), Options/, Scenarios/
    SkirmishMenu/   SkirmishMenu.unity + SkirmishPanel, MenuButtonMotion,
                    MenuToggleSwitch
    MultiplayerMenu/MultiplayerMenu.unity + MultiplayerPanel
    Shared/         LoadingScreen, MenuSceneLink, and Lobby/ — the four
                    widgets BOTH lobby screens use (MapPreviewWidget,
                    ColorPickerPopup, LobbyOptions, LobbyRowLayout)
    GameUI/         the in-match panels (unchanged)
  ```

  Which screen owns a file is not a judgement call: each runtime hook names
  its scene in `OnSceneLoaded` (`scene.name != SceneNames.Menu`), and that is
  what placed them. Note `MenuButtonMotion` targets **Skirmish**, not the main
  menu, despite the generic name. Namespace stays `TheWaningBorder.UI.Menus`,
  per the co-location rule above.

  **`UI/Ingame/Panels/` now holds only panels with NO authored prefab yet**
  (TopChoiceBar, ActionsPanelBinder, SpellsPanelBinder, PauseMenuPanel,
  VictoryPanel, WorkerPanelBinder). A panel graduates out of `UI/` the moment
  its prefab exists. `UI/Data`, `UI/Common` and `UI/World` are NOT panels and
  stay put.

  **The screen folder is `Ingame/`, NOT `Screen/`.** `TheWaningBorder.UI.Screen`
  was tried and reverted: it shadows `UnityEngine.Screen`, so `Screen.width` /
  `Screen.SetResolution` stop resolving anywhere the namespace is in scope. Do
  not reintroduce it.
- Input handling in `Input/RTSInputManager.cs` and `Input/SelectionSystem.cs`
- Camera in `Input/CameraController.cs`

### Assemblies
The game compiles into **two** assemblies today, and the split is being widened
one layer at a time (see the restructure plan):

| Assembly | Root | Files | Contains |
|----------|------|-------|----------|
| `TheWaningBorder.Runtime` | `Assets/Scripts/` (+ `GameData/TechTree` and `GameSystems/{Rendering,Abilities}/` via asmref) | 670 | Core, Components, Systems, Entities, Data, Multiplayer, World, Presentation and all content. **References nothing of ours.** |
| `TheWaningBorder.Presentation` | `Assets/GameSystems/Presentation/` | 74 | `UI/` and `Input/`. They are mutually dependent (17 files one way, 3 the other), so they are one assembly. Its asmdef overrides the parent `GameSystems/` Runtime asmref. → Runtime |
| `TheWaningBorder.Bootstrap` | `Assets/Scripts/Bootstrap/` | 15 | wiring; the only layer allowed to know about everything. → Runtime, Presentation |
| `TheWaningBorder.Editor` | `Assets/Scripts/Editor/` | 6 | `PlayerBuild`, `AlphaBuildPostProcess`, `MapSceneSync`, `MapInfoBaker`, `MapLobbyImageBaker`, `MapAssetFolders`. → Runtime |

**The dependency arrows only point one way, and the compiler now enforces it.**
Simulation code cannot reference the UI: it posts to `Core/SimSignals` (notices,
minimap pings, match end) and `UI/Common/SimSignalPump` drains that each frame.
Screen facts the sim genuinely needs — is the loading overlay up, is a building
being placed, what is selected — are PUBLISHED down into `Core/PresentationState`
by their owner, never read up out of the UI.

Two traps this split exposed, worth knowing before adding code:
- `internal` is per-ASSEMBLY. `AgeUpSystem`'s transform helpers were internal and
  became invisible to `StartAgePromoter` the moment Bootstrap moved out.
- A namespace can lie about which assembly a type is in. `VictoryConditionSystem`
  sat in `Scripts/Systems/Core` (Runtime) declaring `namespace TheWaningBorder.UI.HUD`,
  so a content file needed `using TheWaningBorder.UI.HUD` to reach a Runtime type.
  Keep the namespace matching the folder.

`TheWaningBorder.Editor` is `includePlatforms: ["Editor"]`, so editor code can
no longer reach a player build and **needs no `#if UNITY_EDITOR` guard**. That
guard used to be the only thing keeping `UnityEditor` out of the shipped
assembly, because the `Editor/` folder convention does NOT apply inside an
asmdef — it still doesn't, anywhere else in the tree, so a new editor-only file
placed outside `Assets/Scripts/Editor/` still needs the guard.

**The release pipeline lives in this assembly**: `tools/release.ps1` drives
`-executeMethod TheWaningBorder.EditorTools.PlayerBuild.Build`, which calls
`MapSceneSync.ScenesForPlayerBuild` for the ship gate. If it fails to compile,
builds stop.

### Namespaces
- `TheWaningBorder.AI` - AI brain, managers, behaviors
- `TheWaningBorder.Economy` - FactionResources, FactionEconomy, SuppliesIncome
- Global namespace - ECS components (CoreComponents, UnitComponents, etc.)

## Naming Conventions
- ECS marker components: `XxxTag` (e.g., `HallTag`, `WorkerTag`)
- ECS stateful components: `XxxState` (e.g., `MiningState`)
- Commands: `XxxCommand` (ECS component) + `XxxCommandHelper` (static helper)
- Building tags: `HallTag`, `BarracksTag`, `GathererHutTag`, `HutTag`
- Factions: enum `Faction` (Blue=0 .. White=7)
- Cultures: `Cultures.None / Runai / Alanthor / Feraldis`

## Key Design Decisions (Do Not Change)

> **Nobody gathers (2026-10-03).** [docs/Design/Regions.md](docs/Design/Regions.md)
> §4 removed worker gathering, and the code followed: there is no Miner and no
> Builder — ONE economy unit, the **Worker, which only builds** (and repairs).
> Income is territory: each held slot pays a trickle, and an extractor on it
> (Gatherer's Hut, Mine, Veilstone Mine, Alanthor Trading Outpost) pays its
> SO's slot-income ladder; the capital pays its own SO income
> ([Veilstone_Economy.md §5](docs/Design/Veilstone_Economy.md)).

- **Input reads intent; it does not issue orders** (2026-09-01).
  `RTSInputManager` was 1,285 lines of which ~600 were not input at all — what
  a click MEANS for the selection (attack / heal / build / repair / purify /
  corrupt / convert / patrol / formation / waypoints), plus which units are
  capable of what and who owns them. That is simulation knowledge, it routes
  through `CommandRouter`, and it now lives with it:
  `Scripts/Core/Commands/Issuing/SelectionOrders.cs` in **Runtime**.
  The input layer decides THAT an order was given and what it was aimed at,
  then hands the target over. Runtime never learns about `SelectionSystem` —
  the selection arrives as a delegate the input layer supplies.
  `RTSInputManager` is 738 lines and reads no gameplay rules.
- **The camera is `Presentation/CameraRig/`, namespace `TheWaningBorder.CameraRig`.**
  Not `Camera`: `TheWaningBorder.Camera` shadows `UnityEngine.Camera` wherever
  it is in scope — the same trap as the reverted `TheWaningBorder.UI.Screen`.
  `GameCamera` was folded into `CameraController` once it was only forwarding
  (four of its nine members had no callers at all); the static API is a
  `#region Static API` at the top of the class.
- **Screen facts are read from `PresentationState`, never up out of a panel.**
  This is written down twice already and was still being violated:
  `CameraController`, `RTSInputManager` and `SelectionSystem` each read
  `WorkerCommandPanel.IsPlacingBuilding` directly, while
  `PresentationState.PlacingBuilding` — written faithfully by that same panel —
  had zero readers. All three now read the published fact. `RTSInput.cs` held a
  third copy of the same flag and is deleted: 8 of its 11 members had no callers.
- **No scene ships a camera. The camera rig creates its own** (2026-09-01).
  `CameraController.InitializeCameraRig` has ONE path: it always makes the
  camera and its AudioListener. It used to adopt `Camera.main` when the scene
  had one, which was the path 35 of the 38 boot-path scenes actually took —
  every scenario plus Veilmarch and Sundered Reach all carried a stock
  `File > New Scene` camera. Those 35 have been stripped; a surviving
  `Camera.main` is now a logged error and is destroyed, because adopting it
  silently gives two rendering cameras and Unity's "2 audio listeners" warning.
  Menu scenes keep their cameras — they never call `GameCamera.Ensure()`, whose
  only caller is `GameBootstrap`.
- Player color does NOT change on culture selection
- Income is credited straight to the faction bank by the territory / extractor
  tick — there are NO carrying workers, NO gathering and NO dropoff buildings
- Workers only build and repair; local player workers need an explicit order,
  AI workers find sites on their own
- Workers auto-chain to nearby unfinished structures within LOS
- Shift+click stays in building placement mode for repeated placement

## Development Workflow

### Branch Strategy
- `main` - stable, reviewed code only
- `develop` - integration branch for features
- `feature/<name>` - new features (branch from develop)
- `fix/<name>` - bug fixes (branch from develop)
- `refactor/<name>` - code restructuring (branch from develop)

### Commit Message Format
```
<type>(<scope>): <short description>

<optional body>
```
Types: `feat`, `fix`, `refactor`, `docs`, `test`, `chore`
Scopes: `ai`, `combat`, `economy`, `ui`, `input`, `movement`, `building`, `mining`, `multiplayer`, `world`, `core`

### Before Committing
1. Ensure no compile errors (check for missing references, namespace issues)
2. Verify ECS component changes don't break SystemBase queries
3. Check that new components are registered in appropriate bootstrap files
4. Test that UI panels still render correctly after changes

## File Map (Key Files)
| Domain | Key Files |
|--------|-----------|
| Commands | `Core/Commands/CommandRouter.cs` |
| Economy | `Economy/FactionEconomy.cs`, `Economy/FactionResources.cs` |
| Income | `Systems/World/TerritoryIncomeSystem.cs` (slots, extractors, capital) |
| Construction | `Systems/Work/BuildingConstructionSystem.cs` |
| Combat | `Systems/Combat/TargetingSystem.cs`, `Systems/Combat/MeleeCombatSystem.cs` |
| AI | `GameSystems/AI/AIBrain/AIBrain.cs`, `GameSystems/AI/SimpleAISystem/` |
| Input | `Input/RTSInputManager.cs`, `Input/SelectionSystem.cs` |
| UI | `UI/Data/EntityExtractors.cs`, `UI/Ingame/Panels/BuildCommandPannel.cs` |
| Training | `Systems/Training/TrainingSystem.cs` |
| Factions | `Core/Settings/FactionColors.cs`, `Core/Settings/CultureConfig.cs` |

## What NOT to Modify
- Do not rename `BuildCommandPannel.cs` (known misspelling, kept for reference stability). It moved to `UI/Ingame/Panels/` on 2026-08-28 — the FILE NAME is what must not change, not its folder
- Do not change the global namespace of ECS components without updating all systems
- Do not modify `CommandRouter.cs` routing logic without reviewing all command types
- Do not add Unity packages without consulting the developer

## Agent Pipeline

This project uses a 4-agent development pipeline. Agent definitions are in `.github/agents/`.

### Quick Reference
| Command | What it does |
|---------|-------------|
| "process task: \<description\>" | Runs the full pipeline (intake → spec → code → review) |
| "create issue for: \<description\>" | Task Intake agent only |
| "write spec for issue #N" | Spec Writer agent only |
| "implement issue #N" | Coder agent only |
| "review PR #N" | Reviewer agent only |

### Pipeline: process task
1. **Task Intake** (`.github/agents/task-intake.md`) - Creates a labeled GitHub issue
2. **Spec Writer** (`.github/agents/spec-writer.md`) - Posts implementation spec as issue comment
3. **Coder** (`.github/agents/coder.md`) - Branches, implements, commits, creates PR
4. **Reviewer** (`.github/agents/reviewer.md`) - Reviews PR, approves or requests fixes
5. Coder ↔ Reviewer cycle (max 3 rounds) until approved

### Setup
The pipeline requires a GitHub PAT in `.env`:
```
GH_TOKEN=ghp_your_token_here
```

### GitHub API Pattern
Since `gh` CLI is not installed, use `curl` for all GitHub operations:
```bash
# Read token
GH_TOKEN=$(grep GH_TOKEN .env | cut -d= -f2)

# API calls
curl -s -H "Authorization: token $GH_TOKEN" \
  "https://api.github.com/repos/LuisMartinsGit/super_duper_twb1.2/..."
```

### Pipeline Checkpoints
The pipeline pauses for user confirmation after:
- Issue creation
- Spec posting
- PR creation
- Review completion
