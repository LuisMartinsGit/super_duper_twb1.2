// AbilityEffectExecutor.cs
// Translates an AbilityCard's structured effects into concrete ECS buff
// components on a target. Shared by the active-cast path and the aftermath
// chain so a "cast" is one call.
//
// Reuses the existing SpellBuff / SpellDebuff channels (read live by combat &
// movement) plus the ability-specific components in AbilityRuntimeComponents.

using Unity.Entities;
using TheWaningBorder.Core;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Abilities
{
    public static class AbilityEffectExecutor
    {
        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and these were never disposed, so this hot path leaked one
        // per invocation. A bloated registry slows every later query AND
        // every structural change. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_UnitTagFactionTagArmorTypeDataLocalTransform =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<ArmorTypeData>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTagArmorTypeDataLocalTransform;

        static readonly ComponentType[] QT_UnitTagFactionTagDamageTypeDataLocalTransform =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<DamageTypeData>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTagDamageTypeDataLocalTransform;

        #endregion

        /// <summary>
        /// Apply <paramref name="card"/>'s effects. Self-targeted effects land on
        /// <paramref name="caster"/>; building/target effects land on
        /// <paramref name="target"/> (falls back to caster). Uses the card's own
        /// Duration unless <paramref name="durationOverride"/> &gt; 0.
        /// </summary>
        public static void Apply(EntityManager em, Entity caster, AbilityCard card, Entity target, float durationOverride = -1f)
        {
            if (card == null || caster == Entity.Null || !em.Exists(caster)) return;
            float dur = durationOverride > 0f ? durationOverride : math.max(0f, card.Duration);
            var effTarget = (target != Entity.Null && em.Exists(target)) ? target : caster;
            AbilityVfxSignals.Post(card, caster, effTarget, dur);

            // Accumulate the SpellBuff/SpellDebuff so multiple stat effects on one
            // ability produce a single component.
            var buff = TransientState.Active<SpellBuff>(em, caster) ? em.GetComponentData<SpellBuff>(caster) : default;
            var debuff = TransientState.Active<SpellDebuff>(em, caster) ? em.GetComponentData<SpellDebuff>(caster) : default;
            bool touchBuff = false, touchDebuff = false;

            var effects = card.Effects;
            for (int i = 0; effects != null && i < effects.Length; i++)
            {
                var e = effects[i];
                switch (e.Kind)
                {
                    case AbilityEffectKind.AttackPct:
                        buff.DamageMultiplier = math.max(buff.DamageMultiplier, 1f + e.Value / 100f);
                        buff.TimeRemaining = math.max(buff.TimeRemaining, dur);
                        touchBuff = true;
                        break;

                    case AbilityEffectKind.ArmorPct:
                        // A PERCENTAGE of the unit's own armor for the incoming
                        // damage type, resolved where the damage lands
                        // (CombatDamageHelper.GetSpellBuffArmorBonus). Max-merged,
                        // like every other SpellBuff field, so two sources never stack.
                        buff.ArmorPct = math.max(buff.ArmorPct, e.Value);
                        buff.TimeRemaining = math.max(buff.TimeRemaining, dur);
                        touchBuff = true;
                        break;

                    case AbilityEffectKind.ArmorFlat:
                        buff.ArmorBonus += e.Value;
                        buff.TimeRemaining = math.max(buff.TimeRemaining, dur);
                        touchBuff = true;
                        break;

                    case AbilityEffectKind.DamageTakenPct:
                        // -90 => take 10% damage. Stored as a multiplier (min-wins so the
                        // strongest reduction sticks).
                        {
                            float mult = math.max(0f, 1f + e.Value / 100f);
                            buff.DamageTakenMultiplier = buff.DamageTakenMultiplier <= 0f
                                ? mult : math.min(buff.DamageTakenMultiplier, mult);
                            buff.TimeRemaining = math.max(buff.TimeRemaining, dur);
                            touchBuff = true;
                        }
                        break;

                    case AbilityEffectKind.MoveSpeedPct:
                        // Negative => slow. Positive speed buffs go through SpellBuff.SpeedMultiplier.
                        if (e.Value < 0f)
                        {
                            debuff.SpeedReduction = math.max(debuff.SpeedReduction, math.min(0.95f, -e.Value / 100f));
                            debuff.TimeRemaining = math.max(debuff.TimeRemaining, dur);
                            touchDebuff = true;
                        }
                        else
                        {
                            buff.SpeedMultiplier = math.max(buff.SpeedMultiplier, 1f + e.Value / 100f);
                            buff.TimeRemaining = math.max(buff.TimeRemaining, dur);
                            touchBuff = true;
                        }
                        break;

                    case AbilityEffectKind.SelfDoTPctOverDuration:
                        if (em.HasComponent<Health>(caster) && dur > 0f)
                        {
                            int maxHp = em.GetComponentData<Health>(caster).Max;
                            float total = maxHp * (e.Value / 100f);
                            AddOrSet(em, caster, new SelfDoT { Dps = total / dur, TimeRemaining = dur });
                        }
                        break;

                    case AbilityEffectKind.HpFloor:
                        AddOrSet(em, caster, new LifeCling { Floor = (int)math.max(1f, e.Value), TimeRemaining = dur });
                        break;

                    case AbilityEffectKind.ResourceYieldPct:
                        AddOrSet(em, effTarget, new AutoYieldBoost { Mult = 1f + e.Value / 100f, TimeRemaining = dur });
                        break;

                    case AbilityEffectKind.NoAutomation:
                        AddOrSet(em, effTarget, new UnderAutomation { TimeRemaining = dur });
                        break;

                    case AbilityEffectKind.RevealFog:
                        SpawnFogReveal(em, caster, effTarget, e.Value > 0f ? e.Value : card.Radius, dur);
                        break;

                    case AbilityEffectKind.ChargeDamagePct:
                        ApplyToAlliedCavalry(em, caster, card.Radius,
                            (u) => AddOrSet(em, u, new NextChargePct { Pct = e.Value, TimeRemaining = dur }));
                        break;

                    case AbilityEffectKind.FireRatePct:
                        // Choreographed Volleys. VolleyBuff is the same component
                        // RangedCombatSystem already divides its reload by, so the
                        // ability reuses the shipped mechanic rather than adding a
                        // second fire-rate path. Value 100 = double rate.
                        ApplyToAlliedRanged(em, caster, card.Radius,
                            (u) => AddOrSet(em, u, new VolleyBuff
                            { Mult = 1f + e.Value / 100f, TimeRemaining = dur }));
                        break;

                    case AbilityEffectKind.DisarmWhileBuffed:
                        // Full Gallop's speed burst rides the same radius scan: the
                        // MoveSpeedPct branch above only buffs the caster, so the
                        // sprint is stamped on every allied cavalryman here too.
                        {
                            float spd = card.EffectValue(AbilityEffectKind.MoveSpeedPct);
                            ApplyToAlliedCavalry(em, caster, card.Radius, (u) =>
                            {
                                if (spd > 0f)
                                {
                                    var b = TransientState.Active<SpellBuff>(em, u) ? em.GetComponentData<SpellBuff>(u) : default;
                                    b.SpeedMultiplier = math.max(b.SpeedMultiplier, 1f + spd / 100f);
                                    b.TimeRemaining = math.max(b.TimeRemaining, dur);
                                    TransientState.Set(em, u, b);
                                }
                                AddOrSet(em, u, new TempDisarm { TimeRemaining = dur });
                            });
                        }
                        break;

                    case AbilityEffectKind.DeployFieldHospital:
                        {
                            float3 hpPos = em.HasComponent<LocalTransform>(caster)
                                ? em.GetComponentData<LocalTransform>(caster).Position : float3.zero;
                            Faction hpFac = em.HasComponent<FactionTag>(caster)
                                ? em.GetComponentData<FactionTag>(caster).Value : default;
                            // The team's cap and spacing (docs/Design/Sects.md,
                            // FieldHospitalSystem.asset): a refused cast costs
                            // nothing — the cooldown the lifecycle just charged
                            // is given back.
                            if (!TheWaningBorder.Entities.FieldHospital.CanDeploy(em, hpFac, hpPos, out string hpWhy))
                            {
                                AbilityCooldownUtil.Refund(em, caster, card);
                                if (hpFac == GameSettings.LocalPlayerFaction)
                                    TheWaningBorder.Core.SimSignals.Notify(hpWhy);
                                break;
                            }
                            TheWaningBorder.Entities.FieldHospital.Create(em, hpPos, hpFac);
                        }
                        break;

                    case AbilityEffectKind.ShardboundFury:
                        TheWaningBorder.Entities.ShardboundFury.Cast(em, caster, card);
                        break;

                    // Harkon Fiend (docs/Design/Age_1_Feraldis.md §8).
                    case AbilityEffectKind.FeraldisWarcry:
                        TheWaningBorder.Feraldis.HarkonAbilities.Warcry(em, caster, card, dur);
                        break;
                    case AbilityEffectKind.FeraldisLeap:
                        TheWaningBorder.Feraldis.HarkonAbilities.Leap(em, caster);
                        break;
                    case AbilityEffectKind.FeraldisRaze:
                        TheWaningBorder.Feraldis.HarkonAbilities.Raze(em, caster, target);
                        break;
                    case AbilityEffectKind.FeraldisBloodOath:
                        TheWaningBorder.Feraldis.HarkonAbilities.BloodOath(em, caster, card, dur);
                        break;
                    case AbilityEffectKind.CurseVeilOffering:
                        TheWaningBorder.Systems.Border.VeilOffering.Offer(em, caster, card);
                        break;
                    // The Litharch's Cleanse (Territory_Claims.md §6.9.7): plants a
                    // Cleansing Ward; CleanseFalloff / MaxRadius are its parameters.
                    case AbilityEffectKind.CleanseGround:
                        LitharchCleanse.Begin(em, caster, card, e.Value);
                        break;
                    case AbilityEffectKind.FeraldisFieldSacrifice:
                        TheWaningBorder.Feraldis.FeraldisSacrifice.FieldSacrifice(em, caster);
                        break;
                    case AbilityEffectKind.FeraldisRigUnload:
                        TheWaningBorder.Feraldis.WarRig.Unload(em, caster);
                        break;
                    case AbilityEffectKind.FeraldisOvercharge:
                        TheWaningBorder.Feraldis.FeraldisCombatSystems.Overcharge(em, caster);
                        break;

                    // Runai (docs/Design/Age_1_Runai.md §6.1, §8).
                    case AbilityEffectKind.RunaiWayfinding:
                        TheWaningBorder.Runai.RunaiAbilities.Wayfinding(em, caster, card, dur);
                        break;
                    case AbilityEffectKind.RunaiMirage:
                        TheWaningBorder.Runai.RunaiAbilities.Mirage(em, caster, card, dur);
                        break;
                    case AbilityEffectKind.RunaiCaravanOfSwords:
                        TheWaningBorder.Runai.RunaiAbilities.CaravanOfSwords(em, caster, dur);
                        break;
                    case AbilityEffectKind.RunaiExodus:
                        TheWaningBorder.Runai.RunaiAbilities.Exodus(em, caster, card);
                        break;
                    case AbilityEffectKind.RunaiScatter:
                        TheWaningBorder.Runai.RunaiAbilities.Scatter(em, caster, card);
                        break;
                    case AbilityEffectKind.RunaiTheCall:
                        TheWaningBorder.Runai.RunaiTradeSystem.Call(em, caster);
                        break;
                    case AbilityEffectKind.RunaiReturnToWagons:
                        if (em.HasComponent<FactionTag>(caster))
                            TheWaningBorder.Runai.RunaiTradeSystem.DismissCall(em, em.GetComponentData<FactionTag>(caster).Value);
                        break;

                    // The Alanthor sect heroes (docs/Design/Sects.md §4).
                    case AbilityEffectKind.SectStampOfRecord:
                        TheWaningBorder.Systems.Sect.SectHeroAbilities.StampOfRecord(em, caster, card, target);
                        break;
                    case AbilityEffectKind.SectRecall:
                        TheWaningBorder.Systems.Sect.SectHeroAbilities.Recall(em, caster, card);
                        break;
                    case AbilityEffectKind.SectBloodletting:
                        TheWaningBorder.Systems.Sect.SectHeroAbilities.Bloodletting(em, caster, card, e.Value, dur);
                        break;
                    case AbilityEffectKind.SectLancet:
                        TheWaningBorder.Systems.Sect.SectHeroAbilities.Lancet(em, caster, card, dur, target);
                        break;
                    case AbilityEffectKind.SectSallyForth:
                        TheWaningBorder.Systems.Sect.SectHeroAbilities.SallyForth(em, caster, card, dur);
                        break;
                    case AbilityEffectKind.SectChallenge:
                        TheWaningBorder.Systems.Sect.SectHeroAbilities.Challenge(em, caster, card, dur);
                        break;
                    case AbilityEffectKind.SectStake:
                        TheWaningBorder.Systems.Sect.SectHeroAbilities.Stake(em, caster, dur);
                        break;
                    case AbilityEffectKind.SectLiquidate:
                        TheWaningBorder.Systems.Sect.SectHeroAbilities.Liquidate(em, caster, card, e.Value, target);
                        break;
                    // Catalogued, Cauterize, Drillmaster and Called In are
                    // auras — SectHeroAuraSystem and StoredStrikeChargeSystem.
                    case AbilityEffectKind.SectCatalogued:
                    case AbilityEffectKind.SectCauterize:
                    case AbilityEffectKind.SectDrillmaster:
                    case AbilityEffectKind.SectCalledIn:
                        break;

                    case AbilityEffectKind.SummonPledgeArmy:
                        // Everything about the summon is read off the caster's
                        // hero level, so the card carries no numbers for it —
                        // see PledgeArmy and docs/Design/Heroes.md §3.
                        TheWaningBorder.Entities.PledgeArmy.Summon(em, caster);
                        break;

                    // ChargeBonusFlat and LosRampWhileStill are continuous passives
                    // handled by AbilityAuraSystem, not one-shot casts.
                    case AbilityEffectKind.ChargeBonusFlat:
                    case AbilityEffectKind.LosRampWhileStill:
                    case AbilityEffectKind.None:
                    default:
                        break;
                }
            }

            if (touchBuff) TransientState.Set(em, caster, buff);
            if (touchDebuff) AddOrSet(em, caster, debuff);

            // Schedule the aftermath chain (fires after this ability's full duration).
            if (card.Aftermath != null && card.Aftermath.Length > 0)
            {
                int idx = AbilityCatalog.IndexOf(card.Name);
                AddOrSet(em, caster, new AbilityAftermath { AbilityIndex = idx, Remaining = dur, Target = effTarget });
            }
        }

        /// <summary>
        /// Runs <paramref name="act"/> on every same-faction cavalry unit within
        /// <paramref name="radius"/> of the caster (XZ distance, matching the
        /// King's Call aura scan in AbilityAuraSystem). Cavalry is identified by
        /// ArmorType.Cavalry — the same convention the charge mechanic uses.
        /// </summary>
        private static void ApplyToAlliedCavalry(EntityManager em, Entity caster, float radius, System.Action<Entity> act)
        {
            if (radius <= 0f || !em.HasComponent<LocalTransform>(caster) || !em.HasComponent<FactionTag>(caster)) return;

            float3 srcPos = em.GetComponentData<LocalTransform>(caster).Position;
            Faction srcFac = em.GetComponentData<FactionTag>(caster).Value;
            float radSq = radius * radius;

            var query = QC_UnitTagFactionTagArmorTypeDataLocalTransform.Get(em, QT_UnitTagFactionTagArmorTypeDataLocalTransform);
            using var units = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                // Allied cavalry includes team allies. docs/Design/Teams.md
                if (!Alliances.AreAllied(srcFac, em.GetComponentData<FactionTag>(u).Value)) continue;
                if (em.GetComponentData<ArmorTypeData>(u).Value != ArmorType.Cavalry) continue;

                float3 p = em.GetComponentData<LocalTransform>(u).Position;
                float2 d = new float2(p.x - srcPos.x, p.z - srcPos.z);
                if (math.dot(d, d) > radSq) continue;

                act(u);
            }
        }

        /// <summary>
        /// Runs <paramref name="act"/> on every allied RANGED unit within
        /// <paramref name="radius"/> of the caster. The mirror of
        /// ApplyToAlliedCavalry, selecting on DamageType.Ranged — the same test
        /// the ranged damage ladder uses, so an ability and a tech can never
        /// disagree about what counts as a ranged unit.
        /// </summary>
        private static void ApplyToAlliedRanged(EntityManager em, Entity caster, float radius, System.Action<Entity> act)
        {
            if (radius <= 0f || !em.HasComponent<LocalTransform>(caster) || !em.HasComponent<FactionTag>(caster)) return;

            float3 srcPos = em.GetComponentData<LocalTransform>(caster).Position;
            Faction srcFac = em.GetComponentData<FactionTag>(caster).Value;
            float radSq = radius * radius;

            var query = QC_UnitTagFactionTagDamageTypeDataLocalTransform.Get(
                em, QT_UnitTagFactionTagDamageTypeDataLocalTransform);
            using var units = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                // Team allies included, per docs/Design/Teams.md.
                if (!Alliances.AreAllied(srcFac, em.GetComponentData<FactionTag>(u).Value)) continue;
                if (em.GetComponentData<DamageTypeData>(u).Value != DamageType.Ranged) continue;

                float3 p = em.GetComponentData<LocalTransform>(u).Position;
                float2 d = new float2(p.x - srcPos.x, p.z - srcPos.z);
                if (math.dot(d, d) > radSq) continue;

                act(u);
            }
        }

        private static void SpawnFogReveal(EntityManager em, Entity caster, Entity target, float radius, float dur)
        {
            float3 pos;
            // The player's AIMED point wins. Without this an Area ability could
            // only ever centre on an entity, so Use Celestar revealed around the
            // scout that cast it — useless for scouting somewhere you can't see.
            if (em.HasComponent<AbilityAimPoint>(caster))
                pos = em.GetComponentData<AbilityAimPoint>(caster).Position;
            else if (target != Entity.Null && em.Exists(target) && em.HasComponent<LocalTransform>(target))
                pos = em.GetComponentData<LocalTransform>(target).Position;
            else if (em.HasComponent<LocalTransform>(caster))
                pos = em.GetComponentData<LocalTransform>(caster).Position;
            else pos = float3.zero;

            Faction fac = em.HasComponent<FactionTag>(caster) ? em.GetComponentData<FactionTag>(caster).Value : default;

            // Reuse the existing sect reveal power (RevealCircle) — same mechanism the
            // Reliquary Scry/Vision abilities use. No max-range check on the aim point.
            TheWaningBorder.Systems.Sect.SectActivePowerHelper.SpawnReveal(em, fac, pos, radius, dur);
        }

        // Every component this executor stamps is an enableable transient,
        // pre-added disabled on units (TransientState.cs): activating one is a
        // bit flip, never an add. Buildings (AutoYieldBoost/UnderAutomation)
        // gain the component once and toggle it from then on.
        private static void AddOrSet<T>(EntityManager em, Entity e, T value)
            where T : unmanaged, IComponentData, IEnableableComponent
            => TransientState.Set(em, e, value);
    }
}
