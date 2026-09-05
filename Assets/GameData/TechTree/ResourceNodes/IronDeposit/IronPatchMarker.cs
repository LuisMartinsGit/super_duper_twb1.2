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
        [Min(1)] public int DepositCount = 30;

        [Min(0.5f)] public float Spread = 7f;

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
