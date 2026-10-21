# Spell / Ability Audit — The Waning Border 1.2 (working tree, 2026-09-25)

Read-only audit. Paths are relative to `D:\Users\overw\Documents\The Waning Border 1.2\`.

## 0. There are FIVE separate spell pipelines, not one

| # | Pipeline | Trigger component / entry | Numbers live in | Who can cast it today |
|---|---|---|---|---|
| A | **Card engine** (`GameSystems/Abilities/AbilityLifecycleSystem.cs`, `AbilityEffectExecutor.cs`, `AbilityAuraSystem.cs`) | `AbilityActivated` + `UnitAbilities` slots; `CommandRouter.IssueUnitAbility[Slot]` | **Code seed** `GameData/TechTree/Shared/Abilities/AbilityCatalog.cs` `_seedCards` (see §1 — the SOs are NOT loaded) + per-kind constants in owner folders | Player via `SpellsPanelBinder.cs:238/244`. **No AI caller.** |
| B | **Legacy sect-unit abilities** (`Scripts/Systems/Combat/UnitAbilitySystem.cs`) | `AbilityActivated` + `UnitAbility` (single struct) ; `CommandRouter.IssueAbility` | Hard-coded in `UnitAbilitySystem.cs:96-215` (effects) and in each sect unit factory (`CooldownDuration`, `Range`) | **Nobody.** `CommandRouter.IssueAbility` (CommandRouter.cs:765) has zero callers; no UI/AI reads `UnitAbility`. Dead in play. |
| C | **Sect active powers** (`GameData/TechTree/Sects/SectActivePowerSystem.cs` + `Cultures/*/SectActivePowerSystem.*.cs`) | `SectActivePowerHelper.Fire` → `PendingSectStrike` (wind-up) | C# tables `SectLeverEffects.cs:265-420` (legacy) and `Cultures/{Alanthor,Feraldis,Runai}/SectLeverEffects.*.cs` (canon). **Not JSON** — CLAUDE.md's "JSON-backed" is stale. | Player via `ReligionPanelBinder.cs`; AI only for Alanthor (`AIAlanthorEndgameSystem.Sects.cs`) |
| D | **Legacy culture god power** (`Scripts/Systems/Economy/GodPowerSystem.cs`) | `PendingGodPowerCast` on the bank (CommandRouter.cs:624) | `Core/Settings/BorderConstants.cs:240-249` (CD 90, r 14, dmg 120) | Nobody — HUD/targeting removed (`WorldClickInput.cs:49`). Dead. |
| E | **Bespoke per-owner payloads** called from A (Shardbound Fury, Pledge Army, Field Hospital) | card effect kinds 15/16/18 | `const` fields in the owner's folder | via A |

Two systems (A and B) consume the **same** `AbilityActivated` component with no ordering between them (both `[UpdateBefore(MeleeCombatSystem)]`). A unit that ever carried both `UnitAbilities` and `UnitAbility` would have the activation eaten by whichever runs first.

## 1. SO rule violation: the AbilityDefSOs are dead data

`AbilityCatalog.LoadFromSOs()` (AbilityCatalog.cs:216-229) does `Resources.Load<AbilityCatalogSO>("AbilityCatalog")`. **No `Assets/Resources/AbilityCatalog.asset` exists** (never committed; not in `git ls-files`), and the "Generate Ability SOs" menu referenced in the comments no longer exists in code. So `Cards` always falls back to `_seedCards`.

Consequences:
- The 12 `AbilityDefSO` assets under `**/Abilities/<Ability>/*.asset` are **never read**. Editing them in the Inspector changes nothing.
- Only 12 of the 14 seed cards have an SO (no SO for **Choreographed Volleys** or **Shardbound Fury**).
- The index contract ("list order is the index") would silently change if a catalog asset were ever created in a different order — slots are stored as indices (`UnitAbilities`) and cross the lockstep wire.
- Seed comments label cards "14" and "15" but they are indices **12** and **13**.

Fix direction: make `AbilityCatalogSO` a TechCatalog-style non-Resources catalog (or add it to `ComponentConfigCatalog`-like loader), key cards by **name/id not index**, add the two missing SOs, delete the code seed (or keep it only as a loud-failure test fixture).

## 2. Master table

Legend — Src: **Seed** = AbilityCatalog.cs code seed (live), **SO** = AbilityDefSO asset (dead), **Code** = hard-coded const/literal, **Tbl** = SectLeverEffects C# table. CD = cooldown as authored; "eff." = what the code actually applies. Cast = delay before effect. All radii/ranges in metres.

### 2A. Card engine (pipeline A)

| Idx | Ability | Owner | Activation / targeting / affects | Effect kinds (value) | Cast | Dur | CD | Radius | Range | Dmg/heal | Dmg type | Cost | Numbers live in | Notes |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | King's Call | King Lexor (`Civs/Alanthor/Units/KingLexor/Abilities/KingsCall`) | Passive · Aura · AlliedCulture | AttackPct 15, ArmorPct 15, ChargeBonusFlat 20 | 0 | perm | — | 15 | 0 | — | — | — | Seed (SO dead) | Stacks with sect/aura buffs? Uses SpellBuff merge |
| 1 | Liquid Courage | King Lexor | Active · Self | DamageTakenPct −90, AttackPct +30 | 0 | 10 | **0 → auto 11 s** | 0 | 0 | — | — | — | Seed | **90 % DR at 91 % uptime** (10 s on, recast 1 s after it ends). Aftermath → Veilshift + Life Cling. No design value anywhere in docs/Design. |
| 2 | Veilshift Withdrawal | King Lexor (aftermath) | Active · Self | MoveSpeedPct −50, SelfDoTPctOverDuration 50 | 0 | 5 | 0 → 6 s | 0 | 0 | self-DoT 50 % max HP (325 HP on 650) | untyped direct HP | — | Seed | Life Cling floors it at 1 HP, so the drawback cannot kill |
| 3 | Life Cling | King Lexor (aftermath) | Active · Self | HpFloor 1 | 0 | 5 | 0 → 6 s | 0 | 0 | — | — | — | Seed | Floor only read by SelfDoT (AbilityLifecycleSystem.cs:158) — verify combat paths honour it |
| 4 | Automate Facility | Ledger (`Civs/Alanthor/Units/Ledger/Abilities`) | Active · Single · EconomicBuildings | ResourceYieldPct 30 | **6** | 30 | 0 → auto 37 s | 0 | 5 (+2 slack, AbilityAuraSystem.cs:238) | — | — | — | Seed | Design (Age_1_Alanthor.md:1367-1371) = 30 s boost + 60 s lockout via aftermath — matches |
| 5 | Under Automation | Ledger (aftermath, on building) | Active · Self | NoAutomation | 0 | 60 | 0 → 61 s | 0 | 0 | — | — | — | Seed | |
| 6 | Use Celestar | Scout (`Age0/Buildings/Hall/Units/Scout/Abilities`) | Active · Area · Self | RevealFog 10 | **5** | 15 | 30 | 10 | **0 = unlimited** | — | — | — | Seed | Map-wide aim with only a 5 s delay |
| 7 | Scout Sight | Scout | Passive · Self | LosRampWhileStill | 0 | perm | — | 0 | 0 | — | — | — | Seed + tunables in Scout code | |
| 8 | War Horn | Royal Stable research → all cavalry (`Civs/Alanthor/Buildings/RoyalStable/Abilities`) | Active · Area · AlliedCavalry | ChargeDamagePct 50 | 0 | 20 | 60 | 20 | 0 | +50 % next charge | as unit | research only | Seed | |
| 9 | Full Gallop | Royal Stable research → cavalry | Active · Area · AlliedCavalry | MoveSpeedPct 40, DisarmWhileBuffed | 0 | 8 | 75 | 20 | 0 | — | — | research only | Seed | |
| 10 | Deploy Field Hospital | Litharch, Sect of Renewal research (`Sects/Renewal/Abilities/DeployFieldHospital`) | Active · Self | DeployFieldHospital | **3** | (120 s lifetime) | 300 | 12 | 0 | 3 HP/s in r12, 400 HP building | untyped direct HP | research only | Seed + **Code** `FieldHospital.cs:23-28` (lifetime 120, r 12, 3 HP/s, 400 HP, sight 10) | Card Radius 12 duplicates FieldHospital.HealRadius 12 — two sources. Matches Sects.md:195-198 |
| 11 | Honour thy Pledge | King Lexor, hero Lv ≥ 4 | Active · Area · Self | SummonPledgeArmy | 0 | 0 | 90 | 6 | 0 | summons (level) soldiers, 1 archer per 3, rank 1+(L−4)/2, 30+5(L−4) s | — | — | Seed + **Code** `PledgeArmy.cs:26-42` (ladder), `HonourThyPledge.cs:26` (UnlockLevel 4 duplicated with SO `unlocksAtLevel`) | Heroes.md §3 |
| 12 | Choreographed Volleys | any allied ranged unit after Archery Range tech | Active · Area · AlliedRanged | FireRatePct 100 | 0 | 5 | **120** | 20 | 0 | 2× fire rate | — | research only | Seed only (**no SO**) | **Design says 40 s CD** and faction-wide (Age_0.md:294, Age_1_Alanthor.md:229, Age_1_Feraldis.md:391, Age_1_Runai.md:233). Code: 120 s, 20 m circle |
| 13 | Shardbound Fury | King Lexor while bearing the Shardroot | Active · Area · Enemies | ShardboundFury | 0 | 0 | 120 (`FuryCooldown`) | 22 (`FuryRadius`) | 0 | **90 slam** to units, **600** to buildings; Detonation variant **2 500** bldg and **1<<20 (instakill)** land dmg | untyped direct HP (`ShardboundFury.cs:179`, `ShardboundSystems.cs:112`) | Shardroot | **Code** `ShardboundFury.cs:51-71` (card fields point at the consts; **no SO**) | Bypasses armour, Invulnerable, DR, kill credit |

### 2B. Legacy sect-unit abilities (pipeline B — never castable in play)

All numbers are **Code** (effect in `UnitAbilitySystem.cs`, CD/range in the unit factory). No cast time, no range check (`IssueAbility` only checks target existence, CommandRouter.cs:776-779), no hostility check on targeted debuffs.

| Ability | Owner unit (sect) | Effect (hard-coded) | CD | Range | Radius | Dmg/heal | Dmg type | Factory line | Sects.md says instead |
|---|---|---|---|---|---|---|---|---|---|
| RapidMend | Scar Guard (Renewal) | self HoT 50 HP / 3 s | 15 | 0 | — | heal 50 | — | ScarGuard.cs:54 | Scar Guard = "more damage the closer to dying" (Sects.md:190) |
| ArcanePulse | Golem Autark (Reclamation) | 15 dmg to enemies in 5 m | 20 | 0 | 5 | 15 | untyped direct HP (UnitAbilitySystem.cs:326) | GolemAutark.cs:68 | |
| Fortify | Stone Warden (Fortitude) | +5 armour, immobile, 8 s | 25 | 0 | — | — | — | StoneWarden.cs:54 | |
| Dispel | Archivist Adept (Silence) | strip SpellBuff/Debuff/Condemned/Ignite/VoidStrike/Fortified from target (any faction) | 12 | 14 | — | — | — | ArchivistAdept.cs:69 | "suppresses enemy abilities within a small radius around itself" |
| Sanction | Flame Warden (Retired) | root 2 s | 10 | 6 | — | — | — | FlameWarden.cs:54 | retired sect |
| Safeguard | Vault Keeper (Veneration) | +3 armour 5 s to allies in 6 m | 18 | 0 | 6 | — | — | VaultKeeper.cs:54 | |
| MirrorShield | Glassmark Arcanist (Witness) | reflect 30 % 6 s | 22 | 0 | — | — | — | GlassmarkArcanist.cs:68 | |
| Condemn | Judicator (Justice) | target takes ×1.25 for 6 s | 20 | 10 | — | — | — | Judicator.cs:54 | Judicator = bonus vs Marked |
| Ignite | Ashblade (Ash) | next 3 attacks +8 | 12 | 0 | — | +8 ×3 | as unit | Ashblade.cs:55 | |
| WarCry | Brandbreaker (Retired) | slow enemies in 8 m | 18 | 0 | 8 | — | — | Brandbreaker.cs:56 | retired sect |
| ChainBind | Chaincaster (Wrath) | root 3 s | 14 | 12 | — | — | — | Chaincaster.cs:68 | |
| VoidStrike | Nullblade (Ruin) | next attack +40 (+80 vs Border) | 20 | 0 | — | +40/+80 | as unit | Nullblade.cs:54 | |

Radii 5 / 6 m violate the four-radius rule (Sects.md §2: 1.5 / 8 / 15 / 25). War and Antiquity units have no entry.

### 2C. Sect active powers (pipeline C)

Global rules applied in `SectActivePowerHelper.Fire` (SectActivePowerSystem.cs:337-447), all **Code**:
- **Every authored cooldown is halved at cast** (`cooldown *= 0.5f`, line 370) — the tables below are NOT the real cooldown. Further ×0.5 with a Shardroot allocated, ×0.9 / ×0.8 at Shrine L1 / L2+.
- Wind-up (telegraph, then effect): **1.5 s** offensive (`OffensiveWindupSeconds`), **1.0 s** utility (`UtilityWindupSeconds`). Cooldown starts at cast. No cast range — any point on the map (fog not checked in `Fire`); offensive casts refused only within 8 m of a curse well.
- Blood Rain = global silence of casts (both sides).
- Damage (`ApplyCircleDamage` :732-772, Attainder `SectActivePowerSystem.Alanthor.cs:109`, Nowhere to Hide `SectActivePowerSystem.Runai.cs:215/236`, Unmake :654/675, Spite :724) is a raw `hp.Value -= dmg`: **ignores Defense (incl. magic column), damage type, ArmorType, Invulnerable (so Immovable III does not protect against it), Liquid Courage DR, DeathWard, and awards no kill credit / LastAttacker** (Heroes level from kills only).

Canon sects (answer from culture tables; all three slots available on adoption; level from adoption timing):

| Sect | Slot | Power | Kind | Lv I (r · mag · dur · CD) | Lv II | Lv III | Eff. CD (×0.5) | Src |
|---|---|---|---|---|---|---|---|---|
| Antiquity | 1 | Writ of Attainder | AttainderStrike | Small · 40/kill · – · 110 | Medium · 60/kill · 100 | Large · 80/kill, floor 60 · 90 | 55/50/45 | Tbl Alanthor.cs:58-70 |
| Antiquity | 2 | Heavy Bureaucracy | BuildingShutdown | Single · – · 30 · 150 | Small · 30 · 135 | Large · 30 · 120 | 75/68/60 | :76-82 |
| Antiquity | 3 | Sew Disorder | HostileConversion | Small · 8 · 300 | Medium · 20 · 270 | Large · **permanent** · 240 | 150/135/120 | :88-95 |
| Renewal | 1 | Hands of Plenty | HealCirclePercent | Small · 30 % · 90 | Medium · 50 % · 85 | Medium · 80 % + 10 s regen · 80 | 45/43/40 | :110-120 |
| Renewal | 2 | Raise Anew | RaiseTower | Tower · perm · 156 | Fortification · 195 | Fortress · 234 | 78/98/117 | :139-149 |
| Renewal | 3 | Second Wind | DeathWard | Small · 6 · 150 | Small · 12 · 140 | Medium · 12, +25 % heal · 130 | 75/70/65 | :155-163 |
| Fortitude | 1 | Stoneveil | Veil | Small · 8 · 120 | Small · 15 · 110 | Medium · 15, +25 % dmg after · 100 | 60/55/50 | :181-188 |
| Fortitude | 2 | Bulwark | BuildingHpBuff | Single · +100 % · 30 · 120 | Small · 30 · 110 | Medium · 30, 20 % melee reflect · 100 | 60/55/50 | :195-203 |
| Fortitude | 3 | Immovable | ArmorCircle → Invulnerable | Small · +5 armour · 10 · 120 | Medium · +8 · 15 · 130 | Large · **invulnerable** · 20 · 240 | 60/65/120 | :212-224 |
| Reclamation | 1 | Harvest the Veil | NodeOverYield | Single · 50 S/5 s · 30 · 120 | 75 S + 20 I | 150 S + 60 I + 35 V + 5 Vs | 60 | :243-251 |
| Reclamation | 2 | Cleanse | InfluenceBurst | Small · 6 · 20 · 120 | Medium · 6 · 40 | Large · 8 · 40 + regen | 60 | :260-268 |
| Reclamation | 3 | Veil-Touched | CurseWard | Small · 15 · 100 | Medium · 30 | Large · 30, +20 % speed on curse | 50 | :275-283 |
| War | 1 | Blood Rain | BloodRain | Small pool · ×1.05 AS map-wide · 10 · 240 | ×1.10 · 20 · 220 | ×1.15 · 30 · 200 | 120/110/100 | Tbl Feraldis.cs |
| War | 2 | Call to Arms | TrainingBoon | Single · cost ×0.5 · 15 · 150 | Small · 30 · 135 | Medium · 30 + ×2 speed (`CallToArmsSpeedLv3` const) · 120 | 75/68/60 | Feraldis.cs |
| War | 3 | Bloodfury | DamageCircle → DamageArmorCircle | Small · +25 % · 8 · 120 | Medium · 12 · 110 | Large · +25 % & +5 armour (`BloodfuryArmorLv3` const) · 12 · 100 | 60/55/50 | Feraldis.cs |
| Witness | 1 | Spy Network | SpyNetwork | Single · 3 s cascade r8 · 45 · 120 | r15 · 90 · 105 | 1.5 s · perm · 90 | 60/53/45 | Tbl Runai.cs |
| Witness | 2 | Blinding Glare | Blind | Small · 8 · 120 | Medium · 12 · 110 | Large · 12 + ability lock · 100 | 60/55/50 | Runai.cs |
| Witness | 3 | Nowhere to Hide | RevealedStrike | **map-wide** · **60 dmg** · 300 | 110 dmg, buildings · 270 | **200 dmg**, buildings · 240 | 150/135/120 | Runai.cs |

Legacy sects (Silence, Justice, Veneration, Ash, Ruin, Wrath) — `SectLeverEffects.ActiveOf` legacy table (SectLeverEffects.cs:272-296 slot 1, :331-415 slots 2-3). **Power level is ignored** (`ActiveOf(sectId, slot, level)` falls to `ActiveOf(sectId, slot)`); slots unlock by Temple level; radii are bespoke numbers (6/7/9/10/13/14/22/28 m) violating Sects.md §2; kinds do not match the Sects.md powers at all (e.g. Silence should be Hush / Entomb / Whisper-Wind, Justice Eye of the Law / Sentence 120 **true** dmg / Writ of Blood).

| Sect | Slot 1 | Slot 2 | Slot 3 | Src |
|---|---|---|---|---|
| Silence | SpeedCircle r8 ×1.20 8 s CD 90 | r9 ×1.30 10 s CD 75 | r10 ×1.40 12 s CD 60 | Tbl legacy |
| Justice | RevealCircle r14 10 s CD 60 | SmiteCircle r6 **60 dmg** CD 120 | SmiteCircle r10 **150 dmg** CD 240 | Tbl legacy |
| Veneration | DamageCircle r8 ×1.20 10 s CD 120 | r9 ×1.35 12 s CD 100 | r10 ×1.50 14 s CD 80 | Tbl legacy |
| Ash | BurningCircle r6 8 dps 6 s CD 120 | r7 11 · 9 s CD 100 | r8 14 · 12 s CD 80 | Tbl legacy + BurningGroundSystem |
| Ruin | UnmakeBuilding (search r8) 50 % current HP CD 150 | r9 75 % CD 125 | r10 90 % + 25 % splash in 6 m (const, :624-625) CD 100 | Tbl legacy |
| Wrath | SpitePool r6 CD 120 | r9 CD 100 | r13 CD 80 | Tbl legacy |

Dead legacy rows: Renewal/War tier-2/3 and Antiquity/Fortitude/Reclamation/Witness tier rows in the legacy table are unreachable (those sects are canon).

Also still live and contrary to Sects.md §1 ("There is no chapel aura"): `SectLeverEffects.AuraOf` (:210-242) is consumed by `Sects/Shared/Chapel/SectBuildingLeverSystem.cs:53` and `SectInfo.cs:257`.

### 2D. Legacy god power (pipeline D — dead)
`GodPowerSystem.cs`: CD 90 × 0.8^shardroot, r 14, 120 dmg (+15 %/shardroot, falloff), Alanthor heal 60 %. All `BorderConstants.cs:240-249`. Only enqueued via `CommandRouter.cs:624`; no UI. Delete or fold into C.

## 3. Mechanics the engine has / lacks

| Feature | Status | Evidence |
|---|---|---|
| Cast time | **Delay only.** `AbilityCastState.CastRemaining` counts down then applies (AbilityLifecycleSystem.cs:99-120). Cooldown charged at START. | |
| Cast interrupt | **None.** Nothing removes `AbilityCastState` — not movement, not a new order (`ClearAllCommands` removes `AbilityActivated` only, CommandRouter.cs:2120), not stun/silence ("anything already winding up still resolves", :80-82), not death (executor only checks `Exists`, AbilityEffectExecutor.cs:53, so a unit in its 2 s death animation still lands the cast). Sect wind-ups likewise cannot be interrupted (faction-level, no caster). | |
| Rooting during cast | None — caster may walk; the Ledger alone is special-cased (AbilityAuraSystem.cs:243). | |
| Cast bar / wind-up UI | **None** for card casts (no reader of `AbilityCastState`/`CastRemaining` in Presentation). Sect powers have a world telegraph ring only (`SectPowerVfx.SpawnTelegraph`). | |
| Range enforcement | Card engine: `card.Range` only displayed (SpellsPanelBinder.cs:166) and used by the Ledger aura; no issue-time or cast-time distance check. Legacy B: existence only. Sect C: unlimited range. | |
| Global cooldown | None, in any pipeline. | |
| Cooldown model | A: per-slot (4 slots), `0 ⇒ CastTime+Duration+1`. B: per unit. C: per (faction, sect, slot) on the bank, halved at cast. Three incompatible models. | |
| Charges / mana / cast cost | None. The only "costs" are research/Shardroot gates. | |
| Damage types for spells | **None used.** Combat model has `DamageType {Melee, Ranged, Siege, Magic, True}` and per-type flat `Defense` (M/R/S/Mg) (`Components/CombatComponents.cs:47-68`, `Data/TechTree/CombatModifiers.cs`: `final = max(1, base − defense[type]) + bonusVsTags`, then height/veil mods; the ArmorType matrix is kept but no longer feeds damage). Every spell damage path writes Health directly, so the **magic defense column (Litharch 3, Scholar 4, Lexor 3…) never meets a magic spell**, and "true damage" in Sects.md is indistinguishable from everything else. | |
| Resistances / immunity | `Invulnerable` checked by melee/ranged and legacy ArcanePulse, **not** by sect damage or Shardbound Fury. `ScaleIncoming` (Liquid Courage) likewise skipped by sect/Fury damage (see also docs/Task_Inventory.md:27-29). | |
| Kill credit / XP | Spell kills record no attacker → heroes (kills-only XP, Heroes.md) earn nothing from spells; Writ of Attainder's own kill tally ignores spell kills. | |
| Auto-cast | None anywhere; AI casts no unit abilities and only Alanthor sect powers. | |
| Stacking rule | `MergeSpellBuff` for SpellBuff; separate components (Fortified, Condemned, IgniteBuff…) outside it. | |

## 4. Inconsistencies worth fixing in the overhaul

1. **SO catalog never loads** (§1) — the whole "numbers in the SO" story for abilities is fiction today.
2. **Zero / auto cooldowns**: Liquid Courage (0 → 11 s → ~91 % uptime of 90 % DR on a 650 HP hero), Automate Facility (0 → 37 s, fine only because Under Automation locks the building), aftermath cards (0; harmless).
3. **Halved-at-cast sect cooldowns**: the tables, tooltips (`spec.Description`) and Sects.md are all 2× the live value.
4. **Choreographed Volleys** CD 120 vs design 40; area 20 m vs design faction-wide.
5. **Damage vs par**: Unit_Power par 100 ≈ Spearman 120 HP / Archer 60 HP / Scholar 90 HP. Nowhere to Hide III = 200 untyped map-wide to every revealed enemy every 120 s (eff.) — one-shots every Age 0/1 non-hero unit in the roster; Justice legacy slot 3 = 150 in r10 every 120 s; Shardbound Fury 90 in r22 + 600 to buildings every 120 s; Detonation = instakill. ArcanePulse 15 (irrelevant, dead). No spell is priced against Unit_Power; there is no "spell power per cooldown-second" metric.
6. **Radius rule** (Sects.md §2) broken by every legacy sect row and every legacy sect-unit ability; card engine radii (10/12/15/20/22) are not bound by it either (rule is scoped to sects — decide whether to extend).
7. **Duplicate kinds**: HealCircle vs HealCirclePercent; ArmorCircle vs Fortified vs SpellBuff.ArmorBonus vs King's Call ArmorPct; SpeedCircle vs MoveSpeedPct; DamageCircle vs AttackPct vs Condemned; SmiteCircle vs AttainderStrike vs RevealedStrike vs ArcanePulse vs GodPower; root = Sanction == ChainBind; three separate reveal paths (RevealFog → sect RevealCircle helper).
8. **Sect-unit abilities disagree with Sects.md** unit descriptions and are unreachable; Sects.md does not give them numbers either.
9. **Duplicated sources**: Field Hospital radius (card 12 + const 12), Honour thy Pledge unlock (SO 4 + const 4), Shardbound Fury card fields pointing at C# consts.
10. **Chapel aura table still live** despite "no chapel auras".

## 5. What an overhaul needs in the engine (minimum set)

1. **One ability pipeline.** Port pipeline B's 12 effects into card effect kinds (most already exist or are one new kind: Root, Dispel, NextAttackBonus, ReflectPct, DamageTakenMark, HealOverTime, FlatAoeDamage), give sect units `UnitAbilities`, delete `UnitAbilitySystem` + `UnitAbility` + `IssueAbility`. Delete `GodPowerSystem`. Keep sect powers on their faction-level path but share the damage/heal resolver and the wind-up component.
2. **A working SO catalog** keyed by stable string id; every number (including the payload consts in FieldHospital/PledgeArmy/ShardboundFury/Bloodfury/CallToArms and the 0.5 / 1.5 s / 1.0 s globals in `Fire`) moves into the SO or a `SectActivePowerSystemConfig` asset per the component-config rule.
3. **Spell damage resolver**: `SpellDamage.Apply(em, caster, target, amount, DamageType)` that runs `CombatModifiers.CalculateFinalDamage` with the target's Defense for that type (Magic by default, True where designed), honours Invulnerable / HpFloor / DeathWard / `ScaleIncoming`, stamps `LastAttackerEntity` / `LastDamagedByFaction` / `RecordDamageDealt` for kill credit, and posts damage numbers. Same for heals (`UnhealableTag`, allied-only, no-stack rule from Teams.md).
4. **Cast state machine**: `AbilityCastState` gains `Interruptible`, `RootsCaster`, `CancelOnMove`; ClearAllCommands / death / stun / silence (Entomb, Blinding Glare III, Blood Rain) cancel it; cooldown charged on completion (or refunded on interrupt — design call); channel support (effect ticks while channelling, e.g. rituals could reuse it).
5. **Range enforcement + approach**: cast order walks the caster into `Range` (stand-off point like `RitualApproach`) then casts; reject out-of-range at issue.
6. **Cast bar + cooldown sweep UI** reading `AbilityCastState` and the per-slot cooldowns; world telegraph for area casts (sect path already has one).
7. **Global cooldown / per-caster lockout** (optional; one float on `AbilityCooldowns`).
8. **Auto-cast toggle** for support/heal abilities (Litharch heal, Field Hospital, Safeguard) and AI cast logic for unit abilities (the AI casts none today).
9. **A "spell Power" metric** in `UnitPower.cs`/Unit_Power.md (e.g. damage × targets / effective CD) so spell numbers can be balanced against par ~100.
