// SupplyNodeMarker.cs
// Place in the scene to mark where a supply node stands.
//
// A supply node pays NOTHING. It exists to be a placement rule: a Gatherer's
// Hut may only be built on one (docs/Design/Regions.md §4). That single rule
// replaces the two crutches the hut needed before it — a magic per-territory
// cap, and a gather-area yield the player had to survey the ground for. How
// many huts a territory supports is now map data, and it can differ between a
// rich territory and a poor one.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class SupplyNodeMarker : MapMarker
    {
        protected override float GizmoRadius =>
            TheWaningBorder.Entities.SupplyNode.NodeRadius;

        protected override Color GizmoColor => new Color(0.60f, 0.78f, 0.30f, 1f); // grain green

        protected override string GizmoLabel => "Supply Node";
    }
}
