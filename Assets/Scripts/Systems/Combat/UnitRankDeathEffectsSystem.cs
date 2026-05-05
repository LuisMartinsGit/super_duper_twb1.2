// UnitRankDeathEffectsSystem.cs
// On-death AOE for Lv 4+ veteran units AND drop-pile spawn for Lv 2+
// veterans. Mirrors PillageSystem's death-event hook.
//
// Lv 4: small magical explosion, AOE damage to nearby enemies.
// Lv 5: medium explosion, more damage + push-back.
// Lv 2+: drops cumulative consumed resources at 50%, as a pickup any
//        faction can collect (UpgradePile entity, audit follow-up).
//
// Audit fix #1 + follow-up.
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

            // Snapshot dead veterans before applying effects so iteration
            // doesn't see entities we spawn.
            var deadPositions = new NativeList<float3>(Allocator.Temp);
            var deadFactions  = new NativeList<Faction>(Allocator.Temp);
            var deadRanks     = new NativeList<byte>(Allocator.Temp);

            foreach (var (health, transform, faction, rank) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<UnitRank>>()
                .WithAll<UnitTag>()
                .WithNone<DeathAnimationState>())
            {
                if (health.ValueRO.Value > 0) continue;
                if (rank.ValueRO.Value < 2) continue;

                deadPositions.Add(transform.ValueRO.Position);
                deadFactions.Add(faction.ValueRO.Value);
                deadRanks.Add(rank.ValueRO.Value);
            }

            for (int i = 0; i < deadPositions.Length; i++)
            {
                if (deadRanks[i] >= 4)
                    ApplyExplosion(em, deadPositions[i], deadFactions[i], deadRanks[i]);
                SpawnUpgradePile(em, deadPositions[i], deadRanks[i]);
            }

            deadPositions.Dispose();
            deadFactions.Dispose();
            deadRanks.Dispose();
        }

        /// <summary>
        /// Spawns an UpgradePile entity at the death position carrying 50% of
        /// the cumulative resources the unit paid for its rank-ups. Lv 2 drops
        /// half the Lv-2 cost, Lv 3 drops half of (Lv2 + Lv3), and so on.
        /// (Audit follow-up — drop-pile-on-death.)
        /// </summary>
        private static void SpawnUpgradePile(EntityManager em, float3 pos, byte rank)
        {
            // Sum costs for ranks 2..rank.
            var drop = new TheWaningBorder.Core.Cost();
            for (byte r = 2; r <= rank; r++)
            {
                var c = UnitRankConfig.CostFor(r);
                drop.Supplies  += c.Supplies;
                drop.Iron      += c.Iron;
                drop.Crystal   += c.Crystal;
                drop.Veilsteel += c.Veilsteel;
                drop.Glow      += c.Glow;
            }
            // 50% recovery rate.
            drop.Supplies  /= 2;
            drop.Iron      /= 2;
            drop.Crystal   /= 2;
            drop.Veilsteel /= 2;
            drop.Glow      /= 2;

            // Skip if nothing to drop (shouldn't happen at Lv 2+ but guard anyway).
            if (drop.Supplies + drop.Iron + drop.Crystal + drop.Veilsteel + drop.Glow == 0) return;

            var pile = em.CreateEntity(typeof(UpgradePile), typeof(LocalTransform));
            em.SetComponentData(pile, new UpgradePile
            {
                Drop = drop,
                Lifetime = 60f,       // 1 minute to collect before despawn
                PickupRadius = 1.5f,
            });
            em.SetComponentData(pile, LocalTransform.FromPositionRotationScale(
                pos, quaternion.identity, 1f));
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
