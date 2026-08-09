// AlanthorCombatPassiveSystem.cs
// Arms the Alanthor tech-tree combat passives. Every one of them is "ready"
// once a condition has held long enough — standing still (Shield Wall, Deploy
// Stakes, Siege Screens) or not having dealt damage recently (Charge). Spending
// them is the damage site's job (CombatDamageHelper), so this system only ever
// counts up and sets the Ready flag.
//
// Stillness is measured from the unit's own last sampled position rather than
// DesiredDestination, because movement consumes that component — a unit can be
// mid-step with no destination left. See the DesiredDestination arbitration
// note in the nav design docs.
//
// Throttled to 0.2 s: these are 1-3 second timers, so per-frame precision buys
// nothing and the query walk is O(units with the components).

using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Systems.Abilities
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct AlanthorCombatPassiveSystem : ISystem
    {
        private const float Interval = 0.2f;
        private float _timer;

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            _timer += SystemAPI.Time.DeltaTime;
            if (_timer < Interval) return;
            float dt = _timer;
            _timer = 0f;

            float eps = AlanthorPassiveTuning.StillEpsilonSq;

            // ---- Charge: rearms once the unit has been out of combat ----
            // AttackCooldown.Timer counts down after a swing, so a unit that just
            // attacked has a live timer. We approximate "dealt damage recently" by
            // the presence of a target: no target means disengaged.
            foreach (var (fs, tgt) in SystemAPI.Query<RefRW<FirstStrike>, RefRO<Target>>())
            {
                var v = fs.ValueRO;
                bool engaged = tgt.ValueRO.Value != Entity.Null;
                if (engaged)
                {
                    v.OutOfCombatTimer = 0f;
                }
                else
                {
                    v.OutOfCombatTimer += dt;
                    if (v.OutOfCombatTimer >= AlanthorPassiveTuning.ChargeRearmSeconds) v.Ready = 1;
                }
                fs.ValueRW = v;
            }

            // ---- Shield Wall / Deploy Stakes / Siege Screens: stationary timers ----
            foreach (var (sw, xf) in SystemAPI.Query<RefRW<ShieldWallState>, RefRO<LocalTransform>>())
            {
                var v = sw.ValueRO;
                Tick(ref v.StillTimer, ref v.Ready, xf.ValueRO.Position, dt, eps,
                    AlanthorPassiveTuning.ShieldWallStillSeconds, ref v.LastX, ref v.LastZ);
                sw.ValueRW = v;
            }

            foreach (var (st, xf) in SystemAPI.Query<RefRW<StakesState>, RefRO<LocalTransform>>())
            {
                var v = st.ValueRO;
                Tick(ref v.StillTimer, ref v.Ready, xf.ValueRO.Position, dt, eps,
                    AlanthorPassiveTuning.StakesStillSeconds, ref v.LastX, ref v.LastZ);
                st.ValueRW = v;
            }

            foreach (var (ss, xf) in SystemAPI.Query<RefRW<SiegeScreens>, RefRO<LocalTransform>>())
            {
                var v = ss.ValueRO;
                Tick(ref v.StillTimer, ref v.Ready, xf.ValueRO.Position, dt, eps,
                    AlanthorPassiveTuning.SiegeScreensStillSeconds, ref v.LastX, ref v.LastZ);
                ss.ValueRW = v;
            }

            // ---- One-shot / timed windows ----
            var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            foreach (var (nb, e) in SystemAPI.Query<RefRW<NextShotBonus>>().WithEntityAccess())
            {
                var v = nb.ValueRO; v.TimeRemaining -= dt; nb.ValueRW = v;
                if (v.TimeRemaining <= 0f) ecb.RemoveComponent<NextShotBonus>(e);
            }
            foreach (var (vb, e) in SystemAPI.Query<RefRW<VolleyBuff>>().WithEntityAccess())
            {
                var v = vb.ValueRO; v.TimeRemaining -= dt; vb.ValueRW = v;
                if (v.TimeRemaining <= 0f) ecb.RemoveComponent<VolleyBuff>(e);
            }
            ecb.Playback(state.EntityManager);
            ecb.Dispose();
        }

        /// <summary>Advance a stationary timer; arm at <paramref name="required"/>,
        /// disarm the moment the owner moves.</summary>
        // (helper below)
        private static void Tick(ref float stillTimer, ref byte ready, float3 pos, float dt,
            float epsSq, float required, ref float lastX, ref float lastZ)
        {
            float dx = pos.x - lastX, dz = pos.z - lastZ;
            lastX = pos.x; lastZ = pos.z;

            if (dx * dx + dz * dz > epsSq)
            {
                stillTimer = 0f;
                ready = 0;
                return;
            }

            stillTimer += dt;
            if (stillTimer >= required) ready = 1;
        }
    }

    /// <summary>
    /// Ticks the per-faction cooldowns for the two building-fired Alanthor actives
    /// (Choreographed Volleys, Ranging Shot). Separate from the passive system
    /// because those clocks are managed statics, which Burst cannot touch.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AlanthorActiveCooldownSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            TheWaningBorder.Abilities.AlanthorActiveHelper.Tick(SystemAPI.Time.DeltaTime);
        }
    }
}
