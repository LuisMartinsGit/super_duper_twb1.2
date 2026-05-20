// File: Assets/Scripts/Bootstrap/IronDepositBootstrap.cs
// Spawns iron ore as patches (clusters) instead of single scattered deposits.
// Each player gets one patch close to their Hall plus several patches scattered
// across the map. Replaces the earlier "scatter N deposits with min-distance
// from players" loop, which formed an obvious ring around each spawn.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns iron deposits in patches. Each patch = a tight cluster of N
    /// deposits players can mine without ferrying miners across the map.
    ///
    /// Layout per player:
    /// - 1 NEAR patch close to the Hall (within NearPatchMinDist..MaxDist)
    /// - <see cref="ScatteredPatchesPerPlayer"/> patches scattered across the
    ///   map, with min-distance gates so patches don't overlap and stay out of
    ///   spawn footprints.
    /// </summary>
    public static class IronDepositBootstrap
    {
        // Presentation ID (must match PresentationSpawnSystem)
        public const int IronDepositPresentationId = 402;

        // Per-deposit settings
        private const float DepositRadius = 1.5f;
        private const int IronPerDeposit = 500;

        // Patch settings
        private const int DepositsPerPatch = 3;            // 3 deposits per cluster
        private const float PatchSpread = 4f;              // deposits within 4u of patch center

        // NEAR patch (one per player)
        private const float NearPatchMinDist = 22f;        // outside Hall footprint (~20u)
        private const float NearPatchMaxDist = 32f;        // close enough to mine without long walks

        // SCATTERED patches
        private const int ScatteredPatchesPerPlayer = 4;
        private const float ScatteredMinDistFromPlayer = 50f;
        private const float MinDistBetweenPatchCenters = 24f;

        // Heightmap constraints (only enforced when NOT FlatTestMap)
        private const float MinHeight = 23f;               // above shoreline
        private const float MaxHeight = 85f;
        private const float MaxSlope = 0.6f;               // not on cliffs

        public static void SpawnIronDeposits()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xDEAD));

            var playerPositions = GetPlayerPositions(em);
            int half = GameSettings.MapHalfSize;
            float spawnRange = half * 0.85f;

            // Track placed patch centers so scattered patches don't overlap them
            // (or each other) and form visible clusters across the map.
            var patchCenters = new Unity.Collections.NativeList<float3>(
                playerPositions.Length * (1 + ScatteredPatchesPerPlayer),
                Unity.Collections.Allocator.Temp);

            // 1. NEAR patches — one per player. Always succeed (we picked the
            //    direction from the player ourselves; no validation step).
            for (int p = 0; p < playerPositions.Length; p++)
            {
                float3 center = PickNearPatchCenter(playerPositions[p], ref random);
                SpawnIronPatch(em, center, ref random);
                patchCenters.Add(center);
            }

            // 2. SCATTERED patches — N per player, gated by distance and terrain.
            int scatteredCount = playerPositions.Length * ScatteredPatchesPerPlayer;
            for (int i = 0; i < scatteredCount; i++)
            {
                if (TryFindScatteredPatchCenter(ref random, spawnRange,
                        playerPositions, patchCenters, out float3 center))
                {
                    SpawnIronPatch(em, center, ref random);
                    patchCenters.Add(center);
                }
            }

            patchCenters.Dispose();
        }

        /// <summary>
        /// Place <see cref="DepositsPerPatch"/> deposits jittered within
        /// <see cref="PatchSpread"/> of <paramref name="center"/>. Snaps each to
        /// terrain height. No water/slope check — patch centers were already
        /// validated upstream.
        /// </summary>
        private static void SpawnIronPatch(EntityManager em, float3 center, ref Unity.Mathematics.Random random)
        {
            for (int i = 0; i < DepositsPerPatch; i++)
            {
                float angle = random.NextFloat(0f, math.PI * 2f);
                float dist  = random.NextFloat(0f, PatchSpread);
                float x = center.x + math.cos(angle) * dist;
                float z = center.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                CreateIronDepositEntity(em, new float3(x, y, z));
            }
        }

        /// <summary>
        /// Pick a random direction from <paramref name="player"/> at a random
        /// distance in [NearPatchMinDist, NearPatchMaxDist]. Retries up to 16
        /// times if the candidate lands on terrain the passability layer
        /// considers blocked (mountains can now appear close to the spawn ring
        /// on small maps) — falls back to the last sample if every attempt
        /// fails so we never skip a player's near patch.
        /// </summary>
        private static float3 PickNearPatchCenter(float3 player, ref Unity.Mathematics.Random random)
        {
            var passGrid = PassabilityGrid.Instance;
            float3 last = float3.zero;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float angle = random.NextFloat(0f, math.PI * 2f);
                float dist  = random.NextFloat(NearPatchMinDist, NearPatchMaxDist);
                float x = player.x + math.cos(angle) * dist;
                float z = player.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                last = new float3(x, y, z);
                if (passGrid == null || passGrid.IsReachableByAllPlayersForRadius(last, DepositRadius + 1f))
                    return last;
            }
            return last;
        }

        /// <summary>
        /// Pick a random position on the map for a scattered patch, ensuring
        /// minimum distance from all players and from already-placed patches.
        /// On non-flat maps, also rejects water and out-of-band heights.
        /// </summary>
        private static bool TryFindScatteredPatchCenter(
            ref Unity.Mathematics.Random random,
            float spawnRange,
            float3[] playerPositions,
            Unity.Collections.NativeList<float3> patchCenters,
            out float3 result)
        {
            result = float3.zero;
            bool isFlat = GameSettings.FlatTestMap;
            var terrain = ProceduralTerrain.Instance;

            for (int attempt = 0; attempt < 40; attempt++)
            {
                float x = random.NextFloat(-spawnRange, spawnRange);
                float z = random.NextFloat(-spawnRange, spawnRange);
                float y = TerrainUtility.GetHeight(x, z);
                float3 candidate = new float3(x, y, z);

                if (!isFlat)
                {
                    if (terrain != null && terrain.IsInWater(new Vector3(x, y, z))) continue;
                    if (y < MinHeight || y > MaxHeight) continue;

                    // Slope check (skip cliffs).
                    float step = 2f;
                    float hL = TerrainUtility.GetHeight(x - step, z);
                    float hR = TerrainUtility.GetHeight(x + step, z);
                    float hD = TerrainUtility.GetHeight(x, z - step);
                    float hU = TerrainUtility.GetHeight(x, z + step);
                    float dX = (hR - hL) / (step * 2f);
                    float dZ = (hU - hD) / (step * 2f);
                    if (math.sqrt(dX * dX + dZ * dZ) > MaxSlope) continue;

                    // Reachability check — deposit must sit in the connected
                    // region every player can reach (rejects mountain pockets,
                    // cliff tops, islands the hall is fenced off from).
                    var passGrid = PassabilityGrid.Instance;
                    if (passGrid != null && !passGrid.IsReachableByAllPlayersForRadius(candidate, DepositRadius + 1f))
                        continue;
                }

                bool tooCloseToPlayer = false;
                for (int p = 0; p < playerPositions.Length; p++)
                {
                    if (math.distance(candidate, playerPositions[p]) < ScatteredMinDistFromPlayer)
                    { tooCloseToPlayer = true; break; }
                }
                if (tooCloseToPlayer) continue;

                bool tooCloseToPatch = false;
                for (int o = 0; o < patchCenters.Length; o++)
                {
                    if (math.distance(candidate, patchCenters[o]) < MinDistBetweenPatchCenters)
                    { tooCloseToPatch = true; break; }
                }
                if (tooCloseToPatch) continue;

                result = candidate;
                return true;
            }

            return false;
        }

        private static Entity CreateIronDepositEntity(EntityManager em, float3 position)
        {
            var entity = em.CreateEntity(
                typeof(IronMineTag),
                // ObstacleTag — units route around the deposit on the
                // passability grid AND the deposit's footprint is fed into
                // NavMeshManager so pathfinding can't try to clip through it.
                typeof(ObstacleTag),
                typeof(IronDepositState),
                typeof(LocalTransform),
                typeof(Radius),
                typeof(PresentationId)
            );

            em.SetComponentData(entity, LocalTransform.FromPosition(position));
            // Visual is scaled +30 % from the prefab, so use the larger
            // collision radius (1.5 m × 1.3) for both passability and navmesh.
            em.SetComponentData(entity, new Radius { Value = DepositRadius * 1.3f });
            em.SetComponentData(entity, new PresentationId { Id = IronDepositPresentationId });
            em.SetComponentData(entity, new IronDepositState
            {
                RemainingIron = IronPerDeposit,
                Depleted = 0
            });

            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Block the passability grid cells under the deposit footprint
            // so flow-fields and movement steer around it. NavMeshManager
            // picks up the ObstacleTag entity in its SyncBuildings pass and
            // carves a matching box source out of the navmesh.
            var grid = PassabilityGrid.Instance;
            if (grid != null)
                grid.BlockObstacle(position, DepositRadius * 1.3f);

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
