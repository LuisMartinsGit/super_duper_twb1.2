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
            var elapsed = SystemAPI.Time.ElapsedTime; // for BuildingDamageState stamps
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

                // Fix #212: defensively check HasComponent<Health> before reading.
                // If the target lost its Health component (e.g. DeathSystem removed
                // it via ECB playback ordering), GetComponentData would throw.
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

                // Fix #211: skip targets that are currently Invulnerable (set by
                // SpellBuffSystem). Without this guard, protected units still
                // took full damage, making the buff a no-op.
                if (em.HasComponent<Invulnerable>(tgt.Value)) continue;

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

                        // SpellBuff armor bonus on target (Aegis-style timed buff,
                        // StoneheartBastion +3 aura, etc.). Adds to defense BEFORE
                        // the matrix calc so it actually reduces incoming damage.
                        // Was previously written but never read. (task-062 C-1)
                        defenseValue += CombatDamageHelper.GetSpellBuffArmorBonus(em, tgt.Value);

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

                        // task-063 phase 1: sect melee/AS/crystal/panic/control multipliers
                        // gone with the old multiplier bridge. Phase 2 reintroduces these
                        // per-sect, per-lever — for now use baseline (1.0× damage / no debuffs).

                        // Fix #226: on-hit bonus damage (Condemned/Ignite/VoidStrike) routed through shared helper
                        finalDamage = CombatDamageHelper.ApplyBonusDamageOnHit(em, ecb, entity, tgt.Value, finalDamage);

                        // Fix #226: DamageReflect routed through shared helper
                        CombatDamageHelper.ApplyDamageReflect(em, entity, tgt.Value, finalDamage);

                        finalDamage = math.max(1, finalDamage);

                        // Apply damage — use immediate write so multiple attackers
                        // in the same frame correctly stack damage (not last-write-wins via ECB)
                        var health = em.GetComponentData<Health>(tgt.Value);
                        health.Value -= finalDamage;
                        if (health.Value < 0) health.Value = 0;
                        em.SetComponentData(tgt.Value, health);

                        // Fix #226: last-damager tracking routed through shared helper
                        CombatDamageHelper.TrackLastDamager(em, ecb, entity, tgt.Value, elapsed);

                        // Reset cooldown (sect attack-speed multiplier removed in Phase 1).
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

                    // Battalion members: BattalionSyncSystem moves them toward their
                    // target directly (MovementSystem excludes BattalionMemberData).
                    // Keep target so they attack once BattalionSyncSystem positions them.
                    if (em.HasComponent<BattalionMemberData>(entity))
                    {
                        continue;
                    }

                    // Non-battalion units: chase via DesiredDestination
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