# Visual effect assignments

Every action, event and state that needs (or already has) a visual effect.
Fill the **Resource** column with the pack and prefab to use (e.g.
`Lana / Regeneration_health`, `Hovl / Smoke loop`, `keep`, `none`) — that
column is the instruction the code is wired from.

**Kind:** *once* = plays one time when it happens · *loop* = plays for as long
as the state lasts · *landing* = plays where a power/ability lands.
**Now:** what plays today — `Lana` / `Hovl` = wired 2026-10-02, `proc` =
procedural code effect, `art` = an existing prefab (MagicArsenal / Korean
circles / Resources/Spells), `—` = nothing.

## 1. Units — combat and life

| # | Event / state | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 1.1 | Melee hit lands | once | victim | — | |
| 1.2 | Arrow / bolt in flight | loop | projectile | proc (trail tiers) | |
| 1.3 | Arrow / bolt impact | once | victim / ground | — | |
| 1.4 | Siege stone / bolt impact | once | ground / building | — | |
| 1.5 | Cavalry charge hit | once | victim | — | |
| 1.6 | Unit dies | once | corpse | proc (corpse dissolve) | |
| 1.7 | Unit healed (any source, not repair) | once | unit | Lana Regeneration_health | |
| 1.8 | Unit gains a rank (veterancy) | once | unit | proc (rank pips) | |
| 1.9 | Unit trained (leaves the building) | once | rally/exit point | proc (sparkle) | |
| 1.10 | Unit stunned / launched (Shardbound Fury) | loop | unit | — | |
| 1.11 | Unit slowed (any slow) | loop | unit | — | |
| 1.12 | Unit disarmed (Full Gallop sprint) | loop | unit | — | |
| 1.13 | Unit under a shield bar | loop | unit | proc (shield bar) | |

## 2. Heroes

| # | Event / state | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 2.1 | Hero levels up | once | hero | — | |
| 2.2 | Hero ability unlocked by level | once | hero | — | |
| 2.3 | Hero dies | once | hero | — | |
| 2.4 | Hero revived — Rally the Oath | once | hero | — | |
| 2.5 | Hero revived — Full Honours | once | hero | — | |
| 2.6 | Pledge army soldier appears | once | each soldier | — | |
| 2.7 | Pledge army soldier expires | once | each soldier | — | |
| 2.8 | Shardbound Hero awakened | once | hero | — | |

## 3. Catalog abilities

| # | Ability (owner) | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 3.1 | King's Call (Lexor, passive aura) | loop | Lexor | Lana Orbs_gold | |
| 3.2 | King's Call — buffed ally | loop | each ally in 15 m | — | |
| 3.3 | Liquid Courage (Lexor, 10 s) | loop | Lexor | Lana Shield_gold | |
| 3.4 | Veilshift Withdrawal (Lexor, 5 s) | loop | Lexor | Lana Fog_speedSlow | |
| 3.5 | Life Cling (Lexor, 5 s) | loop | Lexor | Lana Fog_herts | |
| 3.6 | Honour thy Pledge (Lexor) | once | Lexor | Lana Level_up | |
| 3.7 | Shardbound Fury (Lexor, 25 m) | once | around Lexor | Lana top_down_lightning_circle_blue | |
| 3.8 | Shardbound Fury — death detonation | once | where he fell | — | |
| 3.9 | Automate Facility (Ledger, cast channel 6 s) | loop | Ledger | — | |
| 3.10 | Automate Facility — the buff (30 s) | loop | building | Lana Fog_speedFast | |
| 3.11 | Under Automation lockout (60 s) | loop | building | — | |
| 3.12 | Scout Sight (passive) | loop | scout | — | |
| 3.13 | Use Celestar (reveal) | landing | target area | — | |
| 3.14 | War Horn (cavalry) | once | around caster | — | |
| 3.15 | Full Gallop (cavalry sprint) | loop | each rider | — | |
| 3.16 | Choreographed Volleys (ranged) | loop | each archer | — | |
| 3.17 | Deploy Field Hospital (Litharch) | once | Litharch | Lana Regeneration_health_area | |
| 3.18 | Field Hospital tent alive (120 s) | loop | tent | Lana Regeneration_health_area_loop | |
| 3.19 | Ability cast channel (any) | loop | caster | — (cast bar only) | |

## 4. Sect powers (landing + lasting state)

The wind-up telegraph is the Korean ground circle for every power today
(4.0). Each power's landing plays its sect's shared effect unless a row
below says otherwise.

| # | Sect · power | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 4.0 | Wind-up telegraph (all powers) | loop | target circle | art (Korean circle) | |
| 4.1 | Renewal · Hands of Plenty | landing | circle | Lana Regeneration_health_area | |
| 4.2 | Renewal · Hands of Plenty III regen tail | loop | each unit | Lana Regeneration_health_loop | |
| 4.3 | Renewal · Raise Anew | landing | point | Lana Poof_leaves | |
| 4.4 | Renewal · Second Wind | landing | circle | Lana Burst_rings | |
| 4.5 | Renewal · Second Wind ward | loop | each unit | Lana Shield_wind | |
| 4.6 | Antiquity · Writ of Attainder | landing | | art (NovaArcane) | |
| 4.7 | Antiquity · Heavy Bureaucracy (buildings shut down) | loop | each building | art (NovaArcane) | |
| 4.8 | Antiquity · Sew Disorder (units turn hostile) | loop | each unit | art (NovaArcane) | |
| 4.9 | Fortitude · Stoneveil | landing | | art (NovaEarth) | |
| 4.10 | Fortitude · Bulwark | landing | | art (NovaEarth) | |
| 4.11 | Fortitude · Immovable | landing | | art (NovaEarth) | |
| 4.12 | Reclamation · Harvest the Veil | landing | | art (NovaLife) | |
| 4.13 | Reclamation · Cleanse | landing | | art (NovaLife) | |
| 4.14 | Reclamation · Veil-Touched | landing | | art (NovaLife) | |
| 4.15 | Silence · Hush / Entomb / Whisper-Wind | landing | | art (NovaStorm) | |
| 4.16 | Justice · Eye of the Law / Sentence / Writ of Blood | landing | | art (LightPillarBlast) | |
| 4.17 | Justice · Marked (state) | loop | marked unit | — | |
| 4.18 | Veneration · Litany / Crystal Communion / Ascend | landing | | art (AuraCastLight) | |
| 4.19 | Witness · Spy Network / Blinding Glare / Nowhere to Hide | landing | | art (AuraCastArcane) | |
| 4.20 | War · Blood Rain / Call to Arms / Bloodfury | landing | | art (NovaFire) | |
| 4.21 | Ash · Pyre / Cinderfall / Ashen Veil | landing | | art (AreaDamageFire) | |
| 4.22 | Ruin · Unmake / Profane Strike / Sunder | landing | | art (ShadowPillarBlast) | |
| 4.23 | Wrath · Final Hour / Spite / Wrathfire | landing | | art (FirePillarBlast) | |
| 4.24 | Wrath · Spite link (state) | loop | linked units | — | |

Rows 4.15–4.23 bundle a sect's three powers; split them if each should differ.

## 5. Religion

| # | Event / state | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 5.1 | Curse kill pays religion points (last hit) | once | killer | — | |
| 5.2 | A Religion Point is earned | once | Temple | — | |
| 5.3 | Temple completed (RP unlocked) | once | Temple | — | |
| 5.4 | Sect adopted (chapel built) | once | chapel | — | |
| 5.5 | Chapel level bought | once | chapel | — | |
| 5.6 | Sect hero unit arrives | once | unit | — | |
| 5.7 | Mending Hall healing units inside | loop | hall | — (heals show 1.7) | |
| 5.8 | Sanctum held (+1 RP/min) | loop | sanctum territory | — | |

## 6. Buildings

| # | Event / state | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 6.1 | Building planned (owner-only preview) | loop | plan | proc (white ghost) | |
| 6.2 | Construction progresses | once per 5 % | site foot | Hovl Dust puff | |
| 6.3 | Construction completed | once | building | — | |
| 6.4 | Building levelled up | once | building | proc (dissolve wave) | |
| 6.5 | Age-up (landmark completes) | once | landmark | — | |
| 6.6 | Building damaged below 50 % | loop | roof | Hovl Smoke loop | |
| 6.7 | Building being repaired by workers | loop | building | — | |
| 6.8 | Renewal auto-repair (Hands That Mend) | loop | building | — | |
| 6.9 | Building collapses (destroyed / ground lost) | once | building | proc (inward fall + dust) | |
| 6.10 | Building demolished / plan cancelled (Delete) | once | building | — | |
| 6.11 | Research completed | once | building | — | |
| 6.12 | Wall gate opens / closes | once | gate | proc (doors swing) | |
| 6.13 | Wall converted to gate / tower | once | wall piece | — | |
| 6.14 | Trading Outpost trading (per recipe) | loop | outpost | — | |
| 6.15 | Trading Outpost recipe switched | once | outpost | — | |
| 6.16 | Mine / hut producing (income tick) | once per tick | building | proc (floating income text) | |

## 7. Territory and resources

| # | Event / state | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 7.1 | Territory claimed | once | territory centre | — | |
| 7.2 | Territory being contested (meter frozen) | loop | territory centre | — | |
| 7.3 | Territory lost | once | territory centre | — | |
| 7.4 | Territory cut off from a Fortress (wearing down) | loop | territory | — | |
| 7.5 | Veilstone outcrop becomes Cursed | once | outcrop | — | |
| 7.6 | Veilstone outcrop Depleted | loop | outcrop | — | |
| 7.7 | Outcrop refilled by the curse | once | outcrop | — | |
| 7.8 | Curse node destroyed → outcrop pacified (Feraldis veilsteel) | once | outcrop | — | |

## 8. The curse

| # | Event / state | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 8.1 | Curse node emerges | once | node | proc (CurseBeaconVfx pulse) | |
| 8.2 | Curse node standing | loop | node | proc (beacon) | |
| 8.3 | Curse node destroyed | once | node | — | |
| 8.4 | Curse unit spawns | once | unit | — | |
| 8.5 | Curse wrath rises against a faction | once | that faction's units | — | |
| 8.6 | Cursed territory edge | loop | border | proc (aurora veil) | |

## 9. The Shardroot

| # | Event / state | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 9.1 | Unearthed from a well | once | pickup | Lana Loot_drop | |
| 9.2 | Lying on the ground | loop | pickup | Lana Loot_iddle | |
| 9.3 | Being attuned (20 s) | loop | pickup → unit | proc (light beam) | |
| 9.4 | Picked up | once | bearer | Lana Loot_pick_up | |
| 9.5 | Carried | loop | bearer | Lana Loot_flicker | |
| 9.6 | Dropped (bearer died) | once | pickup | Lana Loot_drop | |
| 9.7 | Enshrined in a Temple | once | Temple | — | |
| 9.8 | Embedded in a Maw | loop | maw | proc (gem + light) | |

## 10. Fire

| # | Event / state | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 10.1 | Ground burning | loop | tile | — | |
| 10.2 | Burnt to ash | loop | tile | — | |
| 10.3 | Blood chain-ignition | once | every blood tile | — | |
| 10.4 | Unit burning (burn damage over time) | loop | unit | — | |

## 11. Match

| # | Event / state | Kind | Plays on | Now | Resource |
|---|---|---|---|---|---|
| 11.1 | Player eliminated | once | their last building | — | |
| 11.2 | Victory / defeat | once | screen / camera | — | |
