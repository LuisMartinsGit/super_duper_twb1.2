// UnitRankSystem.cs
// Applies the per-rank stat diff to military units (Damage, Defense, LineOfSight)
// and ticks Lv4+ HP regen. Pairs with UnitRankCommandHelper for the upgrade
// transaction and UnitRankDeathEffectsSystem for the on-death AOE.
//
// Stamp pattern (mirrors SectFortitudeHpSystem): UnitRankApplied tracks the
// last-applied rank. Each tick, units whose UnitRank.Value > UnitRankApplied.Value
// get the (newFactor / oldFactor) diff applied and the stamp bumped.
//
// Audit fix #1.
//
// Location: Assets/Scripts/Systems/Combat/UnitRankSystem.cs

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct UnitRankSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnitTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // ── Rank stamping + stat diffs ─────────────────────────────────
            foreach (var (rank, entity) in SystemAPI
                .Query<RefRO<UnitRank>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                byte newRank = rank.ValueRO.Value;
                if (newRank < 1) newRank = 1;

                byte oldRank = 0;
                if (em.HasComponent<UnitRankApplied>(entity))
                    oldRank = em.GetComponentData<UnitRankApplied>(entity).Value;

                if (newRank == oldRank) continue;

                ApplyRankDiff(em, entity, oldRank, newRank);
                if (em.HasComponent<UnitRankApplied>(entity))
                    em.SetComponentData(entity, new UnitRankApplied { Value = newRank });
                else
                    em.AddComponentData(entity, new UnitRankApplied { Value = newRank });
            }

            // ── Lv 4+ HP regen ────────────────────────────────────────────
            foreach (var (rank, health) in SystemAPI
                .Query<RefRO<UnitRank>, RefRW<Health>>()
                .WithAll<UnitTag>())
            {
                if (rank.ValueRO.Value < 4) continue;
                if (health.ValueRO.Value <= 0) continue;
                if (health.ValueRO.Value >= health.ValueRO.Max) continue;

                float regen = UnitRankConfig.Lv4HpRegenPerSecond * dt;
                int delta = (int)math.ceil(regen);
                if (delta < 1) delta = 0; // sub-1 rounds to nothing — accumulates over multiple ticks
                if (delta > 0)
                    health.ValueRW.Value = math.min(health.ValueRO.Max, health.ValueRO.Value + delta);
            }

            // ── Glow Ability tick ─────────────────────────────────────────
            foreach (var (glow, health, entity) in SystemAPI
                .Query<RefRW<GlowAbilityState>, RefRW<Health>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                if (glow.ValueRO.ActiveRemaining > 0f)
                {
                    glow.ValueRW.ActiveRemaining = math.max(0f, glow.ValueRO.ActiveRemaining - dt);
                    // Burst HP regen while active.
                    if (health.ValueRO.Value > 0 && health.ValueRO.Value < health.ValueRO.Max)
                    {
                        int bonus = (int)math.ceil(UnitRankConfig.GlowAbilityRegenPerSec * dt);
                        if (bonus > 0)
                            health.ValueRW.Value = math.min(health.ValueRO.Max, health.ValueRO.Value + bonus);
                    }
                }
                if (glow.ValueRO.CooldownRemaining > 0f)
                {
                    glow.ValueRW.CooldownRemaining = math.max(0f, glow.ValueRO.CooldownRemaining - dt);
                }
            }
        }

        private static void ApplyRankDiff(EntityManager em, Entity entity,
            byte oldRank, byte newRank)
        {
            float atkDiff = UnitRankConfig.AttackMultiplierFor(newRank)
                          / UnitRankConfig.AttackMultiplierFor(oldRank == 0 ? (byte)1 : oldRank);
            float defDiff = UnitRankConfig.DefenseMultiplierFor(newRank)
                          / UnitRankConfig.DefenseMultiplierFor(oldRank == 0 ? (byte)1 : oldRank);
            float losDiff = UnitRankConfig.LineOfSightMultiplierFor(newRank)
                          / UnitRankConfig.LineOfSightMultiplierFor(oldRank == 0 ? (byte)1 : oldRank);

            if (math.abs(atkDiff - 1f) > 0.001f && em.HasComponent<Damage>(entity))
            {
                var d = em.GetComponentData<Damage>(entity);
                d.Value = (int)(d.Value * atkDiff);
                em.SetComponentData(entity, d);
            }

            if (math.abs(defDiff - 1f) > 0.001f && em.HasComponent<Defense>(entity))
            {
                var def = em.GetComponentData<Defense>(entity);
                def.Melee  = (int)(def.Melee  * defDiff);
                def.Ranged = (int)(def.Ranged * defDiff);
                def.Siege  = (int)(def.Siege  * defDiff);
                def.Magic  = (int)(def.Magic  * defDiff);
                em.SetComponentData(entity, def);
            }

            if (math.abs(losDiff - 1f) > 0.001f && em.HasComponent<LineOfSight>(entity))
            {
                var los = em.GetComponentData<LineOfSight>(entity);
                los.Radius *= losDiff;
                em.SetComponentData(entity, los);
            }
        }
    }
}
