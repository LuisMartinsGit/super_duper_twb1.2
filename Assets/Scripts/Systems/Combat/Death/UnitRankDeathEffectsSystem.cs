// UnitRankDeathEffectsSystem.cs
// On-death AOE for Lv 4+ veteran units AND drop-pile spawn for Lv 2+
// veterans. Mirrors PillageSystem's death-event hook.
//
// Lv 4: small magical explosion, AOE damage to nearby enemies.
// Lv 5: medium explosion, more damage + push-back.
//
// Audit fix #1 + follow-up.

using Unity.Collections;
using TheWaningBorder.Core;
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
        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and these were never disposed, so this hot path leaked one
        // per invocation. A bloated registry slows every later query AND
        // every structural change. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_UnitTagLocalTransformFactionTagHealth =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_UnitTagLocalTransformFactionTagHealth;

        #endregion

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
                // TemporarySummon: a pledged soldier arrives at rank 1-4
                // (Heroes.md §3), so without this a level-10 Honour thy Pledge
                // ended in three rank-4 explosions the moment its timer ran
                // out. Expiry is meant to be quiet; being CUT DOWN is not, but
                // that path sets Health to 0 with the unit already dying and is
                // unaffected here.
                .WithNone<DeathAnimationState, TemporarySummon>())
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
            }

            deadPositions.Dispose();
            deadFactions.Dispose();
            deadRanks.Dispose();
        }


        private static void ApplyExplosion(EntityManager em, float3 center, Faction owner, byte rank)
        {
            int dmg    = rank >= 5 ? UnitRankConfig.Lv5DeathAoeDamage : UnitRankConfig.Lv4DeathAoeDamage;
            float r    = rank >= 5 ? UnitRankConfig.Lv5DeathAoeRadius : UnitRankConfig.Lv4DeathAoeRadius;
            bool push  = rank >= 5;
            float r2   = r * r;

            var query = QC_UnitTagLocalTransformFactionTagHealth.Get(em, QT_UnitTagLocalTransformFactionTagHealth);
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
