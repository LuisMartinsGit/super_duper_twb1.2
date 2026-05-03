// File: Assets/Scripts/Systems/Movement/UnitSeparationSystem.cs
using System.Runtime.CompilerServices;
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
    /// Unit separation with spatial hashing.
    /// Prevents unit overlap/stacking by applying push forces to overlapping units.
    ///
    /// Features:
    /// - NativeParallelMultiHashMap for O(neighbor_count) cell lookups instead of O(all_units)
    /// - Cached EntityQueries to avoid per-frame allocation
    /// - Throttled to 10 updates/sec for performance
    /// - Reduces push force for moving units to avoid jitter
    ///
    /// Runs after MovementSystem to adjust positions after movement.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MovementSystem))]
    public partial struct UnitSeparationSystem : ISystem
    {
        private const float PushForce = 8f;
        private const float MinSeparation = 0.1f;
        private const float UpdateInterval = 0.1f; // 10 updates/sec
        private const float CellSize = 3f; // Grid cell size for spatial hashing
        private const float MaxWalkableSlope = 0.55f; // Must match MovementSystem
        private const float SlopeCheckStep = 1.5f;    // Must match MovementSystem

        private double _lastUpdateTime;

        // Cached queries — built once in OnCreate, reused every frame
        private EntityQuery _unitQuery;
        private EntityQuery _unitQueryIncBattalion;
        private EntityQuery _buildingQuery;
        private EntityQuery _obstacleQuery;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            _lastUpdateTime = 0;

            _unitQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, Radius, UnitTag>()
                .WithNone<BattalionLeader>()
                .Build();

            _unitQueryIncBattalion = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, Radius, UnitTag>()
                .Build();

            _buildingQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, Radius, BuildingTag>()
                .Build();

            _obstacleQuery = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, Radius, ObstacleTag>()
                .Build();
        }

        // Not Burst-compiled: uses managed TerrainUtility.GetHeight for slope checks
        public void OnUpdate(ref SystemState state)
        {
            var currentTime = SystemAPI.Time.ElapsedTime;
            var timeSinceLastUpdate = currentTime - _lastUpdateTime;

            // Throttle updates for performance
            if (timeSinceLastUpdate < UpdateInterval)
            {
                return;
            }

            _lastUpdateTime = currentTime;
            float dt = (float)timeSinceLastUpdate;
            var em = state.EntityManager;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // =============================================================================
            // PHASE 1: Initialize Radius for units that don't have it
            // =============================================================================
            foreach (var (tag, entity) in SystemAPI
                .Query<RefRO<UnitTag>>()
                .WithNone<Radius>()
                .WithEntityAccess())
            {
                ecb.AddComponent(entity, new Radius { Value = 0.5f });
            }

            // =============================================================================
            // PHASE 2: Query all units with required components
            // =============================================================================
            var unitCount = _unitQuery.CalculateEntityCount();
            if (unitCount < 2) return; // Need at least 2 units for separation

            var allUnits = _unitQuery.ToEntityArray(Allocator.Temp);
            var allPositions = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var allRadii = _unitQuery.ToComponentDataArray<Radius>(Allocator.Temp);

            // =============================================================================
            // PHASE 3: Build spatial hash grid using NativeParallelMultiHashMap
            // =============================================================================
            var cellMap = new NativeParallelMultiHashMap<int2, int>(unitCount * 2, Allocator.Temp);

            for (int i = 0; i < allUnits.Length; i++)
            {
                if (!em.Exists(allUnits[i])) continue;

                var pos = allPositions[i].Position;
                GetCellKey(in pos, out int2 cellKey);
                cellMap.Add(cellKey, i);
            }

            // =============================================================================
            // PHASE 4: Process each unit for separation
            // =============================================================================
            for (int i = 0; i < allUnits.Length; i++)
            {
                if (!em.Exists(allUnits[i])) continue;

                var myPos = allPositions[i].Position;
                var myRadius = allRadii[i].Value;
                float3 pushDirection = float3.zero;
                int pushCount = 0;

                GetCellKey(in myPos, out int2 myCell);

                // Check only neighboring cells (3x3 grid around current cell)
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int2 neighborCell = myCell + new int2(dx, dz);

                        // O(neighbor_count) lookup via hash map iterator
                        if (!cellMap.TryGetFirstValue(neighborCell, out int j, out var it))
                            continue;

                        do
                        {
                            if (i == j) continue; // Skip self
                            if (!em.Exists(allUnits[j])) continue;

                            var otherPos = allPositions[j].Position;
                            var otherRadius = allRadii[j].Value;

                            float3 diff = myPos - otherPos;
                            diff.y = 0; // Only separate on XZ plane

                            float distSq = math.lengthsq(diff);
                            float minDist = myRadius + otherRadius + MinSeparation;
                            float minDistSq = minDist * minDist;

                            // Check for overlap
                            if (distSq < minDistSq && distSq > 0.0001f)
                            {
                                float dist = math.sqrt(distSq);
                                float3 pushDir = diff / dist;
                                float overlap = minDist - dist;

                                pushDirection += pushDir * overlap;
                                pushCount++;
                            }
                        } while (cellMap.TryGetNextValue(out j, ref it));
                    }
                }

                // Apply separation push if overlapping with other units
                if (pushCount > 0)
                {
                    pushDirection /= pushCount;

                    // Check if unit is currently moving (reduce push to avoid jitter)
                    bool isMoving = false;
                    if (em.HasComponent<DesiredDestination>(allUnits[i]))
                    {
                        var dd = em.GetComponentData<DesiredDestination>(allUnits[i]);
                        isMoving = dd.Has != 0;
                    }

                    // Reduce push force for moving units to prevent jitter
                    float pushMultiplier = isMoving ? 0.3f : 1.0f;
                    float3 newPos = myPos + pushDirection * PushForce * dt * pushMultiplier;

                    // === SLOPE CHECK: don't push units onto impassable terrain ===
                    if (IsSlopeTooSteep(newPos))
                        continue; // Skip this push — terrain is impassable

                    var transform = em.GetComponentData<LocalTransform>(allUnits[i]);
                    transform.Position = newPos;
                    em.SetComponentData(allUnits[i], transform);
                }
            }

            // =============================================================================
            // PHASE 5: Push units out of buildings (buildings are immovable obstacles)
            // Builders assigned to a construction site are exempt from push by that building.
            // =============================================================================
            var buildingCount = _buildingQuery.CalculateEntityCount();
            if (buildingCount > 0)
            {
                var buildingEntities = _buildingQuery.ToEntityArray(Allocator.Temp);
                var buildingPositions = _buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                var buildingRadii = _buildingQuery.ToComponentDataArray<Radius>(Allocator.Temp);

                // Re-query units to get updated positions (after unit-unit separation)
                var units2 = _unitQuery.ToEntityArray(Allocator.Temp);
                var unitPos2 = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                var unitRad2 = _unitQuery.ToComponentDataArray<Radius>(Allocator.Temp);

                for (int i = 0; i < units2.Length; i++)
                {
                    if (!em.Exists(units2[i])) continue;

                    // Check if this unit is a builder assigned to a construction site
                    Entity assignedSite = Entity.Null;
                    if (em.HasComponent<BuildOrder>(units2[i]))
                        assignedSite = em.GetComponentData<BuildOrder>(units2[i]).Site;
                    else if (em.HasComponent<BuildCommand>(units2[i]))
                        assignedSite = em.GetComponentData<BuildCommand>(units2[i]).TargetBuilding;

                    var myPos = unitPos2[i].Position;
                    var myRadius = unitRad2[i].Value;
                    float3 correctedPos = myPos;

                    for (int b = 0; b < buildingCount; b++)
                    {
                        // Skip push from the building this builder is assigned to construct
                        if (assignedSite != Entity.Null && buildingEntities[b] == assignedSite)
                            continue;

                        var bPos = buildingPositions[b].Position;

                        if (em.HasComponent<BuildingSize>(buildingEntities[b]))
                        {
                            // AABB-based push for buildings with explicit grid size
                            var bSize = em.GetComponentData<BuildingSize>(buildingEntities[b]);
                            float halfW = bSize.Width / 2f + myRadius + MinSeparation;
                            float halfH = bSize.Height / 2f + myRadius + MinSeparation;

                            float dx = correctedPos.x - bPos.x;
                            float dz = correctedPos.z - bPos.z;

                            // Handle exact-center case: default push in +X direction
                            if (math.abs(dx) < 0.0001f && math.abs(dz) < 0.0001f)
                                dx = 0.0001f;

                            if (math.abs(dx) < halfW && math.abs(dz) < halfH)
                            {
                                // Push along the shorter axis. Earlier missing
                                // braces meant Z was always assigned regardless
                                // of pushX/pushZ — units always got popped to a
                                // building corner (Z-dominant) instead of the
                                // nearest edge, scraping along the side.
                                // (task-053 F-3 / MB-3)
                                float pushX = halfW - math.abs(dx);
                                float pushZ = halfH - math.abs(dz);
                                if (pushX < pushZ)
                                    correctedPos.x = bPos.x + math.sign(dx) * halfW;
                                else
                                    correctedPos.z = bPos.z + math.sign(dz) * halfH;
                                correctedPos.y = myPos.y;
                            }
                        }
                        else
                        {
                            // Circular push for buildings without BuildingSize
                            var bRadius = buildingRadii[b].Value;

                            float3 diff = correctedPos - bPos;
                            diff.y = 0;

                            float distSq = math.lengthsq(diff);
                            float minDist = myRadius + bRadius + MinSeparation;

                            if (distSq < minDist * minDist)
                            {
                                float dist = math.sqrt(math.max(distSq, 0.0001f));
                                float3 pushDir = dist > 0.001f ? diff / dist : new float3(1, 0, 0);
                                // Snap directly to the edge - no overshoot
                                correctedPos = bPos + pushDir * minDist;
                                correctedPos.y = myPos.y;
                            }
                        }
                    }

                    if (math.lengthsq(correctedPos - myPos) > 0.0001f)
                    {
                        // Don't push units onto impassable slopes
                        if (IsSlopeTooSteep(correctedPos)) continue;

                        var transform = em.GetComponentData<LocalTransform>(units2[i]);
                        transform.Position = correctedPos;
                        em.SetComponentData(units2[i], transform);
                    }
                }

                buildingEntities.Dispose();
                buildingPositions.Dispose();
                buildingRadii.Dispose();
                units2.Dispose();
                unitPos2.Dispose();
                unitRad2.Dispose();
            }

            // =============================================================================
            // PHASE 5b: Push units out of obstacles (forests, rocks)
            // Same logic as building push, but queries ObstacleTag instead.
            // =============================================================================
            var obstacleCount = _obstacleQuery.CalculateEntityCount();
            if (obstacleCount > 0)
            {
                var obstacleEntities = _obstacleQuery.ToEntityArray(Allocator.Temp);
                var obstaclePositions = _obstacleQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                var obstacleRadii = _obstacleQuery.ToComponentDataArray<Radius>(Allocator.Temp);

                // Re-query units to get positions after building push.
                // Battalion members are now included — individual tree obstacles
                // (radius 0.75) are small enough to push without forcefield scattering.
                // The old exclusion was needed when forests were single 12-radius obstacles.
                var units3 = _unitQueryIncBattalion.ToEntityArray(Allocator.Temp);
                var unitPos3 = _unitQueryIncBattalion.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                var unitRad3 = _unitQueryIncBattalion.ToComponentDataArray<Radius>(Allocator.Temp);

                for (int i = 0; i < units3.Length; i++)
                {
                    if (!em.Exists(units3[i])) continue;

                    var myPos = unitPos3[i].Position;
                    var myRadius = unitRad3[i].Value;
                    float3 correctedPos = myPos;

                    for (int o = 0; o < obstacleCount; o++)
                    {
                        var oPos = obstaclePositions[o].Position;
                        var oRadius = obstacleRadii[o].Value;

                        float3 diff = correctedPos - oPos;
                        diff.y = 0;

                        float distSq = math.lengthsq(diff);
                        float minDist = myRadius + oRadius + MinSeparation;

                        if (distSq < minDist * minDist)
                        {
                            float dist = math.sqrt(math.max(distSq, 0.0001f));
                            float3 pushDir = dist > 0.001f ? diff / dist : new float3(1, 0, 0);
                            // Snap directly to the edge
                            correctedPos = oPos + pushDir * minDist;
                            correctedPos.y = myPos.y;
                        }
                    }

                    if (math.lengthsq(correctedPos - myPos) > 0.0001f)
                    {
                        // Don't push units onto impassable slopes
                        if (IsSlopeTooSteep(correctedPos)) continue;

                        var transform = em.GetComponentData<LocalTransform>(units3[i]);
                        transform.Position = correctedPos;
                        em.SetComponentData(units3[i], transform);
                    }
                }

                obstacleEntities.Dispose();
                obstaclePositions.Dispose();
                obstacleRadii.Dispose();
                units3.Dispose();
                unitPos3.Dispose();
                unitRad3.Dispose();
            }

            // =============================================================================
            // PHASE 6: Cleanup
            // =============================================================================
            cellMap.Dispose();
            allUnits.Dispose();
            allPositions.Dispose();
            allRadii.Dispose();
        }

        /// <summary>
        /// Convert world position to spatial grid cell key.
        /// Uses out parameter instead of return to satisfy Burst constraints.
        /// </summary>
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void GetCellKey(in float3 position, out int2 result)
        {
            result = new int2(
                (int)math.floor(position.x / CellSize),
                (int)math.floor(position.z / CellSize)
            );
        }

        /// <summary>
        /// Check if terrain slope at a position exceeds walkable limit.
        /// Uses the same algorithm as MovementSystem for consistency.
        /// NOTE: Not Burst-compatible (calls managed TerrainUtility) but
        /// UnitSeparationSystem is already not fully Burst-compiled due to
        /// EntityManager calls, so this is acceptable.
        /// </summary>
        private static bool IsSlopeTooSteep(float3 pos)
        {
            float hL = TerrainUtility.GetHeight(pos.x - SlopeCheckStep, pos.z);
            float hR = TerrainUtility.GetHeight(pos.x + SlopeCheckStep, pos.z);
            float hD = TerrainUtility.GetHeight(pos.x, pos.z - SlopeCheckStep);
            float hU = TerrainUtility.GetHeight(pos.x, pos.z + SlopeCheckStep);
            float dX = (hR - hL) / (SlopeCheckStep * 2f);
            float dZ = (hU - hD) / (SlopeCheckStep * 2f);
            float slope = math.sqrt(dX * dX + dZ * dZ);
            return slope > MaxWalkableSlope;
        }
    }
}
