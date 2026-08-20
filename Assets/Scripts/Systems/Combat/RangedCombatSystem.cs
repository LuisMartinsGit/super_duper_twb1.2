// File: Assets/Scripts/Systems/Combat/RangedCombatSystem.cs
using Unity.Entities;
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
        // Default range values (can be overridden by ArcherState)
        private const float DefaultMinRange = 10f;
        private const float DefaultMaxRange = 25f;
        private const float ArrowSpeed = 30f;
        private const float BoltSpeed = 55f; // Siege projectiles (ballista bolts) fly faster

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var dt = SystemAPI.Time.DeltaTime;
            var time = SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            foreach (var (transform, target, archerState, damage, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Target>, RefRW<ArcherState>, RefRO<Damage>, RefRO<FactionTag>>()
                .WithAll<ArcherTag>()
                .WithEntityAccess())
            {
                ref var tgt = ref target.ValueRW;
                ref var archer = ref archerState.ValueRW;

                // Update cooldown timer. Frozen by Recall the Codex
                // (Antiquity): cooldowns do not recover while CodexFrozen.
                if (archer.CooldownTimer > 0 && !em.HasComponent<CodexFrozen>(entity))
                {
                    archer.CooldownTimer -= dt;
                }

                // Validate target exists
                if (tgt.Value == Entity.Null || !em.Exists(tgt.Value))
                {
                    tgt.Value = Entity.Null;
                    if (em.HasComponent<AttackCommand>(entity))
                    {
                        ecb.RemoveComponent<AttackCommand>(entity);
                    }
                    continue;
                }

                // Fix #212: defensively check HasComponent<Health> before reading.
                // If DeathSystem removed Health via ECB playback, GetComponentData
                // would throw.
                if (!em.HasComponent<Health>(tgt.Value))
                {
                    tgt.Value = Entity.Null;
                    if (em.HasComponent<AttackCommand>(entity))
                    {
                        ecb.RemoveComponent<AttackCommand>(entity);
                    }
                    continue;
                }

                // Validate target is alive
                var targetHealth = em.GetComponentData<Health>(tgt.Value);
                if (targetHealth.Value <= 0)
                {
                    tgt.Value = Entity.Null;
                    if (em.HasComponent<AttackCommand>(entity))
                    {
                        ecb.RemoveComponent<AttackCommand>(entity);
                    }
                    continue;
                }

                // The Wall Rule (docs/Design/Combat_Pacing.md): only siege
                // damages wall pieces — a non-siege shooter refuses to loose
                // at one even when force-ordered. Same drop contract as the
                // melee path; the targeting pass filters walls out the same
                // way, so the re-pick lands elsewhere.
                if (em.HasComponent<WallTag>(tgt.Value)
                    && (!em.HasComponent<DamageTypeData>(entity)
                        || em.GetComponentData<DamageTypeData>(entity).Value != DamageType.Siege))
                {
                    tgt.Value = Entity.Null;
                    if (em.HasComponent<AttackCommand>(entity))
                    {
                        ecb.RemoveComponent<AttackCommand>(entity);
                    }
                    continue;
                }

                // Fix #211: skip targets that are currently Invulnerable.
                if (em.HasComponent<Invulnerable>(tgt.Value)) continue;

                // Archers cannot FIRE while moving — but they must keep
                // steering. The old `continue` here was the pursue-jiggle bug:
                // TargetingSystem queues a DesiredDestination{Has=0} clear
                // every frame a live AttackCommand exists, and this system's
                // chase write (below, same end-sim ECB, played back later)
                // only overrode it on stationary ticks — the early-out meant
                // moving ticks wrote nothing, the clear landed uncontested,
                // and Has toggled 1/0/1/0: the unit stuttered every other tick
                // and the aim timer never accumulated. Melee never jiggled
                // precisely because it re-issues its chase every frame. Now
                // ranged does the same: movement intent is (re)written every
                // frame; only the aim/fire block below is gated on standing.
                bool isMoving = false;
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    isMoving = em.GetComponentData<DesiredDestination>(entity).Has != 0;
                }
                if (isMoving)
                {
                    archer.AimTimer = 0;
                    archer.IsFiring = 0;
                }

                var myPos = transform.ValueRO.Position;
                var targetPos = em.GetComponentData<LocalTransform>(tgt.Value).Position;
                var dist = DistXZ(myPos, targetPos);

                // Use archer's configured ranges or defaults
                float minRange = archer.MinRange > 0 ? archer.MinRange : DefaultMinRange;
                float maxRange = archer.MaxRange > 0 ? archer.MaxRange : DefaultMaxRange;

                // High-ground rule: effective range scales per shot with the
                // height difference to THIS target — a shooter above its
                // target reaches farther (up to +20 %), one below falls short
                // (down to -20 %). The same multiplier scales damage at fire
                // time below.
                float heightMult = HeightAdvantage.Multiplier(myPos.y, targetPos.y);
                maxRange *= heightMult;

                // Surface-aware ranging vs bulky targets (buildings): measure
                // to the target's EDGE, not its pivot — a Hall's centre can sit
                // outside an archer's reach even when its walls are in easy
                // range. Buildings with a BuildingSize footprint use the exact
                // axis-aligned rect, same model as MeleeCombatSystem: the
                // legacy circle Radius (max(W,H)/2) is the INSCRIBED circle of
                // a square footprint, so it misjudged rect corners and the
                // short axis of non-square buildings — against 4x4 halls / 7x7
                // temples a catapult could read as inside its own MinRange
                // dead-zone and oscillate retreat/chase without ever letting
                // the aim timer complete. Both the min- AND max-range checks
                // below use this surface distance. (fix 2026-08-03)
                // (Now shared with melee/targeting/arrival via TargetGeometry so
                // every system agrees on how big a given building is.)
                var extent = TargetGeometry.Extent(em, tgt.Value);
                float edgeDist = extent.SurfaceDistXZ(myPos);

                // =============================================================================
                // BEHAVIOR: Too close - RETREAT
                // =============================================================================
                if (edgeDist < minRange)
                {
                    archer.IsRetreating = 1;
                    archer.AimTimer = 0;

                    // Calculate retreat direction (away from target)
                    var retreatDir = math.normalize(myPos - targetPos);
                    var retreatTarget = myPos + retreatDir * (minRange - edgeDist + 3f);

                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = retreatTarget,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = retreatTarget,
                            Has = 1
                        });
                    }
                }
                // =============================================================================
                // BEHAVIOR: In optimal range - AIM AND FIRE
                // =============================================================================
                else if (edgeDist <= maxRange)
                {
                    archer.IsRetreating = 0;

                    // Plant and face the target. Facing used to be siege-only, so
                    // plain archers loosed arrows over their own shoulder.
                    TargetGeometry.StopAndFace(ecb, em, entity, targetPos, dt);

                    // Still mid-step this frame — the stop write above lands at
                    // ECB playback; aim starts accumulating from the NEXT frame
                    // once the unit actually stands. Never fire on the move.
                    if (isMoving) continue;

                    // Full Gallop: a sprinting unit cannot shoot either (mounted
                    // archers keep the same rule as melee cavalry).
                    if (em.HasComponent<TheWaningBorder.Abilities.TempDisarm>(entity)) continue;

                    // Trebuchet pack/unpack: an undeployed trebuchet plants and
                    // faces (StopAndFace above) but must finish its 3 s set-up
                    // before it may aim or fire. TrebuchetDeploySystem (co-located
                    // with the unit) flips Deployed once the engine has stood
                    // with a live target long enough; any movement packs it again.
                    if (em.HasComponent<TrebuchetState>(entity)
                        && em.GetComponentData<TrebuchetState>(entity).Deployed == 0)
                    {
                        archer.AimTimer = 0;
                        archer.IsFiring = 0;
                        continue;
                    }

                    // Use the unit's configured AimTimeRequired as-is
                    // (set per-unit in entity factories: Archer=0.5, Ballista=1.0, etc.)

                    // Accumulate aim time
                    archer.AimTimer += dt;

                    // task-063 phase 1: sect RangedAccuracy multiplier gone with the old
                    // multiplier bridge. Phase 2 reintroduces per-sect levers.
                    float effectiveAimRequired = archer.AimTimeRequired;

                    // Siege units must be SQUARELY on target before firing — a
                    // catapult that looses mid-traverse throws the stone wide.
                    // The rotation itself is done by StopAndFace above (every
                    // ranged unit turns now); this is just the aim gate.
                    bool isSiege = em.HasComponent<SiegeTag>(entity);
                    if (isSiege)
                    {
                        float3 toTarget = targetPos - myPos;
                        toTarget.y = 0;
                        float3 forward = math.mul(
                            em.GetComponentData<LocalTransform>(entity).Rotation, new float3(0, 0, 1));
                        forward.y = 0;
                        if (math.lengthsq(toTarget) > 0.01f && math.lengthsq(forward) > 0.01f)
                        {
                            float dot = math.dot(math.normalizesafe(forward), math.normalizesafe(toTarget));
                            if (dot < 0.9f) // ~25° tolerance before firing
                            {
                                archer.AimTimer = 0;
                                continue;
                            }
                        }
                    }

                    // Fire when aim is ready and cooldown is complete
                    if (archer.AimTimer >= effectiveAimRequired && archer.CooldownTimer <= 0)
                    {
                        archer.IsFiring = 1;

                        // High-ground rule: same height multiplier as the range
                        // scaling above, applied to this shot's damage.
                        int finalDamage = HeightAdvantage.ScaleDamage(damage.ValueRO.Value, myPos.y, targetPos.y);

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
                        if (em.HasComponent<Fortified>(tgt.Value))
                        {
                            var fort = em.GetComponentData<Fortified>(tgt.Value);
                            int fortReduction = (int)fort.ArmorBonus;
                            finalDamage = math.max(1, finalDamage - fortReduction);
                        }

                        // SpellBuff armor bonus on target (Aegis-style timed buff,
                        // StoneheartBastion +3 aura, etc.). Mirrors the Fortified
                        // path — flat reduction on the already-computed damage.
                        // (task-062 C-1)
                        int spellArmor = CombatDamageHelper.GetSpellBuffArmorBonus(em, tgt.Value);
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
                        float3 aimPos = extent.IsBox
                            ? extent.ApproachPoint(myPos, 0f)
                            : targetPos;
                        float aimDist = isSiege ? math.distance(myPos, aimPos) : dist;

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

                        for (int shot = 0; shot < shotCount; shot++)
                        {
                            CreateArrow(ref ecb, myPos, aimPos, aimDist, entity,
                                faction.ValueRO.Value, finalDamage, (float)time + shot * 0.001f, tgt.Value, dmgType,
                                isAOE, aoeRadius, spawnYOffset,
                                archer.Trajectory, archer.ProjectileSpeed, isCatapult,
                                shotBleed, shotIgnites ? ignite : default, shotIgnites);
                        }

                        // Reset state — use unit's configured cooldown.
                        // Glow Ability (Lv 5 active window) shortens by 30%
                        // per the design spec. (audit follow-up)
                        float cooldownValue = 1.5f;
                        if (em.HasComponent<AttackCooldown>(entity))
                            cooldownValue = em.GetComponentData<AttackCooldown>(entity).Cooldown;
                        if (em.HasComponent<GlowAbilityState>(entity)
                            && em.GetComponentData<GlowAbilityState>(entity).ActiveRemaining > 0f)
                            cooldownValue *= (1f / 1.30f);
                        cooldownValue *= CombatDamageHelper.GetFrenzyCooldownMult(em, entity);
                        // Timed haste (Blood Rain and any future SpellBuff
                        // attack-speed effect) fires faster too.
                        cooldownValue *= CombatDamageHelper.GetHasteCooldownMult(em, entity);
                        // Choreographed Volleys: faction-wide archer fire-rate burst.
                        if (em.HasComponent<TheWaningBorder.Abilities.VolleyBuff>(entity))
                        {
                            float m = em.GetComponentData<TheWaningBorder.Abilities.VolleyBuff>(entity).Mult;
                            if (m > 1f) cooldownValue /= m;
                        }

                        archer.CooldownTimer = cooldownValue;
                        archer.AimTimer = 0;
                        archer.IsFiring = 0;
                    }
                }
                // =============================================================================
                // BEHAVIOR: Too far - CHASE (unless holding position or defensive stance)
                // =============================================================================
                else
                {
                    // Hold position units do NOT chase - clear target instead
                    if (em.HasComponent<HoldPositionTag>(entity))
                    {
                        tgt.Value = Entity.Null;
                        archer.AimTimer = 0;
                        if (em.HasComponent<AttackCommand>(entity))
                            ecb.RemoveComponent<AttackCommand>(entity);
                        continue;
                    }

                    archer.IsRetreating = 0;
                    archer.AimTimer = 0;

                    // Move to a position just inside max range, not all the way
                    // to target. Re-issued EVERY frame (see isMoving note above)
                    // so the destination tracks a moving target and always
                    // out-plays TargetingSystem's per-frame Has=0 clear.
                    float3 toTarget = targetPos - myPos;
                    float3 dirToTarget = math.normalizesafe(toTarget);
                    // Stop 2 units inside max range, measured from the EDGE of
                    // bulky targets. (dist - edgeDist) is the center-to-surface
                    // distance along THIS approach line, so the stop point
                    // shares the exact box/circle model of the range checks
                    // above — a mismatched circle stop vs box range check could
                    // land the unit just outside the fire band. (2026-08-03)
                    float stopDist = (dist - edgeDist) + maxRange - 2f;
                    float3 chasePos = targetPos - dirToTarget * stopDist;

                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = chasePos,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = chasePos,
                            Has = 1
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Create an arrow projectile entity.
        /// </summary>
        private void CreateArrow(ref EntityCommandBuffer ecb, float3 start, float3 targetPos,
            float distance, Entity shooter, Faction faction, int damage, float time, Entity targetEntity,
            DamageType dmgType = DamageType.Ranged, bool isAOE = false, float aoeRadius = 0f,
            float spawnYOffset = 1.5f, byte trajectory = ShotTrajectory.Low, float projectileSpeed = 0f,
            bool catapultShot = false,
            InflictsBleed shotBleed = default, IgnitesBlood ignite = default, bool ignitesBlood = false)
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
            var arrow = ecb.CreateEntity();

            ecb.AddComponent(arrow, new LocalTransform
            {
                Position = start + new float3(0, spawnYOffset, 0),
                Rotation = quaternion.LookRotation(velocity, new float3(0, 1, 0)),
                Scale = 1f
            });

            ecb.AddComponent(arrow, new ArrowProjectile
            {
                Velocity = velocity,
                Gravity = 0f,
                Shooter = shooter,
                IsParabolic = false
            });

            ecb.AddComponent(arrow, new Projectile
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

            // Feraldis riders carried by the shot itself (see call site).
            if (shotBleed.DamagePerSecond > 0f && shotBleed.Duration > 0f)
                ecb.AddComponent(arrow, shotBleed);
            if (ignitesBlood)
            {
                ecb.AddComponent(arrow, ignite);
                // Renders as the Synty catapult fire effect, scaled way down.
                ecb.AddComponent<FirethrowerShotTag>(arrow);
            }
        }

    }
}