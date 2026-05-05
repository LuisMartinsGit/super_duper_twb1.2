// SectRenewalAutoRepairSystem.cs
// Implements Renewal's Lv I "Hands That Mend" passive (the auto-repair half):
// buildings owned by a Renewal faction restore HP at 12% of MaxHP per minute
// once they've been out of combat for at least OutOfCombatThreshold seconds.
//
// "Out of combat" is read from BuildingDamageState.LastDamagedAt (stamped by
// CombatDamageHelper.TrackLastDamager + ProjectileSystem.ApplyDamage). A
// building with no BuildingDamageState component is treated as "never been
// damaged" — eligible immediately.
//
// The unit-refund half of the passive (12% of cost back when one of your
// units dies) is deferred to a later phase — it overlaps with Ruin's refund
// rule and needs the no-stack guard to be designed first.
//
// task-063 phase 2c.
//
// Location: Assets/Scripts/Systems/Sect/SectRenewalAutoRepairSystem.cs

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SectRenewalAutoRepairSystem : ISystem
    {
        // Lv I tuning. Phase 4: 25% / 40% per minute for Lv II / Lv III.
        private const float RepairRatePerMinute = 0.12f;            // 12% of MaxHP per minute
        private const float OutOfCombatThreshold = 5.0f;             // seconds without taking damage
        private const float TickInterval = 0.5f;                     // half-second tick (240 buildings @ 60fps would be wasteful)

        // Per-system tick accumulator.
        private float _tickTimer;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildingTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            _tickTimer += dt;
            if (_tickTimer < TickInterval) return;
            float effectiveDt = _tickTimer;
            _tickTimer = 0f;

            var em = state.EntityManager;
            double now = SystemAPI.Time.ElapsedTime;

            // Convert per-minute rate to per-tick HP delta.
            // tickHp = MaxHP * 0.12 * (effectiveDt / 60).
            float perTickFraction = RepairRatePerMinute * (effectiveDt / 60f);

            foreach (var (health, faction, entity) in SystemAPI
                .Query<RefRW<Health>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value <= 0) continue;
                if (health.ValueRO.Value >= health.ValueRO.Max) continue;

                if (!SectQuery.IsAdoptedAtLeast(em, faction.ValueRO.Value,
                        SectConfig.Renewal, SectLeverKind.Passive)) continue;

                // Out-of-combat gate: skip if damaged within the last threshold.
                if (em.HasComponent<BuildingDamageState>(entity))
                {
                    var dmg = em.GetComponentData<BuildingDamageState>(entity);
                    if (now - dmg.LastDamagedAt < OutOfCombatThreshold) continue;
                }

                // Tick repair. Round up so a low-Max building still gets at least 1 HP per tick.
                int delta = (int)math.ceil(health.ValueRO.Max * perTickFraction);
                if (delta < 1) delta = 1;

                int newHp = math.min(health.ValueRO.Max, health.ValueRO.Value + delta);
                health.ValueRW.Value = newHp;
            }
        }
    }
}
