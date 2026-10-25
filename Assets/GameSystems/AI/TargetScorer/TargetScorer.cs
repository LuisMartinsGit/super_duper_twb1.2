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
// workers > huts > nodes > halls ladder) and, in weakest-player form, by
// BorderArmyAISystem.PickTarget.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Data.AI;

namespace TheWaningBorder.AI
{
    public static class TargetScorer
    {
        public static float Score(
            EntityManager em, AISettingsSO s, float riskMultiplier,
            float3 origin, in EnemySightingRecord rec, float now, Faction observer)
        {
            float score = s.CategoryWeight(rec.Category);

            // FOG-HONEST (2026-10-25, Game_AI.md § 6v): the live defenders are
            // read only while the place is in the observer's vision; otherwise
            // the score uses what the sighting recorded there (a building's
            // record carries the garrison seen round it).
            var fog = TheWaningBorder.World.FogOfWar.FogOfWarManager.Instance;
            bool inView = fog == null
                || fog.IsVisible(observer, new UnityEngine.Vector3(rec.Position.x, 0f, rec.Position.z));
            int defenders = inView
                ? TacticalQuery.FactionStrengthInRadius(em, rec.OwnerFaction, rec.Position, s.defenseProbeRadius)
                : math.max(0, rec.EstStrength);
            score -= defenders * s.riskPerDefenseStrength * riskMultiplier;

            float dx = rec.Position.x - origin.x;
            float dz = rec.Position.z - origin.z;
            score -= math.sqrt(dx * dx + dz * dz) * s.travelCostPerMeter;

            score -= math.max(0f, now - rec.LastSeenTime) * s.intelAgePenaltyPerSecond;
            return score;
        }
    }
}
