# How Age of Empires IV Implements Its Skirmish AI

**A technical study for re-implementation in The Waning Border (Unity DOTS/ECS).**

Compiled from developer statements, the game's exposed SCAR/Lua API surface,
official patch notes, GDC material, and community analysis. Where AoE4
internals are unpublished, the matching standard technique is marked
**[Inference]**. The design decisions The Waning Border adopts from this
study live in [docs/Design/Game_AI.md](../Design/Game_AI.md).

## 1. Architecture — Relic's three-tier stack, not machine learning

AoE4 runs on Essence Engine 5 with Lua embedded as SCAR
([official scripting intro](https://support.ageofempires.com/hc/en-us/articles/4438362112660-Introduction-to-Scripting)).
The AI is **native-code hierarchical AI configured by per-personality Lua
data files** — the same framework Relic used in Company of Heroes and Dawn
of War (GDC 2007, ["Dealing with Destruction: AI From the Trenches of
Company of Heroes"](https://www.gdcvault.com/play/765/Dealing-with-Destruction-AI-From)).

The strongest public evidence is the in-game Lua API dumped by the
community ([aoemods lua-docs — Functions/AI](https://aoemods.github.io/lua-docs/modules/Functions_AI.html)),
whose function names reveal the module structure:

- `AIPlayer_UpdateGathering / UpdateSkirmishProduction /
  UpdateSkirmishAttackAndCaptureTasks / UpdateSkirmishScoutingTasks` —
  **separate strategic managers** (economy, production, military, scouting),
  each ticked as a discrete update task.
- `AI_CreateEncounter`, `AIEncounter_TargetGuidance_*`,
  `AIEncounter_CombatGuidance_*`,
  `AIEncounter_FallbackGuidance_EnableRetreatOnSuppression`,
  `AIEncounter_ResourceGuidance_SetResourceMoney` — the tactical layer is
  Relic's **encounter system**: a task-force object with pluggable guidance
  channels (target, combat policy, fallback/retreat, a **resource budget**)
  and a filtered tactic set.
- `AISquad_FindBestSquadTarget`, `AISquad_FindBestIsolatedSquadTarget`
  (isolated-target scoring = raid logic), per-squad blackboards — a squad
  tactical layer below encounters, run as **state trees** parameterized by
  "state model tunings".
- `AI_GetThreatMap`, `AI_GetAIThreatMapClusters`,
  `AIPlayer_IsPointThreatened`, `AIPlayer_GetBestClumpIdx` — a grid
  **threat/influence map**, clustered into enemy "clumps" scored for attack.
- `AI_GetDrawResourceImportanceMap` — a **resource-importance map** driving
  gathering/expansion.
- `AIPlayer_GetOrCreateHomebase` — homebase staging/return anchors.
- `AI_GetPersonality`, `AI_GetPersonalityLuaFileName` — **personality = a
  Lua data file of tunables**; difficulty is a first-class enum.

So: **AIPlayer (strategic managers + blackboard) → Encounters (operational
task forces with guidance & budgets) → Squads (tactical state trees)**.

**Machine learning: marketing vs reality.** Pre-launch marketing promised
ML-trained AI ([wccftech](https://wccftech.com/age-of-empires-iv-has-machine-learning-ai-that-could-eventually-become-unbeatable/)).
The GDC 2022 talk
["Age of Empires IV: Machine Learning Trials and Tribulations"](https://www.gdcvault.com/play/1027607/AI-Summit-Age-of-Empires)
shows what ML actually was: multi-agent RL for naval units (**never
shipped**) and supervised battle-outcome prediction. The shipped
decision-maker is classical scripted/utility AI.

**Think cadence:** not published. **[Inference]** Staggered periodic manager
ticks (0.25–2 s per manager, round-robin so no frame spikes), with squad
state trees ticking much faster for combat reactions.

## 2. Difficulty levels — behavior quality, not cheats

Shipping tiers: Easy, Intermediate, Hard, Hardest, plus (since Apr 2023)
Ridiculous, Outrageous, Absurd — 7 total.

- **No resource cheats at Easy→Hardest.** Dev statement: the AI has "an
  extremely efficient economy" and "they don't get any absurd resource
  buffs or other tricks like full map vision"
  ([forum](https://forums.ageofempires.com/t/how-does-the-a-i-cheat-in-aoe4/179666)).
  Tiers differ purely in behavior quality: economy efficiency, aggression
  timing, counter-unit usage, raid frequency, multi-pronged attacks.
- **Above Hardest, cheats are explicit and quantified**: Ridiculous
  **1.2×**, Outrageous **1.5×**, Absurd **2.0×** gather rate
  ([Patch 6.1.130](https://www.ageofempires.com/news/age-of-empires-iv-season-four-patch-6-1-130/)).
  History lesson: patch 5.2.131 silently gave *Hardest* 2× gathering; the
  community measured it and revolted; 6.1.130 reverted Hardest and moved
  multipliers into new clearly-labeled tiers. **Hidden eco cheats on a
  "fair" tier get measured and resented within weeks.**
- Observed per-tier deltas: **Easy** — slow economy, floats resources, rare
  probes, reduced raiding; **Intermediate** — functional economy, first
  attacks mid-game; **Hard** — early expansion, counter-units, constant
  multi-pronged raids, faster age-ups; **Hardest** — nonstop production,
  first attack ≈ **8 minutes**, flanking, trade harassment. Higher tiers
  "mix units based on what you're fielding"; lower ones don't adapt.
- **[Inference]** Each difficulty is a personality-Lua tuning set over the
  same engine: reaction delays, villager targets, permitted behaviors
  (raiding on/off), aggression thresholds, (top tiers) gather multipliers.

## 3. Economy management

- **Resource-preference weighting is data and patch-tuned**: 5.2.131
  reduced early stone preference, increased wood at Feudal transition, with
  civ-specific overrides. A resource-importance map drives villager
  placement.
- **Villager production**: continuous; high tiers target ≈ **100
  villagers** before tapering
  ([State of the AoE4 AI, Dec 2023](https://forums.ageofempires.com/t/state-of-the-aoe4-ai-december-2023/245464)).
- **Expansion**: builds TCs on outlying resource patches early-mid game
  (does not consolidate when depleted — a known weakness).
- **Economy defense**: villager fight-vs-flee is a tuned casualty-tolerance
  threshold; fishing boats flee/garrison when threatened.
- **[Inference]** Standard match: a gatherer-allocation solver over desired
  per-age resource ratios, re-run every few seconds, assigning workers to
  highest-importance deposits within threat-safe areas.

## 4. Build orders & tech

- No AoE2-style authored per-civ scripts; behavior is **goal-driven**
  (age up quickly → train → attack), with civ specialization expressed as
  data — and leaky: years of patch notes fixing civ-specific AI failures
  (Ottoman stuck in Dark Age, French never researching Merchant Guilds…)
  are the signature of **one generic engine + per-civ data**.
- **Adaptivity**: higher difficulties add counter-units vs the player's
  composition but follow the same patterns across difficulties.
- **[Inference]** Matching technique: utility-scored production requests —
  each manager posts weighted requests into a prioritized spend queue;
  age-up is a request whose utility rises with worker count/income and
  falls with immediate threat.

## 5. Military

- **Armies are encounters launched from a homebase** ("armies always come
  from and return to the main base" — no forward bases, a documented
  weakness).
- **Target choice**: threat-map clump scoring + dedicated siege-target and
  capture-point subsystems; raids target isolated squads. Failure modes:
  wrong building priorities, ranged armies diving Keeps.
- **Retreat**: encounter fallback guidance; **[Inference]** trigger =
  predicted engagement loss over local strength comparison.
- **Static defense**: since the Anniversary update the AI detects
  **chokepoints (river crossings, bridges)** and builds Outposts/Keeps
  there; towers protect resource camps; **walls are almost never built**
  (documented weakness).
- **End game**: "AI now will prioritize finishing off enemies when it has
  advantage to do so" ([Update 24916](https://ageofempires.fandom.com/wiki/Update_24916)).

## 6. Scouting & information

- **Respects fog of war** (dev-confirmed, no map hack). Dedicated scouting
  manager; Anniversary update made scouts aggressive early (enabling
  early rushes on harder tiers), threat-aware (flee when attacked, avoid
  high-threat areas).
- Persistent world model = the threat map with clusters; **[Inference]**
  entries decay when unobserved (standard influence-map treatment). The AI
  does not shadow enemy armies (stale-intel failure mode).

## 7. Personalities

The engine supports Lua personalities (`AI_SetPersonality`), but AoE4
exposes none in the UI — difficulty is the only player-facing axis.
**[Inference]** Each difficulty maps to a personality file; "Hardest" is
"Hard" with more aggressive values, not different code.

## 8. Known concrete numbers

| Value | Source |
|---|---|
| Ridiculous/Outrageous/Absurd gather mult: 1.2× / 1.5× / 2.0× | Patch 6.1.130 |
| Hardest 2× gather (Jan–Apr 2023 only, then reverted) | Patch 5.2.131 |
| First Hardest attack ≈ 8 min | Steam community measurement |
| High-difficulty villager target ≈ 100 | State-of-the-AI thread |
| Repair crews capped at 2 villagers per structure | State-of-the-AI thread |
| 7 difficulty tiers (4 fair + 3 labeled-cheat) | gamerblurb guide |

Think intervals, aggression thresholds and comp weights are not public
(AI files are not extractable).

## 9. Patch-history lessons (the tunable surface)

The chronology (Update 24916 scouting/chokepoints → 5.2.131 resource
weights/Hardest cheat → 6.1.130 cheat tiers → seasonal civ fixes → 16.1
win-condition tactics and team coordination) shows the real tunable
surface: *gather multipliers, per-resource desire weights,
aggression/production timing, flee thresholds, scout policies,
chokepoint/static-defense toggles, per-civ production tables*. Recurring
failure modes: naval/transport logistics, walls, siege micro,
civ-mechanic special cases, late-game goal fixation.

## 10. Re-implementation blueprint (Unity DOTS/ECS)

1. **Per-AI-faction blackboard** + a **personality/difficulty data profile**
   (the Lua-personality equivalent) holding every tunable.
2. **Managers as staggered ticks**: economy (worker curve, gatherer
   allocation, expansion), production (single prioritized utility spend
   queue), military (desired-composition vector + counter matrix,
   encounters), scouting (find enemy early, then perimeter sweeps,
   threat-aware flee), tech/age-up folded into the spend queue.
3. **Encounters as the army abstraction**: objective, members, budget,
   guidance (retreat threshold), Form → Move → Engage → Fallback states.
4. **Spatial layer**: threat/influence map with decay + clustering;
   resource-importance map; chokepoint detection for static defense.
5. **Difficulty = data only**: one brain; per-tier knobs (think interval,
   decision latency, worker target curve, first-attack earliest time,
   raiding/counter-comp toggles, micro quality). Keep fair tiers honest;
   label any cheat tiers explicitly.
6. **Do better than AoE4's documented weaknesses**: forward staging
   positions, real wall/defense budgets, economy consolidation, team
   attack synchronization.

### Primary sources
- [AoE4 in-game Lua AI API (aoemods)](https://aoemods.github.io/lua-docs/modules/Functions_AI.html) — architecture ground truth
- [Patch 5.2.131](https://www.ageofempires.com/news/age-of-empires-iv-update-52131_lunar_faire/) · [Patch 6.1.130](https://www.ageofempires.com/news/age-of-empires-iv-season-four-patch-6-1-130/) · [Update 24916](https://ageofempires.fandom.com/wiki/Update_24916) · [Update 15.1.6970](https://www.ageofempires.com/news/age-of-empires-iv-season-twelve-update-15-1-6970/) · [Update 16.1.9737](https://www.ageofempires.com/news/age-of-empires-iv-update-16-1-9737-and-yue-feis-legacy-dlc-release-preview/)
- [GDC 2022: AoE4 ML Trials and Tribulations](https://www.gdcvault.com/play/1027607/AI-Summit-Age-of-Empires) · [GDC 2007 CoH AI talk](https://www.gdcvault.com/play/765/Dealing-with-Destruction-AI-From)
- [How does the A.I. cheat?](https://forums.ageofempires.com/t/how-does-the-a-i-cheat-in-aoe4/179666) · [State of the AoE4 AI Dec 2023](https://forums.ageofempires.com/t/state-of-the-aoe4-ai-december-2023/245464) · [AI Modding in AoE4](https://forums.ageofempires.com/t/ai-modding-in-aoe4/230269)
- [gamerblurb difficulty guide](https://gamerblurb.com/articles/age-of-empires-iv-ai-difficulty-levels-guide) · [aoe4.club patch index](https://www.aoe4.club/en/patchs)
