// File: Assets/Scripts/Systems/Crystal/SuppressionNodeSystem.cs
// Applies CrystalDebuff to enemy (non-White) units within Suppression aura range.
// Removes the debuff when units leave the aura radius.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Ticks every 1 second. For each Suppression node, queries all
    /// non-White UnitTag entities and adds/removes CrystalDebuff based on distance.
    /// The strongest overlapping debuff wins (max of all auras in range).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SuppressionNodeSystem : ISystem
    {
        private const float TickInterval = 1f;
        private float _timer;

        // Cached query — created once in OnCreate, reused every frame
        private EntityQuery _auraQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<SuppressionAura>();

            _auraQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<SuppressionAura>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<CrystalSubNodeTag>(),
                ComponentType.Exclude<UnderConstruction>()
            );
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            _timer += dt;
            if (_timer < TickInterval) return;
            _timer = 0f;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var em = state.EntityManager;

            var auraTransforms = _auraQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var auraData = _auraQuery.ToComponentDataArray<SuppressionAura>(Allocator.Temp);

            // Process all non-crystal units (enemy units with UnitTag)
            foreach (var (unitTransform, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<UnitTag>()
                .WithEntityAccess())
            {
                // Only debuff non-Curse (player) faction units
                if (faction.ValueRO.Value == Faction.Curse) continue;

                float3 unitPos = unitTransform.ValueRO.Position;

                // Find strongest debuff from all suppression auras in range
                float bestDef = 0f;
                float bestAtt = 0f;
                float bestSpd = 0f;
                bool inRange = false;

                for (int i = 0; i < auraTransforms.Length; i++)
                {
                    float dist = math.distance(unitPos, auraTransforms[i].Position);
                    if (dist <= auraData[i].Radius)
                    {
                        inRange = true;
                        bestDef = math.max(bestDef, auraData[i].DefPenalty);
                        bestAtt = math.max(bestAtt, auraData[i].AttPenalty);
                        bestSpd = math.max(bestSpd, auraData[i].SpeedPenalty);
                    }
                }

                bool hasDebuff = em.HasComponent<CrystalDebuff>(entity);

                if (inRange)
                {
                    var debuff = new CrystalDebuff
                    {
                        DefPenalty = bestDef,
                        AttPenalty = bestAtt,
                        SpeedPenalty = bestSpd
                    };

                    if (hasDebuff)
                        ecb.SetComponent(entity, debuff);
                        else
                            ecb.AddComponent(entity, debuff);
                }
                else if (hasDebuff)
                {
                    ecb.RemoveComponent<CrystalDebuff>(entity);
                }
            }

            auraTransforms.Dispose();
            auraData.Dispose();

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
