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
//
// Location: Assets/Scripts/Systems/Economy/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using static TheWaningBorder.Core.Config.CrystalConstants;

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

                Debug.Log($"[GodPower] {pending.ValueRO.Caster} cast — {storedGlow} stored Glow → " +
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
                    ApplyAoe(em, victimEnts, victimTransforms, victimHealth, victimFactions,
                             pendingTargets[p], pendingCasters[p]);
                    em.RemoveComponent<PendingGodPowerCast>(pendingEnts[p]);
                }
            }

            pendingEnts.Dispose();
            pendingCasters.Dispose();
            pendingTargets.Dispose();
            glowByFaction.Dispose();
        }

        private static void ApplyAoe(EntityManager em,
            NativeArray<Entity> ents, NativeArray<LocalTransform> transforms,
            NativeArray<Health> healths, NativeArray<FactionTag> factions,
            float3 center, Faction caster)
        {
            int hit = 0;
            for (int v = 0; v < ents.Length; v++)
            {
                if (factions[v].Value == caster) continue;  // friendly fire off
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
            Debug.Log($"[GodPower] AOE at ({center.x:F0},{center.z:F0}) hit {hit} non-{caster} targets");
        }
    }
}
