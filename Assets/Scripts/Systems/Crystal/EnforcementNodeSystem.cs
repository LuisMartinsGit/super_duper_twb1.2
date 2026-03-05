// File: Assets/Scripts/Systems/Crystal/EnforcementNodeSystem.cs
// Applies CrystalBuff to crystal-allied units within Enforcement aura range.
// Removes the buff when units leave the aura radius.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Ticks every 1 second. For each Enforcement node, queries all
    /// CrystalUnitTag entities and adds/removes CrystalBuff based on distance.
    /// The strongest overlapping buff wins (max of all auras in range).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct EnforcementNodeSystem : ISystem
    {
        private const float TickInterval = 1f;
        private float _timer;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EnforcementAura>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            _timer += dt;
            if (_timer < TickInterval) return;
            _timer = 0f;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var em = state.EntityManager;

            // Snapshot all enforcement aura sources
            var auraQuery = SystemAPI.QueryBuilder()
                .WithAll<EnforcementAura, LocalTransform, CrystalSubNodeTag>()
                .WithNone<UnderConstruction>()
                .Build();

            var auraTransforms = auraQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var auraData = auraQuery.ToComponentDataArray<EnforcementAura>(Allocator.Temp);

            // Process all crystal units -- add or update CrystalBuff
            foreach (var (unitTransform, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<CrystalUnitTag>()
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

                bool hasBuff = em.HasComponent<CrystalBuff>(entity);

                if (inRange)
                {
                    var buff = new CrystalBuff
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
                    ecb.RemoveComponent<CrystalBuff>(entity);
                }
            }

            auraTransforms.Dispose();
            auraData.Dispose();

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
