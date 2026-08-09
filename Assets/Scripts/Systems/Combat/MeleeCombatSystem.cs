// File: Assets/Scripts/Systems/Combat/MeleeCombatSystem.cs
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
    /// Handles melee combat processing for non-ranged units.
    ///
    /// Features:
    /// - Damage-type vs armor-type modifier matrix (via CombatModifiers)
    /// - Per-damage-type defense with diminishing returns
    /// - Height-based damage modifiers (±20% cap)
    /// - Veilstone buff/debuff integration
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

                // Update cooldown timer. Frozen by Recall the Codex
                // (Antiquity): cooldowns do not recover while CodexFrozen.
                if (cd.Timer > 0 && !em.HasComponent<CodexFrozen>(entity))
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

                // Buildings-only siege (Battering Ram): refuse to swing at
                // anything that is not a building — even when force-ordered.
                // Drop the target so the next targeting pass (which filters
                // the same way) finds a wall or leaves the ram idle.
                // DesiredDestination is deliberately untouched — the
                // chase-arbitration contract stays intact.
                if (em.HasComponent<BuildingsOnlyAttacker>(entity)
                    && !em.HasComponent<BuildingTag>(tgt.Value))
                {
                    tgt.Value = Entity.Null;
                    if (em.HasComponent<AttackCommand>(entity))
                    {
                        ecb.RemoveComponent<AttackCommand>(entity);
                    }
                    continue;
                }

                // Full Gallop: cavalry mid-sprint cannot swing. They keep chasing —
                // only the attack is suppressed for the burst's duration.
                if (em.HasComponent<TheWaningBorder.Abilities.TempDisarm>(entity)) continue;

                // Fix #211: skip targets that are currently Invulnerable (set by
                // SpellBuffSystem). Without this guard, protected units still
                // took full damage, making the buff a no-op.
                if (em.HasComponent<Invulnerable>(tgt.Value)) continue;

                var myPos = transform.ValueRO.Position;
                var targetPos = em.GetComponentData<LocalTransform>(tgt.Value).Position;

                // SURFACE distance — how far the attacker is from the target's
                // BODY, not its pivot. See TargetGeometry: box footprint for
                // buildings, circle for everything else. Shared with the
                // targeting, ranged and arrival paths so a building is the same
                // size to every system that looks at it.
                var extent = TargetGeometry.Extent(em, tgt.Value);
                float surfaceDist = extent.SurfaceDistXZ(myPos);

                // Vertically separated (bridge deck vs underpass): melee
                // cannot reach across surfaces. Drop the target so the next
                // targeting pass picks someone reachable — standing under an
                // enemy hacking at the deck is neither.
                if (math.abs(myPos.y - targetPos.y) > TargetingSystem.MeleeMaxHeightDelta
                    && surfaceDist <= MeleeRange)
                {
                    tgt.Value = Entity.Null;
                    if (em.HasComponent<AttackCommand>(entity))
                        ecb.RemoveComponent<AttackCommand>(entity);
                    continue;
                }

                // In melee range - attack
                if (surfaceDist <= MeleeRange)
                {
                    // Plant and turn to face the target. Stopping alone left the
                    // unit swinging at whatever heading it arrived on — usually
                    // tangential, because the approach ends in a sidestep.
                    TargetGeometry.StopAndFace(ecb, em, entity, targetPos, dt);

                    // Attack if cooldown is ready
                    if (cd.Timer <= 0)
                    {
                        int baseDamage = damage.ValueRO.Value;

                        // --- Batch-read attacker components once ---
                        DamageType dmgType = DamageType.Melee;
                        bool attackerHasDmgType = em.HasComponent<DamageTypeData>(entity);
                        if (attackerHasDmgType)
                            dmgType = em.GetComponentData<DamageTypeData>(entity).Value;

                        bool attackerHasBuff = em.HasComponent<BorderBuff>(entity);
                        BorderBuff attackerBuff = attackerHasBuff
                            ? em.GetComponentData<BorderBuff>(entity)
                            : default;

                        // --- Batch-read target components once ---
                        bool targetHasArmor = em.HasComponent<ArmorTypeData>(tgt.Value);
                        // Buildings without explicit armor read as Structure —
                        // the InfantryLight fallback made archers (1.1x) beat
                        // catapults (0.6x) against them, inverting the siege
                        // matchup (fix 2026-08-03).
                        ArmorType armorType = targetHasArmor
                            ? em.GetComponentData<ArmorTypeData>(tgt.Value).Value
                            : em.HasComponent<BuildingTag>(tgt.Value)
                                ? ArmorType.Structure
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

                        bool targetHasDebuff = em.HasComponent<BorderDebuff>(tgt.Value);
                        BorderDebuff targetDebuff = targetHasDebuff
                            ? em.GetComponentData<BorderDebuff>(tgt.Value)
                            : default;

                        bool targetHasLastDamaged = em.HasComponent<LastDamagedByFaction>(tgt.Value);
                        bool targetHasLastAttacker = em.HasComponent<LastAttackerEntity>(tgt.Value);

                        // Calculate height-based damage modifier
                        float heightMod = CalculateHeightDamageModifier(myPos.y, targetPos.y);

                        // Veilstone modifier (uses pre-fetched data)
                        float borderMod = 1.0f;
                        if (attackerHasBuff)
                            borderMod *= 1f + attackerBuff.AttBonus;
                        if (targetHasDebuff)
                            borderMod *= 1f + targetDebuff.AttPenalty;

                        // Feraldis: blood frenzy + Berserker last stand.
                        borderMod *= CombatDamageHelper.GetFrenzyDamageMult(em, entity);

                        // Tag bonus (AoE4-style): attacker's BonusVsTags vs the
                        // target's tags — flat, armor-ignoring, from the unit SO.
                        int tagBonus = TagBonus.Compute(em, entity, tgt.Value);

                        int finalDamage = CombatModifiers.CalculateFinalDamage(
                            baseDamage, dmgType, armorType, defenseValue, heightMod, borderMod, tagBonus);

                        // task-063 phase 1: sect melee/AS/veilstone/panic/control multipliers
                        // gone with the old multiplier bridge. Phase 2 reintroduces these
                        // per-sect, per-lever — for now use baseline (1.0× damage / no debuffs).

                        // Fix #226: on-hit bonus damage (Condemned/Ignite/VoidStrike) routed through shared helper
                        finalDamage = CombatDamageHelper.ApplyBonusDamageOnHit(em, ecb, entity, tgt.Value, finalDamage);

                        // Fix #226: DamageReflect routed through shared helper
                        CombatDamageHelper.ApplyDamageReflect(em, entity, tgt.Value, finalDamage);

                        finalDamage = math.max(1, finalDamage);

                        // Ability: scale total incoming damage by the target's
                        // damage-taken multiplier (Liquid Courage 90% DR) before HP.
                        finalDamage = TheWaningBorder.Abilities.AbilityDamageHooks.ScaleIncoming(em, tgt.Value, finalDamage);

                        // Apply damage — use immediate write so multiple attackers
                        // in the same frame correctly stack damage (not last-write-wins via ECB)
                        var health = em.GetComponentData<Health>(tgt.Value);
                        health.Value -= finalDamage;
                        if (health.Value < 0) health.Value = 0;
                        // (Life Cling HP-floor is applied centrally in DeathSystem,
                        // source-agnostic, right before the death check.)
                        em.SetComponentData(tgt.Value, health);

                        // Fix #226: last-damager tracking routed through shared helper
                        CombatDamageHelper.TrackLastDamager(em, ecb, entity, tgt.Value, elapsed);

                        // Feraldis on-hit riders. Both no-op for units without
                        // the declaring component, so every other unit in the
                        // game pays two HasComponent checks.
                        if (em.HasComponent<WhirlAttack>(entity)
                            || em.HasComponent<InflictsBleed>(entity)
                            || em.HasComponent<InflictsBuildingBurn>(entity))
                        {
                            var atkFaction = em.HasComponent<FactionTag>(entity)
                                ? em.GetComponentData<FactionTag>(entity).Value
                                : Faction.Blue;

                            // Bloodletter: widen the swing into an area strike.
                            FeraldisWhirl.Strike(em, ecb, entity, tgt.Value,
                                targetPos, atkFaction, finalDamage);
                            // Axe Thrower / future melee bleeders.
                            FeraldisBleed.ApplyFrom(em, ecb, entity, tgt.Value, atkFaction);
                            // Raider: leaves enemy BUILDINGS burning.
                            FeraldisBuildingBurn.ApplyFrom(em, ecb, entity, tgt.Value, atkFaction);
                        }

                        // Reset cooldown. Glow Ability (Lv 5 active window)
                        // shortens the cooldown by 30% per the design spec.
                        // (audit follow-up — was deferred in PR #258.)
                        float cdMult = 1f;
                        if (em.HasComponent<GlowAbilityState>(entity)
                            && em.GetComponentData<GlowAbilityState>(entity).ActiveRemaining > 0f)
                            cdMult = 1f / 1.30f;
                        // Feraldis blood frenzy also swings faster.
                        cdMult *= CombatDamageHelper.GetFrenzyCooldownMult(em, entity);
                        cd.Timer = cd.Cooldown * cdMult;
                    }
                }
                else
                {
                    // Out of range - hold position units do NOT chase
                    // A channelling ritualist holds its ground exactly like a
                    // unit on Hold Position. TargetingSystem no longer hands
                    // one a target, but a target acquired BEFORE the channel
                    // began would survive into it, and the chase below rewrites
                    // DesiredDestination every frame — the same way the
                    // return-to-guard branch did before it was gated, which
                    // broke every measured channel on 2026-08-07.
                    if (em.HasComponent<HoldPositionTag>(entity)
                        || em.HasComponent<RitualState>(entity))
                    {
                        // Clear target so unit stays put
                        tgt.Value = Entity.Null;
                        if (em.HasComponent<AttackCommand>(entity))
                            ecb.RemoveComponent<AttackCommand>(entity);
                        continue;
                    }

                    // Chase via DesiredDestination — ALWAYS aim at the target's
                    // EDGE, never its center. The old code edge-aimed only when
                    // targetRadius > MeleeRange (1.5), so any 3x3 building
                    // (legacy radius exactly 1.5) was chased dead-center: a
                    // destination INSIDE the impassable footprint. The stuck
                    // escalation cancelled it, this system re-issued it next
                    // frame — the bump-and-jiggle at the wall. Edge-aiming is
                    // also strictly better against unit targets (no body
                    // overlap push on arrival).
                    // Closest point on the target's surface, pulled back half a
                    // melee step so the destination is walkable ground squarely
                    // inside attack range — never a cell inside the footprint.
                    float3 chaseTarget = extent.ApproachPoint(myPos, MeleeRange * 0.5f);
                    chaseTarget.y = targetPos.y;

                    // No facing write here on purpose: while the unit is moving,
                    // UnitIntegratorSystem owns rotation (face where you walk).
                    // Writing here too would have the two systems fight over
                    // LocalTransform.Rotation every frame.

                    if (!em.HasComponent<DesiredDestination>(entity))
                    {
                        ecb.AddComponent(entity, new DesiredDestination
                        {
                            Position = chaseTarget,
                            Has = 1
                        });
                    }
                    else
                    {
                        ecb.SetComponent(entity, new DesiredDestination
                        {
                            Position = chaseTarget,
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

    }
}