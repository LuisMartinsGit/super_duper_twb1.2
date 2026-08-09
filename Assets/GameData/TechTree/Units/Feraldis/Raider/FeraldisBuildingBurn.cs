// File: Assets/GameData/TechTree/Units/Feraldis/Raider/FeraldisBuildingBurn.cs
// The Raider's mark on enemy structures. Canon: docs/Design/Age_1_Feraldis.md.
//
// Bleeding is deliberately units-only (buildings don't bleed), so the
// Raider's "damage over time on enemy buildings" needs its own carrier.
// Same shape as Bleeding otherwise: refresh-never-stack, fractional DPS on
// an integer Health via an accumulator, and it NEVER destroys the entity —
// it drives Health to 0 and DeathSystem does the rest.
//
// Flavour: a Raider doesn't siege a structure down, it rides past and
// leaves it burning.

using Unity.Entities;

/// <summary>Declares that this unit's hits leave enemy BUILDINGS burning.</summary>
public struct InflictsBuildingBurn : IComponentData
{
    public float DamagePerSecond;
    public float Duration;
}

/// <summary>An enemy structure currently burning from a Raider strike.</summary>
public struct BuildingBurn : IComponentData
{
    public float DamagePerSecond;
    public float Remaining;
    public Faction Source;
    public float Accumulator;
}

namespace TheWaningBorder.Systems.Combat
{
    public static class FeraldisBuildingBurn
    {
        /// <summary>
        /// Apply the attacker's declared building burn to a structure target.
        /// No-op for attackers without <see cref="InflictsBuildingBurn"/> and
        /// for non-building victims.
        /// </summary>
        public static void ApplyFrom(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity victim, Faction source)
        {
            if (!em.HasComponent<InflictsBuildingBurn>(attacker)) return;
            if (victim == Entity.Null || !em.Exists(victim)) return;
            if (!em.HasComponent<BuildingTag>(victim)) return;
            if (!em.HasComponent<Health>(victim)) return;
            if (em.HasComponent<BuildingCollapseState>(victim)) return;

            var spec = em.GetComponentData<InflictsBuildingBurn>(attacker);
            if (spec.DamagePerSecond <= 0f || spec.Duration <= 0f) return;

            if (em.HasComponent<BuildingBurn>(victim))
            {
                var b = em.GetComponentData<BuildingBurn>(victim);
                if (spec.DamagePerSecond > b.DamagePerSecond)
                    b.DamagePerSecond = spec.DamagePerSecond;
                if (spec.Duration > b.Remaining) b.Remaining = spec.Duration;
                b.Source = source;
                em.SetComponentData(victim, b);
            }
            else
            {
                ecb.AddComponent(victim, new BuildingBurn
                {
                    DamagePerSecond = spec.DamagePerSecond,
                    Remaining = spec.Duration,
                    Source = source,
                    Accumulator = 0f,
                });
            }
        }
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct BuildingBurnSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<BuildingBurn>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;
            var expired = new Unity.Collections.NativeList<Entity>(
                Unity.Collections.Allocator.Temp);

            foreach (var (burn, health, entity) in SystemAPI
                .Query<RefRW<BuildingBurn>, RefRW<Health>>()
                .WithNone<BuildingCollapseState>()
                .WithEntityAccess())
            {
                ref var b = ref burn.ValueRW;

                if (health.ValueRO.Value <= 0)
                {
                    expired.Add(entity);
                    continue;
                }

                b.Remaining -= dt;
                b.Accumulator += b.DamagePerSecond * dt;

                int whole = (int)b.Accumulator;
                if (whole > 0)
                {
                    b.Accumulator -= whole;
                    var h = health.ValueRO;
                    h.Value = Unity.Mathematics.math.max(0, h.Value - whole);
                    health.ValueRW = h;

                    if (em.HasComponent<LastDamagedByFaction>(entity))
                        em.SetComponentData(entity, new LastDamagedByFaction { Value = b.Source });
                }

                if (b.Remaining <= 0f) expired.Add(entity);
            }

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            for (int i = 0; i < expired.Length; i++)
                ecb.RemoveComponent<BuildingBurn>(expired[i]);
            expired.Dispose();
        }
    }
}
