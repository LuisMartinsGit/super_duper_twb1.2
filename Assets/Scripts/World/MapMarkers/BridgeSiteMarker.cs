// BridgeSiteMarker.cs
// A place a river must be crossable.
//
// The procedural generator drops one wherever a river region would otherwise
// cut a player off from the rest of the map. It is authored DATA, not the
// bridge itself: the crossing is stated here, and building the OverpassBridge
// that spans it is a separate pass.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class BridgeSiteMarker : MapMarker
    {
        /// <summary>The two regions this crossing joins.</summary>
        public int RegionA = -1;
        public int RegionB = -1;

        /// <summary>Span in metres — how wide the water is here.</summary>
        [Min(1f)] public float Span = 12f;

        /// <summary>Bearing across the water, degrees.</summary>
        public float Yaw;

        protected override float GizmoRadius => Span * 0.5f;
        protected override Color GizmoColor => new Color(0.85f, 0.7f, 0.35f);
    }
}
