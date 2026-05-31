// CrystalPatchMarker.cs
// Place in the scene to mark where CrystalPatchBootstrap should spawn a
// patch of mineable crystal cadavers at game start. When any
// CrystalPatchMarker exists, the procedural near + scattered placement
// loop is disabled entirely.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class CrystalPatchMarker : MapMarker
    {
        [Tooltip("Number of crystal cadaver nodes in this patch. Default 30 " +
                 "matches the procedural NEAR-patch size.")]
        [Min(1)] public int NodeCount = 30;

        [Tooltip("Crystal carried by each cadaver. Default 30 → patch total " +
                 "= NodeCount × CrystalPerNode (e.g. 30×30 = 900 starter crystal).")]
        [Min(1)] public int CrystalPerNode = 30;

        [Tooltip("Radius (m) the nodes scatter across. Default 7 matches " +
                 "the procedural NEAR patch spread.")]
        [Min(0.5f)] public float Spread = 7f;

        [Tooltip("Hex-grid layout (dense, even field) vs random tight cluster. " +
                 "Hex-grid suits large patches; random suits small outcrops.")]
        public PatchLayout Layout = PatchLayout.HexGrid;

        protected override float GizmoRadius => Spread;
        protected override Color GizmoColor => new Color(0.45f, 0.85f, 1.0f, 1f); // crystal-cyan
        protected override string GizmoLabel =>
            $"Crystal — {NodeCount} × {CrystalPerNode} ({NodeCount * CrystalPerNode} total)";
    }
}
