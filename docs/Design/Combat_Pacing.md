# Combat Pacing — the Meta Ladder & Counter System

Canonical source for **match pacing**: which unit compositions define each
phase of a match, which units counter which, and the wall-siege rule.

> **Data lives on the SOs (2026-10-03, [Unification decisions](Unification_Decisions_2026-10-03.md)).**
> This doc owns the RELATIONSHIPS — who counters whom, which tags a unit
> must carry, how armour classes are ordered and why. The NUMBERS (every
> `bonusVsTags` amount, armour value, damage, range and line of sight) live
> on the unit and building SOs under `Assets/GameData/TechTree/`, and on a
> numeric conflict the SO wins. Read them in the generated calculator
> (`tools/calculator/TechTree.html`, built from the SOs by
> `tools/gen_calculator.py`). Change a relationship here first; change a
> number on the SO.

The game has only two ages ([Overview.md](Overview.md) — Age 0 and the
cultured Age 1, progression via building levels L1-L3). The match still
moves through **five meta beats**; the later beats are keyed to building
levels and the endgame loop, not to further ages.

---

## The five meta beats

| Beat | Keyed to | Defining meta |
|------|----------|---------------|
| **0 — Skirmish** | Age 0 | **A melee age**: spearmen and workers. Ranged is an Age-1 unlock (2026-08-11 — the Age-0 archer rush was uncounterable and ended matches by minute 15), so the age is about spear lines, map reading, and the economy race to age-up. **The race is SHORT by design: median age-up lands at 3-6 minutes depending on difficulty (2026-08-29) — Age 0 is a prologue, not a third of the match.** |
| **1 — Lines** | Age-up, buildings L1-L2 | The bow arrives: archers and crossbowmen enter alongside swordsmen (the Archery Range unlocks at era 2) and beat the Age-0 comps. Walls appear: **walls keep you mostly safe — only siege units can attack wall pieces** (see The Wall Rule below). |
| **2 — Maneuver** | Buildings L2-L3 | Early cavalry wins every open encounter **except against spearmen**. Longbowmen (Range L3) rule the field, urging the advent of the cataphract. Early siege cracks hard targets and brings area damage (Trebuchet). |
| **3 — Game-enders** | L3 + veterancy / equipment tiers | Longbowmen and cataphracts answer almost everything. The triangle closes: **cavalry counters longbowmen; crossbowmen counter cataphracts; spearmen still counter cavalry**. |
| **4 — The Shardroot** | Endgame loop | The match revolves around escorting religious units to wells and holding the Shardroot — see [Curse_And_Shardroot.md](Curse_And_Shardroot.md). Armies exist to screen ritualists and break enemy holds. |

Pacing is therefore tuned through **building-upgrade timing and cost** — each
beat transition is a level gate, not a stat patch.

---

## Combat model (context)

Damage is AoE4-style (`CombatModifiers.CalculateFinalDamage`):

```
final = max(1, baseDamage - flatArmor) + bonusVsTags
```

The old damage-type x armor-type multiplier matrix is **retired** (UI
counter-hints only). All hard counters live in per-unit `bonusVsTags`
entries — flat bonus damage vs a target tag, added **after** armor and
ignoring it. Tags: Infantry, Cavalry, Ranged, Siege, Heavy, Light,
Building, Worker, Religious, Ship.

### Buildings support armies; they do not replace them (2026-10-07)

Developer: "building attack value is too high and towers melt through any
army — rebalance in favour of armies." A capital or a tower is a deterrent
that makes a small raid costly; it must never out-damage the army sent to
take it. The rule the SOs follow: a fully levelled Fortress or Watch Tower
deals no more damage per second than a handful of line infantry, and hits
few targets at once. The Fortress, Watch Tower (all levels) and Renewal's
raised defences were cut to fit on 2026-10-07; the values are on their SOs
and in `tools/calculator/TechTree.html`.

### Fortifications cost real money (2026-10-07)

Developer: "increase fortification prices". With 100,000-unit banks a stone
wall cost next to nothing. Every fortification's price was doubled on its SO
(wall hub and module, palisade hub and module, gate, wall tower, Watch Tower
and its level-ups, both emplacements). And the extractors' level-ups pay less:
each level's gain over level 1 was halved on the slot-income ladders (Guild,
Mine, Gatherer's Hut, Veilstone Mine) and the Trading Outpost's trade-rate
ladder, so levelling is a modest step, not a doubling.

### Flanking (2026-10-04)

**A melee hit that lands on a unit's side or back deals more damage.** The
rule applies to everyone alike: players, the AI and the curse's own units.

- **Arcs are the DEFENDER's.** The defender's facing is its current yaw (the
  way its model points, XZ only). The hit's direction is the line from the
  defender to the attacker. Inside the **front arc** — within
  `frontArcHalfAngleDegrees` either side of the facing, boundary included —
  the hit is a normal hit. Anywhere outside it is a **flank hit**.
- **Side and rear are one arc with one bonus.** There is no separate "rear
  attack" tier: a blow from the side and a blow from directly behind both
  multiply by `flankDamageMultiplier`. Both values are named fields on
  `MeleeCombatSystem.asset` (beside `Systems/Combat/Attacks/MeleeCombatSystem.cs`);
  this doc does not restate them.
- **Melee only.** Only a contact swing resolved by `MeleeCombatSystem` can
  flank. Arrows, bolts and every projectile, splash, spell, ability, bleed,
  burn, damage over time and the Godsplinter's siege slam never do. Riders
  that are computed FROM the swing (the Shardbound cleave share, the
  Bloodletter whirl) carry the swing's flanked number with them; they do not
  test an arc of their own.
- **Only units can be flanked.** A building has no facing: buildings, wall
  hubs, segments, gates and towers, curse nodes and every other structure
  always take a front hit.
- **Facing is real and turns at a finite rate.** A unit swinging at its own
  target turns toward that target (at the shared unit turn rate); a moving
  unit faces where it walks. So a unit already locked in front of one enemy
  keeps its back to a second one for as long as that fight lasts, and a
  fleeing unit shows its back to everyone chasing it. A unit that turns to
  answer a flanker closes its own flank within a fraction of a second — the
  bonus rewards attacking an engaged or retreating unit, not merely walking
  around an idle one.
- **Where it sits in the damage math.** Flanking is a multiplier on the
  post-armour total, on the same layer as the height and veilstone/frenzy
  multipliers:

  ```
  final = max(1, round( (max(1, base - armour) + bonusVsTags)
                         x height x veilstone/frenzy x flank ))
  ```

  It therefore scales the `bonusVsTags` counter damage too, and armour is
  subtracted once, before it — armour is never "flanked around". Everything
  that already came after the formula still comes after it, unchanged:
  on-hit bonus damage riders, the target's damage-taken multiplier (Liquid
  Courage), then the shield.
- **Measured.** Every melee hit and every flank hit is counted per attacking
  faction per minute in the match metrics (`Metrics_Combat.csv`, columns
  `meleeHits` / `flankHits`), so a headless batch can read how often the rule
  fires. Nothing is logged per hit.

### Shield points are hit points

**There is no difference between shield and HP.** A unit with shield points
(the equipment-tier `ShieldBar` granted from the Veilstone tier up and larger
at Veilsteel, plus the siege Veilstone+ aura's `AuraShieldBoost` while in
range; amounts in `EquipmentTierConfig`)
simply has that many extra hit points, and **the next damage draws from the
shield first**. Only what the shield cannot cover reaches Health.

- **Every source** obeys it: melee (and the Shardbound cleave), ranged,
  projectiles and their splash, spells, reflected damage, damage over time,
  burning ground, bleed, curse exposure, death blasts.
- The hit is resolved **before** the shield: armor, `bonusVsTags`, damage-taken
  multipliers (Liquid Courage) and Invulnerable all apply to the incoming
  number, and the result is what the shield pays. A shield is not armor and
  does not reduce a hit — it only absorbs it.
- **A shielded unit can never die from damage its shield covered.** Overkill
  is exact: 30 damage against 10 HP + 50 shield leaves 10 HP + 20 shield.
- A hit that the shield absorbed in full still **counts as a hit**: kill credit
  (`LastDamagedByFaction`) is recorded, the damage ledger counts it, and it
  resets the shield's regen gate.
- Life Cling / Second Wind floors protect **Health**, never the shield: the
  shield is spent first, then the floor holds whatever reaches Health.
- Regen: a fixed number of points once per whole second, after a delay
  without a hit (`EquipmentTierConfig.ShieldBarRegenPerSecond` /
  `ShieldBarRegenDelay`); never above the current max. Max follows the tier (and the siege aura) live.
- The player sees it: a **veilstone-cyan segment** continuing the HP fill on
  the floating health bar (the bar rescales to HP + shield when that exceeds
  max HP), and the selected-unit HP line reads `cur/max + shield`.

Implementation: `ShieldDamage.Absorb` (`Systems/Combat/ShieldBarSystem.cs`) is
called at the point of damage by `MeleeCombatSystem`, `ProjectileSystem`,
`CombatDamageHelper` (reflect / stakes), `DamageOverTime.Commit` (and through
it `SpellDamage.Apply`) and the blast / explosion systems.
`ShieldDamage.RefundUnobserved` is the order-safe backstop for any Health write
that has not been routed through it yet: it refunds an unobserved Health drop
out of the shield in `ShieldBarSystem` and again at the top of `DeathSystem`'s
death check. Any new damage path must call `ShieldDamage.Absorb` before it
writes Health — the backstop is exact only for unclamped writes.

---

## Armor (how it is authored)

Armor is **subtracted**, so a point of it is worth a fixed number of hit points
*per hit* — and therefore worth wildly different amounts depending on what is
hitting. A few points halve a light arrow and are a rounding error against a
trebuchet stone. That inversion is the whole design: it is what lets a
heavy unit genuinely counter light attacks without being good against
everything at once, and it is why armour is authored against the attacks that
will actually land on a unit rather than picked as a percentage.

The `Defense` component's doc comment claimed a diminishing-returns percentage
(`d / (d + 100)`) until 2026-08-28. That formula has not been in the game for a
long time; anything that reasoned about durability from it was wrong by an
order of magnitude, `UnitPower` included.

### Units

The values are on each unit SO (`defense`). What this doc fixes is the
ORDER and the reason for each row:

| Role | Armour shape | Why |
|---|---|---|
| Worker / Ledger | none (a little magic at most) | Not meant to survive contact |
| Scout | a token ranged point | Survives by not being there |
| Litharch | magic only | Robes: the magic column is their only protection |
| Spearman | light | Cheap line infantry; its counter is its bonus vs Cavalry, not its armor |
| Swordsman | mail — clearly above the Spearman in melee | Takes visibly less from a spear than a Spearman does |
| Nobleman | above the Swordsman | |
| Sentinel | the heaviest foot armour | The wall. A plain Archer barely scratches it |
| Archer / Longbowman | no melee armour, a token ranged point | Glass. Zero melee armor is what makes cavalry the answer |
| Crossbowman | light | |
| Outrider | light, both columns | |
| Cataphract | heavy (barded) | Heavy — but the Spearman's and Crossbowman's bonuses land AFTER armor and ignore it, so the counters still hit in full |
| Ballista / Catapult / Trebuchet | no melee armour, high ranged armour | Arrows bounce; swords do not. You cannot shoot a siege line down, you send something at it |
| Battering Ram | some melee, very high ranged | Armoured shell. An Archer does the minimum to it |
| King Lexor | heavy across the board | |

**Siege armor is 0 on every unit** — the mobile engines and the wall
emplacements' engines (`Alanthor_EmplacedBallista` /
`Alanthor_EmplacedTrebuchet`) included (decision 35). Siege damage is the
universal answer, and a siege-armor column that did anything would make its
own counter unreliable. *(The 0 is the rule, not a tuning value.)*

### Buildings

The shape of every building row is one statement: **arrows do almost nothing,
infantry chips slowly, siege goes through.** Classes in rising order; the
values are on each building SO:

| Class | Members |
|---|---|
| Light | Gatherer's Hut |
| Standard | Hut, Archery Range, Royal Stable, the landmarks |
| Military / industrial | Barracks, Siege Yard |
| Core | Temple; the Fortress (the capital carries its own defence block on its SO) |
| Fortification | Watch Tower, Wall, Wall Tower, Gate |

Ranged armor is set **at or above a bow's entire attack** — so an Archer does
the minimum 1 to a wall and a bow line simply *cannot* take a base. That is
what makes the Siege Yard a necessary building rather than an optional one.
Siege armor stays 0 everywhere, so a Ballista's damage and its bonus vs
Building land in full.

Infantry keeps a slow path in on purpose: a Swordsman takes a Barracks down in
dozens of swings. Possible, never efficient — the AoE relationship.

---

## Counter table (which `bonusVsTags` each unit carries)

The amounts are on the unit SOs (`bonusVsTags`); this table is which TAG each
unit is bonused against and the beat it delivers.

| Unit | Bonus vs | Delivers the beat |
|------|----------|-------------------|
| Spearman (Age 0) | **Cavalry** | The one thing early cavalry loses to (beats 2-3) |
| Alanthor_Crossbowman | **Cavalry** | Bolts pierce barding — the cataphract answer (beat 3) |
| Alanthor_Cataphract | **Ranged** | Runs down longbow/crossbow lines (beats 2-3) |
| Alanthor_Outrider | **Ranged** (smaller than the Cataphract's) | Light harasser version of the same job |
| Alanthor_Ballista | Building | Hard-target cracker (beat 2); in the field it hunts heroes, heavy cavalry and engines (§ Target preference) |
| Alanthor_Trebuchet | Building (the largest) | Area siege, wall-line killer (beats 2-3) |
| Alanthor_BatteringRam | Building (the largest) | Buildings-only attacker (`BuildingsOnlyAttacker`) |
| Alanthor_Archer | **Infantry** | Closes the triangle (below) - massed bows clear a foot line |
| Alanthor_Swordsman | **Siege** | Infantry is how a siege line dies; siege carries 0 melee armor to match |
| Alanthor_Sentinel | **Heavy** | Gives the tank something it can actually kill: elite armour |
| Alanthor_Catapult | **Building and Infantry** | Splash - the anti-mass answer as well as a wall-breaker; aims at the densest knot (§ Target preference) |

### Target preference (2026-10-03)

The two **anti-army** siege engines are built to pick a kind of target, and
they do it when they choose their own: a unit carrying `preferTargets` on its
SO takes a preferred candidate **inside its own attack reach** over the
nearest one (TargetingSystem's auto-acquire; `TargetPreference` component).

| Unit | Prefers | Why |
|------|---------|-----|
| Alanthor_Ballista | `Hero`, `Cavalry+Heavy`, `Siege` | Single heavy bolts: heroes, barded cavalry and enemy engines — the high-value targets a bow line cannot finish |
| Alanthor_Catapult | `Massed` (the candidate standing among the most enemies) | Splash: put the stone where the formation is thickest |

Rules: an entry is tags joined by `+` (the candidate must carry all of them),
`Hero`, or `Massed`; units only, never buildings; only inside reach, so a
preference never drags an engine off its post or out of its formation; an
ORDERED target is never overridden. Every other unit keeps the nearest /
value pick. The Battering Ram and the Trebuchet need no preference — the Ram
attacks buildings only and the Trebuchet's work is walls. The AI composes by
the same roles (Game_AI.md §5d).

### The triangle

The counter set is a closed rock-paper-scissors, and a new unit should be placed
against it rather than given a bonus in isolation:

- **Infantry beats Cavalry** - the Spearman's bonus vs Cavalry.
- **Cavalry beats Ranged** - the Cataphract's and Outrider's bonus vs Ranged.
- **Ranged beats Infantry** - the Archer's bonus vs Infantry. **This leg was
  missing until 2026-08-28**: the first two legs were authored and the third
  was not, so infantry had no natural predator and massing Spearmen answered
  everything except the cavalry charge the Spearman already countered.

Siege sits outside the triangle: it beats Buildings, and Infantry beats it.

**Longbowmen still carry no bonus.** Their dominance is raw stats — the top of
the bow ladder in damage and reach. Giving them the anti-infantry leg as well
would leave them strong against two classes of three, which is exactly what
makes the cavalry counter load-bearing.

### Tags are what make any of this fire

A bonus matches the target's `tags`, so **a unit with no tags cannot be
countered by anything** - every bonus in the table above silently reads 0
against it. As of 2026-08-28 that was true of every sect unit, the Ledger and
the Bazaar Wagon; and the Feraldis Raider was tagged `Infantry` while being
cavalry, so it walked through the anti-cavalry counter untouched.

Tag vocabulary, case-insensitive (`UnitTagParse.Tag`): Infantry, Cavalry,
Ranged, Siege, Heavy, Light, Building, Worker, Religious, Ship. An unrecognised
tag parses to 0 and is silently ignored, so a typo reads exactly like no tag.

**Every combat unit needs a class tag (Infantry / Cavalry / Ranged / Siege) and
a weight tag (Heavy / Light).** The Age 0 and Alanthor weights (decision 35,
2026-10-03):

| Weight | Units |
|---|---|
| **Light** | Outrider; Archer, Crossbowman, Longbowman; Scout, Worker, Ledger |
| **Heavy** | Spearman, Swordsman, Nobleman, Sentinel; Cataphract; Ballista, Catapult, Trebuchet, Battering Ram; the emplaced Ballista and Trebuchet; King Lexor |

### The ranged ladder

Rules for the three bow lines (values on the `Alanthor_Archer`,
`Alanthor_Crossbowman` and `Alanthor_Longbowman` SOs). Rebalanced 2026-08-13 —
the old numbers had the **Archer out-ranging the Crossbowman** despite sitting
below it on the ladder, and every line shot further than it could see.

**No ranged unit has a minimum range** (2026-08-28). Only SIEGE keeps a dead
zone — an engine that cannot depress its arc is modelling something real; an
archer backing away from a swordsman is not. In play it cost about a third of
every engagement: the bow lines walked backwards to satisfy a dead zone instead
of shooting, and read as though they were refusing to fight.

Note for anyone re-authoring this: **`minAttackRange: 0` used to mean 10 m.**
`RangedCombatSystem` applied a `DefaultMinRange` of 10 whenever the unit's own
value was zero, so the data could not express "no dead zone" at all. Zero is
taken verbatim now, in both the combat system and the steering halt band.

Three rules hold across the ladder, and new ranged units must respect all:

- **Range never exceeds line of sight.** A unit that outranges its own vision
  can only use the difference through someone else's eyes, which reads as
  shooting at nothing. Range and sight are set equal.
- **Range rises with the ladder.** Damage and reach both increase
  Archer → Crossbowman → Longbowman, so the ordering is unambiguous and a
  higher-tier bow is never a sidegrade.
- **No minimum range** (above).

Longbowmen deliberately carry **no** bonus tag — their dominance is raw
stats, which is exactly what makes the cavalry counter necessary.

Runai / Feraldis counter data follows the same pattern when those trees
are unlocked; the triangle roles (anti-cavalry spear, armor-piercing
crossbow, line-running cavalry) are cross-culture.

### Arrow tips are visible in flight

The arrow-tip research ladder — the same four techs in all three cultures —
is read off the **trail an arrow leaves**, and a faction that has bought
nothing leaves none at all:

| Research | Trail |
|---|---|
| *(none)* | **no trail** |
| Stone-tipped arrows | faint grey, short |
| Iron-tipped arrows | grey |
| Veilstone-tipped arrows | **blue, emissive** |
| Shard-tipped arrows *(the Veilsteel tier)* | **golden, emissive** |

Length and width climb with the tier alongside the colour, because the camera
is never close enough to see an arrowhead and colour alone is a weak read at
distance.

**The base arrow leaving nothing is the point.** Every arrow used to leave the
same white streak, so a fully-upgraded army looked exactly like a starting one
on the field — the one place the ladder matters. Giving tier 0 no trail is what
gives the first upgrade something to be.

Ballista bolts share the look. They are shot by the same army from the same
racks, and the design has no separate bolt ladder to read them against.

Implementation: `GameSystems/Rendering/Vfx/ArrowTrailTiers.cs`. The tech id for
tier 4 is `ShardTippedArrows`, not "VeilsteelTipped" — older name, same tier,
and Veilsteel is what it costs.

---

## The Wall Rule

**Only siege can attack walls.** Any entity carrying `WallTag` — hubs,
curtain instances, wall towers, wall gates — can only be damaged by
attackers whose damage type is **Siege**.

- Non-siege units never auto-acquire wall pieces, and refuse a force-order
  against one (target dropped, same contract as the Battering Ram's
  buildings-only rule).
- Ordinary buildings (barracks, huts...) are NOT covered — any unit
  may still raze them. The rule protects the fortification line only.
- The Border is not exempt and needs no exemption: its wall answer is the
  **Godsplinter** (siege class). Curse pressure against a walled base
  otherwise comes from hostile ground, not from creature chip damage.
- **Buildings obey it too** (2026-09-26). A Fortress, tower, Keep or wall tower
  firing arrows never auto-acquires a wall piece and its arrows do no damage
  to one. The rule is enforced where damage LANDS (`ProjectileSystem`, direct
  hit and splash), so no future shooter can leak past it; target selection
  filters walls out only so no volley is wasted. The Fiendstone Keep's
  Ballista / Trebuchet emplacement shots are siege and may still be aimed at
  a wall — when no other target is in range, the siege shots take the
  nearest enemy wall piece.
- **The player is told.** Right-clicking an enemy wall piece with a
  selection that holds no siege shows *"Only siege can damage walls"* and
  issues no order — a doomed attack order used to be silently dropped by the
  combat system, which read as the units ignoring the player. With at least
  one siege unit selected, the siege units attack and the rest of the
  selection is left alone.

## Directed building fire

Every building that shoots (`BuildingRangedAttack` — watch / totem
towers, Fiendstone Keep, Fortress, wall towers) auto-fires at the nearest
enemies in range, up to its **MaxTargets** at once. The player may also
**direct** that fire (2026-09-26):

- Select one or more of your shooting buildings and **right-click an enemy**:
  that enemy becomes the building's **forced target**. Right-clicking ground
  or a resource still sets the rally point, unchanged.
- The forced target takes **one** of the building's MaxTargets slots —
  always the first — while it is in range. The other slots keep auto-firing
  at the nearest enemies exactly as before, so directing a Keep's fire never
  costs it its volley.
- If the forced target **leaves range the order is kept**; the building
  auto-fires normally meanwhile and snaps back onto the target when it
  returns. The order ends when the target dies or stops being a legal
  target (allied, veiled, an untargetable node), or when the player issues
  **Stop** or a new forced target.
- **The Wall Rule applies.** A building whose fire is not siege cannot be
  directed onto a wall piece; the order is refused with the same notice.
- **Emplacements** are two entities (the platform and the engine on it).
  Right-clicking an enemy with the platform selected orders its ENGINE to
  attack, through the ordinary unit attack order.
- A mixed selection does both: the units attack, and the buildings in it
  take the forced target.
- Replicated: one `BuildingAttack` lockstep command per building
  (`CommandRouter.IssueBuildingAttack`); a clear (Stop) is the same command
  with no target.

Consequence for the meta: beat-1 walls genuinely shelter a base until the
opponent fields beat-2 siege — which is the intended pacing lever.

---

## AI conformance

The AI rides this ladder automatically (`SimpleAISystem` /
`AIAlanthorEndgameSystem`):

- Composition picker trains the best trainable unit per class —
  Longbowman > Crossbowman > Archer, Swordsman > Spearman — and holds
  spearmen while enemy cavalry dominates sightings.
- Cavalry from the Royal Stable (Cataphract > Outrider), siege from the
  Siege Yard. All four engines (Battering Ram, Ballista, Catapult,
  Trebuchet) coexist on the Siege Yard roster.
- Fortress uniques (Ledger, King Lexor) train once, outside the
  budget window.
- The wall doctrine ([Game_AI.md](Game_AI.md)) seals terrain chokepoints
  or encloses the base, with gates and wall towers.
