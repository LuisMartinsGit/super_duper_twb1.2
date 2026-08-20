// ShardrootSystem.cs
// Match lifecycle of the Shardroot artifact (Curse & Shardroot canon §3):
//   * Seeded host-well selection (deterministic from SpawnSeed).
//   * TryAward — called by the verb systems (rituals + node death) when a
//     well is claimed: the FIRST verb on the host well drops the artifact.
//   * Hall delivery — a carrier reaching its own Hall awakens the
//     culture's Shardbound Hero (locked choice; Temple enshrinement is
//     the alternative, handled by GlowFlowSystem's deposit path).
//   * Holder tracking for the minimap beacon and the Border's
//     hunt-the-holder aggression bias.
//
// The artifact itself is a persistent GlowPickup carrying ShardrootTag —
// attunement/carry/interception/drop-on-death/temple-storage/detonation
// all reuse the existing Glow machinery (GlowFlowSystem,
// TempleExplodeSystem) with small Shardroot-aware patches.
//
// Location: Assets/Scripts/Systems/Border/
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.Core.Localization;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class ShardrootSystem : SystemBase
    {
        protected override void OnCreate()
        {
            RequireForUpdate<BorderNodeState>();
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;

            // ── Singleton bootstrap ─────────────────────────────────────
            var stateQuery = em.CreateEntityQuery(ComponentType.ReadWrite<ShardrootState>());
            Entity stateEntity;
            if (stateQuery.IsEmptyIgnoreFilter)
            {
                stateEntity = em.CreateEntity(typeof(ShardrootState));
                em.SetComponentData(stateEntity, new ShardrootState
                {
                    HostNode = Entity.Null,
                    HostChosen = 0,
                    Found = 0,
                    HolderFaction = Faction.Border,
                });
            }
            else
            {
                using var ents = stateQuery.ToEntityArray(Allocator.Temp);
                stateEntity = ents[0];
            }
            var state = em.GetComponentData<ShardrootState>(stateEntity);

            // ── Host-well selection (once, deterministic) ───────────────
            if (state.HostChosen == 0)
            {
                var nodeQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<BorderMainNodeTag>(),
                    ComponentType.ReadOnly<LocalTransform>());
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
                    order.Dispose();
                    TWBLog.Log($"[Shardroot] host well chosen ({count} candidates)");
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
                    var hs = em.GetComponentData<BorderNodeState>(state.HostNode).State;
                    hostClaimed = hs == NodeState.Cleansed || hs == NodeState.Converted;
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
                        bool isClaimed = s == NodeState.Cleansed || s == NodeState.Converted;
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
                        TWBLog.Log("[Shardroot] host re-chosen (previous host dangled)");
                    }
                    else if (hostClaimed || claimed != Entity.Null)
                    {
                        float3 dropPos = hostClaimed
                            ? em.GetComponentData<LocalTransform>(state.HostNode).Position
                            : claimedPos;
                        state.Found = 1;
                        var pickup = GlowPickup.Create(em,
                            dropPos + new float3(3f, 0f, 3f),
                            RitualKind.Purification, ShardrootState.ShardrootPower);
                        em.AddComponent<ShardrootTag>(pickup);
                        MakePersistent(em, pickup);
                        PlayerNotificationSystem.Notify(Loc.T("The SHARDROOT has been unearthed!"));
                        TWBLog.Log("[Shardroot] artifact surfaced by fallback " +
                            "(host lost or claimed without award)");
                    }
                }
            }

            // ── Hall delivery → awaken the Shardbound Hero ──────────────
            if (state.Found != 0)
            {
                Entity courier = Entity.Null;
                float3 courierPos = default;
                Faction courierFaction = Faction.Border;
                foreach (var (carrier, xf, fac, entity) in SystemAPI
                    .Query<RefRO<GlowCarrier>, RefRO<LocalTransform>, RefRO<FactionTag>>()
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
            state.HolderFaction = Faction.Border;
            foreach (var fac in SystemAPI
                .Query<RefRO<FactionTag>>()
                .WithAll<ShardrootTag>())
            {
                if (fac.ValueRO.Value != Faction.Border)
                {
                    state.HolderFaction = fac.ValueRO.Value;
                    break;
                }
            }

            // ── Minimap beacon (2026-08-04, "where did the Shardroot go?"):
            // once unearthed, a slow GOLD pulse follows the artifact wherever
            // it is — ground drop, courier, hero, or enshrining temple — so
            // the One Ring is never invisible again. (The old beacon promise
            // predated the UI redesign and had no surviving consumer.)
            if (state.Found != 0)
            {
                _beaconAcc += SystemAPI.Time.DeltaTime;
                if (_beaconAcc >= BeaconInterval)
                {
                    _beaconAcc = 0f;
                    foreach (var xf in SystemAPI
                        .Query<RefRO<LocalTransform>>()
                        .WithAll<ShardrootTag>())
                    {
                        TheWaningBorder.UI.GameUI.MinimapPings.Post(
                            xf.ValueRO.Position,
                            TheWaningBorder.UI.GameUI.MinimapPings.Power,
                            BeaconInterval + 0.5f, big: true);
                        break; // there is only ever one Shardroot
                    }
                }
            }

            em.SetComponentData(stateEntity, state);
        }

        private float _beaconAcc;
        private const float BeaconInterval = 4f;

        /// <summary>
        /// Called by the verb systems when a well is claimed (Cleansed /
        /// Converted / Destroyed). If the well is the seeded host and the
        /// artifact hasn't surfaced yet, it drops here — announced to all.
        /// </summary>
        public static void TryAward(EntityManager em, Entity node, float3 pos, RitualKind kind)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadWrite<ShardrootState>());
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            var state = em.GetComponentData<ShardrootState>(ents[0]);
            if (state.HostChosen == 0 || state.Found != 0) return;
            if (state.HostNode != node) return;

            state.Found = 1;
            em.SetComponentData(ents[0], state);

            var pickup = GlowPickup.Create(em, pos + new float3(3f, 0f, 3f),
                kind, ShardrootState.ShardrootPower);
            em.AddComponent<ShardrootTag>(pickup);
            MakePersistent(em, pickup);

            PlayerNotificationSystem.Notify(Loc.T("The SHARDROOT has been unearthed!"));
            TWBLog.Log("[Shardroot] artifact dropped at the host well");
        }

        /// <summary>Shardroot pickups never despawn (the artifact is
        /// persistent by canon).</summary>
        public static void MakePersistent(EntityManager em, Entity pickup)
        {
            if (!em.HasComponent<GlowPickupState>(pickup)) return;
            var ps = em.GetComponentData<GlowPickupState>(pickup);
            ps.TimeRemaining = float.MaxValue;
            em.SetComponentData(pickup, ps);
        }

        private static bool TryFindOwnHall(EntityManager em, Faction faction,
            float3 pos, float radius, out Entity hall)
        {
            hall = Entity.Null;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
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
                em.SetComponentData(hero, new MoveSpeed { Value = 4.2f });
            if (em.HasComponent<LineOfSight>(hero))
                em.SetComponentData(hero, new LineOfSight { Radius = 30f });

            em.AddComponent<ShardrootTag>(hero);
            em.AddComponent<ShardboundHeroTag>(hero);
            em.AddComponentData(hero, new GlowCarrier
            {
                Amount = ShardrootState.ShardrootPower,
                Source = RitualKind.Purification,
            });

            // The courier hands the artifact over.
            if (em.HasComponent<ShardrootTag>(courier)) em.RemoveComponent<ShardrootTag>(courier);
            if (em.HasComponent<GlowCarrier>(courier)) em.RemoveComponent<GlowCarrier>(courier);

            PlayerNotificationSystem.Notify(string.Format(Loc.T("{0} has awakened the SHARDBOUND HERO!"), faction));
            TWBLog.Log($"[Shardroot] {faction} hero awakened ({heroId} body)");
        }
    }
}
