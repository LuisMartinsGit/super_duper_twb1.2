// SectWitnessSpySystem.cs
// Runs the Sect of Witness's Spy Network once it has been planted
// (docs/Design/Sects.md §"Sect of Witness"). Four jobs, in this order:
//
//   1. MOVE each eye onto its host, so the owner keeps seeing what the spy
//      sees as the spy walks. The eye is a plain FactionTag + LocalTransform +
//      LineOfSight entity, which is exactly what FogOfWarSystem stamps vision
//      from — the fog needs no idea that spies exist.
//   2. EXPIRE spies whose timer has run out, and spies whose host has died.
//      Lv III spies carry SectEffectDuration.Permanent and only ever leave by
//      dying.
//   3. CASCADE: an enemy that spends the threshold near a spy becomes one too.
//      Proximity must be CONTINUOUS — SpyExposure.LastTouch is what enforces
//      that, so a unit cannot accumulate three seconds over a whole match by
//      walking past repeatedly.
//   4. UNBLIND units whose Blinding Glare has run out (the other Witness
//      timer, folded in here rather than paying for a second system over a
//      handful of entities — the same reasoning AlanthorSectEffectSystem
//      gives for ticking ten effects in one place).
//
// Structural changes are collected and applied AFTER every query iteration:
// AddComponentData inside a SystemAPI.Query foreach throws.
//
// SystemBase rather than ISystem because it creates and destroys entities
// through the managed EntityManager path.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Sect
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SectWitnessSpySystem : SystemBase
    {
        /// <summary>
        /// A spy that is not itself the seed still spreads. Without this an
        /// eye planted in a marching army would infect only the handful of
        /// units that happened to be beside it at the moment of the cast, and
        /// "the network cascades" would be a claim the code never made good on.
        /// </summary>
        private struct PendingSpy
        {
            public Entity Unit;
            public Faction Owner;
            public float Life;
            public byte Level;
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f) return;

            var em = EntityManager;
            float now = (float)SystemAPI.Time.ElapsedTime;

            var ecb = new EntityCommandBuffer(Allocator.Temp);
            var recruits = new NativeList<PendingSpy>(Allocator.Temp);

            MoveEyes(em, ecb);
            TickSpies(em, ecb, dt);
            Cascade(em, ecb, recruits, now, dt);
            TickBlind(em, ecb, dt);

            ecb.Playback(em);
            ecb.Dispose();

            // Structural work last, once every query above is finished with.
            for (int i = 0; i < recruits.Length; i++) Recruit(em, recruits[i]);
            recruits.Dispose();
        }

        // ── 1. the eyes follow their hosts ──────────────────────────────

        private void MoveEyes(EntityManager em, EntityCommandBuffer ecb)
        {
            foreach (var (eye, xf, los, e) in SystemAPI
                .Query<RefRO<WitnessEye>, RefRW<LocalTransform>, RefRW<LineOfSight>>()
                .WithEntityAccess())
            {
                Entity host = eye.ValueRO.Host;
                if (host == Entity.Null || !em.Exists(host)
                    || !em.HasComponent<LocalTransform>(host))
                {
                    ecb.DestroyEntity(e);
                    continue;
                }

                xf.ValueRW.Position = em.GetComponentData<LocalTransform>(host).Position;

                // You see what IT sees, so the eye tracks the host's own sight
                // radius rather than a number of its own — including whatever
                // the host's faction has researched into it, and including a
                // host the Witness player has separately blinded.
                if (em.HasComponent<LineOfSight>(host))
                    los.ValueRW.Radius = em.GetComponentData<LineOfSight>(host).Radius;
            }
        }

        // ── 2. spies expire, or die with their host ─────────────────────

        private void TickSpies(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (spy, e) in SystemAPI.Query<RefRW<WitnessSpy>>().WithEntityAccess())
            {
                bool dead = em.HasComponent<Health>(e)
                            && em.GetComponentData<Health>(e).Value <= 0;

                if (!dead && SectEffectDuration.IsPermanent(spy.ValueRO.TimeRemaining))
                    continue;

                if (!dead)
                {
                    spy.ValueRW.TimeRemaining -= dt;
                    if (spy.ValueRO.TimeRemaining > 0f) continue;
                }

                // The eye goes with it. MoveEyes would also collect an eye
                // whose host is gone, but a spy that merely timed out is still
                // alive — nothing else would tell its eye to stop watching.
                if (spy.ValueRO.Eye != Entity.Null && em.Exists(spy.ValueRO.Eye))
                    ecb.DestroyEntity(spy.ValueRO.Eye);
                ecb.RemoveComponent<WitnessSpy>(e);
            }
        }

        // ── 3. the network spreads ──────────────────────────────────────

        private void Cascade(EntityManager em, EntityCommandBuffer ecb,
            NativeList<PendingSpy> recruits, float now, float dt)
        {
            var spies = new NativeList<Entity>(Allocator.Temp);
            foreach (var (_, e) in SystemAPI.Query<RefRO<WitnessSpy>>().WithEntityAccess())
                spies.Add(e);

            if (spies.Length == 0) { spies.Dispose(); return; }

            foreach (var (xf, faction, e) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRO<FactionTag>>()
                .WithAll<UnitTag>()
                .WithNone<WitnessSpy>()
                .WithEntityAccess())
            {
                float3 p = xf.ValueRO.Position;

                // Nearest spy of the same faction as this unit. A spy only
                // spreads inside its OWN army: the Witness player's units
                // standing beside a spy are not "exposed" to anything.
                Faction owner = default;
                byte level = 0;
                bool touched = false;

                for (int i = 0; i < spies.Length; i++)
                {
                    var s = spies[i];
                    if (!em.HasComponent<FactionTag>(s)) continue;
                    if (em.GetComponentData<FactionTag>(s).Value != faction.ValueRO.Value) continue;

                    var spy = em.GetComponentData<WitnessSpy>(s);
                    SectLeverEffects.WitnessCascade(spy.Level, out _, out float radius);

                    float3 sp = em.GetComponentData<LocalTransform>(s).Position;
                    float dx = sp.x - p.x, dz = sp.z - p.z;
                    if (dx * dx + dz * dz > radius * radius) continue;

                    owner = spy.Owner;
                    level = spy.Level;
                    touched = true;
                    break;
                }

                if (!touched)
                {
                    if (em.HasComponent<SpyExposure>(e)) ecb.RemoveComponent<SpyExposure>(e);
                    continue;
                }

                var exposure = em.HasComponent<SpyExposure>(e)
                    ? em.GetComponentData<SpyExposure>(e)
                    : new SpyExposure { Owner = owner, LastTouch = now };

                // A gap in contact resets the count — proximity has to be
                // continuous, or a unit that walks past a spy every ten
                // seconds would eventually be recruited by accident.
                if (now - exposure.LastTouch > dt * 2f + 0.05f) exposure.Seconds = 0f;
                exposure.Owner = owner;
                exposure.LastTouch = now;
                exposure.Seconds += dt;

                SectLeverEffects.WitnessCascade(level, out float needed, out _);
                if (exposure.Seconds < needed)
                {
                    if (em.HasComponent<SpyExposure>(e)) ecb.SetComponent(e, exposure);
                    else ecb.AddComponent(e, exposure);
                    continue;
                }

                ecb.RemoveComponent<SpyExposure>(e);

                // A recruit inherits the seed's level and lifetime, so a Lv III
                // network never unwinds while its members stay alive.
                var seedSpec = SectLeverEffects.ActiveOf(SectConfig.Witness, 1, level);
                recruits.Add(new PendingSpy
                {
                    Unit  = e,
                    Owner = owner,
                    Life  = seedSpec.Duration > 0f ? seedSpec.Duration : SectEffectDuration.Permanent,
                    Level = level,
                });
            }

            spies.Dispose();
        }

        private static void Recruit(EntityManager em, in PendingSpy p)
        {
            if (!em.Exists(p.Unit) || em.HasComponent<WitnessSpy>(p.Unit)) return;
            SectActivePowerHelper.PlantSpy(em, p.Owner, p.Unit, p.Life, p.Level);
        }

        // ── 4. Blinding Glare hands sight back ──────────────────────────

        private void TickBlind(EntityManager em, EntityCommandBuffer ecb, float dt)
        {
            foreach (var (blind, los, e) in SystemAPI
                .Query<RefRW<SectBlinded>, RefRW<LineOfSight>>().WithEntityAccess())
            {
                blind.ValueRW.TimeRemaining -= dt;
                if (blind.ValueRO.TimeRemaining > 0f) continue;

                los.ValueRW.Radius = blind.ValueRO.OriginalRadius;
                ecb.RemoveComponent<SectBlinded>(e);
            }
        }
    }
}
