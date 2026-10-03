using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AIBudget"/>. The asset is AIBudget.asset,
    /// beside AIBudget.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIBudget",
                     fileName = "AIBudget")]
    public sealed class AIBudgetConfig : ScriptableObject, IComponentConfig
    {
        static AIBudgetConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AIBudgetConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIBudgetConfig>());

        /// <summary>No weight below this — a wallet may be lean, never dead.</summary>
        public float weightFloor;

        /// <summary>Wallet cap ≈ this many seconds of that resource's income
        /// (windfalls must not let one category hoard forever).</summary>
        public float walletCapSeconds;

        public float walletCapMinimum;

        public float logInterval;

        /// <summary>Seconds of simulated time a military shortage stays on
        /// the record after the last refused purchase. Walls yield while the
        /// army is short of what they cost; Trading Outposts stay on Buy and
        /// claims favour outcrops while it is short of veilstone.</summary>
        public float militaryShortHoldSeconds;
    }
}
