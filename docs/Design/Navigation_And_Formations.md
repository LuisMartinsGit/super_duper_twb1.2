# Navigation, Formations & Overpass Structures — Design

Canonical design for unit commands, group movement, formations, and
multi-level (over/under) navigation. Modeled 1:1 on Age of Empires IV's
shipped system — see the research study in
[docs/Research/AoE4_Navigation_Study.md](../Research/AoE4_Navigation_Study.md)
for sources. Where this doc and code disagree, this doc wins.

## 1. Movement model (AoE4 parity)

- **Pathing = flow fields + steering**, never per-unit waypoint A\*.
  Long-range direction comes from a goal flow field (label-correcting
  integration over the nav cost grid, LOS shortcut on open ground);
  the last meters and unit-vs-unit interaction are steering only.
- **Units are never written into the pathing grid.** Crowds are resolved by
  steering (separation, avoid-moving, avoid-immobile, obstacle look-ahead),
  not by re-pathing.
- **Best path**: flow-field integration is cost-optimal over the grid by
  construction (octile metric, terrain weights, wall-clearance penalty).
  LOS-to-goal short-circuit gives straight any-angle movement on open ground.
- Determinism (lockstep multiplayer) is a hard constraint: integer math in
  fields, locked force order in steering, fixed iteration orders.

## 2. Group movement (AoE4 virtual-leader model)

When N ≥ 2 movable units receive one move / attack-move order:

1. A **formation group** is created with a **virtual leader** that paths to
   the clicked destination, starting from the army's current pose (see 7).
2. **Formation spots** are laid out around the leader (see §3) and assigned
   to units by type-rank first, then nearest-slot to minimize crossing.
3. Each unit steers to its moving spot. **If a unit has no line of sight to
   its spot**, it falls back to following the flow field toward the
   destination until the spot is visible again.
4. **Cohesion gate**: only units within the cohesion range
   (**12 m ≈ "a few tiles"**) of the group centroid at order time travel in
   formation; outliers path independently to the destination.
5. **Group speed = slowest member's speed** (members inside the cohesion
   range only).
6. **Members track their spot's VELOCITY, not its position.** A member is
   commanded its spot's own velocity — the leader's step plus the tick's
   rotation applied to the arm out to that spot — plus a proportional pull
   toward it, capped at **+40%** of the group speed.
   - Aiming a member AT its spot is pure pursuit, and pure pursuit only
     converges while the target moves the way the pursuer does. That holds
     for the body of a formation on a straight line and fails everywhere
     else: a flank unit in a wheel has a spot moving almost entirely
     sideways, and chasing it **orbits** — measured at 2.1 complete circles
     per 45° corner for the inner wing.
   - The velocity term is what makes a wheel work at all: the outer flank is
     told to ride forward and the inner flank to give ground, which is the
     difference between ranks wheeling and ranks scattering.
   - This subsumes what used to be four separate speed tiers, a lateral
     correction gain and a don't-steer-backwards rule. Behind, the pull
     points forward and adds speed; ahead, it points back and removes it;
     abeam, it angles the heading. Each was an approximation of this law
     with its own failure mode.
7. Formation **facing = direction of travel**, reached by TURNING, not by
   snapping. No manual facing control.
   - A new order **starts the group in the pose the army is already in**,
     recovered by fitting the remembered slot lattice to the units' live
     positions. Snapping facing to the new bearing teleports every spot, so
     a corner became a scramble rather than a turn. A body of units that is
     not actually in the formation (RMS fit > 2 m) still snaps — for a first
     order, facing the destination is correct.
   - The leader then **wheels**: it travels along its OWN facing and rotates
     toward the flow direction at a bounded rate. Travelling along the flow
     while the facing lags is a *crab* — the lattice points one way, the
     group slides another, and the spots drag sideways through the units
     standing in them.
   - **Turn rate = (catch-up headroom) ÷ (formation radius)**, capped at 1.2
     rad/s, with a 0.05 rad/s anti-deadlock floor only. The outermost slot is
     dragged `ω × radius` sideways and a member has only 40% of its speed
     spare, so this is a hard physical budget: **the floor must never be big
     enough to override it.** At 0.25 it was — a siege train makes the army
     15 m deep *and* drops the group speed to the catapult's, so the floor
     demanded three times the sideways speed the rearmost engine had.
   - Deep, slow armies therefore wheel slowly (a 45° corner: ~2.5 s for
     infantry, ~10 s with siege). That is not a bug to tune away. The leader
     keeps its forward speed through the turn, so the formation sweeps a wide
     arc rather than stalling — shorten the army if a tighter turn is wanted.
   - Forward speed eases by **cos(heading error)**, floored at **50%**, so a
     corner costs tempo without stopping the army dead.
   - Past **~100°** the formation does not wheel, it **re-forms** on the new
     bearing. An about-face at flank-limited rate is ten seconds of pivoting
     on the spot, which is the kiting failure this system exists to prevent.
   - **A blocked leader does not fake arrival.** When the cell ahead along
     the facing is blocked (a corner the slow wheel has not come round yet):
     if the heading error exceeds `stallHeadingToleranceRadians` (0.35 rad),
     the leader **pivots in place** at `catch-up speed ÷ radius` — with no
     forward motion a member's whole catch-up speed is free for the sideways
     drag, not just the 40% headroom — and that is not a stall. Otherwise it
     **steps along the flow direction**, which the goal field routes round
     the blocker. Only when that is blocked too does a stall tick count, and
     after `StallReleaseTicks` (120) the group is **released**: every member
     detaches and walks on to its own final slot on its own flow. It used to
     flip to Arrived, snapping every spot to its final slot mid-route and
     dissolving the group four seconds later far from the destination.
8. **Slots are remembered across orders** (`FormationSlotMemory`, by index,
   guarded by a layout key). Without this, each order re-derives the
   assignment from positions measured along the NEW travel axis, so every
   turn reshuffles who stands where and the army trades places instead of
   turning. The memory outlives group dissolution — which happens on every
   arrival — and is cleared only when a unit genuinely leaves formation
   (plain move, attack-move).
9. **Arrival**: the leader stops at the destination, spots freeze, units
   settle into spots and hold. The group does **not** dissolve the moment
   the leader arrives — rear ranks are still walking then. Each member keeps
   its formation state (and the same-formation push exemption) until it
   settles within stop distance of its own slot, and is detached then; a
   settle timeout (`FormationGroupSystem.asset`, 4 s) releases whoever is
   left. Formation members are exempt from the crowd-arrival rule and from
   StuckRedirect — the group has its own stall and tether handling — and the
   integrator's hard-stuck give-up never cancels a member's order (that read
   as "settled" and detached it mid-march); it only resets the stuck counter.
   - **The push exemption is keyed on the formation ORDER**
     (`FormationSlotMemory.GroupKey`), not only the live group. A settled
     member has detached, so keyed on the live group alone the front rank
     shoved the rear ranks walking in behind it. Units sharing a key do not
     separate/avoid each other unless one of them is chasing a target (a
     fight still separates). The key is cleared by any order that takes a
     unit out of formation.
   - A member released by the settle timeout keeps the formation's exemption
     from the steering arrival fade, and the crowd-arrival rule ignores
     formation-mates as blockers (they cannot push it, so they cannot stop it
     reaching its slot); a foreign unit on its slot still settles it.
10. **Combat pulls a member out of rank, not out of the group**: a member
    that auto-acquires a target fights individually at its own speed, but
    stays on the group's roster; when its target dies or is dropped (leash)
    while the group still exists, it walks back to its slot and rejoins. An
    **explicit** attack order on a member detaches it for good. Re-issuing a
    move order re-forms.
    - While travelling on an attack-move, members hold rank until 20 m from
      the destination unless hit within the last **2.5 s** (see
      [Stances.md §5](Stances.md#5-orders-outrank-stances)).
    - **Group speed** is the slowest member's *effective* speed — base speed
      times its debuffs/buffs (SpellDebuff, BorderDebuff, SpellBuff) — and
      each member's speed override is pre-divided by its own multiplier, so a
      slowed member does not silently lag the formation it is in.
    - **The leader holds for a battle.** While strictly more than
      `engagedHoldFraction` (0.34) of the roster is engaged, the leader
      stops, and the tether fuse and stall counter pause. After arrival the
      settle timer is **frozen while any member is engaged**, so the group
      still exists when its fighters come back for their slots. A skirmisher
      or two peeling off does not stop the army.
    - The **tether fuse** (drop the one member that cannot keep up) fires
      while the leader is easing for a straggler and the worst member
      **offset** (distance to its spot, in any direction — the same number
      that triggers the ease) fails to improve by `tetherProgressEpsilon`
      (0.1 m) for `TetherReleaseTicks` (120). The victim is the member
      holding that offset, and only while it is outside the ease-engage
      threshold. It used to measure behind-ness for progress and victim while
      the ease used distance, so a member wedged sideways kept the ease on
      and the fuse ejected a well-placed member with a tiny lag instead.
11. **Villagers / worker units never form up** — they path independently
    (matches AoE4).
12. **Multiplayer**: a formation order is ONE replicated lockstep command
    (`FormationOrder`: unit network ids sorted ascending and delta-encoded
    in base 36, destination, shape, attack-move flag; split into
    continuation commands above 60 units so no datagram fragments). Every
    peer builds the same `FormationGroup` from identical state; ties in the
    slot assignment break on network id, never on entity index. Formation
    orders used to degrade to per-unit slot moves under lockstep, so
    multiplayer armies arrived in shape but never held it en route.
13. **Idle form-up** (`IdleFormUpSystem`, every faction including humans)
    re-forms an idle cluster in the **shape it last marched in**
    (remembered in `FormationSlotMemory`), treats the members of one
    just-arrived formation as that formation — never as "new faces", never
    split between two clusters — and leaves a settled formation alone.

## 3. Formation set (AoE4 parity)

Formation choice persists per selection; changing it re-slots immediately,
even standing still. All formations are built perpendicular to travel
direction. Base spacing: **2.0 m between unit centers** (existing value).

| Formation | Shape |
|---|---|
| **Default (Box)** | Rectangle ~2:1 width:depth in unit counts |
| **Line** | Wide, 1–2 ranks deep |
| **Wedge** | Pyramid: 1 at head, each rank +2 |
| **Staggered** | Same counts as Box, **2× spacing**, alternate ranks offset half a step (no unit directly behind another) |

**Type layering (front rank → back rank):**

| Rank | Who | Notes |
|---|---|---|
| 0 | **Heroes** | `UniqueUnitTag`. Tested before cavalry, so a mounted hero leads rather than joining the screen |
| 1 | **Cavalry & scouts** | Half screen the front, a quarter cover **each flank** — see below |
| 2 | Melee infantry | |
| 3 | Ranged | |
| 4 | **Support / magic** (healers) | Between the line and the siege, so they can reach the line without standing in it |
| 5 | **Siege** | Rearmost, at **2× spacing** — a catapult is not a spearman with more health |
| 6 | Economy / miners | |

Siege used to sit *ahead* of support, which put engines in front of the
people everything else exists to protect.

**Cavalry wings.** Half the cavalry rides in front, a quarter covers each
flank as a column abeam the body. The split rounds **toward the front**,
because a wing of one is not a wing: two knights both ride ahead, and the
wings only appear at four. Wings need a body to screen — all-cavalry keeps
its block. The flank gap equals the front-to-back block gap, so the
formation reads at one spacing in both axes.

**Blocks are separated by `TypeGapPitches` (2) row pitches**, scaled up to
the roomier of the two ranks where they differ — so the gap in front of a
siege train is a siege-sized gap.

The layout is computed in exactly one place,
`FormationMoveCommandHelper.BuildLayout`, which takes a rank census and
returns slot offsets. Anything that needs to know the shape — including
scenarios that spawn an army already in formation — calls it rather than
re-deriving it, because a spawn that disagrees with the layout by more than
the pose-fit tolerance makes the first order snap instead of continue.

## 4. Command semantics

- **Right-click = context command** (move / attack / gather / build / repair /
  garrison), **Shift+right-click = queue**, **A = attack-move**,
  **Patrol**, **Stop**, and the three stances **G** Aggressive / **D**
  Defensive / **H** Hold Position (see [Stances.md](Stances.md)). Idle units
  auto-engage by stance and are leashed to their guard point; units on ramparts never chase off
  the wall. (All existing behavior, kept.)
- Move / attack-move orders on multi-selections use the current formation.
- Rally points support target entities and shift-queued chains.

## 5. Gates, walls & overpass bridges (dual-level navigation)

The world has **two nav layers at the same XZ**: layer 0 (ground) and
layer 1 (rampart / deck). BFME2-gate rule: **the same footprint can be
walkable both on top (over) and underneath/through (under)**.

- **Walls**: ground layer blocked, rampart layer walkable (existing).
- **Gates**: ground layer *conditionally* passable — owner + allies only,
  blocked for everyone while **locked**; rampart layer walkable over the
  arch. Lock/unlock is a dynamic nav property, not a geometry change.
  (Existing, kept.)
- **Overpass bridges** (new structure class): a deck spanning ground that
  remains walkable underneath.
  - **Under**: ground-layer cells beneath the deck keep their normal terrain
    cost — any unit walks under the bridge freely, no faction filter.
  - **Over**: deck cells are layer-1 walkable for **all factions** (a bridge
    is a road, not a fortification).
  - **Ramps** at both ends are layer-transition cells (same mechanism as
    wall climb-access); units path onto/off the deck automatically when the
    deck route is cheaper — one order, no special input.
  - Deck height per bridge instance (default 4 m, matching rampart DeckY).
- Access filters (AoE4 parity, existing walls): allies climb walls via
  towers/gates from either side; enemies only via breaches (destroyed
  segments); enemies never descend via gates/towers.

## 6. Explicit non-goals

- No RVO/ORCA velocity-obstacle avoidance (AoE4 doesn't use it).
- No manual formation facing (AoE4 has none).
- No aggressive/defensive stance matrix — Hold Position only.
- No per-unit waypoint A\* for ground movement.
