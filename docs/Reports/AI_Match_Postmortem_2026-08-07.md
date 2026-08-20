# AI match postmortem — 2026-08-07 (Logs/AI_*.log)

**Source:** `Logs/AI_Blue.log`, `AI_Green.log`, `AI_Red.log`, `AI_Yellow.log`
(1,608 lines total, written by `Assets/Scripts/AI/Core/AILogger.cs`).
No `Player_*.log` exists, so this was a **4-AI observer match**, all
difficulty **Normal**, run **51 minutes**, and **it never ended**.

---

## 1. Match at a glance

| Faction | Strategy | Culture | Peak state | Dead by | Final snapshot (51:01) |
|---|---|---|---|---|---|
| Blue | TechRush | Alanthor | 22 gatherer huts, 5 towers, 6 Cataphracts | ~17:08 | supplies 12, iron 1, **military 0** |
| Green | Defensive | Alanthor | 17 gatherer huts | ~14:29 | supplies 162, iron 0, veilstone 0, **military 0** |
| Yellow | TechRush | Feraldis | 22 gatherer huts, 17 totems | ~19:51 | supplies 0, iron 0, **military 0** |
| **Red** | Rush | Feraldis | 47-unit army, Barracks L4 | — | supplies **19,283**, veilstone **33,789**, military 36 |

Global curse territory: 11.3 % at 00:51 → 73.9 % at 37:00 → **91.9 %
plateau** from 38:00 onward.

**The shape of the match:** Blue, Green and Yellow were economically dead
between minutes 14 and 20 — not defeated in battle, but starved as the
curse ate the map. Each then logged the *same line* every 60 s for the
next 30+ minutes:

```
[50:51.7] MILITARY: floor blocked ~1 min: deficit 12 x Spearman (trainer missing/queue full/wallet or bank short)
[51:01.7] SNAPSHOT: supplies 162 iron 0 veilstone 0 veilsteel 0 military 0 influence 0.0% curse 91.9%
```

Red was the uncontested winner from ~minute 20 and never won anything. It
banked 33,789 veilstone it had no use for, and spent the last half hour
issuing the same order to the same unit ~200 times.

### Event tallies

| Event | Blue | Green | Red | Yellow |
|---|---|---|---|---|
| Verb dispatched (Corruptor/Scholar) | 0 | 0 | **128** | 0 |
| Verb held back (no escort) | 0 | 0 | 82 | 31 |
| Wave LAUNCHED | 1 | **0** | 4 | 1 |
| Wave BLOCKED | 20 | 21 | 18 | 21 |
| Wave "reinforced" | 25 | 0 | **238** | 1 |
| "floor blocked" (can't train) | 34 | 38 | 0 | 35 |
| "no trainer for X" | 1 | 3 | 4 | 1 |

**Zero verbs completed. Zero eliminations. Zero victories. In 51 minutes.**

---

## 2. End-game objective #1 — player destruction

`Assets/Scripts/Systems/Core/VictoryConditionSystem.cs`

### 2.1 A faction is "alive" while it owns one completed building of any kind

```csharp
// VictoryConditionSystem.cs:110-124
if (_em.HasComponent<UnderConstruction>(entities[i])) continue;
buildingCounts[faction]++;
...
if (kvp.Value == 0) newlyEliminated.Add(kvp.Key);
```

The only elimination test is *zero completed buildings*. Green at minute
15 had 0 military, 0 iron, 0 veilstone, 0 income and no ability to train
anything — and ~16 gatherer huts. It stayed "alive" for another 36
minutes. Yellow and Blue the same.

**Why it feels bad:** the loser is not allowed to lose, and the winner is
not allowed to win. The finishing move in an RTS is razing the last
production building; here it is hunting down sixteen 1-tile huts scattered
across a 400×400 map, at 92 % curse coverage, with no minimap indicator
telling you which ones are left.

**Missing:** no resign for AI, no "you cannot recover" surrender
threshold, no idle/no-op timeout, no score-based timeout.

### 2.2 Victory and defeat produce no UI whatsoever

```csharp
// VictoryConditionSystem.cs:200-203
// Old post-game UI (EndGameButton / PostGameStatsUI) removed with
// the old UI (2026-07-17); the final uGUI will own the post-game screen.
TWBLog.Log($"[Victory] Game over: {result} (winner={winner})");
```

`TriggerGameEnd` and `TriggerNodeVictory` set `_gameOver = true`, call
`GameStatsTracker.EndGame()` (which only takes a final sample and sets a
bool — `GameStatsTracker.cs:234-242`), and write a log line. Nothing else
in the codebase reads `GameEnded` except `VictoryConditionSystem` itself.

Searching all of `Assets/Scripts/UI` for `VICTORY`/`DEFEAT`/`PostGame`
returns exactly one hit: a *comment* in `UI/Common/Styles.cs:257`
describing a banner that does not exist.

**Consequence:** even if you win, the game does not stop, does not tell
you, does not pause, does not show stats, and does not return you to the
menu. The single most important moment in a match is a `Debug.Log`.

### 2.3 The local player's defeat is silently swallowed in observer mode

```csharp
// VictoryConditionSystem.cs:152
if (!GameSettings.IsObserver && faction == GameSettings.LocalPlayerFaction)
```
This is deliberate and correct for observers, but combined with 2.2 it
means *no* path — player defeat, player victory, node victory, surrender —
currently surfaces anything to the screen.

### 2.4 Objectives panel is the only feedback, and it hides itself

`Assets/Scripts/UI/GameUI/ObjectivesPanelBinder.cs:95` disables the whole
panel when `GameSettings.IsObserver`. In this match — an observer match —
the player saw no objectives at all. Even in a normal match it only shows
**your own** progress:

- `CountWells` (line 252) counts only `OwnerFaction == faction`.
- Step 3B "Destroy all other players (0/3)" is a count, with no indication
  of *where* the survivors are or what is keeping them alive.

There is no shared, public well-state readout, which the canon explicitly
requires ("the well count is a shared, legible clock every player reads" —
`docs/Design/Curse_And_Shardroot.md` §1).

---

## 3. End-game objective #2 — the verbs (purify / pacify / destroy)

`Assets/Scripts/Systems/Border/NodeVictorySystem.cs` implements well
domination correctly against canon: Feraldis instant on all-Destroyed,
Alanthor/Runai after `NodeVictoryHoldTime` (5 s grace) on all-Cleansed /
all-Converted, plus a match-point broadcast at N−1. **The scoring is
fine. Nothing can ever feed it.**

### 3.1 ROOT CAUSE — the AI wave system steals the ritualist mid-channel

This is the single most important bug in the report.

`SimpleAISystem.ReinforceActiveWave` re-commands every idle combat unit
every 10 seconds:

```csharp
// SimpleAISystem.cs:1574, 1603-1619
private const float ReinforceInterval = 10f;
...
if (!IsCombatClass(tags[i].Class)) continue;
if (em.HasComponent<UnderConstruction>(e)) continue;
if (em.HasComponent<PlundererTag>(e)) continue;
bool busy = em.HasComponent<AttackMoveTag>(e) || em.HasComponent<AttackCommand>(e);
if (busy) { committed++; continue; }
if (em.HasComponent<UserMoveOrder>(e)) continue;
if (em.HasComponent<BuildCommand>(e)) continue;
AttackMoveCommandHelper.Execute(em, e, aiState.WaveTarget);   // ← ClearAllCommands
```

`IsCombatClass` (line 1905) accepts `Melee | Ranged | Siege | **Magic**`.

The three ritualists:

| Unit | Class | Tag | File |
|---|---|---|---|
| Alanthor Scholar | `UnitClass.Magic` | `ScholarTag` | `GameData/TechTree/Units/Alanthor/Scholar/Scholar.cs:44` |
| Runai Acolyte | `UnitClass.Magic` | `AcolyteTag` | `.../Runai/Acolyte/*.cs:42` |
| Feraldis Iconoclast | `UnitClass.Melee` | `CorruptorTag` | `.../Feraldis/Iconoclast/Iconoclast.cs:50-59` |

`SimpleAISystem.cs` contains **zero** references to `ScholarTag`,
`CorruptorTag`, `AcolyteTag`, `RitualState`, `PurifyCommand`,
`ConvertNodeCommand` or `CorruptCommand`. So every 10 s the wave sweep
picks up the ritualist and issues an AttackMove, and
`AttackMoveCommandHelper.Execute` → `CommandHelper.ClearAllCommands` strips
the verb command.

Now compare the numbers:

| Verb | Channel time | Command stolen after |
|---|---|---|
| Purify | 35 s (`BorderConstants.PurificationChannelTime`) | 10 s |
| Pacify | 45 s (`ConversionChannelTime`) | 10 s |
| Destroy (corrupt) | 40 s (`FeraldisConstants.CorruptionChannelTime`) | 10 s |

**No AI verb can ever complete while a wave is active. It is arithmetically
impossible.** The endgame system re-dispatches on its own 5 s tick
(`AIFeraldisEndgameSystem.ThinkInterval`), so the two systems ping-pong
forever. That is exactly what Red's log is:

```
[24:15.5] STRATEGY: Corruptor dispatched to well at (12,129) with escort 8
[24:22.7] WAVE: wave 4 reinforced with 47 unit(s) (0 already committed)
[24:25.5] STRATEGY: Corruptor dispatched to well at (12,129) with escort 8
[24:33.2] WAVE: wave 4 reinforced with 46 unit(s) (1 already committed)
```

**128 dispatches at the same well over 22 minutes, never once landing.**
Note that `AIFeraldisEndgameSystem.CommitArmy` was already hardened against
exactly this (line 674: `if (em.HasComponent<CorruptorTag>(u)) continue;`) —
but `SimpleAISystem` runs underneath and was never given the same guard.

The code comment at `AIFeraldisEndgameSystem.cs:579-584` blames the
2026-08-06 failure on Corruptors "walking alone and dying before arrival"
and added an escort gate. The escort gate is not the problem; the log
shows escort 8–12 present and the dispatch still repeating. **The fix
applied last time treated the symptom.**

### 3.2 A wave never ends, so the theft never stops

```csharp
// SimpleAISystem.cs:1622-1627
if (committed == 0 && sent == 0) { aiState.WaveActive = 0; return; }
```
A wave is only "spent" when nothing is attack-moving. Because the sweep
re-issues AttackMove to everything idle *in the same pass*, `sent > 0`
essentially always, so `WaveActive` never clears. Red's wave 4 launched at
18:58 and was still "reinforcing" at 51:13 — **32 minutes**, with the last
20 minutes reading `reinforced with 47 unit(s) (0 already committed)`, i.e.
the entire army was standing still at home and being re-ordered forever.

Meanwhile `wave 5 BLOCKED (need 13 idle, posture Pressure, desired 20)`
fires every 2 minutes because everyone is nominally committed to wave 4.
**Red's 47-unit army never attacked anyone after minute 19.**

### 3.3 Runai has no AI at all

`AIAlanthorEndgameSystem.cs` and `AIFeraldisEndgameSystem.cs` exist. There
is no `AIRunaiEndgameSystem`. And the AI is forbidden from picking Runai:

```csharp
// Assets/Scripts/AI/AIBuildOrder.cs:335-337
/// Runai is deliberately absent: it is still an incomplete culture
/// ... AI must never pick it. Restore it here when Runai ships.
```

So **one of the three end-game verbs (pacify) has never been exercised by
an AI**, and `ConversionRitualSystem` is missing the orphan sweep its two
siblings have — flagged in a sibling file's header:

```
// Phase 4 is an orphan sweep. PurificationRitualSystem has one and
// ConversionRitualSystem does not, which is a known bug there — a node whose
// ritualist dies mid-channel is locked out forever.
//   — CorruptionRitualSystem.cs:21-23
```

**Correction to an earlier draft of this report:** this is *latent*, not
live. `PurificationRitualSystem`'s sweep
([PurificationRitualSystem.cs:341-355](../../Assets/Scripts/Systems/Border/PurificationRitualSystem.cs#L341-L355))
ignores `ActiveRitualOnNode.Kind` and that system is ungated, so it has
been silently cleaning up after all three verbs. The real hazard is the
coupling: orphan cleanup for the entire Border stack is an accidental side
effect of the Alanthor system, and a single `RequireForUpdate<ScholarTag>`
added there as an obvious optimisation would brick every verb at once. If
it ever does fire, the symptom is severe — a stale `ActiveRitualOnNode`
removes that well from the match for **every** player, permanently and
invisibly.

### 3.4 The two verb gates are wildly asymmetric

| Culture | Ritualist gate | Upgrade cost to gate | Win requirement |
|---|---|---|---|
| Feraldis | Temple **L3** | 1,300 supplies / 550 iron / 400 veilstone | **Instant** on all-Destroyed |
| Alanthor | Temple **L4** (max) | 2,500 supplies / 1,050 iron / 800 veilstone | All-Cleansed + hold |
| Runai | — | — | All-Converted + hold |

(`TempleLevelConfig.cs:25-31`, `AIAlanthorEndgameSystem.cs:257-261`,
`AIFeraldisEndgameSystem.cs:515-532`.)

Alanthor pays roughly **double** to unlock a verb that then also has to
hold every well simultaneously, while Feraldis pays half and wins the
instant the last well dies. Blue and Green (both Alanthor) never reached
Temple L4 and therefore **never trained a single Scholar in 51 minutes** —
`Scholar dispatched: 0` in both logs. Alanthor's entire end-game is
unreachable at Normal difficulty on this map.

### 3.5 Destroyed wells respawn, which quietly fights the Feraldis win

`BorderExtinctionSystem` (`RespawnDelay = 180f`) respawns a main node when
all main nodes are destroyed. `NodeVictorySystem` fires first *if*
attribution is intact, but any attribution miss (killer culture unresolved,
a rival denying the kill) turns a Feraldis match point into a 3-minute
reset with no feedback. Worth an explicit interlock.

### 3.6 The vulnerability window is a coin flip, not a decision

`CorruptionVulnerableSeconds = 60`, well HP = 4,000
(`BorderConstants.MainNodeHP`), and the timer only pauses while the well is
actively losing HP, capped at `CorruptionMaxHeldSeconds = 120`. So the
attacker gets at most 180 s to do 4,000 damage, and the *defender's* only
counterplay is to kill the Corruptor during a 40 s channel — which is
telegraphed only by a 2.5 s toast (`PlayerNotificationSystem`
`DefaultDuration = 2.5f`) and a minimap ping. There is no persistent
"a well is being corrupted" indicator, no timer on screen, and no way to
see *whose* Corruptor it is.

---

## 4. End-game objective #3 — the curse, which is what actually killed everyone

Nobody in this match was killed by a player. Three factions were killed by
the curse reaching 92 % map coverage, and **the curse has no win/loss
condition attached to it**.

- Curse hits 73.9 % at 37:00 and jumps to 90.9 % at 38:00 — a 17-point
  single-minute jump that no log line explains and no player could have
  reacted to.
- At 91.9 % the map is functionally over. Nothing fires. No warning
  threshold, no "the world is lost" defeat, no acceleration of the
  victory clock.
- `emaS=0.0/s emaI=0.0/s` for 35 straight minutes on three factions is a
  perfectly detectable "this player is finished" signal that nothing reads.

**Why it feels bad:** the game's stated third player wins the map and
receives no credit, and the human sitting in that match has no losing
condition, no comeback mechanic, and no reason to keep watching.

---

## 5. Secondary findings (AI quality / counterplay feel)

**5.1 — Build orders reference trainers that don't exist yet.**
`BUILDORDER: step 5 SKIPPED (no trainer for Spearman)` (Red 01:58, 04:01,
04:04) and `MILITARY: floor unit Archer has no trainer — falling back to
Spearman` (all four factions). The scripted order asks for units before
the Barracks/Archery Range exist. Red silently dropped 3 army steps in the
first 4 minutes of a **Rush** build.

**5.2 — "floor blocked ~1 min: deficit 12 x Spearman" repeats 34–38 times
per dead faction.** It is a real diagnostic the first time and pure log
noise the next 37. The AI never concludes "I cannot train anything, ever
again" and changes behaviour.

**5.3 — Yellow placed the same Mine 7 times in 30 seconds** (19:20.9 →
19:51.0, all at (297,99)/(289,101)) and dropped from 177 supplies to 0 in
the same window. That is a placement loop burning the bank into
foundations. Compare Red at 12:07–12:48: `War Totem planted on blood at
(152,111)` **five times at the identical coordinate**.

**5.4 — Feraldis worker conscription is a treadmill.** Red logged
`conscripted 2 surplus Worker(s) as light infantry` **68 times**, every
35 s, forever, while `military` stayed pinned at 47. The conscripts are
being consumed (dying to curse exposure) as fast as they are made, and the
AI reads them as a standing army — which is exactly the failure mode the
comment at `SimpleAISystem.cs:1240-1246` says was fixed.

**5.5 — Mines are placed on near-empty ore.** `Mine ONLINE at (289,101):
6 iron + 0 veilstone node(s) within 18m (world has 377 iron / 319
veilstone nodes)`. Yellow paid full price for a mine servicing 6 nodes
while 377 existed elsewhere.

**5.6 — Blue fired "Renewal active power" 9 times between 11:42 and 17:08
with no observable effect**, then died. Either the power does nothing or
nothing reports that it did.

**5.7 — Red banked 33,789 veilstone and 19,283 supplies.** The budget
weights sat at `(0.25/0.35/0.40)` and the saturation caps clamped at
`500s/500i/500v` — the AI had no spend sink and no awareness that it had
already won the economy. There is no "you have enough, go end it" state.

---

## 6. Ranked fixes

| # | Fix | Where | Why |
|---|---|---|---|
| **1** | Exclude ritualists from wave sweeps: skip `ScholarTag`/`AcolyteTag`/`CorruptorTag`, and any unit with `RitualState`/`PurifyCommand`/`ConvertNodeCommand`/`CorruptCommand`, in `ReinforceActiveWave` **and** the wave-launch draft | `SimpleAISystem.cs:1603-1619` | Unblocks *all three* verbs. Single highest-value change in the report. |
| **2** | Ship a post-game screen (or at minimum a full-screen banner + timescale stop) driven by `TriggerGameEnd`/`TriggerNodeVictory` | `VictoryConditionSystem.cs:172-248` | Right now winning and losing are both invisible. |
| **3** | Retire a faction on economic death, not building count: no Hall **and** no production building, or `income == 0` + `military == 0` for N minutes → AI resigns | `VictoryConditionSystem.cs:99-170` | Ends the 36-minute zombie tail; makes "destroy all players" a reachable objective. |
| **4** | Clear `WaveActive` when a wave has been reinforcing with `0 already committed` for K sweeps | `SimpleAISystem.cs:1622-1627` | Red's army stood still for 32 minutes. |
| **5** | Add the orphan sweep to `ConversionRitualSystem` (copy `CorruptionRitualSystem.SweepOrphans`) | `ConversionRitualSystem.cs` | Fixes permanent map-wide well lockout on Acolyte death. |
| **6** | Public well-state UI: N wells, who holds each, under what verb, plus hold timers — visible to everyone, incl. observers | `ObjectivesPanelBinder.cs` (+ minimap) | Canon §1 requires a shared legible clock; today only your own count is shown, and observers see nothing. |
| **7** | Level the verb gates — Alanthor Scholar at Temple L3, or Feraldis Corruptor at L4 | `TempleLevelConfig`, Scholar/Iconoclast train reqs | Alanthor's verb was unreachable all match. |
| **8** | Persistent "well under corruption — 40s" indicator with owner colour, not a 2.5 s toast | `PlayerNotificationSystem`, minimap | The defender's only counterplay window is currently near-invisible. |
| **9** | Curse thresholds do something: warn at 60/75 %, and either shorten the victory hold or trigger a shared loss at ~90 % | curse/veil systems | The curse won this match and the game didn't notice. |
| **10** | De-duplicate AI placement (cooldown per coordinate) and gate build-order steps on trainer existence | `AIFeraldisEndgameSystem.TryPlace`, `AIBuildOrder` | Kills the 7×-mine and 5×-totem loops and the skipped Rush steps. |
| **11** | Give Runai an endgame system, or keep it out of human matches too until it ships | `Systems/AI/` | One third of the design's end-game is unexercised. |

---

## 7. Fixes applied (2026-08-07)

Verified with `dotnet build TheWaningBorder.Runtime.csproj` — **0 errors**
(5 pre-existing warnings, none from these changes).

### 7.1 Ritualists are no longer drafted as army — fix #1

New `SimpleAISystem.IsVerbUnit(em, e)`: true for `ScholarTag` /
`AcolyteTag` / `CorruptorTag`, **or** any unit carrying `RitualState` /
`PurifyCommand` / `ConvertNodeCommand` / `CorruptCommand`. The tag half
protects an idle ritualist; the command half protects any future verb
carrier whose tag nobody remembered to add.

Applied at every draft site in `SimpleAISystem`:

| Site | What it was doing |
|---|---|
| `TryLaunchAttack` idle-army collection | drafted the ritualist into the wave |
| `ReinforceActiveWave` | re-commanded it every 10 s, stripping a 35–45 s channel |
| curse-node reclaim squad | same |
| **base-defence recall** | **worst of the four** — unconditional, no busy check, so a Defend posture yanked a ritualist home mid-walk |
| `CountAliveMilitary` | counted a 300-supply caster toward the army floor |

The base-defence recall was not in the original report and is arguably the
more damaging of the two: `ReinforceActiveWave` at least skipped busy
units, while the recall skipped nothing.

### 7.2 Waves resolve and re-launch — fix #4

`SimpleAIState.WaveStartTime` added, stamped on launch. `ReinforceActiveWave`
now:

- **Counts arrivals instead of re-ordering them.** A unit idle within
  `WaveArrivedRadius` (30 m) of the wave target has arrived; re-issuing
  AttackMove to a spot it already occupies completes instantly, which is
  precisely what kept `sent > 0` forever and held a spent wave open. With
  arrivals excluded, `committed == 0 && sent == 0` finally fires and the
  wave retires — releasing the army so the next wave drafts it against a
  **fresh scored target** instead of re-walking a razed one.
- **Ages out at `WaveMaxLifetime` (150 s)** regardless. Backstop for units
  that keep an `AttackMoveTag` forever because they are stuck on terrain
  and read as "committed".

Expected cadence at Normal (`AttackWaveIntervalSeconds = 240`, halved to
120 under Pressure, `WaveBaseUnits = 5`, `WaveGrowthUnits = 2`,
`SustainArmyCap = 20`): a wave every 2–4 minutes with the army freed at
150 s, instead of one wave that ran 32 minutes and blocked every wave
after it.

New log lines to look for next match: `wave N SPENT — K unit(s) hold the
objective` and `wave N SPENT (lifetime)`. If `SPENT (lifetime)` dominates,
the arrival radius is too tight for the map scale.

### 7.3 Conversion owns its ritual claims — fix #5

Kind-filtered orphan sweep added to `ConversionRitualSystem`. See the
correction in §3.3: this was latent, masked by `PurificationRitualSystem`'s
unfiltered sweep. The change removes the hidden cross-system dependency
rather than fixing a live crash.

### 7.4 Curse nerf — see §8

---

## 8. Does the curse need nerfing? — yes, and the reason is the shape, not the speed

**Verdict: yes.** Two independent problems, one tuning and one structural.

### 8.1 It runs ~1.6× faster than its own stated target

`VeilCrustConstants` says the heartbeat is "tuned so an un-mined map takes
~1 hour to fully crust". Measured from the logs:

| Time | Coverage |
|---|---|
| 00:51 | 11.3 % |
| 10:52 | 21.8 % |
| 20:55 | 32.6 % |
| 30:57 | 58.2 % |
| 37:00 | 73.9 % |
| **38:00** | **90.9 %** |
| 51:01 | 91.9 % (plateau) |

Saturated at ~38 min against a 60 min target. Note also the **17-point
jump between 37:00 and 38:00** — the front does not accelerate smoothly,
it snaps, which is worth a look on its own (a burst that large is not
readable as counterplay).

**Applied:** `DormantMinSeconds` 120 → **190**, `DormantMaxSeconds` 200 →
**320** (scaled by 60/38 ≈ 1.58, the calibration the file's own comment
prescribes).

**Deliberately not touched:** `EscalationRampSeconds` (2400) and
`EscalationFloor` (0.5). Escalation was doing exactly what it advertises on
top of a base rate that was already overshooting; changing two multipliers
in one pass makes the next playtest unattributable. With the new base, full
escalation gives 95–160 s windows — still faster than today's *un*-escalated
120–200, so the late-game curse keeps its teeth.

### 8.2 The real problem: it is a cliff, not a slope

This matters more than the rate. The curse's economic effect is binary:

- Miners auto-flee crust at `ExposureFleeSeconds = 3 s`. While clean ground
  exists the curse costs **tempo**. The moment it doesn't, income is
  **exactly zero** — and a deposit that got crusted while unattended is
  permanently unreachable, because the worker flees on arrival, every time.
- Every suppressor that could reverse this is downstream of something you
  lose first: culture influence needs an economy, the Cleansed-well
  sanctify disc (18 m) and the Scholar font (26 m) need the verb chain,
  hero auras need heroes.
- So a faction that falls behind enters a death spiral — lose ground → lose
  influence → lose more ground. All three losers were caught in it: `emaS=0.0/s
  emaI=0.0/s` for 30+ minutes with 15+ gatherer huts standing and nothing
  to do. Blue's influence fell 7.3 % → 0.0 %; Green's 4.6 % → 0.0 %.

`HallHearthRadius` (20 m) is the only **unconditional** clean ground in the
game, and 20 m barely clears the Hall's own footprint.

**Applied:** `HallHearthRadius` 20 → **34**. A Hall now holds its inner
gathering ring workable no matter how bad the map gets — the floor a losing
player rebuilds an army from. Still far inside `SustainRadiusBase` (55), so
the curse keeps the field; it just cannot starve a base to a standstill.
Three consumers benefit consistently: the veil CA suppression stamp, the
mining-corruption immunity check, and the AI's `IsCoveredGround` placement
test.

### 8.3 Not done — flagged

- **Let an actively-worked deposit suppress the crust under it.** Makes
  re-taking a lost patch a decision instead of an impossibility. New
  mechanic, not a tuning change.
- **Investigate the 37→38 min jump.** A 17-point single-minute advance is
  not something a player can read or react to.
- **Curse thresholds should do something** (report fix #9). At 91.9 % the
  match is decided and nothing fires.

### 8.4 Still open from §6 — not addressed here

Fix #2 (post-game screen), #3 (retire a faction on economic death), #6
(public well-state UI), #7 (level the verb gates), #8 (persistent
corruption indicator), #10 (AI placement de-duplication), #11 (Runai AI).
#2 and #3 are what stop a decided match from running another 30 minutes;
#7 is what makes the Alanthor end-game reachable at all.
