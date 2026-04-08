// SpellBuffSystem.cs
// Ticks SpellBuff, SpellDebuff, and Invulnerable timers, removing expired components
// Location: Assets/Scripts/Systems/Combat/SpellBuffSystem.cs

using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Ticks down all temporary spell buff/debuff timers each frame.
    /// Removes the component when the timer expires.
    ///
    /// Handles:
    /// - SpellBuff: armor bonus, damage/speed multiplier, damage reflect
    /// - SpellDebuff: speed reduction, supplies drain
    /// - Invulnerable: building invulnerability
    ///
    /// NOTE: Not BurstCompiled because it uses structural changes (remove component).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SpellBuffSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f) return;

            // Fix #225: use the frame-scoped Singleton ECB.
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // ── Tick SpellBuff timers ──
            foreach (var (buff, entity) in SystemAPI.Query<RefRW<SpellBuff>>().WithEntityAccess())
            {
                buff.ValueRW.TimeRemaining -= dt;
                if (buff.ValueRO.TimeRemaining <= 0f)
                {
                    ecb.RemoveComponent<SpellBuff>(entity);
                }
            }

            // ── Tick SpellDebuff timers ──
            foreach (var (debuff, entity) in SystemAPI.Query<RefRW<SpellDebuff>>().WithEntityAccess())
            {
                debuff.ValueRW.TimeRemaining -= dt;
                if (debuff.ValueRO.TimeRemaining <= 0f)
                {
                    ecb.RemoveComponent<SpellDebuff>(entity);
                }
            }

            // ── Tick Invulnerable timers ──
            foreach (var (invuln, entity) in SystemAPI.Query<RefRW<Invulnerable>>().WithEntityAccess())
            {
                invuln.ValueRW.TimeRemaining -= dt;
                if (invuln.ValueRO.TimeRemaining <= 0f)
                {
                    ecb.RemoveComponent<Invulnerable>(entity);
                }
            }

            // ECB plays back automatically at EndSimulation.
        }
    }
}
