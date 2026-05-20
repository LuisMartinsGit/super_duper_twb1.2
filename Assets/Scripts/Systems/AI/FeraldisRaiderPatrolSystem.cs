// File: Assets/Scripts/Systems/AI/FeraldisRaiderPatrolSystem.cs
// Drives uncontrollable Feraldis Raider units toward the nearest enemy
// every RetargetInterval seconds. Auto-spawn from Houses lives in task-066
// Phase 3; this system only handles movement/aggression.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisRaiderPatrolSystem : SystemBase
    {
        private const float RetargetInterval = 1.5f;
        private const float MaxSearchRadiusSq = 200f * 200f;

        private double _lastRetargetTime;

        protected override void OnCreate()
        {
            RequireForUpdate<FeraldisRaiderTag>();
            _lastRetargetTime = 0;
        }

        protected override void OnUpdate()
        {
            double now = SystemAPI.Time.ElapsedTime;
            if (now - _lastRetargetTime < RetargetInterval) return;
            _lastRetargetTime = now;

            var em = EntityManager;

            // Snapshot all faction-tagged entities with health — Raiders consider
            // both units and buildings as valid targets per design §5.3.
            var enemyQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());

            using var enemyEnts = enemyQuery.ToEntityArray(Allocator.Temp);
            using var enemyFactions = enemyQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var enemyTransforms = enemyQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var enemyHealth = enemyQuery.ToComponentDataArray<Health>(Allocator.Temp);

            foreach (var (transform, factionTag, raiderEntity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<FeraldisRaiderTag>()
                .WithEntityAccess())
            {
                Faction self = factionTag.ValueRO.Value;
                float3 myPos = transform.ValueRO.Position;

                Entity bestTarget = Entity.Null;
                float bestDistSq = MaxSearchRadiusSq;
                float3 bestPos = float3.zero;

                for (int i = 0; i < enemyEnts.Length; i++)
                {
                    if (enemyFactions[i].Value == self) continue;
                    if (enemyHealth[i].Value <= 0) continue;

                    float3 d = enemyTransforms[i].Position - myPos;
                    float distSq = math.lengthsq(d);
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestTarget = enemyEnts[i];
                        bestPos = enemyTransforms[i].Position;
                    }
                }

                if (bestTarget == Entity.Null) continue;

                if (em.HasComponent<DesiredDestination>(raiderEntity))
                {
                    em.SetComponentData(raiderEntity, new DesiredDestination { Position = bestPos, Has = 1 });
                }
                else
                {
                    em.AddComponentData(raiderEntity, new DesiredDestination { Position = bestPos, Has = 1 });
                }

                if (em.HasComponent<Target>(raiderEntity))
                {
                    em.SetComponentData(raiderEntity, new Target { Value = bestTarget });
                }
            }

            enemyQuery.Dispose();
        }
    }
}
