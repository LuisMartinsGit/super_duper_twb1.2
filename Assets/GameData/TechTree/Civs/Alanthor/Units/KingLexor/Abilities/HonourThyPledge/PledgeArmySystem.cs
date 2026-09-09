// PledgeArmySystem.cs
// Counts the pledged down and sends them home.
// Canon: docs/Design/Heroes.md §3.
//
// Expiry follows the project's unit-death contract exactly as FieldHospital
// does: set Health to 0 and let DeathSystem destroy the entity, NEVER
// DestroyEntity from here — a direct destroy corrupts the EndSimulation ECB
// playback.
//
// What makes the expiry "clean" in the design sense is therefore not a
// different removal path but a set of exclusions elsewhere:
//   * HeroLevelSystem skips TemporarySummon, so nobody earns XP off them
//   * UnitRankDeathEffectsSystem skips them, so a rank-4 pledge does not
//     detonate when its time simply runs out
// A pledged soldier CUT DOWN in the field still dies an ordinary death — it is
// only running out of time that is quiet.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class PledgeArmySystem : SystemBase
    {
        private EntityQuery _summonQuery;

        protected override void OnCreate()
        {
            _summonQuery = GetEntityQuery(
                ComponentType.ReadWrite<TemporarySummon>(),
                ComponentType.ReadWrite<Health>());
            RequireForUpdate(_summonQuery);
        }

        protected override void OnUpdate()
        {
            // SimCadence, not a bare DeltaTime accumulator: the countdown is
            // sim state, and a machine-dependent starting phase would expire
            // the same army on different ticks on different peers.
            float dt = _cadence.DueStep(World.Time.DeltaTime, TickInterval);
            if (dt <= 0f) return;

            var em = EntityManager;
            using var ents = _summonQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var s = em.GetComponentData<TemporarySummon>(e);

                // Already dying or dead: leave it to the ordinary death path.
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;

                s.TimeToLive -= dt;
                if (s.TimeToLive > 0f)
                {
                    em.SetComponentData(e, s);
                    continue;
                }

                hp.Value = 0;
                em.SetComponentData(e, hp);
            }
        }

        private const float TickInterval = 0.25f;
        private SimCadence.Periodic _cadence;
    }
}
