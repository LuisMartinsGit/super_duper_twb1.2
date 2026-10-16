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
    /// Handles melee combat processing for non-ranged units.
    ///
    /// Features:
    /// - Damage-type vs armor-type modifier matrix (via CombatModifiers)
    /// - Per-damage-type defense with diminishing returns
    /// - Height-based damage modifiers (±20% cap)
    /// - Flanking: a hit outside the defender's front arc is multiplied
    ///   (docs/Design/Combat_Pacing.md § Flanking; MeleeCombatSystem.asset)
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
        /// <summary>Melee reach to the target's surface. Internal: the
        /// Defensive / Hold stances acquire by it (TargetingSystem.AttackReach).</summary>
        internal const float MeleeRange = 1.5f;

        // Height damage modifier settings
        private const float HeightDamageScale = 0.04f; // 4% per unit height diff
        private const float MaxHeightBonus = 0.20f;    // Cap at +20%
        private const float MaxHeightPenalty = -0.20f; // Cap at -20%

        /// <summary>The per-unit reads, through lookups (2026-10-15 perf
        /// pass — see CombatLookups). Created once, refreshed every update.</summary>
        private CombatLookups _lk;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
            _lk = CombatLookups.Create(ref state);
        }

        /// <summary>
        /// Drop the AttackCommand if it is live — TransientState.Active +
        /// TransientState.Clear, through the lookup. Active implies present,
        /// so Clear's own presence guard is already satisfied.
        /// </summary>
        private static void DropAttackCommand(in CombatLookups lk, EntityCommandBuffer ecb, Entity e)
        {
            if (CombatLookups.Active(in lk.AttackCommand, e))
                ecb.SetComponentEnabled<AttackCommand>(e, false);
        }

        public void OnUpdate(ref SystemState state)
        {
            // Instrumented (2026-08-16 perf sweep): main-thread managed loop
            // over every melee unit, per frame, not Bursted.
            double perfT0 = UnityEngine.Time.realtimeSinceStartupAsDouble;

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
            var dt = SystemAPI.Time.DeltaTime;
            var elapsed = SimCadence.MatchTimeOr(SystemAPI.Time.ElapsedTime); // for BuildingDamageState stamps
            var em = state.EntityManager;
            _lk.Update(ref state);
            var lk = _lk;

            // Flanking (docs/Design/Combat_Pacing.md § Flanking). Read once per
            // update; the arc test below compares a dot product against the
            // cosine of the half-angle, so no per-hit trig. A missing config
            // has already been reported loudly by ComponentConfig.Require —
            // flanking is then off rather than every melee swing throwing.
            var meleeCfg = MeleeCombatSystemConfig.I;
            float flankMult = meleeCfg != null ? meleeCfg.flankDamageMultiplier : 1f;
            float frontArcCos = meleeCfg != null
                ? math.cos(math.radians(meleeCfg.frontArcHalfAngleDegrees))
                : -2f; // below any dot product: everything is "front"

            // The melee loop, Burst (MeleeCombatSystem.Jobs.cs): a parallel plan
            // per unit, then an in-order apply that hands each due swing back
            // here. Same entity set and order as the old foreach.
            var query = SystemAPI.QueryBuilder()
                .WithAll<LocalTransform, UnitTag, Damage>()
                .WithAllRW<Target, AttackCooldown>()
                .WithNone<ArcherTag>()             // Exclude ranged units
                // A CORPSE DOES NOT SWING. DeathAnimationState is added the
                // moment DeathSystem registers the death and the entity then
                // survives for the length of its death animation. The frame a
                // unit dies it is still in this query; Health (read live at
                // apply) closes that window.
                .WithNone<DeathAnimationState>()
                .WithNone<TheWaningBorder.Entities.Launched>()   // airborne units swing at nothing
                .Build();

            using var units = query.ToEntityArray(Unity.Collections.Allocator.TempJob);
            int n = units.Length;
            using var plans = new Unity.Collections.NativeArray<MeleePlan>(n,
                Unity.Collections.Allocator.TempJob, Unity.Collections.NativeArrayOptions.UninitializedMemory);

            var planJob = new MeleePlanJob
            {
                Units = units,
                Lk = lk,
                Targets = SystemAPI.GetComponentLookup<Target>(true),
                Cooldowns = SystemAPI.GetComponentLookup<AttackCooldown>(true),
                Damages = SystemAPI.GetComponentLookup<Damage>(true),
                Storage = SystemAPI.GetEntityStorageInfoLookup(),
                Dt = dt,
                Plans = plans,
            };
            var planHandle = planJob.Schedule(n, 32, state.Dependency);
            planHandle.Complete();

            ResolveChasePoints(plans);

            using var stop = new Unity.Collections.NativeReference<int>(0, Unity.Collections.Allocator.TempJob);
            var apply = new MeleeApplyJob
            {
                Units = units,
                Plans = plans,
                Health = SystemAPI.GetComponentLookup<Health>(true),
                Targets = SystemAPI.GetComponentLookup<Target>(false),
                Cooldowns = SystemAPI.GetComponentLookup<AttackCooldown>(false),
                Transforms = SystemAPI.GetComponentLookup<LocalTransform>(false),
                Ecb = ecb,
                Dt = dt,
                Stop = stop,
            };
            int next = 0;
            while (next < n)
            {
                apply.Start = next;
                apply.Run();
                int k = stop.Value;
                if (k >= n) break;
                Swing(em, ecb, in lk, units[k], plans[k].Target, elapsed, flankMult, frontArcCos);
                next = k + 1;
            }

            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("MeleeCombat",
                (UnityEngine.Time.realtimeSinceStartupAsDouble - perfT0) * 1000.0);
        }

        /// <summary>
        /// One melee hit, unchanged from the old loop body: the damage
        /// pipeline and its riders, then the cooldown reset. Managed (sect,
        /// ability and Feraldis hooks); called by OnUpdate between apply runs,
        /// in query order, after this unit's stop-and-face.
        /// </summary>
        private static void Swing(EntityManager em, EntityCommandBuffer ecb, in CombatLookups lk,
            Entity entity, Entity target, double elapsed, float flankMult, float frontArcCos)
        {
            var tgt = new Target { Value = target };
            var myPos = lk.Transform[entity].Position;
            var targetXf = lk.Transform[tgt.Value];
            var targetPos = targetXf.Position;

            int baseDamage = em.GetComponentData<Damage>(entity).Value;

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
            if (TransientState.Active<Fortified>(em, tgt.Value))
            {
                var fort = em.GetComponentData<Fortified>(tgt.Value);
                defenseValue += (int)fort.ArmorBonus;
            }

            // SpellBuff armor bonus on target (Aegis-style timed buff,
            // StoneheartBastion +3 aura, etc.). Adds to defense BEFORE
            // the matrix calc so it actually reduces incoming damage.
            // Was previously written but never read. (task-062 C-1)
            defenseValue += CombatDamageHelper.GetSpellBuffArmorBonus(em, tgt.Value, CombatDamageHelper.BaseDefense(em, tgt.Value, dmgType));

            bool targetHasDebuff = em.HasComponent<BorderDebuff>(tgt.Value);
            BorderDebuff targetDebuff = targetHasDebuff
                ? em.GetComponentData<BorderDebuff>(tgt.Value)
                : default;

            bool targetHasLastDamaged = em.HasComponent<LastDamagedByFaction>(tgt.Value);
            bool targetHasLastAttacker = em.HasComponent<LastAttackerEntity>(tgt.Value);
            // (presence only feeds the add-vs-set below; the enable
            // bit is raised by the helper either way)

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

            // Flanking: same multiplier layer as height and
            // veilstone — on the post-armour total, tag bonus
            // included. Units only; a building has no facing.
            bool flank = em.HasComponent<UnitTag>(tgt.Value)
                && IsFlankHit(targetXf, myPos, frontArcCos);
            if (flank) borderMod *= flankMult;

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
            finalDamage = TheWaningBorder.Abilities.AbilityDamageHooks.ScaleIncoming(em, tgt.Value, finalDamage, entity);

            // Apply damage — use immediate write so multiple attackers
            // in the same frame correctly stack damage (not last-write-wins via ECB)
            // Shield points are hit points: the shield pays first.
            var health = em.GetComponentData<Health>(tgt.Value);
            health.Value -= ShieldDamage.Absorb(em, tgt.Value, finalDamage);
            if (health.Value < 0) health.Value = 0;
            // (Life Cling HP-floor is applied centrally in DeathSystem,
            // source-agnostic, right before the death check.)
            em.SetComponentData(tgt.Value, health);

            // Fix #226: last-damager tracking routed through shared helper
            CombatDamageHelper.TrackLastDamager(em, ecb, entity, tgt.Value, elapsed);

            // The Shardbound King's blows cleave: every hostile
            // within CleaveRadius of the target takes a share of
            // the same hit (Curse_And_Shardroot.md 3.1).
            if (em.HasComponent<TheWaningBorder.Entities.ShardboundKing>(entity))
                Cleave(em, entity, tgt.Value, finalDamage);

            // Match-long damage ledger — what Wrath's Spite pools
            // and pays back (docs/Design/Sects.md).
            CombatDamageHelper.RecordDamageDealt(em, ecb, entity, finalDamage);

            // Diagnostics only (no-op unless metrics are on):
            // melee / flank hit counts per attacking faction.
            if (TheWaningBorder.Core.Diagnostics.MatchMetrics.Enabled
                && em.HasComponent<FactionTag>(entity))
                TheWaningBorder.Core.Diagnostics.MatchMetrics.RecordMeleeHit(
                    em.GetComponentData<FactionTag>(entity).Value, flank);

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
            // Hamstring (Feraldis War Hall tech): melee hits slow.
            TheWaningBorder.Feraldis.FeraldisCombat.OnMeleeHit(em, ecb, entity, tgt.Value);

            // Reset cooldown. Glow Ability (Lv 5 active window)
            // shortens the cooldown by 30% per the design spec.
            // (audit follow-up — was deferred in PR #258.)
            float cdMult = 1f;
            if (em.HasComponent<ShardrootAbilityState>(entity)
                && em.GetComponentData<ShardrootAbilityState>(entity).ActiveRemaining > 0f)
                cdMult = 1f / 1.30f;
            // Feraldis blood frenzy also swings faster.
            cdMult *= CombatDamageHelper.GetFrenzyCooldownMult(em, entity);
            // Timed haste (Blood Rain and any future SpellBuff
            // attack-speed effect) swings faster too.
            cdMult *= CombatDamageHelper.GetHasteCooldownMult(em, entity);
            var cd = em.GetComponentData<AttackCooldown>(entity);
            cd.Timer = cd.Cooldown * cdMult;
            em.SetComponentData(entity, cd);
        }

        /// <summary>
        /// The point this chaser is walking to, latched until the target moves.
        ///
        /// Recomputed only when the anchor is missing, names a different target,
        /// the target has drifted past <see cref="ChaseAnchor.RecomputeDistance"/>,
        /// or the latched point is no longer standable (a building went up on
        /// it, a wall closed). Otherwise the previous point is returned
        /// unchanged, which is the whole point: a stable goal is what lets the
        /// flow field route the unit AROUND an obstacle instead of grinding
        /// into it.
        ///
        /// A fresh anchor that lands on impassable ground is pushed outward
        /// along the approach direction until it clears — the destination must
        /// be somewhere the unit can actually stand, or the field cannot reach
        /// it and line-of-sight steering is defeated too.
        /// </summary>
        static readonly ComponentType[] QT_CleaveVictims =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_CleaveVictims;

        /// <summary>Shardbound cleave: CleaveFraction of the hit to every
        /// hostile unit within CleaveRadius of the struck target (the target
        /// itself already took the full blow).</summary>
        private static void Cleave(EntityManager em, Entity attacker, Entity struck, int damage)
        {
            if (!em.HasComponent<LocalTransform>(struck) || !em.HasComponent<FactionTag>(attacker)) return;
            int share = (int)math.round(damage * TheWaningBorder.Entities.ShardboundFury.CleaveFraction);
            if (share <= 0) return;
            var centre = em.GetComponentData<LocalTransform>(struck).Position;
            var mine = em.GetComponentData<FactionTag>(attacker).Value;
            float r2 = TheWaningBorder.Entities.ShardboundFury.CleaveRadius
                     * TheWaningBorder.Entities.ShardboundFury.CleaveRadius;
            var q = QC_CleaveVictims.Get(em, QT_CleaveVictims);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (ents[i] == struck || ents[i] == attacker) continue;
                if (hps[i].Value <= 0) continue;
                if (!Alliances.AreHostile(mine, facs[i].Value)) continue;
                var p = xfs[i].Position;
                float dx = p.x - centre.x, dz = p.z - centre.z;
                if (dx * dx + dz * dz > r2) continue;
                var h = hps[i];
                h.Value = math.max(0, h.Value - ShieldDamage.Absorb(em, ents[i], TheWaningBorder.Abilities.AbilityDamageHooks.ScaleIncoming(em, ents[i], share)));
                em.SetComponentData(ents[i], h);
            }
        }

        private static float3 ResolveChaseAnchor(in CombatLookups lk, EntityCommandBuffer ecb,
            Entity self, Entity target, float3 targetPos, float3 myPos,
            in TargetExtent extent)
        {
            bool hasAnchor = CombatLookups.Active(in lk.ChaseAnchor, self);
            if (hasAnchor)
            {
                var a = lk.ChaseAnchor[self];
                bool sameTarget = a.Target == target;
                float dx = a.TargetPos.x - targetPos.x;
                float dz = a.TargetPos.z - targetPos.z;
                bool targetHeld = dx * dx + dz * dz
                    <= ChaseAnchor.RecomputeDistance * ChaseAnchor.RecomputeDistance;

                if (sameTarget && targetHeld
                    && TheWaningBorder.Systems.Navigation.NavGridQuery.IsWorldStandable(a.Point))
                    return a.Point;
            }

            float3 point = extent.ApproachPoint(myPos, MeleeRange * 0.5f);
            point.y = targetPos.y;

            // Walk the point outward until it stands on legal ground. Bounded:
            // past a couple of body-widths the target is unreachable anyway and
            // the stuck/redirect systems take over.
            if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsWorldStandable(point))
            {
                float3 outward = point - targetPos;
                outward.y = 0f;
                float len = math.length(outward);
                if (len > 1e-4f)
                {
                    outward /= len;
                    for (float extra = 0.75f; extra <= 3f; extra += 0.75f)
                    {
                        float3 probe = point + outward * extra;
                        if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsWorldStandable(probe))
                            continue;
                        point = probe;
                        break;
                    }
                }
            }

            var anchor = new ChaseAnchor { Target = target, TargetPos = targetPos, Point = point };
            TransientState.Set(ecb, self, anchor);
            return point;
        }

        /// <summary>
        /// True when <paramref name="attackerPos"/> stands outside the
        /// defender's front arc: the angle between the defender's facing
        /// (yaw forward, XZ) and the defender-to-attacker line exceeds the
        /// half-angle whose cosine is <paramref name="frontArcCos"/>. The
        /// boundary counts as front; a degenerate (coincident) position is
        /// front. Side and rear are one arc. docs/Design/Combat_Pacing.md.
        /// </summary>
        internal static bool IsFlankHit(in LocalTransform defender, float3 attackerPos, float frontArcCos)
        {
            float3 fwd = math.mul(defender.Rotation, new float3(0f, 0f, 1f));
            float fx = fwd.x, fz = fwd.z;
            float flen2 = fx * fx + fz * fz;
            float tx = attackerPos.x - defender.Position.x;
            float tz = attackerPos.z - defender.Position.z;
            float tlen2 = tx * tx + tz * tz;
            if (flen2 < 1e-8f || tlen2 < 1e-8f) return false;
            float cos = (fx * tx + fz * tz) / math.sqrt(flen2 * tlen2);
            return cos < frontArcCos;
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