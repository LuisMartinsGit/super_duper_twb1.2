// File: Assets/Scripts/Bootstrap/IronDepositBootstrap.cs
// Spawns iron ore as patches (clusters) driven by IronPatchMarker components
// placed in the hand-authored map scene. Each marker spawns one patch
// (hex-grid or random cluster) at its position.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns iron deposits in patches from scene markers. Each patch = a tight
    /// cluster of N deposits players can mine without ferrying miners across the
    /// map. Placement is fully marker-driven (hand-authored maps only).
    /// </summary>
    public static class IronDepositBootstrap
    {
        // Presentation ID (must match PresentationSpawnSystem)
        public const int IronDepositPresentationId = 402;

        // Per-deposit settings.
        private const float DepositRadius = 1.5f;
        private const int IronPerDeposit = 50;

        public static void SpawnIronDeposits()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xDEAD));

            if (!MapMarkerRegistry.HasIronMarkers)
            {
                Debug.LogWarning("[IronDepositBootstrap] no IronPatchMarker in the scene — " +
                                 "no iron deposits will spawn. Place markers in the map.");
                return;
            }

            // IronPatchMarkers in the scene drive placement. We seed RNG from
            // SpawnSeed so the jitter / shuffle inside each patch is
            // deterministic across re-loads.
            SpawnIronFromMarkers(em, ref random);
        }

        /// <summary>
        /// Spawn one iron patch per IronPatchMarker in the scene, honouring its
        /// DepositCount, Spread, and Layout.
        /// </summary>
        private static void SpawnIronFromMarkers(EntityManager em, ref Unity.Mathematics.Random random)
        {
            var markers = MapMarkerRegistry.IronPatches;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;

                var p = m.WorldPosition;
                float y = TerrainUtility.GetHeight(p.x, p.z);
                float3 center = new float3(p.x, y, p.z);

                if (m.Layout == PatchLayout.HexGrid)
                    SpawnIronPatchHex(em, center, m.DepositCount, m.Spread, ref random);
                else
                    SpawnIronPatchRandom(em, center, m.DepositCount, m.Spread, ref random);
            }
        }

        /// <summary>
        /// Hex-grid spawn for a marker-driven patch — deposits placed on a hex
        /// grid with per-cell jitter, taking deposit count + spread from the
        /// marker.
        /// </summary>
        private static void SpawnIronPatchHex(EntityManager em, float3 center,
            int depositCount, float spread, ref Unity.Mathematics.Random random)
        {
            // Pick a ring count large enough to fit depositCount with some
            // shuffle headroom. 1+6+12+18+24 = 61 slots at 4 rings, plenty
            // for typical patch sizes.
            int rings = depositCount <= 7  ? 1 :
                        depositCount <= 19 ? 2 :
                        depositCount <= 37 ? 3 : 4;
            float spacing = spread / Mathf.Max(1, rings);
            float jitter  = spacing * 0.30f;

            var slots = new Unity.Collections.NativeList<float2>(64, Unity.Collections.Allocator.Temp);
            GenerateHexSlots(rings, spacing, slots);

            // Fisher-Yates shuffle.
            for (int i = slots.Length - 1; i > 0; i--)
            {
                int j = random.NextInt(0, i + 1);
                float2 tmp = slots[i];
                slots[i] = slots[j];
                slots[j] = tmp;
            }

            int placeCount = math.min(depositCount, slots.Length);
            for (int i = 0; i < placeCount; i++)
            {
                float2 slot = slots[i];
                float jx = random.NextFloat(-jitter, jitter);
                float jz = random.NextFloat(-jitter, jitter);
                float x = center.x + slot.x + jx;
                float z = center.z + slot.y + jz;
                float y = TerrainUtility.GetHeight(x, z);
                CreateIronDepositEntity(em, new float3(x, y, z));
            }

            slots.Dispose();
        }

        /// <summary>
        /// Random-cluster spawn for a marker-driven patch.
        /// </summary>
        private static void SpawnIronPatchRandom(EntityManager em, float3 center,
            int depositCount, float spread, ref Unity.Mathematics.Random random)
        {
            for (int i = 0; i < depositCount; i++)
            {
                float angle = random.NextFloat(0f, math.PI * 2f);
                float dist  = random.NextFloat(0f, spread);
                float x = center.x + math.cos(angle) * dist;
                float z = center.z + math.sin(angle) * dist;
                float y = TerrainUtility.GetHeight(x, z);
                CreateIronDepositEntity(em, new float3(x, y, z));
            }
        }

        // Axial-coordinate neighbour directions for hex-grid traversal.
        // Walked in this order they trace each ring once around the centre.
        private static readonly int[,] HexDirs = new int[,]
        {
            {  1,  0 }, {  1, -1 }, {  0, -1 },
            { -1,  0 }, { -1,  1 }, {  0,  1 }
        };

        /// <summary>
        /// Fills <paramref name="output"/> with the cartesian positions of
        /// every cell in a hex grid of <paramref name="maxRings"/> rings,
        /// starting from the centre cell (ring 0). Output is centred on the
        /// origin — the caller offsets to the patch position.
        /// </summary>
        private static void GenerateHexSlots(
            int maxRings,
            float spacing,
            Unity.Collections.NativeList<float2> output)
        {
            output.Add(float2.zero);
            const float SQRT3_OVER_2 = 0.8660254f;

            for (int ring = 1; ring <= maxRings; ring++)
            {
                int q = -ring;
                int r = ring;
                for (int side = 0; side < 6; side++)
                {
                    for (int step = 0; step < ring; step++)
                    {
                        float x = spacing * (q + r * 0.5f);
                        float z = spacing * r * SQRT3_OVER_2;
                        output.Add(new float2(x, z));
                        q += HexDirs[side, 0];
                        r += HexDirs[side, 1];
                    }
                }
            }
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
                InitialIron = IronPerDeposit,
                Depleted = 0
            });

            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            // Block the passability grid cells under the deposit footprint
            // so movement steers around it. NavMeshManager picks up the
            // ObstacleTag entity and carves a matching box out of the navmesh.
            var grid = PassabilityGrid.Instance;
            if (grid != null)
                grid.BlockObstacle(position, DepositRadius * 1.3f);

            return entity;
        }
    }
}
