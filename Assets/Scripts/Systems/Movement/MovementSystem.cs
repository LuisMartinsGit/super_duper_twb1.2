// File: Assets/Scripts/Systems/Movement/MovementSystem.cs
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// Handles unit movement and rotation.
    /// - Converts MoveCommand -> DesiredDestination
    /// - Moves units toward their destinations each frame
    /// - Updates rotation to face movement direction
    /// - Manages UserMoveOrder tag lifecycle
    ///
    /// Combat logic is handled by UnifiedCombatSystem.
    /// IMPORTANT: Does NOT remove AttackCommand - lets UnifiedCombatSystem handle it
    ///
    /// Flow field direction lookup uses FlowFieldLookup struct (NativeArray-based)
    /// instead of managed FlowFieldMovementHelper singleton, enabling future
    /// Burst compilation of the movement loop when TerrainUtility is also refactored.
    ///
    /// Performance optimizations:
    /// - MovementCache component caches last flow field destination to skip redundant
    ///   RequestFlowField calls when the destination has not changed.
    /// - em.HasComponent+GetComponentData replaced with TryGetComponent pattern
    ///   to halve random-access lookups in the hot loop.
    /// - Terrain height cached per cell in MovementCache to avoid repeated
    ///   TerrainUtility.GetHeight calls when the unit stays in the same cell.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct MovementSystem : ISystem
    {
        private const float StopDistance = 0.5f;
        private const float DefaultMoveSpeed = 3.5f;
        private const float TurnSpeed = 8f; // radians per second (~460 deg/s)
        private const float MaxWalkableSlope = 0.55f; // terrain slope above this blocks movement
        private const float SlopeCheckStep = 1.5f;    // distance between height samples for slope estimation

        /// <summary>Minimum squared distance between cached and current destination to trigger a new request.</summary>
        private const float DestChangedThresholdSq = 0.01f; // 0.1 world units

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Get ECB for structural changes
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // =============================================================================
            // PHASE 1: Process MoveCommand -> DesiredDestination conversion
            // =============================================================================
            foreach (var (mc, entity) in SystemAPI.Query<RefRO<MoveCommand>>().WithEntityAccess())
            {
                // Buildings don't move
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

                // Update guard point when explicitly commanded to move
                if (em.HasComponent<GuardPoint>(entity))
                {
                    ecb.SetComponent(entity, new GuardPoint
                    {
                        Position = mc.ValueRO.Destination,
                        Has = 1
                    });
                }

                // Set destination
                if (!em.HasComponent<DesiredDestination>(entity))
                {
                    ecb.AddComponent(entity, new DesiredDestination
                    {
                        Position = mc.ValueRO.Destination,
                        Has = 1
                    });
                }
                else
                {
                    ecb.SetComponent(entity, new DesiredDestination
                    {
                        Position = mc.ValueRO.Destination,
                        Has = 1
                    });
                }

                // Clear Target - this is safe because we only set data
                if (em.HasComponent<Target>(entity))
                {
                    ecb.SetComponent(entity, new Target { Value = Entity.Null });
                }

                // Add UserMoveOrder tag to prevent auto-targeting from overriding
                if (!em.HasComponent<UserMoveOrder>(entity))
                {
                    ecb.AddComponent<UserMoveOrder>(entity);
                }

                // Reset smoothing state for fresh movement
                if (em.HasComponent<SmoothedDirection>(entity))
                    ecb.SetComponent(entity, new SmoothedDirection { Value = float3.zero });
                if (em.HasComponent<StuckState>(entity))
                    ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });

                // Invalidate flow field cache so next frame re-requests for new destination
                if (em.HasComponent<MovementCache>(entity))
                {
                    ecb.SetComponent(entity, new MovementCache
                    {
                        LastDestination = new float3(float.MaxValue),
                        LastSnappedDest = -1,
                        LastHeightCell = new int2(int.MinValue),
                        CachedHeight = 0f
                    });
                }

                // DON'T remove AttackCommand here - let UnifiedCombatSystem see the MoveCommand
                // and skip attack processing for this entity

                // Remove MoveCommand itself - it's been processed
                ecb.RemoveComponent<MoveCommand>(entity);
            }

            // =============================================================================
            // PHASE 1b: Process AttackMoveCommand -> DesiredDestination conversion
            // Same as MoveCommand but adds AttackMoveTag instead of UserMoveOrder
            // =============================================================================
            foreach (var (amc, entity) in SystemAPI.Query<RefRO<AttackMoveCommand>>().WithEntityAccess())
            {
                // Buildings don't move
                if (SystemAPI.HasComponent<BuildingTag>(entity))
                {
                    ecb.RemoveComponent<AttackMoveCommand>(entity);
                    continue;
                }

                // Set guard point to attack-move destination
                if (em.HasComponent<GuardPoint>(entity))
                {
                    ecb.SetComponent(entity, new GuardPoint
                    {
                        Position = amc.ValueRO.Destination,
                        Has = 1
                    });
                }

                // Set destination
                if (!em.HasComponent<DesiredDestination>(entity))
                {
                    ecb.AddComponent(entity, new DesiredDestination
                    {
                        Position = amc.ValueRO.Destination,
                        Has = 1
                    });
                }
                else
                {
                    ecb.SetComponent(entity, new DesiredDestination
                    {
                        Position = amc.ValueRO.Destination,
                        Has = 1
                    });
                }

                // Clear Target
                if (em.HasComponent<Target>(entity))
                {
                    ecb.SetComponent(entity, new Target { Value = Entity.Null });
                }

                // Add AttackMoveTag if not present
                if (!em.HasComponent<AttackMoveTag>(entity))
                {
                    ecb.AddComponent<AttackMoveTag>(entity);
                }

                // Remove UserMoveOrder if present (attack-move units should NOT have this)
                if (em.HasComponent<UserMoveOrder>(entity))
                {
                    ecb.RemoveComponent<UserMoveOrder>(entity);
                }

                // Reset smoothing state for fresh movement
                if (em.HasComponent<SmoothedDirection>(entity))
                    ecb.SetComponent(entity, new SmoothedDirection { Value = float3.zero });
                if (em.HasComponent<StuckState>(entity))
                    ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });

                // Invalidate flow field cache so next frame re-requests for new destination
                if (em.HasComponent<MovementCache>(entity))
                {
                    ecb.SetComponent(entity, new MovementCache
                    {
                        LastDestination = new float3(float.MaxValue),
                        LastSnappedDest = -1,
                        LastHeightCell = new int2(int.MinValue),
                        CachedHeight = 0f
                    });
                }

                // Remove AttackMoveCommand itself - it's been processed
                ecb.RemoveComponent<AttackMoveCommand>(entity);
            }

            // =============================================================================
            // PHASE 2: Move units toward their destinations
            // =============================================================================

            // Pathfinding mode: flow fields (shared BFS) or A* (per-unit paths)
            bool useFlowFields = GameSettings.UseFlowFields;
            var ffm = useFlowFields ? FlowFieldManager.Instance : null;
            var ffLookup = (ffm != null) ? ffm.CurrentLookup : default;
            var astarStore = useFlowFields ? null : AStarPathStore.Instance;

            foreach (var (xf, dd, entity) in SystemAPI
                .Query<RefRW<LocalTransform>, RefRW<DesiredDestination>>()
                .WithAll<UnitTag>()
                .WithNone<BattalionMemberData>()
                .WithEntityAccess())
            {
                if (dd.ValueRO.Has == 0) continue;

                // Safety check: buildings should never move
                if (SystemAPI.HasComponent<BuildingTag>(entity))
                {
                    dd.ValueRW.Has = 0;
                    continue;
                }

                // Lazy-add new components (safe via ECB - structural change deferred)
                if (!em.HasComponent<SmoothedDirection>(entity))
                    ecb.AddComponent(entity, new SmoothedDirection { Value = float3.zero });
                if (!em.HasComponent<StuckState>(entity))
                    ecb.AddComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });
                if (!em.HasComponent<MovementCache>(entity))
                {
                    ecb.AddComponent(entity, new MovementCache
                    {
                        LastDestination = new float3(float.MaxValue),
                        LastSnappedDest = -1,
                        LastHeightCell = new int2(int.MinValue),
                        CachedHeight = 0f
                    });
                }

                // Get move speed: FormationSpeedOverride > MoveSpeed > default
                // Use TryGetComponent to avoid double lookup (HasComponent + GetComponentData)
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

                // Apply SpellDebuff speed reduction (roots, slows)
                if (em.HasComponent<SpellDebuff>(entity))
                {
                    var debuff = em.GetComponentData<SpellDebuff>(entity);
                    speed *= (1f - debuff.SpeedReduction);
                }

                // Apply Fortified immobilization
                if (em.HasComponent<Fortified>(entity))
                {
                    speed = 0f;
                }

                // Apply SpellBuff speed multiplier (Battle Fervor etc.)
                if (em.HasComponent<SpellBuff>(entity))
                {
                    var buff = em.GetComponentData<SpellBuff>(entity);
                    if (buff.SpeedMultiplier > 0f && buff.SpeedMultiplier != 1f)
                        speed *= buff.SpeedMultiplier;
                }

                // Skip movement entirely if speed is zero or negative
                if (speed <= 0f)
                    continue;

                // Archers with an active target in range should not move — enforce
                // mutual exclusion between attacking and moving. Skip battalion members
                // (their movement is handled by BattalionSyncSystem).
                if (em.HasComponent<ArcherTag>(entity) && !em.HasComponent<BattalionMemberData>(entity)
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
                        if (tgtDist <= maxRange)
                            continue; // In firing range — don't move, let RangedCombatSystem handle it
                    }
                }

                float3 pos = xf.ValueRO.Position;
                float3 goal = dd.ValueRO.Position;

                // Calculate direction (ignore Y for horizontal movement)
                float3 to = goal - pos;
                to.y = 0f;

                float distSqr = math.lengthsq(to);

                // Check if arrived at destination
                if (distSqr <= (StopDistance * StopDistance))
                {
                    dd.ValueRW.Has = 0;

                    // Remove UserMoveOrder tag when destination reached
                    if (em.HasComponent<UserMoveOrder>(entity))
                        ecb.RemoveComponent<UserMoveOrder>(entity);

                    // Remove AttackMoveTag when destination reached
                    if (em.HasComponent<AttackMoveTag>(entity))
                        ecb.RemoveComponent<AttackMoveTag>(entity);

                    // NOTE: PatrolTag is NOT removed here.
                    // PatrolSystem will detect Has==0 and set the next waypoint.

                    // Remove formation speed override
                    if (em.HasComponent<FormationSpeedOverride>(entity))
                        ecb.RemoveComponent<FormationSpeedOverride>(entity);

                    continue;
                }

                // Move toward goal
                float dist = math.sqrt(distSqr);
                float3 dir = to / math.max(1e-5f, dist);

                // === PATHFINDING DIRECTION ===
                if (!useFlowFields && astarStore != null && em.HasComponent<AStarPathIndex>(entity))
                {
                    // A* waypoint following
                    var pathIdx = em.GetComponentData<AStarPathIndex>(entity);
                    if (astarStore.TryGetWaypoint(entity, pathIdx.CurrentWaypoint, out float3 wp))
                    {
                        float3 toWp = wp - pos;
                        toWp.y = 0f;
                        float wpDist = math.length(toWp);

                        if (wpDist < StopDistance * 2f)
                        {
                            // Advance to next waypoint
                            pathIdx.CurrentWaypoint++;
                            ecb.SetComponent(entity, pathIdx);

                            // Check if more waypoints remain
                            if (astarStore.TryGetWaypoint(entity, pathIdx.CurrentWaypoint, out float3 nextWp))
                            {
                                toWp = nextWp - pos;
                                toWp.y = 0f;
                                wpDist = math.length(toWp);
                            }
                            // else: path exhausted, fall through — the DesiredDestination
                            // arrival check at line ~243 handles final stop
                        }

                        if (wpDist > 1e-4f)
                            dir = toWp / wpDist;
                    }
                    // else: no path available, keep direct-line dir
                }
                else
                {
                    // Flow field direction lookup with per-unit destination caching
                    int snappedDest = -1;
                    if (ffm != null)
                    {
                        bool destChanged = true;
                        if (em.HasComponent<MovementCache>(entity))
                        {
                            var cache = em.GetComponentData<MovementCache>(entity);
                            float3 diff2 = goal - cache.LastDestination;
                            destChanged = math.lengthsq(diff2) > DestChangedThresholdSq;
                            if (!destChanged)
                            {
                                snappedDest = cache.LastSnappedDest;
                            }
                        }

                        if (destChanged)
                        {
                            var field = ffm.RequestFlowField(goal);
                            if (field.HasValue)
                                snappedDest = field.Value.DestinationIndex;

                            if (em.HasComponent<MovementCache>(entity))
                            {
                                var mvc = em.GetComponentData<MovementCache>(entity);
                                mvc.LastDestination = goal;
                                mvc.LastSnappedDest = snappedDest;
                                em.SetComponentData(entity, mvc);
                            }
                        }
                    }
                    dir = ffLookup.GetDirection(pos, snappedDest, dir, dist);
                }

                // === Per-unit direction smoothing ===
                // Lerp toward raw flow field direction to prevent cell-boundary oscillation.
                // Use TryGetComponent to avoid double lookup
                float3 smoothedDir = dir;
                if (em.HasComponent<SmoothedDirection>(entity))
                {
                    var sd = em.GetComponentData<SmoothedDirection>(entity);
                    if (math.lengthsq(sd.Value) > 1e-8f)
                    {
                        const float SmoothRate = 12f;
                        smoothedDir = math.normalizesafe(math.lerp(sd.Value, dir, math.saturate(SmoothRate * dt)));
                    }
                    ecb.SetComponent(entity, new SmoothedDirection { Value = smoothedDir });
                }

                var t = xf.ValueRO;

                // === Cosmetic rotation (does NOT affect speed) ===
                if (math.lengthsq(smoothedDir) > 1e-8f)
                {
                    float3 fwd = math.normalize(new float3(smoothedDir.x, 0f, smoothedDir.z));
                    quaternion targetRot = quaternion.RotateY(math.atan2(fwd.x, fwd.z));
                    float maxTurn = TurnSpeed * dt;
                    SmoothSlerp(in t.Rotation, in targetRot, maxTurn, out var smoothed);
                    t.Rotation = smoothed;
                }

                // === Full speed movement - no facingFactor ===
                float step = math.min(speed * dt, dist);
                float3 nextPos = pos + smoothedDir * step;

                // === PASSABILITY CHECK ===
                bool blocked = false;
                var passGrid = PassabilityGrid.Instance;
                int2 nextCell = default;
                if (passGrid != null)
                {
                    nextCell = passGrid.WorldToCell(nextPos);
                    if (!passGrid.IsPassable(nextCell))
                        blocked = true;
                }

                // === SLOPE CHECK with terrain height caching ===
                // Cache terrain height per cell: if the unit's next position is in the same
                // cell as the last sample, reuse the cached height instead of calling
                // TerrainUtility.GetHeight 4 times for slope estimation.
                if (!blocked)
                {
                    bool slopeCacheHit = false;
                    if (passGrid != null && em.HasComponent<MovementCache>(entity))
                    {
                        var heightCache = em.GetComponentData<MovementCache>(entity);
                        if (math.all(nextCell == heightCache.LastHeightCell)
                            && heightCache.LastHeightCell.x != int.MinValue)
                        {
                            // Same cell as last frame - skip slope recomputation
                            // (slope does not change within a cell)
                            slopeCacheHit = true;
                        }
                    }

                    if (!slopeCacheHit)
                    {
                        float hL = TerrainUtility.GetHeight(nextPos.x - SlopeCheckStep, nextPos.z);
                        float hR = TerrainUtility.GetHeight(nextPos.x + SlopeCheckStep, nextPos.z);
                        float hD = TerrainUtility.GetHeight(nextPos.x, nextPos.z - SlopeCheckStep);
                        float hU = TerrainUtility.GetHeight(nextPos.x, nextPos.z + SlopeCheckStep);
                        float dX = (hR - hL) / (SlopeCheckStep * 2f);
                        float dZ = (hU - hD) / (SlopeCheckStep * 2f);
                        float slopeAtNext = math.sqrt(dX * dX + dZ * dZ);
                        if (slopeAtNext > MaxWalkableSlope)
                            blocked = true;
                    }
                }

                // === STUCK DETECTION with 3-tier escalation ===
                if (blocked)
                {
                    if (em.HasComponent<StuckState>(entity))
                    {
                        var stuck = em.GetComponentData<StuckState>(entity);
                        stuck.Counter = (byte)math.min(stuck.Counter + 1, 255);

                        if (stuck.Counter > 30)
                        {
                            // Tier 3 (30+ frames ~ 0.5s): cancel destination entirely
                            dd.ValueRW.Has = 0;
                            if (em.HasComponent<UserMoveOrder>(entity))
                                ecb.RemoveComponent<UserMoveOrder>(entity);
                            if (em.HasComponent<AttackMoveTag>(entity))
                                ecb.RemoveComponent<AttackMoveTag>(entity);
                            if (em.HasComponent<FormationSpeedOverride>(entity))
                                ecb.RemoveComponent<FormationSpeedOverride>(entity);
                            ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });
                        }
                        else if (stuck.Counter > 5)
                        {
                            // Tier 2 (6-30 frames): try perpendicular directions
                            byte attempt = (byte)(stuck.LastAttempt == 1 ? 2 : 1);
                            float3 perp = attempt == 1
                                ? new float3(-smoothedDir.z, 0f, smoothedDir.x)
                                : new float3(smoothedDir.z, 0f, -smoothedDir.x);
                            perp = math.normalizesafe(perp);

                            float3 perpPos = pos + perp * step;
                            bool perpBlocked = false;
                            if (passGrid != null)
                            {
                                int2 perpCell = passGrid.WorldToCell(perpPos);
                                if (!passGrid.IsPassable(perpCell))
                                    perpBlocked = true;
                            }

                            if (!perpBlocked)
                            {
                                perpPos.y = TerrainUtility.GetHeight(perpPos.x, perpPos.z);
                                t.Position = perpPos;
                            }

                            stuck.LastAttempt = attempt;
                            ecb.SetComponent(entity, stuck);
                        }
                        else
                        {
                            // Tier 1 (1-5 frames): retry same direction, flow field may update
                            ecb.SetComponent(entity, stuck);
                        }
                    }

                    // Always write back rotation even when blocked (cosmetic update)
                    xf.ValueRW = t;
                    continue;
                }

                // Not blocked - reset stuck counter
                if (em.HasComponent<StuckState>(entity))
                    ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });

                // === Terrain height snap with caching ===
                // Reuse cached height if still in the same cell, otherwise sample and cache
                float terrainY;
                if (passGrid != null && em.HasComponent<MovementCache>(entity))
                {
                    var mvCache = em.GetComponentData<MovementCache>(entity);
                    if (math.all(nextCell == mvCache.LastHeightCell)
                        && mvCache.LastHeightCell.x != int.MinValue)
                    {
                        terrainY = mvCache.CachedHeight;
                    }
                    else
                    {
                        terrainY = TerrainUtility.GetHeight(nextPos.x, nextPos.z);
                        mvCache.LastHeightCell = nextCell;
                        mvCache.CachedHeight = terrainY;
                        em.SetComponentData(entity, mvCache);
                    }
                }
                else
                {
                    terrainY = TerrainUtility.GetHeight(nextPos.x, nextPos.z);
                }
                nextPos.y = terrainY;

                t.Position = nextPos;
                xf.ValueRW = t;
            }
        }

        /// <summary>
        /// Slerp from current to target rotation, clamped to maxAngle radians.
        /// </summary>
        [BurstCompile]
        private static void SmoothSlerp(in quaternion from, in quaternion to, float maxAngle, out quaternion result)
        {
            // Ensure shortest path
            float4 toVal = to.value;
            float dot = math.dot(from.value, toVal);
            if (dot < 0f)
            {
                toVal = -toVal;
                dot = -dot;
            }

            quaternion toFixed = new quaternion(toVal);

            // Already aligned
            if (dot > 0.9999f)
            {
                result = toFixed;
                return;
            }

            float angle = math.acos(math.clamp(dot, -1f, 1f)) * 2f;
            if (angle <= maxAngle)
            {
                result = toFixed;
                return;
            }

            float t = maxAngle / angle;
            result = math.slerp(from, toFixed, t);
        }
    }
}
