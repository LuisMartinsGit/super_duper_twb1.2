using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Tuning numbers for <see cref="AIPivotalReserve"/>. The asset is AIPivotalReserve.asset,
    /// beside AIPivotalReserve.cs.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/AI/AIPivotalReserve",
                     fileName = "AIPivotalReserve")]
    public sealed class AIPivotalReserveConfig : ScriptableObject, IComponentConfig
    {
        static AIPivotalReserveConfig _i;
        /// <summary>The one instance, for the static AI classes that read it.</summary>
        public static AIPivotalReserveConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<AIPivotalReserveConfig>());

        /// <summary>Headroom above the summed reserve before discretionary
        /// spending resumes — the pivotal purchase must land before the
        /// next competitor drains the bank again.</summary>
        public int pad;

        /// <summary>Longest continuous hold per faction. FAMINE RELEASE
        /// (2026-08-11, 67-min match): every iron deposit on the map ran
        /// dry by minute 30, the Temple-L3 reserve (350 iron) became
        /// unfillable, and the hold froze walls, towers, expansion and army
        /// growth for the remaining 37 minutes — a total economic deadlock.
        /// Saving only makes sense while income can actually fill the
        /// reserve; past this window the hold releases (the reserve entry
        /// stays, so the purchase still fires the moment it ever becomes
        /// affordable).</summary>
        // 90 -> 240 (2026-08-31): 90 s was shorter than the time territory
        // income needs to accumulate an expansion Hall (600 supplies), so
        // the hold always lapsed and the claim never happened — 12 matches,
        // zero Halls. With army training now pausing under the hold
        // (SimpleAISystem.Production), the bank fills in well under this
        // ceiling; the ceiling only catches a genuinely starved faction.
        public float maxHoldSeconds;

        /// <summary>Breathing window between holds — see the duty cycle in
        /// <see cref="ShouldHold"/>.</summary>
        public float releaseSeconds;
    }
}
