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

        // Forest settings. Tuned for the current heightmap (PlainY=8, hills
        // cap ~20 m, mountain peaks ~30 m) — old 25-45 m band targeted a
        // retired 90 m heightmap and never matched real terrain.
        private const int MinForestClusters = 10;
        private const int MaxForestClusters = 18;
        // 12 m radius patches give the player big enough no-walk pockets to
        // matter tactically without eating too much of the playable area.
        // 18 patches × π × 12² ≈ 8 100 m² ≈ 13 % of a 250 m map.
        private const float ForestRadius = 12f;
        private const float ForestMinHeight = 5f;   // above water + beach
        private const float ForestMaxHeight = 22f;  // hill belt — no forest on mountains
        // 0.6 ≈ tan 31°. Wild-zone hill flanks easily hit ≈ 0.9 so 0.45 was
        // rejecting almost every candidate. 0.6 still excludes cliffs but
        // welcomes ordinary rolling hills, which is where forests belong.
        private const float ForestMaxSlope = 0.6f;
        // Must stay > Expansion radius (32 m) + a small clearance so forests
        // don't pin to the edge of the build zone, but short enough that
        // forests actually fit on a 250 m map between players that sit
        // ~180 m apart on the diagonal.
        private const float ForestMinDistFromPlayers = 38f;
        private const float ForestMinDistFromOther = 26f;

        // Per-tree settings — kept for the legacy procedural-forest renderer
        // fallback only. The new path plants Unity terrain trees inside the
        // disc; no per-tree ECS obstacle entity is created any more.
        private const float TreeObstacleRadius = 0.75f;
        private const int MinTreesPerForest = 20;
        private const int MaxTreesPerForest = 31;

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

            // === FORESTS: paint trees wherever the splat is brown ground ===
            // No more random forest discs. ProceduralTerrain walks every
            // brown-ground cell on the splatmap, plants a tree at lower
            // density (~1.6 m spacing), blocks the matching passability
            // cells, and returns one macro centre per ~5 m cell so we can
            // emit a single ObstacleTag entity per centre. NavMeshManager
            // picks those entities up via SyncBuildings and carves them
            // out of the navmesh.
            int forestMacroCount = 0;
            var terrain = ProceduralTerrain.Instance;
            if (terrain != null)
            {
                var macroCenters = terrain.PaintTreesOnBrownGround();
                float macroSize = terrain.ForestMacroCellSizeMeters;
                float macroRadius = macroSize * 0.5f;
                foreach (var c in macroCenters)
                {
                    // One impassable obstacle per macro cell. Radius is half
                    // the cell size so the box exactly fills the cell.
                    CreateForestMacroObstacle(em, new float3(c.x, c.y, c.z), macroRadius);
                    forestMacroCount++;
                }
            }
            int forestsSpawned = forestMacroCount;
            int forestCount = forestMacroCount; // for the log below

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

            Debug.Log($"[ObstacleBootstrap] forests {forestsSpawned}/{forestCount}, rocks {rocksSpawned}/{rockCount} " +
                      $"(map half {half}, range {spawnRange}, height {ForestMinHeight}..{ForestMaxHeight}, " +
                      $"slope ≤ {ForestMaxSlope}, minPlayer {ForestMinDistFromPlayers})");

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

            // Up from 20 → 60 attempts. The valid placement band on a 250 m
            // map with 4 players, slope cap 0.6, and 38 m minDist is narrow
            // enough that random sampling needs more shots to land.
            for (int attempt = 0; attempt < 60; attempt++)
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
        /// Spawn an ObstacleTag entity representing one macro cell of
        /// brown-ground forest. NavMeshManager turns it into a Box source
        /// at the cell's footprint. Passability cells were already blocked
        /// inside ProceduralTerrain.PaintTreesOnBrownGround.
        /// </summary>
        private static void CreateForestMacroObstacle(EntityManager em, float3 center, float radius)
        {
            var entity = em.CreateEntity(
                typeof(ObstacleTag),
                typeof(LocalTransform),
                typeof(Radius)
            );
            em.SetComponentData(entity, LocalTransform.FromPosition(center));
            em.SetComponentData(entity, new Radius { Value = radius });

            // Track centre for the minimap dark-green tinting. Use the
            // macro-cell radius so the minimap dot is the right size.
            ForestPositions.Add((center, radius));
        }

        // CreateForestWithTrees + the per-disc forest pipeline were removed
        // in 2026-05. Forests are now driven by ProceduralTerrain.PaintTreesOnBrownGround,
        // which walks the L_FOREST splat at 1.6 m spacing and returns macro
        // cells consumed by CreateForestMacroObstacle above.

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
