// SupplyNodeBootstrap.cs
// Spawns the map's supply nodes from SupplyNodeMarker scene markers.
//
// Marker-driven with a per-territory fallback, the same arrangement iron and
// veilstone use — and for the same reason. A supply node is the ONLY place a
// Gatherer's Hut can stand (docs/Design/Regions.md §4), so a map that ships
// without any is not a map with a weaker economy, it is a map with no supply
// economy at all. The fallback keeps an unauthored map playable; the editor
// tool (Waning Border > Maps > Seed Resource Nodes For Open Scene) is what
// turns that into authored markers you can then move by hand.

using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Entities;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    public static class SupplyNodeBootstrap
    {
        /// <summary>
        /// Supply nodes per territory when a map authored none. This is the
        /// number of Gatherer's Huts a territory will support, so it is a
        /// balance figure, not a cosmetic one.
        /// </summary>
        public const int FallbackNodesPerTerritory = 3;

        /// <summary>How far from its seed a fallback node is scattered.</summary>
        private const float FallbackSpread = 26f;

        public static void SpawnSupplyNodes()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            if (MapMarkerRegistry.HasSupplyNodes)
            {
                var markers = MapMarkerRegistry.SupplyNodes;
                int placed = 0;
                for (int i = 0; i < markers.Count; i++)
                {
                    var m = markers[i];
                    if (m == null) continue;
                    var p = m.WorldPosition;
                    // Snap Y at spawn. MapMarker.SnapToTerrain only moves the
                    // GIZMO — WorldPosition is the raw transform — so a marker
                    // authored at Y=0 (which is how they are seeded) would put
                    // its node under the ground.
                    float y = TerrainUtility.GetHeight(p.x, p.z);
                    SupplyNode.Create(em, new float3(p.x, y, p.z));
                    placed++;
                }
                TWBLog.Log($"[SupplyNodeBootstrap] {placed} authored supply node(s).");
                return;
            }

            Debug.LogWarning("[SupplyNodeBootstrap] no SupplyNodeMarker in the scene — " +
                             "scattering fallback nodes so Gatherer's Huts are placeable. " +
                             "Run Waning Border > Maps > Seed Resource Nodes For Open Scene " +
                             "to author them.");
            SpawnFallbackNodes(em);
        }

        /// <summary>
        /// One small ring of nodes per territory, around its seed. Seeded from
        /// the region index rather than a random generator so every peer lays
        /// them out identically without threading the spawn seed through.
        /// </summary>
        private static void SpawnFallbackNodes(EntityManager em)
        {
            if (!RegionMap.Ready) return;

            int placed = 0;
            for (int r = 0; r < RegionMap.Count; r++)
            {
                Vector2 seed = RegionMap.SeedOf(r);
                for (int i = 0; i < FallbackNodesPerTerritory; i++)
                {
                    // Deterministic bearing: spread evenly, offset per region so
                    // neighbouring territories do not line their nodes up.
                    float angle = (i / (float)FallbackNodesPerTerritory + r * 0.37f)
                                  * Mathf.PI * 2f;
                    float x = seed.x + Mathf.Cos(angle) * FallbackSpread;
                    float z = seed.y + Mathf.Sin(angle) * FallbackSpread;

                    // Keep it inside the territory it is meant to feed — a node
                    // that drifts over the border would hand its huts, and the
                    // supplies they pay, to the neighbour.
                    if (RegionMap.RegionAt(x, z) != r) continue;

                    float y = TerrainUtility.GetHeight(x, z);
                    SupplyNode.Create(em, new float3(x, y, z));
                    placed++;
                }
            }
            TWBLog.Log($"[SupplyNodeBootstrap] {placed} fallback supply node(s) " +
                       $"across {RegionMap.Count} territories.");
        }
    }
}
