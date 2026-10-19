// CombatDamageHelper.cs
// Shared on-hit pipeline used by MeleeCombatSystem and RangedCombatSystem.

using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Sect;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// The read-only component lookups the per-unit combat loops share
    /// (MeleeCombatSystem, RangedCombatSystem) — 2026-10-15 perf pass.
    ///
    /// Why: EntityManager.HasComponent / GetComponentData find the component
    /// by walking the entity archetype's type list, ~100-120 types on a
    /// late-game unit, and the melee loop made ~30 such calls per unit per
    /// tick before it even knew whether it would swing. A ComponentLookup
    /// caches the last archetype it saw and answers a repeat in O(1).
    ///
    /// Reads only, and a lookup reads the live component memory, so a value
    /// the loop wrote a moment earlier through the EntityManager (a hit's
    /// Health) is what the next read sees — the answers are exactly the
    /// EntityManager's. Writes and structural changes stay on the
    /// EntityManager / the ECB as before.
    ///
    /// Owned by the system: <see cref="Create"/> in OnCreate,
    /// <see cref="Update"/> at the top of every OnUpdate.
    /// </summary>
    public struct CombatLookups
    {
        [Unity.Collections.ReadOnly] public ComponentLookup<Health> Health;
        [Unity.Collections.ReadOnly] public ComponentLookup<FactionTag> Faction;
        [Unity.Collections.ReadOnly] public ComponentLookup<Unity.Transforms.LocalTransform> Transform;
        [Unity.Collections.ReadOnly] public ComponentLookup<BuildingSize> Size;
        [Unity.Collections.ReadOnly] public ComponentLookup<Radius> Radius;
        [Unity.Collections.ReadOnly] public ComponentLookup<BuildingTag> Building;
        [Unity.Collections.ReadOnly] public ComponentLookup<WallTag> Wall;
        [Unity.Collections.ReadOnly] public ComponentLookup<FlyingTag> Flying;
        [Unity.Collections.ReadOnly] public ComponentLookup<ArcherTag> Archer;
        [Unity.Collections.ReadOnly] public ComponentLookup<SiegeTag> Siege;
        [Unity.Collections.ReadOnly] public ComponentLookup<BallistaShooterTag> BallistaShooter;
        [Unity.Collections.ReadOnly] public ComponentLookup<BuildingsOnlyAttacker> BuildingsOnly;
        [Unity.Collections.ReadOnly] public ComponentLookup<DamageTypeData> DamageType;
        [Unity.Collections.ReadOnly] public ComponentLookup<CodexFrozen> CodexFrozen;
        [Unity.Collections.ReadOnly] public ComponentLookup<FiresOnMove> FiresOnMove;
        [Unity.Collections.ReadOnly] public ComponentLookup<NavLayerIndex> NavLayer;
        [Unity.Collections.ReadOnly] public ComponentLookup<DesiredDestination> Destination;
        [Unity.Collections.ReadOnly] public ComponentLookup<EmplacedEngineTag> Emplaced;
        [Unity.Collections.ReadOnly] public ComponentLookup<StationaryAutoFire> StationaryFire;
        [Unity.Collections.ReadOnly] public ComponentLookup<RitualState> Ritual;
        [Unity.Collections.ReadOnly] public ComponentLookup<UnitEngagement> Engagement;
        [Unity.Collections.ReadOnly] public ComponentLookup<ChaseAnchor> ChaseAnchor;
        [Unity.Collections.ReadOnly] public ComponentLookup<Invulnerable> Invulnerable;
        [Unity.Collections.ReadOnly] public ComponentLookup<TheWaningBorder.Abilities.TempDisarm> TempDisarm;
        [Unity.Collections.ReadOnly] public ComponentLookup<TheWaningBorder.Core.Commands.Types.AttackCommand> AttackCommand;
        /// <summary>Friendly Fire (Sects.md, 2026-10-18): a frenzied unit
        /// hurts and is hurt by everyone, its own side included.</summary>
        [Unity.Collections.ReadOnly] public ComponentLookup<SectDisordered> Disordered;

        public static CombatLookups Create(ref SystemState state) => new CombatLookups
        {
            Health = state.GetComponentLookup<Health>(true),
            Faction = state.GetComponentLookup<FactionTag>(true),
            Transform = state.GetComponentLookup<Unity.Transforms.LocalTransform>(true),
            Size = state.GetComponentLookup<BuildingSize>(true),
            Radius = state.GetComponentLookup<Radius>(true),
            Building = state.GetComponentLookup<BuildingTag>(true),
            Wall = state.GetComponentLookup<WallTag>(true),
            Flying = state.GetComponentLookup<FlyingTag>(true),
            Archer = state.GetComponentLookup<ArcherTag>(true),
            Siege = state.GetComponentLookup<SiegeTag>(true),
            BallistaShooter = state.GetComponentLookup<BallistaShooterTag>(true),
            BuildingsOnly = state.GetComponentLookup<BuildingsOnlyAttacker>(true),
            DamageType = state.GetComponentLookup<DamageTypeData>(true),
            CodexFrozen = state.GetComponentLookup<CodexFrozen>(true),
            FiresOnMove = state.GetComponentLookup<FiresOnMove>(true),
            NavLayer = state.GetComponentLookup<NavLayerIndex>(true),
            Destination = state.GetComponentLookup<DesiredDestination>(true),
            Emplaced = state.GetComponentLookup<EmplacedEngineTag>(true),
            StationaryFire = state.GetComponentLookup<StationaryAutoFire>(true),
            Ritual = state.GetComponentLookup<RitualState>(true),
            Engagement = state.GetComponentLookup<UnitEngagement>(true),
            ChaseAnchor = state.GetComponentLookup<ChaseAnchor>(true),
            Invulnerable = state.GetComponentLookup<Invulnerable>(true),
            TempDisarm = state.GetComponentLookup<TheWaningBorder.Abilities.TempDisarm>(true),
            AttackCommand = state.GetComponentLookup<TheWaningBorder.Core.Commands.Types.AttackCommand>(true),
            Disordered = state.GetComponentLookup<SectDisordered>(true),
        };

        public void Update(ref SystemState state)
        {
            Disordered.Update(ref state);
            Health.Update(ref state);
            Faction.Update(ref state);
            Transform.Update(ref state);
            Size.Update(ref state);
            Radius.Update(ref state);
            Building.Update(ref state);
            Wall.Update(ref state);
            Flying.Update(ref state);
            Archer.Update(ref state);
            Siege.Update(ref state);
            BallistaShooter.Update(ref state);
            BuildingsOnly.Update(ref state);
            DamageType.Update(ref state);
            CodexFrozen.Update(ref state);
            FiresOnMove.Update(ref state);
            NavLayer.Update(ref state);
            Destination.Update(ref state);
            Emplaced.Update(ref state);
            StationaryFire.Update(ref state);
            Ritual.Update(ref state);
            Engagement.Update(ref state);
            ChaseAnchor.Update(ref state);
            Invulnerable.Update(ref state);
            TempDisarm.Update(ref state);
            AttackCommand.Update(ref state);
        }

        /// <summary>TransientState.Active through a lookup: present AND enabled.</summary>
        public static bool Active<T>(in ComponentLookup<T> lookup, Entity e)
            where T : unmanaged, IComponentData, IEnableableComponent
            => lookup.HasComponent(e) && lookup.IsComponentEnabled(e);

        /// <summary>TargetGeometry.Extent(em, e) through the lookups.</summary>
        public TheWaningBorder.Core.TargetExtent Extent(Entity target)
            => TheWaningBorder.Core.TargetGeometry.Extent(target, in Transform, in Size, in Radius);

        /// <summary><see cref="CombatDamageHelper.DealsSiege"/> through the lookup.</summary>
        public bool DealsSiege(Entity attacker)
            => DamageType.TryGetComponent(attacker, out var d) && d.Value == global::DamageType.Siege;
    }

    /// <summary>
    /// Fix #226: central place for the on-hit effect pipeline so Melee and
    /// Ranged combat systems don't have to maintain their own 80-line copies
    /// of the same logic.
    ///
    /// Covers the pieces that were 100% duplicated between the two systems:
    ///   - Condemned mark bonus damage
    ///   - IgniteBuff consumption and bonus damage
    ///   - VoidStrikeBuff consumption and bonus damage
    ///   - SpellBuff.DamageMultiplier on attacker
    ///   - DamageReflect from target SpellBuff
    ///   - LastDamagedByFaction and LastAttackerEntity tracking
    ///   - Sect panic/control chance debuff application
    ///
    /// The main damage calculation (damage-type x armor-type matrix, defense,
    /// height modifier, sect multipliers) is intentionally NOT extracted
    /// because it is deeply coupled with the caller's local cooldown and
    /// sect-multiplier state. Callers should fold <see cref="GetSpellBuffArmorBonus"/>
    /// into the defense value BEFORE invoking the matrix calculation.
    /// </summary>
    public static class CombatDamageHelper
    {
        /// <summary>
        /// Returns extra armor on the target — sums SpellBuff.ArmorBonus and
        /// SilenceVigilArmor.Bonus. Callers MUST add this to the defender's
        /// defense value before running the damage-type x armor-type matrix.
        /// Wired into Melee/Ranged combat so abilities like StoneheartBastion's
        /// +3 armor aura and Silence's "Steadfast Vigil" stance bonus actually
        /// fire. (task-062 C-1, task-063 phase 2e)
        /// </summary>
        public static int GetSpellBuffArmorBonus(EntityManager em, Entity target)
            => GetSpellBuffArmorBonus(em, target, 0);

        /// <summary>
        /// As above, plus the PERCENTAGE armor buffs (SpellBuff.ArmorPct —
        /// King's Call): <paramref name="baseDefense"/> is the target's own
        /// armor for the incoming damage type, and the buff adds that
        /// percentage of it, rounded. A unit with no armor in that column gains
        /// nothing from a percentage, which is what "percent" means.
        /// docs/Design/Spells.md.
        /// </summary>
        public static int GetSpellBuffArmorBonus(EntityManager em, Entity target, int baseDefense)
        {
            int bonus = 0;
            if (TransientState.Active<SpellBuff>(em, target))
            {
                var buff = em.GetComponentData<SpellBuff>(target);
                bonus += (int)buff.ArmorBonus;
                if (buff.ArmorPct > 0f && baseDefense > 0)
                    bonus += (int)math.round(baseDefense * buff.ArmorPct / 100f);
            }
            if (em.HasComponent<SilenceVigilArmor>(target))
                bonus += em.GetComponentData<SilenceVigilArmor>(target).Bonus;
            return bonus;
        }

        // ── Fighting from a wall-walk (docs/Design/Combat_Pacing.md, 2026-10-20)
        // A TROOP standing on a wall-walk (NavLayerIndex on the rampart layer —
        // mid-climb does not count) deals more damage and carries more armour.
        // Decided live from NavLayerIndex where the damage is resolved, so there
        // is no component to keep in step and nothing for a save to carry. The
        // two percentages live on WallGarrison.asset (WallGarrisonConfig).

        /// <summary>True when <paramref name="e"/> is a unit standing on a
        /// wall-walk — the same test PresentationSpawnSystem.IsOnWallLayer and
        /// the battlements code use.</summary>
        public static bool OnWallTop(EntityManager em, Entity e)
            => e != Entity.Null && em.Exists(e)
               && em.HasComponent<UnitTag>(e)
               && em.HasComponent<NavLayerIndex>(e)
               && em.GetComponentData<NavLayerIndex>(e).Layer == NavLayerIndex.LayerRampart;

        /// <summary>Damage multiplier for a troop on a wall-walk
        /// (1 + WallGarrisonConfig.wallTopDamagePct / 100).</summary>
        public static float WallTopDamageMult
            => 1f + TheWaningBorder.Entities.WallGarrisonConfig.I.wallTopDamagePct / 100f;

        /// <summary>
        /// The attacker-side multiplier on a weapon hit: the wall-walk bonus
        /// when <paramref name="attacker"/> fights from a wall-walk, otherwise
        /// <paramref name="heightMult"/> (HeightAdvantage / the melee height
        /// modifier). The wall bonus REPLACES the height damage scaling rather
        /// than stacking on it — a wall-top shooter is always above its target,
        /// so stacking would pay the same elevation twice. (Range is not
        /// touched: a wall-top shooter keeps HeightAdvantage's range.)
        /// </summary>
        public static float AttackerHeightOrWallMult(EntityManager em, Entity attacker, float heightMult)
            => OnWallTop(em, attacker) ? WallTopDamageMult : heightMult;

        /// <summary>
        /// Extra armour for a troop on a wall-walk: WallGarrisonConfig.wallTopArmorPct
        /// percent of its own armour for the incoming damage type
        /// (<paramref name="baseDefense"/>), rounded — resolved exactly like
        /// SpellBuff.ArmorPct in <see cref="GetSpellBuffArmorBonus(EntityManager, Entity, int)"/>.
        /// 0 off the wall. Callers add it to the defence value BEFORE the
        /// damage formula.
        /// </summary>
        public static int WallTopArmorBonus(EntityManager em, Entity target, int baseDefense)
        {
            if (baseDefense <= 0 || !OnWallTop(em, target)) return 0;
            float pct = TheWaningBorder.Entities.WallGarrisonConfig.I.wallTopArmorPct;
            return pct > 0f ? (int)math.round(baseDefense * pct / 100f) : 0;
        }

        /// <summary>The target's own armor against <paramref name="dmgType"/>
        /// (0 without a Defense component), before any buff.</summary>
        public static int BaseDefense(EntityManager em, Entity target, DamageType dmgType)
            => em.HasComponent<Defense>(target)
                ? CombatModifiers.GetDefenseValue(em.GetComponentData<Defense>(target), dmgType)
                : 0;

        /// <summary>
        /// Feraldis fire-and-blood attack multiplier for one attacker
        /// (docs/Design/Age_1_Feraldis.md). Two independent sources, applied
        /// multiplicatively:
        ///   - <see cref="BloodFrenzy"/>: the culture signature — any Feraldis
        ///     unit fighting on bloodsoaked ground. Stamped by BloodFrenzySystem.
        ///   - <see cref="DeathFrenzyState"/>: the Berserker's last stand.
        /// Returns 1.0 for everyone else, so non-Feraldis combat is untouched.
        /// </summary>
        public static float GetFrenzyDamageMult(EntityManager em, Entity attacker)
        {
            // Frenzy (scaled by the Blood ladder), Berserk and Overcharge —
            // docs/Design/Age_1_Feraldis.md §6-7 (2026-10-12).
            float mult = TheWaningBorder.Feraldis.FeraldisCombat.DamageMult(
                em, attacker, TheWaningBorder.Core.Config.FeraldisConstants.FrenzyDamageMult);
            if (em.HasComponent<DeathFrenzyState>(attacker))
                mult *= TheWaningBorder.Core.Config.FeraldisConstants.DeathFrenzyDamageMult;
            return mult;
        }

        /// <summary>
        /// Attack-cooldown multiplier from Feraldis blood frenzy (&lt; 1 means
        /// swings come faster). Death Frenzy deliberately does NOT shorten the
        /// cooldown — its bonus is raw damage and speed, so the two frenzies
        /// read differently in play.
        /// </summary>
        public static float GetFrenzyCooldownMult(EntityManager em, Entity attacker)
        {
            return em.HasComponent<BloodFrenzy>(attacker)
                ? TheWaningBorder.Core.Config.FeraldisConstants.FrenzyCooldownMult
                : 1f;
        }

        /// <summary>
        /// Attack-cooldown multiplier from timed haste (SectHaste, i.e. Blood
        /// Rain). Returns 1 when the unit is not hasted, so callers can multiply
        /// it in unconditionally.
        ///
        /// SectHaste.Multiplier is a SPEED multiplier, so the cooldown is
        /// DIVIDED by it: +15% attack speed means the unit swings on 1/1.15 of
        /// its normal cooldown. A zero multiplier means the component was
        /// stamped without one — that is "no effect", not "infinite cooldown",
        /// hence the &lt;= 0 guard.
        /// </summary>
        public static float GetHasteCooldownMult(EntityManager em, Entity attacker)
        {
            if (!TransientState.Active<SectHaste>(em, attacker)) return 1f;
            var haste = em.GetComponentData<SectHaste>(attacker);
            if (haste.TimeRemaining <= 0f) return 1f;
            return haste.Multiplier <= 0f ? 1f : 1f / haste.Multiplier;
        }

        /// <summary>
        /// Merge a new SpellBuff onto an entity. If the entity already has one,
        /// the per-field max wins (so a shorter Safeguard doesn't wipe a longer
        /// Aura's reflect). Without this merge, `ecb.AddComponent` overwrites
        /// the existing buff and silently drops fields it didn't set —
        /// stacking Safeguard onto a unit already inside a Sanctuary aura
        /// discarded the aura's reflect/armor. (task-062 C-3)
        /// </summary>
        public static void MergeSpellBuff(EntityManager em, EntityCommandBuffer ecb,
            Entity target, SpellBuff incoming)
        {
            // Merge only against an ACTIVE buff — a disabled SpellBuff is a
            // stale husk from an expired buff, and max()ing against it would
            // resurrect old values.
            if (TransientState.Active<SpellBuff>(em, target))
            {
                var existing = em.GetComponentData<SpellBuff>(target);
                existing.ArmorBonus       = Unity.Mathematics.math.max(existing.ArmorBonus, incoming.ArmorBonus);
                existing.ArmorPct         = Unity.Mathematics.math.max(existing.ArmorPct, incoming.ArmorPct);
                existing.DamageMultiplier = Unity.Mathematics.math.max(existing.DamageMultiplier, incoming.DamageMultiplier);
                existing.SpeedMultiplier  = Unity.Mathematics.math.max(existing.SpeedMultiplier, incoming.SpeedMultiplier);
                existing.DamageReflect    = Unity.Mathematics.math.max(existing.DamageReflect, incoming.DamageReflect);
                existing.TimeRemaining    = Unity.Mathematics.math.max(existing.TimeRemaining, incoming.TimeRemaining);
                em.SetComponentData(target, existing);
            }
            else
            {
                TransientState.Set(ecb, target, incoming);
            }
        }

        /// <summary>
        /// Applies on-hit bonus damage from Condemned, IgniteBuff, VoidStrikeBuff,
        /// and SpellBuff.DamageMultiplier on the attacker. Returns the modified
        /// damage. Consumes IgniteBuff / VoidStrikeBuff charges via ECB.
        ///
        /// Order: matrix damage → SpellBuff.DamageMultiplier (attacker buff) →
        /// Condemned (target debuff) → Ignite/VoidStrike one-shot bonuses. The
        /// multiplier is applied before flat add-ons so timed Empower-style
        /// buffs scale with base damage, not with one-shot proc damage.
        /// (task-062 C-1)
        /// </summary>
        /// <summary>
        /// True when <paramref name="attacker"/> may actually take HP off
        /// <paramref name="target"/> — the same rule
        /// <see cref="ApplyBonusDamageOnHit"/> enforces by returning 0, exposed
        /// so a caller can skip the hit ENTIRELY.
        ///
        /// Callers need this because every damage site ends with a
        /// minimum-damage floor (`math.max(1, finalDamage)`, or the ranged
        /// building-chip clamp). A floor applied AFTER the ally gate turns the
        /// gate's 0 straight back into 1, and teammates chipped each other for
        /// a point a swing — the "allies are attacking each other" report of
        /// 2026-08-15. The gate and the floor have to agree, so the floor asks
        /// this first. docs/Design/Teams.md
        ///
        /// Entities without a FactionTag (neutral props, unowned wreckage) are
        /// damageable as before — this only speaks about faction relations.
        /// </summary>
        /// <summary>
        /// The Wall Rule (docs/Design/Combat_Pacing.md): only SIEGE damages a
        /// wall piece. True when a hit of <paramref name="dmgType"/> on
        /// <paramref name="target"/> must do nothing. Every damage path that
        /// can reach a wall asks this where the damage lands, so a shooter
        /// that forgets to filter walls out of its targeting (the building
        /// towers did, until 2026-09-26) still cannot chip one down.
        /// </summary>
        public static bool WallRuleBlocks(EntityManager em, Entity target, DamageType dmgType)
            => dmgType != DamageType.Siege && em.HasComponent<WallTag>(target);

        /// <summary>The attacker-side form of <see cref="WallRuleBlocks"/>:
        /// may this entity's own damage type hurt a wall at all?</summary>
        public static bool DealsSiege(EntityManager em, Entity attacker)
            => em.HasComponent<DamageTypeData>(attacker)
               && em.GetComponentData<DamageTypeData>(attacker).Value == DamageType.Siege;

        /// <summary><see cref="CanDamage(EntityManager, Entity, Entity)"/>
        /// through the shared lookups (the per-unit combat loops).</summary>
        public static bool CanDamage(in CombatLookups lk, Entity attacker, Entity target)
        {
            if (Frenzied(in lk, attacker) || Frenzied(in lk, target)) return attacker != target;
            if (!lk.Faction.TryGetComponent(attacker, out var af)
                || !lk.Faction.TryGetComponent(target, out var tf))
                return true;
            return Alliances.AreHostile(af.Value, tf.Value);
        }

        /// <summary>A unit under Friendly Fire: hostile to everyone, everyone
        /// hostile to it, whatever the factions say.</summary>
        public static bool Frenzied(in CombatLookups lk, Entity e)
            => lk.Disordered.HasComponent(e) && lk.Disordered.IsComponentEnabled(e);

        public static bool Frenzied(EntityManager em, Entity e)
            => TransientState.Active<SectDisordered>(em, e);

        public static bool CanDamage(EntityManager em, Entity attacker, Entity target)
        {
            if (Frenzied(em, attacker) || Frenzied(em, target)) return attacker != target;
            if (!em.HasComponent<FactionTag>(attacker) || !em.HasComponent<FactionTag>(target))
                return true;

            return Alliances.AreHostile(
                em.GetComponentData<FactionTag>(attacker).Value,
                em.GetComponentData<FactionTag>(target).Value);
        }

        public static int ApplyBonusDamageOnHit(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity target, int baseDamage,
            TheWaningBorder.Core.HitSource source = TheWaningBorder.Core.HitSource.Melee)
        {
            // Last line of defence for the no-allied-damage rule. Hostility is
            // decided upstream (target acquisition, attack orders, AoE filters)
            // and MeleeCombatSystem / RangedCombatSystem deliberately do no
            // faction check of their own — they damage whatever is in Target.
            // A zero here means any path that slips through, now or later,
            // still cannot hurt a teammate. docs/Design/Teams.md
            if (em.HasComponent<FactionTag>(attacker) && em.HasComponent<FactionTag>(target)
                && !Frenzied(em, attacker) && !Frenzied(em, target))
            {
                var af = em.GetComponentData<FactionTag>(attacker).Value;
                var tf = em.GetComponentData<FactionTag>(target).Value;
                if (!Alliances.AreHostile(af, tf)) return 0;
            }

            int final = baseDamage;

            // Presentation: a hit effect on the struck UNIT (the charge impact
            // when the attacker is charging). Both melee and projectile hits
            // come through here. A queue the sim only writes — never read back.
            if (em.HasComponent<UnitTag>(target))
                TheWaningBorder.Core.CombatVfxSignals.Post(target, source,
                    TransientState.Active<TheWaningBorder.Abilities.Charging>(em, attacker),
                    em.HasComponent<FactionTag>(attacker) ? em.GetComponentData<FactionTag>(attacker).Value : default);

            // SpellBuff.DamageMultiplier on attacker (Empower-style timed buff)
            if (TransientState.Active<SpellBuff>(em, attacker))
            {
                float dmgMult = em.GetComponentData<SpellBuff>(attacker).DamageMultiplier;
                if (dmgMult > 0f && !Unity.Mathematics.math.abs(dmgMult - 1f).Equals(0f))
                    final = (int)(final * dmgMult);
            }

            // Feraldis War Totem aura: a fractional attack bonus while the
            // attacker stands in a friendly totem's radius. Added/removed by
            // WarTotemAuraSystem as units enter and leave, so this is a flat
            // component read rather than a per-attack totem scan.
            if (em.HasComponent<TotemAuraBuff>(attacker))
            {
                float bonus = em.GetComponentData<TotemAuraBuff>(attacker).AttackBonus;
                if (bonus > 0f) final = (int)(final * (1f + bonus));
            }

            // Charge payoff. Percentages first (the unit's own innate charge plus a
            // one-shot War Horn window), then King's Call's flat bonus on top.
            if (TransientState.Active<TheWaningBorder.Abilities.Charging>(em, attacker))
            {
                float chargePct = 0f;
                if (em.HasComponent<TheWaningBorder.Abilities.InnateChargePct>(attacker))
                    chargePct += em.GetComponentData<TheWaningBorder.Abilities.InnateChargePct>(attacker).Pct;
                if (TransientState.Active<TheWaningBorder.Abilities.NextChargePct>(em, attacker))
                {
                    chargePct += em.GetComponentData<TheWaningBorder.Abilities.NextChargePct>(attacker).Pct;
                    // War Horn is a NEXT-charge window: spend it on this hit.
                    TransientState.Clear<TheWaningBorder.Abilities.NextChargePct>(em, ecb, attacker);
                }

                if (chargePct > 0f) final = (int)(final * (1f + chargePct / 100f));

                // Ability: flat charge bonus while the attacker is charging (King's
                // Call grants ChargeDamageBonus to allied cavalry; King Lexor gains
                // it from his own aura).
                if (TransientState.Active<TheWaningBorder.Abilities.ChargeDamageBonus>(em, attacker))
                    final += em.GetComponentData<TheWaningBorder.Abilities.ChargeDamageBonus>(attacker).Bonus;
            }
            // (Liquid Courage's incoming-damage reduction is applied uniformly at
            // every HP-application site via AbilityDamageHooks.ScaleIncoming, not here.)

            // Alanthor sect unit traits and the Catalogued mark
            // (docs/Design/Sects.md §4): Stasis Bolt, Borrowed Breath, Stored
            // Strike, Run Down.
            final = TheWaningBorder.Systems.Sect.AlanthorSectUnitTraits.OnHit(em, ecb, attacker, target, final);

            // Condemned mark: target takes bonus damage
            if (TransientState.Active<Condemned>(em, target))
            {
                var condemned = em.GetComponentData<Condemned>(target);
                final = (int)(final * condemned.DamageMultiplier);
            }

            // MarkedForSentence (Justice Lv I passive): if the target was marked
            // by the attacker's faction, the marker faction's units deal bonus
            // damage. Other factions attacking the same target don't get the
            // bonus — the mark is per-marker. (task-063 phase 2c)
            if (TransientState.Active<MarkedForSentence>(em, target)
                && em.HasComponent<FactionTag>(attacker))
            {
                var mark = em.GetComponentData<MarkedForSentence>(target);
                if (mark.MarkerFaction == em.GetComponentData<FactionTag>(attacker).Value
                    && mark.DamageBonus > 0f)
                {
                    final = (int)(final * (1f + mark.DamageBonus));
                }
            }

            // Ruin "Profane Hands": Ruin-adopted attackers deal +25/40/60%
            // damage to enemy buildings. Refund-on-destroy half lives in
            // SectRuinRefundSystem. Friendly fire is excluded. (task-063
            // phase 2d / phase 4 scaling)
            if (em.HasComponent<BuildingTag>(target)
                && em.HasComponent<FactionTag>(attacker)
                && em.HasComponent<FactionTag>(target))
            {
                var atkFac = em.GetComponentData<FactionTag>(attacker).Value;
                var tgtFac = em.GetComponentData<FactionTag>(target).Value;
                if (atkFac != tgtFac)
                {
                    byte ruinLevel = SectQuery.LevelOf(em, atkFac,
                        SectConfig.Ruin, SectLeverKind.Passive);
                    if (ruinLevel > 0)
                    {
                        float ruinMult = ruinLevel switch
                        {
                            2 => 1.40f,
                            3 => 1.60f,
                            _ => 1.25f,
                        };
                        final = (int)(final * ruinMult);
                    }
                }
            }

            // Antiquity "Tally of the Lost": +N% per logged kill of the
            // target's UnitClass; per-kill bonus scales with lever level.
            // (task-063 phase 2e / phase 4 scaling)
            if (em.HasComponent<AntiquityKills>(attacker)
                && em.HasComponent<UnitTag>(target)
                && em.HasComponent<FactionTag>(attacker))
            {
                byte antiqLevel = SectQuery.LevelOf(em,
                    em.GetComponentData<FactionTag>(attacker).Value,
                    SectConfig.Antiquity, SectLeverKind.Passive);
                if (antiqLevel > 0)
                {
                    var kills = em.GetComponentData<AntiquityKills>(attacker);
                    var tgtClass = em.GetComponentData<UnitTag>(target).Class;
                    byte n = SectAntiquityTallySystem.KillsAgainst(in kills, tgtClass);
                    if (n > 0)
                    {
                        // Spec: +0.5% / +1% / +1.5% per logged kill, which
                        // with the 10-kill tally cap yields the spec's
                        // +5% / +10% / +15% per-class ceilings.
                        float perKill = antiqLevel switch
                        {
                            2 => 0.010f,
                            3 => 0.015f,
                            _ => 0.005f,
                        };
                        final = (int)(final * (1f + perKill * n));
                    }
                }
            }

            // Wrath "Spite of the Forsaken": +N% per 5% HP missing on the
            // attacker. Lv I 0.5% per 5% (max +9.5%). Lv II 1% (max +19%).
            // Lv III 1.5% (max +28.5%). Stacks multiplicatively with the
            // blood-pool bonus when the attacker is standing in a Feraldis
            // pool (phase 3): +10/+15/+20% by lever level.
            // (task-063 phase 2c / phase 4 scaling / phase 3)
            if (em.HasComponent<FactionTag>(attacker)
                && em.HasComponent<Health>(attacker))
            {
                byte wrathLevel = SectQuery.LevelOf(em,
                    em.GetComponentData<FactionTag>(attacker).Value,
                    SectConfig.Wrath, SectLeverKind.Passive);
                if (wrathLevel > 0)
                {
                    var hp = em.GetComponentData<Health>(attacker);
                    if (hp.Max > 0 && hp.Value < hp.Max)
                    {
                        float fractionMissing = 1f - (float)hp.Value / hp.Max;
                        float scalar = wrathLevel switch
                        {
                            2 => 0.20f,
                            3 => 0.30f,
                            _ => 0.10f,
                        };
                        final = (int)(final * (1f + fractionMissing * scalar));
                    }

                    if (em.HasComponent<InBloodPool>(attacker))
                    {
                        float poolMult = wrathLevel switch
                        {
                            2 => 1.15f,
                            3 => 1.20f,
                            _ => 1.10f,
                        };
                        final = (int)(final * poolMult);
                    }
                }
            }

            // IgniteBuff: attacker's next attacks deal bonus fire damage
            if (em.HasComponent<IgniteBuff>(attacker))
            {
                var ignite = em.GetComponentData<IgniteBuff>(attacker);
                if (ignite.AttacksRemaining > 0)
                {
                    final += (int)ignite.BonusDamage;
                    ignite.AttacksRemaining--;
                    if (ignite.AttacksRemaining <= 0)
                        ecb.RemoveComponent<IgniteBuff>(attacker);
                    else
                        em.SetComponentData(attacker, ignite);
                }
            }

            // VoidStrikeBuff: attacker's next attack deals bonus damage
            if (em.HasComponent<VoidStrikeBuff>(attacker))
            {
                var voidStrike = em.GetComponentData<VoidStrikeBuff>(attacker);
                float bonus = em.HasComponent<BorderTag>(target)
                    ? voidStrike.BonusVsBorder
                    : voidStrike.BonusDamage;
                final += (int)bonus;
                ecb.RemoveComponent<VoidStrikeBuff>(attacker);
            }

            // ---- Alanthor tech passives -------------------------------------
            // Attacker side: the Garrison "Charge" opening blow and the Siege Yard
            // "Ranging Shot" aimed shot. Both are one-shot windows spent here.
            if (TransientState.Active<TheWaningBorder.Abilities.FirstStrike>(em, attacker))
            {
                var fs = em.GetComponentData<TheWaningBorder.Abilities.FirstStrike>(attacker);
                // Only a blow landed INSIDE the charge window counts. Ready on
                // its own just means armed — the unit has to have actually
                // gone in. Zeroing the window here is what tells the passive
                // system to take the speed burst back off.
                if (fs.Ready != 0 && fs.WindowRemaining > 0f)
                {
                    final = (int)(final * (1f + fs.Pct / 100f));
                    fs.Ready = 0;
                    fs.WindowRemaining = 0f;
                    fs.OutOfCombatTimer = 0f;
                    ecb.SetComponent(attacker, fs);
                }
            }
            if (TransientState.Active<TheWaningBorder.Abilities.NextShotBonus>(em, attacker))
            {
                final = (int)(final * (1f + em.GetComponentData<TheWaningBorder.Abilities.NextShotBonus>(attacker).Pct / 100f));
                TransientState.Clear<TheWaningBorder.Abilities.NextShotBonus>(em, ecb, attacker);
            }

            // Defender side. Shield Wall eats the first hit while planted; Deploy
            // Stakes only answers a CHARGING attacker; Siege Screens is continuous
            // but ranged-only. Each reduction comes off the post-bonus number.
            if (em.HasComponent<TheWaningBorder.Abilities.ShieldWallState>(target))
            {
                var sw = em.GetComponentData<TheWaningBorder.Abilities.ShieldWallState>(target);
                if (sw.Ready != 0)
                {
                    final = (int)(final * (1f - sw.Pct / 100f));
                    sw.Ready = 0;
                    sw.StillTimer = 0f;
                    ecb.SetComponent(target, sw);
                }
            }
            if (em.HasComponent<TheWaningBorder.Abilities.StakesState>(target)
                && TransientState.Active<TheWaningBorder.Abilities.Charging>(em, attacker))
            {
                var st = em.GetComponentData<TheWaningBorder.Abilities.StakesState>(target);
                if (st.Ready != 0)
                {
                    final = (int)(final * (1f - st.Pct / 100f));
                    st.Ready = 0;
                    st.CooldownRemaining = TheWaningBorder.Abilities
                        .AlanthorPassiveTuning.StakesRefreshSeconds;
                    ecb.SetComponent(target, st);

                    // The stakes bite back: half of what still landed is paid
                    // into the horse that ran onto them. Taken from the
                    // POST-reduction number, so the rider is answering for the
                    // blow he actually delivered.
                    if (st.ReflectPct > 0f && em.HasComponent<Health>(attacker))
                    {
                        int paid = (int)(final * (st.ReflectPct / 100f));
                        if (paid > 0)
                        {
                            var atkHealth = em.GetComponentData<Health>(attacker);
                            atkHealth.Value -= ShieldDamage.Absorb(em, attacker, TheWaningBorder.Abilities.AbilityDamageHooks.ScaleIncoming(em, attacker, paid));
                            em.SetComponentData(attacker, atkHealth);
                        }
                    }
                }
            }
            if (em.HasComponent<TheWaningBorder.Abilities.SiegeScreens>(target)
                && em.HasComponent<DamageTypeData>(attacker)
                && em.GetComponentData<DamageTypeData>(attacker).Value == DamageType.Ranged)
            {
                var ss = em.GetComponentData<TheWaningBorder.Abilities.SiegeScreens>(target);
                if (ss.Ready != 0) final = (int)(final * (1f - ss.Pct / 100f));
            }
            if (final < 1) final = 1;


            return final;
        }

        /// <summary>
        /// Reflects a fraction of dealt damage back to the attacker if the
        /// target has a SpellBuff with DamageReflect > 0.
        /// </summary>
        public static void ApplyDamageReflect(EntityManager em,
            Entity attacker, Entity target, int finalDamage)
        {
            if (!TransientState.Active<SpellBuff>(em, target)) return;

            var tgtBuff = em.GetComponentData<SpellBuff>(target);
            if (tgtBuff.DamageReflect <= 0f) return;

            int reflected = math.max(1, (int)(finalDamage * tgtBuff.DamageReflect));
            if (!em.HasComponent<Health>(attacker)) return;
            // A shooting wall tower is a wall piece: a reflected hit is not
            // siege, so it must not chip the tower. The Wall Rule.
            if (em.HasComponent<WallTag>(attacker)) return;
            var attackerHealth = em.GetComponentData<Health>(attacker);
            attackerHealth.Value -= ShieldDamage.Absorb(em, attacker, TheWaningBorder.Abilities.AbilityDamageHooks.ScaleIncoming(em, attacker, reflected));
            em.SetComponentData(attacker, attackerHealth);
        }

        /// <summary>
        /// Updates LastDamagedByFaction and LastAttackerEntity on the target.
        /// Used by PillageSystem, CaravanDeathSystem, and defensive-stance
        /// return-fire logic.
        ///
        /// If <paramref name="elapsedTime"/> is non-zero AND the target is a
        /// building, also stamps <see cref="BuildingDamageState.LastDamagedAt"/>
        /// so out-of-combat readers (Renewal's auto-repair Lv I, etc.) can
        /// gate repair ticks on a quiet-window threshold. Pass 0 from callers
        /// that don't have the time handy. (task-063 phase 2c)
        /// </summary>
        /// <summary>
        /// Add <paramref name="damage"/> to the attacker's match-long damage
        /// ledger (<see cref="DamageDealtTotal"/>), which the Wrath sect's
        /// Spite pools and pays back. Call this wherever damage is actually
        /// subtracted from a target's Health, with the FINAL amount that
        /// landed — Spite's whole premise is that a unit answers for what it
        /// really did, not what it rolled before mitigation.
        ///
        /// Silently ignores non-positive amounts and dead references, so it is
        /// safe to call unconditionally from a combat hot path.
        /// </summary>
        public static void RecordDamageDealt(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, int damage)
        {
            if (damage <= 0) return;
            if (attacker == Entity.Null || !em.Exists(attacker)) return;

            if (em.HasComponent<DamageDealtTotal>(attacker))
            {
                // Pre-added disabled with Value 0 on units: enabling and
                // accumulating is exact. Enable-bit writes are not structural,
                // so no ECB detour is needed.
                var led = em.GetComponentData<DamageDealtTotal>(attacker);
                led.Value += damage;
                em.SetComponentData(attacker, led);
                em.SetComponentEnabled<DamageDealtTotal>(attacker, true);
            }
            else
            {
                // Structural add during query iteration — must go through the
                // command buffer.
                TransientState.Set(ecb, attacker, new DamageDealtTotal { Value = damage });
            }
        }

        public static void TrackLastDamager(EntityManager em, EntityCommandBuffer ecb,
            Entity attacker, Entity target, double elapsedTime = 0)
        {
            if (em.HasComponent<FactionTag>(attacker))
            {
                var lastDamaged = new LastDamagedByFaction
                {
                    Value = em.GetComponentData<FactionTag>(attacker).Value
                };
                if (em.HasComponent<LastDamagedByFaction>(target))
                {
                    em.SetComponentData(target, lastDamaged);
                    em.SetComponentEnabled<LastDamagedByFaction>(target, true);
                }
                else
                    TransientState.Set(ecb, target, lastDamaged);
            }

            // Use ECB for structural add (required during query iteration),
            // but immediate write for existing component to ensure latest attacker wins.
            if (em.HasComponent<LastAttackerEntity>(target))
            {
                em.SetComponentData(target, new LastAttackerEntity { Value = attacker });
                em.SetComponentEnabled<LastAttackerEntity>(target, true);
            }
            else
                TransientState.Set(ecb, target, new LastAttackerEntity { Value = attacker });

            // Building-only damage timestamp for the out-of-combat repair window.
            if (elapsedTime > 0 && em.HasComponent<BuildingTag>(target))
            {
                var stamp = new BuildingDamageState { LastDamagedAt = elapsedTime };
                if (em.HasComponent<BuildingDamageState>(target))
                    em.SetComponentData(target, stamp);
                else
                    ecb.AddComponent(target, stamp);
            }
        }
    }
}
