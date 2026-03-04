// File: Assets/Scripts/Bootstrap/ObstacleBootstrap.cs
// Spawns forest clusters and rock formations as navigation obstacles.
// Follows the same pattern as CreatureBootstrap: random positions with
// terrain height/slope checks and minimum distance from player bases.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns forest and rock obstacle entities on the terrain at game start.
    /// Forests prefer flat lowland, rocks prefer slopes and highlands.
    /// All obstacles block unit movement via UnitSeparationSystem.
    /// </summary>
    public static class ObstacleBootstrap
    {
        // Presentation IDs (must match PresentationSpawnSystem)
        public const int ForestPresentationId = 400;
        public const int RockPresentationId = 401;

        // Forest settings
        private const int MinForestClusters = 8;
        private const int MaxForestClusters = 14;
        private const float ForestRadius = 5f;
        private const float ForestMinHeight = 25f;
        private const float ForestMaxHeight = 45f;
        private const float ForestMaxSlope = 0.2f;
        private const float ForestMinDistFromPlayers = 50f;
        private const float ForestMinDistFromOther = 15f;

        // Rock settings
        private const int MinRockFormations = 6;
        private const int MaxRockFormations = 10;
        private const float RockRadius = 3f;
        private const float RockMinHeight = 35f;
        private const float RockMaxHeight = 65f;
        private const float RockPreferredMinSlope = 0.1f; // Rocks prefer slopes
        private const float RockMinDistFromPlayers = 40f;
        private const float RockMinDistFromOther = 10f;

        /// <summary>
        /// Main entry point. Call after terrain and player spawns are initialized.
        /// </summary>
        public static void SpawnObstacles()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[ObstacleBootstrap] No ECS World available!");
                return;
            }

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(System.DateTime.Now.Ticks ^ 0xBEEF));

            var playerPositions = GetPlayerPositions(em);
            int half = GameSettings.MapHalfSize;
            float spawnRange = half * 0.8f;

            // Track placed obstacle positions to avoid overlap
            var placedPositions = new Unity.Collections.NativeList<float3>(32, Unity.Collections.Allocator.Temp);

            // === SPAWN FORESTS ===
            int forestCount = random.NextInt(MinForestClusters, MaxForestClusters + 1);
            int forestsSpawned = 0;

            for (int i = 0; i < forestCount; i++)
            {
                if (TryFindPosition(
                    ref random, spawnRange, playerPositions, placedPositions,
                    ForestMinHeight, ForestMaxHeight, ForestMaxSlope, float.MinValue,
                    ForestMinDistFromPlayers, ForestMinDistFromOther,
                    out float3 pos))
                {
                    CreateObstacleEntity(em, pos, ForestRadius, ForestPresentationId);
                    placedPositions.Add(pos);
                    forestsSpawned++;
                }
            }

            // === SPAWN ROCKS ===
            int rockCount = random.NextInt(MinRockFormations, MaxRockFormations + 1);
            int rocksSpawned = 0;

            for (int i = 0; i < rockCount; i++)
            {
                if (TryFindPosition(
                    ref random, spawnRange, playerPositions, placedPositions,
                    RockMinHeight, RockMaxHeight, float.MaxValue, RockPreferredMinSlope,
                    RockMinDistFromPlayers, RockMinDistFromOther,
                    out float3 pos))
                {
                    CreateObstacleEntity(em, pos, RockRadius, RockPresentationId);
                    placedPositions.Add(pos);
                    rocksSpawned++;
                }
            }

            placedPositions.Dispose();

            Debug.Log($"[ObstacleBootstrap] Spawned {forestsSpawned} forests + {rocksSpawned} rock formations");
        }

        /// <summary>
        /// Try up to 20 random positions to find one matching height, slope, and distance constraints.
        /// </summary>
        private static bool TryFindPosition(
            ref Unity.Mathematics.Random random,
            float spawnRange,
            float3[] playerPositions,
            Unity.Collections.NativeList<float3> placedPositions,
            float minHeight, float maxHeight,
            float maxSlope, float preferredMinSlope,
            float minDistFromPlayers, float minDistFromOther,
            out float3 result)
        {
            result = float3.zero;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                float x = random.NextFloat(-spawnRange, spawnRange);
                float z = random.NextFloat(-spawnRange, spawnRange);
                float y = TerrainUtility.GetHeight(x, z);
                float3 candidate = new float3(x, y, z);

                // Check not in water
                var terrain = ProceduralTerrain.Instance;
                if (terrain != null && terrain.IsInWater(new Vector3(x, y, z)))
                    continue;

                // Check height range
                if (y < minHeight || y > maxHeight)
                    continue;

                // Estimate slope from neighboring samples
                float step = 2f;
                float hL = TerrainUtility.GetHeight(x - step, z);
                float hR = TerrainUtility.GetHeight(x + step, z);
                float hD = TerrainUtility.GetHeight(x, z - step);
                float hU = TerrainUtility.GetHeight(x, z + step);
                float dX = (hR - hL) / (step * 2f);
                float dZ = (hU - hD) / (step * 2f);
                float slope = math.sqrt(dX * dX + dZ * dZ);

                // Check slope constraints
                if (maxSlope < float.MaxValue && slope > maxSlope)
                    continue;
                if (preferredMinSlope > float.MinValue && slope < preferredMinSlope)
                {
                    // Soft preference: 50% chance to accept even without slope
                    if (random.NextFloat() > 0.5f)
                        continue;
                }

                // Check distance from player positions
                bool tooCloseToPlayer = false;
                for (int p = 0; p < playerPositions.Length; p++)
                {
                    if (math.distance(candidate, playerPositions[p]) < minDistFromPlayers)
                    {
                        tooCloseToPlayer = true;
                        break;
                    }
                }
                if (tooCloseToPlayer) continue;

                // Check distance from already-placed obstacles
                bool tooCloseToOther = false;
                for (int o = 0; o < placedPositions.Length; o++)
                {
                    if (math.distance(candidate, placedPositions[o]) < minDistFromOther)
                    {
                        tooCloseToOther = true;
                        break;
                    }
                }
                if (tooCloseToOther) continue;

                result = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Create an obstacle ECS entity with the minimal components needed.
        /// </summary>
        private static Entity CreateObstacleEntity(EntityManager em, float3 position, float radius, int presentationId)
        {
            var entity = em.CreateEntity(
                typeof(ObstacleTag),
                typeof(LocalTransform),
                typeof(Radius),
                typeof(PresentationId)
            );

            em.SetComponentData(entity, LocalTransform.FromPosition(position));
            em.SetComponentData(entity, new Radius { Value = radius });
            em.SetComponentData(entity, new PresentationId { Id = presentationId });

            return entity;
        }

        /// <summary>
        /// Get player positions from existing Halls, or estimate from spawn layout.
        /// Same pattern as CreatureBootstrap.GetPlayerPositions().
        /// </summary>
        private static float3[] GetPlayerPositions(EntityManager em)
        {
            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<LocalTransform>()
            );

            using var hallTransforms = hallQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

            if (hallTransforms.Length > 0)
            {
                var positions = new float3[hallTransforms.Length];
                for (int i = 0; i < hallTransforms.Length; i++)
                    positions[i] = hallTransforms[i].Position;
                return positions;
            }

            // Fallback: estimate from player count
            int playerCount = GameSettings.TotalPlayers;
            int half = GameSettings.MapHalfSize;
            float spawnRadius = half * 0.5f;
            var fallback = new float3[playerCount];

            for (int i = 0; i < playerCount; i++)
            {
                float angle = (i / (float)playerCount) * math.PI * 2f;
                fallback[i] = new float3(
                    math.cos(angle) * spawnRadius,
                    0f,
                    math.sin(angle) * spawnRadius
                );
            }

            return fallback;
        }
    }
}
