// File: Assets/Scripts/Systems/Combat/MeleeCombatSystem.cs
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Economy;

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

                // Account for target's physical radius (buildings are larger)
                float targetRadius = 0f;
                if (em.HasComponent<Radius>(tgt.Value))
                    targetRadius = em.GetComponentData<Radius>(tgt.Value).Value;
                float effectiveMeleeRange = MeleeRange + targetRadius;

                // In melee range - attack
                if (dist <= effectiveMeleeRange)
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

                        // --- Batch-read attacker components once ---
                        DamageType dmgType = DamageType.Melee;
                        bool attackerHasDmgType = em.HasComponent<DamageTypeData>(entity);
                        if (attackerHasDmgType)
                            dmgType = em.GetComponentData<DamageTypeData>(entity).Value;

                        bool attackerHasBuff = em.HasComponent<CrystalBuff>(entity);
                        CrystalBuff attackerBuff = attackerHasBuff
                            ? em.GetComponentData<CrystalBuff>(entity)
                            : default;

                        // --- Batch-read target components once ---
                        bool targetHasArmor = em.HasComponent<ArmorTypeData>(tgt.Value);
                        ArmorType armorType = targetHasArmor
                            ? em.GetComponentData<ArmorTypeData>(tgt.Value).Value
                            : ArmorType.InfantryLight;

                        bool targetHasDefense = em.HasComponent<Defense>(tgt.Value);
                        int defenseValue = targetHasDefense
                            ? CombatModifiers.GetDefenseValue(em.GetComponentData<Defense>(tgt.Value), dmgType)
                            : 0;

                        // Fortified armor bonus on target
                        if (em.HasComponent<Fortified>(tgt.Value))
                        {
                            var fort = em.GetComponentData<Fortified>(tgt.Value);
                            defenseValue += (int)fort.ArmorBonus;
                        }

                        bool targetHasDebuff = em.HasComponent<CrystalDebuff>(tgt.Value);
                        CrystalDebuff targetDebuff = targetHasDebuff
                            ? em.GetComponentData<CrystalDebuff>(tgt.Value)
                            : default;

                        bool targetHasLastDamaged = em.HasComponent<LastDamagedByFaction>(tgt.Value);
                        bool targetHasLastAttacker = em.HasComponent<LastAttackerEntity>(tgt.Value);

                        // Calculate height-based damage modifier
                        float heightMod = CalculateHeightDamageModifier(myPos.y, targetPos.y);

                        // Crystal modifier (uses pre-fetched data)
                        float crystalMod = 1.0f;
                        if (attackerHasBuff)
                            crystalMod *= 1f + attackerBuff.AttBonus;
                        if (targetHasDebuff)
                            crystalMod *= 1f + targetDebuff.AttPenalty;

                        int finalDamage = CombatModifiers.CalculateFinalDamage(
                            baseDamage, dmgType, armorType, defenseValue, heightMod, crystalMod);

                        // Apply sect melee damage and damage-vs-crystal multipliers
                        FactionSectState.SectMultipliers sectMults = default;
                        bool hasSectMults = false;
                        if (FactionSectState.Instance != null && em.HasComponent<FactionTag>(entity))
                        {
                            sectMults = FactionSectState.Instance.GetMultipliers(
                                em.GetComponentData<FactionTag>(entity).Value);
                            hasSectMults = true;
                            finalDamage = (int)(finalDamage * sectMults.MeleeDamage);
                            if (em.HasComponent<CrystalTag>(tgt.Value))
                                finalDamage = (int)(finalDamage * sectMults.DamageVsCrystal);
                            finalDamage = math.max(1, finalDamage);
                        }

                        // Condemned mark: target takes bonus damage
                        if (em.HasComponent<Condemned>(tgt.Value))
                        {
                            var condemned = em.GetComponentData<Condemned>(tgt.Value);
                            finalDamage = (int)(finalDamage * condemned.DamageMultiplier);
                        }

                        // IgniteBuff: attacker's next attacks deal bonus fire damage
                        if (em.HasComponent<IgniteBuff>(entity))
                        {
                            var ignite = em.GetComponentData<IgniteBuff>(entity);
                            if (ignite.AttacksRemaining > 0)
                            {
                                finalDamage += (int)ignite.BonusDamage;
                                ignite.AttacksRemaining--;
                                if (ignite.AttacksRemaining <= 0)
                                    ecb.RemoveComponent<IgniteBuff>(entity);
                                else
                                    em.SetComponentData(entity, ignite);
                            }
                        }

                        // VoidStrikeBuff: attacker's next attack deals bonus damage
                        if (em.HasComponent<VoidStrikeBuff>(entity))
                        {
                            var voidStrike = em.GetComponentData<VoidStrikeBuff>(entity);
                            float bonus = em.HasComponent<CrystalTag>(tgt.Value) ? voidStrike.BonusVsCrystal : voidStrike.BonusDamage;
                            finalDamage += (int)bonus;
                            ecb.RemoveComponent<VoidStrikeBuff>(entity);
                        }

                        // DamageReflect: target reflects damage back to attacker
                        if (em.HasComponent<SpellBuff>(tgt.Value))
                        {
                            var tgtBuff = em.GetComponentData<SpellBuff>(tgt.Value);
                            if (tgtBuff.DamageReflect > 0f)
                            {
                                int reflected = math.max(1, (int)(finalDamage * tgtBuff.DamageReflect));
                                var attackerHealth = em.GetComponentData<Health>(entity);
                                attackerHealth.Value -= reflected;
                                em.SetComponentData(entity, attackerHealth);
                            }
                        }

                        finalDamage = math.max(1, finalDamage);

                        // Apply damage — use immediate write so multiple attackers
                        // in the same frame correctly stack damage (not last-write-wins via ECB)
                        var health = em.GetComponentData<Health>(tgt.Value);
                        health.Value -= finalDamage;
                        if (health.Value < 0) health.Value = 0;
                        em.SetComponentData(tgt.Value, health);

                        // Track last damager faction for kill credit (used by PillageSystem, CaravanDeathSystem)
                        if (em.HasComponent<FactionTag>(entity))
                        {
                            var lastDamaged = new LastDamagedByFaction
                            {
                                Value = em.GetComponentData<FactionTag>(entity).Value
                            };
                            if (targetHasLastDamaged)
                                em.SetComponentData(tgt.Value, lastDamaged);
                            else
                                ecb.AddComponent(tgt.Value, lastDamaged);
                        }

                        // Track attacker entity for defensive stance return-fire
                        if (targetHasLastAttacker)
                            em.SetComponentData(tgt.Value, new LastAttackerEntity { Value = entity });
                        else
                            ecb.AddComponent(tgt.Value, new LastAttackerEntity { Value = entity });

                        // Sect panic chance: apply SpellDebuff (speed reduction) on hit
                        if (hasSectMults && sectMults.PanicChance > 0f)
                        {
                            int hash = entity.Index ^ (tgt.Value.Index * 397);
                            if ((math.abs(hash) % 100) < (int)(sectMults.PanicChance * 100f))
                            {
                                if (!em.HasComponent<SpellDebuff>(tgt.Value))
                                    ecb.AddComponent(tgt.Value, new SpellDebuff { SpeedReduction = 0.5f, TimeRemaining = 2f });
                                else
                                    ecb.SetComponent(tgt.Value, new SpellDebuff { SpeedReduction = 0.5f, TimeRemaining = 2f });
                            }
                        }

                        // Sect control chance: apply full root SpellDebuff on hit
                        if (hasSectMults && sectMults.ControlChance > 0f)
                        {
                            int hash = entity.Index ^ (tgt.Value.Index * 631);
                            if ((math.abs(hash) % 100) < (int)(sectMults.ControlChance * 100f))
                            {
                                if (!em.HasComponent<SpellDebuff>(tgt.Value))
                                    ecb.AddComponent(tgt.Value, new SpellDebuff { SpeedReduction = 1.0f, TimeRemaining = 1f });
                                else
                                    ecb.SetComponent(tgt.Value, new SpellDebuff { SpeedReduction = 1.0f, TimeRemaining = 1f });
                            }
                        }

                        // Reset cooldown (with sect attack speed bonus)
                        float cooldownVal = cd.Cooldown;
                        if (hasSectMults && sectMults.AttackSpeed > 1f)
                            cooldownVal /= sectMults.AttackSpeed;
                        cd.Timer = cooldownVal;
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

                    // Chase target — battalion members pathfind toward their target
                    // just like non-battalion units. BattalionSyncSystem releases them
                    // from formation when they have a combat target.
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