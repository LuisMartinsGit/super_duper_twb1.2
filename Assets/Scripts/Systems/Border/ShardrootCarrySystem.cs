// ShardrootCarrySystem.cs
// The Shardroot artifact's carry chain (Curse_And_Shardroot.md §3.1):
//   1. despawn timer — the Shardroot itself is PERSISTENT and exempt
//   2. a unit standing on it long enough claims it and becomes the bearer
//   3. the bearer reaching their Temple ENSHRINES it (sect powers amplified)
//   4. the bearer dying drops it in place, for anyone to claim again
//
// This was GlowFlowSystem, the Glow economy's carry loop. Glow is gone; the
// Shardroot had already cannibalized this machinery, so what survived is the
// artifact's and now says so.
//
// STILL CARRIES AN 'Amount': enshrinement is scored by stored quantity
// (ShardrootStored on the Temple), which canon §3.1 replaces with a flat 30%
// cooldown cut on a binary enshrined/not state. Not yet converted.
//
// Carrier-death runs UpdateBefore(DeathSystem) so we can catch the unit
// at HP <= 0 before the entity is queued for destruction.
//
// Spec §5.1: "Shardroot pickup can be intercepted in transit by any faction."
// (any unit can pick up, regardless of faction).

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Combat;
using TheWaningBorder.Core.Localization;
using static TheWaningBorder.Core.Config.BorderConstants;

using TheWaningBorder.Core;
namespace TheWaningBorder.Systems.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(DeathSystem))]
    public partial class ShardrootCarrySystem : SystemBase
    {
        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and these were never disposed, so this hot path leaked one
        // per invocation. A bloated registry slows every later query AND
        // every structural change. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_UnitTagFactionTagLocalTransformHealth =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTagLocalTransformHealth;

        static readonly ComponentType[] QT_TempleOfRidanTagFactionTagLocalTransformHealth =
        {
            ComponentType.ReadOnly<TempleOfRidanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_TempleOfRidanTagFactionTagLocalTransformHealth;

        #endregion

        protected override void OnUpdate()
        {
            var em = EntityManager;
            float dt = (float)SystemAPI.Time.DeltaTime;

            // ── Phase 1: pickup-timer despawn ──────────────────────────
            // The Shardroot pickup is PERSISTENT (canon §3.1) — excluded.
            var expiredPickups = new NativeList<Entity>(2, Allocator.Temp);
            foreach (var (state, entity) in SystemAPI
                .Query<RefRW<ShardrootPickupState>>()
                .WithAll<ShardrootPickupTag>()
                .WithNone<ShardrootTag>()
                .WithEntityAccess())
            {
                state.ValueRW.TimeRemaining -= dt;
                if (state.ValueRO.TimeRemaining <= 0f)
                    expiredPickups.Add(entity);
            }
            for (int i = 0; i < expiredPickups.Length; i++)
            {
                TWBLog.Log($"[Shardroot] pickup despawned (timeout)");
                em.DestroyEntity(expiredPickups[i]);
            }
            expiredPickups.Dispose();

            // ── Phase 2: attunement claim (spec refinement #4) ─────────
            // 20-second visible attunement — no instant-on-touch claim.
            // First non-Border unit in range becomes the Attuner. If they
            // move out of range, die, or change faction, AttunementProgress
            // resets and another in-range unit can take over (fight-over-
            // loot). On completion, transfer to ShardrootBearer + destroy pickup.
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Snapshot units so the per-pickup loop doesn't re-query.
            var unitSnapshotQuery = QC_UnitTagFactionTagLocalTransformHealth.Get(em, QT_UnitTagFactionTagLocalTransformHealth);
            using var unitEnts = unitSnapshotQuery.ToEntityArray(Allocator.Temp);
            using var unitFactions = unitSnapshotQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitTransforms = unitSnapshotQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var unitHealths = unitSnapshotQuery.ToComponentDataArray<Health>(Allocator.Temp);

            var claimedPickups = new NativeList<Entity>(2, Allocator.Temp);
            var claimers = new NativeList<Entity>(2, Allocator.Temp);
            var claimedAmounts = new NativeList<int>(2, Allocator.Temp);
            var claimedSources = new NativeList<RitualKind>(2, Allocator.Temp);

            foreach (var (stateRW, pickupTransform, pickupEntity) in SystemAPI
                .Query<RefRW<ShardrootPickupState>, RefRO<LocalTransform>>()
                .WithAll<ShardrootPickupTag>()
                .WithEntityAccess())
            {
                ref var state = ref stateRW.ValueRW;
                var pickupPos = pickupTransform.ValueRO.Position;

                // Validate current attuner — still in range, alive, non-Border?
                bool attunerValid = false;
                if (state.Attuner != Entity.Null && em.Exists(state.Attuner))
                {
                    if (em.HasComponent<Health>(state.Attuner)
                        && em.GetComponentData<Health>(state.Attuner).Value > 0
                        && em.HasComponent<FactionTag>(state.Attuner)
                        && em.GetComponentData<FactionTag>(state.Attuner).Value != Faction.Border
                        && em.HasComponent<LocalTransform>(state.Attuner))
                    {
                        var aPos = em.GetComponentData<LocalTransform>(state.Attuner).Position;
                        float dxz = math.distance(
                            new float2(aPos.x, aPos.z),
                            new float2(pickupPos.x, pickupPos.z));
                        if (dxz <= ShardrootPickupRadius) attunerValid = true;
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
                        if (unitFactions[i].Value == Faction.Border) continue;

                        var uPos = unitTransforms[i].Position;
                        float dxz = math.distance(
                            new float2(uPos.x, uPos.z),
                            new float2(pickupPos.x, pickupPos.z));
                        if (dxz > ShardrootPickupRadius) continue;

                        state.Attuner = unitEnts[i];
                        state.AttunementProgress = 0f;
                        TWBLog.Log($"[Shardroot] {unitFactions[i].Value} unit begins attuning ({ShardrootAttunementTime:F0}s)");
                        break;
                    }
                }

                // Tick attunement.
                if (state.Attuner != Entity.Null)
                {
                    state.AttunementProgress += dt;
                    if (state.AttunementProgress >= ShardrootAttunementTime)
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
                    if (em.HasComponent<ShardrootBearer>(unit))
                    {
                        var car = em.GetComponentData<ShardrootBearer>(unit);
                        existing = car.Amount;
                        keepSrc = car.Source; // first ritual wins for the source label
                    }
                    var merged = new ShardrootBearer { Amount = existing + amount, Source = keepSrc };
                    if (em.HasComponent<ShardrootBearer>(unit))
                        em.SetComponentData(unit, merged);
                    else
                        em.AddComponentData(unit, merged);

                    Faction f = em.HasComponent<FactionTag>(unit)
                        ? em.GetComponentData<FactionTag>(unit).Value : Faction.Blue;
                    TWBLog.Log($"[Shardroot] {f} attunement complete — picked up {amount} Shardroot (carrying {merged.Amount})");

                    // The Shardroot hops from the pickup onto the claimer:
                    // the carrier is now the artifact's embodiment (visible
                    // to all on the minimap, drops it on death).
                    if (em.Exists(claimedPickups[i])
                        && em.HasComponent<ShardrootTag>(claimedPickups[i])
                        && !em.HasComponent<ShardrootTag>(unit))
                    {
                        em.AddComponent<ShardrootTag>(unit);
                        SimSignals.Notify(
                            string.Format(Loc.T("{0} carries the SHARDROOT!"), f));
                    }
                }
                if (em.Exists(claimedPickups[i]))
                    em.DestroyEntity(claimedPickups[i]);
            }
            claimedPickups.Dispose();
            claimers.Dispose();
            claimedAmounts.Dispose();
            claimedSources.Dispose();

            // ── Phase 3: carrier-near-temple → deposit ─────────────────
            // Spec refinement #2: Shardroot is stored on TempleOfRidan, not on a
            // standalone reliquary. The stockpile stays in the Temple — it
            // does NOT flush into the faction bank. Stored Shardroot is consumed
            // directly by spending paths (Shardroot weapon upgrades, god powers
            // when those reach refinement #6 wiring).
            var templeQuery = QC_TempleOfRidanTagFactionTagLocalTransformHealth.Get(em, QT_TempleOfRidanTagFactionTagLocalTransformHealth);
            using var templeEnts = templeQuery.ToEntityArray(Allocator.Temp);
            using var templeFactions = templeQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var templeTransforms = templeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var templeHealth = templeQuery.ToComponentDataArray<Health>(Allocator.Temp);

            if (templeEnts.Length > 0)
            {
                foreach (var (carrierRW, unitTransform, unitFaction, unitHealth, unitEntity) in SystemAPI
                    .Query<RefRW<ShardrootBearer>, RefRO<LocalTransform>, RefRO<FactionTag>, RefRO<Health>>()
                    .WithNone<ShardboundHeroTag>() // hero WIELDS it — never deposits (no backsies)
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
                        // carry ShardrootStored — skip gracefully rather than crash.
                        if (!em.HasComponent<ShardrootStored>(templeEnts[i])) continue;

                        var dxz = math.distance(
                            new float2(unitPos.x, unitPos.z),
                            new float2(templeTransforms[i].Position.x, templeTransforms[i].Position.z));
                        if (dxz > ShardrootDepositRadius) continue;

                        int delivered = carrierRW.ValueRO.Amount;
                        var stored = em.GetComponentData<ShardrootStored>(templeEnts[i]);
                        stored.Amount += delivered;
                        em.SetComponentData(templeEnts[i], stored);

                        carrierRW.ValueRW.Amount = 0;
                        ecb.RemoveComponent<ShardrootBearer>(unitEntity);

                        // Temple choice: enshrining the Shardroot moves the
                        // tag onto the Temple (locked in — it leaves only
                        // via the Temple's detonation, TempleExplodeSystem).
                        if (em.HasComponent<ShardrootTag>(unitEntity))
                        {
                            ecb.RemoveComponent<ShardrootTag>(unitEntity);
                            ecb.AddComponent<ShardrootTag>(templeEnts[i]);
                            SimSignals.Notify(
                                string.Format(Loc.T("{0} has ENSHRINED the Shardroot — their powers surge!"), f));
                        }

                        TWBLog.Log($"[Shardroot] {f} deposited {delivered} Shardroot at Temple of Ridan (stored: {stored.Amount})");
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

            // THE SHARDROOT IS THE ONLY THING THAT DROPS (2026-09-01). Every
            // other death yields nothing on the ground: kill rewards are
            // credited straight to the bank, the way gathering is. Gated by
            // ShardrootTag rather than by a carried amount, so a courier or
            // the Shardbound Hero drops the artifact and nobody drops loot.
            foreach (var (carrier, health, transform, entity) in SystemAPI
                .Query<RefRO<ShardrootBearer>, RefRO<Health>, RefRO<LocalTransform>>()
                .WithAll<ShardrootTag>()
                .WithEntityAccess())
            {
                if (health.ValueRO.Value > 0) continue;
                dropList.Add(entity);
                dropPositions.Add(transform.ValueRO.Position);
                dropAmounts.Add(carrier.ValueRO.Amount);
                dropSources.Add(carrier.ValueRO.Source);
            }
            for (int i = 0; i < dropList.Length; i++)
            {
                // The artifact — persistent, tagged, up for grabs again.
                var dropped = ShardrootPickup.Create(em, dropPositions[i], dropSources[i], dropAmounts[i]);
                em.AddComponent<ShardrootTag>(dropped);
                TheWaningBorder.Systems.Border.ShardrootSystem.MakePersistent(em, dropped);
                em.RemoveComponent<ShardrootTag>(dropList[i]);
                SimSignals.Notify(Loc.T("The SHARDROOT has fallen — claim it!"));

                if (em.HasComponent<ShardrootBearer>(dropList[i]))
                    em.RemoveComponent<ShardrootBearer>(dropList[i]);
                TWBLog.Log("[Shardroot] bearer died — the artifact lies where they fell");
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
