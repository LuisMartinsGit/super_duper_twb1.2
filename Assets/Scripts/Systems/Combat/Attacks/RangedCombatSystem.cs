using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Handles ranged combat processing for archer units.
    ///
    /// Features:
    /// - Minimum range enforcement with retreat behavior
    /// - Dynamic aim time based on distance
    /// - High-ground rule (HeightAdvantage): per-shot range AND damage scale
    ///   with the shooter-target height difference — shooting downhill grants
    ///   up to +20 % range/damage, shooting uphill costs up to -20 %
    /// - Damage-type propagation to projectiles (via DmgType on Projectile)
    /// - Arrow projectile creation
    /// - Attack cooldown management
    ///
    /// Archers will:
    /// - Retreat if enemies get too close (below MinRange)
    /// - Stop and aim when in optimal range
    /// - Chase enemies that are too far away
    ///
    /// Runs after TargetingSystem to process acquired targets.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct RangedCombatSystem : ISystem
    {
        // ZERO IS A VALID MIN RANGE, AND IT IS NOW THE NORMAL ONE.
        //
        // There used to be a DefaultMinRange of 10 m here, applied whenever
        // ArcherState.MinRange was 0 — the `x > 0 ? x : fallback` shape
        // CLAUDE.md warns about, and it was worse than the usual version of
        // that mistake: authoring minAttackRange: 0 on a unit did not remove
        // its dead zone, it gave the unit a TEN METRE one, larger than any
        // value in the roster. The data could not express the thing a designer
        // would most want to say.
        //
        // Only siege keeps a dead zone now (docs/Design/Combat_Pacing.md):
        // an engine that cannot depress its arc is modelling something. An
        // archer backing away from a swordsman is not — it was a third of the
        // fight spent walking backwards, and it made the bow lines feel like
        // they were refusing to fight.
        private const float DefaultMaxRange = 25f;
        // 30 -> 39 (2026-09-03): arrow flight sped up 30 percent.
        private const float ArrowSpeed = 39f;
        private const float BoltSpeed = 55f; // Siege projectiles (ballista bolts) fly faster

        /// <summary>The per-unit reads, through lookups (2026-10-15 perf
        /// pass — see CombatLookups). Created once, refreshed every update.</summary>
        private CombatLookups _lk;

        /// <summary>Every shot starts from this archetype (LocalTransform +
        /// ArrowProjectile + Projectile): one structural step at playback
        /// instead of create + three adds. The optional riders are still
        /// added after it, so the final component set is unchanged.</summary>
        private EntityArchetype _arrowArch;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            _lk = CombatLookups.Create(ref state);
            _arrowArch = state.EntityManager.CreateArchetype(
                typeof(LocalTransform), typeof(ArrowProjectile), typeof(Projectile));
        }

        /// <summary>Drop the AttackCommand if it is live — TransientState.Active
        /// + Clear, through the lookup (Active implies present).</summary>
        private static void DropAttackCommand(in CombatLookups lk, EntityCommandBuffer ecb, Entity e)
        {
            if (CombatLookups.Active(in lk.AttackCommand, e))
                ecb.SetComponentEnabled<AttackCommand>(e, false);
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var dt = SystemAPI.Time.DeltaTime;
            var time = SimCadence.MatchTimeOr(SystemAPI.Time.ElapsedTime);
            var em = state.EntityManager;
            _lk.Update(ref state);
            var lk = _lk;

            // The ranged loop, Burst (RangedCombatSystem.Jobs.cs): a parallel
            // plan per unit, then an in-order apply that hands each shot back
            // here. Same entity set and order as the old foreach.
            var query = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, Damage, FactionTag, ArcherTag>()
                .WithAllRW<Target, ArcherState>()
                // A CORPSE DOES NOT SHOOT. DeathAnimationState is added the
                // moment DeathSystem registers the death; on the frame a unit
                // dies it is still in this query and its zero Health (read by
                // the plan) closes that window.
                .WithNone<DeathAnimationState>()
                .WithNone<TheWaningBorder.Entities.Launched>()   // airborne units shoot at nothing
                .Build();

            using var units = query.ToEntityArray(Unity.Collections.Allocator.TempJob);
            int n = units.Length;
            using var plans = new Unity.Collections.NativeArray<RangedPlan>(n,
                Unity.Collections.Allocator.TempJob, Unity.Collections.NativeArrayOptions.UninitializedMemory);

            var planJob = new RangedPlanJob
            {
                Units = units,
                Lk = lk,
                Targets = SystemAPI.GetComponentLookup<Target>(true),
                Archers = SystemAPI.GetComponentLookup<ArcherState>(true),
                Damages = SystemAPI.GetComponentLookup<Damage>(true),
                Trebuchets = SystemAPI.GetComponentLookup<TrebuchetState>(true),
                Storage = SystemAPI.GetEntityStorageInfoLookup(),
                Dt = dt,
                Plans = plans,
            };
            var planHandle = planJob.Schedule(n, 32, state.Dependency);
            planHandle.Complete();

            using var stop = new Unity.Collections.NativeReference<int>(0, Unity.Collections.Allocator.TempJob);
            var apply = new RangedApplyJob
            {
                Units = units,
                Plans = plans,
                Targets = SystemAPI.GetComponentLookup<Target>(false),
                Archers = SystemAPI.GetComponentLookup<ArcherState>(false),
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(false),
                Ecb = ecb,
                Stop = stop,
            };
            int next = 0;
            while (next < n)
            {
                apply.Start = next;
                apply.Run();
                int k = stop.Value;
                if (k >= n) break;
                Fire(em, ecb, _arrowArch, units[k], plans[k], time);
                next = k + 1;
            }
        }

        /// <summary>
        /// One volley, unchanged from the old loop body: damage, riders,
        /// projectile spawn, cooldown reset. Managed (Feraldis / sect / ability
        /// helpers); called by OnUpdate between apply runs, in query order.
        /// </summary>
        private static void Fire(EntityManager em, EntityCommandBuffer ecb, EntityArchetype arrowArch,
            Entity entity, in RangedPlan plan, double time)
        {
            var archer = em.GetComponentData<ArcherState>(entity);
            var tgt = new Target { Value = plan.Target };
            float3 myPos = plan.MyPos;
            float3 targetPos = plan.TargetPos;
            float3 aimPos = plan.AimPos;
            float aimDist = plan.AimDist;
            bool isSiege = plan.IsSiege != 0;

            // High-ground damage — or, from a wall-walk, the wall bonus INSTEAD
            // (docs/Design/Combat_Pacing.md § Fighting from a wall-walk). The
            // range side of HeightAdvantage (plan job) is kept either way.
            int finalDamage = math.max(1, (int)math.round(em.GetComponentData<Damage>(entity).Value
                * CombatDamageHelper.AttackerHeightOrWallMult(em, entity,
                    HeightAdvantage.Multiplier(myPos.y, targetPos.y))));

            // Veilstone buff on attacker (bonus damage)
            if (em.HasComponent<BorderBuff>(entity))
            {
                var buff = em.GetComponentData<BorderBuff>(entity);
                finalDamage = (int)math.round(finalDamage * (1f + buff.AttBonus));
            }
            // Note: BorderDebuff on target is applied at projectile impact, not here

            // Feraldis: blood frenzy (the Hunter frenzies on blood
            // like every other Feraldis unit).
            float frenzy = CombatDamageHelper.GetFrenzyDamageMult(em, entity);
            if (frenzy != 1f)
                finalDamage = (int)math.round(finalDamage * frenzy);

            finalDamage = math.max(1, finalDamage);

            // task-063 phase 1: sect RangedDamage / DamageVsBorder multipliers
            // gone — baseline 1.0×. Phase 2 reintroduces per-sect levers.

            // Fortified armor bonus on target (flat defense increase)
            if (TransientState.Active<Fortified>(em, tgt.Value))
            {
                var fort = em.GetComponentData<Fortified>(tgt.Value);
                int fortReduction = (int)fort.ArmorBonus;
                finalDamage = math.max(1, finalDamage - fortReduction);
            }

            // SpellBuff armor bonus on target (Aegis-style timed buff,
            // StoneheartBastion +3 aura, etc.). Mirrors the Fortified
            // path — flat reduction on the already-computed damage.
            // (task-062 C-1)
            int spellArmor = CombatDamageHelper.GetSpellBuffArmorBonus(em, tgt.Value, CombatDamageHelper.BaseDefense(em, tgt.Value, em.HasComponent<DamageTypeData>(entity) ? em.GetComponentData<DamageTypeData>(entity).Value : DamageType.Ranged));
            if (spellArmor > 0)
                finalDamage = math.max(1, finalDamage - spellArmor);

            // Fix #226: on-hit bonus damage (Condemned/Ignite/VoidStrike) routed through shared helper.
            // ApplyDamageReflect is intentionally NOT called here — for
            // ranged attacks the reflect must trigger at impact, not at
            // fire time, so the shooter only loses HP if the projectile
            // actually lands (target alive, not blocked, etc). Calling
            // here would punish a missed shot or one whose target died
            // before impact, and would double-reflect against
            // the original target. ProjectileSystem.ApplyDamage handles
            // the impact-time reflect call. (task-062 C-2)

            finalDamage = math.max(1, finalDamage);

            // Get shooter's damage type (default Ranged for archers)
            DamageType dmgType = DamageType.Ranged;
            if (em.HasComponent<DamageTypeData>(entity))
                dmgType = em.GetComponentData<DamageTypeData>(entity).Value;

            // Spawn height: use entity's Radius + 0.5f (taller units shoot higher)
            float spawnYOffset = em.HasComponent<Radius>(entity)
                ? em.GetComponentData<Radius>(entity).Value + 0.5f
                : 1.5f;

            // Create projectile(s)
            bool isAOE = em.HasComponent<AOEShooterData>(entity);
            float aoeRadius = isAOE ? em.GetComponentData<AOEShooterData>(entity).Radius : 0f;
            bool isCatapult = em.HasComponent<CatapultTag>(entity);

            // Siege bolt-throwers volley 3 bolts; catapults lob ONE AOE stone.
            int shotCount = isSiege && !isCatapult ? 3 : 1;
            // Aim at the target's SURFACE, not its pivot.
            //
            // Range is measured edge-to-edge (edgeDist above), but
            // the shot was always sent at the centre — so against a
            // building the arrow flew past the wall it was ranged
            // against and landed in the middle of the footprint.
            // Doubling the footprints made that gap impossible to
            // miss: a Hall's pivot is now 4 m behind its wall and a
            // Temple's is 8 m, so arrows visibly sailed over the
            // near wall. Aiming at the surface point facing the
            // shooter puts the arc back in agreement with the
            // ranging that authorised the shot.
            //
            // Units are unaffected: a non-box target's surface
            // point is its centre offset by its own small radius.

            // Feraldis on-hit riders travel WITH the shot, so a
            // volley that lands after its shooter dies still
            // bleeds / ignites. (Axe Thrower, Firethrower.)
            InflictsBleed shotBleed = em.HasComponent<InflictsBleed>(entity)
                ? em.GetComponentData<InflictsBleed>(entity)
                : default;
            bool shotIgnites = em.HasComponent<IgnitesBlood>(entity);
            IgnitesBlood ignite = shotIgnites
                ? em.GetComponentData<IgnitesBlood>(entity)
                : default;

            // 1 = ballista bolt, 2 = trebuchet stone (the projectile's look).
            byte shooterKind = em.HasComponent<BallistaShooterTag>(entity) ? (byte)1
                : em.HasComponent<TrebuchetShooterTag>(entity) ? (byte)2 : (byte)0;
            for (int shot = 0; shot < shotCount; shot++)
            {
                CreateArrow(ref ecb, arrowArch, myPos, aimPos, aimDist, entity,
                    em.GetComponentData<FactionTag>(entity).Value, finalDamage, (float)time + shot * 0.001f, tgt.Value, dmgType,
                    isAOE, aoeRadius, spawnYOffset,
                    archer.Trajectory, archer.ProjectileSpeed, isCatapult,
                    shotBleed, shotIgnites ? ignite : default, shotIgnites, shooterKind,
                    em.HasComponent<CorpseEngineTag>(entity) ? (byte)1
                    : em.HasComponent<HarpoonRigTag>(entity) ? (byte)2 : (byte)0);
            }

            // Reset state — use unit's configured cooldown.
            // Glow Ability (Lv 5 active window) shortens by 30%
            // per the design spec. (audit follow-up)
            float cooldownValue = 1.5f;
            if (em.HasComponent<AttackCooldown>(entity))
                cooldownValue = em.GetComponentData<AttackCooldown>(entity).Cooldown;
            if (em.HasComponent<ShardrootAbilityState>(entity)
                && em.GetComponentData<ShardrootAbilityState>(entity).ActiveRemaining > 0f)
                cooldownValue *= (1f / 1.30f);
            cooldownValue *= CombatDamageHelper.GetFrenzyCooldownMult(em, entity);
            // Timed haste (Blood Rain and any future SpellBuff
            // attack-speed effect) fires faster too.
            cooldownValue *= CombatDamageHelper.GetHasteCooldownMult(em, entity);
            // Choreographed Volleys: faction-wide archer fire-rate burst.
            if (TransientState.Active<TheWaningBorder.Abilities.VolleyBuff>(em, entity))
            {
                float m = em.GetComponentData<TheWaningBorder.Abilities.VolleyBuff>(entity).Mult;
                if (m > 1f) cooldownValue /= m;
            }

            archer.CooldownTimer = cooldownValue;
            archer.AimTimer = 0;
            archer.IsFiring = 0;
            em.SetComponentData(entity, archer);
        }

        /// <summary>
        /// Create an arrow projectile entity.
        /// </summary>
        private static void CreateArrow(ref EntityCommandBuffer ecb, EntityArchetype arrowArch, float3 start, float3 targetPos,
            float distance, Entity shooter, Faction faction, int damage, float time, Entity targetEntity,
            DamageType dmgType = DamageType.Ranged, bool isAOE = false, float aoeRadius = 0f,
            float spawnYOffset = 1.5f, byte trajectory = ShotTrajectory.Low, float projectileSpeed = 0f,
            bool catapultShot = false,
            InflictsBleed shotBleed = default, IgnitesBlood ignite = default, bool ignitesBlood = false,
            byte shooterKind = 0, byte feraldisShot = 0)
        {
            // Calculate initial velocity towards target
            var direction = math.normalize(targetPos - start);

            // Apply 15° uncertainty to projectile direction (spread fire)
            const float UncertaintyDeg = 15f;
            float halfRad = math.radians(UncertaintyDeg * 0.5f);
            // Deterministic pseudo-random based on shooter + time
            uint seed = (uint)(shooter.Index * 17 + (int)(time * 1000f));
            seed = seed * 1103515245 + 12345;
            float yawOffset = ((seed % 1000) / 1000f - 0.5f) * 2f * halfRad;
            seed = seed * 1103515245 + 12345;
            float pitchOffset = ((seed % 1000) / 1000f - 0.5f) * 2f * halfRad * 0.3f; // less vertical spread
            quaternion yawRot = quaternion.AxisAngle(new float3(0, 1, 0), yawOffset);
            quaternion pitchRot = quaternion.AxisAngle(math.normalizesafe(math.cross(direction, new float3(0, 1, 0))), pitchOffset);
            direction = math.mul(yawRot, math.mul(pitchRot, direction));
            direction = math.normalizesafe(direction);

            // Add slight upward arc for visual appeal — skipped for FLAT
            // trajectories (crossbow bolts fly dead straight at the target).
            if (trajectory != ShotTrajectory.Flat)
            {
                float minPitch = math.radians(5f);
                float currentPitch = math.asin(direction.y);
                if (currentPitch < minPitch)
                {
                    float3 horizontalDir = math.normalize(new float3(direction.x, 0, direction.z));
                    direction = horizontalDir * math.cos(minPitch) + new float3(0, math.sin(minPitch), 0);
                    direction = math.normalize(direction);
                }
            }

            float speed = (dmgType == DamageType.Siege) ? BoltSpeed : ArrowSpeed;
            if (projectileSpeed > 0f) speed = projectileSpeed;
            // Catapult stones hang in the air — a 2-3 s arc scaled by range
            // (design 2026-08-02) instead of the fast siege-bolt speed.
            // Flat-trajectory shooters (the Ballista refit) keep their
            // authored projectileSpeed — a hang-time override would make the
            // straight bolt crawl.
            if (catapultShot && trajectory != ShotTrajectory.Flat)
            {
                float flight = math.clamp(distance / 9f, 2f, 3f);
                speed = math.max(0.1f, distance / flight);
            }
            var velocity = direction * speed;
            var estimatedFlightTime = distance / speed;

            // Create arrow entity
            var arrow = ecb.CreateEntity(arrowArch);

            ecb.SetComponent(arrow, new LocalTransform
            {
                Position = start + new float3(0, spawnYOffset, 0),
                Rotation = quaternion.LookRotation(velocity, new float3(0, 1, 0)),
                Scale = 1f
            });

            ecb.SetComponent(arrow, new ArrowProjectile
            {
                Velocity = velocity,
                Gravity = 0f,
                Shooter = shooter,
                IsParabolic = false
            });

            ecb.SetComponent(arrow, new Projectile
            {
                Start = start,
                End = targetPos,
                StartTime = time,
                FlightTime = estimatedFlightTime,
                Damage = damage,
                Target = targetEntity,
                Faction = faction,
                DmgType = dmgType
            });

            if (isAOE)
                ecb.AddComponent(arrow, new AOEProjectile { Radius = aoeRadius });

            // Trajectory profile (design 2026-07-04):
            //   low  — default capped Bezier arc (shortbow), no component.
            //   flat — crossbow bolt: ArcFraction 0 flattens the Bezier into a
            //          straight line; combined with a high ProjectileSpeed it
            //          reads as a fast, direct shot.
            //   high — longbow: tall parabola, same shape family as siege lobs.
            if (trajectory == ShotTrajectory.Flat)
                ecb.AddComponent(arrow, new HighArcProjectile { ArcFraction = 0f });
            else if (trajectory == ShotTrajectory.High)
                ecb.AddComponent(arrow, new HighArcProjectile
                {
                    // Catapult lobs peak noticeably higher than longbow shots.
                    ArcFraction = catapultShot ? 0.45f : 0.30f
                });

            // Siege BOLTS pierce through multiple targets; catapult stones
            // don't — they burst on impact (AOEProjectile above) instead.
            if (dmgType == DamageType.Siege && !catapultShot)
                ecb.AddComponent(arrow, new PiercingProjectile { RemainingPierces = 5 });

            // Catapult stones render as the Synty FX_Catapult effect
            // (ProjectileVisualSystem picks the template off this tag).
            // Flat-trajectory siege (the Ballista) is exempt: its bolt is
            // rendered per-entity by ProjectileVisualSystem, so the visual IS
            // the damage carrier and impact timing can never drift.
            if (catapultShot && trajectory != ShotTrajectory.Flat)
                ecb.AddComponent<CatapultShotTag>(arrow);

            // The engine's own projectile look: a large bolt for ballistas, the
            // Synty siege rock for trebuchets (ProjectileVisualSystem).
            if (shooterKind == 1) ecb.AddComponent<BallistaBoltTag>(arrow);
            else if (shooterKind == 2) ecb.AddComponent<TrebuchetStoneTag>(arrow);

            // Feraldis riders carried by the shot itself (see call site).
            if (shotBleed.DamagePerSecond > 0f && shotBleed.Duration > 0f)
                ecb.AddComponent(arrow, shotBleed);
            // Corpse Engine (blood on impact) and Harpoon Rig (barbed wire) —
            // docs/Design/Age_1_Feraldis.md §7.5.
            if (feraldisShot == 1) ecb.AddComponent(arrow, new CorpseShot());
            else if (feraldisShot == 2) ecb.AddComponent(arrow, new HarpoonShot { From = start, Shooter = shooter });
            if (ignitesBlood)
            {
                ecb.AddComponent(arrow, ignite);
                // Renders as the Synty catapult fire effect, scaled way down.
                ecb.AddComponent<FirethrowerShotTag>(arrow);
            }
        }

    }
}