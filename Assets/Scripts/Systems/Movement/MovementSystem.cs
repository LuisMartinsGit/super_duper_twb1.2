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
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct MovementSystem : ISystem
    {
        private const float StopDistance = 0.5f;
        private const float DefaultMoveSpeed = 3.5f;
        private const float TurnSpeed = 8f; // radians per second (~460 deg/s)
        private const float MaxWalkableSlope = 0.55f; // terrain slope above this blocks movement
        private const float SlopeCheckStep = 1.5f;    // distance between height samples for slope estimation

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

                // Remove AttackMoveCommand itself - it's been processed
                ecb.RemoveComponent<AttackMoveCommand>(entity);
            }

            // =============================================================================
            // PHASE 2: Move units toward their destinations
            // =============================================================================

            // Read FlowFieldLookup from manager (NativeArray-based, no managed singleton
            // access in the movement loop). Falls back to direct-line if unavailable.
            var ffm = FlowFieldManager.Instance;
            var ffLookup = (ffm != null) ? ffm.CurrentLookup : default;

            foreach (var (xf, dd, entity) in SystemAPI
                .Query<RefRW<LocalTransform>, RefRW<DesiredDestination>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                if (dd.ValueRO.Has == 0) continue;

                // Safety check: buildings should never move
                if (SystemAPI.HasComponent<BuildingTag>(entity))
                {
                    dd.ValueRW.Has = 0;
                    continue;
                }

                // Get move speed: FormationSpeedOverride > MoveSpeed > default
                float speed = DefaultMoveSpeed;
                if (em.HasComponent<FormationSpeedOverride>(entity))
                {
                    var fs = em.GetComponentData<FormationSpeedOverride>(entity).Value;
                    if (fs > 0) speed = fs;
                }
                else if (em.HasComponent<MoveSpeed>(entity))
                {
                    var ms = em.GetComponentData<MoveSpeed>(entity).Value;
                    if (ms > 0) speed = ms;
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
                    // This allows auto-targeting to resume
                    if (em.HasComponent<UserMoveOrder>(entity))
                        ecb.RemoveComponent<UserMoveOrder>(entity);

                    // Remove AttackMoveTag when destination reached
                    // Attack-move is complete, unit becomes idle
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

                // Pre-warm flow field cache for this destination (managed call,
                // but just a dictionary lookup / queue enqueue — not in the hot path).
                // This ensures async generation is triggered for uncached destinations.
                if (ffm != null)
                    ffm.RequestFlowField(goal);

                // Flow-field direction lookup via NativeArray-based FlowFieldLookup
                // (no managed singleton access — uses pre-built lookup struct).
                // Falls back to direct-line if lookup is not valid or field not cached.
                dir = ffLookup.GetDirection(pos, goal, dir, dist);

                var t = xf.ValueRO;

                // Smooth rotation toward movement direction
                float facingDot = 1f;
                if (math.lengthsq(dir) > 1e-8f)
                {
                    float3 fwd = math.normalize(new float3(dir.x, 0f, dir.z));
                    quaternion targetRot = quaternion.RotateY(math.atan2(fwd.x, fwd.z));

                    // Slerp toward target rotation at TurnSpeed
                    float maxTurn = TurnSpeed * dt;
                    SmoothSlerp(in t.Rotation, in targetRot, maxTurn, out var smoothed);
                    t.Rotation = smoothed;

                    // Measure how well unit faces its goal (1 = facing, 0 = perpendicular, -1 = backwards)
                    float3 currentFwd = math.mul(t.Rotation, new float3(0, 0, 1));
                    facingDot = math.dot(new float3(currentFwd.x, 0, currentFwd.z), fwd);
                }

                // Scale movement by facing: full speed when facing goal, reduced when turning
                // Clamp so units still creep forward even while turning (min 20% speed)
                float facingFactor = math.clamp(facingDot, 0.2f, 1f);
                float step = math.min(speed * dt * facingFactor, dist);
                float3 nextPos = pos + dir * step;

                // === PASSABILITY CHECK: avoid stepping onto blocked grid cells ===
                var passGrid = PassabilityGrid.Instance;
                if (passGrid != null)
                {
                    int2 nextCell = passGrid.WorldToCell(nextPos);
                    if (!passGrid.IsPassable(nextCell))
                    {
                        // Blocked — don't move this frame but KEEP destination.
                        // Flow field will provide obstacle-aware direction next frame.
                        continue;
                    }
                }

                // === SLOPE CHECK: block movement onto impassable steep terrain ===
                float hCenter = TerrainUtility.GetHeight(nextPos.x, nextPos.z);
                float hL = TerrainUtility.GetHeight(nextPos.x - SlopeCheckStep, nextPos.z);
                float hR = TerrainUtility.GetHeight(nextPos.x + SlopeCheckStep, nextPos.z);
                float hD = TerrainUtility.GetHeight(nextPos.x, nextPos.z - SlopeCheckStep);
                float hU = TerrainUtility.GetHeight(nextPos.x, nextPos.z + SlopeCheckStep);
                float dX = (hR - hL) / (SlopeCheckStep * 2f);
                float dZ = (hU - hD) / (SlopeCheckStep * 2f);
                float slopeAtNext = math.sqrt(dX * dX + dZ * dZ);

                if (slopeAtNext > MaxWalkableSlope)
                {
                    // Terrain too steep — skip movement this frame but KEEP destination.
                    // Flow field will provide obstacle-aware direction next frame.
                    continue;
                }

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