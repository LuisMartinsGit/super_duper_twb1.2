// NatureRegionBootstrap.cs
// Stamps every NatureRegionMarker into the passability grid, so authored
// forests and rock fields actually stop movement.
//
// This closes a live gap rather than adding a feature. Procedural obstacle
// scatter went away with procedural maps and nothing replaced it:
// ObstacleBootstrap.ForestPositions has been empty on every hand-authored map,
// and the only callers of PassabilityGrid.BlockObstacle are the three resource
// node types. Meanwhile the shipped localization has been telling players
// "Forests block sight and movement. Scouts pierce sight through trees but
// still pay the move cost." — which was false on every real map.
//
// Ordering matters: this runs BEFORE reachability is computed in
// SpawnDelayHelper, or the reachability pass would route players through
// forests that are about to become walls.
//
// Design: docs/Design/Territory_And_Nature.md §3.

using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.World.MapMarkers
{
    public static class NatureRegionBootstrap
    {
        /// <summary>
        /// Block the passability cells under every authored nature region.
        /// No-op on maps with no markers, which is every map until one is
        /// authored — so this is safe to call unconditionally.
        /// </summary>
        public static void BlockNatureRegions()
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null)
            {
                Debug.LogWarning("[NatureRegionBootstrap] No PassabilityGrid — nature regions will not block");
                return;
            }

            var regions = MapMarkerRegistry.NatureRegions;
            if (regions.Count == 0) return;

            int blocked = 0;
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                if (r == null) continue;

                // BlockObstacle only overwrites cells that are currently
                // Passable, so a region overlapping a cliff or a building
                // footprint leaves those classifications intact.
                grid.BlockObstacle(r.WorldPosition, Mathf.Max(1f, r.Radius));
                blocked++;
            }

            TWBLog.Log($"[NatureRegionBootstrap] Blocked {blocked} nature region(s)");
        }
    }
}
