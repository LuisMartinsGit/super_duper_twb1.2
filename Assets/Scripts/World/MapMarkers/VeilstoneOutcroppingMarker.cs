// VeilstoneOutcroppingMarker.cs
// Place in the scene to mark where VeilstoneOutcroppingBootstrap should spawn a
// patch of mineable veilstone outcroppings at game start. When any
// VeilstoneOutcroppingMarker exists, the procedural near + scattered placement
// loop is disabled entirely.

using UnityEngine;
using UnityEngine.Serialization;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class VeilstoneOutcroppingMarker : MapMarker
    {
        [Tooltip("Number of veilstone outcropping nodes in this patch. Default 30 " +
                 "matches the procedural NEAR-patch size.")]
        [Min(1)] public int NodeCount = 30;

        [Tooltip("Veilstone carried by each outcropping. Default 30 → patch total " +
                 "= NodeCount × VeilstonePerNode (e.g. 30×30 = 900 starter veilstone).")]
        [FormerlySerializedAs("CrystalPerNode")]
        [Min(1)] public int VeilstonePerNode = 30;

        [Tooltip("Radius (m) the nodes scatter across. Default 7 matches " +
                 "the procedural NEAR patch spread.")]
        [Min(0.5f)] public float Spread = 7f;

        [Tooltip("Hex-grid layout (dense, even field) vs random tight cluster. " +
                 "Hex-grid suits large patches; random suits small outcrops.")]
        public PatchLayout Layout = PatchLayout.HexGrid;

        protected override float GizmoRadius => Spread;
        protected override Color GizmoColor => new Color(0.45f, 0.85f, 1.0f, 1f); // veilstone-cyan
        protected override string GizmoLabel =>
            $"Veilstone — {NodeCount} × {VeilstonePerNode} ({NodeCount * VeilstonePerNode} total)";
    }
}
