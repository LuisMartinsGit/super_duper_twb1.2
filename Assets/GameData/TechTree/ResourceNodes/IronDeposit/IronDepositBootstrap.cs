// File: Assets/GameData/TechTree/ResourceNodes/IronDeposit/IronDepositBootstrap.cs
// Spawns iron ore as patches (clusters) driven by IronPatchMarker components
// placed in the hand-authored map scene. Each marker spawns one patch
// (hex-grid or random cluster) at its position.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
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
        /// Hex-grid spawn for a marker-driven patch — a gapless block of build
        /// cells in fixed (most symmetric) order. Geometry and the reasoning
        /// behind it live in the shared <see cref="ResourcePatchFill"/>.
        /// </summary>
        private static void SpawnIronPatchHex(EntityManager em, float3 center,
            int depositCount, float spread, ref Unity.Mathematics.Random random)
            => SpawnIronPatch(em, center, depositCount, spread, raggedEdge: false, ref random);

        /// <summary>
        /// Random-cluster spawn for a marker-driven patch — same gapless block
        /// with a seed-varied outer edge.
        /// </summary>
        private static void SpawnIronPatchRandom(EntityManager em, float3 center,
            int depositCount, float spread, ref Unity.Mathematics.Random random)
            => SpawnIronPatch(em, center, depositCount, spread, raggedEdge: true, ref random);

        private static void SpawnIronPatch(EntityManager em, float3 center,
            int depositCount, float spread, bool raggedEdge, ref Unity.Mathematics.Random random)
        {
            if (depositCount <= 0) return;

            var positions = new Unity.Collections.NativeList<float3>(
                depositCount, Unity.Collections.Allocator.Temp);
            ResourcePatchFill.CollectCells(em, center, depositCount, raggedEdge, ref random, positions);

            for (int i = 0; i < positions.Length; i++)
                CreateIronDepositEntity(em, positions[i]);

            int placed = positions.Length;
            positions.Dispose();

            ResourcePatchFill.ReportFit(
                "IronDepositBootstrap", center, placed, depositCount, spread);
        }

        private static Entity CreateIronDepositEntity(EntityManager em, float3 position)
        {
            // One deposit, one build cell, snapped to its centre.
            // docs/Design/Build_Grid.md
            position = BuildGrid.SnapToCellCentre(position);

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
            // Half a build cell: the deposit blocks exactly its own cell, so
            // the circle the placement validator and steering test has to be
            // that cell rather than the old visual-derived 1.95 m, which
            // reached a metre into cells the deposit did not own.
            em.SetComponentData(entity, new Radius { Value = BuildGrid.HalfCell });
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
                grid.BlockObstacle(position, BuildGrid.HalfCell);

            return entity;
        }
    }
}
