// File: Assets/GameData/TechTree/Units/Feraldis/Berserker/BerserkerDeathFrenzySystem.cs
// Ticks the Berserker's last stand. Canon: docs/Design/Age_1_Feraldis.md.
//
// ARMING and the HP clamp are NOT here — they live in
// FeraldisDeathInterceptor, called from DeathSystem's pre-death pass, which
// is the only point guaranteed to run after every damage source in the
// frame. See that file for the full reasoning. This system owns only the
// countdown and the force-kill at the end of it.
//
// When the window closes: Health -> 0 and DeathSystem takes it on its very
// next pass (DeathFrenzySpent is already latched, so the interceptor lets it
// through). The corpse splats blood like any other, feeding the ground its
// allies fight on.

using Unity.Collections;
using Unity.Entities;
using static TheWaningBorder.Core.Config.FeraldisConstants;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial struct BerserkerDeathFrenzySystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<DeathFrenzyState>();
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            var expiring = new NativeList<Entity>(Allocator.Temp);

            // NOT filtered on BerserkerTag: the Plunderer's 2 s berserk uses
            // the same DeathFrenzyState. Filtering here would have left every
            // Plunderer clamped at 1 HP with a timer nothing ticked —
            // permanently unkillable, which is the exact opposite of the
            // intended nerf.
            foreach (var (frenzy, health, entity) in SystemAPI
                .Query<RefRW<DeathFrenzyState>, RefRW<Health>>()
                .WithNone<DeathAnimationState>()
                .WithEntityAccess())
            {
                frenzy.ValueRW.Remaining -= dt;
                if (frenzy.ValueRO.Remaining > 0f) continue;

                // Window closed: it dies now.
                var dead = health.ValueRO;
                dead.Value = 0;
                health.ValueRW = dead;
                expiring.Add(entity);
            }

            for (int i = 0; i < expiring.Length; i++)
                state.EntityManager.RemoveComponent<DeathFrenzyState>(expiring[i]);
            expiring.Dispose();
        }
    }
}
