using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Tunables for <see cref="ArrowTrailTiers"/>. The asset is
    /// ArrowTrailTiers.asset, beside ArrowTrailTiers.cs. No field initialisers.
    /// Only what the effects do not already control themselves: the Stone /
    /// Iron ribbons are code-built trails, and the tip / hit sizes place an
    /// authored effect on an arrow. Everything about how a tip LOOKS is its
    /// prefab's own Particle System settings — edited in the Inspector, live
    /// (the Arrow Trails scenario's panel selects each part).
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Arrow Trail Tiers",
                     fileName = "ArrowTrailTiers")]
    public sealed class ArrowTrailTiersConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Width multiplier on the Stone-tipped (white) trail ribbon.</summary>
        public float stoneTrailWidth;
        /// <summary>Width multiplier on the Iron-tipped (blue) trail ribbon.</summary>
        public float ironTrailWidth;
        /// <summary>Veilstone-tipped arrows (Lana dark magic).</summary>
        public TipLook veilstone;
        /// <summary>Veilsteel (Shard) -tipped arrows (Lana electric).</summary>
        public TipLook veilsteel;

        /// <summary>An arrow whose head is a projectile effect.</summary>
        [System.Serializable]
        public sealed class TipLook
        {
            /// <summary>The effect that replaces the arrowhead.</summary>
            public GameObject tipPrefab;
            /// <summary>World scale of the whole tip effect on the arrow.</summary>
            public float tipScale;
            /// <summary>World scale of this tier's hit effect (the hit prefab
            /// itself is UnitCombatVfx's).</summary>
            public float hitScale;
            /// <summary>Yaw, degrees, turning the prefab's flight axis onto the
            /// arrow's forward (+Z). Lana's projectiles fly along +X, so their
            /// stretched heads need -90; a camera-facing head does not care.</summary>
            public float tipYaw;
        }

        public TipLook LookFor(ArrowTrailTier tier)
            => tier == ArrowTrailTier.Veilsteel ? veilsteel
             : tier == ArrowTrailTier.Veilstone ? veilstone
             : null;
    }
}
