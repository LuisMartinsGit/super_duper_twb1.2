// SectAdoptionPanel.cs
// IMGUI panel stub for sect adoption — task-063 phase 1.
// Location: Assets/Scripts/UI/Panels/SectAdoptionPanel.cs
//
// task-063 phase 1: the old SectAdoptionPanel rendered the 12 deleted sects
// (Renewal / Antiquity / LivingStone / VeiledMemory / StillFlame / QuietVault
// / MirrorRite / ShardJudgment / EmberAsh / HollowBrand / FlamewroughtChains
// / UnmakersGrasp) and called FactionSectState.GetMultipliers / TryAdopt /
// SectConfig.GetDisplayName / GetPassiveDescription / SynergyPairs — every
// one of which has been removed. The body is reduced to a placeholder so the
// type stays available for callers; the user has flagged a fresh stub UI for
// the next pass.

using UnityEngine;
using TheWaningBorder.UI.Common;

namespace TheWaningBorder.UI.Panels
{
    /// <summary>
    /// Adoption-panel stub. The Phase 1 implementation only renders a
    /// "rebuilding" placeholder line; Phase 1 follow-up will rewrite this
    /// against the new SectAdoption / SectAdoptionState / FactionReligionPoints
    /// API.
    /// </summary>
    public static class SectAdoptionPanel
    {
        /// <summary>
        /// Draw the sect adoption section. Called from the temple panel.
        /// task-063 phase 1: stub — emits a single placeholder label.
        /// </summary>
        public static void Draw(Faction faction)
        {
            Styles.Initialize();
            _ = faction;
            GUILayout.Space(8);
            GUILayout.Label(
                "Sect adoption — rebuilding for the new 12-sect system (task-063 phase 1).",
                Styles.SmallLabel);
        }
    }
}
