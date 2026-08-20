// BlightPocketMarker.cs
// Place in the scene to author an Age 0 blight pocket (§2.5b): a patch of
// established veil haze anchored by a destructible SmallNode. Put 1-2 at
// each spawn's base ring — they are the early game's curse content (secure
// yourself, get paid in residue). When any BlightPocketMarker exists, the
// procedural one-per-Hall fallback placement is disabled entirely.

using UnityEngine;

namespace TheWaningBorder.World.MapMarkers
{
    [DisallowMultipleComponent]
    public sealed class BlightPocketMarker : MapMarker
    {
        [Tooltip("Radius (m) of the established haze patch seeded around the " +
                 "SmallNode at match start. Default matches VeilCrustConstants.PocketRadius.")]
        [Min(4f)] public float Radius = 12f;

        protected override float GizmoRadius => Radius;
        protected override Color GizmoColor => new Color(0.55f, 0.25f, 0.8f, 1f); // veil purple
        protected override string GizmoLabel => $"Blight pocket — r{Radius:0}";
    }
}
