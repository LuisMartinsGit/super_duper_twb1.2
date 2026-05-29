// GlowFlowSystem.cs
// End-to-end Glow pickup chain (spec §5.1 + §6.3):
//   1. GlowPickup despawn after timeout
//   2. Unit walks over a GlowPickup → GlowCarrier transfers the amount
//   3. GlowCarrier walks near an owned Reliquary → deposit into reliquary
//   4. Reliquary flushes stored amount into faction bank
//   5. Carrier dies → respawn a GlowPickup at the death position (interception)
//
// Carrier-death runs UpdateBefore(DeathSystem) so we can catch the unit
// at HP <= 0 before the entity is queued for destruction.
//
// Spec §5.1: "Glow pickup can be intercepted in transit by any faction."
// (any unit can pick up, regardless of faction).
//
// Location: Assets/Scripts/Systems/Economy/

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class GlowFlowSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;

            // ── Phase 1: pickup-timer despawn ──────────────────────────
            var expiredPickups = new NativeList<Entity>(2, Allocator.Temp);
            foreach (var (state, entity) in SystemAPI
                .Query<RefRW<GlowPickupState>>()
                .WithAll<GlowPickupTag>()
                .WithEntityAccess())
            {
                state.ValueRW.TimeRemaining -= dt;
                if (state.ValueRO.TimeRemaining <= 0f)
                    expiredPickups.Add(entity);
            }
            for (int i = 0; i < expiredPickups.Length; i++)
            {
                TWBLog.Log($"[Glow] pickup despawned (timeout)");
                em.DestroyEntity(expiredPickups[i]);
            }
            expiredPickups.Dispose();

            // ── Phase 2: attunement claim (spec refinement #4) ─────────
            // 20-second visible attunement — no instant-on-touch claim.
            // First non-Curse unit in range becomes the Attuner. If they
            // move out of range, die, or change faction, AttunementProgress
            // resets and another in-range unit can take over (fight-over-
            // loot). On completion, transfer to GlowCarrier + destroy pickup.
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Snapshot units so the per-pickup loop doesn't re-query.
            var unitSnapshotQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var unitEnts = unitSnapshotQuery.ToEntityArray(Allocator.Temp);
            using var unitFactions = unitSnapshotQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitTransforms = unitSnapshotQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var unitHealths = unitSnapshotQuery.ToComponentDataArray<Health>(Allocator.Temp);

            var claimedPickups = new NativeList<Entity>(2, Allocator.Temp);
            var claimers = new NativeList<Entity>(2, Allocator.Temp);
            var claimedAmounts = new NativeList<int>(2, Allocator.Temp);
            var claimedSources = new NativeList<RitualKind>(2, Allocator.Temp);

            foreach (var (stateRW, pickupTransform, pickupEntity) in SystemAPI
                .Query<RefRW<GlowPickupState>, RefRO<LocalTransform>>()
                .WithAll<GlowPickupTag>()
                .WithEntityAccess())
            {
                ref var state = ref stateRW.ValueRW;
                var pickupPos = pickupTransform.ValueRO.Position;

                // Validate current attuner — still in range, alive, non-Curse?
                bool attunerValid = false;
                if (state.Attuner != Entity.Null && em.Exists(state.Attuner))
                {
                    if (em.HasComponent<Health>(state.Attuner)
                        && em.GetComponentData<Health>(state.Attuner).Value > 0
                        && em.HasComponent<FactionTag>(state.Attuner)
                        && em.GetComponentData<FactionTag>(state.Attuner).Value != Faction.Curse
                        && em.HasComponent<LocalTransform>(state.Attuner))
                    {
                        var aPos = em.GetComponentData<LocalTransform>(state.Attuner).Position;
                        float dxz = math.distance(
                            new float2(aPos.x, aPos.z),
                            new float2(pickupPos.x, pickupPos.z));
                        if (dxz <= GlowAutoPickupRadius) attunerValid = true;
                    }
                }
                if (!attunerValid)
                {
                    state.Attuner = Entity.Null;
                    state.AttunementProgress = 0f;
                }

                // Find new attuner if none. First valid unit in the snapshot wins.
                if (state.Attuner == Entity.Null)
                {
                    for (int i = 0; i < unitEnts.Length; i++)
                    {
                        if (unitHealths[i].Value <= 0) continue;
                        if (unitFactions[i].Value == Faction.Curse) continue;

                        var uPos = unitTransforms[i].Position;
                        float dxz = math.distance(
                            new float2(uPos.x, uPos.z),
                            new float2(pickupPos.x, pickupPos.z));
                        if (dxz > GlowAutoPickupRadius) continue;

                        state.Attuner = unitEnts[i];
                        state.AttunementProgress = 0f;
                        TWBLog.Log($"[Glow] {unitFactions[i].Value} unit begins attuning ({GlowPickupAttunementTime:F0}s)");
                        break;
                    }
                }

                // Tick attunement.
                if (state.Attuner != Entity.Null)
                {
                    state.AttunementProgress += dt;
                    if (state.AttunementProgress >= GlowPickupAttunementTime)
                    {
                        claimedPickups.Add(pickupEntity);
                        claimers.Add(state.Attuner);
                        claimedAmounts.Add(state.Amount);
                        claimedSources.Add(state.Source);
                    }
                }
            }

            // Apply claims after the loop.
            for (int i = 0; i < claimedPickups.Length; i++)
            {
                Entity unit = claimers[i];
                int amount = claimedAmounts[i];
                RitualKind src = claimedSources[i];

                if (em.Exists(unit))
                {
                    int existing = 0;
                    RitualKind keepSrc = src;
                    if (em.HasComponent<GlowCarrier>(unit))
                    {
                        var car = em.GetComponentData<GlowCarrier>(unit);
                        existing = car.Amount;
                        keepSrc = car.Source; // first ritual wins for the source label
                    }
                    var merged = new GlowCarrier { Amount = existing + amount, Source = keepSrc };
                    if (em.HasComponent<GlowCarrier>(unit))
                        em.SetComponentData(unit, merged);
                    else
                        em.AddComponentData(unit, merged);

                    Faction f = em.HasComponent<FactionTag>(unit)
                        ? em.GetComponentData<FactionTag>(unit).Value : Faction.Blue;
                    TWBLog.Log($"[Glow] {f} attunement complete — picked up {amount} Glow (carrying {merged.Amount})");
                }
                if (em.Exists(claimedPickups[i]))
                    em.DestroyEntity(claimedPickups[i]);
            }
            claimedPickups.Dispose();
            claimers.Dispose();
            claimedAmounts.Dispose();
            claimedSources.Dispose();

            // ── Phase 3: carrier-near-temple → deposit ─────────────────
            // Spec refinement #2: Glow is stored on TempleOfRidan, not on a
            // standalone reliquary. The stockpile stays in the Temple — it
            // does NOT flush into the faction bank. Stored Glow is consumed
            // directly by spending paths (Glow weapon upgrades, god powers
            // when those reach refinement #6 wiring).
            var templeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var templeEnts = templeQuery.ToEntityArray(Allocator.Temp);
            using var templeFactions = templeQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var templeTransforms = templeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var templeHealth = templeQuery.ToComponentDataArray<Health>(Allocator.Temp);

            if (templeEnts.Length > 0)
            {
                foreach (var (carrierRW, unitTransform, unitFaction, unitHealth, unitEntity) in SystemAPI
                    .Query<RefRW<GlowCarrier>, RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<Health>>()
                    .WithEntityAccess())
                {
                    if (unitHealth.ValueRO.Value <= 0) continue;
                    if (carrierRW.ValueRO.Amount <= 0) continue;

                    Faction f = unitFaction.ValueRO.Value;
                    var unitPos = unitTransform.ValueRO.Position;

                    for (int i = 0; i < templeEnts.Length; i++)
                    {
                        if (templeFactions[i].Value != f) continue;
                        if (templeHealth[i].Value <= 0) continue;
                        // Skip temples still under construction.
                        if (em.HasComponent<UnderConstruction>(templeEnts[i])) continue;
                        // Temple created via the older factory path may not yet
                        // carry GlowStored — skip gracefully rather than crash.
                        if (!em.HasComponent<GlowStored>(templeEnts[i])) continue;

                        var dxz = math.distance(
                            new float2(unitPos.x, unitPos.z),
                            new float2(templeTransforms[i].Position.x, templeTransforms[i].Position.z));
                        if (dxz > GlowAutoDepositRadius) continue;

                        int delivered = carrierRW.ValueRO.Amount;
                        var stored = em.GetComponentData<GlowStored>(templeEnts[i]);
                        stored.Amount += delivered;
                        em.SetComponentData(templeEnts[i], stored);

                        carrierRW.ValueRW.Amount = 0;
                        ecb.RemoveComponent<GlowCarrier>(unitEntity);

                        TWBLog.Log($"[Glow] {f} deposited {delivered} Glow at Temple of Ridan (stored: {stored.Amount})");
                        break;
                    }
                }
            }

            // ── Phase 5: carrier-dies → respawn pickup ─────────────────
            // Runs before DeathSystem so the carrier still exists. DeathSystem
            // will then process its death normally on the next frame. We add
            // a guard component so we only drop the pickup once per carrier.
            var dropList = new NativeList<Entity>(2, Allocator.Temp);
            var dropPositions = new NativeList<float3>(2, Allocator.Temp);
            var dropAmounts = new NativeList<int>(2, Allocator.Temp);
            var dropSources = new NativeList<RitualKind>(2, Allocator.Temp);

            foreach (var (carrier, health, transform, entity) in SystemAPI
                .Query<RefRO<GlowCarrier>, RefRO<Health>, RefRO<LocalTransform>>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;
                if (carrier.ValueRO.Amount <= 0) continue;
                dropList.Add(entity);
                dropPositions.Add(transform.ValueRO.Position);
                dropAmounts.Add(carrier.ValueRO.Amount);
                dropSources.Add(carrier.ValueRO.Source);
            }
            for (int i = 0; i < dropList.Length; i++)
            {
                GlowPickup.Create(em, dropPositions[i], dropSources[i], dropAmounts[i]);
                if (em.HasComponent<GlowCarrier>(dropList[i]))
                    em.RemoveComponent<GlowCarrier>(dropList[i]);
                TWBLog.Log($"[Glow] carrier died — dropped {dropAmounts[i]} Glow at pickup");
            }
            dropList.Dispose();
            dropPositions.Dispose();
            dropAmounts.Dispose();
            dropSources.Dispose();

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
