// File: Assets/Scripts/Bootstrap/CrystalNodeBootstrap.cs
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Entities;
using TheWaningBorder.Core.Config;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns Crystal Main Nodes at game start.
    /// Each node acts as a Crystal Curse hive: it spreads cursed ground
    /// and controls crystal faction AI behavior.
    /// Call after terrain and player spawns are initialized.
    /// </summary>
    public static class CrystalNodeBootstrap
    {
        private const int MinNodes = 2;
        private const int MaxNodes = 4;
        private const float MinDistFromPlayers = 60f;
        private const float MinDistBetweenNodes = 50f;

        /// <summary>
        /// Spawn crystal main nodes.
        /// Returns the number of nodes spawned.
        /// </summary>
        public static int SpawnCrystalNodes()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[CrystalNodeBootstrap] No ECS World available!");
                return 0;
            }

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(System.DateTime.Now.Ticks ^ 0xC7A5));

            var playerPositions = GetPlayerPositions(em);
            int half = GameSettings.MapHalfSize;
            float spawnRange = half * 0.7f;

            int nodeCount = random.NextInt(MinNodes, MaxNodes + 1);
            var nodePosArray = new float3[nodeCount];
            int nodesSpawned = 0;

            for (int n = 0; n < nodeCount; n++)
            {
                float3 nodePos = float3.zero;
                bool found = false;

                for (int attempt = 0; attempt < 30; attempt++)
                {
                    float x = random.NextFloat(-spawnRange, spawnRange);
                    float z = random.NextFloat(-spawnRange, spawnRange);
                    float y = TerrainUtility.GetHeight(x, z);
                    float3 candidate = new float3(x, y, z);

                    // Check not in water
                    var terrain = ProceduralTerrain.Instance;
                    if (terrain != null && terrain.IsInWater(new Vector3(x, y, z)))
                        continue;

                    // Check distance from all player positions
                    bool tooCloseToPlayer = false;
                    for (int p = 0; p < playerPositions.Length; p++)
                    {
                        if (math.distance(candidate, playerPositions[p]) < MinDistFromPlayers)
                        {
                            tooCloseToPlayer = true;
                            break;
                        }
                    }
                    if (tooCloseToPlayer) continue;

                    // Check distance from already-placed nodes
                    bool tooCloseToNode = false;
                    for (int prev = 0; prev < nodesSpawned; prev++)
                    {
                        if (math.distance(candidate, nodePosArray[prev]) < MinDistBetweenNodes)
                        {
                            tooCloseToNode = true;
                            break;
                        }
                    }
                    if (tooCloseToNode) continue;

                    nodePos = candidate;
                    found = true;
                    break;
                }

                if (!found) continue;

                // Create the crystal main node
                CrystalMainNode.Create(em, nodePos);
                nodePosArray[nodesSpawned] = nodePos;
                nodesSpawned++;
            }

            Debug.Log($"[CrystalNodeBootstrap] Spawned {nodesSpawned} crystal nodes");
            return nodesSpawned;
        }

        /// <summary>
        /// Get player positions from existing Halls, or estimate from spawn layout.
        /// Get player positions from existing Halls, or estimate from spawn layout.
        /// </summary>
        private static float3[] GetPlayerPositions(EntityManager em)
        {
            var hallQuery = em.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<HallTag>(),
                Unity.Entities.ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()
            );

            using var hallTransforms = hallQuery.ToComponentDataArray<Unity.Transforms.LocalTransform>(
                Unity.Collections.Allocator.Temp);

            if (hallTransforms.Length > 0)
            {
                var positions = new float3[hallTransforms.Length];
                for (int i = 0; i < hallTransforms.Length; i++)
                {
                    positions[i] = hallTransforms[i].Position;
                }
                return positions;
            }

            // Fallback: estimate based on player count and map layout
            int playerCount = GameSettings.TotalPlayers;
            int half = GameSettings.MapHalfSize;
            float spawnRadius = half * 0.7f;
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
