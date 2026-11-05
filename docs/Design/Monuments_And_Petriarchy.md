# Monuments, Ruins and the Petriarchy

> **Doc version: 2026-11-05. Canon for monuments, exploring ruins, the
> Shardroot's descent, sect-unit religion income, Petriarchy support and the
> Petriarchy's Army.** Supersedes the religion-point descent counter in
> [Curse_And_Shardroot.md](Curse_And_Shardroot.md) §3.1c and the Temple's RP
> price and first-RP framing in [Religion.md](Religion.md) §1-§2. Numbers live
> in `MonumentSystem.asset` and `FactionReligionPoints.asset`; this doc states
> rules only.

Developer's reason: a match with little curse had almost no access to religion
points beyond Sanctums and the Temple's trickle. Religion now has a contested
map objective, and the game-winning artifact comes from a choice players pay
for instead of a counter that fills by itself.

## 1. Monuments

- **One monument stands in each starting curse territory** (a Veilstone rich
  territory; on a map with none, each territory the initial curse nodes rose
  in). Monuments follow the map's curse deal, so they are mirrored wherever it is.
- A monument is **not a building**: no health, cannot be attacked or destroyed,
  does not hold a territory, does not count for elimination, score or Sanctum
  dominion.
- **A visible ring marks it. Any unit inside the ring for `captureSeconds` takes
  it**, regardless of curse presence: a worker or scout counts, and curse units
  neither block it nor take it. An arc fills along the ring as it is taken; ring
  and light wear the holder's colour.
- The timer needs one side in the ring, allies together. **Hostile sides inside at
  the same time freeze it**, so a holder with a unit inside keeps the monument. Leaving
  the ring resets it. Nobody holds a monument at match start.
- **A held monument trickles religion points** to its holder through the same
  escalating points ladder as curse kills and the Temple. It works from Age 0:
  it does not depend on territory claims, which open at age-up.

## 2. Exploring the ruins

- The holder of a monument may pay to **explore its ruins, once per monument per
  match**, whoever explores it. The price is on `MonumentSystem.asset`.
- **Exploring grants one random buff** from a pool of six, permanent and
  faction-wide. A faction never rolls a buff it already has. The six are tech
  SOs in `TechTree/Monument/Buffs/`, granted rather than researched, and listed
  on `MonumentSystem.asset`.
- The roll comes from the match seed, so every peer and replay agrees.

## 3. The Shardroot's descent

- **One monument, chosen at random from the match seed when monuments are
  placed, hides the Shardroot.** Nobody can see which.
- **Exploring that monument makes the Shardroot descend in front of it.** It is
  random but guaranteed: each exploration may be the one, and the last ruin
  explored always is if none before it was.
- **One Shardroot per match.** Religion points no longer count toward it, and
  there is no time or random-spawn fallback.
- From there the artifact behaves as before: it can be carried, handed to the
  king or enshrined (Curse_And_Shardroot.md §3).

## 4. Temple and religion income

- **The Temple costs no religion points**, only its resource price.
- **Sect units earn religion points in proportion to weapon damage dealt**:
  one RP per `sectDamagePerRp` damage, kept per faction, paid straight to the
  balance, not through the ladder. Sect units and sect heroes both count.
  Sect powers do not.

## 5. Petriarchy support (the Temple)

A standing Temple has **one button per resource**: supplies, iron, veilstone,
veilsteel. Each spends one RP and pays that resource's amount from
`FactionReligionPoints.asset`, with no cooldown. In the fiction the player asks
the Petriarchy to support their cause.

## 6. The Petriarchy's Army (the Temple)

A standing Temple sells **three armies, for 5, 10 or 20 RP** (prices and unit
counts in `FactionReligionPoints.asset`). The units appear around the Temple,
drawn evenly from **every sect's unit available in the edition, adopted or not**.
They are permanent, ignore the sect-unit train limit and may take a faction past
its population cap. Heroes are not included.

## 7. AI

The AI runs a squad into the ring of the nearest monument (a plain move, not an attack-move, so it does not stop to fight on the way), explores what it
holds as soon as it can pay, and after its sect purchases spends remaining RP on
Petriarchy support when a resource is short, otherwise on the largest army it can
afford. Values are on `SimpleAISystem.asset`. Code: `SimpleAISystem.Monuments.cs`
and `AIEndgameCommon`.

## 8. Code

`TechTree/Monument/` (`Monument`, `MonumentSystem`, the visual, the buffs);
`Sects/ReligionPurchases.cs` (every purchase, explore included, one lockstep
command); `Sects/PetriarchyArmy.cs`; `Sects/SectDamageReligion.cs`.

## 9. Watch in playtest

- Whether armies paying for themselves through sect damage makes a runaway.
- How early the first exploration lands, since it can end the match by ascension
  about ten minutes after that.
- The art is an interim Synty statue, with a light in the holder's colour.
