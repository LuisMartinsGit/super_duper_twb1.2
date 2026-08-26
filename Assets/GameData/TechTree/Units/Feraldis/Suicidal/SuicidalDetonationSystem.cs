// The Feraldis Suicidal's blast. Canon: docs/Design/Age_1_Feraldis.md.
//
// Two ways to detonate, and BOTH are wins for Feraldis:
//   1. ARRIVAL — an enemy comes within SuicideCharge.TriggerRadius. Owned by
//      this system.
//   2. DEATH — it is shot down on the approach. Owned by
//      FeraldisDeathInterceptor, called from DeathSystem's pre-death pass,
//      because that is the only point guaranteed to run after every damage
//      source in the frame (a plain [UpdateBefore(DeathSystem)] system could
//      be sorted before the arrow that killed it, and the blast would be
//      silently skipped).
//
// Either way the blast damages enemies in radius and stamps a LARGE blood
// pool, so enemy fire is converted into the ground Feraldis frenzies on and
// its War Totems drink. That is the point of the unit.
//
// SuicideSpent latches so a unit can never detonate twice.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Influence;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class SuicidalDetonationSystem : SystemBase
    {
        private static readonly ComponentType[] VictimTypes =
        {
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadWrite<Health>(),
        };

        private static CachedEntityQuery _victimQuery;

        protected override void OnCreate()
        {
            RequireForUpdate<SuicidalTag>();
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // ONE snapshot per frame, shared by every Suicidal's proximity
            // check. The old per-unit ToEntityArray + per-entity random
            // component reads was O(suicidals x entities) of main-thread
            // lookups every frame — the exact shape logged in Perf.log
            // hitches.
            var query = _victimQuery.Get(em, VictimTypes);
            using var xfs = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var healths = query.ToComponentDataArray<Health>(Allocator.Temp);

            var arrived = new NativeList<Entity>(Allocator.Temp);

            foreach (var (transform, health, faction, charge, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Health>, RefRO<FactionTag>, RefRO<SuicideCharge>>()
                .WithAll<SuicidalTag>()
                .WithNone<SuicideSpent, DeathAnimationState>()
                .WithEntityAccess())
            {
                // Death-triggered blasts belong to FeraldisDeathInterceptor;
                // this system only handles reaching the target alive.
                if (health.ValueRO.Value <= 0) continue;

                var pos = transform.ValueRO.Position;
                float r2 = charge.ValueRO.TriggerRadius * charge.ValueRO.TriggerRadius;
                var owner = faction.ValueRO.Value;

                for (int i = 0; i < xfs.Length; i++)
                {
                    // Allies must not trip the charge. A faction-EQUALITY test
                    // spares only the owner, so in a team game a teammate's
                    // unit walked in and set it off (docs/Design/Teams.md:
                    // AreHostile is the only valid hostility test).
                    if (!Alliances.AreHostile(owner, facs[i].Value)) continue;
                    if (healths[i].Value <= 0) continue;
                    float dx = xfs[i].Position.x - pos.x;
                    float dz = xfs[i].Position.z - pos.z;
                    if (dx * dx + dz * dz > r2) continue;
                    arrived.Add(entity);
                    break;
                }
            }

            // Post-loop: Detonate makes structural changes.
            for (int i = 0; i < arrived.Length; i++)
            {
                var e = arrived[i];
                Detonate(em, e);
                // Arrival IS death — zero HP and let DeathSystem take it.
                var h = em.GetComponentData<Health>(e);
                h.Value = 0;
                em.SetComponentData(e, h);
            }
            arrived.Dispose();
        }

        /// <summary>
        /// Fire the blast for one Suicidal and latch <see cref="SuicideSpent"/>.
        /// Public because FeraldisDeathInterceptor calls it from DeathSystem
        /// for the shot-down case. Makes structural changes — never call it
        /// from inside an entity-query iteration.
        /// </summary>
        public static void Detonate(EntityManager em, Entity suicidal)
        {
            if (!em.HasComponent<SuicideCharge>(suicidal)) return;
            if (em.HasComponent<SuicideSpent>(suicidal)) return;

            var charge = em.GetComponentData<SuicideCharge>(suicidal);
            var center = em.GetComponentData<LocalTransform>(suicidal).Position;
            var owner = em.GetComponentData<FactionTag>(suicidal).Value;
            em.AddComponent<SuicideSpent>(suicidal);

            // --- Blast: enemies only. Feraldis does not friendly-fire its
            //     own charge — massing Suicidals is meant to be viable. ---
            float r2 = charge.BlastRadius * charge.BlastRadius;
            var query = _victimQuery.Get(em, VictimTypes);
            using var entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (e == suicidal) continue;
                // Blast respects alliance, not just ownership — allies were
                // eating the full 45 (docs/Design/Teams.md).
                if (!Alliances.AreHostile(owner, em.GetComponentData<FactionTag>(e).Value)) continue;
                if (em.HasComponent<Invulnerable>(e)) continue;

                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;

                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                if (dx * dx + dz * dz > r2) continue;

                hp.Value = math.max(0, hp.Value - charge.BlastDamage);
                em.SetComponentData(e, hp);

                if (em.HasComponent<LastDamagedByFaction>(e))
                    em.SetComponentData(e, new LastDamagedByFaction { Value = owner });
            }

            // --- The pool. One AddBlood splat is only ~2.5 m across, so the
            //     "large pool" of the design is a centre splat plus a ring. ---
            BloodMap.AddBlood(new UnityEngine.Vector3(center.x, center.y, center.z), charge.BloodAmount);
            for (int i = 0; i < SuicideBloodRingCount; i++)
            {
                float a = math.PI * 2f * i / SuicideBloodRingCount;
                BloodMap.AddBlood(new UnityEngine.Vector3(
                    center.x + math.cos(a) * SuicideBloodRingRadius,
                    center.y,
                    center.z + math.sin(a) * SuicideBloodRingRadius), charge.BloodAmount);
            }

            SimSignals.Ping(center,
                SimPingKind.Combat, 3f, big: true);
        }
    }
}
