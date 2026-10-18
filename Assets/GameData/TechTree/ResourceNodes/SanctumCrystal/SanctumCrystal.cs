// SanctumCrystal.cs
// The Sanctum's landmark: a cluster of pale-gold crystal spires on a ring of
// weathered standing stones, one in every SANCTUM territory
// (docs/Design/Territory_Claims.md §11 — the type with no nodes whose holder
// earns +1 Religion Point a minute; look: docs/Design/Art_Direction.md §6.3).
//
// WHY IT IS A SIM ENTITY AND NOT A SCENE PROP. Which territory is a Sanctum is
// often decided at load (RegionSeedMarker.Resources = Auto, resolved from the
// match seed by TerritoryResources.Resolve), so no scene can carry it. It is
// laid by TerritoryResources.Spawn — the deterministic pass every lockstep peer
// runs at load — right after the nodes, and it is an ENTITY so it rides the
// save-game snapshot like every node does (a restored game never re-runs
// Spawn). It has a footprint on purpose: units walking through a 6 m crystal
// read as a bug, and a hut built through it is worse. The footprint is the
// resource-node square (2 x 2 cells), blocked exactly as an iron deposit's is,
// so BlockRestoredObstacles re-blocks it after a load with no extra code.
//
// It is NOT a resource node: no NodeTag the extractors, the curse's
// precipitation or the AI's node finders look for. It changes no rule — the
// Sanctum's +1 RP/min stays SanctumSystem's, keyed on the territory type.
//
// SITING. Nearest-first from the territory's centroid on the 2 m grid (one
// fixed order, so every peer lands on the same site): the node-site rule
// (ResourceNodeSite.TryResolve — buildable ground, outside every start
// clearing, a clear cell from every node), plus a walkable ring of the
// territory's own ground all around it and no node near it, so it never
// plugs a ramp or crowds a node. A territory with no such ground gets the
// plain node-site rule; with none of that, no crystal (logged).

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

/// <summary>Marks the Sanctum landmark entity (SanctumCrystal.cs).</summary>
public struct SanctumCrystalTag : IComponentData { }

namespace TheWaningBorder.Entities
{
    public static class SanctumCrystal
    {
        /// <summary>Presentation id — must match PresentationSpawnSystem's
        /// dispatch. 408 is the next free id in the 400 map-feature block.</summary>
        public const int PresentationID = 408;

        /// <summary>How far out the search walks from the centroid, in cells (60 m).</summary>
        private const int SearchCells = 30;
        /// <summary>Radii of the walkable ring the site must keep (metres from its centre).</summary>
        private static readonly float[] ClearRings = { 4.5f, 7f };
        private const int RingSamples = 16;
        /// <summary>No node's square within this of the crystal's centre (half-extent, metres).</summary>
        private const float NodeKeepOut = 7f;

        private static int2[] _offsets;

        /// <summary>
        /// One crystal in every Sanctum territory. Called by
        /// <see cref="TerritoryResources.Spawn"/> after the nodes are laid, with
        /// its territory centroids. Territories are walked in index order.
        /// </summary>
        public static void LayAll(EntityManager em, float3[] centres)
        {
            int laid = 0, missed = 0;
            for (int t = 0; t < RegionMap.Count && t < centres.Length; t++)
            {
                if (!TerritoryResources.IsSanctum(t)) continue;
                if (TryFindSite(em, t, centres[t], strict: true, out var site)
                    || TryFindSite(em, t, centres[t], strict: false, out site))
                {
                    Create(em, site);
                    laid++;
                }
                else
                {
                    missed++;
                    Debug.LogWarning($"[SanctumCrystal] no legal ground for the landmark in {RegionMap.NameOf(t)} — not placed.");
                }
            }
            if (laid + missed > 0)
                Debug.Log($"[SanctumCrystal] {laid} Sanctum landmark(s) placed" + (missed > 0 ? $", {missed} missed." : "."));
        }

        public static Entity Create(EntityManager em, float3 position)
        {
            var entity = em.CreateEntity(
                typeof(SanctumCrystalTag),
                // Units route around it and nothing is built on it — the same
                // block an iron deposit's footprint gets.
                typeof(ObstacleTag),
                typeof(LocalTransform),
                typeof(Radius),
                typeof(PresentationId)
            );
            em.SetComponentData(entity, LocalTransform.FromPositionRotationScale(
                position, quaternion.identity, 1f));
            em.SetComponentData(entity, new Radius { Value = BuildGrid.ResourceNodeHalf });
            em.SetComponentData(entity, new PresentationId { Id = PresentationID });
            em.AddComponentData(entity, new NodeFootprint { Meters = BuildGrid.ResourceNodeMeters });

            PassabilityGrid.Instance?.BlockObstacle(position, BuildGrid.ResourceNodeBlockRadius);
            return entity;
        }

        private static bool TryFindSite(EntityManager em, int t, float3 centre, bool strict, out float3 site)
        {
            site = default;
            var grid = PassabilityGrid.Instance;
            var start = BuildGrid.SnapResourceNode(centre);
            var offsets = Offsets();
            for (int k = 0; k < offsets.Length; k++)
            {
                var d = start;
                d.x += offsets[k].x * BuildGrid.CellSize;
                d.z += offsets[k].y * BuildGrid.CellSize;
                if (RegionMap.RegionAt(d.x, d.z) != t) continue;

                if (strict)
                {
                    if (!RingClear(grid, t, d)) continue;
                    var keepMin = new float2(d.x - NodeKeepOut, d.z - NodeKeepOut);
                    var keepMax = new float2(d.x + NodeKeepOut, d.z + NodeKeepOut);
                    if (ResourceNodeSite.OverlapsNode(em, keepMin, keepMax, null)) continue;
                }

                d.y = TerrainUtility.GetHeight(d.x, d.z);
                // The node-site rule, and only where it leaves the spot where
                // it is — a moved site would skip the ring test above.
                if (!ResourceNodeSite.TryResolve(em, d, out var resolved)) continue;
                if (math.distancesq(resolved.xz, d.xz) > 0.01f) continue;
                site = resolved;
                return true;
            }
            return false;
        }

        /// <summary>Every ring sample is the territory's own walkable ground.</summary>
        private static bool RingClear(PassabilityGrid grid, int t, float3 c)
        {
            for (int r = 0; r < ClearRings.Length; r++)
                for (int s = 0; s < RingSamples; s++)
                {
                    float a = s * (math.PI * 2f / RingSamples);
                    var p = new float3(c.x + math.cos(a) * ClearRings[r], 0f, c.z + math.sin(a) * ClearRings[r]);
                    if (RegionMap.RegionAt(p.x, p.z) != t) return false;
                    if (grid != null && !grid.IsPassable(p)) return false;
                }
            return true;
        }

        /// <summary>Grid offsets nearest-first, ties by z then x — one order on every peer.</summary>
        private static int2[] Offsets()
        {
            if (_offsets != null) return _offsets;
            int n = SearchCells, side = 2 * n + 1, k = 0;
            var list = new int2[side * side];
            for (int z = -n; z <= n; z++)
                for (int x = -n; x <= n; x++)
                    list[k++] = new int2(x, z);
            System.Array.Sort(list, (a, b) =>
            {
                int da = a.x * a.x + a.y * a.y, db = b.x * b.x + b.y * b.y;
                if (da != db) return da.CompareTo(db);
                if (a.y != b.y) return a.y.CompareTo(b.y);
                return a.x.CompareTo(b.x);
            });
            _offsets = list;
            return list;
        }
    }
}
