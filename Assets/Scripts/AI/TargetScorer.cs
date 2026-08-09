// TargetScorer.cs
// Strategic target value assessment (AI plan M2). Scores an intel sighting as
// an attack candidate:
//
//   score = category weight            (what is it worth to hit)
//         - defender strength * risk   (how hard is it defended; personality-scaled)
//         - march distance * travel    (how far do we walk)
//         - sighting age * staleness   (how much do we trust the intel)
//
// Consumed by SimpleAISystem.ChooseAttackTarget (replacing the fixed
// miners > huts > nodes > halls ladder) and, in weakest-player form, by
// BorderArmyAISystem.PickTarget.
//
// Location: Assets/Scripts/AI/TargetScorer.cs

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Data.AI;

namespace TheWaningBorder.AI
{
    public static class TargetScorer
    {
        public static float Score(
            EntityManager em, AISettingsSO s, float riskMultiplier,
            float3 origin, in EnemySightingRecord rec, float now)
        {
            float score = s.CategoryWeight(rec.Category);

            int defenders = TacticalQuery.FactionStrengthInRadius(
                em, rec.OwnerFaction, rec.Position, s.defenseProbeRadius);
            score -= defenders * s.riskPerDefenseStrength * riskMultiplier;

            float dx = rec.Position.x - origin.x;
            float dz = rec.Position.z - origin.z;
            score -= math.sqrt(dx * dx + dz * dz) * s.travelCostPerMeter;

            score -= math.max(0f, now - rec.LastSeenTime) * s.intelAgePenaltyPerSecond;
            return score;
        }
    }
}
