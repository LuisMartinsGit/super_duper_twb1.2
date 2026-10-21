// ShardrootCarrySystem.cs
// The Shardroot artifact's carry chain (Curse_And_Shardroot.md §3.1):
//   1. despawn timer — the Shardroot itself is PERSISTENT and exempt
//   2. a unit reaching it claims it at once and becomes the bearer
//   3. the bearer reaching their Temple ENSHRINES it (sect powers amplified,
//      and the whole army empowered -- ShardrootEmpowermentSystem, § 3.1b)
//      unless the AI routed it to the Hall (ShardrootBearer.Intent)
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

        static readonly ComponentType[] QT_HeroesWithDestination =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<TheWaningBorder.Abilities.UniqueUnitTag>(),
            ComponentType.ReadOnly<DesiredDestination>(),
        };
        static CachedEntityQuery QC_HeroesWithDestination;

        // Phase 3's and phase 5's own queries, for an emptiness test before
        // their foreach (which completes LocalTransform writers even when it
        // then finds nothing).
        static readonly ComponentType[] QT_Couriers =
        {
            ComponentType.ReadOnly<ShardrootBearer>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.Exclude<ShardboundHeroTag>(),
        };
        static CachedEntityQuery QC_Couriers;

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

            // ── Phase 2: instant claim (2026-10-06) ────────────────────
            // The pickup is taken the TICK a living non-Border unit stands
            // within ShardrootPickupRadius — no channel. The old 20 s
            // attunement locked in whichever unit arrived first, so an
            // escort reaching the artifact a step ahead of King Lexor held
            // the attunement and the king, ordered onto it, never got it.
            //
            // Who claims, when several are in range this tick:
            //   1. a HERO (UniqueUnitTag) in range — the player who walks the
            //      king onto the artifact means him to carry it;
            //   2. otherwise the first other unit in snapshot order, EXCEPT a
            //      unit whose own faction has a hero currently ordered onto
            //      the artifact (its DesiredDestination lies within the
            //      pickup radius): the hero has right of way over his own
            //      escort. Enemies are never held back — interception stays.
            // Snapshot order is chunk order, which every peer shares.
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
                    byte keepIntent = ShardrootIntent.None; // a fresh courier has no intent
                    if (em.HasComponent<ShardrootBearer>(unit))
                    {
                        var car = em.GetComponentData<ShardrootBearer>(unit);
                        existing = car.Amount;
                        keepSrc = car.Source; // first ritual wins for the source label
                        keepIntent = car.Intent;
                    }
                    var merged = new ShardrootBearer { Amount = existing + amount, Source = keepSrc, Intent = keepIntent };
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

            // ── Phase 3: carrier-near-temple → deposit ─────────────────
            // Spec refinement #2: Shardroot is stored on TempleOfRidan, not on a
            // standalone reliquary. The stockpile stays in the Temple — it
            // does NOT flush into the faction bank. Stored Shardroot is consumed
            // directly by spending paths (Shardroot weapon upgrades, god powers
            // when those reach refinement #6 wiring).
            // Only with a depositing courier about (a Shardbound hero never
            // deposits): otherwise the temple snapshot is skipped, and with it
            // a main-thread read of LocalTransform every tick.
            if (!QC_Couriers.Get(em, QT_Couriers).IsEmpty)
            {
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
                    // A courier bound for the Hall (the King road, § 3.1b)
                    // walks past its Temple without enshrining.
                    if (carrierRW.ValueRO.Intent == ShardrootIntent.Hall) continue;

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

                        // Measured to the Temple's WALL, not its pivot
                        // (2026-10-07): the Temple's footprint puts its pivot
                        // further from its own wall than the deposit radius,
                        // so on centre distance no courier could ever reach
                        // it and the enshrine never fired.
                        var dxz = TargetGeometry.SurfaceDistXZ(em, unitPos,
                            templeTransforms[i].Position, templeEnts[i]);
                        if (dxz > ShardrootDepositRadius) continue;

                        // THE PRICE (§3.1c, 2026-10-09): enshrining is paid on
                        // delivery; a faction that cannot pay waits here.
                        if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, f,
                                TheWaningBorder.Entities.ShardrootEmpowermentConfig.I.EnshrinePrice,
                                TheWaningBorder.Economy.SpendCategory.Religion))
                            break;

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
                                string.Format(Loc.T("{0} enshrines the Shardroot — its army is empowered!"), f));
                        }

                        UnityEngine.Debug.Log($"[Shardroot] {f} deposited {delivered} Shardroot at Temple of Ridan (stored: {stored.Amount})");
                        break;
                    }
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
                if (em.HasComponent<TheWaningBorder.Entities.ShardboundKing>(dropList[i]))
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

            // Heroes with a destination — the only units that can hold a
            // faction's right of way (was: a HasComponent pair per unit).
            var heroQuery = QC_HeroesWithDestination.Get(em, QT_HeroesWithDestination);
            using var heroFactions = heroQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var heroHealths = heroQuery.ToComponentDataArray<Health>(Allocator.Temp);
            using var heroDests = heroQuery.ToComponentDataArray<DesiredDestination>(Allocator.Temp);

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

                // Factions with a living hero ordered onto this pickup.
                uint heroBound = 0u;
                for (int i = 0; i < heroFactions.Length; i++)
                {
                    if (heroHealths[i].Value <= 0) continue;
                    int fi = (int)heroFactions[i].Value;
                    if (fi < 0 || fi >= 32) continue;
                    var dd = heroDests[i];
                    if (dd.Has == 0) continue;
                    if (math.distance(new float2(dd.Position.x, dd.Position.z), pickupXZ)
                        <= ShardrootPickupRadius)
                        heroBound |= 1u << fi;
                }

                int chosen = -1;
                bool chosenIsHero = false;
                for (int i = 0; i < unitEnts.Length; i++)
                {
                    if (unitHealths[i].Value <= 0) continue;
                    if (unitFactions[i].Value == Faction.Border) continue;

                    var uPos = unitTransforms[i].Position;
                    if (math.distance(new float2(uPos.x, uPos.z), pickupXZ) > ShardrootPickupRadius)
                        continue;

                    bool hero = em.HasComponent<TheWaningBorder.Abilities.UniqueUnitTag>(unitEnts[i]);
                    if (hero)
                    {
                        chosen = i;
                        chosenIsHero = true;
                        break;
                    }
                    if (chosen >= 0) continue;

                    int fi = (int)unitFactions[i].Value;
                    if (fi >= 0 && fi < 32 && (heroBound & (1u << fi)) != 0) continue;
                    chosen = i;
                }

                if (chosen >= 0)
                {
                    claimedPickups.Add(pickupEntity);
                    claimers.Add(unitEnts[chosen]);
                    claimedAmounts.Add(state.Amount);
                    claimedSources.Add(state.Source);
                    TWBLog.Log($"[Shardroot] {unitFactions[chosen].Value} {(chosenIsHero ? "hero" : "unit")} takes the pickup");
                }
            }
        }
    }
}
