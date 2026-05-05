---
deft:
  id: task-sect-system-redesign-063
  type: improvement
  status: active
  stage: architecture
  phase: 0
  total_phases: 5
  priority: high
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
  labels: [feature, sect, religion, large]
---

# Sect system redesign — 12 sects, RP economy, blood pools

## Context

Replaces the existing 12-sect roster (Renewal/Antiquity/LivingStone/VeiledMemory/StillFlame/QuietVault/MirrorRite/ShardJudgment/EmberAsh/HollowBrand/FlamewroughtChains/UnmakersGrasp) with a new 12-sect design organised into 3 cultural clusters (Alanthor, Runaii, Feraldis), each sect carrying 4 mechanical levers across 3 upgrade levels.

Full spec embedded in §Spec below. This is the source of truth — implementation details (file paths, JSON schema, etc.) live in §Implementation plan.

## User Value

Religion is currently a flat passive system with stale identities. The new design gives each sect:
- A clear identity (intel / repair / static-defense / etc.)
- Three lever types beyond passives (a unique building, a unique unit, an active power)
- A 3-level upgrade arc that takes 3 ages to max
- A new faction-baseline mechanic for Feraldis (blood pools) integrated with the existing GPU influence map

Players choose 6 sects max from a shared 12, balancing same-culture (cheap) vs cross-culture (expensive) picks. Adoption is public; upgrades are private (scout-to-infer).

## User decisions made

- [x] **Migrate or replace?** **Replace.** Delete old sect roster (Renewal/Antiquity/LivingStone/VeiledMemory/StillFlame/QuietVault/MirrorRite/ShardJudgment/EmberAsh/HollowBrand/FlamewroughtChains/UnmakersGrasp), the 12 sect unit abilities, sect aura buildings, and old multiplier bridge. Build the new 12-sect system from scratch.
- [ ] **Cross-faction Wrath weakness (§Balance flag #6).** Decide later — playtest first.
- [x] **Stacking refunds.** **Do NOT stack.** Renewal / Ruin / Reclamation refund-on-X effects must each be guarded so a single death/destruction can be refunded by at most one of them. Implement as a per-event "refund consumed" flag.
- [x] **Blood pool gating.** **Age-gated.** No players are Feraldis at game start (culture is chosen on age-up). Blood pools spawn only when the killer is Feraldis-cultured **and** has aged up to Age II+. No baseline pre-age-up.

## Adoption flow — chapels & Temple (clarified after task creation)

This is the canonical adoption flow per the user's design call:

- **Shrine of Ahridan** (Age 1, existing). On completion grants **+1 RP** (one-time, age-1 only).
- **Temple of Ridan** (Age 2+ only). Build option appears only after age-up to Age 2. Has **6 chapel slots** — the hard cap of 6 adopted sects is enforced *by the slots*, not by a separate counter.
- **Chapels** = the adoption marker. 12 chapel building types (one per sect, e.g. `Chapel_Sect_Antiquity`). Building a chapel inside the Temple's slot system **is** the adoption action. Cost = adoption RP cost (2 same-cluster / 3 cross-cluster) plus whatever resource cost the chapel itself has.
- **Sect-unique buildings** (the "Building lever" — Reliquary / Workshop Eternal / Oath-Stone / Crucible / Sepulchre / Tribunal / Sanctified Pyre / Spire of Witness / War Forge / Furnace / Desecrator / Hollow Altar) are **separate entities**, not chapels. Built outside the Temple's slot system. Granted as buildable when the corresponding sect is adopted (chapel built).
- **Lever upgrades** (Lv I → II → III on Passive / Building / Unit / Active Power) happen **at the chapel**, not at the Temple. Each chapel exposes 4 upgrade buttons (one per lever). RP cost: 2 (Lv I→II) / 3 (Lv II→III). Age-gating: Lv II requires the chapel to have been built in a *previous* age; Lv III requires Lv II in a previous age.

## RP economy (revised total)

| Source | Points |
|--------|--------|
| Shrine completion (Age 1) | **1** |
| Age II up | 6 |
| Age III up | 8 |
| Age IV up | 10 |
| **Total** | **25** |

Carryover: ⌊leftover / 2⌋ at age-up.

## Requirements

R1. Replace the 12 sect IDs with the new roster (Antiquity, Renewal, Fortitude, Reclamation, Silence, Justice, Veneration, Witness, War, Ash, Ruin, Wrath).
R2. Implement Religion Points economy: 6/8/10 per age (24 total), per-action cost schedule, hard cap of 6 sects, age-gating for Lv II/III, 2:1 carryover.
R3. Per-sect data model: Passive + Building + Unit + Active Power, each at Lv I/II/III. Backed by ScriptableObjects.
R4. Public adoption announcement at age-up; private upgrade levels (visual differentiation but no UI broadcast).
R5. Blood pool layer for Feraldis: new GPU influence-map channel, every Feraldis kill spawns a pool that buffs Feraldis units inside.
R6. All 12 sects' Lv I lever effects implemented and gameplay-tested (stand-alone playable per §Balance flag #7).
R7. All 12 sects' Lv II + Lv III lever effects implemented.
R8. Adoption UI: 12-sect chapel/temple panel showing affordability, age-gating, current adoption state, upgrade buttons.

## Acceptance Criteria

- [ ] **R1**: existing sect IDs renamed to new roster; tech-tree JSON, building factory, sect aura system, unit ability system all reference the new IDs.
- [ ] **R2**: faction with no sects adopted accumulates 6/8/10 RP at each age; spending RP follows the cost schedule; can't go below 0; hard cap of 6 enforced; Lv II requires prior-age adoption (recorded with timestamp); 2:1 carryover applies at age-up only.
- [ ] **R3**: `SectDefinition` ScriptableObject per sect, with nested `SectLevelData` for each of the 4 levers × 3 levels.
- [ ] **R4**: adoption fires a `SectAdopted` event broadcast to all players; upgrade events stay local (only the owner sees them).
- [ ] **R5**: blood pool channel exists in `InfluenceManager`; Feraldis kills spawn pools; Wrath/Ash/Ruin/War sect levers that reference blood pools (per §Spec §6) read this channel.
- [ ] **R6 + R7**: every per-sect lever entry in §Spec §3-§6 has a working code path. Each sect's combined lever set produces visible gameplay effects in skirmish.
- [ ] **R8**: chapel UI shows the 12 sects, RP balance, affordability colour-coding, "previous-age requirement" warnings, upgrade arrows.

## Phase plan

**Phase 1 — Foundation** (this task starts here)
- New 12-sect roster IDs in `SectConfig.cs` (replacing the old 12 wholesale).
- `SectDefinition` ScriptableObject + `SectLeverData` + `SectLevelData` types.
- `SectAdoptionState` per faction (per-sect: AdoptedAtAge byte, 4 lever-level bytes).
- `FactionReligionPoints` component: balance, age-up award (1 from Shrine + 6/8/10 per age), 2:1 carryover.
- `SectAdoption` static helper: `Adopt(faction, sectId)`, `UpgradeLever(faction, sectId, leverKind)`, affordability + age-gating + 6-cap (enforced via chapel-slot count, not separate counter).
- 12 chapel building types (one per new sect ID) registered in `BuildingFactory` so the Temple slot system can construct them. Chapels are *adoption markers* + *upgrade UI hosts* — they grant the sect on completion via `SectAdoption.Adopt`.
- Temple's `TempleChapelSlot` buffer system (existing) repurposed: 6 slots, each slot = one chapel. Temple build is gated on Age 2+.
- Shrine completion bonus (+1 RP) wired into `BuildingConstructionSystem.GrantShrineRPBonus` (the path is already there for the old design — repointed at the new RP component).
- `SectAdopted` event broadcast on chapel completion.
- Stub adoption UI (rebuild `SectAdoptionPanel.cs` as a functional but unstyled chapel-build + upgrade panel).
- **Demolition**: delete the old SectEffectSystem, SectBuildingAuraSystem, SectPassiveHUD, the 12 old sect unit abilities in `UnitAbilitySystem.cs`, the old chapel/unique-building creators with the 12 old sect names. Strip sect-multiplier reads (`mults.AllIncome`, `mults.MeleeDamage`, etc.) from combat / training / income / vault / fog systems — Phase 2 reintroduces these per-sect, per-lever.

**Phase 2 — Lv I lever implementations**
- Each sect's Lv I Passive, Building, Unit, Active Power.
- Where the existing 12 sects had a working effect that maps to the new sect's Lv I, keep it. Where they don't, write the new effect.
- 12 × 4 = 48 lever entries. Estimate 3-4 PRs sliced by cluster (4 sects × 4 levers per PR).

**Phase 3 — Blood pool layer**
- Extend `InfluenceManager` with blood-pool RenderTexture channel.
- `BloodPoolSpawnSystem`: every Feraldis kill writes to the channel.
- `BloodPoolBuffSystem`: Feraldis units inside the channel gain +X% damage (baseline buff, before Wrath/Ash multipliers).
- Wire Wrath/Ash/Ruin/War sect levers that touch blood pools (sample shader-side).

**Phase 4 — Lv II + Lv III lever implementations**
- 12 × 4 × 2 = 96 upgrade entries.
- Sliced by cluster, 3-4 PRs.

**Phase 5 — Polish + balance**
- Adoption UI styling.
- Balance-flag tests (§8.1-§8.7).
- Cooldown timing pass on actives.
- Tutorial/tooltip pass.

---

## §Spec — Source of truth

> This is the user-supplied design spec. Treat as the authoritative source. Implementation details (paths, JSON keys, etc.) live below in §Implementation plan.

### Cultural Clusters

#### Alanthor
**Themes:** Recovery · Fortification · Craftsman knowledge
**Creed:** *"The curse will be made to serve. We rebuild first, and from what is broken we make ourselves whole."*
Pragmatic restorationists. The curse is a *resource* to rebuild with. Sects: Antiquity, Renewal, Fortitude, Reclamation.

#### Runaii
**Themes:** Doctrine · Secrecy · Veneration of the crystal
**Creed:** *"The curse was righteous. We keep its silence, enforce its verdict, and witness what others cannot bear to see."*
Institutional zealots. The curse is *divine verdict*. Sects: Silence, Justice, Veneration, Witness.

#### Feraldis
**Themes:** Industry · Profanity · Vengeance against the absent god
**Creed:** *"We take the sacred and break it. The blood of our enemies marks the ground we will not surrender."*
Industrial iconoclasts. **Faction-baseline mechanic: blood pools** — every kill spawns a buff zone on the ground. Sects: War, Ash, Ruin, Wrath.

### Adoption Economy

| Age | Points awarded |
|-----|----------------|
| Age II | 6 |
| Age III | 8 |
| Age IV | 10 |
| **Total** | **24** |

| Action | Cost |
|--------|------|
| Adopt same-culture sect | 2 pts |
| Adopt cross-culture sect | 3 pts |
| Upgrade Lv I → Lv II | 2 pts |
| Upgrade Lv II → Lv III | 3 pts |

Rules:
- Hard cap: 6 sects. Once adopted, cannot be dropped.
- Adoption is publicly announced. Upgrades are private.
- Lv II requires the sect to have been adopted in a previous age.
- Lv III requires Lv II in a previous age. Maxing requires three ages.
- Unspent points carry to the next age at 2:1 (4 unspent → 2 next age).

### Sect Definitions

Each sect has 4 levers (Passive, Building, Unit, Active Power), each with 3 levels.

#### Alanthor Sects

##### Sect of Antiquity — *the holy librarians*
**Identity:** Intel & enemy shutdown
- **Passive — Cataloged Memory**
  - I: +0.5%/kill against unit-type, cap +5%/type
  - II: +1%/kill, cap +10%
  - III: +1.5%/kill, cap +15%
- **Building — The Reliquary**
  - I: One of three abilities (map scry / ability lockout / vision aura) on long CD
  - II: All three unlocked, independent CDs
  - III: All three, CDs −30%, garrison effects double
- **Unit — Lorekeeper**
  - I: Reveals stealth (small radius). Garrison Reliquary → −15% CDs
  - II: Reveal radius doubled. Garrison → −30% CDs
  - III: Aura grants sight through fog. Garrison → −50% CDs
- **Active — Recall the Codex**
  - I: AoE freeze enemy ability cooldowns 10s, 5min CD
  - II: 15s freeze, 4min CD
  - III: 20s freeze + active CDs +50%, 3min CD

##### Sect of Renewal — *the master craftsmen*
**Identity:** Resilience & economy
- **Passive — Hands That Mend**
  - I: Buildings auto-repair 12% out-of-combat. Killed units refund 12% cost.
  - II: 25% / 25%
  - III: 40% repair / 35% refund
- **Building — The Workshop Eternal**
  - I: Adjacent buildings (3 tiles) +15% build/upgrade speed. Trains basic Automatons.
  - II: +25% adjacency. Slowly repairs nearby allied units. Automatons gain ranged variant.
  - III: +40% adjacency. Automatons +1 veterancy. Workshop immune to sabotage.
- **Unit — Tinker**
  - I: Field repair worker (slow). Cannot fight.
  - II: 2× repair speed. Salvages destroyed enemy buildings for partial refund.
  - III: Repair speed = Worker build speed. Salvage maxed. Self-heals when idle.
- **Active — Restoration**
  - I: AoE: instant 50% repair on all allied buildings & heal units. 6min CD.
  - II: Full repair + heal. 5min CD.
  - III: Full repair + heal + 30s damage reduction. 4min CD.

##### Sect of Fortitude — *the wall-keepers*
**Identity:** Static defense
- **Passive — Veiled Stone**
  - I: Walls/towers +12% HP. Towers +0.5 range.
  - II: +25% HP, +1 range, walls reflect 10% melee damage.
  - III: +40% HP, +1.5 range, 20% melee reflect.
- **Building — The Oath-Stone**
  - I: All buildings +12% HP. Destroyed buildings explode minor AoE.
  - II: +25% HP, moderate AoE.
  - III: +40% HP, heavy AoE. Oath-Stone immune to area damage.
- **Unit — Aegis-Bearer**
  - I: Heavy slow. Magical dome (25% damage reduction to allies inside). Cannot attack while projecting.
  - II: Larger dome, 50% reduction, +20% move speed.
  - III: Largest dome, 50% reduction + 20% reflect. Can attack at reduced speed while projecting.
- **Active — Stoneveil**
  - I: AoE: allies invulnerable 8s but cannot attack/move. 5min CD.
  - II: 15s. 4min CD.
  - III: 15s + post-effect +25% damage 10s. 3min CD.

##### Sect of Reclamation — *the curse-harvesters*
**Identity:** Curse exploitation
- **Passive — Curse-Hardened**
  - I: Units take −25% damage from Crystal Curse PvE. Workers harvest tier-1 cursed nodes.
  - II: −50% damage. All cursed nodes harvestable. Cursed-terrain slow ignored.
  - III: −75% damage. +25% yield from cursed nodes. Allies regen on cursed terrain.
- **Building — The Crucible**
  - I: Place on/near cursed terrain. Slow resource trickle while curse is nearby.
  - II: Trickle 2×. Pulses accelerate curse spread.
  - III: Trickle 3×. Pulses generate temporary buff zones for allied units in curse aura.
- **Unit — Soulsplitter**
  - I: Strong military. Active 90s CD: summons uncontrollable copy 10s. Killing original dispels copy.
  - II: Copy 15s, CD 60s.
  - III: Copy 20s, CD 45s. Copy can fork once if not killed.
- **Active — Harvest the Veil**
  - I: Target cursed terrain or PvE entity. Resource burst, 30s clearance. 5min CD.
  - II: Larger burst, 60s clearance. 4min CD.
  - III: Massive burst, 90s clearance, damages enemies caught. 3min CD.

#### Runaii Sects

##### Sect of Silence — *the entombed watchers*
**Identity:** Sealing, stillness, ambush. *Lore:* Silent Ones are sealed alive in vault-caverns with relics secured by the Sect of Justice.
- **Passive — Vigil**
  - I: Hold 8s → stealth + next attack +25%.
  - II: Hold 5s → +50%.
  - III: Hold 3s → +75%, plus +25% move speed for 3s on first move.
- **Building — The Sepulchre**
  - I: Entomb 1 unit (consumed) → Tier I aura: enemies inside cannot use actives.
  - II: Entomb up to 2 → +Tier II: enemies in aura −25% attack speed.
  - III: Entomb up to 3 → +Tier III: aura pulses damage to enemies inside.
- **Unit — Silent One**
  - I: Heavy ambusher. Self-entomb anywhere: invisible & immobile until enemy enters range, then bursts.
  - II: 2× burst damage. Re-entomb on CD after firing.
  - III: 3× burst damage. Re-entomb CD halved. While entombed, drains nearby enemy HP.
- **Active — Entomb**
  - I: AoE seal 5s — untargetable, immobile, no damage either way. 4min CD.
  - II: 8s. 3min CD.
  - III: 8s. On release, sealed enemies are Marked (visible, +25% damage taken) for 15s. 3min CD.

##### Sect of Justice — *the inquisitors*
**Identity:** Punishment & focus-fire
- **Passive — Marked for Sentence**
  - I: Enemy that kills your unit is marked 30s: visible through fog, +10% damage taken from your units.
  - II: +20% damage taken.
  - III: +30% damage taken. Killing marked enemy briefly marks adjacents.
- **Building — The Tribunal**
  - I: Unlocks 1 Sentence (Weakness/Slowness/Vulnerability), 60s CD, 30s effect.
  - II: All 3 Sentences, independent 60s CDs.
  - III: 3 Sentences, 45s CDs, 45s effect.
  - *Sentences:* Weakness (−50% damage out, 30s) · Slowness (−50% move/attack, 30s) · Vulnerability (+50% damage taken, 30s).
- **Unit — Inquisitor**
  - I: Slow caster. Cleanse 1 debuff from ally on CD.
  - II: Adds dispel: removes 1 buff from enemy on CD.
  - III: Adds combat mark: target +50% damage from your units 30s. Stacks with passive mark.
- **Active — Final Sentence**
  - I: AoE smite, 5s windup w/ telegraph, moderate damage. 5min CD.
  - II: 3s windup, heavy. 4min CD.
  - III: 3s windup, massive. Survivors marked 30s. 3min CD.

##### Sect of Veneration — *the devout of the crystal*
**Identity:** Stacking offense
- **Passive — Fervor**
  - I: Each kill → +3% damage / +3% attack speed for 3s. Stacks.
  - II: +5% / +5%, 3s.
  - III: +5% / +5% / +5% move, 4s.
- **Building — The Sanctified Pyre**
  - I: Aura (small radius): allied HP regen + +5% damage.
  - II: Full radius. Allies +10% damage. Enemies in aura −20% attack speed.
  - III: Largest radius. Allies +15% damage + 10% damage reduction. Enemies −30% attack speed.
- **Unit — Devotee**
  - I: Carries Blessing: blessed ally +20% damage / +20% HP. Re-target freely.
  - II: +30% / +30%. On death, Blessing jumps to nearest ally.
  - III: +30% / +30%. Two simultaneous Blessings (different allies).
- **Active — Crystal Communion**
  - I: Zone 15s: allies +25% damage, +15% damage reduction. 5min CD.
  - II: 20s: +50% / +25%. 4min CD.
  - III: 20s: +50% / +25% / +25% movement. 3min CD.

##### Sect of Witness — *the seers*
**Identity:** Pure information
- **Passive — All-Seeing**
  - I: Spotted enemies remain on minimap +15s after vision lost. Scouts +25% vision.
  - II: +30s. Scouts +50%.
  - III: +60s. Scouts +75%. Cursed terrain reveals enemies passing through.
- **Building — The Spire of Witness**
  - I: Moderate vision aura. Active 90s CD: reveal target area 8s.
  - II: Large aura. Reveal 10s. 60s CD.
  - III: Largest aura. Reveal 15s incl. stealthed. 45s CD.
- **Unit — Watcher**
  - I: Long-vision scout. Active 45s CD: True Sight reveals stealth in radius 10s.
  - II: 15s True Sight. On death, reveals area around corpse 30s.
  - III: 20s True Sight. Death reveal 60s. Watcher permanently stealthed when stationary.
- **Active — Foresight**
  - I: Reveal all enemy units across map 8s. 6min CD.
  - II: All units/buildings/stealthed 15s. 4min CD.
  - III: Full map 20s. Your units +25% damage vs revealed targets while active. 4min CD.

#### Feraldis Sects

> Faction-baseline mechanic: **Blood Pools.** Every Feraldis kill spawns a buff zone (AoI patch) on the ground. New channel on the GPU influence map. Sects below interact with this layer in distinct ways.

##### Sect of War — *the war-makers*
**Identity:** Pure military scaling. **Blood:** Generates more (bigger pools from elite kills via War Forge).
- **Passive — Forged in Battle**
  - I: Military −5% cost, +15% train speed. +0.5% global damage per military unit produced after first 20 (cap +12%).
  - II: −10% / +25% / +1% / cap +25%.
  - III: −15% / +35% / +1.5% / cap +35%.
- **Building — The War Forge**
  - I: Replaces standard military building. Units +10% HP. Kills generate slightly bigger blood pools.
  - II: +25% HP, +1 veterancy. Pools 1.5× radius.
  - III: +40% HP, +1 veterancy + random bonus stat. Pools 2× radius.
- **Unit — Warbreaker**
  - I: Heavy elite. Active 60s CD: Challenge — forces enemies in radius to attack only Warbreaker for 4s.
  - II: 6s, 45s CD.
  - III: 8s, 30s CD. Allies near Warbreaker during Challenge +25% damage.
- **Active — Call to Arms**
  - I: 15s of free military training (1 queue/building). 8min CD.
  - II: 30s. 6min CD.
  - III: 30s, trained units +1 veterancy. 5min CD.

##### Sect of Ash — *the burners*
**Identity:** Area denial & blood ignition. **Blood:** Weaponizes (turns blood into fire).
- **Passive — Scorched Ground**
  - I: Blood pools your units enter ignite 5s, minor damage to enemies inside. Allies still gain blood buff.
  - II: 8s ignite, moderate damage. Destroyed enemy buildings burn rubble 60s, blocking rebuild on footprint.
  - III: 12s ignite, heavy damage. Rubble 120s. Allies in burning blood +10% damage.
- **Building — The Furnace**
  - I: Smoke cloud reduces enemy ranged range −15% inside.
  - II: −30%. Ranged accuracy disrupted.
  - III: −40%. Smoke also damages enemies inside.
- **Unit — Cinderwalker**
  - I: Mid-tier infantry. On death: small AoE explosion. Death in blood pool ignites connected pool 5s.
  - II: Bigger explosion. Death in pool ignites entire connected blood region.
  - III: Biggest explosion. In pool, +15% damage. Death detonation also creates fresh blood pool.
- **Active — Pyre**
  - I: AoE ignites ground 15s, damages enemies inside.
  - II: 30s. Zone impassable to enemies (heavy damage to cross).
  - III: 45s. Impassable. Existing blood in zone ignites permanently for duration.

##### Sect of Ruin — *the iconoclasts*
**Identity:** Anti-building & desecration. **Blood:** Creates from buildings (alt source).
- **Passive — Profane Hands**
  - I: +25% damage to buildings. Destroying refunds 12% build cost.
  - II: +50% / 25%.
  - III: +75% / 35%. Destroyed buildings leave small blood pool on footprint.
- **Building — The Desecrator**
  - I: Channel slow curse on target enemy building anywhere. Loses 0.5% max HP/s.
  - II: 1%/s.
  - III: 1.5%/s. Two simultaneous targets.
- **Unit — Defiler**
  - I: Siege specialist. Active 90s CD: Defile — channel 4s on enemy building, disable 20s.
  - II: 30s disable, 60s CD. Destroying buildings creates blood pools.
  - III: 45s disable, 45s CD. Defile slows nearby enemy production.
- **Active — Unmake**
  - I: Target enemy building. 5s windup w/ telegraph. 50% current HP as damage. 6min CD.
  - II: 75% current HP. 4min CD.
  - III: 90% current HP. Adjacent enemy buildings 25% splash. 4min CD.

##### Sect of Wrath — *the godless*
**Identity:** Risk/reward berserker. **Blood:** Lives in it (sustain, healing, resurrection).
- **Passive — Spite of the Forsaken**
  - I: +0.5% damage per 5% HP missing. +10% damage in blood pools.
  - II: +1% / +25%.
  - III: +1.5% / +40%.
- **Building — The Hollow Altar**
  - I: Allied combat-deaths resurrect once at 25% HP after 30s. Cannot resurrect again.
  - II: 50% HP. Dying in blood pool returns at full HP with permanent +10% damage.
  - III: 75% HP. Dying in blood pool: full HP + permanent +20% damage + +10% HP.
- **Unit — Wrathborn**
  - I: Berserker infantry. Cannot be healed conventionally. +3% attack speed per 10% HP missing. Regen in blood pools.
  - II: +5%/10%. Faster regen in blood pools.
  - III: +8%/10%. Massive regen in blood pools. Self-bleed: lose 5% HP, create small blood pool (45s CD).
- **Active — Final Hour**
  - I: 12s, units cannot drop below 1 HP. End: low-HP units explode AoE. 8min CD.
  - II: 20s. End-explosions create blood pools. 6min CD.
  - III: 20s. End-explosions create large blood pools + apply Bleeding (DoT) to enemies hit. 5min CD.

### Cluster Mechanical Spread

| Sect | Cluster | Eco | Mil | Utility | Niche |
|------|---------|-----|-----|---------|-------|
| Antiquity | Alanthor | — | — | ●●● | Intel & enemy shutdown |
| Renewal | Alanthor | ●●● | — | ● | Resilience & repair |
| Fortitude | Alanthor | — | ●● | ● | Static defense |
| Reclamation | Alanthor | ●● | — | ●● | Curse engagement |
| Silence | Runaii | — | ●● | ●● | Sealing, stillness, ambush |
| Justice | Runaii | — | ●●● | ● | Punishment & focus-fire |
| Veneration | Runaii | ● | ●●● | — | Stacking offense |
| Witness | Runaii | — | — | ●●●● | Pure information |
| War | Feraldis | — | ●●●● | — | Pure military scaling |
| Ash | Feraldis | — | ●● | ●● | Area denial & counter-ranged |
| Ruin | Feraldis | ●● | ● | ● | Anti-building & demolition |
| Wrath | Feraldis | — | ●●● | ● | Risk/reward berserker |

### Balance Flags

1. **Wrathborn + Hollow Altar interaction.** Wrathborn cannot be healed conventionally → Hollow Altar = primary recovery. "Must pick the building if you pick the unit" trap. Lv III self-bleed partially mitigates for cross-faction picks.
2. **Ruin's economy refund potentially uncapped.** Late-game demolition could fund another army. Possible per-game cap or diminishing return.
3. **Final Hour is the most dramatic active.** Risk: other Feraldis actives feel underwhelming. Fix is presentation parity, not nerf.
4. **Reclamation + Ash combo.** Two AoI systems (curse + blood) compounding. Watch for runaway scaling.
5. **Veneration + Renewal combo.** Renewal keeps army alive; Veneration's Fervor stacks decay only when units stop killing. Self-sustaining army may keep stacks indefinitely.
6. **Cross-faction Wrath is mechanically weak.** Identity depends on blood pools, which non-Feraldis players generate slowly. Lv III self-bleed helps but may not be enough — playtest before adjusting.
7. **Lv I designs must be playable on their own.** Wide Collector builds adopt many sects at Lv I and never upgrade. Every Lv I needs standalone value. Sepulchre Lv I (1 entombment) and Tribunal Lv I (1 Sentence) are most at risk of feeling underpowered.

---

## §Implementation plan

### Existing sect machinery (must migrate or replace)

| File | Role | Migration strategy |
|------|------|--------------------|
| `Assets/Scripts/Economy/SectConfig.cs` | 12 string IDs + IsSectTech / GetTechId / GetSectIdForTechId | Rename the 12 IDs to new roster. Inverse-lookup tables stay structurally the same. |
| `Assets/Scripts/Economy/FactionSectState.cs` | Per-faction adoption flags + multipliers | Replace flags with `SectAdoptionState[12]`. Multipliers struct stays for live-tick reads (e.g. VaultInterest); some Q-15 dead fields will get wired in Phase 2. |
| `Assets/Scripts/Economy/SectEffectSystem.cs` | Applies multiplier deltas on adoption + effect-application API | Replace with per-sect, per-level effect dispatch. Old delta system retired. |
| `Assets/Scripts/Systems/Combat/SectBuildingAuraSystem.cs` | Existing sect aura buildings (Stoneheart, GlassSanctum, Tribunal already there) | Three of these map cleanly to new sect buildings (Tribunal=Justice, GlassSanctum→Witness/Sepulchre?, Stoneheart→Fortitude Oath-Stone). Migrate building tags + aura logic. |
| `Assets/Scripts/Systems/Combat/UnitAbilitySystem.cs` | 12 sect unit abilities | Re-map each ability to its new sect. Some keep their effect (e.g. Sanction → Justice's Marked-for-Sentence dispel; Safeguard → Fortitude/Renewal aura). |
| `Assets/Resources/JSON/TechTree*.json` | Sect tech entries | Rename keys 1:1. Keep effect bodies as scaffolding. |
| `Assets/Scripts/Entities/Buildings/BuildingFactory.cs` | `Chapel_Sect_*` + `Sect_*` building creators | Rename Chapel keys + sect-unique-building keys to new roster. |
| `Assets/Scripts/Core/Components/SpellComponents.cs` | SpellBuff fields | Existing fields suffice for most Phase 1/2 work; new fields added per-sect as needed. |

### Phase 1 deliverables (this PR + maybe one follow-up)

1. **Rename the 12 sect string IDs** in SectConfig + cascade through every file in the migration table. All existing effect bodies preserved verbatim under the new names so the game still runs.
2. **`SectDefinition` ScriptableObject** type (`Assets/Scripts/Economy/SectDefinition.cs`):
   - `string SectId`
   - `Cluster Cluster` (enum: Alanthor/Runaii/Feraldis)
   - `SectLeverData Passive`, `Building`, `Unit`, `ActivePower`
   - `SectLeverData`: nested `[3]` array of `SectLevelData` (one per level).
   - `SectLevelData`: human description + serialized parameters + reference to a strategy class (e.g. by-name) that knows how to apply the effect.
3. **`SectAdoptionState`** per faction:
   ```csharp
   struct PerSectState {
     public byte AdoptedAtAge;   // 0 = not adopted; 2/3/4 otherwise
     public byte PassiveLevel;   // 0/1/2/3
     public byte BuildingLevel;
     public byte UnitLevel;
     public byte ActivePowerLevel;
   }
   PerSectState[12] Sects;  // indexed by sect-id-to-index map
   ```
4. **RP economy** (`FactionReligionPoints` component or singleton):
   - On age-up: award 6/8/10 + carryover ⌊leftover / 2⌋.
   - `Spend(faction, amount, action)` validates affordability + age-gating.
5. **Adoption helper** (`SectAdoption` static):
   - `Adopt(faction, sectId)` enforces hard cap, deducts cost, records age, fires `SectAdopted` event.
   - `UpgradeLever(faction, sectId, lever)` validates Lv II/III prior-age rule + deducts cost.
6. **Adoption UI stub** — show 12 sects, RP balance, affordability colour-coding. Existing `SectAdoptionPanel.cs` is the entry point; rebuild the model layer behind it.

### Out of scope for Phase 1 (Phase 2-5)

- Per-sect Lv I lever effects.
- Blood pool channel.
- Lv II / Lv III lever effects.
- Style polish on adoption UI.
