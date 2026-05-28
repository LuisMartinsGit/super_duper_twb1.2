// CurseNodeMarker.cs
// Place in the scene to mark where CrystalNodeBootstrap should spawn a
// Crystal Curse main node at game start. When any CurseNodeMarker exists,
// the procedural placement loop (with its forest / connectivity / player-
// distance gates) is disabled — markers are the single source of truth.
//
// Curse-main positions matter for balance, so manual placement is the
// recommended workflow on hand-authored maps.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class CurseNodeMarker : MapMarker
    {
        // The curse spreads ~15 m around the node within seconds of spawning,
        // so a 12-15 m ring previews where players will see crystal ground.
        protected override float GizmoRadius => 12f;

        protected override Color GizmoColor => new Color(0.75f, 0.30f, 0.95f, 1f); // curse-violet

        protected override string GizmoLabel => "Curse Main Node";
    }
}
