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
        [Min(1)] public int NodeCount = 30;

        [FormerlySerializedAs("CrystalPerNode")]
        [Min(1)] public int VeilstonePerNode = 30;

        [Min(0.5f)] public float Spread = 7f;

        public PatchLayout Layout = PatchLayout.HexGrid;

        protected override float GizmoRadius => Spread;
        protected override Color GizmoColor => new Color(0.45f, 0.85f, 1.0f, 1f); // veilstone-cyan
        protected override string GizmoLabel =>
            $"Veilstone — {NodeCount} × {VeilstonePerNode} ({NodeCount * VeilstonePerNode} total)";
    }
}
