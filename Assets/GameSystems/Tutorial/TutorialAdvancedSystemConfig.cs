using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Tutorial
{
    /// <summary>
    /// The Alanthor tutorial mission's numbers (docs/Design/Tutorial_Missions.md).
    /// The asset is TutorialAdvancedSystem.asset, beside TutorialAdvancedSystem.cs.
    /// The sites must match what tools/maps/tutorial_alanthor/layout.py prints.
    ///
    /// No field initialisers: the values live in the asset and nowhere else.
    /// </summary>
    [CreateAssetMenu(menuName = "Waning Border/Component Config/Tutorial/TutorialAdvancedSystem",
                     fileName = "TutorialAdvancedSystem")]
    public sealed class TutorialAdvancedSystemConfig : ScriptableObject, IComponentConfig
    {
        static TutorialAdvancedSystemConfig _i;
        public static TutorialAdvancedSystemConfig I
            => _i != null ? _i : (_i = ComponentConfig.Require<TutorialAdvancedSystemConfig>());

        public string sceneName;
        public string kingUnitId;

        public Vector2 nodePosition;
        public Vector2 rivalSeat;
        public Vector2[] rivalLands;

        /// <summary>The town the mission opens on, around the normal base.</summary>
        public Vector2 litharchOffset;
        public Vector2[] townHutOffsets;
        public Vector2 townBarracksOffset;
        public int townGatherersHuts, townMines;

        /// <summary>Paid at the start, on top of the bank's starting resources.</summary>
        public int grantSupplies, grantIron, grantVeilstone;

        public string archeryRangeId, towerId, templeId;
        /// <summary>Any one of these counts as the Guild survey objective.</summary>
        public string[] surveyTechs;
        public int religionPointsNeeded;
        public float rivalNoteSeconds;
        public float nodeRevealRadius;

        public float firstWaveSeconds, waveIntervalSeconds;
        public int waveCrystallings, waveVeilstingers;
        public float waveSpawnRadius, waveRetargetSeconds;

        /// <summary>Shorter than a match's: enshrinement and Sanctum dominion.</summary>
        public float winCountdownSeconds;

        public string[] deniedUntilReligion;
        public string[] deniedUnits;
    }
}
