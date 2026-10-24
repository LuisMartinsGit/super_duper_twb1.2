# Sects

> **2026-10-15:** §1's slots are now three powers, a **sect building** (its
> standing effect is the passive), a research, a trainable **unit (limit
> 20)** and a unique **hero** (Religion.md §3-§4). The Alanthor cluster (§4)
> is rewritten and Reclamation is replaced by Prosperity; set passives are §6b.

Canonical design for the 12-sect religion layer. Supersedes the sect sections of
`.deft/tasks/task-sect-system-redesign-063/task.md` where they disagree, and
supersedes the shipped `SectLeverEffects` numbers (the code is being aligned to
this document, not the other way round).

Companion visualization: [`docs/SectReference.jsx`](../SectReference.jsx) — open
[`docs/SectReference.html`](../SectReference.html) to read it.

---

## 1. What a sect grants

> **Superseded in part 2026-09-29 by [Religion.md](Religion.md):** the sect
> building IS its chapel (max six, bought with RP), and the sect unit is a
> **hero** (limit one, levels per Heroes.md) instead of a unit capped at 5.

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

**A Single Target power is cast by clicking the unit or building itself**
(2026-10-18, [Spells.md](Spells.md) §6): the cursor picks the entity under it,
the power names which kind it may be cast on (enemy unit, own building,
friendly building, ...), and a click on the ground or on the wrong kind is
refused with a message and costs no cooldown. "Own" is the caster's faction;
team allies count only where a power says *friendly*. The effect lands on that
entity after the wind-up; if it is gone by then, nothing happens and the
cooldown is refunded. The per-power target list is in Spells.md §2.
| **Small** | 8 m | a squad, one building cluster |
| **Medium** | 15 m | an engagement, a base district |
| **Large** | 25 m | an army, a whole base |

**Line of sight (2026-10-19, [Spells.md](Spells.md) §6.1):** a power is cast
only where its caster (or a team ally) can see right now — the aimed centre of
an area power, the target of a single-target enemy power. Own / allied targets
are always seen; Plenty, Nowhere to Hide and reveals are exempt. A refused
cast spends nothing; the AI aims only at what it sees.

## 2b. Cooldowns, damage and wind-ups

Every sect power's numbers are set by the balance ladder in
[Spells.md](Spells.md) §8, and the full table of every power is Spells.md
§9. The short version:

- **A power's cooldown is flat across its levels.** The level buys reach,
  magnitude or duration, never tempo. Bands: tactical and economy 60 s,
  damage 75 s, wildcard 120 s, map-wide 150 s. Two powers rise with the level
  because the level changes what they are: Raise Anew (120 / 150 / 180 s) and
  Immovable (60 / 60 / 120 s).
- **Damage per victim falls with reach**: Single 120, Small 60, Medium 40,
  Large 30, map-wide 20 at level I, times 1 / 1.5 / 2 for level I / II / III.
  A power whose reach grows each level keeps its per-victim number.
- **Wind-ups**: 3 s for a power that deals damage, 1.5 s for a hostile power
  that does not, 1 s for a friendly one.
- The cooldown written here is the one a player waits. There is no hidden
  global scale; only the Shrine of Ridan and the Shardroot shorten it.

## 3. Power levels come from adoption timing

> **SUPERSEDED 2026-09-29 by [Religion.md](Religion.md) §3.1:** a power's
> level is its chapel's level, bought with RP. Early adoption is no longer
> rewarded.

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

## 3b. The three actives are not three of anything — counterparts + a wildcard

**Every sect's kit is one power from each counterpart pair, plus a wildcard.**

| | Pair | Take exactly one |
|---|---|---|
| **A** | damage ↔ heal | an area that hurts, or an area that mends |
| **B** | buff ↔ debuff | an area that strengthens yours, or one that weakens theirs |
| **★** | wildcard | a PIVOT — see below |

No sect gets two heals, or two buffs, or a damage and a heal. The pairs exist so
that every sect answers a different half of the same question, and so that
reading one sect's kit tells you what it cannot do.

**The wildcard is the important slot.** It is not "the level III version of the
other two" and it is not a bigger number — it is the moment a match turns. Sew
Disorder turning a defence into an attacking mob, a fortress appearing where
there was open ground, an army that cannot be seen walking through a battle
line. If the third power could be described as "more of slot 1", it is not a
wildcard and the sect has effectively two powers.

A wildcard that merely restates a counterpart is the common failure: an
army-wide invulnerability in a sect whose Pair B is already an armour buff adds
nothing but magnitude.

### Why this pass exists

**Feraldis overruns Alanthor.** Aggression is Feraldis's whole identity and it
was winning uncontested, because the four Alanthor sects were built almost
entirely out of buffs and utility — Fortitude ran three buffs and no answer at
all. Alanthor is the defensive culture; defensive is not the same as passive,
and a sect that can only make its own units slightly better cannot punish an
attack that has already begun. The Alanthor kits below are meant to BITE BACK.

## 4. Alanthor cluster — the House of Vaelith

> **Named 2026-10-15:** in game text the four Alanthor sects are the
> **House of Vaelith** (`SectConfig.ClusterDisplayName`).

> **Rewritten 2026-10-15** (developer-agreed; mockup and lore in the
> "Alanthor Sect Codex"). **Reclamation is retired and replaced by
> Prosperity**; the Lorekeeper, Stone Warden, Golem Autark and Tinker are
> retired.
>
> **The magic is time.** Glow is condensed time, veilsteel stores it,
> veilstone is time frozen. Alanthor's sects work in the *past*: the record,
> rewind, stored charge, accelerated work. Player-facing text names what is on
> the map — no trade jargon, no places beyond the map.
>
> Each sect: three powers (A damage-or-heal / B buff-or-debuff / ★ wildcard,
> §3b), a sect building whose **standing effect** is the sect's passive, one
> research bought there, one **offensive unit (limit 20)** trained there, and
> one **unique hero** recruited there with abilities at hero levels 1 / 3 / 6.
> Power numbers follow the Spells.md §8 ladder; cooldowns are flat across
> levels except where stated.
>
> Every piece was checked against King Lexor's kit (King's Call, Liquid
> Courage, Life Cling, Honour thy Pledge) and against the rest of the cluster:
> each has a partner ("pairs with") and no two do the same job.

### Sect of Antiquity — *the Registry*
Surveyors, engineers and bureaucrats who license every process and study the
Old Empire's artifacts. Their magic is recovered Vyrelin seals, lenses and
glow-engines.

**[A] Trespass** — enemies in the area take the damage band if they stand
inside your territory, half of it outside. Both players can see which applies.
**S / M / L**, damage cooldown, 3 s wind-up.

**[B] Embargo** — one enemy building stops training, research and income;
queue progress is kept. **Always single target**; lasts **20 / 30 / 40 s**.

**[★] Friendly Fire** — **30 % of the enemy units in the area** (2026-10-24,
developer: "must affect only 30% of the units in a radius"; picked by a
shuffle seeded from the cast, so every lockstep peer agrees) go mad for
**8 s / 20 s / until killed**. **A Heal cures them** (Renewal [A]). **S / M / L**. *(Reworked 2026-10-18, developer: "Units
targeted by friendly fire do not change ownership. They target each other and
become uncontrollable. I want the player to read it as 'my army committed
suicide' and not 'my army got conquered by the curse'.")*
- The units **keep their owner and colour** — they are never the curse's.
- Their owner **cannot control** them (player or AI) while it lasts.
- They **attack everyone near them, their own side included** (each other
  too) — **units and buildings, their own side's buildings too** — and
  everyone may attack them. **Only the caster's own units are spared**; the
  caster's allies go mad like anyone else (2026-10-19, developer: "Buildings
  and allies should not be spared").
- A frenzied unit wears a **red aura** while it lasts (`UnitCombatVfx`,
  `frenziedPrefab` — Lana `Area_generic_red`, copied to
  `Sects/Antiquity/Abilities/FriendlyFire/FriendlyFire_Aura.prefab`).
- Its cooldown is **longer** than the other wildcards' (the value is in
  `SpellLadder.FriendlyFireCooldown`; 2026-10-18 "dramatically increase",
  2026-10-19 "too much", brought down).
- Code: `SectDisordered` + `NotControllableTag`; TargetingSystem's rogue
  bucket and `CombatDamageHelper.Frenzied` let the hits through.

**[BUILDING] Reliquary** — while it stands, your sect powers recharge faster.
(Its old Scry / Lockout / Vision building actives are retired.)

**[RESEARCH] Cartography** — every territory you hold is **fully visible**
(2026-10-19; was "capturing a territory explores all of it"): live sight over
exactly the territory's ground, following its border and nothing outside it,
so enemy units inside it are seen. Shared with team-mates like any sight, and
counted as sight by the simulation (`FactionSight`, the sect-power aim rule).
When ownership is lost the territory falls back to explored fog. It does not
let units auto-acquire targets further than their own range. Code: the fog
grid stamps a per-territory cell mask (`FogOfWarManager` territory sight, fed
by `FogOfWarSystem`); `FactionSight.SeesHeldTerritory`.

**[UNIT] Artificer** — long-range anti-armour; a slow glow-lance. **Stasis
Bolt:** a target it hits is slowed (move and attack speed) for a few seconds.
Weak to cavalry and fast melee. Pairs with the Archivist and the Collector.

**[HERO] Archivist of Hours**
- **L1 Catalogued** — aura (M): slowed enemies near the Archivist take +20 %
  damage from every source.
- **L3 Stamp of Record** — one enemy unit or hero has every ability put on
  cooldown.
- **L6 Recall** — the Archivist and allies within S jump back to where they
  stood 5 s ago; position only, HP unchanged.

### Sect of Renewal — *the Hospices*
Healers of the poor; their magic is rewind — a mended wound is a body
returned to an earlier moment.

**[A] Heal** — restore **30 % / 40 % / 60 %** of max HP. **S / M / M**,
allied units and buildings. **It also cures Friendly Fire** on every unit it
reaches (2026-10-24, developer: "Healing spell removes Friendly fire").

**[B] Lifeline** — allies **take 50 % less damage** (2026-10-24) and cannot
drop below 1 HP for **6 / 12 / 12 s**; at
III survivors heal 25 % when it ends. **S / S / M**.

**[★] Revive** — your units that died in the area in the last
**10 / 15 / 20 s** return at **30 % / 40 % / 50 %** HP, at most
**5 / 10 / 15**. Heroes are excluded. **S / M / L**.

**[BUILDING] Mending Hall** — damaged units inside heal over time; while it
stands, your buildings slowly repair themselves out of combat.

**[RESEARCH] Field Hospital** — Litharchs gain Deploy Field Hospital. **A team
(a faction and its allies) may keep at most `capPerTeam` hospitals standing at
once, and no two closer than `minSpacingMetres` (`FieldHospitalSystem.asset`,
2026-10-22)**: a cast past the cap or too close is refused and costs no
cooldown. The AI used to stack them into an instant-heal zone under an army
that could not be killed.

**[UNIT] Scar Guard** — heavy melee shock. **Borrowed Breath:** more damage
the lower its HP. Weak to ranged focus fire. Pairs with the Surgeon and
Lifeline.

**[HERO] Surgeon**
- **L1 Cauterize** — aura (M): allies are immune to damage over time
  (burning, bleeding, cursed ground).
- **L3 Bloodletting** — allies within S above 35 % HP drop to 35 % (cannot
  kill) and gain +25 % attack speed for 10 s.
- **L6 Lancet** — one enemy bleeds for 10 s and cannot be healed or repaired
  while it bleeds.

### Sect of Fortitude — *the Wardens*
Keepers of walls, gates and watch-fires. Their walls are laced with
veilsteel that stores glow and releases it.

**[A] Ward Discharge** — arm **one of your structures**. When an enemy enters
range (**S / M / L**) it fires **1 / 4 / 8** pulses, one every **3 / 2 / 1 s**,
each the damage band to every enemy in range. Unspent pulses stay armed; the
charge fades after **5 min**. Cooldown **300 s**. The armed structure glows
(visible to the enemy).

**[B] Emergency Repairs** — buildings gain **+100 % max HP** (current HP rises
by the same amount) for 15 s; at III they reflect 20 % of melee damage.
**Single / S / M**.

**[★] Fortify** — raise a permanent, complete **Tower / Fortification /
Fortress** at a point on legal build ground. Cooldown **120 / 300 / 600 s**
(climbs with level to stop Fortress spam).

**[BUILDING] Stonehold** — a windowless blockhouse that blocks paths; while it
stands, garrisoned walls and towers gain range and damage (**Arrow Slits**).

**[RESEARCH] Deep Foundations** — walls, towers and gates cost less and build
faster.

**[UNIT] Sallyguard** — anti-siege striker with a veilsteel halberd.
**Stored Strike:** charges while out of combat; the first blow afterwards
deals double. Bonus damage against siege. Weak to archers. Pairs with the
Warden.

**[HERO] Warden**
- **L1 Drillmaster** — aura (M): Sallyguards charge Stored Strike twice as
  fast.
- **L3 Sally Forth** — allies within M gain move speed for 8 s; nearby
  Sallyguards are fully charged at once.
- **L6 Challenge** — enemies within S, siege included, must attack the Warden
  for 6 s (a taunt; no invulnerability).

### Sect of Prosperity — *the Guild*
Alanthor's workshops and counting rooms. Vyrelin engines fed glow make time
run faster inside a workshop.

**[A] Bounty** — one enemy unit takes the single-target damage band; if it
dies within 10 s you are paid its cost.

**[B] Overtime** — one of your buildings trains, researches and produces
**50 % / 75 % / 100 %** faster for 20 s.

**[★] Plenty** — instantly gain **60 / 90 / 120 s** of your current income.
Cooldown **300 s**.

**[BUILDING] Treasury** — while it stands, your extractors earn more.

**[RESEARCH] Charter of Works** — extractors cost less to build and to level.

**[UNIT] Collector** — fast melee finisher, the fastest Alanthor infantry.
**Run Down:** bonus damage against slowed enemies and enemies under half HP.
Weak to heavy infantry. Pairs with the Artificer and the Guildmaster.

**[HERO] Guildmaster**
- **L1 Called In** — aura (M): enemies under half HP are slowed 20 %.
- **L3 Stake** — channel on a node: its extractor pays double while the
  Guildmaster stays; any damage breaks it. Up to 30 s.
- **L6 Liquidate** — demolish one of your buildings at once for 50 % of
  everything invested in it, levels included; the enemy gets no raze bounty
  or kill credit.

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
- **II** — **Small** area, 90 true damage.
- **III** — **Medium** area, 80 true damage; survivors Marked for 30 s.

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

**[ACTIVE] Spy Network** — *the eye opens one lid at a time*
**Single target.** Turn one enemy unit into an unwitting spy: you see everything
it sees, and it does not know. The network then **spreads on its own** — an
enemy that spends **3 s** near a spy becomes a spy too, and can in turn infect
others.

- **I** — Spies last 45 s. Cascade needs 3 s of proximity.
- **II** — Spies last 90 s, and the cascade reaches further.
- **III** — Spies last **until they die**, and the cascade is faster.

> This is the sect's engine, not its payoff. Seeded into a marching army it can
> quietly become total vision; seeded into a lone scout it dies with the scout.
> The player chooses where to plant it and then lives with how the enemy moves —
> the sect's information is EARNED by the opponent's own behaviour rather than
> bought with a cast.
>
> It replaces a flat map-wide reveal, which handed the same total information
> for one button and made the choice of where to look meaningless.

**[ACTIVE · B] Blinding Glare**
- **I** — Enemies in a **small** area lose all vision for 8 s.
- **II** — **Medium** area, 12 s.
- **III** — **Large** area, 12 s, and they cannot use abilities.

**[ACTIVE · ★ WILDCARD] Nowhere to Hide**
Every enemy unit **you can currently see** takes damage at once, anywhere on the
map. Its reach is not a radius — it is however much of the enemy army you have
managed to reveal.

- **I** — 20 damage to every revealed enemy.
- **II** — 30, and it also strikes revealed buildings.
- **III** — 40: enough to finish a wounded army outright.

> Map-wide is the lowest band on the ladder per victim, because the victim
> count has no ceiling. It used to deal 60 / 110 / 200, which deleted any
> revealed army outright rather than finishing a wounded one.

> The pivot, and the reason Spy Network is worth planting. A Witness player who
> has done nothing gets a weak map-wide tickle; one whose spy network has
> cascaded through a whole army deletes it from across the map. The power is
> a MULTIPLIER ON PREPARATION, which is what makes it a wildcard rather than a
> damage spell — and Witness's Pair A is satisfied by it, the one sect where
> the wildcard and the damage counterpart are deliberately the same power.

> **Foresight and Watcher's Mark are cut.** Both were plain reveals, and Spy
> Network is a better version of the same idea: it reveals by spreading rather
> than by paying.

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

> Every Ash fire — Pyre, Cinderfall, Pyre's Promise, the Ash Pyre — is
> ordinary fire and so **ownerless** ([Fire.md §3](Fire.md#3-standing-in-fire)):
> it burns your own and allied units standing in it too. "Damaging enemies"
> above describes who the sect aims it at, not who it spares. No Ash lever
> grants fire immunity today.

**[BUILDING] Ash Pyre** — a permanently burning pyre. Enemies adjacent to it take
burn damage; it is as much a weapon as a building. **Limit 5.**

**[UNIT] Ashblade** — melee infantry that ignites whatever it strikes.
**Up to 5.**

**[RESEARCH] Everburning** — all of your fire effects last 50 % longer and deal
25 % more damage.

### Sect of Ruin — *the unmakers*
Identity: structure breaking.

**[ACTIVE] Unmake**
- **I** — **One** enemy building — the single nearest to the cast point, within a **small** search radius — takes 40 % of its current HP as damage after a 3 s telegraph. Never more than one building, whatever else stands in range.
- **II** — 60 % of current HP, still a single building.
- **III** — 80 % of current HP; other buildings in a **small** area take 25 % splash.

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
hitting harder per head. **The share is capped at 120 per head** (the ladder's
conditional cap): a lone veteran pays its account up to that, not a
thousand-damage bill in one cast.

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

## 6b. Set passives (2026-10-15)

Religion.md §3.3 says when a set is complete. What each does:

| Set | Passive | Effect |
|---|---|---|
| All 4 Alanthor sects | **Sovereign Hour** | Inside your territory your units attack 15 % faster and enemy units move 10 % slower |
| All 4 Feraldis sects | **The Breaking Hour** | When an enemy building or a curse node falls to you, your units within L of it attack and move 20 % faster for 15 s |
| All 4 Runai sects | **Foreknown** | Hostile sect powers aimed at your units or buildings take twice as long to wind up |
| Two of each cluster | **Concord** | Every power of your six sects is cast one level higher, up to III |

The Feraldis and Runai passives were drafted against those clusters' old
kits and may move when they get their pass.

## 7. Implementation delta

What the code does today vs. this document:

| Area | Code today | This doc |
|---|---|---|
| Radii | bespoke per power (4–28 m) | four fixed radii |
| Power level | tracks the purchasable Active-Power lever | tracks Temple upgrades **since adoption** |
| Chapel aura | **Done (2026-09-27).** The aura table is emptied: `SectLeverEffects.AuraOf` returns no aura (radius 0) for every sect, and the sect tooltips describe the building instead of an aura. `SectBuildingLeverSystem.cs` is now a no-op and is waiting to be deleted | **no aura** unless a Passive/Research says so |
| Actives | 3 per sect, shipped | same shape, new effects; several need new `SectActivePowerKind`s (hostile-conversion, building shutdown, tower-raising, veil/untargetable, flat-armor buff, invulnerability, armor-strip, reveal-until-death, node over-yield, influence burst, map-wide attack-speed, map-wide silence, blood-pool deposit, training-cost discount, training-speed boost) |
| Unit | 12 factories exist, no meshes | unchanged roster, capped at 5, **trained at the sect building** (not the Temple), authored mesh required |
| Research | none | 12 new techs, one per sect, bought at the sect building |
| Sect building | 1 of 12 exists (Reliquary) | 12 buildings, **limit 5 each**, authored mesh; each trains its sect's unit and sells its sect's research |
