// AbilityEffectExecutor.cs
// Translates an AbilityCard's structured effects into concrete ECS buff
// components on a target. Shared by the active-cast path and the aftermath
// chain so a "cast" is one call.
//
// Reuses the existing SpellBuff / SpellDebuff channels (read live by combat &
// movement) plus the ability-specific components in AbilityRuntimeComponents.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Abilities
{
    public static class AbilityEffectExecutor
    {
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

            // Accumulate the SpellBuff/SpellDebuff so multiple stat effects on one
            // ability produce a single component.
            var buff = em.HasComponent<SpellBuff>(caster) ? em.GetComponentData<SpellBuff>(caster) : default;
            var debuff = em.HasComponent<SpellDebuff>(caster) ? em.GetComponentData<SpellDebuff>(caster) : default;
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
                        // Applied as a flat armor bonus (placeholder scaling — tune later).
                        buff.ArmorBonus += e.Value; // e.g. +15
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
                                    var b = em.HasComponent<SpellBuff>(u) ? em.GetComponentData<SpellBuff>(u) : default;
                                    b.SpeedMultiplier = math.max(b.SpeedMultiplier, 1f + spd / 100f);
                                    b.TimeRemaining = math.max(b.TimeRemaining, dur);
                                    AddOrSet(em, u, b);
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
                            TheWaningBorder.Entities.FieldHospital.Create(em, hpPos, hpFac);
                        }
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

            if (touchBuff) AddOrSet(em, caster, buff);
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

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<ArmorTypeData>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var units = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                if (em.GetComponentData<FactionTag>(u).Value != srcFac) continue;
                if (em.GetComponentData<ArmorTypeData>(u).Value != ArmorType.Cavalry) continue;

                float3 p = em.GetComponentData<LocalTransform>(u).Position;
                float2 d = new float2(p.x - srcPos.x, p.z - srcPos.z);
                if (math.dot(d, d) > radSq) continue;

                act(u);
            }
        }

        private static void SpawnFogReveal(EntityManager em, Entity caster, Entity target, float radius, float dur)
        {
            float3 pos;
            if (target != Entity.Null && em.Exists(target) && em.HasComponent<LocalTransform>(target))
                pos = em.GetComponentData<LocalTransform>(target).Position;
            else if (em.HasComponent<LocalTransform>(caster))
                pos = em.GetComponentData<LocalTransform>(caster).Position;
            else pos = float3.zero;

            Faction fac = em.HasComponent<FactionTag>(caster) ? em.GetComponentData<FactionTag>(caster).Value : default;

            // Reuse the existing sect reveal power (RevealCircle) — same mechanism the
            // Reliquary Scry/Vision abilities use. No max-range check on the aim point.
            TheWaningBorder.Systems.Sect.SectActivePowerHelper.SpawnReveal(em, fac, pos, radius, dur);
        }

        private static void AddOrSet<T>(EntityManager em, Entity e, T value) where T : unmanaged, IComponentData
        {
            if (em.HasComponent<T>(e)) em.SetComponentData(e, value);
            else em.AddComponentData(e, value);
        }
    }
}
