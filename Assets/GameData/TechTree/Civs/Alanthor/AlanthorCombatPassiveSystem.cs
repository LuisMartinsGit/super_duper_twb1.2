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
        private SimCadence.Periodic _timer;

        [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
        public void OnUpdate(ref SystemState state)
        {
            float dt = _timer.DueStep(SystemAPI.Time.DeltaTime, Interval);
            if (dt <= 0f) return;

            float eps = AlanthorPassiveTuning.StillEpsilonSq;

            // ---- Charge: arm out of combat, activate on contact, expire ----
            // Three transitions, in the order they can happen in one tick:
            //   armed + engaged        -> activate (speed on, window opens)
            //   charging, window gone  -> expire  (speed off, must rearm)
            //   disengaged             -> count toward arming
            // The blow itself is spent at the damage site (CombatDamageHelper),
            // which zeroes Ready and the window; the speed it left behind is
            // taken off here, so every MoveSpeed write lives in one place.
            foreach (var (fs, tgt, spd) in SystemAPI
                .Query<RefRW<FirstStrike>, RefRO<Target>, RefRW<MoveSpeed>>())
            {
                var v = fs.ValueRO;
                bool engaged = tgt.ValueRO.Value != Entity.Null;

                if (v.WindowRemaining > 0f)
                {
                    v.WindowRemaining -= dt;
                    if (v.WindowRemaining <= 0f)
                    {
                        // Closed on nothing: drop the burst and start over.
                        v.WindowRemaining = 0f;
                        v.Ready = 0;
                        v.OutOfCombatTimer = 0f;
                    }
                }
                else if (v.Ready != 0 && engaged && v.SpeedBonus == 0f)
                {
                    // Contact: the charge goes in.
                    v.WindowRemaining = AlanthorPassiveTuning.ChargeWindowSeconds;
                    v.SpeedBonus = spd.ValueRO.Value
                                 * (AlanthorPassiveTuning.ChargeSpeedPct / 100f);
                    spd.ValueRW = new MoveSpeed { Value = spd.ValueRO.Value + v.SpeedBonus };
                }
                else if (!engaged)
                {
                    v.OutOfCombatTimer += dt;
                    if (v.OutOfCombatTimer >= AlanthorPassiveTuning.ChargeRearmSeconds) v.Ready = 1;
                }

                // The burst is over the moment the window is — whether it was
                // spent on a hit or simply ran out. Subtracting the exact
                // amount added keeps this idempotent against every other
                // speed layer (rank, tech, Full Gallop).
                if (v.WindowRemaining <= 0f && v.SpeedBonus != 0f)
                {
                    spd.ValueRW = new MoveSpeed { Value = spd.ValueRO.Value - v.SpeedBonus };
                    v.SpeedBonus = 0f;
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

            // Deploy Stakes: a plain refresh timer. Spending the stakes at the
            // damage site sets CooldownRemaining; they re-plant when it runs out.
            foreach (var st in SystemAPI.Query<RefRW<StakesState>>())
            {
                var v = st.ValueRO;
                if (v.Ready == 0)
                {
                    v.CooldownRemaining -= dt;
                    if (v.CooldownRemaining <= 0f)
                    {
                        v.CooldownRemaining = 0f;
                        v.Ready = 1;
                    }
                    st.ValueRW = v;
                }
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
