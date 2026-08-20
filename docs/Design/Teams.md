# Teams

**Status:** canonical. Teams are a lobby-level setting, not a game mode.

Before this, hostility was "any faction that is not mine". Teams introduce the
first relationship layer the game has ever had: a faction/faction verdict that
is either **allied** or **hostile**.

---

## 1. Assignment

Each slot in the skirmish lobby — human or AI — is assigned either to a
**team** or to **no team**.

| Setting | Meaning |
|---|---|
| **No team** (default) | The slot fights alone. It is hostile to everyone, including other unteamed slots. This is the existing free-for-all, unchanged. |
| **Team 1..4** | The slot is allied with every other slot on the same team, and hostile to everyone else. |

"No team" is the default for every slot, so a lobby nobody touches behaves
exactly as it does today.

A team of one is the same thing as no team. Teams are not required to be
balanced, and a team may hold any number of slots.

**The curse is never anybody's ally.** `Faction.Border` is hostile to all
players and all teams, and no team setting changes that. It remains allied
with *itself* — the horde does not fight itself — but it can never join or be
joined to a team.

---

## 2. What allies share

### Line of sight — shared

Allies see through each other's eyes. Anything visible to any member of a team
is visible to every member: fog of war, the minimap, and the
visible/revealed/ghosted state of enemy units all resolve against the **team's**
combined vision rather than the single faction's.

This is vision only. It does not share control, resources, population, research,
influence or buildings.

### Attacks — impossible between allies

Allied units and buildings **cannot** damage each other, by any route:

- they are never auto-acquired as targets
- an explicit attack order on an ally is rejected
- area-of-effect damage, projectile splash and damage-over-time ground skip
  allied entities exactly as they skip the caster's own
- AI target selection never picks an ally

There is no friendly-fire toggle. Allied damage is not reduced — it does not
happen.

### Heals and buffs — allowed, but they do not stack

A healer or buff source may target an ally, and allied auras reach allied
units. Applying the **same** effect twice does **not** stack: the stronger
application wins and the duration refreshes, exactly as two same-faction
sources already merge today.

This is the existing merge rule (`CombatDamageHelper.MergeSpellBuff` and the
aura loop's per-field maximum) extended across the team rather than a new
system. Two allies running the same aura therefore produce one aura's worth of
benefit, not two.

Different effects still combine normally — an armour buff and a speed buff
from two different allies both apply. It is only same-effect double-dipping
that is prevented.

---

## 3. What allies do NOT share

Stated explicitly, because each is a plausible reading of "team":

- resources, supplies, veilstone, iron, research points
- population cap
- research and tech unlocks
- unit control and selection
- buildings, production queues and rally points
- sect adoptions and god powers
- territory / influence
- walls and gates: a gate opens for its **team**, since a wall that stops your
  ally is worse than no wall, but the wall still belongs to its owner

---

## 4. Victory

The match ends when exactly one **team** remains, counting unteamed factions
as teams of one. Eliminating an ally's last building does not advance you
toward victory on your own.

Well-domination victory (see [Curse_And_Shardroot.md](Curse_And_Shardroot.md))
remains a **per-faction** condition: a single faction must hold all N wells at
once. Teams do not pool well control — allies help you get there, they do not
win it for you.

---

## 5. Implementation contract

One helper answers every hostility question in the codebase:

```
Alliances.AreHostile(a, b)   // the ONLY correct hostility test
Alliances.AreAllied(a, b)    // includes self
```

Rules encoded there, once:

- a faction is always allied with itself — checked FIRST, so the curse does
  not come out hostile to the curse
- `Faction.Border` is hostile to every *other* faction, and every other
  faction is hostile to it
- two factions on the same non-zero team are allied
- everything else is hostile

Team assignment is carried on `PlayerSlot.TeamIndex` (0 = no team) and copied
into a flat faction→team table at match start, so Burst systems can read it
without touching managed lobby state.

Raw `factionA == factionB` comparisons are no longer a valid hostility test
anywhere in the codebase.
