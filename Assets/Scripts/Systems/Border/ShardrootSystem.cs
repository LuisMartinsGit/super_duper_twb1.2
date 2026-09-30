// ShardrootSystem.cs
// Match lifecycle of the Shardroot artifact (Curse & Shardroot canon §3):
//   * Seeded host-well selection (deterministic from SpawnSeed).
//   * TryAward — called by the verb systems (rituals + node death) when a
//     well is claimed: the FIRST verb on the host well drops the artifact.
//   * Hall delivery — a carrier reaching its own Hall awakens the
//     culture's Shardbound Hero (locked choice; Temple enshrinement is
//     the alternative, handled by ShardrootCarrySystem's deposit path).
//   * Holder tracking for the minimap beacon and the Border's
//     hunt-the-holder aggression bias (read by CurseTerritorySystem.Living).
//   * THE MAW BACKSTOP (section 3): a host well left Wild (unverbed) for
//     BorderSettings.shardrootMawSeconds reaches "Maw maturity" -- the
//     artifact becomes visibly embedded in it (a ShardrootEmbedded display
//     entity + minimap beacon + a ping for everyone). It is still claimed
//     only by verbing that well. The Well->Fissure->Maw ladder itself is
//     superseded by the Veil (section 2.3), so maturity is measured as sim
//     time the host has spent alive and Wild. No map data is needed: the Maw
//     IS the host well, wherever the map put it.
//
// The artifact itself is a persistent ShardrootPickup carrying ShardrootTag —
// attunement/carry/interception/drop-on-death/temple-storage/detonation
// all reuse the existing Shardroot machinery (ShardrootCarrySystem,
// TempleExplodeSystem) with small Shardroot-aware patches.
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.Core.Localization;

using TheWaningBorder.Core;
using TheWaningBorder.Data.Border;
namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ShardrootSystem : SystemBase
    {
        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and these were never disposed, so this hot path leaked one
        // per invocation. A bloated registry slows every later query AND
        // every structural change. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_ShardrootState =
        {
            ComponentType.ReadOnly<ShardrootState>(),
        };
        static CachedEntityQuery QC_ShardrootState;

        static readonly ComponentType[] QT_BorderMainNodeTagLocalTransform =
        {
            ComponentType.ReadOnly<BorderMainNodeTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_BorderMainNodeTagLocalTransform;

        static readonly ComponentType[] QT_HallTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagLocalTransform;

        static readonly ComponentType[] QT_Embedded =
        {
            ComponentType.ReadOnly<ShardrootEmbedded>(),
        };
        static CachedEntityQuery QC_Embedded;

        #endregion

        protected override void OnCreate()
        {
            // No well gate any more (Territory_Claims.md §6.6, 2026-09-29):
            // there are no wells, and this system still owns the ShardrootState
            // singleton, the Fortress delivery and the holder tracking the
            // curse hunts by. Host selection and the Maw find no well and
            // simply never arm; the artifact comes from a curse spawn
            // (CurseTerritorySystem.TryRollShardroot and its guarantee).
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // ── Singleton bootstrap ─────────────────────────────────────
            var stateQuery = QC_ShardrootState.Get(em, QT_ShardrootState);
            Entity stateEntity;
            if (stateQuery.IsEmptyIgnoreFilter)
            {
                stateEntity = em.CreateEntity(typeof(ShardrootState));
                em.SetComponentData(stateEntity, FreshState());
            }
            else
            {
                using var ents = stateQuery.ToEntityArray(Allocator.Temp);
                stateEntity = ents[0];
            }
            var state = em.GetComponentData<ShardrootState>(stateEntity);

            // Per-match state must never walk into the next match (the
            // second-match-in-process desync class). The world is disposed
            // at teardown today, which already drops this singleton; this
            // guard is what keeps a previous match's Found = 1 from blocking
            // the artifact if any path ever keeps a world alive. A scenario-
            // authored state (MatchEpoch 0) is left alone.
            if (state.MatchEpoch != 0 && state.MatchEpoch != SimCadence.Epoch)
            {
                ClearEmbedded(em);
                state = FreshState();
                UnityEngine.Debug.Log("[Shardroot] state carried over from a previous match -- reset");
            }

            // ── Host-well selection (once, deterministic) ───────────────
            if (state.HostChosen == 0)
            {
                var nodeQuery = QC_BorderMainNodeTagLocalTransform.Get(em, QT_BorderMainNodeTagLocalTransform);
                int count = nodeQuery.CalculateEntityCount();
                if (count > 0)
                {
                    using var nodes = nodeQuery.ToEntityArray(Allocator.Temp);
                    using var xfs = nodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

                    // Deterministic order: sort indices by (x, z) so every
                    // client picks the same host regardless of chunk order.
                    var order = new NativeArray<int>(count, Allocator.Temp);
                    for (int i = 0; i < count; i++) order[i] = i;
                    for (int a = 0; a < count - 1; a++)
                        for (int b = a + 1; b < count; b++)
                        {
                            var pa = xfs[order[a]].Position;
                            var pb = xfs[order[b]].Position;
                            bool swap = pb.x < pa.x || (pb.x == pa.x && pb.z < pa.z);
                            if (swap) { (order[a], order[b]) = (order[b], order[a]); }
                        }

                    uint hash = (uint)GameSettings.SpawnSeed * 2654435761u + 97u;
                    hash ^= hash >> 13; hash *= 0x5bd1e995u; hash ^= hash >> 15;
                    int pick = (int)(hash % (uint)count);
                    state.HostNode = nodes[order[pick]];
                    state.HostChosen = 1;
                    state.HostWildSeconds = 0f;
                    var hp = xfs[order[pick]].Position;
                    order.Dispose();
                    UnityEngine.Debug.Log($"[Shardroot] host well chosen: #{pick + 1} of {count} " +
                        $"at ({hp.x:F0},{hp.z:F0}), seed {GameSettings.SpawnSeed}");
                    // Tell every player the artifact exists. Nothing else
                    // does until someone lands a verb on the host well, so
                    // a match could run its whole course without anyone
                    // knowing there was something to look for.
                    SimSignals.Notify(Loc.T(
                        "A SHARDROOT sleeps beneath one of the wells. The first to work that well claims it."));
                }
            }

            // ── Host validation + guaranteed surfacing (2026-08-11) ─────
            // "4 wells claimed and no Shardroot": the host reference can
            // dangle (extinction respawns REPLACE well entities; a
            // persisted world can carry a previous match's state), and any
            // claim path that misses TryAward would strand the artifact
            // forever. The Shardroot is a map item and MUST enter play:
            //   * dangling host — re-choose among live UNCLAIMED wells;
            //   * host claimed without an award, or no unclaimed well
            //     left — surface the artifact immediately.
            if (state.HostChosen != 0 && state.Found == 0)
            {
                bool hostValid = state.HostNode != Entity.Null
                    && em.Exists(state.HostNode)
                    && em.HasComponent<BorderNodeState>(state.HostNode);
                bool hostClaimed = false;
                if (hostValid)
                {
                    // Destroyed counts as claimed (2026-09-26): the death
                    // intercept awards any killer now, but a well set
                    // Destroyed by any other path used to strand the artifact
                    // inside a dormant husk until regrowth.
                    hostClaimed = IsClaimed(em.GetComponentData<BorderNodeState>(state.HostNode).State);
                }

                if (!hostValid || hostClaimed)
                {
                    // Deterministic scan — smallest (x, z) wins per category.
                    Entity unclaimed = Entity.Null; float3 unclaimedPos = default;
                    Entity claimed = Entity.Null; float3 claimedPos = default;
                    foreach (var (ns, xf, e) in SystemAPI
                        .Query<RefRO<BorderNodeState>, RefRO<LocalTransform>>()
                        .WithAll<BorderMainNodeTag>()
                        .WithEntityAccess())
                    {
                        var s = ns.ValueRO.State;
                        var p = xf.ValueRO.Position;
                        bool isClaimed = IsClaimed(s);
                        if (!isClaimed)
                        {
                            if (unclaimed == Entity.Null || p.x < unclaimedPos.x
                                || (p.x == unclaimedPos.x && p.z < unclaimedPos.z))
                            { unclaimed = e; unclaimedPos = p; }
                        }
                        else
                        {
                            if (claimed == Entity.Null || p.x < claimedPos.x
                                || (p.x == claimedPos.x && p.z < claimedPos.z))
                            { claimed = e; claimedPos = p; }
                        }
                    }

                    if (!hostValid && unclaimed != Entity.Null)
                    {
                        state.HostNode = unclaimed;
                        state.HostWildSeconds = 0f;
                        if (state.Embedded != 0) { ClearEmbedded(em); state.Embedded = 0; }
                        UnityEngine.Debug.Log($"[Shardroot] host re-chosen at ({unclaimedPos.x:F0},{unclaimedPos.z:F0}) " +
                            "(previous host dangled)");
                    }
                    else if (hostClaimed || claimed != Entity.Null)
                    {
                        float3 dropPos = hostClaimed
                            ? em.GetComponentData<LocalTransform>(state.HostNode).Position
                            : claimedPos;
                        state.Found = 1;
                        ClearEmbedded(em);
                        state.Embedded = 0;
                        var pickup = ShardrootPickup.Create(em,
                            dropPos + new float3(3f, 0f, 3f),
                            RitualKind.Purification, ShardrootState.ShardrootPower);
                        em.AddComponent<ShardrootTag>(pickup);
                        MakePersistent(em, pickup);
                        SimSignals.Notify(Loc.T("The SHARDROOT has been unearthed!"));
                        UnityEngine.Debug.Log($"[Shardroot] artifact surfaced by fallback at ({dropPos.x:F0},{dropPos.z:F0}) " +
                            "(host lost, or claimed/destroyed without an award)");
                    }
                }
            }

            // ── The Maw backstop (section 3) ────────────────────────────
            // Only while the artifact is still inside the host: once it is
            // out -- awarded, surfaced, or riding out with the curse (2.13
            // rule 5) -- Found = 1 and the backstop never fires.
            if (state.HostChosen != 0 && state.Found == 0 && state.Embedded == 0
                && state.HostNode != Entity.Null && em.Exists(state.HostNode)
                && em.HasComponent<BorderNodeState>(state.HostNode))
            {
                float maw = BorderSettings.Get().shardrootMawSeconds;
                var hs = em.GetComponentData<BorderNodeState>(state.HostNode).State;
                if (maw > 0f && hs == NodeState.Active)
                {
                    state.HostWildSeconds += SystemAPI.Time.DeltaTime;
                    if (state.HostWildSeconds >= maw)
                        EmbedInMaw(em, ref state);
                }
            }

            // ── Hall delivery → awaken the Shardbound Hero ──────────────
            if (state.Found != 0)
            {
                Entity courier = Entity.Null;
                float3 courierPos = default;
                Faction courierFaction = Faction.Border;
                foreach (var (carrier, xf, fac, entity) in SystemAPI
                    .Query<RefRO<ShardrootBearer>, RefRO<LocalTransform>, RefRO<FactionTag>>()
                    .WithAll<ShardrootTag>()
                    .WithNone<ShardboundHeroTag>()
                    .WithEntityAccess())
                {
                    courier = entity;
                    courierPos = xf.ValueRO.Position;
                    courierFaction = fac.ValueRO.Value;
                    break; // there is only ever one Shardroot
                }

                if (courier != Entity.Null
                    && TryFindOwnHall(em, courierFaction, courierPos,
                        ShardrootState.HallDeliverRadius, out _))
                {
                    AwakenHero(em, courier, courierPos, courierFaction);
                }
            }

            // ── Holder tracking (minimap beacon + Border aggression) ────
            // A Faction.Border holder (the ground pickup, or a curse unit
            // carrying it) is "unheld": the curse does not hunt itself.
            Faction prevHolder = state.HolderFaction;
            state.HolderFaction = Faction.Border;
            state.HolderPos = default;
            foreach (var (fac, xf) in SystemAPI
                .Query<RefRO<FactionTag>, RefRO<LocalTransform>>()
                .WithAll<ShardrootTag>())
            {
                if (fac.ValueRO.Value != Faction.Border)
                {
                    state.HolderFaction = fac.ValueRO.Value;
                    state.HolderPos = xf.ValueRO.Position;
                    break;
                }
            }
            if (state.HolderFaction != prevHolder)
                UnityEngine.Debug.Log(state.HolderFaction == Faction.Border
                    ? $"[Shardroot] {prevHolder} no longer holds the artifact"
                    : $"[Shardroot] {state.HolderFaction} now holds the artifact at " +
                      $"({state.HolderPos.x:F0},{state.HolderPos.z:F0})");

            // ── Minimap beacon (2026-08-04, "where did the Shardroot go?"):
            // once unearthed, a slow GOLD pulse follows the artifact wherever
            // it is — ground drop, courier, hero, or enshrining temple — so
            // the One Ring is never invisible again. (The old beacon promise
            // predated the UI redesign and had no surviving consumer.)
            // The Maw's embedded artifact beacons the same way (section 3,
            // "map ping").
            if (state.Found != 0 || state.Embedded != 0)
            {
                if (_beaconAcc.Due(SystemAPI.Time.DeltaTime, BeaconInterval))
                {
                    bool pinged = false;
                    foreach (var xf in SystemAPI
                        .Query<RefRO<LocalTransform>>()
                        .WithAll<ShardrootTag>())
                    {
                        SimSignals.Ping(
                            xf.ValueRO.Position,
                            SimPingKind.Discovery,
                            BeaconInterval + 0.5f, big: true);
                        pinged = true;
                        break; // there is only ever one Shardroot
                    }
                    if (!pinged)
                        foreach (var xf in SystemAPI
                            .Query<RefRO<LocalTransform>>()
                            .WithAll<ShardrootEmbedded>())
                        {
                            SimSignals.Ping(
                                xf.ValueRO.Position,
                                SimPingKind.Discovery,
                                BeaconInterval + 0.5f, big: true);
                            break;
                        }
                }
            }

            em.SetComponentData(stateEntity, state);
        }

        private SimCadence.Periodic _beaconAcc;
        private const float BeaconInterval = 4f;

        /// <summary>
        /// Called by the verb systems when a well is claimed (Cleansed /
        /// Converted / Destroyed). If the well is the seeded host and the
        /// artifact hasn't surfaced yet, it drops here — announced to all.
        /// </summary>
        public static void TryAward(EntityManager em, Entity node, float3 pos, RitualKind kind)
        {
            var q = QC_ShardrootState.Get(em, QT_ShardrootState);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            var state = em.GetComponentData<ShardrootState>(ents[0]);
            if (state.HostChosen == 0 || state.Found != 0) return;
            if (state.HostNode != node) return;

            bool fromMaw = state.Embedded != 0;
            state.Found = 1;
            state.Embedded = 0;
            em.SetComponentData(ents[0], state);
            ClearEmbedded(em);

            var pickup = ShardrootPickup.Create(em, pos + new float3(3f, 0f, 3f),
                kind, ShardrootState.ShardrootPower);
            em.AddComponent<ShardrootTag>(pickup);
            MakePersistent(em, pickup);

            SimSignals.Notify(Loc.T("The SHARDROOT has been unearthed!"));
            UnityEngine.Debug.Log($"[Shardroot] AWARDED -- {kind} on the host well at ({pos.x:F0},{pos.z:F0}) " +
                $"drops the artifact{(fromMaw ? " (it was embedded in the Maw)" : "")}");
        }

        private static ShardrootState FreshState() => new ShardrootState
        {
            HostNode = Entity.Null,
            HostChosen = 0,
            Found = 0,
            HolderFaction = Faction.Border,
            HostWildSeconds = 0f,
            Embedded = 0,
            MatchEpoch = SimCadence.Epoch,
        };

        /// <summary>A well is "claimed" for the Shardroot when any verb has
        /// landed on it -- purified, pacified, or destroyed.</summary>
        private static bool IsClaimed(NodeState s) =>
            s == NodeState.Cleansed || s == NodeState.Converted || s == NodeState.Destroyed;

        /// <summary>The Maw backstop fires: the artifact shows itself inside
        /// the host well. A display-only entity (no pickup, no ShardrootTag)
        /// so nobody can walk off with it; TryAward still decides who gets it.</summary>
        private static void EmbedInMaw(EntityManager em, ref ShardrootState state)
        {
            state.Embedded = 1;
            var wellPos = em.GetComponentData<LocalTransform>(state.HostNode).Position;
            var marker = em.CreateEntity(
                typeof(PresentationId),
                typeof(LocalTransform),
                typeof(ShardrootEmbedded));
            em.SetComponentData(marker, new PresentationId
                { Id = TheWaningBorder.Core.Config.BorderConstants.ShardrootPresentationID });
            em.SetComponentData(marker, LocalTransform.FromPosition(wellPos));
            em.SetComponentData(marker, new ShardrootEmbedded { Well = state.HostNode });

            SimSignals.Ping(wellPos, SimPingKind.Discovery, BeaconInterval * 3f, big: true);
            SimSignals.Notify(Loc.T("The SHARDROOT gleams in the heart of a Maw -- verb that well to claim it!"));
            UnityEngine.Debug.Log($"[Shardroot] MAW -- the host well at ({wellPos.x:F0},{wellPos.z:F0}) stood Wild " +
                $"for {state.HostWildSeconds:F0}s unverbed; the artifact is now visibly embedded in it");
        }

        /// <summary>Remove the Maw's display artifact, if any.</summary>
        private static void ClearEmbedded(EntityManager em)
        {
            var q = QC_Embedded.Get(em, QT_Embedded);
            if (q.IsEmptyIgnoreFilter) return;
            em.DestroyEntity(q);
        }

        /// <summary>Shardroot pickups never despawn (the artifact is
        /// persistent by canon).</summary>
        public static void MakePersistent(EntityManager em, Entity pickup)
        {
            if (!em.HasComponent<ShardrootPickupState>(pickup)) return;
            var ps = em.GetComponentData<ShardrootPickupState>(pickup);
            ps.TimeRemaining = float.MaxValue;
            em.SetComponentData(pickup, ps);
        }

        static readonly ComponentType[] QT_UniqueUnitTagFactionTagHealth =
        {
            ComponentType.ReadOnly<TheWaningBorder.Abilities.UniqueUnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
        };
        static CachedEntityQuery QC_UniqueUnitTagFactionTagHealth;

        private static Entity FindLivingKing(EntityManager em, Faction faction)
        {
            var q = QC_UniqueUnitTagFactionTagHealth.Get(em, QT_UniqueUnitTagFactionTagHealth);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<TheWaningBorder.Abilities.UniqueUnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction && hps[i].Value > 0
                    && tags[i].Kind == TheWaningBorder.Abilities.UniqueUnitKind.KingLexor)
                    return ents[i];
            return Entity.Null;
        }

        private static bool TryFindOwnHall(EntityManager em, Faction faction,
            float3 pos, float radius, out Entity hall)
        {
            hall = Entity.Null;
            var q = QC_HallTagFactionTagLocalTransform.Get(em, QT_HallTagFactionTagLocalTransform);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                float dx = xfs[i].Position.x - pos.x;
                float dz = xfs[i].Position.z - pos.z;
                if (dx * dx + dz * dz <= radius * radius) { hall = ents[i]; return true; }
            }
            return false;
        }

        /// <summary>
        /// Hall choice: consume the carried Shardroot and awaken the
        /// culture's Shardbound Hero — a heavily empowered champion who
        /// WIELDS the artifact (it drops from his body on death via the
        /// existing carrier-death interception). No backsies.
        /// </summary>
        private static void AwakenHero(EntityManager em, Entity courier,
            float3 pos, Faction faction)
        {
            byte culture = FactionColors.GetFactionCulture(faction);

            // Alanthor's Shardbound Hero IS King Lexor bearing the artifact
            // (Curse_And_Shardroot.md 3.1, "The Shardbound King"). A courier
            // reaching the Hall hands it to the living king; if the courier
            // is the king himself there is nothing to hand over -- he is
            // marked the hero and keeps walking. Only a faction with no
            // living king falls through to the placeholder champion.
            if (culture == Cultures.Alanthor)
            {
                Entity king = FindLivingKing(em, faction);
                if (king != Entity.Null)
                {
                    if (king != courier)
                    {
                        em.AddComponent<ShardrootTag>(king);
                        if (em.HasComponent<ShardrootBearer>(courier))
                            em.AddComponentData(king, em.GetComponentData<ShardrootBearer>(courier));
                        else
                            em.AddComponentData(king, new ShardrootBearer
                                { Amount = ShardrootState.ShardrootPower, Source = RitualKind.Purification });
                        if (em.HasComponent<ShardrootTag>(courier)) em.RemoveComponent<ShardrootTag>(courier);
                        if (em.HasComponent<ShardrootBearer>(courier)) em.RemoveComponent<ShardrootBearer>(courier);
                    }
                    em.AddComponent<ShardboundHeroTag>(king);
                    SimSignals.Notify(string.Format(Loc.T("{0}'s King Lexor takes up the SHARDROOT!"), faction));
                    UnityEngine.Debug.Log($"[Shardroot] {faction}: the Hall hands the artifact to King Lexor");
                    return;
                }
            }
            // Culture-flavored base body; stats overridden below. Cultures
            // without a bespoke age-2 unit fall back to the Swordsman body.
            string heroId = culture == Cultures.Alanthor ? "Alanthor_Cataphract" : "Swordsman";

            var hero = UnitFactory.Create(em, heroId, pos + new float3(2f, 0f, 2f), faction);
            if (hero == Entity.Null) return;

            // Shardbound empowerment (kits TBD in balance passes — canon §9).
            if (em.HasComponent<Health>(hero))
                em.SetComponentData(hero, new Health { Value = 1500, Max = 1500 });
            if (em.HasComponent<Damage>(hero))
                em.SetComponentData(hero, new Damage { Value = 60 });
            if (em.HasComponent<MoveSpeed>(hero))
                // Overridden here rather than read from a def, so it does not
                // move with the SOs and has to be kept in step by hand.
                em.SetComponentData(hero, new MoveSpeed { Value = 4.2f });
            if (em.HasComponent<LineOfSight>(hero))
                em.SetComponentData(hero, new LineOfSight { Radius = 30f });

            em.AddComponent<ShardrootTag>(hero);
            em.AddComponent<ShardboundHeroTag>(hero);
            em.AddComponentData(hero, new ShardrootBearer
            {
                Amount = ShardrootState.ShardrootPower,
                Source = RitualKind.Purification,
            });

            // The courier hands the artifact over.
            if (em.HasComponent<ShardrootTag>(courier)) em.RemoveComponent<ShardrootTag>(courier);
            if (em.HasComponent<ShardrootBearer>(courier)) em.RemoveComponent<ShardrootBearer>(courier);

            SimSignals.Notify(string.Format(Loc.T("{0} has awakened the SHARDBOUND HERO!"), faction));
            UnityEngine.Debug.Log($"[Shardroot] {faction} hero awakened ({heroId} body)");
        }
    }
}
