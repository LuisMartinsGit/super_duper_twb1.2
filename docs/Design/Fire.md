# Fire

**Status:** canonical for how fire starts, spreads, burns and dies out, and for
what it does to blood.

Fire is a **world** mechanic, not a Feraldis one — anything that ignites ground
uses these rules (the Firethrower, the Ash sect's pyres, Wrathfire, a burning
building). Feraldis simply lives closer to it than anyone else, which is why
"fire and blood" is their identity: the two halves of this document are the two
halves of their culture.

---

## 1. The ground burns, not the air

Fire is a property of **ground cells**, not of units. A cell is in exactly one
of four states:

| State | Meaning | Flammable? |
|---|---|---|
| **Fuel** | grass, forest, trees — the map as authored | yes |
| **Burning** | actively on fire, hurting what stands in it | — |
| **Ash** | burnt out, black and bare | **no** |
| **Bare** | rock, road, sand, curse crust, water | no |

Only **Fuel** can catch. That single rule is what makes fire readable: a player
can look at the ground and know where it can go.

### What counts as Fuel

- **Grassy ground** — terrain painted with a grass layer.
- **Forest and trees** — any cell carrying a tree.

Everything else is **Bare** and stops fire dead. Bare ground is therefore the
map's natural firebreak: roads, rock shelves, rivers and the curse's own crust
will not carry a flame.

Fuel is **baked once per match**, when the terrain is ready, into a grid that
mirrors the veil field's shape (see
[Curse_And_Shardroot.md](Curse_And_Shardroot.md)). Terrain does not repaint
itself mid-match, so the fuel map never needs rebuilding — only the burn state
on top of it changes.

---

## 2. Spread

Fire spreads **slowly**, cell to cell, to the four orthogonal neighbours only —
never diagonally, so a fire front reads as a growing blob rather than a
star. Each burning cell tries to light its neighbours; a neighbour catches only
if it is Fuel.

Slowness is the whole character of the mechanic. A fire is something you can
walk away from, reposition around, and use as terrain — not a wipe. A player
who ignites a forest should have time to regret it.

Spread is **deterministic**: it advances on simulation pulses, in cell-index
order, with no wall clock and no `UnityEngine.Random`. Two lockstep peers burn
identically (see the rules in
[Multiplayer_Desync_Sweep_2026-08-16.md](../Multiplayer_Desync_Sweep_2026-08-16.md)).

---

## 3. Standing in fire

A unit or building standing on a **Burning** cell takes **damage over time**,
for as long as it stands there. Nothing is immune by default.

The damage is a burn: it does not care about armour type or counter tags. Fire
is the one thing on this map that treats a Spearman and a Siege Yard the same
way.

---

## 4. Blood catches all at once

Blood pools (the Feraldis mechanic — pools left by deaths, drunk by War Totems)
are **soaked ground**, and they behave nothing like grass.

> **When fire reaches ANY blood tile, EVERY blood tile on the map ignites
> immediately** — not by spreading to them, but at that instant, wherever they
> are.

This is the sharpest event in the system and it is meant to be: a Feraldis army
that has been feeding a battlefield with blood is standing in a bomb, and one
torch sets the whole thing off at once. Blood ignited this way **burns
longer** than ordinary fuel.

The chain fires **once per ignition event** — igniting blood does not re-trigger
itself from the fires it just created, or the map would flash forever.

### The effect

Blood ignition is not a fire spreading; it is an explosion. Each blood tile
that goes up produces a hard, immediate burst — a dark red detonation with a
fast expanding ring — distinct from the ordinary fire's continuous flicker, so
a player who sees it knows instantly that this was the blood going, not the
grass. See [§6](#6-effects).

---

## 5. Ash, and the map healing

When a burning cell has consumed its fuel it becomes **Ash**:

- Ash is **not flammable**. Fire cannot re-enter it, so a burnt area is a
  firebreak the fire made itself — a fire cannot burn the same ground twice
  and cannot loop back through where it came from. This is what guarantees
  every fire eventually dies.
- Ash is **cosmetic scarring only** — it does not slow movement or block
  building.

After a while, **ash reverts to the ground it came from** — grass returns to
grass, forest to forest — and becomes flammable again. The map heals. A match
long enough to burn a forest twice is possible, but only long after the first
fire is a memory.

The full cell lifecycle:

```
Fuel ──ignites──> Burning ──burns out──> Ash ──regrows──> Fuel
 ^                                                          |
 └──────────────────────────────────────────────────────────┘
```

Bare ground never enters this cycle at all.

---

## 6. Effects

Two visual languages, deliberately different, because they mean different
things:

**Ordinary fire** — continuous and alive. Flame plumes rising from the burning
cells with smoke above them, an orange ground glow that fades as the cell burns
down toward ash, and embers. It should read as *a region that is on fire*, not
as a collection of props: the front is what the player watches.

**Blood ignition** — instantaneous and violent. A dark red / black burst per
tile with a fast expanding shockwave ring, all of them at the same moment
across the map. Where ordinary fire says "this area is dangerous now", blood
ignition says "everything you spilled just came due".

Both obey the presentation rules in
[project performance contract]: procedural pieces go through
`ProceduralMaterialHelper`, realtime lights stay within budget, and the effect
count is capped — a hundred burning cells must not mean a hundred lights.

---

## 7. Interactions worth stating

- **The curse.** Veil crust is Bare: the curse does not burn, and fire will not
  clear it. Only the verbs in
  [Curse_And_Shardroot.md](Curse_And_Shardroot.md) do that.
- **Walls.** Wall segments are structures standing on ground; the ground under
  them can burn, and they take burn damage like anything else, but stone does
  not carry the fire onward.
- **Everburning** (Ash sect research) multiplies fire duration and damage; it
  applies to every fire in this document, including blood.
- **Buildings** take burn damage but never become Fuel — a burning building
  does not turn its cell into a permanent fire source.
