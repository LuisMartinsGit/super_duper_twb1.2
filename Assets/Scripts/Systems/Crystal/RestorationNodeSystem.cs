// File: Assets/Scripts/Systems/Crystal/RestorationNodeSystem.cs
// Heals nearby CrystalTag entities (buildings and units) within Restoration aura range.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Ticks every 1 second. For each Restoration node, queries all
    /// CrystalTag entities with Health and heals damaged ones within range.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct RestorationNodeSystem : ISystem
    {
        private const float TickInterval = 1f;
        private float _timer;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<RestorationAura>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            _timer += dt;
            if (_timer < TickInterval) return;
            _timer = 0f;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var em = state.EntityManager;

            // Snapshot all restoration aura sources
            var auraQuery = SystemAPI.QueryBuilder()
                .WithAll<RestorationAura, LocalTransform, CrystalSubNodeTag>()
                .WithNone<UnderConstruction>()
                .Build();

            var auraTransforms = auraQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var auraData = auraQuery.ToComponentDataArray<RestorationAura>(Allocator.Temp);

            // Process all crystal entities with Health
            foreach (var (health, transform, entity) in SystemAPI
                .Query<RefRO<Health>, RefRO<LocalTransform>>()
                .WithAll<CrystalTag>()
                .WithEntityAccess())
            {
                int currentHP = health.ValueRO.Value;
                int maxHP = health.ValueRO.Max;

                // Skip entities already at full health or dead
                if (currentHP >= maxHP || currentHP <= 0) continue;

                float3 entityPos = transform.ValueRO.Position;

                // Sum heal from all restoration auras in range (they stack)
                float totalHeal = 0f;

                for (int i = 0; i < auraTransforms.Length; i++)
                {
                    float dist = math.distance(entityPos, auraTransforms[i].Position);
                    if (dist <= auraData[i].Radius)
                    {
                        totalHeal += auraData[i].HealPerSecond;
                    }
                }

                if (totalHeal > 0f)
                {
                    int newHP = math.min(currentHP + (int)totalHeal, maxHP);
                    ecb.SetComponent(entity, new Health
                    {
                        Value = newHP,
                        Max = maxHP
                    });
                }
            }

            auraTransforms.Dispose();
            auraData.Dispose();

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
