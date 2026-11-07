using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Tutorial
{
    /// <summary>
    /// The Basic tutorial mission's numbers (docs/Design/Tutorial_Missions.md).
    /// The asset is TutorialMissionSystem.asset, beside TutorialMissionSystem.cs.
    /// The sites must match what tools/maps/tutorial_basic/layout.py prints.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Tutorial/TutorialMissionSystem",
                     fileName = "TutorialMissionSystem")]
    public sealed class TutorialMissionSystemConfig : ScriptableObject, IComponentConfig
    {
        static TutorialMissionSystemConfig _i;
        public static TutorialMissionSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<TutorialMissionSystemConfig>());

        /// <summary>The scene the mission plays on.</summary>
        public string sceneName;

        public Vector2 litharchStart;
        public Vector2 campSpot;
        /// <summary>Metres from the camp spot a unit must reach to find it.</summary>
        public float campRadius;
        public Vector2 nodePosition;
        public Vector2[] supplyNodes, ironNodes, veilstoneNodes;
        public Vector2[] workerSites;
        public int workersPerSite;
        public Vector2[] spearmanSites;
        public int spearmenPerSite;
        /// <summary>Metres between units of a lost group and of a wave at its spawn.</summary>
        public float groupSpacing;

        /// <summary>Paid when the camp is found, on top of the bank's starting resources.</summary>
        public int grantSupplies, grantIron, grantVeilstone;

        public int hutsNeeded, gatherersHutsNeeded, veilstoneMinesNeeded, ironMinesNeeded, barracksNeeded;
        public int scoutsNeeded, spearmenToTrain;

        /// <summary>Living Spearmen that open the curse stage.</summary>
        public int harassAtSpearmen;
        public int harassCrystallings;
        public int waveCrystallings, waveVeilstingers;
        public float waveIntervalSeconds;
        /// <summary>Sight kept on the node once it is revealed.</summary>
        public float nodeRevealRadius;
        /// <summary>Seconds between re-aiming wave units that have run out of orders.</summary>
        public float waveRetargetSeconds;

        public string[] campBuildings;
        public string[] settleBuildings;
        public string[] musterUnits;
        public string[] musterResearch;
    }
}
