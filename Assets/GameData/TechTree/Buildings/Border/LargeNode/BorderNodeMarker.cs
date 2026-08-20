// BorderNodeMarker.cs
// Place in the scene to switch the curse on for this map, and — with
// AuthoredPosition ticked — to say exactly where its wells stand.
//
// TWO MODES, one marker (2026-08-12):
//
//   * ON/OFF LEVER (default, AuthoredPosition = false)
//     Marker presence enables the Border faction; positions are ignored and
//     BorderNodeBootstrap spawns the canonical FOUR CORNER WELLS (design
//     2026-08-11 — the curse presses in from the edges and every spawn reads
//     its nearest corner as "its" well). One marker anywhere is enough.
//
//   * AUTHORED WELLS (AuthoredPosition = true)
//     The map takes over: one well per ticked marker, at that marker's
//     position, and N is whatever the author placed. Use it when the well
//     layout IS the map — a single well in the middle of a duel map, or one
//     well per bridgehead on a river map, where corner wells would say
//     nothing about the ground being fought over.
//
// Mixing is not a thing: as soon as ONE marker is ticked, every un-ticked
// marker is ignored (with a warning). A half-authored well set would be a
// silent balance change nobody asked for.
//
// Well positions matter for balance either way, so on hand-authored maps
// prefer authored wells and put them where the fight should be.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class BorderNodeMarker : MapMarker
    {
        [Tooltip("Spawn a well AT THIS MARKER instead of using the four " +
                 "map-corner defaults. Ticking any marker in the scene makes " +
                 "the ticked markers the map's complete well list.")]
        public bool AuthoredPosition = false;

        // A wild well spreads BorderConstants.MainNodeSpreadRadius (22 m) of
        // haze around itself within the first minute, so the ring previews the
        // ground that will be cursed — the thing that actually decides whether
        // a well is "next to" a bridge or a base.
        protected override float GizmoRadius => 22f;

        protected override Color GizmoColor => new Color(0.75f, 0.30f, 0.95f, 1f); // border-violet

        protected override string GizmoLabel =>
            AuthoredPosition ? "Well (authored position)" : "Curse ON (corner wells)";
    }
}
