// AlanthorGallopSystem.cs
// Galloping (docs/Design/Age_1_Alanthor.md § Galloping), per tick:
//   1. The clock. Moving at GallopMinSpeedPct of its own speed for
//      GallopAfterSeconds starts the gallop; slower than that for
//      GallopGraceSeconds ends it and the clock starts over. Measured from the
//      rider's own sampled position, never DesiredDestination (movement
//      consumes it — see AlanthorCombatPassiveSystem).
//   2. Trample. Every hostile unit within the rider's radius + TrampleReach
//      that this gallop has not hit yet takes TrampleDamagePct of the rider's
//      Damage (galloping bonus included), once.
//   3. Strike in passing. A galloping rider with no target of its own swings
//      at the nearest hostile unit in melee reach whenever its attack is ready,
//      through MeleeCombatSystem.Swing (the whole weapon pipeline), without
//      stopping, turning or taking that unit as its target. A rider WITH a
//      target is MeleeCombatSystem's.
// Damage taken and dealt are read at the damage sites (AlanthorGallop); the
// ride-through is SteeringSystem's, the no-auto-acquire is TargetingSystem's.
//
// Deterministic: fixed sim step, query order, ties broken by entity index.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Abilities;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(MeleeCombatSystem))]
    public partial struct AlanthorGallopSystem : ISystem
    {
        private CombatLookups _lk;
        private EntityQuery _riders;
        private EntityQuery _victims;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            _lk = CombatLookups.Create(ref state);
            _riders = new EntityQueryBuilder(Allocator.Temp)
                .WithAllRW<GallopState>()
                .WithAll<LocalTransform, FactionTag>()
                .Build(ref state);
            _victims = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<UnitTag, LocalTransform, FactionTag, Health>()
                .WithNone<DeathAnimationState>()
                .Build(ref state);
            state.RequireForUpdate(_riders);
        }

        public void OnUpdate(ref SystemState state)
        {
            float dt = SystemAPI.Time.DeltaTime;
            if (dt <= 0f) return;
            var em = state.EntityManager;

            using var riders = _riders.ToEntityArray(Allocator.Temp);
            int galloping = 0;

            // ── 1. The clock ─────────────────────────────────────────────
            for (int i = 0; i < riders.Length; i++)
            {
                var r = riders[i];
                var s = em.GetComponentData<GallopState>(r);
                bool alive = !em.HasComponent<Health>(r) || em.GetComponentData<Health>(r).Value > 0;
                var p = em.GetComponentData<LocalTransform>(r).Position;
                float speed = em.HasComponent<MoveSpeed>(r) ? em.GetComponentData<MoveSpeed>(r).Value : 0f;

                bool moving = false;
                if (alive && s.HasLast != 0 && speed > 0f)
                {
                    float dx = p.x - s.LastX, dz = p.z - s.LastZ;
                    float need = speed * (s.MinSpeedPct / 100f) * dt;
                    moving = dx * dx + dz * dz >= need * need;
                }
                s.LastX = p.x;
                s.LastZ = p.z;
                s.HasLast = 1;

                if (moving)
                {
                    s.MovingSeconds += dt;
                    s.StillSeconds = 0f;
                }
                else
                {
                    s.StillSeconds += dt;
                    if (!alive || s.StillSeconds >= s.GraceSeconds) s.MovingSeconds = 0f;
                }

                byte was = s.Active;
                s.Active = (byte)(alive && s.AfterSeconds > 0f && s.MovingSeconds >= s.AfterSeconds ? 1 : 0);
                em.SetComponentData(r, s);

                if (was != 0 && s.Active == 0 && em.HasBuffer<GallopTrampled>(r))
                    em.GetBuffer<GallopTrampled>(r).Clear();
                if (s.Active != 0) galloping++;
            }
            if (galloping == 0) return;

            // ── 2 + 3. Trample and strike in passing ─────────────────────
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>()
                .CreateCommandBuffer(state.WorldUnmanaged);
            double elapsed = SimCadence.MatchTimeOr(SystemAPI.Time.ElapsedTime);

            var meleeCfg = MeleeCombatSystemConfig.I;
            float flankMult = meleeCfg != null ? meleeCfg.flankDamageMultiplier : 1f;
            float frontArcCos = meleeCfg != null
                ? math.cos(math.radians(meleeCfg.frontArcHalfAngleDegrees))
                : -2f;

            using var vEnts = _victims.ToEntityArray(Allocator.Temp);
            using var vFacs = _victims.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var hits = new NativeList<Entity>(Allocator.Temp);

            for (int i = 0; i < riders.Length; i++)
            {
                var r = riders[i];
                if (!em.Exists(r) || !em.HasComponent<GallopState>(r)) continue;
                var s = em.GetComponentData<GallopState>(r);
                if (s.Active == 0) continue;
                if (em.HasComponent<Health>(r) && em.GetComponentData<Health>(r).Value <= 0) continue;

                // Damage may have changed the world since the last rider.
                _lk.Update(ref state);

                var fac = em.GetComponentData<FactionTag>(r).Value;
                var pos = em.GetComponentData<LocalTransform>(r).Position;
                float myRadius = _lk.Radius.TryGetComponent(r, out var rr) ? rr.Value : 0f;
                float trampleReach = myRadius + s.TrampleReach;
                int damage = em.HasComponent<Damage>(r) ? em.GetComponentData<Damage>(r).Value : 0;

                bool canStrike = damage > 0
                    && em.HasComponent<AttackCooldown>(r)
                    && em.GetComponentData<AttackCooldown>(r).Timer <= 0f
                    && (!em.HasComponent<Target>(r) || em.GetComponentData<Target>(r).Value == Entity.Null)
                    && !CombatLookups.Active(in _lk.TempDisarm, r);

                var done = em.GetBuffer<GallopTrampled>(r);
                hits.Clear();
                Entity strike = Entity.Null;
                float best = float.MaxValue;

                for (int v = 0; v < vEnts.Length; v++)
                {
                    var e = vEnts[v];
                    if (e == r) continue;
                    if (!Alliances.AreHostile(fac, vFacs[v].Value)) continue;
                    if (_lk.Flying.HasComponent(e)) continue;
                    if (!_lk.Health.TryGetComponent(e, out var vh) || vh.Value <= 0) continue;

                    float d = _lk.Extent(e).SurfaceDistXZ(pos);
                    if (d <= trampleReach && !Contains(done, e)) hits.Add(e);

                    if (canStrike && d <= MeleeCombatSystem.MeleeRange
                        && !CombatLookups.Active(in _lk.Invulnerable, e)
                        && (d < best || (d == best && e.Index < strike.Index)))
                    {
                        best = d;
                        strike = e;
                    }
                }

                // Trample: a share of the rider's attack, with its galloping bonus.
                if (hits.Length > 0 && damage > 0)
                {
                    int trample = (int)math.round(damage * (s.TramplePct / 100f) * (1f + s.DealtPct / 100f));
                    for (int h = 0; h < hits.Length; h++)
                    {
                        if (!em.Exists(hits[h])) continue;
                        if (SpellDamage.Apply(em, hits[h], trample, DamageType.Melee, fac) > 0)
                            CombatDamageHelper.TrackLastDamager(em, ecb, r, hits[h], elapsed);
                        em.GetBuffer<GallopTrampled>(r).Add(new GallopTrampled { Value = hits[h] });
                    }
                }

                // Strike in passing (the target may have died to the trample).
                if (strike != Entity.Null && em.Exists(strike)
                    && em.GetComponentData<Health>(strike).Value > 0)
                {
                    _lk.Update(ref state);
                    MeleeCombatSystem.Swing(em, ecb, in _lk, r, strike, elapsed, flankMult, frontArcCos);
                }
            }
            hits.Dispose();
        }

        private static bool Contains(DynamicBuffer<GallopTrampled> buf, Entity e)
        {
            for (int i = 0; i < buf.Length; i++)
                if (buf[i].Value == e) return true;
            return false;
        }
    }
}
