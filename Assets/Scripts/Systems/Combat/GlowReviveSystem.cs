// GlowReviveSystem.cs
// Glow-tier on-death revive (spec §4.2 Glow tier). When a Glow-tier unit
// reaches 0 HP for the first time, it pops back up with a fraction of its
// max HP intact and gets a GlowReviveUsed tag so the revive can't fire
// twice. Subsequent deaths fall through to DeathSystem + GlowWeaponDropSystem.
//
// Runs UpdateBefore(GlowWeaponDropSystem) and UpdateBefore(DeathSystem) so
// the revive takes precedence over the drop / destroy path. After revival,
// Health.Value > 0 again, so both downstream systems skip the unit.
//
// Curse units don't revive — Glow tier is a player-side equipment concept.
//
// Location: Assets/Scripts/Systems/Combat/

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
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            foreach (var (healthRW, applied, faction, entity) in SystemAPI
                .Query<RefRW<Health>, RefRO<UnitEquipmentApplied>, RefRO<FactionTag>>()
                .WithNone<GlowReviveUsed>()
                .WithEntityAccess())
            {
                if (healthRW.ValueRO.Value > 0) continue;
                if (applied.ValueRO.Value != EquipmentTier.Glow) continue;
                if (faction.ValueRO.Value == Faction.Curse) continue;

                int revived = (int)math.max(1, healthRW.ValueRO.Max * EquipmentTierConfig.GlowReviveHealthPercent);
                healthRW.ValueRW.Value = revived;
                ecb.AddComponent<GlowReviveUsed>(entity);

                // Refresh shield bar on revive so the next blow doesn't kill
                // through a depleted shield.
                if (em.HasComponent<ShieldBar>(entity))
                {
                    var sb = em.GetComponentData<ShieldBar>(entity);
                    sb.Current = sb.Max;
                    sb.LastObservedHealth = revived;
                    sb.RegenDelayTimer = 0f;
                    em.SetComponentData(entity, sb);
                }

                Debug.Log($"[GlowRevive] {faction.ValueRO.Value} Glow-tier unit revived at {revived}/{healthRW.ValueRO.Max} HP");
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
