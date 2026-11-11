// AbilityBuildingSite.cs
// BUILDINGS RAISED BY ABILITIES are placed like any building
// (docs/Design/Build_Grid.md § Buildings raised by abilities, 2026-11-11):
// the aim is snapped to the 2 m build grid by the building's own footprint,
// and the snapped site answers to the ordinary placement rules. One test, three
// askers: the placement ghost (GroundPlacement), the AI's site picker and the
// simulation, which re-runs it on every peer before anything is spent — so a
// red ghost is exactly a refused cast, and a cast from the AI or a replay lands
// on the grid too.
//
// Which rules apply is the ability's call (the Ground argument); each ability
// file says which it uses and why. Reads replicated simulation state and the
// map only, so every peer reaches the same verdict.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Entities
{
    public static class AbilityBuildingSite
    {
        /// <summary>Which ground an ability's building may stand on.</summary>
        public enum Ground : byte
        {
            /// <summary>The geometry rules only (map edge, terrain, overlap,
            /// resource nodes, obstacles) — cursed ground and anyone's
            /// territory allowed. The Cleansing Ward.</summary>
            GeometryOnly,
            /// <summary>The geometry rules and the veil-crust rule — anywhere
            /// a building could stand, whoever holds it.</summary>
            Buildable,
            /// <summary>Everything a worker's building answers to: the
            /// territory gate (own ground only), the crust rule and the
            /// geometry.</summary>
            OwnTerritory,
        }

        /// <summary>Where a building of <paramref name="sizeMeters"/> aimed at
        /// <paramref name="aim"/> stands: on the build grid (Build_Grid.md §1
        /// snap rule), at the ground's height there.</summary>
        public static float3 Snap(float3 aim, int2 sizeMeters)
        {
            float3 site = BuildGrid.Snap(aim, sizeMeters);
            site.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(site.x, site.z);
            return site;
        }

        /// <summary>
        /// Null when a building of <paramref name="sizeMeters"/> may stand at the
        /// SNAPPED <paramref name="site"/> for <paramref name="faction"/>, else
        /// the (localised) reason it may not. <paramref name="buildingId"/> (may
        /// be null) lets the rules make their per-building exceptions.
        /// Never on one of the faction's own plans.
        /// </summary>
        public static string Refusal(EntityManager em, Faction faction, float3 site, int2 sizeMeters,
            string buildingId, Ground ground)
        {
            PlacementRefusal r;
            if (ground == Ground.OwnTerritory)
            {
                r = TerritoryOwnership.TerritoryRefusal(em, faction, buildingId, site.x, site.z);
                if (r != PlacementRefusal.None) return PlacementRefusalText.Of(r, buildingId);
            }
            r = ground == Ground.GeometryOnly
                ? BuildCommandHelper.CheckSiteGeometry(em, site, sizeMeters, buildingId)
                : BuildCommandHelper.CheckBuildPosition(em, site, sizeMeters, buildingId);
            if (r != PlacementRefusal.None) return PlacementRefusalText.Of(r, buildingId);
            if (PlannedBuildings.OverlapsOwnPlan(em, faction, site, sizeMeters))
                return PlacementRefusalText.Of(PlacementRefusal.Overlap, buildingId);
            return null;
        }

        /// <summary>A site judge: null = legal, else the reason.</summary>
        public delegate string Judge(float3 snappedSite);

        /// <summary>
        /// A legal snapped site near <paramref name="aim"/> within
        /// <paramref name="searchRadius"/>: offsets one build cell apart,
        /// searched ring by ring outward, the nearest legal one of the first
        /// ring that has any (ties broken by scan order, so every run picks
        /// the same one). For the AI's pickers: a cast it sends lands exactly
        /// where this says, because the simulation asks the same judge.
        /// </summary>
        public static bool TryNearest(float3 aim, int2 sizeMeters, float searchRadius, Judge judge,
            out float3 site)
        {
            site = default;
            float step = BuildGrid.CellSize;
            int n = (int)math.floor(math.max(0f, searchRadius) / step);
            float r2 = searchRadius * searchRadius;
            for (int ring = 0; ring <= n; ring++)
            {
                float best = float.MaxValue;
                for (int dz = -ring; dz <= ring; dz++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (math.max(math.abs(dx), math.abs(dz)) != ring) continue;   // the ring's edge only
                        float ox = dx * step, oz = dz * step;
                        float d2 = ox * ox + oz * oz;
                        if (d2 > r2 + 0.001f || d2 >= best) continue;
                        float3 p = Snap(new float3(aim.x + ox, aim.y, aim.z + oz), sizeMeters);
                        if (judge(p) != null) continue;
                        best = d2;
                        site = p;
                    }
                }
                if (best < float.MaxValue) return true;
            }
            return false;
        }
    }
}
