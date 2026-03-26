// File: Assets/Scripts/Bootstrap/IronDepositBootstrap.cs
// Spawns iron ore deposits along mountain foothills and highlands.
// Follows the ObstacleBootstrap pattern: random positions with
// terrain height/slope checks and minimum distance from player bases.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns iron deposit entities on the terrain at game start.
    /// Iron deposits prefer mountain foothills and highland areas.
    /// Miners can gather iron from these deposits.
    /// </summary>
    public static class IronDepositBootstrap
    {
        // Presentation ID (must match PresentationSpawnSystem)
        public const int IronDepositPresentationId = 402;

        // Spawn settings
        private const int MinDeposits = 12;
        private const int MaxDeposits = 20;
        private const float DepositRadius = 1.5f;
        private const int IronPerDeposit = 500;

        // Placement constraints — prefer highland/mountain foothills
        private const float MinHeight = 28f;             // Above beach/shoreline (~23)
        private const float MaxHeight = 85f;             // Below extreme peaks
        private const float PreferredMinSlope = 0.05f;   // Slight preference for sloped terrain
        private const float MaxSlope = 0.6f;             // Not on cliffs
        private const float MinDistFromPlayers = 25f;
        private const float MinDistFromOther = 12f;

        /// <summary>
        /// Main entry point. Call after terrain and player spawns are initialized.
        /// </summary>
        public static void SpawnIronDeposits()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[IronDepositBootstrap] No ECS World available!");
                return;
            }

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xDEAD));

            var playerPositions = GetPlayerPositions(em);
            int half = GameSettings.MapHalfSize;
            float spawnRange = half * 0.85f;

            // Track placed positions to avoid overlap
            var placedPositions = new Unity.Collections.NativeList<float3>(16, Unity.Collections.Allocator.Temp);

            int depositCount = random.NextInt(MinDeposits, MaxDeposits + 1);
            int depositsSpawned = 0;

            for (int i = 0; i < depositCount; i++)
            {
                if (TryFindPosition(
                    ref random, spawnRange, playerPositions, placedPositions,
                    out float3 pos))
                {
                    CreateIronDepositEntity(em, pos);
                    placedPositions.Add(pos);
                    depositsSpawned++;
                }
            }

            placedPositions.Dispose();

            Debug.Log($"[IronDepositBootstrap] Spawned {depositsSpawned} iron deposits");
        }

        /// <summary>
        /// Try up to 30 random positions to find one matching height, slope, and distance constraints.
        /// </summary>
        private static bool TryFindPosition(
            ref Unity.Mathematics.Random random,
            float spawnRange,
            float3[] playerPositions,
            Unity.Collections.NativeList<float3> placedPositions,
            out float3 result)
        {
            result = float3.zero;

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

                // Check height range — prefer highlands/mountain foothills
                if (y < MinHeight || y > MaxHeight)
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

                // Not on cliff faces
                if (slope > MaxSlope) continue;

                // Soft preference for sloped terrain (near mountains)
                if (slope < PreferredMinSlope)
                {
                    // 60% chance to accept flat terrain
                    if (random.NextFloat() > 0.6f)
                        continue;
                }

                // Check distance from player positions
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

                // Check distance from already-placed deposits
                bool tooCloseToOther = false;
                for (int o = 0; o < placedPositions.Length; o++)
                {
                    if (math.distance(candidate, placedPositions[o]) < MinDistFromOther)
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
        /// Create an iron deposit ECS entity.
        /// </summary>
        private static Entity CreateIronDepositEntity(EntityManager em, float3 position)
        {
            var entity = em.CreateEntity(
                typeof(IronMineTag),
                typeof(IronDepositState),
                typeof(LocalTransform),
                typeof(Radius),
                typeof(PresentationId)
            );

            em.SetComponentData(entity, LocalTransform.FromPosition(position));
            em.SetComponentData(entity, new Radius { Value = DepositRadius });
            em.SetComponentData(entity, new PresentationId { Id = IronDepositPresentationId });
            em.SetComponentData(entity, new IronDepositState
            {
                RemainingIron = IronPerDeposit,
                Depleted = 0
            });

            // Assign network ID for multiplayer lockstep synchronization
            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

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
