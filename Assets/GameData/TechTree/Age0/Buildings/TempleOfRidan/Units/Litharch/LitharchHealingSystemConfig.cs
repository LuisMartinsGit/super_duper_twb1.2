using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Healer-positioning tunables for <see cref="LitharchHealingSystem"/>
    /// (docs/Design/Age_0.md § Litharch, docs/Design/Stances.md § Support
    /// units). The asset is LitharchHealingSystem.asset, beside the system.
    ///
    /// The heal RANGE and RATE are per-unit stats and live in Litharch.asset
    /// (healRange / healsPerSecond); this is only how the healer moves.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Combat/LitharchHealingSystem",
                     fileName = "LitharchHealingSystem")]
    public sealed class LitharchHealingSystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>How far (m) an idle Litharch looks for a wounded friendly
        /// to heal on its own.</summary>
        public float autoSearchRange;

        /// <summary>The Litharch walks to a point this far (m) INSIDE its heal
        /// range from the patient, on the patient→Litharch line — never to the
        /// patient itself.</summary>
        public float standOffMargin;

        /// <summary>A patient with an enemy (its target, or the unit that last
        /// hit it) within this many metres is in melee contact; the auto-search
        /// prefers any patient that is not.</summary>
        public float meleeContactRadius;

        /// <summary>An armed enemy unit within this many metres makes the
        /// Litharch step away.</summary>
        public float threatRadius;

        /// <summary>How far (m) it steps away from that enemy.</summary>
        public float stepAwayDistance;

        /// <summary>How often (s) each Litharch checks for a nearby enemy.</summary>
        public float threatCheckInterval;

        /// <summary>A step-away ends when the Litharch is within this many
        /// metres of its step destination. It also ends after
        /// stepAwayDistance / speed + threatCheckInterval seconds, so a blocked
        /// step cannot freeze the healer.</summary>
        public float stepArriveTolerance;

        /// <summary>A stand point with an armed enemy within threatRadius plus
        /// this many metres is unsafe: the Litharch does not walk to it — an
        /// auto patient is dropped for another, an ordered one is waited on.</summary>
        public float standSafetyMargin;
    }
}
