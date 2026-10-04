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

        /// <summary>Share of every think window's veilstone income earmarked
        /// for the army while its composition is behind target (AIBudget
        /// SetMilitaryVeilstoneClaim). Non-military veilstone spenders — the
        /// Fortress, research — must leave the earmark in the bank; they live
        /// on the remainder plus whatever the army does not spend.</summary>
        public float militaryVeilstoneShare;

        /// <summary>The earmark never exceeds this much veilstone (nor the
        /// bank). Sized to cover the dearest single military purchase (King
        /// Lexor, 350) so the army can save for it, without letting an army
        /// that cannot spend lock the whole bank away from the Fortress.</summary>
        public float militaryVeilstoneCreditCap;

        /// <summary>SURPLUS (docs/Design/Game_AI.md 5e). The bank is
        /// OVERFLOWING while it holds at least surplusSupplies supplies AND
        /// surplusIron iron. Overflowing and short of veilstone for the army
        /// (IsMilitaryShort) = veilstone-held surplus: the AI spends on
        /// Trading Outposts, their trade research and veilstone ground;
        /// overflowing alone = more building levels and research per think.
        /// 0 on either field turns surplus spending off.</summary>
        public int surplusSupplies;
        public int surplusIron;

        /// <summary>ARMY FIRST (docs/Design/Game_AI.md 5f): the surplus
        /// spenders (non-production building levels and the research sweep)
        /// yield to the army while alive + queued is below this fraction of
        /// its desired size. 0 turns the gate off.</summary>
        public float armyFirstTargetFraction;

        /// <summary>While the army waits on a resource, a surplus spend that
        /// does not cost that resource must still leave this many of the
        /// army's next unit (capped at the deficit) banked in what it does
        /// cost.</summary>
        public int armyFirstReserveUnits;

        /// <summary>The army reading is ignored once older than this many
        /// seconds (a faction whose floor stopped reporting yields nothing).</summary>
        public float armyFirstStatusMaxAge;

        /// <summary>Seconds between "ARMYFIRST: ... yields" lines per faction.</summary>
        public float armyFirstLogInterval;
    }
}
