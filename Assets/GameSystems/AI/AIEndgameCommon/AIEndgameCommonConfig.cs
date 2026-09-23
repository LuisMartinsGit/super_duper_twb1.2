using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AIEndgameCommon"/>. The asset is AIEndgameCommon.asset,
    /// beside AIEndgameCommon.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIEndgameCommon",
                     fileName = "AIEndgameCommon")]
    public sealed class AIEndgameCommonConfig : ScriptableObject, IComponentConfig
    {
        static AIEndgameCommonConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AIEndgameCommonConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIEndgameCommonConfig>());

        /// <summary>Supplies held back so chapel adoption never eats the
        /// economy's floor. Was duplicated verbatim in both systems.</summary>
        public int chapelReserveSupplies;

        /// <summary>Veilstone counterpart of <see cref="ChapelReserveSupplies"/>.</summary>
        public int chapelReserveVeilstone;

        /// <summary>Standoff radius for a ritual escort ring. Both cultures
        /// converged on 14 m after the 2026-08-07 FFA8 postmortem (escorts
        /// piled onto the well itself and blocked the ritualist); it lived as
        /// two separate literals until this was extracted.</summary>
        public float escortStandoffRadius;

        /// <summary>Curse units within this many metres of a well count as
        /// its defenders; a rite never starts while there are any
        /// (Curse_And_Shardroot.md 2.12).</summary>
        public float wellDefenceRadius;

        /// <summary>Assault force = this many times the defenders counted.</summary>
        public float assaultOdds;

        /// <summary>An assault never marches with fewer than this.</summary>
        public int assaultMinUnits;

        /// <summary>After this faction's rite broke at a well, the well is
        /// off-limits for this long (on top of the eruption itself).</summary>
        public float riteRetrySeconds;
    }
}
