// VeilsteelDepositMarker.cs
// Place in the scene to mark where VeilsteelDepositBootstrap should spawn a
// Veilsteel "Sharp Crystals" node at game start. Unlike iron (patches of
// small deposits), veilsteel is a SINGLE large node per marker.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class VeilsteelDepositMarker : MapMarker
    {
        [Tooltip("Veilsteel units in this node. Design default: 1500.")]
        [Min(1)] public int Amount = 1500;

        protected override float GizmoRadius => 2.5f;
        protected override Color GizmoColor => new Color(0.55f, 0.85f, 0.95f, 1f); // pale steel-blue
        protected override string GizmoLabel => $"Veilsteel — {Amount}";
    }
}
