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
- **A claim circle marks it, a few metres across (`captureRadius`). Any unit inside
  for `captureSeconds` takes it**, regardless of curse presence: a worker or scout
  counts, and curse units neither block it nor take it.
- **The circle is a ground decal, like the selection decals.** Its outer edge is
  always visible, but **fog hides it**: a player sees the circle only with vision of
  it now, or on a monument it owns. Inside, alpha runs linearly from fully transparent
  at the centre to fully visible at the edge, in the owner's colour. A capture fills
  in from the edge toward the centre in the capturer's colour, and anyone with vision
  of it can watch.
- **Monuments are selectable by everyone.** The panel shows the owner (`Derelict` until
  someone claims it), and the owner gets the Explore button.
- The timer needs one side in the ring, allies together. **Hostile sides inside at
  the same time freeze it**, so a holder with a unit inside keeps the monument. Leaving
  the ring resets it. Nobody holds a monument at match start.
- **A held monument trickles religion points** to its holder through the same
  escalating points ladder as curse kills and the Temple. It works from Age 0:
  it does not depend on territory claims, which open at age-up.

## 2. Exploring the ruins

- The holder of a monument may pay to **explore its ruins, once per player per
  monument (2026-11-05)**: taking a monument someone else explored lets you explore it
  too, and you get its buff as well. The price is on `MonumentSystem.asset`.
- **Every monument has its own buff.** The pool of six is shuffled from the match
  seed and dealt out, one per monument, so no two match unless a map has more than six.
  Which is which stays hidden until explored. **Exploring activates it for the explorer,
  for good, and from then on anyone can read it** by selecting the monument or hovering
  over it in the world (out of sight, hovering says only that it is a monument). The six
  are tech SOs in `TechTree/Monument/Buffs/`, granted rather than researched, and listed
  on `MonumentSystem.asset`.
- The deal comes from the match seed, so every peer and replay agrees.

## 3. The hidden Shardroot (2026-11-05)

- **The Shardroot lies on the map from the start, invisible**, at a random walkable
  spot from the match seed: reachable on foot, and at least
  `shardrootStartClearance` from every starting capital.
- **One monument, chosen at random from the match seed, knows where.** Nobody can
  see which. **Every player who explores that monument learns the spot**, on the map
  and in the world, and no one else does; allies are not told. So a monument that
  changes hands can tell several players.
- **It is visible to all only once it is picked up**, and it stays public from then
  on, also when a bearer drops it. Until then nobody sees it, in vision or on the
  minimap, and its fog lifts only for those who know the spot. Spectators with no faction selected see it.
- **Any unit that walks onto it takes it, whether or not its owner knew.**
- **A unit that passes without taking it sends it elsewhere (2026-11-05).** When every
  unit of a side, the curse included, has left the `shardrootPassRadius` circle around
  the hidden artifact without picking it up, it moves to a new random walkable spot,
  clear of every unit and capital. Stumbling on it is much rarer, and it removes itself
  from common travel lanes. Whoever knew the spot still knows it, and is told it moved.
  Once picked up, the artifact is public and this rule ends.
- **The curse does not know either:** it guards the artifact only when a bearer has
  dropped it, and hunts the holder as before.
- **One Shardroot per match.** Religion points no longer count toward it, and there
  is no time fallback. From the pickup on it behaves as before: it can be carried,
  handed to the king or enshrined (Curse_And_Shardroot.md §3).

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
holds as soon as it can pay. It knows the Shardroot's spot only as a player would, by
exploring the right monument, and then strikes for it (Game_AI.md § 6l). After its sect purchases it spends remaining RP on
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
- How often an army stumbles on the hidden Shardroot by accident, and whether a first
  pickup with no curse guard comes too cheaply to whoever was told.
- The art is an interim Synty statue, with a light in the holder's colour.
