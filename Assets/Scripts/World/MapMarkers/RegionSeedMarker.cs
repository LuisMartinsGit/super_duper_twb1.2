// RegionSeedMarker.cs
// One seed per claimable REGION. The map is partitioned by nearest seed
// (Voronoi), so the regions are authored by dragging ~9 markers around rather
// than by drawing polygons.
//
// Why seeds and not polygons: the hand-drawn region lines on a map sketch are
// already, in effect, the boundaries between nearest-centre areas. Encoding the
// centres reproduces them, needs no polygon editor, reshapes correctly when a
// marker moves, and makes "which region is this point in?" a nearest-seed
// search over a handful of candidates instead of point-in-polygon.
//
// Design: docs/Design/Regions.md. This marker carries the region's IDENTITY
// only — who owns it is decided at runtime from the influence map and is never
// authored here.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class RegionSeedMarker : MapMarker
    {
        [Tooltip("Display name for this region — shown in the UI when it flips. " +
                 "Blank falls back to \"Region <n>\".")]
        public string RegionName = "";

        [Tooltip("Build space this region grants its owner, as a share of the " +
                 "region's passable area. Design default 1 — raise only for a " +
                 "region that should be worth fighting for out of proportion " +
                 "to its size.")]
        [Min(0f)] public float ValueMultiplier = 1f;

        // Seeds have no footprint of their own — the gizmo is a locator, and
        // the real extent is wherever this seed is the nearest one. Kept large
        // enough to find at a zoomed-out map view.
        protected override float GizmoRadius => 12f;

        protected override Color GizmoColor => new Color(0.95f, 0.85f, 0.25f);

        protected override string GizmoLabel =>
            string.IsNullOrEmpty(RegionName) ? "Region seed" : $"Region — {RegionName}";
    }
}
