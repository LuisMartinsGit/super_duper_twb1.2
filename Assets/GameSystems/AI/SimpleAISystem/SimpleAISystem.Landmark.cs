// SimpleAISystem.Landmark.cs
// THE AGE-UP IS THE LANDMARK (docs/Design/Age_0.md § Age-up by landmark,
// 2026-09-29). The AI no longer builds a Shrine and then researches the
// age-up: it picks its culture ONCE (AICultureChoice) and builds that
// culture's landmark. Construction completing is the age-up, so there is no
// second step to save for and nothing to issue.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        /// <summary>The landmark each faction committed to this match.
        /// Picked once so the AI does not flip-flop between cultures while
        /// it saves; cleared on a new match epoch.</summary>
        private static readonly Dictionary<Faction, string> _landmarkPick =
            new Dictionary<Faction, string>();
        private static int _landmarkEpoch = -1;

        /// <summary>
        /// The landmark this AI builds to age up: the culture AICultureChoice
        /// picks (personality + scouted intel + seed), turned into the
        /// building that ages into it. The ship gate is applied by the pick
        /// (CultureConfig.Playable), so the demo always lands on the Vault.
        /// </summary>
        private string AgeUpLandmark(EntityManager em, Faction faction)
        {
            if (_landmarkEpoch != SimCadence.Epoch)
            {
                _landmarkEpoch = SimCadence.Epoch;
                _landmarkPick.Clear();
            }
            if (_landmarkPick.TryGetValue(faction, out var id)) return id;

            byte culture = CultureConfig.Playable(Cultures.Alanthor);
            var brainEntity = FindBrainEntity(em, faction);
            if (brainEntity != Entity.Null)
            {
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                culture = AICultureChoice.Pick(em, faction, brainEntity,
                    brain.Personality, brain.Difficulty, NextRandUint());
            }
            id = culture == Cultures.Feraldis ? "FiendstoneKeep" : "VaultOfAlmierra";
            _landmarkPick[faction] = id;
            AILogger.Log(faction, "CULTURE",
                $"age-up landmark = {id} ({CultureConfig.GetName(culture)})");
            return id;
        }

        /// <summary>A landmark placed — finished or still being built.</summary>
        private static bool FactionHasLandmark(EntityManager em, Faction faction)
            => BuildingFactory.GetFactionChoiceBuilding(em, faction) != null;

        /// <summary>
        /// Any choice-building id an authored build order still names (the
        /// retired Shrine included) means "this faction's landmark".
        /// </summary>
        private string ResolveLandmarkId(EntityManager em, Faction faction, string buildingId)
        {
            if (buildingId == "ShrineOfRidan" || BuildingFactory.IsChoiceBuilding(buildingId))
                return AgeUpLandmark(em, faction);
            return buildingId;
        }
    }
}
