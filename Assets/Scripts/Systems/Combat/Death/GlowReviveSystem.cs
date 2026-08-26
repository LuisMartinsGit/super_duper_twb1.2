// GlowReviveSystem.cs
// Glow-tier on-death revive (spec §4.2 Glow tier: "Revive on cooldown").
//
// Per-unit GlowReviveCooldown ticks down each frame. When a Glow-tier
// non-Border unit reaches 0 HP AND the cooldown is ready, it pops back
// at GlowReviveHealthPercent of max with a refreshed shield bar; the
// cooldown resets to GlowReviveCooldownSec. While the cooldown is on,
// lethal damage falls through to DeathSystem + GlowWeaponDropSystem
// normally — the recurring revive is a window of opportunity, not a
// permanent invulnerability.
//
// Runs UpdateBefore(GlowWeaponDropSystem) so the revive takes precedence
// over the drop/destroy path. After revival, Health.Value > 0 again, so
// both downstream systems skip the unit.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Systems.Combat
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EquipmentTierSystem))]
    [UpdateBefore(typeof(GlowWeaponDropSystem))]
    public partial class GlowReviveSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // ── Phase 1: ensure Glow-tier units carry GlowReviveCooldown, tick down. ──
            foreach (var (applied, faction, entity) in SystemAPI
                .Query<RefRO<UnitEquipmentApplied>, RefRO<FactionTag>>()
                .WithEntityAccess())
            {
                bool isGlow = applied.ValueRO.Value == EquipmentTier.Glow
                    && faction.ValueRO.Value != Faction.Border;
                bool hasCd = em.HasComponent<GlowReviveCooldown>(entity);

                if (isGlow && !hasCd)
                {
                    // Stamp ready-to-revive on first observation.
                    ecb.AddComponent(entity, new GlowReviveCooldown { TimeRemaining = 0f });
                }
                else if (!isGlow && hasCd)
                {
                    // Lost the Glow tier (e.g. via UnitTierOverride downgrade) — strip cooldown.
                    ecb.RemoveComponent<GlowReviveCooldown>(entity);
                }
            }

            foreach (var cdRW in SystemAPI.Query<RefRW<GlowReviveCooldown>>())
            {
                if (cdRW.ValueRO.TimeRemaining > 0f)
                    cdRW.ValueRW.TimeRemaining = math.max(0f, cdRW.ValueRO.TimeRemaining - dt);
            }

            // ── Phase 2: fire revive on Health <= 0 if cooldown is ready. ──
            foreach (var (healthRW, applied, faction, cdRW, entity) in SystemAPI
                .Query<RefRW<Health>, RefRO<UnitEquipmentApplied>, RefRO<FactionTag>, RefRW<GlowReviveCooldown>>()
                .WithEntityAccess())
            {
                if (healthRW.ValueRO.Value > 0) continue;
                if (applied.ValueRO.Value != EquipmentTier.Glow) continue;
                if (faction.ValueRO.Value == Faction.Border) continue;
                if (cdRW.ValueRO.TimeRemaining > 0f) continue;

                int revived = (int)math.max(1, healthRW.ValueRO.Max * EquipmentTierConfig.GlowReviveHealthPercent);
                healthRW.ValueRW.Value = revived;
                cdRW.ValueRW.TimeRemaining = EquipmentTierConfig.GlowReviveCooldownSec;

                if (em.HasComponent<ShieldBar>(entity))
                {
                    var sb = em.GetComponentData<ShieldBar>(entity);
                    sb.Current = sb.Max;
                    sb.LastObservedHealth = revived;
                    sb.RegenDelayTimer = 0f;
                    em.SetComponentData(entity, sb);
                }

                TWBLog.Log($"[GlowRevive] {faction.ValueRO.Value} Glow unit revived at {revived}/{healthRW.ValueRO.Max} HP — cooldown {EquipmentTierConfig.GlowReviveCooldownSec:F0}s");
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
