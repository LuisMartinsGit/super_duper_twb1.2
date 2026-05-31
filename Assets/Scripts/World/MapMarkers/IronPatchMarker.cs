// IronPatchMarker.cs
// Place in the scene to mark where IronDepositBootstrap should spawn a
// patch of iron deposits at game start. When any IronPatchMarker exists,
// the procedural near + scattered placement loop is disabled entirely;
// markers are the single source of truth.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class IronPatchMarker : MapMarker
    {
        [Tooltip("Number of iron deposits in this patch. Default 30 matches " +
                 "the procedural NEAR-patch size; use ~3 for a small outcrop.")]
        [Min(1)] public int DepositCount = 30;

        [Tooltip("Radius (m) the deposits scatter across. Default 7 matches " +
                 "the procedural NEAR patch spread.")]
        [Min(0.5f)] public float Spread = 7f;

        [Tooltip("Hex-grid layout (dense, even field) vs random tight cluster. " +
                 "Hex-grid suits large patches; random suits small outcrops.")]
        public PatchLayout Layout = PatchLayout.HexGrid;

        protected override float GizmoRadius => Spread;
        protected override Color GizmoColor => new Color(0.85f, 0.55f, 0.20f, 1f); // iron-orange
        protected override string GizmoLabel => $"Iron — {DepositCount} × {Spread:F0}m";
    }

    public enum PatchLayout
    {
        HexGrid = 0,
        RandomCluster = 1
    }
}
