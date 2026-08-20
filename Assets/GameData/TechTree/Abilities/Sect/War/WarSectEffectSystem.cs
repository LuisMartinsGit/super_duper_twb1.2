// File: Assets/GameData/TechTree/Abilities/Sect/War/WarSectEffectSystem.cs
// Ticks the two timed Sect of War effects and cleans them up on expiry.
//
// One system for both, for the same reason AlanthorSectEffectSystem is one
// system for ten: they share the whole of their behaviour - count down, then
// remove yourself - and the per-effect logic that is NOT shared lives at the
// consuming site (TrainingSystem reads the boon, the two cast gates read the
// silence), gated on the component's presence.
//
// ISystem, not SystemBase: neither effect touches managed state.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WarSectEffectSystem : ISystem
    {
        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f) return;

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Blood Rain's map-wide lockout. The entity is destroyed rather
            // than left at zero so IsGloballySilenced stays a presence test for
            // anything that wants the cheap version.
            foreach (var (silence, e) in SystemAPI
                .Query<RefRW<SectGlobalSilence>>().WithEntityAccess())
            {
                silence.ValueRW.TimeRemaining -= dt;
                if (silence.ValueRO.TimeRemaining <= 0f) ecb.DestroyEntity(e);
            }

            // Call to Arms. Removing the component restores full price and
            // normal training speed - nothing has to be handed back, because
            // the boon never altered the building's stored stats.
            foreach (var (boon, e) in SystemAPI
                .Query<RefRW<SectTrainingBoon>>().WithEntityAccess())
            {
                boon.ValueRW.TimeRemaining -= dt;
                if (boon.ValueRO.TimeRemaining <= 0f) ecb.RemoveComponent<SectTrainingBoon>(e);
            }

            // Blood Rain's haste. Its own clock, not SpellBuff's - see the
            // SectHaste doc comment for why sharing that timer was wrong.
            foreach (var (haste, e) in SystemAPI
                .Query<RefRW<SectHaste>>().WithEntityAccess())
            {
                haste.ValueRW.TimeRemaining -= dt;
                if (haste.ValueRO.TimeRemaining <= 0f) ecb.RemoveComponent<SectHaste>(e);
            }

            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }
    }
}
