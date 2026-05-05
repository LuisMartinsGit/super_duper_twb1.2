// File: Assets/Scripts/Bootstrap/ObstacleBootstrap.cs
// Spawns forest clusters and rock formations as navigation obstacles.
// Uses random positions with terrain height/slope checks and minimum
// distance from player bases.

using System.Collections.Generic;
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

        /// <summary>
        /// Forest center positions and radii, populated at spawn time.
        /// Used by MinimapRenderer to draw forest areas on the background.
        /// </summary>
        public static readonly List<(float3 center, float radius)> ForestPositions = new();

        // Forest settings
        private const int MinForestClusters = 10;
        private const int MaxForestClusters = 18;
        private const float ForestRadius = 12f;
        private const float ForestMinHeight = 25f;
        private const float ForestMaxHeight = 45f;
        private const float ForestMaxSlope = 0.2f;
        private const float ForestMinDistFromPlayers = 50f;
        private const float ForestMinDistFromOther = 25f;

        // Individual tree obstacle settings
        private const float TreeObstacleRadius = 0.75f; // 1.5 unit diameter cylinder per tree
        private const int MinTreesPerForest = 20;
        private const int MaxTreesPerForest = 31; // exclusive upper bound → 20-30 trees

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
                return;
            }

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xBEEF));

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
                    CreateForestWithTrees(em, pos);
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

            // PR3 — flow-field invalidation removed. NavMeshManager picks up
            // the new building set via its own ECS sync.

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
        /// Create a forest: a visual root entity (no ObstacleTag) plus individual tree
        /// obstacles on the passability grid. Trees use the same RNG as PresentationSpawnSystem
        /// so blocked cells align with visual tree positions.
        /// </summary>
        private static void CreateForestWithTrees(EntityManager em, float3 center)
        {
            // Create visual-only forest root (no ObstacleTag → UnitSeparationSystem ignores it)
            var forestEntity = em.CreateEntity(
                typeof(LocalTransform),
                typeof(Radius),
                typeof(PresentationId)
            );

            em.SetComponentData(forestEntity, LocalTransform.FromPosition(center));
            em.SetComponentData(forestEntity, new Radius { Value = ForestRadius });
            em.SetComponentData(forestEntity, new PresentationId { Id = ForestPresentationId });

            // Store for minimap rendering
            ForestPositions.Add((center, ForestRadius));

            // Generate tree positions with the same RNG seed as PresentationSpawnSystem
            var treeRng = new System.Random(forestEntity.Index + 12345);
            int treeCount = treeRng.Next(MinTreesPerForest, MaxTreesPerForest);

            var grid = PassabilityGrid.Instance;

            for (int t = 0; t < treeCount; t++)
            {
                // Position (matches PresentationSpawnSystem.CreateProceduralForest)
                float angle = (float)(treeRng.NextDouble() * System.Math.PI * 2.0);
                float dist = (float)(treeRng.NextDouble() * ForestRadius * 0.65f);
                float offsetX = (float)System.Math.Cos(angle) * dist;
                float offsetZ = (float)System.Math.Sin(angle) * dist;

                // Advance RNG to stay in sync with presentation (treeHeight, trunkRadius, canopyRadius, greenVariation)
                treeRng.NextDouble(); // treeHeight
                treeRng.NextDouble(); // trunkRadius
                treeRng.NextDouble(); // canopyRadius
                treeRng.NextDouble(); // greenVariation

                float3 treeWorldPos = new float3(
                    center.x + offsetX,
                    TerrainUtility.GetHeight(center.x + offsetX, center.z + offsetZ),
                    center.z + offsetZ
                );

                // Create individual tree obstacle entity for physical collision
                var treeEntity = em.CreateEntity(
                    typeof(ObstacleTag),
                    typeof(LocalTransform),
                    typeof(Radius)
                );
                em.SetComponentData(treeEntity, LocalTransform.FromPosition(treeWorldPos));
                em.SetComponentData(treeEntity, new Radius { Value = TreeObstacleRadius });

                // Block passability around each tree trunk.
                // Use at least cellSize so every tree reliably blocks 1+ grid cells.
                if (grid != null)
                {
                    float blockRadius = math.max(TreeObstacleRadius, grid.CellSize);
                    grid.BlockObstacle(treeWorldPos, blockRadius);
                }
            }
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

            // Block passability grid cells so flow fields route around this obstacle
            var grid = PassabilityGrid.Instance;
            if (grid != null)
                grid.BlockObstacle(position, radius);

            return entity;
        }

        /// <summary>
        /// Get player positions from existing Halls, or estimate from spawn layout.
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
