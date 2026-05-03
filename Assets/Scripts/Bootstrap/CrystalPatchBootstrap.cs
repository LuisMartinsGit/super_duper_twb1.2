// File: Assets/Scripts/Bootstrap/CrystalPatchBootstrap.cs
// Spawns mineable crystal cadavers as patches near each player and scattered
// across the map, so AI / players have a starting crystal source without
// having to fight Crystallings first. Used in addition to (or in place of)
// CrystalNodeBootstrap depending on map mode.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns cadaver-based crystal patches at game start.
    ///
    /// Layout per player:
    /// - 1 NEAR patch close to the Hall (within NearPatchMinDist..MaxDist)
    /// - <see cref="ScatteredPatchesPerPlayer"/> patches scattered across the map
    ///
    /// Each patch = a small cluster of cadavers with crystal in them. Mineable
    /// by Miners via GatherCommand on a cadaver (CrystalMiningSystem handles
    /// the gathering loop). Independent of CrystalNodeBootstrap (which spawns
    /// the curse main nodes that grow Crystallings).
    /// </summary>
    public static class CrystalPatchBootstrap
    {
        // Crystal amount per cadaver. 200 = ~10s of mining at 1 crystal/1.5s
        // for one miner. With 3 cadavers per patch (DefaultCadaversPerPatch),
        // a patch yields ~600 crystal — enough to cover an age-up's crystal
        // cost from a single near-patch even before scattered patches kick in.
        private const int CrystalPerOutcrop = 200;
        private const int DefaultOutcropsPerPatch = 3;

        // Cluster geometry: cadavers within PatchSpread units of patch center.
        // Larger than iron's 4u because cadavers are bigger visually.
        private const float PatchSpread = 5f;

        // NEAR patch (one per player)
        private const float NearPatchMinDist = 22f; // outside Hall footprint
        private const float NearPatchMaxDist = 32f;

        // SCATTERED patches
        private const int ScatteredPatchesPerPlayer = 2;
        private const float ScatteredMinDistFromPlayer = 50f;
        private const float MinDistBetweenPatchCenters = 24f;

        // Heightmap constraints (only enforced when NOT FlatTestMap)
        private const float MinHeight = 23f;
        private const float MaxHeight = 85f;
        private const float MaxSlope = 0.6f;

        public static void SpawnCrystalPatches()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xCEED));

            var playerPositions = GetPlayerPositions(em);
            int half = GameSettings.MapHalfSize;
            float spawnRange = half * 0.85f;

            var patchCenters = new Unity.Collections.NativeList<float3>(
                playerPositions.Length * (1 + ScatteredPatchesPerPlayer),
                Unity.Collections.Allocator.Temp);

            // 1. NEAR patches — one per player.
            for (int p = 0; p < playerPositions.Length; p++)
            {
                float3 center = PickNearPatchCenter(playerPositions[p], ref random);
                SpawnCrystalPatch(em, center, ref random);
                patchCenters.Add(center);
            }

            // 2. SCATTERED patches — N per player, gated by distance + terrain.
            int scatteredCount = playerPositions.Length * ScatteredPatchesPerPlayer;
            for (int i = 0; i < scatteredCount; i++)
            {
                if (TryFindScatteredPatchCenter(ref random, spawnRange,
                        playerPositions, patchCenters, out float3 center))
                {
                    SpawnCrystalPatch(em, center, ref random);
                    patchCenters.Add(center);
                }
            }

            patchCenters.Dispose();
        }

        private static void SpawnCrystalPatch(EntityManager em, float3 center, ref Unity.Mathematics.Random random)
        {
            for (int i = 0; i < DefaultOutcropsPerPatch; i++)
            {
                float angle = random.NextFloat(0f, math.PI * 2f);
                float dist  = random.NextFloat(0f, PatchSpread);
                float x = center.x + math.cos(angle) * dist;
                float z = center.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                Cadaver.Create(em, new float3(x, y, z), CrystalPerOutcrop);
            }
        }

        private static float3 PickNearPatchCenter(float3 player, ref Unity.Mathematics.Random random)
        {
            float angle = random.NextFloat(0f, math.PI * 2f);
            float dist  = random.NextFloat(NearPatchMinDist, NearPatchMaxDist);
            float x = player.x + math.cos(angle) * dist;
            float z = player.z + math.sin(angle) * dist;
            float y = TerrainUtility.GetHeight(x, z);
            return new float3(x, y, z);
        }

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

                    float step = 2f;
                    float hL = TerrainUtility.GetHeight(x - step, z);
                    float hR = TerrainUtility.GetHeight(x + step, z);
                    float hD = TerrainUtility.GetHeight(x, z - step);
                    float hU = TerrainUtility.GetHeight(x, z + step);
                    float dX = (hR - hL) / (step * 2f);
                    float dZ = (hU - hD) / (step * 2f);
                    if (math.sqrt(dX * dX + dZ * dZ) > MaxSlope) continue;
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

        private static float3[] GetPlayerPositions(EntityManager em)
        {
            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()
            );

            using var hallTransforms = hallQuery.ToComponentDataArray<Unity.Transforms.LocalTransform>(
                Unity.Collections.Allocator.Temp);

            if (hallTransforms.Length > 0)
            {
                var positions = new float3[hallTransforms.Length];
                for (int i = 0; i < hallTransforms.Length; i++)
                    positions[i] = hallTransforms[i].Position;
                return positions;
            }

            int playerCount = GameSettings.TotalPlayers;
            int half = GameSettings.MapHalfSize;
            float spawnRadius = half * 0.5f;
            var fallback = new float3[playerCount];
            for (int i = 0; i < playerCount; i++)
            {
                float angle = (i / (float)playerCount) * math.PI * 2f;
                fallback[i] = new float3(
                    math.cos(angle) * spawnRadius, 0f, math.sin(angle) * spawnRadius);
            }
            return fallback;
        }
    }
}
