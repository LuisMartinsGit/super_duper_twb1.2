using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.World
{
    /// <summary>
    /// Tunables for <see cref="FogRevealSystem"/> — the shot reveal
    /// (docs/Design/Combat_Pacing.md § A shot gives the shooter away) and the
    /// claim-block reveal (docs/Design/Territory_Claims.md §3.1). The asset is
    /// FogRevealSystem.asset, beside FogRevealSystem.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/World/FogRevealSystem",
                     fileName = "FogRevealSystem")]
    public sealed class FogRevealSystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>How long (s) a shooter stays revealed to the owner of the
        /// unit it last hit with a projectile.</summary>
        public float rangedHitDuration;

        /// <summary>How long (s) one claim tick keeps a blocking structure
        /// revealed. Must exceed the claim tick (TerritoryClaimSystem, one
        /// second) or the reveal flickers between ticks.</summary>
        public float lockRevealDuration;

        /// <summary>Radius (m) of the circle of sight stamped on a revealed
        /// unit. A revealed building's circle also covers its footprint.</summary>
        public float revealRadius;
    }
}
