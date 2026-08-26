using Unity.Entities;
using Unity.Mathematics;
using static TheWaningBorder.Core.MathUtil;
using Unity.Transforms;
using TheWaningBorder.Systems.Combat;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Hybrid siege/ranged combat system for Godsplinter veilstone units.
    ///
    /// Two combat modes:
    /// 1. Siege mode (close range): Direct damage to target (modifier from combat matrix)
    /// 2. Laser mode (medium range): Multi-target laser barrage (up to 4 targets)
    ///
    /// Behavior priority:
    /// - If in siege range and siege cooldown ready: siege attack
    /// - Else if in laser range and laser cooldown ready: laser barrage
    /// - Else if beyond laser range: chase (unless HoldPositionTag)
    ///
    /// Uses Projectile + ArrowProjectile entities for laser visuals,
    /// processed by the existing ProjectileSystem.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct GodsplinterCombatSystem : ISystem
    {
        private const float SiegeCooldownDuration = 3.0f;
        // Long-arc lob: ~1.4 s flight time reads as a slow, weighty trebuchet
        // shell rather than a hitscan beam. Combined with HighArcProjectile,
        // this gives an obvious telegraph the player can dodge.
        private const float BombardFlightTime = 1.4f;


        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            float dt = SystemAPI.Time.DeltaTime;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            foreach (var (transform, target, godState, damage, faction, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Target>, RefRW<GodsplinterState>, RefRO<Damage>, RefRO<FactionTag>>()
                .WithAll<BorderUnitTag>()
                .WithEntityAccess())
            {
                ref var tgt = ref target.ValueRW;
                ref var gs = ref godState.ValueRW;

                // Update cooldown timers
                if (gs.SiegeCooldownTimer > 0)
                    gs.SiegeCooldownTimer -= dt;
                if (gs.LaserCooldownTimer > 0)
                    gs.LaserCooldownTimer -= dt;

                // Validate target exists and is alive
                if (tgt.Value == Entity.Null || !em.Exists(tgt.Value))
                {
                    tgt.Value = Entity.Null;
                    gs.IsSieging = 0;
                    continue;
                }

                var targetHealth = em.GetComponentData<Health>(tgt.Value);
                if (targetHealth.Value <= 0)
                {
                    tgt.Value = Entity.Null;
                    gs.IsSieging = 0;
                    continue;
                }

                var myPos = transform.ValueRO.Position;
                var targetPos = em.GetComponentData<LocalTransform>(tgt.Value).Position;
                var dist = DistXZ(myPos, targetPos);
                int baseDmg = damage.ValueRO.Value;
                Faction myFaction = faction.ValueRO.Value;

                // Get Godsplinter's damage type (default Siege)
                DamageType dmgType = DamageType.Siege;
                if (em.HasComponent<DamageTypeData>(entity))
                    dmgType = em.GetComponentData<DamageTypeData>(entity).Value;

                // =============================================================================
                // BEHAVIOR 1: Siege mode - close range direct damage
                // =============================================================================
                if (dist <= gs.SiegeRange && gs.SiegeCooldownTimer <= 0)
                {
                    // Skip Invulnerable target — Godsplinter siege damage previously
                    // bypassed LockdownVault. (task-062 C-4)
                    if (em.HasComponent<Invulnerable>(tgt.Value)) continue;

                    gs.IsSieging = 1;

                    // Stop moving
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                    }

                    // Calculate siege damage (modifier handled by combat matrix)
                    int siegeDmg = baseDmg;

                    // Get target's armor type
                    ArmorType armorType = ArmorType.InfantryLight;
                    if (em.HasComponent<ArmorTypeData>(tgt.Value))
                        armorType = em.GetComponentData<ArmorTypeData>(tgt.Value).Value;

                    // Get target's defense for this damage type
                    int defenseValue = 0;
                    if (em.HasComponent<Defense>(tgt.Value))
                        defenseValue = CombatModifiers.GetDefenseValue(em.GetComponentData<Defense>(tgt.Value), dmgType);

                    // Veilstone modifier
                    float borderMod = 1.0f;
                    if (em.HasComponent<BorderBuff>(entity))
                    {
                        var buff = em.GetComponentData<BorderBuff>(entity);
                        borderMod *= 1f + buff.AttBonus;
                    }
                    if (em.HasComponent<BorderDebuff>(tgt.Value))
                    {
                        var debuff = em.GetComponentData<BorderDebuff>(tgt.Value);
                        borderMod *= 1f + debuff.AttPenalty;
                    }

                    // Tag bonus (AoE4-style, from the unit SO — e.g. +vs Building).
                    int tagBonus = TagBonus.Compute(em, entity, tgt.Value);

                    int siegeFinal = CombatModifiers.CalculateFinalDamage(
                        siegeDmg, dmgType, armorType, defenseValue, 1.0f, borderMod, tagBonus);

                    // Apply direct damage to target — use immediate write so multiple attackers
                    // in the same frame correctly stack damage (not last-write-wins via ECB)
                    var health = em.GetComponentData<Health>(tgt.Value);
                    health.Value -= siegeFinal;
                    if (health.Value < 0) health.Value = 0;
                    em.SetComponentData(tgt.Value, health);

                    // Track last damager faction for kill credit (used by PillageSystem, CaravanDeathSystem)
                    if (em.HasComponent<FactionTag>(entity))
                    {
                        var lastDamaged = new LastDamagedByFaction
                        {
                            Value = em.GetComponentData<FactionTag>(entity).Value
                        };
                        if (em.HasComponent<LastDamagedByFaction>(tgt.Value))
                            em.SetComponentData(tgt.Value, lastDamaged);
                            else
                                ecb.AddComponent(tgt.Value, lastDamaged);
                    }

                    // Reset siege cooldown. Per-unit cadence comes from the
                    // unit's SO (siegeCooldown); 0 = legacy constant.
                    gs.SiegeCooldownTimer = gs.SiegeCooldown > 0f ? gs.SiegeCooldown : SiegeCooldownDuration;
                }
                // =============================================================================
                // BEHAVIOR 2: Arcing AOE bombard — long range, single splash shot
                // =============================================================================
                else if (dist <= gs.LaserRange && gs.LaserCooldownTimer <= 0)
                {
                    gs.IsSieging = 0;

                    // Bombard while moving — DesiredDestination is left alone so
                    // a Godsplinter on attack-move keeps walking between volleys.

                    // Veilstone buff on attacker (bonus damage)
                    int shotDmg = baseDmg;
                    if (em.HasComponent<BorderBuff>(entity))
                    {
                        var buff = em.GetComponentData<BorderBuff>(entity);
                        shotDmg = (int)math.round(shotDmg * (1f + buff.AttBonus));
                        shotDmg = math.max(1, shotDmg);
                    }

                    // Spawn height: use entity's Radius + 0.5f (taller units shoot higher)
                    float spawnYOffset = em.HasComponent<Radius>(entity)
                        ? em.GetComponentData<Radius>(entity).Value + 0.5f
                        : 1.5f;

                    // Splash radius from the unit's SO (aoeRadius); 0 = constant.
                    float aoeRadius = gs.AoeRadius > 0f ? gs.AoeRadius : GodsplinterAoeRadius;

                    CreateArcedAoeShot(ref ecb, myPos, targetPos,
                        entity, myFaction, shotDmg, time, tgt.Value, dmgType, spawnYOffset, aoeRadius);

                    // Per-unit cadence comes from the unit's SO (attackCooldown);
                    // 0 = legacy constant.
                    gs.LaserCooldownTimer = gs.LaserCooldown > 0f ? gs.LaserCooldown : GodsplinterFireCooldown;
                }
                // =============================================================================
                // BEHAVIOR 3: Too far - CHASE (unless holding position)
                // =============================================================================
                else if (dist > gs.LaserRange)
                {
                    gs.IsSieging = 0;

                    // Hold position units do NOT chase
                    if (em.HasComponent<HoldPositionTag>(entity))
                    {
                        tgt.Value = Entity.Null;
                        continue;
                    }

                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = targetPos,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = targetPos,
                            Has = 1
                        });
                    }
                }
            }

        }

        /// <summary>
        /// Spawn a single Godsplinter siege bombard — a slow, high parabolic
        /// shell that lands an AOE splash on impact. No <c>LaserProjectileTag</c>
        /// so <c>ProjectileSystem</c> routes it through the arrow/Bezier path
        /// and uses the <c>HighArcProjectile</c> component to peak high above
        /// the midpoint (siege trajectory). Splash radius is
        /// <c>GodsplinterAoeRadius</c>.
        /// </summary>
        private static void CreateArcedAoeShot(ref EntityCommandBuffer ecb, float3 start, float3 targetPos,
            Entity shooter, Faction faction, int damage, float time, Entity targetEntity,
            DamageType dmgType, float spawnYOffset, float aoeRadius)
        {
            float3 spawnPos = start + new float3(0, spawnYOffset, 0);
            float3 dir = targetPos - spawnPos;
            var direction = math.normalizesafe(dir, new float3(0, 0, 1));

            var shell = ecb.CreateEntity();

            ecb.AddComponent(shell, new LocalTransform
            {
                Position = spawnPos,
                Rotation = quaternion.LookRotation(direction, new float3(0, 1, 0)),
                Scale = 1f
            });

            ecb.AddComponent(shell, new ArrowProjectile
            {
                Velocity = direction, // overwritten each frame by Bezier tangent
                Gravity = 0f,
                Shooter = shooter,
                IsParabolic = true
            });

            ecb.AddComponent(shell, new Projectile
            {
                Start = spawnPos,
                End = targetPos,
                StartTime = time,
                FlightTime = BombardFlightTime,
                Damage = damage,
                Target = targetEntity,
                Faction = faction,
                DmgType = dmgType
            });

            // Splash damage on impact.
            ecb.AddComponent(shell, new AOEProjectile { Radius = aoeRadius });

            // High parabolic trajectory (siege lob).
            ecb.AddComponent(shell, new HighArcProjectile { ArcFraction = GodsplinterArcFraction });

            // GodsplinterProjectileTag drives the largest arcane-missile visual
            // and impact VFX. NO LaserProjectileTag → ProjectileSystem follows
            // the arrow/Bezier path and applies HighArcProjectile.
            ecb.AddComponent<GodsplinterProjectileTag>(shell);
        }

    }
}
