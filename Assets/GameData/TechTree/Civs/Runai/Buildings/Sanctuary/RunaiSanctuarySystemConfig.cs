using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Runai
{
    /// <summary>
    /// Every number of the Sanctuary and its curse ladder
    /// (docs/Design/Age_1_Runai.md §5). The asset is RunaiSanctuarySystem.asset
    /// beside <see cref="RunaiSanctuarySystem"/>. No field initialisers. All
    /// placeholders. A Sanctuary produces only religion points.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Runai/RunaiSanctuarySystem",
                     fileName = "RunaiSanctuarySystem")]
    public sealed class RunaiSanctuarySystemConfig : ScriptableObject, IComponentConfig
    {
        /// <summary>Religion points per minute from a Sanctuary on an Inactive / a Cursed outcrop.</summary>
        public float rpPerMinute;
        public float cursedRpPerMinute;
        /// <summary>Trade RP for veilsteel (§5.1): one trade spends rpPerTrade religion points for veilsteelPerTrade veilsteel.</summary>
        public int rpPerTrade;
        public int veilsteelPerTrade;
        /// <summary>How far from a Sanctuary the outcrop under it is looked for.</summary>
        public float outcropSearchRadius;

        /// <summary>Hushed Veil: the curse's scan radius against the faction, as a fraction.</summary>
        public float hushedScanMultiplier;
        /// <summary>Communion: HP per second for a Runai unit standing on curse-held ground.</summary>
        public float communionHealPerSecond;
        /// <summary>Veilbound: seconds between conversions per Sanctuary, and the reach.</summary>
        public float veilboundIntervalSeconds;
        public float veilboundRadius;
        /// <summary>Voice of the Veil: Veilbound's reach.</summary>
        public float voiceRadius;
    }
}
