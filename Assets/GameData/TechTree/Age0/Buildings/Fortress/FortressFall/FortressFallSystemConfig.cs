using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Tuning numbers for <see cref="FortressFallSystem"/>: how hard the one
    /// Fortress is to bring down (decision 69, 2026-11-17). The asset is
    /// FortressFallSystem.asset, beside FortressFallSystem.cs. The rebuild
    /// grace is on EliminationSystem.asset, which owns the elimination.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// Simulation data: every lockstep peer loads the same asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Combat/FortressFallSystem",
                     fileName = "FortressFallSystem")]
    public sealed class FortressFallSystemConfig : ScriptableObject, IComponentConfig
    {
        static FortressFallSystemConfig _i;
        /// <summary>The one instance.</summary>
        public static FortressFallSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<FortressFallSystemConfig>());

        /// <summary>Seconds between two rubble sweeps (rebuilt or out).</summary>
        public float scanIntervalSeconds;

        /// <summary>Share of a SIEGE hit that lands on the Fortress. Every
        /// engine's hit lands, so the damage grows with the siege brought.</summary>
        public float siegeShare;

        /// <summary>Share of a RANGED (non-siege) hit that lands: arrows and
        /// bolts barely scratch stone (the Age of Empires IV model).</summary>
        public float rangedShare;

        /// <summary>Share of a MELEE hit (and magic, true damage, the curse's
        /// blows) that lands: soldiers can burn a Fortress, slowly.</summary>
        public float meleeShare;

        /// <summary>Share of its price a fallen keystone (Fortress, Temple,
        /// landmark, sect building) costs to build again on its rubble.</summary>
        public float rubbleRebuildCostShare;
    }
}
