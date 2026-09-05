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
    }
}
