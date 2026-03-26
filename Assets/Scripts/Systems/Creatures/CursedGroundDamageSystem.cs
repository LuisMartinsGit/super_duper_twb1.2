// File: Assets/Scripts/Systems/Creatures/CursedGroundDamageSystem.cs
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Creatures
{
    /// <summary>
    /// Applies damage-over-time to non-crystal units standing on cursed ground.
    /// Runs on a 1-second tick to reduce per-frame cost. Crystal-tagged entities
    /// (creatures and crystal structures) are immune to cursed ground damage.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CursedGroundDamageSystem : ISystem
    {
        /// <summary>Interval between damage ticks in seconds.</summary>
        private const float DamageTickInterval = 1f;

        private float _tickTimer;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CursedGroundTag>();
            _tickTimer = 0f;
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            _tickTimer += dt;
            if (_tickTimer < DamageTickInterval) return;
            _tickTimer -= DamageTickInterval;

            // Collect all cursed ground positions and their effect data
            var groundPositions = new NativeList<float3>(Allocator.Temp);
            var groundDPS = new NativeList<CursedGroundDPS>(Allocator.Temp);

            foreach (var (dps, groundTransform) in SystemAPI
                .Query<RefRO<CursedGroundDPS>, RefRO<LocalTransform>>()
                .WithAll<CursedGroundTag>())
            {
                groundPositions.Add(groundTransform.ValueRO.Position);
                groundDPS.Add(dps.ValueRO);
            }

            if (groundPositions.Length == 0)
            {
                groundPositions.Dispose();
                groundDPS.Dispose();
                return;
            }

            // Apply damage to non-crystal units standing on cursed ground
            foreach (var (health, unitTransform, entity) in SystemAPI
                .Query<RefRW<Health>, RefRO<LocalTransform>>()
                .WithAll<UnitTag>()
                .WithNone<CrystalTag>()
                .WithEntityAccess())
            {
                float3 unitPos = unitTransform.ValueRO.Position;

                // Check if unit is on any cursed ground tile
                for (int i = 0; i < groundPositions.Length; i++)
                {
                    float dist = math.distance(
                        new float2(unitPos.x, unitPos.z),
                        new float2(groundPositions[i].x, groundPositions[i].z));

                    if (dist <= groundDPS[i].EffectRadius)
                    {
                        // Apply one tick of damage (DPS * tick interval)
                        int damage = math.max(1, (int)(groundDPS[i].DamagePerSecond * DamageTickInterval));
                        ref var hp = ref health.ValueRW;
                        hp.Value = math.max(0, hp.Value - damage);
                        break; // Only take damage from one tile per tick
                    }
                }
            }

            groundPositions.Dispose();
            groundDPS.Dispose();
        }
    }
}
