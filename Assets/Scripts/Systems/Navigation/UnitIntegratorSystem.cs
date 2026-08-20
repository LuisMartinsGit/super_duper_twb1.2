// UnitIntegratorSystem.cs
// task-112 M4 -- replaces the deleted MovementSystem.cs from
// Assets/Scripts/Systems/Movement/. Reads SteeringDesiredDir (M2)
// with a FlowDesiredDir (M1) fallback, integrates LocalTransform,
// manages UserMoveOrder / DesiredDestination / AttackMoveTag /
// FormationSpeedOverride lifecycle, and applies SpellDebuff /
// SpellBuff / Fortified speed modifiers.
//
// What's GONE compared to the old MovementSystem:
//   * Every NavMesh.SamplePosition call (off-mesh gate + height snap).
//   * NavMeshPathfollowState / NavMeshWaypoint corridor walk.
//   * PassabilityGrid centre-cell + spiral-escape (M4 OOS for the
//     integrator; cost-field IsCellPassable is the source of truth).
//   * The "navmesh delivered us as close as it can" corridor-end branch
//     (the cost-field-driven flow always converges on the goal cell).
//
// What's KEPT:
//   * MoveCommand / AttackMoveCommand -> DesiredDestination conversion.
//   * Per-unit smoothed-direction blending (anti-jitter).
//   * Per-unit cosmetic rotation (turn-rate clamped).
//   * Slope check against TerrainUtility.GetHeight (no NavMesh sample).
//   * Stuck-counter escalation (sidestep -> escape -> cancel).
//   * Combat short-circuit for archers with in-range targets.
//
// Determinism notes:
//   * No UnityEngine.Random / wall-clock / Mathf in sim-affecting code.
//   * dt = SystemAPI.Time.DeltaTime is the fixed-step value the
//     SimulationSystemGroup ticks at -- deterministic across machines.
//   * Per-unit work writes only the entity's own components; no
//     cross-entity reads inside the per-entity loop.
//   * Job not Burst-compiled because TerrainUtility.GetHeight is managed
//     (calls into UnityEngine.Terrain). Same limitation as the old
//     MovementSystem -- documented in the file header there too.
//
// [UpdateAfter(typeof(...))] hooks that used to point at MovementSystem
// (BattalionSyncSystem, WallGarrisonSystem, WallDoorAccessSystem) now
// point at UnitIntegratorSystem.
//
// Location: Assets/Scripts/Systems/Navigation/UnitIntegratorSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// task-112 M4 integrator. Single-thread main-thread foreach over units
    /// (mirrors the deleted MovementSystem's shape). The per-unit work is
    /// pure (writes only the entity's own components) so a future Burst
    /// IJobEntity port is mechanical -- M4 keeps it readable / debuggable
    /// since this is the deletion-gate phase.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct UnitIntegratorSystem : ISystem
    {
        private const float StopDistance = 0.5f;
        private const float DefaultMoveSpeed = 3.5f;
        private const float TurnSpeed = 8f;             // rad/s (~460 deg/s)

        // Mirror of SteeringSystem.SeparationRadius. Two units cannot stand
        // closer than this, which is the whole reason for the crowded-arrival
        // rule below: with a 0.5 m StopDistance and a 1.5 m separation ring,
        // two units sent to the SAME point can never both satisfy arrival.
        private const float SeparationClearance = 1.5f;
        /// <summary>Arrival window when the ordered point is already occupied
        /// by someone who got there first.</summary>
        private const float CrowdStopDistance = StopDistance + SeparationClearance;

        /// <summary>How near the goal another unit must be before it genuinely
        /// blocks arrival. A unit `d` from the goal keeps us at least
        /// (SeparationClearance - d) away, which only beats StopDistance while
        /// d &lt; SeparationClearance - StopDistance. Past that we can walk
        /// around it, and declaring arrival would be giving up early.</summary>
        private const float GoalBlockedRadius = SeparationClearance - StopDistance;

        // Arrival braking. SteeringSystem normalises its force vector, so a
        // unit one metre from its goal, with flow and separation very nearly
        // cancelling, still moved at FULL speed along whatever tiny residual
        // survived — the literal circling motion at a destination. Taper the
        // step over the last few metres so units decelerate into the goal
        // instead of skating around it.
        private const float ArriveSlowRadius = 2.5f;
        private const float MinArriveSpeedScale = 0.2f;
        // Per-step terrain backstop: the PassabilityGrid cell mask is the
        // single authority (see the TERRAIN CHECK in the step loop) — no
        // independent slope constants here any more.
        private const float SmoothRate = 12f;

        // Max walking-surface drop per step. Terrain is continuous so real
        // slopes never trip this; only ledges (bridge deck edges) do.
        private const float MaxLedgeDrop = 2f;

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // Neighbour lookup for the crowded-arrival test. Optional: the hash
            // is allocated lazily by SpatialHashRebuildSystem, so early ticks
            // simply fall back to the plain StopDistance arrival.
            bool hasHash = SystemAPI.TryGetSingleton<NavSpatialHash>(out var navHash);

            // ── PHASE 1: MoveCommand -> DesiredDestination ──────────────
            foreach (var (mc, entity) in SystemAPI.Query<RefRO<MoveCommand>>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<BuildingTag>(entity))
                {
                    ecb.RemoveComponent<MoveCommand>(entity);
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        var dd = em.GetComponentData<DesiredDestination>(entity);
                        dd.Has = 0;
                        ecb.SetComponent(entity, dd);
                    }
                    continue;
                }

                if (em.HasComponent<GuardPoint>(entity))
                    ecb.SetComponent(entity, new GuardPoint { Position = mc.ValueRO.Destination, Has = 1 });

                if (!em.HasComponent<DesiredDestination>(entity))
                    ecb.AddComponent(entity, new DesiredDestination { Position = mc.ValueRO.Destination, Has = 1 });
                else
                    ecb.SetComponent(entity, new DesiredDestination { Position = mc.ValueRO.Destination, Has = 1 });

                if (em.HasComponent<Target>(entity))
                    ecb.SetComponent(entity, new Target { Value = Entity.Null });

                if (!em.HasComponent<UserMoveOrder>(entity))
                    ecb.AddComponent<UserMoveOrder>(entity);

                if (em.HasComponent<SmoothedDirection>(entity))
                    ecb.SetComponent(entity, new SmoothedDirection { Value = float3.zero });
                if (em.HasComponent<StuckState>(entity))
                    ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });

                ecb.RemoveComponent<MoveCommand>(entity);
            }

            // ── PHASE 1b: AttackMoveCommand -> DesiredDestination ───────
            foreach (var (amc, entity) in SystemAPI.Query<RefRO<AttackMoveCommand>>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<BuildingTag>(entity))
                {
                    ecb.RemoveComponent<AttackMoveCommand>(entity);
                    continue;
                }

                if (em.HasComponent<GuardPoint>(entity))
                    ecb.SetComponent(entity, new GuardPoint { Position = amc.ValueRO.Destination, Has = 1 });

                if (!em.HasComponent<DesiredDestination>(entity))
                    ecb.AddComponent(entity, new DesiredDestination { Position = amc.ValueRO.Destination, Has = 1 });
                else
                    ecb.SetComponent(entity, new DesiredDestination { Position = amc.ValueRO.Destination, Has = 1 });

                if (em.HasComponent<Target>(entity))
                    ecb.SetComponent(entity, new Target { Value = Entity.Null });

                if (!em.HasComponent<AttackMoveTag>(entity))
                    ecb.AddComponent<AttackMoveTag>(entity);

                if (em.HasComponent<UserMoveOrder>(entity))
                    ecb.RemoveComponent<UserMoveOrder>(entity);

                if (em.HasComponent<SmoothedDirection>(entity))
                    ecb.SetComponent(entity, new SmoothedDirection { Value = float3.zero });
                if (em.HasComponent<StuckState>(entity))
                    ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });

                ecb.RemoveComponent<AttackMoveCommand>(entity);
            }

            // ── PHASE 2: integrate units toward DesiredDestination ──────
            // Dying units (DeathAnimationState) are excluded so a corpse stops
            // instantly and doesn't slide through its death animation — its
            // movement is cancelled the moment DeathSystem registers the death.
            foreach (var (xf, dd, entity) in SystemAPI
                .Query<RefRW<LocalTransform>, RefRW<DesiredDestination>>()
                .WithAll<UnitTag>()
                .WithNone<DeathAnimationState>()
                .WithEntityAccess())
            {
                if (dd.ValueRO.Has == 0) continue;

                // Buildings should never move.
                if (SystemAPI.HasComponent<BuildingTag>(entity))
                {
                    dd.ValueRW.Has = 0;
                    continue;
                }

                // task-112 M5 -- LayerTransitionSystem owns the unit's
                // position while a climb / gate traversal is in flight.
                // The integrator must not touch LocalTransform here or
                // the two systems will fight over the same frame.
                if (em.HasComponent<LayerTraversalState>(entity)) continue;

                // Lazy-add ECS scratch (deferred via ECB).
                if (!em.HasComponent<SmoothedDirection>(entity))
                    ecb.AddComponent(entity, new SmoothedDirection { Value = float3.zero });
                if (!em.HasComponent<StuckState>(entity))
                    ecb.AddComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });

                // Resolve effective speed: FormationSpeedOverride > MoveSpeed > default.
                float speed = DefaultMoveSpeed;
                if (em.HasComponent<FormationSpeedOverride>(entity))
                {
                    var fso = em.GetComponentData<FormationSpeedOverride>(entity);
                    if (fso.Value > 0) speed = fso.Value;
                }
                else if (em.HasComponent<MoveSpeed>(entity))
                {
                    var ms = em.GetComponentData<MoveSpeed>(entity);
                    if (ms.Value > 0) speed = ms.Value;
                }

                if (em.HasComponent<SpellDebuff>(entity))
                {
                    var debuff = em.GetComponentData<SpellDebuff>(entity);
                    speed *= (1f - debuff.SpeedReduction);
                }
                // The Veil / Suppression auras: BorderDebuff.SpeedPenalty was
                // authored but never consumed — units wading through veil
                // crust (or a Suppression field) now actually slow down.
                if (em.HasComponent<BorderDebuff>(entity))
                {
                    var bd = em.GetComponentData<BorderDebuff>(entity);
                    if (bd.SpeedPenalty > 0f)
                        speed *= (1f - math.min(0.9f, bd.SpeedPenalty));
                }
                if (em.HasComponent<Fortified>(entity)) speed = 0f;
                if (em.HasComponent<SpellBuff>(entity))
                {
                    var buff = em.GetComponentData<SpellBuff>(entity);
                    if (buff.SpeedMultiplier > 0f && buff.SpeedMultiplier != 1f)
                        speed *= buff.SpeedMultiplier;
                }
                if (speed <= 0f) continue;

                // Archers in firing range: ARRIVE, don't just halt. The old
                // bare `continue` here skipped the arrival Has-clear below, so
                // the unit froze with DesiredDestination.Has = 1 — which is
                // exactly the "is moving" state RangedCombatSystem's fire gate
                // reads, so the archer stood forever without shooting (the
                // auto-acquire freeze). Clearing Has makes the halt a real
                // arrival the combat system can fire from. The metric also now
                // matches RangedCombatSystem exactly (XZ distance, height-
                // scaled max range, measured to the target's edge) so the two
                // systems can never disagree about "in range" and flip-flop
                // across a halt/chase band on slopes or against buildings.
                // ...but an EXPLICIT player move order outranks the firing
                // band. Without this, any ranged unit that auto-acquires a
                // target mid-march declares itself arrived and drops the order.
                // On a catapult (min 5.5-10 m, max 20-30 m) that means it
                // freezes the moment anything enters a very wide band and
                // refuses to continue — reported as "siege engines cannot
                // move". Auto-acquire deliberately does NOT exclude
                // UserMoveOrder (TargetingSystem.Acquire), so the exemption
                // has to live here.
                if (em.HasComponent<ArcherTag>(entity)
                    && em.HasComponent<Target>(entity)
                    && !em.HasComponent<UserMoveOrder>(entity))
                {
                    var tgt = em.GetComponentData<Target>(entity);
                    if (tgt.Value != Entity.Null && em.Exists(tgt.Value)
                        && em.HasComponent<LocalTransform>(tgt.Value)
                        && em.HasComponent<Health>(tgt.Value)
                        && em.GetComponentData<Health>(tgt.Value).Value > 0)
                    {
                        float3 myP = xf.ValueRO.Position;
                        float3 tgtPos = em.GetComponentData<LocalTransform>(tgt.Value).Position;
                        float maxRange = 25f, minRange = 10f; // RangedCombat defaults
                        if (em.HasComponent<ArcherState>(entity))
                        {
                            var ast = em.GetComponentData<ArcherState>(entity);
                            if (ast.MaxRange > 0) maxRange = ast.MaxRange;
                            if (ast.MinRange > 0) minRange = ast.MinRange;
                        }
                        maxRange *= TheWaningBorder.Systems.Combat.HeightAdvantage
                            .Multiplier(myP.y, tgtPos.y);
                        // Shared surface metric — the legacy circle Radius here
                        // disagreed with RangedCombatSystem's box math against
                        // sized buildings, which is exactly the halt/chase
                        // flip-flop this block exists to prevent.
                        float edge = TheWaningBorder.Core.TargetGeometry
                            .SurfaceDistXZ(em, myP, tgt.Value);
                        // Halt only inside the FIRING band. Below min range the
                        // unit must stay mobile or RangedCombat's retreat order
                        // would be arrived-out right here every frame.
                        if (edge <= maxRange && edge >= minRange)
                        {
                            dd.ValueRW.Has = 0;
                            continue;
                        }
                    }
                }

                float3 pos = xf.ValueRO.Position;
                float3 goal = dd.ValueRO.Position;

                float3 to = goal - pos;
                to.y = 0f;
                float distSqr = math.lengthsq(to);

                float dist = math.sqrt(distSqr);

                // A unit with a live Target is CHASING: its destination is
                // re-issued every frame at a point that moves with the target.
                // Neither the crowd-arrival rule nor the arrival brake below
                // applies to that — settling early would stop a chase short of
                // contact, and braking near a fleeing target of equal speed
                // would mean melee could never close. Combat approach keeps the
                // behaviour it already had (see GoalWallSlideSuppressRadius in
                // SteeringSystem, which owns the orbit-a-building case).
                bool chasing = em.HasComponent<Target>(entity)
                    && em.GetComponentData<Target>(entity).Value != Entity.Null;

                // Arrived — either inside the ordinary stop window, or as close
                // as the crowd physically permits. The second case matters
                // because StopDistance (0.5 m) is smaller than the separation
                // ring (1.5 m): whenever two or more units are sent to the same
                // point — a rally flag, a formation slot that snapped onto an
                // already-taken cell, an AI staging position — everyone but the
                // first arrival is FORBIDDEN by steering from ever satisfying
                // the plain test. They used to mill around the point instead,
                // until StuckRedirectSystem noticed seconds later. Settle them
                // immediately.
                bool arrived = distSqr <= (StopDistance * StopDistance);
                if (!arrived && !chasing && hasHash
                    && distSqr <= CrowdStopDistance * CrowdStopDistance)
                    arrived = GoalTakenByCloserNeighbour(em, in navHash, goal, pos, entity, dist);

                if (arrived)
                {
                    dd.ValueRW.Has = 0;
                    if (em.HasComponent<UserMoveOrder>(entity)) ecb.RemoveComponent<UserMoveOrder>(entity);
                    if (em.HasComponent<AttackMoveTag>(entity)) ecb.RemoveComponent<AttackMoveTag>(entity);
                    if (em.HasComponent<FormationSpeedOverride>(entity)) ecb.RemoveComponent<FormationSpeedOverride>(entity);
                    continue;
                }

                float3 dir = to / math.max(1e-5f, dist);

                // Which nav layer is this unit on (0 = Ground, 1 = Rampart)?
                byte unitLayer = 0;
                if (em.HasComponent<NavLayerIndex>(entity))
                    unitLayer = em.GetComponentData<NavLayerIndex>(entity).Layer;
                bool onRampart = unitLayer == NavLayerIndex.LayerRampart;

                // === PATHFINDING DIRECTION (M4 preference chain) ===
                // task-112 M4: NavMesh corridor is gone. Preference chain is:
                //   1. SteeringDesiredDir (M2) -- flow + local avoidance
                //   2. FlowDesiredDir    (M1) -- raw flow direction
                //   3. direct-line       -- straight at the goal cell
                // Rampart units skip flow/steering entirely — those are
                // computed on the ground (layer-0) cost field and would steer
                // a wall-top unit off the wall. They walk straight toward
                // their (garrison-slot) goal; the layer-aware passability
                // check below keeps them on walkable rampart cells.
                if (!onRampart && em.HasComponent<SteeringDesiredDir>(entity))
                {
                    var sdd = em.GetComponentData<SteeringDesiredDir>(entity);
                    if (sdd.HasValue != 0 && math.lengthsq(sdd.Value) > 1e-8f)
                        dir = math.normalize(new float3(sdd.Value.x, 0f, sdd.Value.z));
                }
                else if (!onRampart && em.HasComponent<FlowDesiredDir>(entity))
                {
                    var fdd = em.GetComponentData<FlowDesiredDir>(entity);
                    if (fdd.HasValue != 0 && math.lengthsq(fdd.Value) > 1e-8f)
                        dir = math.normalize(new float3(fdd.Value.x, 0f, fdd.Value.z));
                }

                // === Smoothing ===
                float3 smoothedDir = dir;
                if (em.HasComponent<SmoothedDirection>(entity))
                {
                    var sd = em.GetComponentData<SmoothedDirection>(entity);
                    if (math.lengthsq(sd.Value) > 1e-8f)
                        smoothedDir = math.normalizesafe(math.lerp(sd.Value, dir, math.saturate(SmoothRate * dt)));
                    ecb.SetComponent(entity, new SmoothedDirection { Value = smoothedDir });
                }

                var t = xf.ValueRO;

                // === Cosmetic rotation ===
                if (math.lengthsq(smoothedDir) > 1e-8f)
                {
                    float3 fwd = math.normalize(new float3(smoothedDir.x, 0f, smoothedDir.z));
                    quaternion targetRot = quaternion.RotateY(math.atan2(fwd.x, fwd.z));
                    float maxTurn = TurnSpeed * dt;
                    SmoothSlerp(in t.Rotation, in targetRot, maxTurn, out var smoothed);
                    t.Rotation = smoothed;
                }

                // === Step ===
                // Arrival braking: taper speed over the last ArriveSlowRadius
                // metres. Without it the unit ran at full speed right up to the
                // arrival test, and since the steering vector is NORMALISED,
                // near-cancelling forces at the goal still produced full-speed
                // motion in an arbitrary direction — a circle, not a stop.
                float arriveScale = (!chasing && dist < ArriveSlowRadius)
                    ? math.max(MinArriveSpeedScale, dist / ArriveSlowRadius)
                    : 1f;
                float step = math.min(speed * arriveScale * dt, dist);
                float3 nextPos = pos + smoothedDir * step;

                // === COST-FIELD PASSABILITY (replaces PassabilityGrid + NavMesh sample gate) ===
                bool blocked = false;
                // Faction-aware: gate cells stamp CostConditional (254), which
                // the plain IsCellPassable reports walkable for EVERYONE. That
                // made every gate a permanent hole in the wall for the enemy.
                // The owner faction is in the cell's flag bits, so ask the
                // faction-aware overload instead.
                byte navFaction = em.HasComponent<FactionTag>(entity)
                    ? (byte)em.GetComponentData<FactionTag>(entity).Value
                    : (byte)0xFF; // no faction -> never matches an owner
                int2 nextCell = NavGridQuery.WorldToCellInt2(nextPos);
                if (nextCell.x != int.MinValue
                    && !NavGridQuery.IsCellPassableFor(nextCell, unitLayer, navFaction))
                {
                    // Only enforce the cost-field block if the unit's current
                    // cell IS passable -- units that spawn inside a building
                    // need to walk OUT through their own footprint.
                    int2 currentCell = NavGridQuery.WorldToCellInt2(pos);
                    if (currentCell.x != int.MinValue
                        && NavGridQuery.IsCellPassableFor(currentCell, unitLayer, navFaction))
                        blocked = true;
                }

                // === TERRAIN CHECK (ground only — the rampart deck is flat) ===
                // Backstop against cost-grid rounding at cell edges. Consults
                // the SAME PassabilityGrid mask the pathfinding bake uses
                // (slope budget, water, NoWalk paint, paint-only mode and
                // bridges are all encoded there), so movement can never
                // disagree with the plan. The old version re-derived slope
                // from raw terrain heights per step — on sculpted terrain
                // that spiked over the budget on surface noise the grid
                // considered walkable, and units stuttered on legitimate
                // inclines (blocked step -> sidestep -> retry).
                if (!blocked && !onRampart)
                {
                    var pg = TheWaningBorder.World.Terrain.PassabilityGrid.Instance;
                    // IsMaskReady, not just non-null: an unbuilt mask is all
                    // zeros, i.e. "everything walkable", and this backstop
                    // would wave units into water and off cliffs while
                    // reporting agreement with the plan.
                    if (pg != null && pg.IsMaskReady)
                    {
                        var cell = pg.WorldToCell(nextPos);
                        if (pg.GetCell(cell) == TheWaningBorder.World.Terrain.PassabilityGrid.TerrainBlocked)
                        {
                            blocked = true;
                        }
                        else if (pg.IsBridgeDeckOnly(cell))
                        {
                            // Deck-only cell: the ground here is cliff/NoWalk;
                            // only the bridge deck is walkable. Admit the step
                            // only when the deck is within step-up reach of
                            // the unit (same MountStepLimit rule as
                            // GetSurfaceHeight, so admission and the height
                            // snap can never disagree) — a ground-level unit
                            // under the span must route around, not
                            // cliff-walk beneath.
                            bool onDeck =
                                TheWaningBorder.World.Terrain.BridgeSurface.TryGetDeckHeight(
                                    nextPos.x, nextPos.z, out float deckY)
                                && deckY - t.Position.y
                                    <= TheWaningBorder.World.Terrain.BridgeSurface.MountStepLimit;
                            if (!onDeck) blocked = true;
                        }

                        // === LEDGE GUARD ===
                        // A step whose walking surface drops far below the
                        // unit is a fall off a deck edge (terrain itself is
                        // continuous, so ordinary downhill never trips this).
                        // Units must leave a bridge via its ramps, not by
                        // clipping down the sides.
                        if (!blocked)
                        {
                            float nextSurf = TerrainUtility.GetSurfaceHeight(
                                nextPos.x, nextPos.z, t.Position.y);
                            if (t.Position.y - nextSurf > MaxLedgeDrop)
                                blocked = true;
                        }
                    }
                }

                // === STUCK DETECTION ===
                if (blocked)
                {
                    if (em.HasComponent<StuckState>(entity))
                    {
                        var stuck = em.GetComponentData<StuckState>(entity);
                        stuck.Counter = (byte)math.min(stuck.Counter + 1, 255);

                        if (stuck.Counter > 30)
                        {
                            // Tier 3: try cost-field escape via NavGridQuery.SnapToPassable.
                            // If the current cell is impassable, snap to the nearest
                            // passable cell so the unit can resume moving.
                            // Ground-only — snapping a rampart unit to passable
                            // ground would yank it off the wall to terrain.
                            bool escaped = false;
                            int2 here = NavGridQuery.WorldToCellInt2(pos);
                            if (!onRampart && here.x != int.MinValue && !NavGridQuery.IsCellPassable(here))
                            {
                                NavGridQuery.SnapToPassable(pos, out var escapePos, out var escOk);
                                if (escOk)
                                {
                                    escapePos.y = TerrainUtility.GetSurfaceHeight(escapePos.x, escapePos.z, pos.y);
                                    // Ledge guard applies to escapes too — a
                                    // deck unit must not teleport off the
                                    // bridge side; fall through to the order
                                    // cancel instead.
                                    if (pos.y - escapePos.y <= MaxLedgeDrop)
                                    {
                                        t.Position = escapePos;
                                        ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });
                                        escaped = true;
                                    }
                                }
                            }

                            if (!escaped)
                            {
                                dd.ValueRW.Has = 0;
                                if (em.HasComponent<UserMoveOrder>(entity)) ecb.RemoveComponent<UserMoveOrder>(entity);
                                if (em.HasComponent<AttackMoveTag>(entity)) ecb.RemoveComponent<AttackMoveTag>(entity);
                                if (em.HasComponent<FormationSpeedOverride>(entity)) ecb.RemoveComponent<FormationSpeedOverride>(entity);
                                ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });

                                // Tell the leash the order is abandoned, or
                                // TargetingSystem's return-to-guard re-issues
                                // this exact destination next tick and the unit
                                // grinds back into the same blocker forever.
                                if (em.HasComponent<GuardPoint>(entity))
                                {
                                    var gp = em.GetComponentData<GuardPoint>(entity);
                                    if (gp.Has != 0)
                                    {
                                        var mark = new GuardSuppressed { Point = gp.Position };
                                        if (em.HasComponent<GuardSuppressed>(entity))
                                            ecb.SetComponent(entity, mark);
                                        else
                                            ecb.AddComponent(entity, mark);
                                    }
                                }
                            }
                        }
                        else if (stuck.Counter > 5)
                        {
                            // Tier 2: try perpendicular direction.
                            byte attempt = (byte)(stuck.LastAttempt == 1 ? 2 : 1);
                            float3 perp = attempt == 1
                                ? new float3(-smoothedDir.z, 0f, smoothedDir.x)
                                : new float3(smoothedDir.z, 0f, -smoothedDir.x);
                            perp = math.normalizesafe(perp);

                            float3 perpPos = pos + perp * step;
                            bool perpBlocked = false;
                            int2 perpCell = NavGridQuery.WorldToCellInt2(perpPos);
                            if (perpCell.x != int.MinValue && !NavGridQuery.IsCellPassable(perpCell, unitLayer))
                                perpBlocked = true;

                            if (!perpBlocked)
                            {
                                perpPos.y = onRampart
                                    ? TheWaningBorder.Systems.Buildings.LayeredMoveSystem
                                        .RampartSurfaceY(perpPos.x, perpPos.z)
                                    : TerrainUtility.GetSurfaceHeight(perpPos.x, perpPos.z, t.Position.y);
                                // Ledge guard applies to sidesteps too — the
                                // perpendicular hop must not carry a deck
                                // unit over the bridge side.
                                if (t.Position.y - perpPos.y > MaxLedgeDrop)
                                    perpBlocked = true;
                            }

                            if (!perpBlocked)
                            {
                                t.Position = perpPos;
                            }

                            stuck.LastAttempt = attempt;
                            ecb.SetComponent(entity, stuck);
                        }
                        else
                        {
                            ecb.SetComponent(entity, stuck);
                        }
                    }

                    xf.ValueRW = t;
                    continue;
                }

                // Not blocked: reset stuck counter.
                if (em.HasComponent<StuckState>(entity))
                    ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });

                // === Layer-aware height snap (task-112 M5) ===
                // Layer 0 (Ground) -> walking-surface height (ground, or a
                // bridge deck when the unit is already up on one — nearest
                // surface to the unit's current Y wins, so units under an
                // arch stay under it). Layer 1 (Rampart) -> the wall-deck
                // constant, or the actual deck mesh height when the unit is
                // crossing an overpass bridge (BridgeSurface-covered cells).
                if (onRampart)
                    nextPos.y = TheWaningBorder.Systems.Buildings.LayeredMoveSystem
                        .RampartSurfaceY(nextPos.x, nextPos.z);
                else
                    nextPos.y = TerrainUtility.GetSurfaceHeight(nextPos.x, nextPos.z, t.Position.y);

                t.Position = nextPos;
                xf.ValueRW = t;
            }
        }

        /// <summary>
        /// True when another unit already holds the ordered point and is
        /// wedged between us and it — i.e. it sits CLOSER to the goal than we
        /// are, and is inside our separation ring, so steering can never let us
        /// past it. That makes our current stand-off the closest approach
        /// physically available, and the order complete.
        ///
        /// Existential over the 3x3 spatial-hash ring around the goal, so the
        /// result does not depend on bucket iteration order (determinism: the
        /// hash is populated in chunk-walk order, and this reads it read-only).
        /// </summary>
        private static bool GoalTakenByCloserNeighbour(EntityManager em, in NavSpatialHash hash,
            float3 goal, float3 pos, Entity self, float myDist)
        {
            if (!hash.Map.IsCreated) return false;

            NavSpatialHash.WorldToCell(in goal, hash.CellSize, out int cx, out int cz);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int key = NavSpatialHash.PackKey(cx + dx, cz + dz);
                    if (!hash.Map.TryGetFirstValue(key, out Entity other, out var it)) continue;
                    do
                    {
                        if (other == self) continue;
                        if (!em.HasComponent<LocalTransform>(other)) continue;
                        // A corpse mid-death-animation is about to stop
                        // occupying the point; don't settle behind it.
                        if (em.HasComponent<DeathAnimationState>(other)) continue;

                        float3 op = em.GetComponentData<LocalTransform>(other).Position;

                        float gx = op.x - goal.x, gz = op.z - goal.z;
                        float otherToGoal = math.sqrt(gx * gx + gz * gz);

                        // The blocker must actually be SITTING ON the goal, not
                        // merely somewhere between us and it. "Closer than me"
                        // alone was far too weak: a worker walking past, or one
                        // parked at a different stand slot 2.8 m away, counted
                        // as proof the destination was unreachable — so units
                        // stopped metres short of ground they could plainly have
                        // walked to, which is the "they give up too soon".
                        //
                        // A unit `d` from the goal forces us no nearer than
                        // (SeparationClearance - d), so it only genuinely
                        // prevents arrival while that exceeds StopDistance.
                        // Anything further out we can simply walk around.
                        if (otherToGoal >= GoalBlockedRadius) continue;
                        if (otherToGoal >= myDist) continue; // not ahead of us

                        float sx = op.x - pos.x, sz = op.z - pos.z;
                        float otherToMe = math.sqrt(sx * sx + sz * sz);
                        if (otherToMe <= SeparationClearance) return true;
                    } while (hash.Map.TryGetNextValue(out other, ref it));
                }
            }
            return false;
        }

        /// <summary>
        /// Slerp from current to target rotation, clamped to maxAngle radians.
        /// Pure math; safe to mark [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)] for any future Burst-job
        /// port of the integrator.
        /// </summary>
        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        private static void SmoothSlerp(in quaternion from, in quaternion to, float maxAngle, out quaternion result)
        {
            float4 toVal = to.value;
            float dot = math.dot(from.value, toVal);
            if (dot < 0f) { toVal = -toVal; dot = -dot; }
            quaternion toFixed = new quaternion(toVal);

            if (dot > 0.9999f) { result = toFixed; return; }

            float angle = math.acos(math.clamp(dot, -1f, 1f)) * 2f;
            if (angle <= maxAngle) { result = toFixed; return; }

            float tParam = maxAngle / angle;
            result = math.slerp(from, toFixed, tParam);
        }
    }
}
