# Tutorial missions (2026-11-07)

Developer's brief. **The tutorial is not a skirmish.** It is a set of scripted missions, each
on its own small scene, each teaching one thing. This replaces the coach that ran on a normal
match (`TutorialDirector`, no longer launched from the menu).

The infographic cards are **held back** until they are reviewed: no mission opens one.
(The pause menu's Field Guide is a separate entry and is unchanged.)

## 1. How a mission differs from a match

- **One faction and the curse.** No AI opponents, no lockstep, no replay, no save.
- **The mission boots the world.** No Fortress and no starting army. Fog is on, because
  exploring is the lesson.
- **The curse is real but scripted.** The node, its held territory and its cursed ground are
  the game's own. Everything the curse does by itself (garrisons, expansion, reseeding,
  attack waves, army conversion, Shardroot behaviour) is off; only the mission sends units.
- **No monuments, no Shardroot, no elimination.** The mission alone decides win and loss.
- **No wear on unlinked ground, no religion.** A mission's first Fortress stands on ground no
  Fortress links yet, and the wear-down rule (Territory_Claims.md §10) would kill its foundation.
  Curse kills pay no Religion Points while the mission hides religion.
- **Only what is being taught is on screen.** Panels stay hidden until their step, and the
  build, train and research buttons are filtered to the current step's list.

## 2. Mission 1: Basic (Age 0)

Teaches camera, orders, building, income, research, training, combat and the curse.

**The map** is very small and has two territories: the Wildwood, where the player starts,
and the Blight, which holds one curse node. Layout: `tools/maps/tutorial_basic/layout.py`.
The resource nodes stand around the camp clearing by hand (the Start territory's own counts:
three supply, three iron, one veilstone), not dealt across the woods, so a beginner can see them.

**The story.** A Litharch searches the wilderness for resources after the calamity. They find
workers lost in the woods, and together they look for a place fit for a refugee camp.

### Stages

| Stage | What happens | On screen |
|---|---|---|
| 1 Rescue | One Litharch. Teaches the camera, move orders and chained (Shift) orders. Workers join when found | Map, unit info. **No build screen, no resources** |
| 2 Camp | With all workers rescued, the clearing is marked. Reaching it claims the Wildwood and grants the mission's resources. Workers can build the Shelter only | + resources, build screen |
| 3 Settle | Huts, Gatherer's Huts, mines and a Barracks | + each building's own actions |
| 4 Muster | Train Scouts, find the lost Spearmen, train Spearmen. Research is introduced at the Barracks | + training, research |
| 5 The curse | At the first moment the player has 8 living Spearmen (found or trained), a harassing wave attacks and the node is revealed. Then a full wave comes on a fixed clock until the node falls | + Objective 5 |

### Objectives panel

Completed rows stay ticked, and rows for later stages appear when their stage opens.

1. Rescue all the workers (0/6)
2. Build the Shelter (0/1)
3. a. Build 6 Huts · b. Build 3 Gatherer's Huts · c. Build 1 Veilstone Mine and 2 Iron Mines
   · d. Build a Barracks
4. a. Train Scouts (0/2) and find the lost Spearmen (0/16) · b. Train 10 Spearmen
5. Survive the curse long enough to vanquish the node

The curse stage opens on the 8 Spearmen rule, so rows 4 and 5 can be open together.

### Rules

- **Lost units join when seen:** a player unit coming within sight of them. Any unit can find
  them, though Scouts are the ones that are meant to.
- **Trained** counts units produced, so a Spearman or Scout that later dies stays counted;
  found Spearmen never count toward row 4b.
- **Win:** the node is destroyed, at any point. **Lose:** the finished Shelter is destroyed.
- **Population fits:** six Huts and the Shelter house everything the objectives ask for.
- **Wave composition, timing, grants, trigger counts and positions** are on
  `GameSystems/Tutorial/TutorialMissionSystem.asset`. Unit and building stats stay on their SOs.

## 3. Code

| Piece | Where |
|---|---|
| Stages, spawns, waves, win and loss | `GameSystems/Tutorial/TutorialMissionSystem.cs` (Runtime) |
| What the UI may offer, published progress | `GameSystems/Tutorial/TutorialMissionState.cs` |
| Objectives panel, coach lines, hiding panels | `Presentation/UI/Ingame/TutorialMissionPanel.cs` |
| Scene | `GameData/Scenes/Tutorial/` (`MapRegistry.TutorialRoot`: ships, never in the lobby) |
| Launch | `TutorialMenuItem` loads the Tutorials screen (`Scenes/Menus/TutorialsMenu/`, `TutorialsPanel`); `Launch(mission)` sets `GameSettings.TutorialMission` |
| Automated playthrough | `Bootstrap/Headless/HeadlessTutorial*.cs`: `-twbTutorialAuto [-twbMission advanced]`, `-twbMenuTest` |

## 4. Mission 2: Alanthor and the House of Vaelith (advanced)

**Status: implemented. Code: `GameSystems/Tutorial/TutorialAdvancedSystem.cs`, map
`tools/maps/tutorial_alanthor/layout.py`. Numbers: `TutorialAdvancedSystem.asset`.**

### 4.1 The developer's brief (2026-11-07)

1. Ageing up: the Litharch has been appointed king and has led the Shelter into becoming a
   large settlement.
2. A monument is erected to honour the new king and usher the town into grandeur.
3. Build the Vault and age up to Alanthor.
4. Explain the new Alanthor advanced economy (Guild, Mine, and the Trading Outpost that keeps a node at its last unit — 2026-11-10).
5. Explain the new military buildings.
6. Explain defenses as waves continue to come at the town, and how territories can be conquered
   and settled.
7. Introduce a rival kingdom.
8. The religion points harvested from the curse waves are the key to victory.
9. Introduce religion.
10. The player moves to kill the curse (in a Sanctum territory) and discovers a monument and the
    Sanctum mechanic.
11. The monument uncovers the Shardroot.
12. The victory conditions are presented: conquest, enshrinement or Sanctum victory. There must
    be another Sanctum on the map.

The tutorial ends when the player wins.

### 4.2 Decisions (2026-11-07)

- **Text:** plain instruction in Mission 1's tone (the developer's call; it replaces an earlier
  idea of first-person lines by the Litharch). The story belongs to the campaign.
- **Steps 2 and 3 are one event:** the monument that ages the town up is the Vault of Almiérra.
- **The Litharch becomes the first of King Lexor's dynasty at the age-up:** the unit is replaced
  by the King Lexor hero where it stands.
- **The mission starts from a fresh prepared town,** not carried over from Mission 1.
- **The rival is a Beginner AI** that starts with two extra territories and never attacks.
- **Enshrinement and Sanctum dominion use a shorter countdown** (`winCountdownSeconds`).
- **The only rule change is which monument reveals the Shardroot,** and it needs no code: one
  curse node means one monument.
- **The rival stays out of reach until the victory options appear.** Until then it shares the
  player's team, so neither fights and neither contests ground; the real victory check is off.
  At step 12 the teams split and `EliminationSystem` takes over.

### 4.3 Objectives

1. Build the Vault of Almiérra. 2. a. Level up your Guilds · b. Research Trade Charters at the Fortress (2026-11-10, decision 45: Stop Extracting is removed; Trade Charters is a Fortress L1 tech, and a Trading Outpost keeps its node at the last unit).
3. Build an Archery Range. 4. a. Build a Watch Tower · b. Claim a territory.
5. Earn Religion Points. 6. a. Build the Temple · b. Adopt a sect.
7. Destroy the curse node in the Sanctum. 8. a. Claim the monument · b. Explore its ruins.
9. Win: destroy the rival, enshrine the Shardroot, or hold every Sanctum.

The rival is introduced between 4 and 5, with a note and no objective. The mission ends when the
player wins; losing the capital before step 9 fails it.

**The menu:** the main menu's Tutorial entry opens the **Tutorials screen**, a copy of
the Scenarios browser: the missions listed on the left, the selected one's briefing on
the right, < MAIN MENU and START MISSION in the footer. The Scenarios browser no longer
lists the tutorial.

## 5. Later missions

Not specified yet. The old coach's later chapters (culture, territory, religion, heroes) are
the raw material. A mission list in the menu comes with the second mission.
