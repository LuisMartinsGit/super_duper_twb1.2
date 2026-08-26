// GodPowerSystem.cs
// Resolves god-power casts (spec §6.2 + refinement #6).
//   - Tick: counts down CooldownRemaining for every faction bank.
//   - Cast: when a bank carries PendingGodPowerCast, read the caster's
//           Temple-of-Ridan GlowStored to compute the post-cast cooldown:
//             new_cooldown = BaseCooldown × 0.8^stored_glow
//           Apply AOE damage at TargetPosition, set CooldownRemaining,
//           increment CastCount, remove the pending component.
//
// Glow stays in the Temple (refinement #6): the cast does NOT deduct
// stored Glow. Storing more compresses cooldown asymptotically, but
// losing the Temple wipes the discount.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class GodPowerSystem : SystemBase
    {
        private EntityQuery _victimQuery;
        private EntityQuery _templeQuery;

        protected override void OnCreate()
        {
            _victimQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<FactionTag>()
            );
            _templeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<GlowStored>()
            );
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;

            // ── Phase 1: tick cooldowns ─────────────────────────────────
            foreach (var gpsRW in SystemAPI.Query<RefRW<GodPowerState>>())
            {
                if (gpsRW.ValueRO.CooldownRemaining > 0f)
                    gpsRW.ValueRW.CooldownRemaining = math.max(0f,
                        gpsRW.ValueRO.CooldownRemaining - dt);
            }

            // ── Phase 2: resolve pending casts ──────────────────────────
            // Build a Faction → stored-Glow lookup so the per-cast read
            // doesn't iterate temples for every bank.
            using var templeFactions = _templeQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var templeStored = _templeQuery.ToComponentDataArray<GlowStored>(Allocator.Temp);
            var glowByFaction = new NativeHashMap<byte, int>(8, Allocator.Temp);
            for (int i = 0; i < templeFactions.Length; i++)
            {
                byte k = (byte)templeFactions[i].Value;
                int prior = glowByFaction.ContainsKey(k) ? glowByFaction[k] : 0;
                glowByFaction[k] = prior + templeStored[i].Amount;
            }

            var pendingEnts = new NativeList<Entity>(2, Allocator.Temp);
            var pendingCasters = new NativeList<Faction>(2, Allocator.Temp);
            var pendingTargets = new NativeList<float3>(2, Allocator.Temp);

            foreach (var (pending, gpsRW, entity) in SystemAPI
                .Query<RefRO<PendingGodPowerCast>, RefRW<GodPowerState>>()
                .WithEntityAccess())
            {
                if (gpsRW.ValueRO.CooldownRemaining > 0f) continue;

                int storedGlow = glowByFaction.ContainsKey((byte)pending.ValueRO.Caster)
                    ? glowByFaction[(byte)pending.ValueRO.Caster]
                    : 0;

                float multiplier = math.pow(GodPowerCooldownPerGlow, storedGlow);
                float newCooldown = gpsRW.ValueRO.BaseCooldown * multiplier;

                gpsRW.ValueRW.CooldownRemaining = newCooldown;
                gpsRW.ValueRW.CastCount += 1;

                TWBLog.Log($"[GodPower] {pending.ValueRO.Caster} cast — {storedGlow} stored Glow → " +
                          $"cooldown {newCooldown:F1}s (×{multiplier:F2} of {gpsRW.ValueRO.BaseCooldown:F0}s)");

                pendingEnts.Add(entity);
                pendingCasters.Add(pending.ValueRO.Caster);
                pendingTargets.Add(pending.ValueRO.TargetPosition);
            }

            if (pendingEnts.Length > 0)
            {
                // Snapshot victims once for the AOE pass.
                using var victimEnts = _victimQuery.ToEntityArray(Allocator.Temp);
                using var victimTransforms = _victimQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                using var victimHealth = _victimQuery.ToComponentDataArray<Health>(Allocator.Temp);
                using var victimFactions = _victimQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

                for (int p = 0; p < pendingEnts.Length; p++)
                {
                    // Spec §6.4: faction-bias variants. Branch on the caster's
                    // culture rather than hardcoding a single effect.
                    byte casterCulture = CultureOf(em, pendingCasters[p]);
                    int storedGlow = glowByFaction.ContainsKey((byte)pendingCasters[p])
                        ? glowByFaction[(byte)pendingCasters[p]] : 0;

                    switch (casterCulture)
                    {
                        case Cultures.Alanthor:
                            ApplyAlanthorSanctify(em, victimEnts, victimTransforms,
                                victimHealth, victimFactions, pendingTargets[p],
                                pendingCasters[p], storedGlow);
                            break;
                        case Cultures.Feraldis:
                            ApplyFeraldisPyre(em, victimEnts, victimTransforms,
                                victimHealth, victimFactions, pendingTargets[p],
                                pendingCasters[p], storedGlow);
                            break;
                        case Cultures.Runai:
                            ApplyRunaiVeilWard(em, victimEnts, victimTransforms,
                                victimHealth, victimFactions, pendingTargets[p],
                                pendingCasters[p], storedGlow);
                            break;
                        default:
                            // Pre-culture-commit: generic AOE damage (Timeless Age fallback).
                            ApplyGenericAoeDamage(em, victimEnts, victimTransforms,
                                victimHealth, victimFactions, pendingTargets[p],
                                pendingCasters[p]);
                            break;
                    }
                    em.RemoveComponent<PendingGodPowerCast>(pendingEnts[p]);
                }
            }

            pendingEnts.Dispose();
            pendingCasters.Dispose();
            pendingTargets.Dispose();
            glowByFaction.Dispose();
        }

        /// <summary>Look up a faction's culture via its Hall. Cultures.None pre-age-up.</summary>
        private byte CultureOf(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var prog = q.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (tags[i].Value == faction) return prog[i].Culture;
            return Cultures.None;
        }

        /// <summary>
        /// Alanthor "Sanctify Ground" (spec §6.4 cleansing-themed): heals all
        /// allied units in radius. Heal scales with stored Glow (each Glow
        /// adds +10% heal).
        /// </summary>
        private static void ApplyAlanthorSanctify(EntityManager em,
            NativeArray<Entity> ents, NativeArray<LocalTransform> transforms,
            NativeArray<Health> healths, NativeArray<FactionTag> factions,
            float3 center, Faction caster, int storedGlow)
        {
            int healed = 0;
            int healPerUnit = (int)(GodPowerDamage * 0.6f * (1f + 0.1f * storedGlow));
            for (int v = 0; v < ents.Length; v++)
            {
                if (factions[v].Value != caster) continue;     // allies only
                if (healths[v].Value <= 0) continue;
                if (healths[v].Value >= healths[v].Max) continue;

                var pos = transforms[v].Position;
                float dxz = math.distance(
                    new float2(pos.x, pos.z),
                    new float2(center.x, center.z));
                if (dxz > GodPowerRadius) continue;

                var h = em.GetComponentData<Health>(ents[v]);
                h.Value = math.min(h.Max, h.Value + healPerUnit);
                em.SetComponentData(ents[v], h);
                healed++;
            }
            TWBLog.Log($"[GodPower:Alanthor Sanctify] healed {healed} allies for {healPerUnit} HP each");
        }

        /// <summary>
        /// Feraldis "Pyre of the Forsaken" (spec §6.4 destructive): big AOE
        /// damage to enemies. Damage scales sharply with stored Glow (+15% per).
        /// </summary>
        private static void ApplyFeraldisPyre(EntityManager em,
            NativeArray<Entity> ents, NativeArray<LocalTransform> transforms,
            NativeArray<Health> healths, NativeArray<FactionTag> factions,
            float3 center, Faction caster, int storedGlow)
        {
            int hit = 0;
            int damage = (int)(GodPowerDamage * (1f + 0.15f * storedGlow));
            for (int v = 0; v < ents.Length; v++)
            {
                if (factions[v].Value == caster) continue;     // friendly fire off
                if (healths[v].Value <= 0) continue;

                var pos = transforms[v].Position;
                float dxz = math.distance(
                    new float2(pos.x, pos.z),
                    new float2(center.x, center.z));
                if (dxz > GodPowerRadius) continue;

                float falloff = 1f - (dxz / GodPowerRadius);
                int dealt = (int)math.max(1, damage * falloff);

                var h = em.GetComponentData<Health>(ents[v]);
                h.Value = math.max(0, h.Value - dealt);
                em.SetComponentData(ents[v], h);
                hit++;
            }
            TWBLog.Log($"[GodPower:Feraldis Pyre] hit {hit} non-{caster} targets, {damage} base damage");
        }

        /// <summary>
        /// Runai "Veil Ward" (spec §6.4 map enhancement/passive aura): grants
        /// a temporary SpellBuff (speed + armor) to all allied units in radius.
        /// Buff duration scales with stored Glow (+1.5s per).
        /// </summary>
        private static void ApplyRunaiVeilWard(EntityManager em,
            NativeArray<Entity> ents, NativeArray<LocalTransform> transforms,
            NativeArray<Health> healths, NativeArray<FactionTag> factions,
            float3 center, Faction caster, int storedGlow)
        {
            int buffed = 0;
            float duration = 12f + 1.5f * storedGlow;
            var buff = new SpellBuff
            {
                ArmorBonus = 3f,
                DamageMultiplier = 1f,
                SpeedMultiplier = 1.25f,
                DamageReflect = 0f,
                TimeRemaining = duration,
            };
            for (int v = 0; v < ents.Length; v++)
            {
                if (factions[v].Value != caster) continue;
                if (healths[v].Value <= 0) continue;

                var pos = transforms[v].Position;
                float dxz = math.distance(
                    new float2(pos.x, pos.z),
                    new float2(center.x, center.z));
                if (dxz > GodPowerRadius) continue;

                if (em.HasComponent<SpellBuff>(ents[v]))
                {
                    var existing = em.GetComponentData<SpellBuff>(ents[v]);
                    existing.ArmorBonus = math.max(existing.ArmorBonus, buff.ArmorBonus);
                    existing.SpeedMultiplier = math.max(existing.SpeedMultiplier, buff.SpeedMultiplier);
                    existing.TimeRemaining = math.max(existing.TimeRemaining, buff.TimeRemaining);
                    em.SetComponentData(ents[v], existing);
                }
                else
                {
                    em.AddComponentData(ents[v], buff);
                }
                buffed++;
            }
            TWBLog.Log($"[GodPower:Runai Veil Ward] buffed {buffed} allies for {duration:F1}s");
        }

        /// <summary>Pre-culture-commit fallback: generic AOE damage.</summary>
        private static void ApplyGenericAoeDamage(EntityManager em,
            NativeArray<Entity> ents, NativeArray<LocalTransform> transforms,
            NativeArray<Health> healths, NativeArray<FactionTag> factions,
            float3 center, Faction caster)
        {
            int hit = 0;
            for (int v = 0; v < ents.Length; v++)
            {
                if (factions[v].Value == caster) continue;
                if (healths[v].Value <= 0) continue;

                var pos = transforms[v].Position;
                float dxz = math.distance(
                    new float2(pos.x, pos.z),
                    new float2(center.x, center.z));
                if (dxz > GodPowerRadius) continue;

                float falloff = 1f - (dxz / GodPowerRadius);
                int dealt = (int)math.max(1, GodPowerDamage * falloff);

                var h = em.GetComponentData<Health>(ents[v]);
                h.Value = math.max(0, h.Value - dealt);
                em.SetComponentData(ents[v], h);
                hit++;
            }
            TWBLog.Log($"[GodPower:Generic] hit {hit} non-{caster} targets");
        }
    }
}
