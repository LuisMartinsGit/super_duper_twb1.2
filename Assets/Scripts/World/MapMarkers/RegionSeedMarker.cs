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
        public string RegionName = "";

        [Min(0f)] public float ValueMultiplier = 1f;

        /// <summary>
        /// The territory's OUTLINE, as world XZ points in order. Authored: this
        /// is the shape, not a hint. Leave it empty and the region falls back
        /// to the nearest-seed (Voronoi) cell it has always been.
        ///
        /// Regions.md §3b — territories have FIXED shapes and ownership is the
        /// only variable, so this is baked once and never recomputed.
        /// </summary>
        public Vector2[] Shape = new Vector2[0];

        /// <summary>
        /// What this region IS. Every feature of the map is a region — forests,
        /// water and mountains are not placed inside one, they are one — and the
        /// environment is generated from this plus the shape above.
        /// Regions.md § Region kinds.
        /// </summary>
        public enum RegionKind
        {
            /// <summary>Claimable ground with randomized resource nodes.</summary>
            Normal = 0,
            /// <summary>A start region: the same, with a richer opening node set.</summary>
            PlayerStart = 1,
            /// <summary>Claimable, but planted with trees to the region's shape.</summary>
            Forest = 2,
            /// <summary>Excavated to the shape. Unclaimable, impassable.</summary>
            Water = 3,
            /// <summary>Raised, then noise/erosion. Unclaimable, impassable.</summary>
            Mountain = 4,
            /// <summary>Nothing generated; it simply blocks. Unclaimable.</summary>
            Obstacle = 5,
        }

        public RegionKind Kind = RegionKind.Normal;

        // Seeds have no footprint of their own — the gizmo is a locator, and
        // the real extent is wherever this seed is the nearest one. Kept large
        // enough to find at a zoomed-out map view.
        protected override float GizmoRadius => 12f;

        protected override Color GizmoColor => new Color(0.95f, 0.85f, 0.25f);

        protected override string GizmoLabel =>
            string.IsNullOrEmpty(RegionName) ? "Region seed" : $"Region — {RegionName}";
    }
}
