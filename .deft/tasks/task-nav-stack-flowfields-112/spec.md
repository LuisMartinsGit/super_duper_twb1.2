# Implementation Prompt -- Crystal Curse Navigation Stack (Unity ECS/DOTS)

> Paste this prompt to start an implementation session. It defines the target
> architecture, the full system inventory, the constraints, and the build order.
> Treat it as a spec, not a one-shot request: confirm scope, then implement one
> system at a time with Burst jobs and tests.

---

## 1. Context

I'm building **Crystal Curse**, a solo-developed asymmetric RTS in **Unity (ECS/DOTS)**.
It has three factions whose economies and movement differ:

- **Alanthor** -- wall-based. Units patrol *on top of* walls, climb up at designated
  access points, and pass through gatehouses at both ground and rampart level. This
  faction is the reason the navigation stack must be multi-layer.
- **Feraldis** -- patrol/plunder, mobile.
- **Runaii** -- trade-route oriented.

The game ships with **single-player and local multiplayer**, so the simulation must be
**deterministic** (lockstep-safe). The "Crystal Curse" PvE mechanic mutates terrain,
resources, and threat over time, so the navigable world is **dynamic**.

We have already decided on the architecture (below) and explicitly ruled out Unity's
baked NavMesh + `NavMeshQuery`: the jobs query API is deprecated in Unity 6 without a
replacement, and baked meshes handle toggleable multi-level connectivity (gates,
wall-tops, destructible walls) poorly.

## 2. Goal

Implement a **grid + hierarchical portal graph + flow fields + steering** navigation
stack in pure DOTS, modeled on the architecture Relic shipped in *Age of Empires IV*
(Frank Cheng, GDC 2022, "Pathing in Age of Empires IV: Flow Fields and Steering
Behaviors"), itself built on Elijah Emerson's flow-field-tiles technique.

Target scale: hundreds of simultaneously-moving units (design for ~1500), a large grid
(start 512x512, design to 1024x1024), with construction, destruction, and terrain
mutation happening mid-match.

Reference reading (free): Emerson, "Crowd Pathfinding and Steering Using Flow Field
Tiles" (Game AI Pro, Ch. 23); Botea et al., HPA* paper.

## 3. Requirements (acceptance criteria)

1. Units steer away from each other and from small/dynamic obstacles without baking
   those obstacles into the pathfinder.
2. Static obstacles (buildings, walls) update the navigable space at runtime;
   pathing reacts without a full rebuild and without per-frame stalls.
3. Units traverse **two layers** -- ground and rampart-top -- connected only at
   **designated portals** (stairs/ramps and gatehouses).
4. **Gates** are conditional portals: passable by friendly units, blocked for enemies,
   toggleable open/closed. They connect inside-outside at ground level **and** connect
   rampart segments across the gatehouse at the top level, independently.
5. Per-unit-type traversal: footprint size, which layers a unit may enter, whether it
   may climb, terrain cost multipliers.
6. Everything is deterministic across machines and Burst-compiled.

---

## 4. Target Architecture (overview)

Two cleanly separated layers:

- **Global pathing** = where to go. Cost field -> hierarchical portal graph ->
  abstract A* -> flow field over the traversed tiles. Shared by all units heading the
  same direction.
- **Local steering** = how to move right now. Flow direction blended with neighbor
  avoidance and cohesion, resolved every sim tick. Transient obstacles (other units)
  live ONLY here, never in the pathfinder.

The portal graph is the key to the wall/gate mechanic: walls are impassable cells,
while climb points and gates are *portals* whose traversability is conditional and
toggleable -- no rebaking required.

---

## 5. System Inventory

Implement each as an `ISystem` (Burst) with jobs (`IJobEntity` / `IJobParallelFor` /
`IJob`). For each system below I want: components/blobs it owns, the jobs it schedules,
its update group/ordering, and its determinism notes.

### S1 -- Cost Field (data + maintenance)
- **Owns:** a grid of cells. Per cell: traversal cost (byte/ushort, non-uniform),
  passability flags, **layer/context id** (Ground, Rampart), and owner id for
  conditional cells. Store per layer (two cost fields or a context dimension).
- **Maintenance:** when buildings/walls/terrain (Crystal Curse mutation) are placed,
  destroyed, or changed, stamp footprints into the cost field and mark the affected
  **tiles dirty**.
- **Determinism:** integer costs only; deterministic write ordering for overlapping
  stamps.

### S2 -- Spatial Hash (neighbor queries)
- Uniform-grid spatial hash of unit positions, rebuilt each tick. Burst-friendly
  `NativeParallelMultiHashMap`. Shared by steering (S7) and formations (S8).
- **Determinism:** stable cell ordering; no float-hash nondeterminism.

### S3 -- Portal Graph (HPA* abstraction)
- Divide the grid into fixed **tiles** (start 16x16 cells). On each tile edge, detect
  **portals** = contiguous walkable spans crossing the boundary. Within a tile,
  flood-fill to compute intra-tile portal-to-portal connectivity and cost. Add
  cross-tile edges between adjacent portals.
- **Special portals:**
  - *Climb portal* -- connects a Ground cell cluster to a Rampart cell cluster at a
    designated access point.
  - *Gate portal (ground)* -- connects inside-outside, gated by owner + open state.
  - *Gate portal (rampart)* -- bridges two rampart spans across a gatehouse, gated
    independently.
- **Incremental rebuild:** only rebuild tiles flagged dirty by S1; recompute the
  affected portals and their edges, not the whole graph.
- **Determinism:** deterministic flood-fill order; stable portal ids.

### S4 -- Abstract Pathfinder (A* on portal graph)
- Input: start cell, goal cell, a **traversal profile** (footprint size, allowed
  layers, can-climb, owner, terrain multipliers). Output: an ordered **flow path** =
  sequence of tiles/portals. Conditional portals (gates) consulted at query time
  against the profile + gate state.
- Job-based, one A* per request; budget requests per tick (see S9).
- **Determinism:** integer/ordered costs; tie-break by stable id.

### S5 -- Flow Field Generation
- For tiles along the flow path, build the **integration field** (distance transform --
  start with Dijkstra, upgrade to Fast Marching + a line-of-sight pass for smooth
  gradients) and the **flow field** (per-cell direction; store 8-bit / 256 directions).
- **Segmented flow:** generate per-tile as units advance, with **overlapping segments**
  for accuracy. **Cache** per (tile, target portal, traversal profile) and reuse as
  building blocks.
- **Extended flow:** generate from the most-restricted unit type in a group, then
  extend coverage so smaller-footprint units in the same group aren't stranded.
- **Determinism:** fixed iteration order; integer integration where possible.

### S6 -- Flow Following / Movement
- Each unit samples the flow field at its current cell -> desired direction. Integrate
  velocity with max speed / accel / turn-rate. Fixed-step integration only.
- Hand off the desired direction to S7 rather than moving directly.

### S7 -- Steering / Local Avoidance
- Blend flow direction with: separation from neighbors, avoidance of moving units
  (reciprocal/RVO-style), avoidance of immobile units and small static obstacles, and
  group cohesion. Neighbor lookups via S2.
- This is the only system that "sees" other units as obstacles.
- **Determinism:** deterministic neighbor iteration; no order-dependent float accumulation.

### S8 -- Formation Movement
- A virtual formation leader requests/follows a flow path (S4/S5). Units occupy
  formation slots around the leader and fall back to following the flow when they have
  no line of sight to their slot.

### S9 -- Request Scheduler / Budget
- Queue path/flow requests with priorities; process a per-tick budget; spread work
  across ticks so a mass-move order never stalls the sim. Coalesce duplicate requests
  to the same goal/profile.

### S10 -- Layer Transition / Portal Traversal
- When a unit's path reaches a climb or gate portal: verify eligibility (owner, open
  state, can-climb), then transition the unit between Ground and Rampart contexts --
  move it along the link (animated traversal, not an instant teleport) and switch which
  cost field / flow context it samples afterward. Enemy units are rejected at gate
  portals here as a backstop even if a stale path reached them.

### S11 -- Debug Visualization (editor only)
- Gizmos/overlays for: cost field heatmap per layer, tile boundaries + portals
  (color-coded by type, including gate open/closed and climb links), the abstract A*
  path, and the generated flow vectors. Non-deterministic/editor-only; must not affect sim.

---

## 6. Cross-cutting Constraints

- **Language:** C# for Unity, DOTS idioms (`ISystem`, `SystemState`, `EntityCommandBuffer`,
  `BlobAssetReference` for static graph data, `NativeArray`/`NativeParallel*` for mutable).
- **Burst:** every job Burst-compiled; no managed allocations in hot paths.
- **Determinism (critical for local multiplayer):** no `UnityEngine.Random`, no
  wall-clock, no machine-dependent float ops in sim-affecting code; deterministic job
  scheduling and iteration order; prefer integer math for costs/integration. Keep all
  nav work inside the fixed-step deterministic sim group, separate from rendering.
- **No deprecated APIs:** do not use `NavMeshQuery` / experimental NavMesh jobs.
- **Memory:** explicit `Allocator` choices (Persistent for the graph, TempJob for
  per-tick work); document ownership/disposal.
- **Testing:** unit tests for cost-field stamping, portal detection, A* on a known
  graph, and flow correctness on hand-authored grids; a stress scene spawning N units.

## 7. Build Order (milestones -- implement and validate one at a time)

1. **M1 -- One unit moves.** S1 (single layer) + a whole-map flow field + S6. Click a
   goal, unit follows the flow. No hierarchy yet.
2. **M2 -- Crowds.** Add S2 + S7. Hundreds of units reach a goal while avoiding each other.
3. **M3 -- Scale.** Add S3 + S4 + S5 (segmented + cached flow). Large grid, no full-map
   integration.
4. **M4 -- Dynamic world.** Wire S1 dirty-tracking -> S3 incremental rebuild. Place/destroy
   buildings mid-run; pathing adapts without stalls.
5. **M5 -- Walls & gates.** Add Rampart layer to S1, climb + gate portals to S3, and S10.
   Alanthor units climb at access points, patrol ramparts, and pass gates by owner/state
   at both levels.
6. **M6 -- Polish.** Add S8 (formations), extended flow + mixed sizes (S5), S9 budgeting.
7. **M7 -- Hardening.** Determinism audit, S11 visualization, stress + regression tests.

## 8. Working Agreement (how to respond when this prompt is used)

1. Start by restating the chosen architecture in one paragraph and listing any
   assumptions you're making about my existing ECS setup (ask only if blocking).
2. Propose the component/blob layout for the milestone in question before writing system code.
3. Implement the milestone's systems as compilable C# files, Burst-ready, with the jobs
   and the data structures, plus the relevant tests.
4. Call out determinism risks explicitly at each step.
5. Don't jump ahead to later milestones; finish and verify the current one first.

**First task when resumed:** confirm scope and implement **M1** (S1 single-layer cost
field + whole-map flow field + S6 flow following), with a minimal stress scene and a
flow-correctness test.
