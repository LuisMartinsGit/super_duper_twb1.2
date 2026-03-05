// File: Assets/Scripts/Bootstrap/CreatureBootstrap.cs
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Entities;
using TheWaningBorder.Core.Config;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns hostile creature groups around the map at game start.
    /// Creatures are placed on land, away from player spawn positions.
    ///
    /// When CrystalNodeBootstrap runs first and spawns crystal nodes,
    /// this bootstrap spawns fewer standalone groups since node guards
    /// already populate the map with creatures.
    /// </summary>
    public static class CreatureBootstrap
    {
        private const int MinGroups = 4;
        private const int MaxGroups = 6;
        private const int MinPerGroup = 2;
        private const int MaxPerGroup = 3;
        private const float MinDistFromPlayers = 40f;
        private const float GroupSpread = 4f; // How far apart creatures in a group are

        /// <summary>
        /// Spawn creature groups at random positions around the map.
        /// Call after terrain and player spawns are initialized.
        /// If crystal nodes were spawned by CrystalNodeBootstrap, reduces
        /// standalone groups to avoid over-populating the map.
        /// </summary>
        /// <param name="crystalNodesSpawned">Number of crystal nodes already placed (0 if none).</param>
        public static void SpawnCreatureGroups(int crystalNodesSpawned = 0)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[CreatureBootstrap] No ECS World available!");
                return;
            }

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)System.DateTime.Now.Ticks);

            // Reduce standalone groups when crystal nodes already provide creature guards
            int adjustedMin = math.max(1, MinGroups - crystalNodesSpawned);
            int adjustedMax = math.max(adjustedMin, MaxGroups - crystalNodesSpawned);
            int groupCount = random.NextInt(adjustedMin, adjustedMax + 1);
            int totalSpawned = 0;

            // Get player spawn positions to avoid
            var playerPositions = GetPlayerPositions(em);

            int half = GameSettings.MapHalfSize;
            float spawnRange = half * 0.8f;

            for (int g = 0; g < groupCount; g++)
            {
                // Try to find a valid position
                float3 groupCenter = float3.zero;
                bool found = false;

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

                    // Check distance from all player positions
                    bool tooClose = false;
                    for (int p = 0; p < playerPositions.Length; p++)
                    {
                        if (math.distance(candidate, playerPositions[p]) < MinDistFromPlayers)
                        {
                            tooClose = true;
                            break;
                        }
                    }

                    if (!tooClose)
                    {
                        groupCenter = candidate;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // Fallback: place at a random position anyway
                    float x = random.NextFloat(-spawnRange, spawnRange);
                    float z = random.NextFloat(-spawnRange, spawnRange);
                    float y = TerrainUtility.GetHeight(x, z);
                    groupCenter = new float3(x, y, z);
                }

                // Spawn creatures in this group
                int creaturesInGroup = random.NextInt(MinPerGroup, MaxPerGroup + 1);
                for (int c = 0; c < creaturesInGroup; c++)
                {
                    float offsetX = random.NextFloat(-GroupSpread, GroupSpread);
                    float offsetZ = random.NextFloat(-GroupSpread, GroupSpread);
                    float3 creaturePos = groupCenter + new float3(offsetX, 0f, offsetZ);
                    creaturePos.y = TerrainUtility.GetHeight(creaturePos.x, creaturePos.z);

                    Creature.Create(em, creaturePos);
                    totalSpawned++;
                }
            }

            Debug.Log($"[CreatureBootstrap] Spawned {totalSpawned} creatures in {groupCount} groups");
        }

        /// <summary>
        /// Get approximate positions of all player bases (Halls).
        /// Falls back to calculated spawn positions if no halls exist yet.
        /// </summary>
        private static float3[] GetPlayerPositions(EntityManager em)
        {
            // Try to find existing halls
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
