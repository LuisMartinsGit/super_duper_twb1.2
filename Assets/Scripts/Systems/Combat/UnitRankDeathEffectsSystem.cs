// UnitRankDeathEffectsSystem.cs
// On-death AOE for Lv 4+ veteran units. Mirrors PillageSystem's death-event
// hook (runs before DeathSystem with WithNone<DeathAnimationState>).
//
// Lv 4: small magical explosion, AOE damage to nearby enemies.
// Lv 5: medium explosion, more damage + push-back (sets DesiredDestination
//       on nearby enemies away from the death position).
//
// Audit fix #1.
//
// Location: Assets/Scripts/Systems/Combat/UnitRankDeathEffectsSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct UnitRankDeathEffectsSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<UnitRank>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Snapshot dead Lv 4+ units before applying AOE so the iteration
            // doesn't see effects we cause.
            var explosions = new NativeList<float3>(Allocator.Temp);
            var explosionFactions = new NativeList<Faction>(Allocator.Temp);
            var explosionRanks = new NativeList<byte>(Allocator.Temp);

            foreach (var (health, transform, faction, rank) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<UnitRank>>()
                .WithAll<UnitTag>()
                .WithNone<DeathAnimationState>())
            {
                if (health.ValueRO.Value > 0) continue;
                if (rank.ValueRO.Value < 4) continue;

                explosions.Add(transform.ValueRO.Position);
                explosionFactions.Add(faction.ValueRO.Value);
                explosionRanks.Add(rank.ValueRO.Value);
            }

            for (int i = 0; i < explosions.Length; i++)
            {
                ApplyExplosion(em, explosions[i], explosionFactions[i], explosionRanks[i]);
            }

            explosions.Dispose();
            explosionFactions.Dispose();
            explosionRanks.Dispose();
        }

        private static void ApplyExplosion(EntityManager em, float3 center, Faction owner, byte rank)
        {
            int dmg    = rank >= 5 ? UnitRankConfig.Lv5DeathAoeDamage : UnitRankConfig.Lv4DeathAoeDamage;
            float r    = rank >= 5 ? UnitRankConfig.Lv5DeathAoeRadius : UnitRankConfig.Lv4DeathAoeRadius;
            bool push  = rank >= 5;
            float r2   = r * r;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadWrite<Health>());
            using var entities = query.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                if (em.GetComponentData<FactionTag>(e).Value == owner) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - center.x, dz = p.z - center.z;
                float distSqr = dx * dx + dz * dz;
                if (distSqr > r2) continue;

                var hp = em.GetComponentData<Health>(e);
                hp.Value = math.max(0, hp.Value - dmg);
                em.SetComponentData(e, hp);

                if (push && distSqr > 0.001f)
                {
                    float dist = math.sqrt(distSqr);
                    float3 awayDir = new float3(dx / dist, 0f, dz / dist);
                    float3 pushTarget = p + awayDir * UnitRankConfig.Lv5PushDistance;
                    if (em.HasComponent<DesiredDestination>(e))
                        em.SetComponentData(e, new DesiredDestination { Position = pushTarget, Has = 1 });
                    // Don't add DesiredDestination to entities that don't have it
                    // (mostly buildings filtered out by UnitTag query already).
                }
            }
        }
    }
}
