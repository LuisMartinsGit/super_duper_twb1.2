---
deft:
  id: task-cursed-ground-luminous-crystals-111
  type: improvement
  status: active
  stage: scope
  phase: 0
  total_phases: 0
  priority: normal
  source: manual
  roadmap_id: null
  branch_strategy: current
  mode: human-in-the-loop
---

# Cursed-ground visual rework — luminous crystal shards with emanation

---

## Iteration 2 spec (2026-05-21) — voxel-grid pivot, "Living Particles" reference

### Reference

User shared a screenshot of the
[SineVFX "Living Particles" asset](https://assetstore.unity.com/packages/vfx/particles/spells/living-particles-105817)
as the target visual. The screenshot shows:

- A field of **upright rectangular voxel blocks** of varying heights,
  arranged on a grid
- Block **tops** are strongly emissive (bright orange/red in the asset's
  example; purple/green for our curse)
- **Sides** are darker, lit only by ambient — adjacent blocks at
  different heights show their dark side walls, and those side walls
  are what creates the **visible "dark grid lines"** between blocks
- Brighter "vein" patterns flow through the field as a wave (the
  current pulse driver already does this once the geometry matches)
- Sparse coverage at the perimeter where the curse splat falls off

The Iteration 1 shape language (radial starbursts + scatter nubs +
central dome) is wrong for this reference. Geometry needs to be rebuilt.

### User-confirmed scope (this iteration)

Two answers from the user during planning:

1. **Progression FX**: "All three layered together" — tendril growth +
   crystal bloom + ambient swarm.
2. **Recession FX**: "Reverse tendrils — particles flow back toward the
   node centre, picking up shards as dust as they pass."

User chose **Plan only** for the implementation roll-out, so this
section captures the spec; code lands in a follow-up session.

### Plan

Seven items, in order of visual impact. Items 1-4 are the geometry
rewrite (focused PR); items 5-7 are the animation layer on top.

**Item 1 — Voxel grid generator.**
Replace `ProceduralCurseShardGenerator.Create` shard loop with a 5×5
(adjustable) grid of upright rectangular prisms. Cells snap to integer
world coordinates so adjacent cursed-ground tiles' grids share the same
world-grid origin and tile seamlessly. Block width ~0.85 m on a 1.0 m
grid step leaves a ~0.15 m gap — that gap is the dark grid line.
Recommended: rename file to `ProceduralCurseBlockGenerator.cs` to
reflect the shape change.

**Item 2 — Deterministic per-cell heights.**
Block height per `(gridX, gridZ)` cell is computed by hashing the cell
coords. Inner cells (near a node centre): 0.4 - 1.2 m. Outer cells:
0.15 - 0.5 m. Deterministic across re-spawns AND across peers
(multiplayer-safe) without needing per-tile RNG seed. Falls back to
mid-range if no owning node found.

**Item 3 — Splatmap filter (unchanged).**
Each candidate cell's centre is checked against the curse splat layer
weight (existing
[Iteration 1 mechanism](../task-cursed-ground-luminous-crystals-111/task.md)).
Cells below threshold are skipped → grid conforms to the organic blob
shape. No change needed beyond porting the chunk-read helper to the
new generator.

**Item 4 — Pulse driver (unchanged).**
[CursedPulseDriver](../../../Assets/Scripts/Presentation/CursedPulseDriver.cs)
already modulates emission via MaterialPropertyBlock per cluster, with
the wavefront travelling outward from the node centre at 7 m/s. Each
block cluster registers itself with the driver — already in place.

**Item 5 — Block growth animation (progression: "crystal bloom").**
Each new block's `transform.localScale.y` animates from 0 → full
height over ~1.0 s with slight overshoot easing. Brief purple particle
burst at the block top when it pops in. Per-block animator
MonoBehaviour attached on spawn; self-destructs after the growth
completes to keep per-frame work bounded.

**Item 6 — Tendril growth + ambient swarm (progression: "tendrils" +
"ambient swarm").**
Per-node `ParticleSystem` already exists (Iteration 1 Phase 3) emitting
upward purple motes. Re-target velocityOverLifetime so motes flow
**outward** along the spread direction while the spread is still
expanding (read `CrystalSpread.CurrentRingRadius < SpreadRadius`),
idling to gentle upward drift when spread is complete. Adds the
"tendril" feel without a new system.

**Item 7 — Recession animation (reverse tendrils).**
When a cursed-ground tile entity is destroyed by
[CursedGroundRecessionSystem](../../../Assets/Scripts/Systems/Creatures/CursedGroundRecessionSystem.cs),
the cluster's blocks animate `scale.y → 0` over ~0.7 s. Each block emits
a small particle stream aimed at the node centre. A per-node "inward
flow" particle system collects all the dying blocks' particles. Cleanup
happens after the animation completes, not on entity destroy — the
`PresentationSpawnSystem` cleanup path needs a hook to delay
GameObject destruction by the animation duration (or the animator
itself defers `Destroy(gameObject)`).

### Files touched (estimated)

| File | Change | Lines |
|---|---|---|
| `ProceduralCurseShardGenerator.cs` (rename → `ProceduralCurseBlockGenerator.cs`) | Geometry rewrite | ~200 |
| New `CurseBlockGrowthAnimator.cs` | Per-block growth animation | ~70 |
| New `CurseBlockRecessionAnimator.cs` | Per-block recession animation | ~80 |
| New `CurseNodeFlowDirector.cs` | Per-node particle director | ~120 |
| `PresentationSpawnSystem.cs:352-360` | Update path to new generator | ~5 |
| `CursedPulseDriver.cs` | No change | 0 |
| `CrystalSpreadSystem.cs` | No change | 0 |
| `CursedGroundRecessionSystem.cs` | No change (cleanup happens via animator's `Destroy`) | 0 |

Total: ~475 lines new code + ~5 changed.

### Open questions for Iteration 2 implementation session

1. **Block z-fighting at tile overlaps**: adjacent tiles' grids both
   claim some cells. Simple approach: accept the doubled-up render at
   overlap. Cleaner: dedup via static `HashSet<int2>` of spawned cells
   (cleanup when cluster destroyed). Decide at implementation time.
2. **Per-block animator allocation**: ~200 tiles × ~25 blocks =
   ~5000 MonoBehaviours with Update during growth. They self-destruct
   after ~1 s so steady-state is 0. Acceptable but worth measuring.
3. **Material reuse**: current 5-bucket gradient material set still
   applies, but the cell-distance-based bucket needs recomputing per
   cell (not per tile cluster). Each block in a cluster might land in
   different buckets — that's OK, material is still shared per bucket
   so SRP batcher batches per bucket.
4. **Recession-system parented cleanup**: currently the tile entity's
   destruction triggers GameObject destruction immediately via
   `PresentationSpawnSystem`. Need a hook to detach the GameObject
   from entity tracking when an animator owns its lifecycle. Either:
   (a) animator parents itself to a separate "cursed-ground-orphans"
   GameObject before Destroy, OR (b) `PresentationSpawnSystem` checks
   for a "death-animator" component and skips immediate destruction.
   Decide at implementation time.

### Why this captures the asset's look

- The **glow-edge effect** in the reference image comes from emissive
  top faces + dark recessed side walls + URP bloom. No special shader
  needed.
- The **flowing "vein" patterns** in the reference are the same
  wavefront-pulse the driver already does — once blocks have emissive
  tops, the pulse will travel through them as bright bands.
- The **grid look** comes from snapping to integer world coords (item
  1) + width-less-than-step gaps (item 1) + per-cell height variation
  (item 2).
- The **sparse perimeter** comes from the existing splatmap filter
  (item 3) — cells where the splat is faint never spawn a block.

---

## Iteration 1 (2026-05-21) — luminous crystal shards (SHIPPED)

> This section preserves the original Iteration 1 spec for reference.
> The implementation landed (3 phases) and the visual was iterated on
> (low/wide spiky → scatter carpet → splatmap filter → pulse retune).
> Iteration 2 above supersedes the visual direction.

## Context

User feedback 2026-05-21: the existing cursed-ground visual (flat
purple/grey splatmap stain on the terrain) does not read as
"threatening." Players see the curse spreading but it feels passive —
no geometry rises out of the ground, no light is emitted, nothing
animates. The central
[CrystalNode](../../../Assets/Scripts/Bootstrap/CrystalNodeBootstrap.cs)
prefab is the only visual cue inside the cursed radius.

**Root cause** — at
[PresentationSpawnSystem.cs:352-360](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L352)
the cursed-ground presentation literally returns an **inactive
GameObject** with the comment "Invisible — terrain painting is the
visual." So every ring tile spawned by
[CrystalSpreadSystem.SpawnRingTiles](../../../Assets/Scripts/Systems/Creatures/CrystalSpreadSystem.cs#L180)
has the `CursedGroundDPS` damage component but **no renderable**. The
only visual is the flat terrain splat plus the central node.

The user wants the cursed area to feel actively dangerous: glowing
crystals jutting from the ground, light emanation, ideally with a
sickly green-purple gradient that communicates "spreading rot" outward
from the node.

## User Value

- The cursed area communicates threat at-a-glance — players instinctively
  steer units away even without reading the DPS tooltip.
- Visual gradient (purple at the source → sickly green at the edge)
  doubles as a directional cue showing where the curse is spreading.
- Light emanation makes the cursed area legible at night/in shadow and
  contributes to the game's emerging "religion vs corruption" theme.

## Requirements

- **R1** — Replace the invisible cursed-ground GameObject at
  [PresentationSpawnSystem.cs:352-360](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L352)
  with a procedural crystal cluster mesh per tile. Each cluster is 4-6
  small elongated octahedrons (varied scale, tilted 5-20°, randomised
  rotation per tile to avoid grid-look).
- **R2** — Cluster material is URP/Lit with **emission enabled**. Base
  color and emission shift across the radius — purple/violet at tiles
  near the node center, sickly green/yellow-green at the outer ring.
  Use [CursedGroundDPS.EffectRadius](../../../Assets/Scripts/Systems/Creatures/CrystalSpreadSystem.cs#L210)
  + the owner node's `LocalTransform` to compute per-tile distance and
  drive the gradient.
- **R3** — Visual scaling by distance: shards near the node are taller
  (1.0-1.4 m) and more saturated; outer shards are smaller (0.4-0.7 m)
  and dimmer. Communicates "danger intensity" gradient.
- **R4** — Hero lights: roughly 1 in 8-12 tiles attaches a small
  `Light` component (Point, range 2-3, intensity 1.0-1.5, color matching
  its emissive tone) so the cursed area emanates real light.
  Hero-tile selection is **deterministic** by `entity.Index % 10 == 0`
  so peers agree (multiplayer-safe presentation choice, no RNG drift).
- **R5** — URP bloom must contribute to the glow. If the active Volume
  profile in the scene doesn't have Bloom enabled, document that as a
  setup prerequisite — emission alone reads as flat without bloom.
- **R6** — Per-node ambient particle drift (optional, Phase 3): one
  `ParticleSystem` per crystal node (NOT per tile) emitting slow upward
  motes within the current spread radius. Spawned by
  [CrystalSpreadSystem](../../../Assets/Scripts/Systems/Creatures/CrystalSpreadSystem.cs)
  when the node is first created. Skip if Phase 1+2 already feel right.
- **R7** — Existing terrain splat stays unchanged (acts as a base
  "stain" beneath the shards). No edits to
  [ProceduralTerrain.PaintCursedGround](../../../Assets/Scripts/World/Terrain/ProceduralTerrain.cs#L1144).
- **R8** — Multiplayer/lockstep determinism: no RNG drift between
  peers. All randomisation seeded from the cursed-ground entity's
  Index/Version so both peers produce identical visuals.
- **R9** — Perf budget: a single node at full spread can have ~150-250
  tiles. Procedural mesh generation must use shared mesh + shared
  material so URP SRP batcher can batch them. Lights capped to ~15-25
  per node (hero-tile selection above).

## Acceptance Criteria

- [ ] Stepping into a cursed-ground area, the player sees a visible
      cluster of glowing crystal shards on every tile — not just the
      flat terrain stain.
- [ ] Tiles closest to the crystal node are taller and read purple;
      tiles at the outer ring are shorter and read sickly green.
- [ ] Shards visibly emit light (their material's emission contributes
      to URP bloom; hero-tile Point Lights cast real light onto
      surrounding objects/terrain).
- [ ] Color gradient is continuous, not stepped — adjacent tiles within
      ~1m of each other read as the same intermediate hue.
- [ ] A node with ~150 tiles at full spread does not regress framerate
      by more than ~5% on the target hardware (single mid-range GPU).
      If it does, fall back to single-mesh-per-tile (no per-shard
      `MeshFilter` GameObjects).
- [ ] 2-client lockstep test: both peers see identical shard rotations,
      sizes, and hero-light placement (Entity.Index-seeded
      randomisation works deterministically).
- [ ] No regression to the central crystal node visual or to the
      existing terrain splat stain — both remain visible underneath.
- [ ] Cleanup works: when a node is destroyed,
      [CursedGroundRecessionSystem](../../../Assets/Scripts/Systems/Creatures/CursedGroundRecessionSystem.cs)
      cascades the tiles; the new procedural shard GameObjects are
      destroyed alongside their entity (existing
      [PresentationSpawnSystem](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs)
      tracking handles this automatically).

## Implementation Phases

### Phase 1: Procedural crystal shards on every tile (highest impact)
**Scope:** Replace the invisible cursed-ground GameObject with a
4-6-shard procedural cluster per tile. Emissive purple-to-green
gradient driven by per-tile distance from the owning node. Deterministic
rotation/scale variation seeded by Entity.Index.
**Files:**
- `Assets/Scripts/Presentation/ProceduralCurseShardGenerator.cs` (new) — mirrors
  the shape of [ProceduralCadaverLoot](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L371)
  and [ProceduralUnitGenerator.cs](../../../Assets/Scripts/Presentation/ProceduralUnitGenerator.cs).
  Static `Create(pos, entity, nodeEntity, distanceFromNode, maxRadius)`
  builds the shard GameObject with shared mesh + shared material.
- `Assets/Scripts/Presentation/PresentationSpawnSystem.cs` — replace
  the inactive-GameObject path at lines 352-360 with a call to the
  new generator. Pass the owning node entity (read from
  `OwnerNode` component on the cursed-ground entity) so the generator
  can compute distance.
- (Maybe) `Assets/Scripts/Systems/Creatures/CrystalSpreadSystem.cs` —
  pass `maxRadius` (= `crystalNode.SpreadRadius`) through to the
  shard generator if not already accessible via component lookup.
**Verification:**
- [ ] In-editor: spawn a crystal node, spread the curse, walk close —
      every tile has a visible shard cluster.
- [ ] Color gradient reads continuously from purple at the node to
      green at the outer ring.
- [ ] No new compile errors; no regression to the central node visual.
- [ ] Frame profile shows the new cluster GameObjects are batched (one
      SetPass call dominant for the shared shard material).
**Estimated effort:** Medium (~45-60 min)

### Phase 2: Hero-tile Point Lights (light emanation)
**Scope:** Attach a `Light` component to roughly every 8th-12th shard
cluster (deterministic by `entity.Index`) so the cursed area emits
real light onto surrounding terrain/units. Verify URP bloom is
enabled in the active Volume profile.
**Files:**
- `Assets/Scripts/Presentation/ProceduralCurseShardGenerator.cs` —
  conditional `Light` attachment in `Create`.
- (Possibly) `Assets/Settings/VolumeProfiles/*` — confirm Bloom is
  enabled; this is a manual asset check, not a code change.
**Verification:**
- [ ] Walking a unit through the cursed area, the unit picks up
      faint purple lighting on its lit side.
- [ ] Hero-light count caps at ~15-25 per fully-spread node.
- [ ] No URP forward-renderer "too many lights" warnings.
**Estimated effort:** Small (~15-20 min)

### Phase 3: Ambient particle drift (optional polish)
**Scope:** Per-node `ParticleSystem` emitting slow upward purple motes
within the current spread radius. Spawned by `CrystalSpreadSystem`
when the node first becomes active; its emission shape scales with
`spread.CurrentRingRadius`.
**Files:**
- `Assets/Scripts/Presentation/ProceduralCurseParticleGenerator.cs`
  (new) — builds the per-node `ParticleSystem` GameObject.
- `Assets/Scripts/Systems/Creatures/CrystalSpreadSystem.cs` — spawn
  the particle GO on first-tick-with-tiles. Track via the node entity
  so cleanup works automatically.
**Verification:**
- [ ] Cursed area shows slow drifting motes visible from the standard
      RTS camera angle.
- [ ] Particle emission rate stays well under 200 alive particles per
      node at full spread.
**Estimated effort:** Small (~20-30 min). May be skipped entirely if
Phase 1+2 already feel sufficiently threatening.

## Edge Cases

- **Cursed area overlapping with player units**: shards must not block
  click/selection of friendly units standing inside the curse.
  Easiest: shards live on a non-clickable layer.
- **Multiplayer lockstep**: any RNG must be Entity.Index-seeded so
  both peers produce identical visuals.
- **Curse recession**: when a tile is destroyed by
  `CursedGroundRecessionSystem`, the shard cluster + its Light + any
  attached audio must clean up. Existing `PresentationSpawnSystem`
  tracking should handle this if the new GO is properly returned and
  registered — verify in implementation.
- **Crystal node killed**: all tiles cascade-cleanup via
  `WallSegmentCleanupSystem`-style pattern (or whatever the actual
  recession system uses). Same tracking concern as above.
- **Fog of war**: cursed ground may be hidden by FoW. The Phase 2
  Point Lights still cast light through fog (URP doesn't gate lights
  on visibility). Decide: kill the light when the tile is hidden, or
  let the glow be a fog-piercing tell. Recommend "kill when hidden"
  to preserve fog as a strategic mechanic.

## Technical Notes

**Color palette (user-confirmed 2026-05-21):**
- **Inner ring** (distance 0-30% of `SpreadRadius`): purple/violet.
  Base `#5C2A7A`, emission `#9B5BE0` × intensity 1.5-2.0.
- **Mid ring** (distance 30-70%): muted purple-green blend.
  Lerp between inner and outer LERP-style.
- **Outer ring** (distance 70-100%): sickly green/yellow-green.
  Base `#3D6B2A`, emission `#7AC83A` × intensity 0.8-1.2.

**Reference exemplars in codebase:**
- [ProceduralCadaverLoot](../../../Assets/Scripts/Presentation/PresentationSpawnSystem.cs#L371) — closest pattern; crystal cluster on the ground.
- [ProceduralUnitGenerator.cs](../../../Assets/Scripts/Presentation/ProceduralUnitGenerator.cs) — pattern for static `Create(pos, entity)` mesh generators.
- [WorldOverlayPalette.cs](../../../Assets/Scripts/UI/Common/WorldOverlayPalette.cs) — register the new shard color tokens here for cross-system reuse.

**Anti-patterns to avoid (from memory):**
- No new per-tick `CreateEntityQuery` (the generator runs per spawn,
  not per frame, so this isn't a concern; just stay alert).
- No RNG drift between peers — seed all randomisation from
  `entity.Index ^ entity.Version`.
- No `UnityEngine.Random.*` calls (use deterministic seeded RNG).

## Out of Scope

- Replacing the central crystal node prefab — its current visual is
  fine; only the surrounding tile field is in scope.
- Audio (cursed-ground hum SFX) — separate task if desired.
- Particle effects on unit damage from cursed ground — separate task.
- Animated growth/sway on the shards — possible polish, not v1.
- Changing the terrain splat color or pattern — task-110-era fix
  already restored the terrain layer; that's the base layer beneath
  the shards.
- Cursed-ground gameplay rebalance (DPS values, radius, spread rate).
  Visual-only task.
