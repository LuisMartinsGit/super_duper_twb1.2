// SpellBuffSystem.cs
// Ticks SpellBuff, SpellDebuff, and Invulnerable timers, removing expired components

using Unity.Burst;
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
    /// Burst-compiled (perf pass 2026-10-16): the old note said it could not
    /// be because it removed components — it has not done that since the
    /// TransientState pre-add (an expiry is an ECB enable-bit flip), and every
    /// call it makes is Burst-safe. Same loops, same order, same results.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct SpellBuffSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
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
                    TransientState.Clear<SpellBuff>(state.EntityManager, ecb, entity);
                }
            }

            // ── Tick SpellDebuff timers ──
            foreach (var (debuff, entity) in SystemAPI.Query<RefRW<SpellDebuff>>().WithEntityAccess())
            {
                debuff.ValueRW.TimeRemaining -= dt;
                if (debuff.ValueRO.TimeRemaining <= 0f)
                {
                    TransientState.Clear<SpellDebuff>(state.EntityManager, ecb, entity);
                }
            }

            // ── Tick Invulnerable timers ──
            foreach (var (invuln, entity) in SystemAPI.Query<RefRW<Invulnerable>>().WithEntityAccess())
            {
                invuln.ValueRW.TimeRemaining -= dt;
                if (invuln.ValueRO.TimeRemaining <= 0f)
                {
                    TransientState.Clear<Invulnerable>(state.EntityManager, ecb, entity);
                }
            }

            // ECB plays back automatically at EndSimulation.
        }
    }
}
