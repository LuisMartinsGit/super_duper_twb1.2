# Unit Stances — Design

> Canonical for how a unit decides, **on its own**, whether to fight, how far
> it may chase, and when it walks home. Anything a player orders directly
> (an attack, a move, an attack-move) is not a stance question and is covered
> in [§5](#5-orders-outrank-stances).
>
> Implementation: `Scripts/Systems/Combat/Stance/UnitStanceSystem.cs` (defaults,
> settings singleton), `Scripts/Systems/Combat/Targeting/TargetingSystem*.cs`
> (acquire rules `RulesFor`, leash, return, and `MayChase` / `IsFixedMount`
> which the melee and ranged chase branches ask),
> `Core/Commands/CommandTypes/StanceCommand.cs` (the order). Tunables:
> `Scripts/Systems/Combat/Stance/UnitStanceSystem.asset`.

## 1. The three stances

> **Revised 2026-09-26 (user decision).** Stances now govern ONLY what a
> unit does on its own. Any order — attack, move, attack-move, patrol —
> is carried out on every stance, Hold included (§5). The old Defensive
> "reach + 2 m / assist a friend within 8 m / 8 m chase" and the old
> "Hold still shoots what is in reach" rules are gone.

A stance is a **mode**, not an order. It survives every order the unit is
given — move, attack, attack-move, patrol, **Stop** — and only changes when
the player picks another stance.

| Stance | Auto-engages | Returns fire | Pursues (moves on its own to fight) |
|---|---|---|---|
| **Aggressive** *(default for everyone, 2026-09-29)* | any hostile inside its **line of sight** | yes | **yes** — up to the **30 m** leash from its guard point, then walks home |
| **Defensive** | only an enemy **attacking it** that is inside its **attack reach** | yes | **never** |
| **Hold Position** | nothing | **no** | **never** |

- *Attacking it* means: the enemy **hit it within the last 5 s**
  (`retaliationWindow`; it is the unit's `LastAttackerEntity`), **or** the
  enemy's own target is this unit (its shot may still be in the air).
  Defensive does **not** engage an enemy that is merely in range — "return
  fire" means fire aimed at *it*. (Decision 2026-09-26: attackers only.)
  The one exception is the **assist rule** of [§1c](#1c-standing-by-a-struck-neighbour)
  (2026-10-25): an idle Defensive unit standing right next to a friend that
  is being hit goes for that friend's attacker.
- *Attack reach* is the unit's own: melee `1.5 m` to the target's surface,
  ranged the unit's `MaxRange` (ArcherState). A Defensive unit whose
  attacker steps out of reach lets it go; it never follows. (A ranged unit
  with a minimum range still backs off from a target inside it — that is
  kiting its own target, not pursuit.)
- *Line of sight* is the unit's own `LineOfSight.Radius`; there is no
  acquire bonus beyond it.
- **Hold is completely passive** unless ordered: it does not return fire,
  does not move, is never formed up by idle form-up, and never walks home.
  Setting Hold also stops the unit and makes where it stands its guard
  point. Hold is the pre-existing **H** key; `HoldPositionTag` is its
  marker.
- The UI keeps the name **Defensive** (the user's working name was
  "Normal"): it matches the **D** hotkey and the stance id, and "returns
  fire, never pursues" is what defensive means.

### 1a. Fixed mounts (independent of stance)

A **fixed mount** auto-fires at any hostile inside its own attack reach,
whatever its stance, and **never moves for any target — ordered or not**:

- every **emplaced engine** (`EmplacedEngineTag` — Ballista / Trebuchet
  emplacements), which is also always on Hold;
- anything carrying `StationaryAutoFire` — the scenarios that used Hold to
  mean "stand here and shoot" (the showcase's immortal spearman, the
  Longbowman line battle, the arrow-trail lanes) now add it explicitly.

### 1b. Support units

`UnitClass.Support` units (Litharch, Lorekeeper, Inquisitor) **never
auto-acquire**. If armed (the Litharch only after **Warrior Priests**) they
return fire like a Defensive unit — attackers only, inside reach, never
pursuing — on Aggressive, Defensive **and** on attack-move/patrol; on Hold
they do nothing. Zero-damage units (the Litharch before Warrior Priests,
the Holy Scholar, scouts, workers) never auto-engage at all. `Magic` class
is **not** covered by this rule — it holds offensive casters (Chaincaster,
Glassmark Arcanist, Golem Autark) that fight like any other unit.

The Litharch's own movement (healing) is its job, not a fight, and follows
[Age_0.md § Litharch](Age_0.md#litharch): it walks to a stand-off point
inside heal range (never onto the patient), prefers patients not in melee
contact, and steps away from an armed enemy that comes within 5 m. On Hold
it heals only what is already in range and never steps away; an ordered
heal is followed on every stance; a move order always wins.

### 1c. Standing by a struck neighbour

> **Added 2026-10-25 (playtest request).** A soldier who sees the man beside
> him cut down does not stand and watch.

When a unit is **attacked** — it lost health within the last `assistWindow`
and something is recorded as its attacker (`LastAttackerEntity`, the last
thing that damaged it) — every **idle** soldier of the **same faction** within
`assistRadius` of it goes for that attacker, then walks back to its own guard
point (§2) when the attacker dies or the leash (§3) breaks.

- **Who assists:** Aggressive and **Defensive** units. **Hold never does**
  (Hold is completely passive, §1). Support units (§1b), fixed mounts (§1a),
  men on a wall-walk, scouts, workers, economy and zero-damage units never do
  — they stay where they are. The curse's units never do (their wave logic
  drives them, §4).
- **Idle** means exactly what auto-acquisition already requires: no target,
  no order (attack, move, queued move, a live destination), not on an
  attack-move or patrol (those already fight by the Aggressive rules), and
  not inside the post-leash re-acquire cooldown. A unit **already fighting
  keeps its fight**, and **a player's move order is never interrupted**.
- **Same faction only** — an ally's units are its own player's to command and
  do not join in (decision 2026-10-25; allies still share sight,
  [Teams.md](Teams.md)).
- **The attacker must be one this unit could pick on its own:** alive, hostile
  to it (`Alliances.AreHostile` / the Friendly Fire rogue bucket), not veiled,
  not hidden by stealth, a wall piece only for siege, a flyer only for units
  that hit the air, a building only for a buildings-only ram — and within the
  Aggressive leash of the unit's guard point (otherwise the chase would break
  at once).
- **The engagement is an ordinary auto engagement** with the Aggressive leash
  anchored at the guard point — for a Defensive unit too, which otherwise
  never pursues. So the leash, the walk home and the re-acquire cooldown are
  exactly §3's. The assist **outranks the unit's own stance pick** that tick.
- If several struck friends are in reach, the **nearest** one's attacker wins
  (ties in the peer-stable network-id order of the targeting grid), so the
  choice is the same on every lockstep peer.

Implementation: the assist search runs inside TargetingSystem's acquire plan
(`TargetingSystem.Assist.cs`), on the unit's acquisition stagger tick, over the
same (cell, faction) grid as the enemy scan — its own faction's buckets.

## 2. The guard point

The guard point is where the unit was last told to be:

- where it spawned / rallied;
- where a **plain move arrived** (a unit that finishes a move takes up its
  stance **there** — Aggressive by default, so it engages what comes into
  sight but is leashed back to this point);
- where it stood when set to **Hold**;
- the destination of an **attack-move** or the current patrol waypoint;
- the **target of an explicit attack order** (2026-10-22), and, once that
  target dies, **wherever the unit then stands** — so an army ordered onto
  one enemy holds the field and takes on the rest of the enemy army inside
  its leash. It used to keep the destination of the move it was on before
  the order, kill the one target and walk off to it, ignoring everything
  else. An attack order also takes the unit out of its formation march and
  drops the standing move: a new order, nothing of the old one left.
## 3. The chase leash

Only an **automatically acquired** target that the unit is allowed to
**pursue** is leashed — i.e. Aggressive, attack-move and patrol
engagements. Defensive, Hold and support engagements carry a leash of 0:
the unit never moves for them, and the melee / ranged systems drop the
target the moment it leaves reach (`TargetingSystem.MayChase`). The
engagement remembers where it started (the *anchor*: the guard point for
an idle unit, the unit's own position for an attack-moving or patrolling
one).

- When the unit gets farther than its leash from the anchor, it **drops the
  target and walks back** (idle) or **resumes its march** (attack-move /
  patrol).
- After breaking a leash it will not auto-acquire again for **1.5 s**, so a
  fleeing target cannot tow it back out on the next tick.
- An idle Aggressive unit only auto-acquires while it is **inside its own
  leash** of its guard point, so a unit walking home from a broken leash
  does not turn round and chase again.
- Attack-move and patrol always use the **Aggressive** rules (the order is
  "fight your way there"), regardless of the unit's stance — Hold included.
  Support units are the exception (§1b): they only return fire.
- A unit walking home still returns fire by its stance rules.

Before the stances, melee and ranged chases were unbounded: one scout could
tow a defending army into the enemy base. That is the bug the leash closes.

## 4. Who gets which default

- **Every unit starts Aggressive** — human players' and AI factions' alike
  (2026-09-29; human units used to start Defensive). Territory is now held by
  standing on it ([Territory_Claims.md](Territory_Claims.md)), so the default
  army must fight what walks into its ground, not only return fire.
- **Curse (Border) units** are driven by the curse's own wave logic and are
  **not leashed**. A curse unit on **garrison** duty stands **Defensive**
  (2026-10-05, Territory_Claims.md §6.7): its guard order does the
  engaging, so it no longer shoots player buildings it merely sees from its
  post. Drafted curse units (parties, attack waves) stand Aggressive.
- **Buildings and towers** have no stance.
- Scouts, workers and zero-damage units never auto-engage whatever their
  stance (unchanged); support units only return fire (§1b).
- AI armies move by attack-move, which fights by the Aggressive rules
  whatever the stance, so the AI is not slowed by the Defensive/Hold rework.

## 5. Orders outrank stances

**Player orders always win.** Stance only governs autonomous behaviour.

- An **attack order** (player or AI) moves the unit to engage the target
  on **every** stance — Hold included — and is never leashed, never dropped
  by stance logic, and never dropped by the movement stack's stuck
  recovery: a unit that cannot make progress toward an ordered target keeps
  the order and keeps trying rather than switching to whatever is nearest.
  The only exception is a fixed mount (§1a), which physically cannot move:
  an ordered target out of its reach is dropped.
- After an ordered fight a Hold unit stays where the fight left it (Hold
  never walks home); other stances walk back to their guard point.
- A **plain move** does not auto-engage en route (unchanged). On arrival the
  stance takes over.
- **Stop** clears orders, not the stance.
- **Attack-move in formation** holds rank until **20 m** from its
  destination, then fights; a member **hit within the last 2.5 s** fights
  back at once (it used to key on "the last attacker is still alive", which
  meant a unit grazed once by an archer that then lived on was treated as
  under fire forever).
- A **Purify** order makes the well the Holy Scholar's post: its guard point
  moves to the approach stand point, then to the spot the channel starts
  on, so it is not walked home when the rite ends.

## 6. Controls

| Key | Stance |
|---|---|
| **G** | Aggressive |
| **D** | Defensive |
| **H** | Hold Position |

The unit actions panel shows the three stances on its second row. The
stance the whole selection shares is lit gold; with a mixed selection every
stance present is lit dimmer and the tooltip says how many units hold it.

## 7. Multiplayer

Setting a stance is a replicated lockstep order (`SetStance`, one per unit,
the stance byte in `TargetEntityId`). The legacy `HoldPosition` opcode still
executes and maps to the Hold stance. Acquisition is staggered over four
ticks by network id, so it is identical on every peer.

## 8. Tunables

All in `UnitStanceSystem.asset` (`UnitStanceSystemConfig`):
`aggressiveLeash` (30 m), `retaliationWindow` (5 s — how long a hit counts
as "attacking me" for return fire), `leashReacquireCooldown` (1.5 s),
`underFireWindow` (2.5 s — formation attack-move retaliation),
`assistRadius` (how close an idle friend must stand to a struck unit to join
in, §1c) and `assistWindow` (how long after a hit the struck unit still counts
as attacked, §1c — longer than the acquisition stagger, or a neighbour could
miss it).
The Defensive-only `defensiveLeash`, `defensiveAcquireMargin` and
`defensiveAssistRadius` were removed with the 2026-09-26 rework.
Litharch positioning: `LitharchHealingSystem.asset`.
