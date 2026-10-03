using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.World.Regions
{
    /// <summary>
    /// Tuning numbers for <see cref="TerritoryOwnership"/>'s placement rules
    /// (docs/Design/Regions.md §2). The asset is TerritoryOwnership.asset,
    /// beside TerritoryOwnership.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// Read by the lockstep executor, so every peer must load the SAME asset —
    /// it ships with the build like every other config.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Territory Ownership",
                     fileName = "TerritoryOwnership")]
    public sealed class TerritoryOwnershipConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Metres (XZ) within which one of the placing faction's
        /// workers must stand for a Hall to be placed. Enforced by the
        /// placement ghost and, authoritatively, by the executor at the tick
        /// the order runs (the worker is named in the PlaceBuilding command).</summary>
        public float hallWorkerRange;

        /// <summary>The Hall's escalation step (Regions.md §2 "No territory
        /// hopping"): a Hall costs its base price x (1 + step x N), where N is
        /// the faction's live + under-construction Halls not counting the
        /// starting Fortress. 0.5 = the first expansion Hall at base price,
        /// the second at 1.5x, the third at 2x.</summary>
        public float hallCostStep;

        // ── The ownership meter (docs/Design/Territory_Claims.md §2, §8). ──

        /// <summary>Meter points per second per unit of claim weight.</summary>
        public float claimRate;

        /// <summary>Exponent on a side's summed population in a territory:
        /// 1 = linear, 0.5 = square root (flattens deathballs).</summary>
        public float claimExponent;

        /// <summary>Meter points lost per second by an empty, unbuilt territory.</summary>
        public float decayRate;

        /// <summary>The curse's claim weight multiplier (§6.1).</summary>
        public float curseClaimMultiplier;
    }
}
