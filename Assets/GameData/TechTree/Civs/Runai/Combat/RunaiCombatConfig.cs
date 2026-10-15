using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Runai
{
    /// <summary>
    /// Every number of the Runai trainer specials (docs/Design/Age_1_Runai.md
    /// §7.3-7.5). The asset is RunaiCombat.asset beside <see cref="RunaiCombat"/>.
    /// No field initialisers. All placeholders.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Runai/RunaiCombat",
                     fileName = "RunaiCombat")]
    public sealed class RunaiCombatConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Seconds between passes.</summary>
        public float tickSeconds;

        /// <summary>Remounts: seconds without losing health before cavalry heal, and HP per second.</summary>
        public float remountCalmSeconds;
        public float remountHealPerSecond;

        /// <summary>Feigned Retreat: below this HP fraction, cavalry move this much faster.</summary>
        public float feignedRetreatHpFraction;
        public float feignedRetreatSpeedMultiplier;

        /// <summary>Escort Duty: Road Wardens within this distance of a caravan take this damage multiple.</summary>
        public float escortDutyRadius;
        public float escortDutyDamageTaken;

        /// <summary>Wagon Laager: seconds a War Wagon stands still before it braces, and the flat armour it gains.</summary>
        public float laagerStillSeconds;
        public float laagerArmorBonus;
    }
}
