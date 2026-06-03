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
        private const float MaxWalkableSlope = 0.55f;
        private const float SlopeCheckStep = 1.5f;
        private const float SmoothRate = 12f;

        [BurstCompile]
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
            foreach (var (xf, dd, entity) in SystemAPI
                .Query<RefRW<LocalTransform>, RefRW<DesiredDestination>>()
                .WithAll<UnitTag>()
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
                if (em.HasComponent<Fortified>(entity)) speed = 0f;
                if (em.HasComponent<SpellBuff>(entity))
                {
                    var buff = em.GetComponentData<SpellBuff>(entity);
                    if (buff.SpeedMultiplier > 0f && buff.SpeedMultiplier != 1f)
                        speed *= buff.SpeedMultiplier;
                }
                if (speed <= 0f) continue;

                // Archers in firing range: don't move (RangedCombatSystem owns the action).
                if (em.HasComponent<ArcherTag>(entity)
                    && em.HasComponent<Target>(entity))
                {
                    var tgt = em.GetComponentData<Target>(entity);
                    if (tgt.Value != Entity.Null && em.Exists(tgt.Value)
                        && em.HasComponent<LocalTransform>(tgt.Value)
                        && em.HasComponent<Health>(tgt.Value)
                        && em.GetComponentData<Health>(tgt.Value).Value > 0)
                    {
                        float3 tgtPos = em.GetComponentData<LocalTransform>(tgt.Value).Position;
                        float tgtDist = math.distance(xf.ValueRO.Position, tgtPos);
                        float maxRange = em.HasComponent<ArcherState>(entity)
                            ? em.GetComponentData<ArcherState>(entity).MaxRange : 25f;
                        if (tgtDist <= maxRange) continue;
                    }
                }

                float3 pos = xf.ValueRO.Position;
                float3 goal = dd.ValueRO.Position;

                float3 to = goal - pos;
                to.y = 0f;
                float distSqr = math.lengthsq(to);

                // Arrived.
                if (distSqr <= (StopDistance * StopDistance))
                {
                    dd.ValueRW.Has = 0;
                    if (em.HasComponent<UserMoveOrder>(entity)) ecb.RemoveComponent<UserMoveOrder>(entity);
                    if (em.HasComponent<AttackMoveTag>(entity)) ecb.RemoveComponent<AttackMoveTag>(entity);
                    if (em.HasComponent<FormationSpeedOverride>(entity)) ecb.RemoveComponent<FormationSpeedOverride>(entity);
                    continue;
                }

                float dist = math.sqrt(distSqr);
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
                float step = math.min(speed * dt, dist);
                float3 nextPos = pos + smoothedDir * step;

                // === COST-FIELD PASSABILITY (replaces PassabilityGrid + NavMesh sample gate) ===
                bool blocked = false;
                int2 nextCell = NavGridQuery.WorldToCellInt2(nextPos);
                if (nextCell.x != int.MinValue && !NavGridQuery.IsCellPassable(nextCell, unitLayer))
                {
                    // Only enforce the cost-field block if the unit's current
                    // cell IS passable -- units that spawn inside a building
                    // need to walk OUT through their own footprint.
                    int2 currentCell = NavGridQuery.WorldToCellInt2(pos);
                    if (currentCell.x != int.MinValue && NavGridQuery.IsCellPassable(currentCell, unitLayer))
                        blocked = true;
                }

                // === SLOPE CHECK (ground only — the rampart deck is flat) ===
                if (!blocked && !onRampart)
                {
                    float hL = TerrainUtility.GetHeight(nextPos.x - SlopeCheckStep, nextPos.z);
                    float hR = TerrainUtility.GetHeight(nextPos.x + SlopeCheckStep, nextPos.z);
                    float hD = TerrainUtility.GetHeight(nextPos.x, nextPos.z - SlopeCheckStep);
                    float hU = TerrainUtility.GetHeight(nextPos.x, nextPos.z + SlopeCheckStep);
                    float dX = (hR - hL) / (SlopeCheckStep * 2f);
                    float dZ = (hU - hD) / (SlopeCheckStep * 2f);
                    float slopeAtNext = math.sqrt(dX * dX + dZ * dZ);
                    if (slopeAtNext > MaxWalkableSlope) blocked = true;
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
                                    escapePos.y = TerrainUtility.GetHeight(escapePos.x, escapePos.z);
                                    t.Position = escapePos;
                                    ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });
                                    escaped = true;
                                }
                            }

                            if (!escaped)
                            {
                                dd.ValueRW.Has = 0;
                                if (em.HasComponent<UserMoveOrder>(entity)) ecb.RemoveComponent<UserMoveOrder>(entity);
                                if (em.HasComponent<AttackMoveTag>(entity)) ecb.RemoveComponent<AttackMoveTag>(entity);
                                if (em.HasComponent<FormationSpeedOverride>(entity)) ecb.RemoveComponent<FormationSpeedOverride>(entity);
                                ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });
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
                                    ? LayerTransitionSystem.DeckY
                                    : TerrainUtility.GetHeight(perpPos.x, perpPos.z);
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
                // Layer 0 (Ground) -> terrain height. Layer 1 (Rampart) ->
                // DeckY constant. unitLayer/onRampart resolved at the top of
                // this unit's body.
                if (onRampart)
                    nextPos.y = LayerTransitionSystem.DeckY;
                else
                    nextPos.y = TerrainUtility.GetHeight(nextPos.x, nextPos.z);

                t.Position = nextPos;
                xf.ValueRW = t;
            }
        }

        /// <summary>
        /// Slerp from current to target rotation, clamped to maxAngle radians.
        /// Pure math; safe to mark [BurstCompile] for any future Burst-job
        /// port of the integrator.
        /// </summary>
        [BurstCompile]
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
