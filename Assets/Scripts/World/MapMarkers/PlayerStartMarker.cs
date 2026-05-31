// PlayerStartMarker.cs
// Place one in the scene per faction you want to spawn at a designed
// position. PlayerSpawnSystem will spawn that faction's Hall + 3 Builders
// here instead of using the procedural radial layout.
//
// Lobby slots whose faction has no marker fall back to procedural placement
// (we log a warning). Markers whose faction has no active slot are skipped.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class PlayerStartMarker : MapMarker
    {
        [Tooltip("Which faction spawns here. Must match a Faction selected " +
                 "in the lobby slot for it to take effect.")]
        public Faction Faction = Faction.Blue;

        // Hall footprint is ~4×4 cells (≈8 m square); a 6 m ring gives a
        // realistic preview of where the Hall + Builders will land.
        protected override float GizmoRadius => 6f;

        protected override Color GizmoColor => FactionColors.Get(Faction);

        protected override string GizmoLabel => $"Player Start — {Faction}";
    }
}
