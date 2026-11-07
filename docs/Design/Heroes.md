# Heroes

**Canon for what a hero is, hero levels, the Shardroot's Ascended state, hero
abilities and hero death.** Rewritten 2026-11-07 from the developer's rules; it
supersedes the earlier versions of this page and, where they differ, the hero
passages of [Religion.md](Religion.md) §4, [Sects.md](Sects.md),
[Spells.md](Spells.md) and [Curse_And_Shardroot.md](Curse_And_Shardroot.md) §3.1.

Every stat and price is on the hero's unit SO, every cooldown and radius on the
ability SO, the XP curve and the revival scale in `HeroProgressionConfig`, and
the Ascended numbers in `AscendedHero.asset`.

## 1. What a hero is

A hero is **a normal unit, but stronger and with levels**: trained at a building
and ordered like any soldier, one of each per faction.

| Hero | Trained at | Niche |
|---|---|---|
| King Lexor | Fortress | Army general for decisive battles |
| Ledger | Fortress | Armoured automaton that also drives the economy around it |
| Archivist of Hours | Reliquary | Ranged magic; shuts down enemy heroes and casters |
| Surgeon | Mending Hall | Fights on cursed ground and through attrition |
| Warden | Stonehold | Holds home ground, kills siege |
| Guildmaster | Treasury | Fast raider who makes fights pay |
| Harkon Fiend | Feraldis Fortress | Frontline breaker of heavy troops and buildings |
| Wayfinder | Runai Fortress | Fast leader who moves armies |

**The stat rule.** A hero must **beat every non-hero unit** one on one, sect units
included, and **stand toe to toe with every other hero**. Heroes differ in how they
win, not in how much. Checked by 1v1 time-to-kill from the SO stats at level 1,
without abilities: any two heroes should stay close to even, and no non-hero should
come close. Every hero's line of sight must be at least its largest ability radius.

The Ledger is a hero under this rule: it fights, takes orders and levels from kills.
[Vault_And_Ledger.md](Vault_And_Ledger.md)'s non-fighting Ledger does not stand.

A sect's **line unit is not a hero** ([Religion.md](Religion.md) §4). The sects
without an authored hero (the Runai and Feraldis clusters) have only their line unit.

## 2. Levels

Heroes have levels 1 to 10, **earned from kills, never bought**. A kill is worth the
victim's total resource cost in XP; the killer takes all of it and allied heroes
nearby take a share. Temporary summons are worth nothing as killer or victim. The
step between levels widens every level.

Levels unlock abilities. They grant no stat increase.

**Level and progress are public.** Every hero wears its own small bar, whoever owns it
and whatever the health-bar setting: health, XP toward the next level, and the level
number. It shows wherever the hero itself is visible; fog still hides it.
`Scenes/Menus/GameUI/HeroBar/`, drawn by `FloatingHealthBars`.

## 3. Abilities

Each hero has one ability per tier, unlocked at the level on its SO, and the last at
**level 10** (five slots: the four tiers and The Call a Runai hero is granted). Below its level an ability is not castable and not shown.

**Everything below level 10 casts itself.** The player never presses these. They fire
from `HeroAutoCastSystem`, a simulation system every lockstep peer runs identically,
on a rule per effect. Their reach is limited to **the hero's own line of sight**: a
target or aim point outside it is refused (`AbilityTargetRules`), and the AI uses the
same system.

| Hero | Level 1 | Level 3 | Level 4-6 | Level 10: the game ender (manual) |
|---|---|---|---|---|
| Lexor | King's Call, Liquid Courage | | L4 Honour thy Pledge | **Call to the Banners:** a great temporary army of veterans around Lexor |
| Ledger | Automate Facility | Iron Frame | L6 Accounts Settled | **Foreclosure:** takes a large share of every hostile faction's stockpile |
| Archivist | Catalogued | Stamp of Record | L6 Recall | **Stop the Hours:** every hostile unit on the map is frozen; enemy heroes' abilities go on cooldown |
| Surgeon | Cauterize | Bloodletting | L6 Lancet | **The Great Rewind:** your whole army healed and the recently fallen return |
| Warden | Drillmaster | Sally Forth | L6 Challenge | **The Realm Holds:** hostile units on your ground lose most of their HP; your buildings cannot be hurt for a time |
| Guildmaster | Called In | Stake | L6 Liquidate | **Buy Out:** enemy buildings in a large aimed circle fall (capitals lose a share) and you are paid part of their worth |
| Harkon | Warcry | Leap | L5 Raze | **Blood Oath:** your whole army cannot die and frenzies for a time |
| Wayfinder | Wayfinding | Mirage | L5 Caravan of Swords | **Exodus:** your whole army crosses the map to an aimed point |

**A level-10 ability must be able to end the game.** It is cast by the player (or the
AI's own rule), with no line-of-sight limit, on a very long cooldown. Blood Rain's
silence and an enemy Stamp of Record still apply.

When they cast themselves:

- **Self-defence** (Liquid Courage, Iron Frame): hurt, with an enemy close.
- **Ally buffs** (Warcry, Sally Forth, Wayfinding, Bloodletting, Mirage, Caravan of
  Swords): enemies in sight.
- **Honour thy Pledge:** several enemies in sight.
- **Stamp of Record:** the nearest enemy with abilities. **Lancet:** the strongest enemy
  in sight, a hero first. **Challenge:** enemies inside its radius.
- **Recall:** badly hurt with enemies in sight. **Leap:** at an enemy in sight not yet
  in reach. **Raze:** the nearest enemy building in sight.
- **The Call** (a Wayfinder or other Runai hero): enemies in sight and escorts to call.
- **Automate Facility:** the nearest eligible own economy building in sight. The Ledger
  no longer roams; it goes where it is sent. **Stake:** standing at an own extractor with
  no enemy in sight, and only extractors qualify.
- **Liquidate:** an own building in sight about to fall (never the capital), which
  denies the attacker the kill and pays the refund.

**Anything that moves a unit lands it on walkable ground** (Recall, Exodus, Leap, the
Banners), or that unit stays put.

Two kits changed to make this safe. **Bloodletting** no longer cuts allies' HP: own
units in its radius attack faster and take more damage for its duration. A trigger
that drops your army to a fraction of its health is not one to fire by itself.
**Liquidate** fires only on a building about to fall, as above.

## 4. The Shardroot: only heroes, Ascended while they carry it

- **Only a hero can capture the Shardroot.** Any other unit walks over it; a hero within
  reach takes it at once.
- **Carrying it makes the hero Ascended**, at no price: it takes a fraction of every hit,
  loses at most a fixed share of max HP per second however many strike it, regenerates
  out of combat, cannot be thrown or disabled (slows still work), cleaves, and unleashes
  **Shardbound Fury** by itself when enemies crowd it. Everyone can see the bearer, the
  curse hunts it and every AI rival goes for it.
- **Killed while carrying it, the hero explodes:** everything nearby, friend or foe, is
  thrown and killed, buildings take heavy damage, the artifact drops, and the killer's
  faction is paid the bounty.
- **Its owner may choose instead:**
  - **Drop it safely** on the ground, with no explosion. The power leaves the hero and the
    artifact is up for grabs. The hero that dropped it cannot retake it until it has
    stepped out of reach. Action button or lockstep command `DropShardroot`.
  - **Stow it in the Temple of Ridan** for the Temple victory, unchanged from
    [Curse_And_Shardroot.md](Curse_And_Shardroot.md) §3.1c: paid on delivery, the army is
    empowered, the 10-minute countdown runs, and the Temple falling drops it again.
- **There is no Hall road:** no binding price, no delivery at the Fortress, no placeholder
  champion.

## 5. Death

**A hero's level is never reset.** A fallen hero is trained again at its building and comes
back with the same level and every point of XP, for every hero. The price is money and
time: cost and training time grow with the level it fell at
(`HeroProgressionConfig.RevivalMultiplier`). Sect heroes pay no RP again. Rally the Oath
and the revival choice are gone. Per faction and hero, in `FactionHeroRecords` (a buffer on
the faction bank).

## 6. Open items

- All hero and sect-unit numbers are first-pass values from the 1v1 check, not played.
- A level-10 ability's strength and cooldown need playing in real matches.
- The eight sects outside the Alanthor cluster still have no authored hero.
