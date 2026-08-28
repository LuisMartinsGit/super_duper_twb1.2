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
                // Fall back rather than leave the match with NO IRON AT ALL.
                // Veilstone and veilsteel both already do this, and
                // VeilstoneOutcroppingBootstrap's fallback comment even claims
                // it is "mirroring how iron is always available" — which was
                // not true: this path used to just return, so a markerless map
                // ran a full match with a dead iron economy and only a warning
                // in the log.
                Debug.LogWarning("[IronDepositBootstrap] no IronPatchMarker in the scene — " +
                                 "spawning fallback patches near bases + scattered " +
                                 "(markerless map). Place markers for authored placement.");
                SpawnFallbackPatches(em, ref random);
                return;
            }

            // IronPatchMarkers in the scene drive placement. We seed RNG from
            // SpawnSeed so the jitter / shuffle inside each patch is
            // deterministic across re-loads.
            SpawnIronFromMarkers(em, ref random);
        }

        /// <summary>
        /// Markerless fallback: one patch near every faction Hall plus a few
        /// scattered across the playable area. Deliberately mirrors
        /// VeilstoneOutcroppingBootstrap.SpawnFallbackPatches so the two
        /// resources behave the same way on an unauthored map.
        ///
        /// Iron is the one resource a match cannot open without — and under
        /// docs/Design/Regions.md an Age 0 player is confined to their start
        /// region, so a base with no reachable iron is a soft-lock, not a
        /// disadvantage.
        /// </summary>
        private static void SpawnFallbackPatches(EntityManager em, ref Unity.Mathematics.Random random)
        {
            const int DepositsPerPatch = 30;
            const float PatchSpread = 7f;
            const int ScatteredPatches = 6;

            // Near-base patches: one per Hall, 24-32 m out at a random bearing,
            // which is close enough to work in the opening and far enough not
            // to sit inside the base footprint.
            var hallQuery = em.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<HallTag>(),
                Unity.Entities.ComponentType.ReadOnly<LocalTransform>());
            var halls = hallQuery.ToComponentDataArray<LocalTransform>(
                Unity.Collections.Allocator.Temp);

            for (int i = 0; i < halls.Length; i++)
            {
                float ang = random.NextFloat(0f, math.PI * 2f);
                float dist = random.NextFloat(24f, 32f);
                float x = halls[i].Position.x + math.cos(ang) * dist;
                float z = halls[i].Position.z + math.sin(ang) * dist;
                SpawnIronPatchHex(em, new float3(x, TerrainUtility.GetHeight(x, z), z),
                                  DepositsPerPatch, PatchSpread, ref random);
            }
            halls.Dispose();
            hallQuery.Dispose();

            // Scattered patches across the playable area so the mid-map is
            // worth contesting even with no authored markers.
            TerrainUtility.GetPlayableBounds(out var mn, out var mx);
            for (int i = 0; i < ScatteredPatches; i++)
            {
                float x = random.NextFloat(mn.x, mx.x);
                float z = random.NextFloat(mn.y, mx.y);
                SpawnIronPatchHex(em, new float3(x, TerrainUtility.GetHeight(x, z), z),
                                  DepositsPerPatch, PatchSpread, ref random);
            }
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

        /// <summary>
        /// ONE node per marker, holding what the whole patch used to
        /// (docs/Design/Regions.md §4, "Nodes, not patches").
        ///
        /// A patch was a mining-era shape: thirty small deposits so thirty
        /// workers had somewhere to stand. Nothing stands on them any more —
        /// a territory holding a node pays a trickle, and a mine built on the
        /// node pays more — so the scatter bought nothing and cost a hundred-odd
        /// entities, a hundred-odd blocked passability cells and a visual that
        /// no one could point at. The marker's DepositCount and Spread are now
        /// read as "how much" and "how big", not "how many" and "how far".
        ///
        /// `raggedEdge` and the RNG are ignored and kept in the signature only
        /// so PatchLayout stays a valid authored field on existing markers.
        /// </summary>
        private static void SpawnIronPatch(EntityManager em, float3 center,
            int depositCount, float spread, bool raggedEdge, ref Unity.Mathematics.Random random)
        {
            if (depositCount <= 0) return;
            CreateIronDepositEntity(em, center, depositCount * IronPerDeposit);
        }

        /// <summary>
        /// The node's footprint, in build cells across. Sized so a single node
        /// reads as a landmark you contest rather than a pebble you walk past,
        /// and so a Mine placed on it has something to sit against.
        /// docs/Design/Build_Grid.md
        /// </summary>
        public const int NodeFootprintCells = 3;

        /// <summary>Half-extent of the node footprint, in metres.</summary>
        public static float NodeRadius => BuildGrid.CellSize * NodeFootprintCells * 0.5f;

        private static Entity CreateIronDepositEntity(EntityManager em, float3 position,
            int amount = IronPerDeposit)
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

            // Scaled up with the footprint: one node now stands where a patch
            // of thirty used to, so it has to LOOK like the landmark it is.
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, NodeFootprintCells));
            // The node blocks its whole footprint, so the circle the placement
            // validator and steering test against is that footprint — not the
            // single cell a patch member used to occupy.
            em.SetComponentData(entity, new Radius { Value = NodeRadius });
            em.SetComponentData(entity, new PresentationId { Id = IronDepositPresentationId });
            em.SetComponentData(entity, new IronDepositState
            {
                RemainingIron = amount,
                InitialIron = amount,
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
                grid.BlockObstacle(position, NodeRadius);

            return entity;
        }
    }
}
