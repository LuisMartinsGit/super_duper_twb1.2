// ShardrootCarrySystem.cs
// The Shardroot artifact's carry chain (Curse_And_Shardroot.md §3.1):
//   1. despawn timer — the Shardroot itself is PERSISTENT and exempt
//   2. a HERO reaching it claims it at once and becomes the bearer (Ascended,
//      docs/Design/Heroes.md §4); no other unit can take it
//   3. a bearer under the player's or AI's enshrine order stows it in its Temple
//   4. the bearer dying explodes and drops it, for any hero to claim again
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

        // PERF (2026-10-16): the claim pass below snapshotted every unit
        // (four full columns, ~3,000 rows) on EVERY tick, although it only
        // has work to do while a pickup lies on the ground. While the
        // Shardroot is carried, enshrined or embedded there is no pickup, and
        // the snapshot is now skipped. The hero right-of-way scan reads only
        // heroes with a destination (a narrower query, same rows in the same
        // relative order, an order-free bitmask) instead of a HasComponent +
        // GetComponentData per unit per pickup. Result-identical.
        static readonly ComponentType[] QT_Pickups =
        {
            ComponentType.ReadOnly<ShardrootPickupTag>(),
            ComponentType.ReadOnly<ShardrootPickupState>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Pickups;

        static readonly ComponentType[] QT_TaggedBearers =
        {
            ComponentType.ReadOnly<ShardrootBearer>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<ShardrootTag>(),
        };
        static CachedEntityQuery QC_TaggedBearers;

        // Bearers under a player's enshrine order (ShardrootEnshrine, § 3.1d).
        static readonly ComponentType[] QT_EnshrineOrders =
        {
            ComponentType.ReadOnly<ShardrootEnshrineOrder>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_EnshrineOrders;

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

            // ── Phase 2: instant claim ─────────────────────────────────
            // ONLY A HERO takes it (docs/Design/Heroes.md §4), the tick it stands
            // within ShardrootPickupRadius. First in snapshot order, which is chunk
            // order, which every peer shares.
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            var claimedPickups = new NativeList<Entity>(2, Allocator.Temp);
            var claimers = new NativeList<Entity>(2, Allocator.Temp);
            var claimedAmounts = new NativeList<int>(2, Allocator.Temp);
            var claimedSources = new NativeList<RitualKind>(2, Allocator.Temp);

            // No pickup on the ground (the Shardroot is carried, enshrined or
            // embedded): the claim pass has nothing to do — and nothing it
            // would have written (Attuner / AttunementProgress live on the
            // pickup) exists. See the PERF note on QT_Pickups.
            if (!QC_Pickups.Get(em, QT_Pickups).IsEmpty)
                ClaimPickups(em, claimedPickups, claimers, claimedAmounts, claimedSources);

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
                    UnityEngine.Debug.Log($"[Shardroot] {f} picked up {amount} Shardroot (carrying {merged.Amount})");

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

            // ── Phase 3a: the player's enshrine order (2026-10-20) ─────
            // A bearer the player sent to their Temple (right-click,
            // ShardrootEnshrine): walk, pay, enshrine — the same delivery as
            // the courier pass below, plus the notices a player needs.
            {
                var orderQuery = QC_EnshrineOrders.Get(em, QT_EnshrineOrders);
                if (!orderQuery.IsEmpty)
                {
                    using var ordered = orderQuery.ToEntityArray(Allocator.Temp);
                    ShardrootEnshrine.TickOrders(em, ecb, ordered, dt);
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
            if (!QC_TaggedBearers.Get(em, QT_TaggedBearers).IsEmpty)
            foreach (var (carrier, health, transform, entity) in SystemAPI
                .Query<RefRO<ShardrootBearer>, RefRO<Health>, RefRO<LocalTransform>>()
                .WithAll<ShardrootTag>()
                .WithEntityAccess())
            {
                // DYING, not just at 0 HP (2026-10-20). A heal that lands on a
                // corpse (a Renewal regen tail ticking after the killing blow)
                // put it back above 0 before this pass saw it; DeathSystem had
                // already started its death and destroyed it two seconds
                // later — with the artifact, no drop, no log (FiendstonePass
                // 2026-10-20: Green's courier). A started death always drops.
                bool dying = TransientState.Active<DeathAnimationState>(em, entity);
                if (health.ValueRO.Value > 0 && !dying) continue;
                // Not dead yet: DeathSystem applies Life Cling's HP floor
                // AFTER this system runs, so a clinging king at 0 HP survives
                // the tick. Dropping here made him lose the artifact and live
                // (2026-09-15). The floor is what decides; mirror it.
                if (TransientState.Active<TheWaningBorder.Abilities.LifeCling>(em, entity)
                    && em.GetComponentData<TheWaningBorder.Abilities.LifeCling>(entity).Floor > 0) continue;
                dropList.Add(entity);
                dropPositions.Add(transform.ValueRO.Position);
                dropAmounts.Add(carrier.ValueRO.Amount);
                dropSources.Add(carrier.ValueRO.Source);
            }
            for (int i = 0; i < dropList.Length; i++)
            {
                // The Shardbound King detonates first: everything around him
                // is thrown skyward and killed on landing, buildings shatter
                // (Curse_And_Shardroot.md 3.1). The artifact then drops as
                // for any bearer.
                if (em.HasComponent<TheWaningBorder.Entities.AscendedHero>(dropList[i]))
                {
                    var kf = em.HasComponent<FactionTag>(dropList[i])
                        ? em.GetComponentData<FactionTag>(dropList[i]).Value : Faction.Border;
                    TheWaningBorder.Entities.ShardboundFury.Detonate(em, dropList[i], dropPositions[i], kf);
                }

                // The artifact — persistent, tagged, up for grabs again.
                var dropped = ShardrootPickup.Create(em, dropPositions[i], dropSources[i], dropAmounts[i]);
                em.AddComponent<ShardrootTag>(dropped);
                TheWaningBorder.Systems.Border.ShardrootSystem.MakePersistent(em, dropped);
                em.RemoveComponent<ShardrootTag>(dropList[i]);
                SimSignals.Notify(Loc.T("The SHARDROOT has fallen — claim it!"));

                if (em.HasComponent<ShardrootBearer>(dropList[i]))
                    em.RemoveComponent<ShardrootBearer>(dropList[i]);
                UnityEngine.Debug.Log($"[Shardroot] bearer ({(em.HasComponent<FactionTag>(dropList[i]) ? em.GetComponentData<FactionTag>(dropList[i]).Value : Faction.Border)}) died at ({dropPositions[i].x:F0},{dropPositions[i].z:F0}) — the artifact lies where they fell");
            }
            dropList.Dispose();
            dropPositions.Dispose();
            dropAmounts.Dispose();
            dropSources.Dispose();

            ecb.Playback(em);
            ecb.Dispose();
        }

        /// <summary>Phase 2, the instant claim — run only while a pickup
        /// exists. Same rules and the same snapshot order as before.</summary>
        private void ClaimPickups(EntityManager em,
            NativeList<Entity> claimedPickups, NativeList<Entity> claimers,
            NativeList<int> claimedAmounts, NativeList<RitualKind> claimedSources)
        {
            // Snapshot units so the per-pickup loop doesn't re-query.
            var unitSnapshotQuery = QC_UnitTagFactionTagLocalTransformHealth.Get(em, QT_UnitTagFactionTagLocalTransformHealth);
            using var unitEnts = unitSnapshotQuery.ToEntityArray(Allocator.Temp);
            using var unitFactions = unitSnapshotQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var unitTransforms = unitSnapshotQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var unitHealths = unitSnapshotQuery.ToComponentDataArray<Health>(Allocator.Temp);

            foreach (var (stateRW, pickupTransform, pickupEntity) in SystemAPI
                .Query<RefRW<ShardrootPickupState>, RefRO<LocalTransform>>()
                .WithAll<ShardrootPickupTag>()
                .WithEntityAccess())
            {
                ref var state = ref stateRW.ValueRW;
                var pickupPos = pickupTransform.ValueRO.Position;
                var pickupXZ = new float2(pickupPos.x, pickupPos.z);

                // No channel any more: nothing is ever mid-attunement.
                state.Attuner = Entity.Null;
                state.AttunementProgress = 0f;

                int chosen = -1;
                bool dropperInReach = false;
                for (int i = 0; i < unitEnts.Length; i++)
                {
                    if (unitHealths[i].Value <= 0) continue;
                    if (unitFactions[i].Value == Faction.Border) continue;
                    if (!em.HasComponent<TheWaningBorder.Abilities.UniqueUnitTag>(unitEnts[i])) continue;

                    var uPos = unitTransforms[i].Position;
                    if (math.distance(new float2(uPos.x, uPos.z), pickupXZ) > ShardrootPickupRadius)
                        continue;

                    // The hero that set it down has to step away first.
                    if (unitEnts[i] == state.DroppedBy) { dropperInReach = true; continue; }
                    if (chosen < 0) chosen = i;
                }
                if (!dropperInReach) state.DroppedBy = Entity.Null;

                if (chosen >= 0)
                {
                    claimedPickups.Add(pickupEntity);
                    claimers.Add(unitEnts[chosen]);
                    claimedAmounts.Add(state.Amount);
                    claimedSources.Add(state.Source);
                    TWBLog.Log($"[Shardroot] {unitFactions[chosen].Value} hero takes the pickup");
                }
            }
        }
    }
}
