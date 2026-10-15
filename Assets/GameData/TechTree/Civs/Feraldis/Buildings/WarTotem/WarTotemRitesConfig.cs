using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Feraldis
{
    /// <summary>
    /// Totem tiers and rites (docs/Design/Age_1_Feraldis.md §6.2). The asset is
    /// WarTotemRites.asset beside <see cref="WarTotemRites"/>. Blood amounts are
    /// in the totem's own units (TotemFervor: mean blood drunk × the drink scale).
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Feraldis/WarTotemRites",
                     fileName = "WarTotemRites")]
    public sealed class WarTotemRitesConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Lifetime blood that evolves a totem to tier II and to tier III.</summary>
        public float tier2Lifetime;
        public float tier3Lifetime;
        /// <summary>Most stored blood a totem holds.</summary>
        public float storedCap;
        /// <summary>Aura radius (allies affected by the passive and by rites).</summary>
        public float radius;

        /// <summary>Passive: frenzy linger (seconds) inside the radius at tier I, II, III.</summary>
        public float[] lingerByTier;
        /// <summary>Tier III passive: HP a kill inside the radius heals its killer.</summary>
        public int killHeal;

        /// <summary>Rite costs in stored blood, by rite (Bloodlust, Hunger, RedMist, BloodMoon).</summary>
        public float[] riteCost;
        /// <summary>Rite durations (seconds), by rite.</summary>
        public float[] riteSeconds;
        /// <summary>Seconds before the same totem may start another rite.</summary>
        public float riteCooldown;
        /// <summary>Bloodlust: attack-speed multiplier at tier I / II / III.</summary>
        public float[] bloodlustHaste;
        /// <summary>Hunger: HP a second healed on allies in the radius at tier I / II / III.</summary>
        public float[] hungerHealPerSecond;
    }
}
