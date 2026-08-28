// FeraldisLowHpRewardSystem.cs
// Feraldis battle economy — spec §3.1: "Earned by damaging enemy
// buildings below 25% HP." The existing PillageSystem already handles
// kill-reward (units + buildings); this system covers the niche where
// the building is BEING damaged below the threshold but not (yet) killed.
//
// Each frame:
//   - Snapshot per-building LastObservedHealth (stored in a transient
//     BuildingHpSnapshot component, added lazily on first observation).
//   - When Health drops AND the building is at < 25% of Max AND the
//     LastDamagedByFaction is a Feraldis-culture faction, credit that
//     faction with Supplies proportional to the HP lost.
//
// Empty / max-HP buildings just resync the snapshot.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Economy;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Systems.Economy
{

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class FeraldisLowHpRewardSystem : SystemBase
    {
        /// <summary>Building HP fraction below which the reward fires (spec §3.1).</summary>
        private const float LowHpFraction = 0.25f;

        /// <summary>Supplies credited per HP of damage dealt while below the threshold.</summary>
        private const float SuppliesPerHp = 0.5f;

        protected override void OnUpdate()
        {
            var em = EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // ── Phase 1: lazy-add snapshot to any building lacking one. ──
            foreach (var (health, entity) in SystemAPI
                .Query<RefRO<Health>>()
                .WithAll<BuildingTag>()
                .WithNone<BuildingHpSnapshot>()
                .WithEntityAccess())
            {
                ecb.AddComponent(entity, new BuildingHpSnapshot { LastObservedHealth = health.ValueRO.Value });
            }
            ecb.Playback(em);
            ecb.Dispose();

            // ── Phase 2: diff + reward. ──
            // Build a Faction → culture lookup once (Halls carry FactionProgress).
            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var hallTags = hallQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hallProgress = hallQuery.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            var cultureOf = new NativeHashMap<byte, byte>(8, Allocator.Temp);
            for (int i = 0; i < hallTags.Length; i++)
            {
                byte k = (byte)hallTags[i].Value;
                if (!cultureOf.ContainsKey(k)) cultureOf.Add(k, hallProgress[i].Culture);
            }

            foreach (var (snapRW, health, lastDamager, faction, entity) in SystemAPI
                .Query<RefRW<BuildingHpSnapshot>, RefRO<Health>, RefRO<LastDamagedByFaction>, RefRO<FactionTag>>()
                .WithAll<BuildingTag>()
                .WithEntityAccess())
            {
                int prev = snapRW.ValueRO.LastObservedHealth;
                int now = health.ValueRO.Value;
                int delta = prev - now;
                snapRW.ValueRW.LastObservedHealth = now;

                if (delta <= 0) continue;
                if (now <= 0) continue;  // destroy — PillageSystem owns that reward
                if (health.ValueRO.Max <= 0) continue;

                bool wasLow = (float)prev / health.ValueRO.Max < LowHpFraction;
                bool isLow = (float)now / health.ValueRO.Max < LowHpFraction;
                if (!isLow && !wasLow) continue;

                // Damage attributable to the low-HP window only — clip the
                // portion of `delta` that occurred above 25% HP.
                int lowHpThreshold = (int)(health.ValueRO.Max * LowHpFraction);
                int damageInWindow = math.min(delta, math.max(0, lowHpThreshold - now));
                if (damageInWindow <= 0) damageInWindow = delta;  // fully inside window

                // Identify the killer's culture. Only Feraldis-aligned kills earn the reward.
                Faction killerFaction = lastDamager.ValueRO.Value;
                if (killerFaction == faction.ValueRO.Value) continue;  // self-damage
                if (!cultureOf.TryGetValue((byte)killerFaction, out byte culture)) continue;
                if (culture != Cultures.Feraldis) continue;

                int supplies = (int)math.max(1, damageInWindow * SuppliesPerHp);
                FactionEconomy.Add(em, killerFaction, Cost.Of(supplies: supplies));
                TWBLog.Log($"[FeraldisLowHp] {killerFaction} earned {supplies} Supplies from low-HP damage");
            }

            cultureOf.Dispose();
        }
    }
}
