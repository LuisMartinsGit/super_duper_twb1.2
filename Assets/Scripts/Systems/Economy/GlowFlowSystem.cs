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
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;
using static TheWaningBorder.Core.Config.CrystalConstants;
using Cost = TheWaningBorder.Core.Cost;

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
                Debug.Log($"[Glow] pickup despawned (timeout)");
                em.DestroyEntity(expiredPickups[i]);
            }
            expiredPickups.Dispose();

            // ── Phase 2: unit-touches-pickup → transfer ────────────────
            // Snapshot live pickups into temp arrays so the per-unit loop
            // doesn't keep re-querying the EM.
            var pickupQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<GlowPickupTag>(),
                ComponentType.ReadOnly<GlowPickupState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var pickupEnts = pickupQuery.ToEntityArray(Allocator.Temp);
            using var pickupStates = pickupQuery.ToComponentDataArray<GlowPickupState>(Allocator.Temp);
            using var pickupTransforms = pickupQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            // Pickups claimed this tick — don't double-claim.
            var claimed = new NativeHashSet<int>(pickupEnts.Length, Allocator.Temp);

            var ecb = new EntityCommandBuffer(Allocator.Temp);

            if (pickupEnts.Length > 0)
            {
                foreach (var (unitTransform, unitFaction, unitHealth, unitEntity) in SystemAPI
                    .Query<RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<Health>>()
                    .WithAll<UnitTag>()
                    .WithEntityAccess())
                {
                    if (unitHealth.ValueRO.Value <= 0) continue;
                    // Curse units never carry — they're a hazard to pickups,
                    // not a delivery vector. Players + AI only.
                    if (unitFaction.ValueRO.Value == Faction.Curse) continue;

                    var unitPos = unitTransform.ValueRO.Position;
                    for (int i = 0; i < pickupEnts.Length; i++)
                    {
                        if (claimed.Contains(i)) continue;

                        var dxz = math.distance(
                            new float2(unitPos.x, unitPos.z),
                            new float2(pickupTransforms[i].Position.x, pickupTransforms[i].Position.z));
                        if (dxz > GlowAutoPickupRadius) continue;

                        // Transfer the pickup to this unit. If the unit
                        // already carries glow, stack the amount and keep
                        // the older source label (first ritual wins).
                        int existing = 0;
                        RitualKind src = pickupStates[i].Source;
                        if (em.HasComponent<GlowCarrier>(unitEntity))
                        {
                            var car = em.GetComponentData<GlowCarrier>(unitEntity);
                            existing = car.Amount;
                            src = car.Source;
                        }
                        var merged = new GlowCarrier
                        {
                            Amount = existing + pickupStates[i].Amount,
                            Source = src,
                        };
                        if (em.HasComponent<GlowCarrier>(unitEntity))
                            em.SetComponentData(unitEntity, merged);
                        else
                            ecb.AddComponent(unitEntity, merged);

                        ecb.DestroyEntity(pickupEnts[i]);
                        claimed.Add(i);
                        Debug.Log($"[Glow] {unitFaction.ValueRO.Value} unit picked up {pickupStates[i].Amount} Glow (carrying {merged.Amount})");
                        break; // one pickup per unit per tick
                    }
                }
            }
            claimed.Dispose();

            // ── Phase 3: carrier-near-reliquary → deposit ──────────────
            // Snapshot reliquaries.
            var reliquaryQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<GlowReliquaryTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var relEnts = reliquaryQuery.ToEntityArray(Allocator.Temp);
            using var relFactions = reliquaryQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var relTransforms = reliquaryQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var relHealth = reliquaryQuery.ToComponentDataArray<Health>(Allocator.Temp);

            if (relEnts.Length > 0)
            {
                foreach (var (carrierRW, unitTransform, unitFaction, unitHealth, unitEntity) in SystemAPI
                    .Query<RefRW<GlowCarrier>, RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<Health>>()
                    .WithEntityAccess())
                {
                    if (unitHealth.ValueRO.Value <= 0) continue;
                    if (carrierRW.ValueRO.Amount <= 0) continue;

                    Faction f = unitFaction.ValueRO.Value;
                    var unitPos = unitTransform.ValueRO.Position;

                    for (int i = 0; i < relEnts.Length; i++)
                    {
                        if (relFactions[i].Value != f) continue;
                        if (relHealth[i].Value <= 0) continue;
                        // Skip reliquaries still under construction.
                        if (em.HasComponent<UnderConstruction>(relEnts[i])) continue;

                        var dxz = math.distance(
                            new float2(unitPos.x, unitPos.z),
                            new float2(relTransforms[i].Position.x, relTransforms[i].Position.z));
                        if (dxz > GlowAutoDepositRadius) continue;

                        int delivered = carrierRW.ValueRO.Amount;
                        var stored = em.GetComponentData<GlowReliquaryStored>(relEnts[i]);
                        stored.Amount += delivered;
                        em.SetComponentData(relEnts[i], stored);

                        carrierRW.ValueRW.Amount = 0;
                        ecb.RemoveComponent<GlowCarrier>(unitEntity);

                        Debug.Log($"[Glow] {f} deposited {delivered} Glow into reliquary (now stored: {stored.Amount})");
                        break;
                    }
                }
            }

            // ── Phase 4: reliquary flush → faction bank ────────────────
            // Reliquaries hold Glow until destroyed; the spec implies the
            // glow IS the stockpile (not the bank), so the explode-on-death
            // gate works. But for the player to actually USE the Glow, we
            // flush 1 unit per second from stored → bank. (Tuning knob.)
            foreach (var (storedRW, faction, health, entity) in SystemAPI
                .Query<RefRW<GlowReliquaryStored>, RefRO<FactionTag>, RefRO<Health>>()
                .WithAll<GlowReliquaryTag>()
                .WithEntityAccess())
            {
                if (storedRW.ValueRO.Amount <= 0) continue;
                if (health.ValueRO.Value <= 0) continue;
                if (em.HasComponent<UnderConstruction>(entity)) continue;

                // Flush 1 per second. Simpler than a per-frame fractional
                // accumulator; uses the floor of (dt * rate) per tick.
                // At 60fps with dt ≈ 0.0167s, 1*dt = 0.0167 → cast to int = 0
                // most frames. We accumulate by stamping a fractional helper.
                // Simplest: only flush every full second using ElapsedTime.
                int wholeSecond = (int)SystemAPI.Time.ElapsedTime;
                if (em.HasComponent<GlowFlushTimer>(entity))
                {
                    var t = em.GetComponentData<GlowFlushTimer>(entity);
                    if (t.LastFlushSecond == wholeSecond) continue;
                    t.LastFlushSecond = wholeSecond;
                    em.SetComponentData(entity, t);
                }
                else
                {
                    ecb.AddComponent(entity, new GlowFlushTimer { LastFlushSecond = wholeSecond });
                    continue;  // first observation, defer flush to next tick
                }

                FactionEconomy.Add(em, faction.ValueRO.Value, Cost.Of(glow: 1));
                storedRW.ValueRW.Amount -= 1;
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
                Debug.Log($"[Glow] carrier died — dropped {dropAmounts[i]} Glow at pickup");
            }
            dropList.Dispose();
            dropPositions.Dispose();
            dropAmounts.Dispose();
            dropSources.Dispose();

            ecb.Playback(em);
            ecb.Dispose();
        }
    }

    /// <summary>
    /// Per-reliquary tracker for the once-per-second flush from Stored to
    /// the faction bank. Holds the last whole-second tick that flushed.
    /// </summary>
    public struct GlowFlushTimer : IComponentData
    {
        public int LastFlushSecond;
    }
}
