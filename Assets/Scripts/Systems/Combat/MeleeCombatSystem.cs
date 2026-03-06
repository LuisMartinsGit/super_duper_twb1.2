// File: Assets/Scripts/Systems/Combat/MeleeCombatSystem.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Handles melee combat processing for non-ranged units.
    ///
    /// Features:
    /// - Damage-type vs armor-type modifier matrix (via CombatModifiers)
    /// - Per-damage-type defense with diminishing returns
    /// - Height-based damage modifiers (±20% cap)
    /// - Crystal buff/debuff integration
    /// - Attack cooldown management
    /// - Chase behavior when target is out of range
    /// - Minimum damage guarantee (never less than 1)
    ///
    /// Runs after TargetingSystem to process acquired targets.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TargetingSystem))]
    public partial struct MeleeCombatSystem : ISystem
    {
        private const float MeleeRange = 1.5f;

        // Height damage modifier settings
        private const float HeightDamageScale = 0.04f; // 4% per unit height diff
        private const float MaxHeightBonus = 0.20f;    // Cap at +20%
        private const float MaxHeightPenalty = -0.20f; // Cap at -20%

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var dt = SystemAPI.Time.DeltaTime;
            var em = state.EntityManager;

            foreach (var (transform, target, cooldown, damage, entity) in SystemAPI
                .Query<RefRO<LocalTransform>, RefRW<Target>, RefRW<AttackCooldown>, RefRO<Damage>>()
                .WithAll<UnitTag>()
                .WithNone<ArcherTag>()  // Exclude ranged units
                .WithEntityAccess())
            {
                ref var tgt = ref target.ValueRW;
                ref var cd = ref cooldown.ValueRW;

                // Update cooldown timer
                if (cd.Timer > 0)
                {
                    cd.Timer -= dt;
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

                var myPos = transform.ValueRO.Position;
                var targetPos = em.GetComponentData<LocalTransform>(tgt.Value).Position;
                var dist = DistXZ(myPos, targetPos);

                // In melee range - attack
                if (dist <= MeleeRange)
                {
                    // Stop moving when in range
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.SetComponent(entity, new DesiredDestination { Has = 0 });
                    }

                    // Attack if cooldown is ready
                    if (cd.Timer <= 0)
                    {
                        int baseDamage = damage.ValueRO.Value;

                        // Get attacker's damage type (default Melee if component missing)
                        DamageType dmgType = DamageType.Melee;
                        if (em.HasComponent<DamageTypeData>(entity))
                            dmgType = em.GetComponentData<DamageTypeData>(entity).Value;

                        // Get target's armor type (default InfantryLight if missing)
                        ArmorType armorType = ArmorType.InfantryLight;
                        if (em.HasComponent<ArmorTypeData>(tgt.Value))
                            armorType = em.GetComponentData<ArmorTypeData>(tgt.Value).Value;

                        // Get target's defense for this damage type
                        int defenseValue = 0;
                        if (em.HasComponent<Defense>(tgt.Value))
                            defenseValue = CombatModifiers.GetDefenseValue(em.GetComponentData<Defense>(tgt.Value), dmgType);

                        // Calculate height-based damage modifier
                        float heightMod = CalculateHeightDamageModifier(myPos.y, targetPos.y);

                        // Crystal modifier
                        float crystalMod = 1.0f;
                        if (em.HasComponent<CrystalBuff>(entity))
                        {
                            var buff = em.GetComponentData<CrystalBuff>(entity);
                            crystalMod *= 1f + buff.AttBonus;
                        }
                        if (em.HasComponent<CrystalDebuff>(tgt.Value))
                        {
                            var debuff = em.GetComponentData<CrystalDebuff>(tgt.Value);
                            crystalMod *= 1f + debuff.AttPenalty;
                        }

                        int finalDamage = CombatModifiers.CalculateFinalDamage(
                            baseDamage, dmgType, armorType, defenseValue, heightMod, crystalMod);

                        // Apply damage
                        var health = em.GetComponentData<Health>(tgt.Value);
                        health.Value -= finalDamage;
                        if (health.Value < 0) health.Value = 0;
                        ecb.SetComponent(tgt.Value, health);

                        // Track last damager faction for kill credit (used by PillageSystem, CaravanDeathSystem)
                        if (em.HasComponent<FactionTag>(entity))
                        {
                            ecb.AddComponent(tgt.Value, new LastDamagedByFaction
                            {
                                Value = em.GetComponentData<FactionTag>(entity).Value
                            });
                        }

                        // Reset cooldown
                        cd.Timer = cd.Cooldown;
                    }
                }
                else
                {
                    // Out of range - hold position units do NOT chase
                    if (em.HasComponent<HoldPositionTag>(entity))
                    {
                        // Clear target so unit stays put
                        tgt.Value = Entity.Null;
                        if (em.HasComponent<AttackCommand>(entity))
                            ecb.RemoveComponent<AttackCommand>(entity);
                        continue;
                    }

                    // Chase target
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
        /// Calculate height-based damage modifier.
        /// Returns multiplier: 0.8 to 1.2 (±20% cap)
        /// Higher ground = bonus damage, lower ground = penalty
        /// </summary>
        private static float CalculateHeightDamageModifier(float attackerHeight, float targetHeight)
        {
            float heightDiff = attackerHeight - targetHeight;
            float modifier = heightDiff * HeightDamageScale;

            // Clamp to ±20%
            modifier = math.clamp(modifier, MaxHeightPenalty, MaxHeightBonus);

            return 1.0f + modifier;
        }

        private static float DistXZ(float3 a, float3 b)
        {
            return math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
        }
    }
}