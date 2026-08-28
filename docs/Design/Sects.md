# Sects

Canonical design for the 12-sect religion layer. Supersedes the sect sections of
`.deft/tasks/task-sect-system-redesign-063/task.md` where they disagree, and
supersedes the shipped `SectLeverEffects` numbers (the code is being aligned to
this document, not the other way round).

Companion visualization: [`docs/SectReference.jsx`](../SectReference.jsx) — open
[`docs/SectReference.html`](../SectReference.html) to read it.

---

## 1. What a sect grants

Every sect grants exactly five things. **There is no chapel aura** — a sect
projects no passive area effect unless its Passive or its Research explicitly
says so. (This removes the old `SectLeverEffects.AuraOf` table.)

| Slot | Count | Notes |
|---|---|---|
| **Active powers** | 3 | Each has levels I / II / III |
| **Passive** | 1 | Live only while the Temple stands |
| **Building** | 1 | **Limit 5 per faction.** Authored mesh. It is where the sect's unit is trained and its research is bought |
| **Unit** | 1 | Trained at the sect building; capped at 5. Authored mesh |
| **Research** | 1 | Bought at the sect building; one faction-wide effect |

The building is the sect's footprint on the map: adopting a sect unlocks it,
and everything else the sect sells — its unit, its research — is bought there.
Five is a real cap, not a soft one; the placement panel greys out at five.

## 2. Casting radii — exactly four

Every targeted effect uses one of these four. No other radius exists; do not
author a bespoke number.

| Name | Radius | Use for |
|---|---|---|
| **Single Target** | one entity | surgical effects, execute-style damage, building shutdown |
| **Small** | 8 m | a squad, one building cluster |
| **Medium** | 15 m | an engagement, a base district |
| **Large** | 25 m | an army, a whole base |

## 3. Power levels come from adoption timing

A power's level is **how many Temple upgrades happened while the sect was
already adopted**, capped at III.

- Adopt a sect, then upgrade the Temple → that sect's powers go I → II.
- Upgrade again → II → III.
- Adopt a sect *after* the Temple is already maxed → its powers stay at I.

**Early adoption is the reward.** A sect taken at the first opportunity reaches
III; a late pick-up is a level-I sect for the rest of the match. This replaces
the old model where power tier tracked a purchasable Active-Power lever.

Implementation note: the level must be *stored per adopted sect at adoption
time* (an adoption-epoch or a level counter incremented by the Temple upgrade
for every already-adopted sect) — it cannot be derived from the current Temple
level alone, because that would retroactively reward late adopters.

---

## 4. Alanthor cluster

### Sect of Antiquity — *the holy librarians*
Identity: intel and enemy shutdown.

**[ACTIVE] Scour the Registry**
- **I** — Reveal a **medium** area for 15 s.
- **II** — Reveal a **large** area for 15 s.
- **III** — Reveal a **large** area for 35 s.

**[ACTIVE] Heavy Bureaucracy**
- **I** — **Single target** building stops training, research and resource output for 30 s.
- **II** — Buildings in a **small** area stop for 30 s.
- **III** — Buildings in a **large** area stop for 30 s.

**[ACTIVE] Sew Disorder**
- **I** — Units in a **small** area turn hostile to all other units for 8 s.
- **II** — Units in a **medium** area turn hostile for 20 s.
- **III** — Units in a **large** area turn hostile **until killed**.

**[PASSIVE] Tally of the Lost** — units gain +damage per unit-type they have
killed this match, tracked **per unit type**. Alanthor Cataphracts that have
killed 12 Archers deal +12 % damage to Archers; caps at **+15 %**. The tally
belongs to the unit *type*, not the individual — replacements inherit it.

**[BUILDING] Reliquary** — a vaulted archive. Every Reliquary standing shortens your
sect-power cooldowns a little, so spreading them out is the Antiquity tempo play. **Limit 5.**

**[UNIT] Lore Keeper** — acts as a Ledger, and additionally empowers military
buildings for faster training and more damage. **Up to 5.**

**[RESEARCH] Royal Index** — all technologies and building upgrades take 30 %
less time and 10 % fewer resources.

### Sect of Renewal — *the menders*
Identity: repair and sustain.

**[ACTIVE] Hands of Plenty**
- **I** — Restore 30 % HP to units and buildings in a **small** area.
- **II** — Restore 50 % in a **medium** area.
- **III** — Restore 80 % in a **medium** area, and healing continues for 10 s.

**[ACTIVE] Raise Anew**
Conjures Watch Towers outright — it does not touch construction queues.
- **I** — Raise one free **Lv 1** Watch Tower at a **single target** point. It crumbles after 30 s.
- **II** — Raise **Lv 2** Watch Towers across a **small** area. They crumble after 60 s.
- **III** — Raise a **Lv 3** Watch Tower at a **single target** point. It is permanent — it stays until destroyed.

> Note: the escalation here is tower level and permanence, not reach. III returns to
> Single Target on purpose: a permanent free Lv 3 tower is the payoff, and granting
> several of them would out-value every other level-III power in the game.

**[ACTIVE] Second Wind**
- **I** — Units in a **small** area cannot drop below 1 HP for 6 s.
- **II** — **Small** area, 12 s.
- **III** — **Medium** area, 12 s; survivors heal 25 % when it ends.

**[PASSIVE] Hands That Mend** — your buildings auto-repair at 2 % max HP/s
while out of combat.

**[BUILDING] Mending Hall** — an open-sided infirmary. Damaged units that walk inside
heal over time; it is the only place a Scar Guard is trained and the only place
Field Hospital is researched. **Limit 5.**

**[UNIT] Scar Guard** — heavy frontline infantry that deals **more damage the
closer it is to dying**. Pairs deliberately with Second Wind, which pins it at
1 HP: a full-strength Scar Guard line that cannot die is the sect's payoff
combo. **Up to 5.**

**[RESEARCH] Field Hospital** — your **Litharchs** unlock **Deploy Field
Hospital**: a 3 s cast that raises a temporary infirmary at the caster's feet,
healing allied units around it and then destroying itself after 2 minutes.
300 s cooldown.

> The Litharch is an Age 0 unit every culture trains, so this is the one sect
> research that reaches back and re-arms a pre-culture unit. That is the point:
> Renewal's identity is sustain, and the mobile hospital is the field half of
> what the Mending Hall does at home. Only a faction that has adopted Renewal
> and raised a Mending Hall can buy it.

### Sect of Fortitude — *the wall-keepers*
Identity: static defense.

**[ACTIVE] Stoneveil**
Veiled units keep moving — faster, in fact — but are removed from the fight:
they are **invisible, cannot be targeted, and cannot interact with anything**
(no attacking, gathering, building or capturing). They gain a move-speed bonus
while veiled. **Sect powers still reach them**, friendly and hostile alike.
- **I** — **Small** area, 8 s.
- **II** — **Small** area, 15 s.
- **III** — **Medium** area, 15 s; on expiry they gain +25 % damage for 10 s.

**[ACTIVE] Bulwark**
- **I** — **Single target** building gains +100 % HP for 30 s.
- **II** — Buildings in a **small** area gain +100 % HP for 30 s.
- **III** — Buildings in a **medium** area gain +100 % HP for 30 s and reflect 20 % of melee damage.

**[ACTIVE] Immovable**
(Replaces the earlier crowd-control version — the game has no pushback or
knockback system for it to negate.)
- **I** — Units in a **small** area gain **+5 armor** for 10 s.
- **II** — **Medium** area, **+8 armor**, 15 s.
- **III** — **Large** area, **invulnerable**, 20 s.

> Balance flag: III is a 25 m army-wide invulnerability for 20 s — the single
> strongest defensive effect in the game. On-theme for the wall-keepers, but it
> is the first number to revisit if Fortitude dominates.

**[PASSIVE] Veiled Stone** — your walls and towers gain +25 % HP; towers gain
+1 range.

**[BUILDING] Stonehold** — a squat blockhouse with no windows. It is built to be shot
at: it has the highest HP of any non-Hall structure and blocks pathing like a wall. **Limit 5.**

**[UNIT] Stone Warden** — slow heavy infantry projecting a damage-reduction
dome. It **can never attack**, at all — it is a walking wall, not a fighter.
**Up to 5.**

**[RESEARCH] Deep Foundations** — defensive structures cost 20 % less and build
30 % faster.

### Sect of Reclamation — *the curse-harvesters*
Identity: curse exploitation.

**[ACTIVE] Harvest the Veil**
Target a **resource node**; it over-yields on a 5 s tick for 30 s. Every level
targets a single node — the escalation is entirely in what comes out of it.
- **I** — 50 Supplies per tick. *(300 Supplies over the 30 s.)*
- **II** — 75 Supplies + 20 Iron per tick. *(450 S + 120 I.)*
- **III** — 150 Supplies + 60 Iron + 35 Veilstone + 5 Veilsteel per tick.
  *(900 S + 360 I + 210 V + 30 Vs.)*

**[ACTIVE] Cleanse**
Pumps aggressive bursts of **player influence** into the area for the duration —
it drives the existing influence map rather than inventing a suppression system,
so it pushes the curse back and claims ground in one motion.
- **I** — **Small** area, 20 s of heavy influence deposit.
- **II** — **Medium** area, 40 s.
- **III** — **Large** area, 40 s, and allies inside regenerate.

**[ACTIVE] Veil-Touched**
- **I** — Units in a **small** area take no curse damage for 15 s.
- **II** — **Medium** area, 30 s.
- **III** — **Large** area, 30 s, and they move 20 % faster on cursed ground.

**[PASSIVE] Border-Hardened** — your units take 50 % less damage from Border
sources, and your workers can harvest cursed nodes.

**[BUILDING] Veilworks** — a smelter for cursed matter. It can be raised **on cursed
ground**, which no other building may do, and takes no curse damage. **Limit 5.**

**[UNIT] Golem Autark** — curse-immune construct that fights and harvests on
cursed ground. **Up to 5.**

**[RESEARCH] Warden's Ledger** — veilstone yields +25 %, and every cursed node
is harvestable regardless of tier.

---

## 5. Runai cluster

### Sect of Silence — *the hush*
Identity: denial and tempo.

**[ACTIVE] Hush**
- **I** — **Single target** building cannot train or research for 20 s.
- **II** — Buildings in a **small** area are silenced for 20 s.
- **III** — **Medium** area, 30 s.

**[ACTIVE] Entomb**
- **I** — **Single target** unit is sealed for 5 s: untargetable, immobile, deals no damage.
- **II** — Units in a **small** area sealed 8 s.
- **III** — **Small** area, 8 s; on release they are Marked (+25 % damage taken) for 15 s.

**[ACTIVE] Whisper-Wind**
- **I** — Allies in a **small** area move 20 % faster for 8 s.
- **II** — **Medium** area, 12 s.
- **III** — **Large** area, 12 s, and they ignore terrain slow.

**[PASSIVE] Steadfast Vigil** — your units gain +3 armor while holding position.

**[BUILDING] Hush Vault** — a sunken stone cell. Enemy sect powers cast within its
footprint cost their caster extra cooldown. **Limit 5.**

**[UNIT] Archivist Adept** — caster that suppresses enemy abilities within a
small radius around itself. **Up to 5.**

**[RESEARCH] Quiet Roads** — your units move 15 % faster while out of combat.

### Sect of Justice — *the tribunal*
Identity: retribution.

**[ACTIVE] Eye of the Law**
- **I** — Reveal a **medium** area for 10 s.
- **II** — Reveal a **large** area for 10 s; stealth is revealed too.
- **III** — Reveal a **large** area for 25 s; revealed units are Marked.

**[ACTIVE] Sentence**
- **I** — **Single target** takes 120 true damage after a 3 s telegraph.
- **II** — **Small** area, 120 true damage.
- **III** — **Medium** area, 180 true damage; survivors Marked for 30 s.

**[ACTIVE] Writ of Blood**
- **I** — Enemies in a **small** area that have killed your units take +50 % damage for 10 s.
- **II** — **Medium** area, 20 s.
- **III** — **Large** area, 20 s, and they are slowed 30 %.

**[PASSIVE] Marked for Sentence** — anything that kills one of your units takes
+25 % damage from your army until it dies.

**[BUILDING] Tribunal** — a raised court platform. Marked enemies that die anywhere on
the map refund a little of the Tribunal's research cost. **Limit 5.**

**[UNIT] Judicator** — executioner dealing heavy bonus damage to Marked
targets. **Up to 5.**

**[RESEARCH] Writ of Law** — Marked lasts twice as long and spreads to enemies
near the marked target.

### Sect of Veneration — *the choir*
Identity: escalating offense.

**[ACTIVE] Litany**
- **I** — Allies in a **small** area gain +20 % damage for 10 s.
- **II** — **Medium** area, 15 s.
- **III** — **Large** area, 15 s, +50 % damage.

**[ACTIVE] Crystal Communion**
- **I** — Allies in a **small** area gain +15 % damage reduction for 15 s.
- **II** — **Medium** area, 20 s, +25 %.
- **III** — **Large** area, 20 s, +25 % reduction and +25 % move speed.

**[ACTIVE] Ascend**
- **I** — **Single target** ally gains +1 veterancy rank.
- **II** — Allies in a **small** area gain +1 rank.
- **III** — Allies in a **medium** area gain +1 rank.

**[PASSIVE] Fervor** — each of your unit's kills grants a stacking +2 % damage
and attack rate, capping at **+20 %**.

**[BUILDING] Choir Hall** — a resonating hall. Friendly units passing through gain a
short Fervor bonus. **Limit 5.**

**[UNIT] Vault Keeper** — elite guard that doubles Fervor stacking for nearby
allies. **Up to 5.**

**[RESEARCH] Rite of Ascension** — your units gain veterancy 50 % faster.

### Sect of Witness — *the open eye*
Identity: vision.

**[ACTIVE] Foresight**
- **I** — Reveal a **large** area for 8 s.
- **II** — Reveal a **large** area for 15 s, including stealth.
- **III** — Reveal a **large** area for 20 s; revealed enemies take +25 % damage from your units.

**[ACTIVE] Watcher's Mark**
- **I** — **Single target** enemy is revealed until it dies.
- **II** — Enemies in a **small** area are revealed until they die.
- **III** — **Medium** area; revealed enemies also lose half their own vision.

**[ACTIVE] Blinding Glare**
- **I** — Enemies in a **small** area lose all vision for 8 s.
- **II** — **Medium** area, 12 s.
- **III** — **Large** area, 12 s, and they cannot use abilities.

**[PASSIVE] All-Seeing** — your Scouts gain +50 % vision; every other unit gains
+2 m.

**[BUILDING] Glass Spire** — a thin mirrored tower. It sees further than any other
building and cannot be built inside another Glass Spire's sight radius. **Limit 5.**

**[UNIT] Glassmark Arcanist** — caster granting permanent vision over the ground
it stands on. **Up to 5.**

**[RESEARCH] The Long Watch** — explored terrain never returns to fog; enemy
buildings stay visible once seen.

---

## 6. Feraldis cluster

### Sect of War — *the muster*
Identity: mass and momentum.

**[ACTIVE] Blood Rain**
- **I** — Blood falls over a **small** area, leaving a blood pool where it lands.
  For 10 s **every unit on the map** — yours, your allies', your enemies' —
  attacks **5 % faster**, and **no ability or sect power can be cast anywhere**.
- **II** — **Medium** area, **10 %** attack speed, 20 s.
- **III** — **Large** area, **15 %** attack speed, 30 s.

> Blood Rain is a *global* power with a local deposit. The attack-speed gift and
> the spell lockout are both map-wide and side-blind: casting it turns the whole
> match into a pure weapons fight for the duration, which rewards the side that
> is already winning the melee. The pool it leaves is the local half — real blood
> on real ground, feeding Frenzy and legal ground for a **War Totem** planting
> (see [Age_1_Feraldis.md](Age_1_Feraldis.md) § Blood, Frenzy & War Totems).
> The lockout silences War itself: no second Blood Rain and no other sect power
> lands, on either side, until it lapses.

**[ACTIVE] Call to Arms**
- **I** — **Single target** military building trains units **50 % cheaper** for 15 s.
- **II** — Military buildings in a **small** area train **50 % cheaper** for 30 s.
- **III** — **Medium** area, 30 s, and those buildings also train at **double speed**.

**[ACTIVE] Bloodfury**
- **I** — Allies in a **small** area deal **+25 % attack damage** for 8 s.
- **II** — **Medium** area, 12 s.
- **III** — **Large** area, 12 s, +25 % damage **and +5 armor**.

**[PASSIVE] Forged in Battle** — your military units cost 10 % less and train
20 % faster.

**[BUILDING] Muster Yard** — a stockade of training posts and armourers' racks.
While one stands, every
[per-battalion upgrade](Overview.md#per-battalion-military-upgrades-cross-faction-rule)
applied anywhere in the faction costs **50 % less**. The discount does **not**
stack — a second Muster Yard buys redundancy, not a deeper cut. **Limit 5.**

**[UNIT] Warbreaker** — shock infantry gaining damage for each nearby ally.
**Up to 5.**

**[RESEARCH] Endless Muster** — military buildings train **two units at once**.
Queue depth is unchanged.

### Sect of Ash — *the burning ground*
Identity: area denial by fire.

**[ACTIVE] Pyre**
- **I** — Ignite a **small** area for 15 s, damaging enemies inside.
- **II** — **Medium** area, 30 s; the zone is impassable to enemies.
- **III** — **Large** area, 30 s, impassable, and any blood in it ignites.

**[ACTIVE] Cinderfall**
- **I** — **Single target** burns for heavy damage over 10 s.
- **II** — **Small** area burns for 10 s.
- **III** — **Medium** area burns for 15 s, spreading to units that flee it.

**[ACTIVE] Ashen Veil**
- **I** — A **small** area fills with smoke: ranged attacks into it miss 30 % of the time for 10 s.
- **II** — **Medium** area, 15 s.
- **III** — **Large** area, 15 s, 50 % miss chance.

**[PASSIVE] Pyre's Promise** — your units leave a burning patch where they die.

**[BUILDING] Ash Pyre** — a permanently burning pyre. Enemies adjacent to it take
burn damage; it is as much a weapon as a building. **Limit 5.**

**[UNIT] Ashblade** — melee infantry that ignites whatever it strikes.
**Up to 5.**

**[RESEARCH] Everburning** — all of your fire effects last 50 % longer and deal
25 % more damage.

### Sect of Ruin — *the unmakers*
Identity: structure breaking.

**[ACTIVE] Unmake**
- **I** — **One** enemy building — the single nearest to the cast point, within the cast radius — takes 50 % of its current HP as damage after a 3 s telegraph. Never more than one building, whatever else stands in range.
- **II** — 75 % of current HP, still a single building.
- **III** — 90 % of current HP; other buildings in a **small** area take 25 % splash.

**[ACTIVE] Profane Strike**
- **I** — Burst damage across a **small** area.
- **II** — **Medium** area.
- **III** — **Large** area; buildings hit cannot be repaired for 20 s.

**[ACTIVE] Sunder**
- **I** — **Single target** building loses all armor for 20 s.
- **II** — Buildings in a **small** area lose all armor for 20 s.
- **III** — **Medium** area; they also take +50 % siege damage.

**[PASSIVE] Profane Hands** — your units deal +25 % damage to buildings and
refund their own cost when a building falls to them.

**[BUILDING] Ruinworks** — a scaffold of breaking-tools. Siege units built while it
stands carry extra damage against structures. **Limit 5.**

**[UNIT] Nullblade** — siege-class infantry that ignores building armor.
**Up to 5.**

**[RESEARCH] Debris** — a building you destroy does not simply fall: it comes
apart, and the wreck is a weapon. When a building dies to your faction it
detonates, dealing **severe damage to everything hostile to you within a small
radius — units and BUILDINGS alike**.

Because the blast damages buildings, a detonation can kill a neighbouring
structure, which detonates in turn: in a tightly packed base a single collapse
can walk through the whole quarter. That chain is the point of the research,
not a side effect.

Rules that keep the chain honest:
- Each building detonates **at most once**, so a chain can never feed back into
  a wreck it already came from.
- The blast respects alliance: it never harms you or your allies, only what is
  hostile to the faction that owns the research (`Alliances.AreHostile` is the
  only valid test — see [Teams.md](Teams.md)).
- It fires wherever the building dies, including to the debris of another
  building — that is what makes the domino possible.

### Sect of Wrath — *the forsaken*
Identity: punishment at low HP.

**[ACTIVE] Final Hour**
- **I** — Allies in a **small** area cannot drop below 1 HP for 12 s; when it ends, low-HP units explode.
- **II** — **Medium** area, 20 s.
- **III** — **Large** area, 20 s; the explosions leave blood pools and apply Bleeding.

**[ACTIVE] Spite** — the sect's signature: a group is made to answer, together,
for everything it has done.

Every enemy unit inside the area has its **damage dealt this match** added to a
single pool. That pool is then **divided equally among them** and each takes
that share. A lone veteran pays its whole account; a crowd splits one bill.

- **I** — **Small** area.
- **II** — **Medium** area.
- **III** — **Large** area.

> Worked example (canon): five units stand inside, and between them they have
> dealt 200 damage this match. The pool is 200, split five ways — **each takes
> 40**. The same five with one 200-damage veteran among four fresh recruits
> still take 40 each; the veteran's account is what the recruits are paying.

Levels scale the AREA only — the arithmetic is identical at every level, so a
higher Spite catches more of the enemy army in one accounting rather than
hitting harder per head.

**[ACTIVE] Wrathfire**
- **I** — A burning pillar scorches a **small** area for 8 s.
- **II** — **Medium** area, 12 s.
- **III** — **Large** area, 16 s, and enemies inside bleed.

**[PASSIVE] Spite of the Forsaken** — your units deal up to +40 % damage as
their HP falls.

**[BUILDING] Chain Altar** — an altar strung with iron links. Enemies killed near it
feed a faction-wide damage stack that decays over time. **Limit 5.**

**[UNIT] Chaincaster** — caster that links enemies so damage dealt to one bleeds
through to the others. **Up to 5.**

**[RESEARCH] Blood Debt** — your units' deaths grant the faction resources, and
every death **heals the rest of the army**: each unit you lose restores **5 HP
to every other living unit in your faction**, anywhere on the map.

A losing fight therefore knits the survivors back together — the more of your
line falls, the more the rest of it recovers. Healing never exceeds a unit's
maximum HP and never revives the dead.

---

## 7. Implementation delta

What the code does today vs. this document:

| Area | Code today | This doc |
|---|---|---|
| Radii | bespoke per power (4–28 m) | four fixed radii |
| Power level | tracks the purchasable Active-Power lever | tracks Temple upgrades **since adoption** |
| Chapel aura | `SectLeverEffects.AuraOf` grants an aura per sect | **no aura** unless a Passive/Research says so — delete the table |
| Actives | 3 per sect, shipped | same shape, new effects; several need new `SectActivePowerKind`s (hostile-conversion, building shutdown, tower-raising, veil/untargetable, flat-armor buff, invulnerability, armor-strip, reveal-until-death, node over-yield, influence burst, map-wide attack-speed, map-wide silence, blood-pool deposit, training-cost discount, training-speed boost) |
| Unit | 12 factories exist, no meshes | unchanged roster, capped at 5, **trained at the sect building** (not the Temple), authored mesh required |
| Research | none | 12 new techs, one per sect, bought at the sect building |
| Sect building | 1 of 12 exists (Reliquary) | 12 buildings, **limit 5 each**, authored mesh; each trains its sect's unit and sells its sect's research |
