// File: Assets/Scripts/Systems/Border/EnforcementNodeSystem.cs
// Applies BorderBuff to veilstone-allied units within Enforcement aura range.
// Removes the buff when units leave the aura radius.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Ticks every 1 second. For each Enforcement node, queries all
    /// BorderUnitTag entities and adds/removes BorderBuff based on distance.
    /// The strongest overlapping buff wins (max of all auras in range).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct EnforcementNodeSystem : ISystem
    {
        private const float TickInterval = 1f;
        private float _timer;

        // Cached query — created once in OnCreate, reused every frame
        private EntityQuery _auraQuery;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnforcementAura>();

            _auraQuery = state.GetEntityQuery(
                ComponentType.ReadOnly<EnforcementAura>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<BorderSubNodeTag>(),
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
            var auraData = _auraQuery.ToComponentDataArray<EnforcementAura>(Allocator.Temp);

            // Process all veilstone units -- add or update BorderBuff
            foreach (var (unitTransform, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<BorderUnitTag>()
                .WithEntityAccess())
            {
                float3 unitPos = unitTransform.ValueRO.Position;

                // Find strongest buff from all enforcement auras in range
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
                        bestDef = math.max(bestDef, auraData[i].DefBonus);
                        bestAtt = math.max(bestAtt, auraData[i].AttBonus);
                        bestSpd = math.max(bestSpd, auraData[i].SpeedBonus);
                    }
                }

                bool hasBuff = em.HasComponent<BorderBuff>(entity);

                if (inRange)
                {
                    var buff = new BorderBuff
                    {
                        DefBonus = bestDef,
                        AttBonus = bestAtt,
                        SpeedBonus = bestSpd
                    };

                    if (hasBuff)
                        ecb.SetComponent(entity, buff);
                        else
                            ecb.AddComponent(entity, buff);
                }
                else if (hasBuff)
                {
                    ecb.RemoveComponent<BorderBuff>(entity);
                }
            }

            auraTransforms.Dispose();
            auraData.Dispose();

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
