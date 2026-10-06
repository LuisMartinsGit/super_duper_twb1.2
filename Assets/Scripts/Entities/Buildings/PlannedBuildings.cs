// PlannedBuildings.cs
// The planned-building stage (docs/Design/Planned_Buildings.md).
//
//   order given  ->  PLAN (white preview, owner-only, paid, no collider role)
//   worker arrives  ->  BREAK GROUND: the real under-construction site
//
// A plan reserves its OWNER's tiles only: the same player cannot plan two
// buildings on the same cells, but two opposing players may plan the same
// spot. Whoever breaks ground first wins it; every other faction's plan that
// overlaps the new site is cancelled and refunded on the spot.
//
// Everything here runs in the simulation (the lockstep executor and
// PlannedBuildingSystem), from replicated state only, in query order — every
// peer creates, converts and cancels the same plans on the same tick.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Entities
{
    public static class PlannedBuildings
    {
        static readonly ComponentType[] QT_Plans =
        {
            ComponentType.ReadOnly<PlannedBuilding>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static readonly ComponentType[] QT_Workers =
        {
            ComponentType.ReadOnly<CanBuild>(),
            ComponentType.ReadOnly<BuildCommand>(),
        };
        static readonly ComponentType[] QT_Queues =
        {
            ComponentType.ReadOnly<CanBuild>(),
            ComponentType.ReadOnly<QueuedBuildSite>(),
        };
        static CachedEntityQuery QC_Plans, QC_Workers, QC_Queues;

        // ── A REFUSED PLAN WAITS BEFORE IT IS CANCELLED (2026-10-05) ──
        // BreakGround used to cancel and refund on the first refusal, and the
        // AI, seeing no plan, paid for a new one on its next think: 55 pay-
        // and-refund cycles of one Gatherer's Hut in the first 15 s of a
        // match (6,600 supplies through the ledger on a 164-supply bank),
        // and 146 cycles in one 15 s period at minute 18. Most refusals are
        // transient (ground still being claimed at match start, a unit on
        // the footprint), so the plan now keeps its spot and retries every
        // BreakGroundRetrySeconds; one still refused after
        // BreakGroundGraceSeconds is cancelled and refunded, and the spot is
        // remembered so the AI's site search avoids it (IsRecentlyRefused).
        // Engine timing, not tuning: the grace is how long a transient
        // refusal may last, the memory how long a spot stays suspect.
        private const float BreakGroundRetrySeconds = 2f;
        private const float BreakGroundGraceSeconds = 20f;
        private const float RefusedSpotMemorySeconds = 180f;

        private struct RefusedSpot { public Faction Faction; public float3 Pos; public float At; }
        private static readonly System.Collections.Generic.List<RefusedSpot> _refusedSpots
            = new System.Collections.Generic.List<RefusedSpot>();

        /// <summary>Did one of <paramref name="faction"/>'s plans get cancelled
        /// for a persistent refusal within <paramref name="radius"/> of
        /// <paramref name="pos"/> in the last RefusedSpotMemorySeconds?</summary>
        public static bool IsRecentlyRefused(Faction faction, float3 pos, float radius, float now)
        {
            float r2 = radius * radius;
            for (int i = _refusedSpots.Count - 1; i >= 0; i--)
            {
                var s = _refusedSpots[i];
                if (now - s.At > RefusedSpotMemorySeconds) { _refusedSpots.RemoveAt(i); continue; }
                if (s.Faction == faction && math.distancesq(s.Pos.xz, pos.xz) <= r2) return true;
            }
            return false;
        }

        /// <summary>A new match: forget every refused spot.</summary>
        public static void ResetRefusedSpots() => _refusedSpots.Clear();

        /// <summary>
        /// Does this building go through a plan? Everything a worker raises
        /// does. The landmarks self-construct with no worker and are placed
        /// as sites directly, as before.
        /// </summary>
        public static bool UsesPlan(string buildingId)
            => !BuildingFactory.IsChoiceBuilding(buildingId);

        /// <summary>Create the plan. The caller has validated and SPENT.</summary>
        public static Entity Create(EntityManager em, string buildingId, float3 position,
            Faction faction, float yawDegrees, Cost paid, int paidReligion)
        {
            var pos = BuildGrid.Snap(position, buildingId);
            var size = BuildingSizeConfig.GetSize(buildingId);
            var e = em.CreateEntity();
            em.AddComponentData(e, new PlannedBuilding
            {
                BuildingId = new FixedString64Bytes(buildingId),
                Yaw = yawDegrees,
                PaidReligion = paidReligion,
            });
            em.AddComponentData(e, new PaidBuildCost { Value = paid });
            em.AddComponentData(e, new FactionTag { Value = faction });
            em.AddComponentData(e, LocalTransform.FromPositionRotationScale(
                pos, quaternion.RotateY(math.radians(yawDegrees)), 1f));
            em.AddComponentData(e, new BuildingSize { Width = size.x, Height = size.y });
            em.AddComponentData(e, new Radius { Value = BuildingSizeConfig.GetLegacyRadius(size) });
            em.AddComponentData(e, new PresentationId { Id = BuildingFactory.GetPresentationId(buildingId) });
            em.AddComponentData(e, new DisplayName { Value = DisplayNames.ForBuildingFixed(buildingId) });
            // A Build order can name the plan in the same lockstep tick.
            em.AddComponentData(e, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = NetworkIdGenerator.CurrentTick,
            });
            return e;
        }

        /// <summary>The building id a plan stands for.</summary>
        public static string IdOf(EntityManager em, Entity plan)
            => em.GetComponentData<PlannedBuilding>(plan).BuildingId.ToString();

        /// <summary>
        /// True when a footprint at <paramref name="position"/> overlaps one of
        /// <paramref name="faction"/>'s OWN plans — a player cannot plan two
        /// buildings on the same tiles. Other factions' plans never block.
        /// </summary>
        public static bool OverlapsOwnPlan(EntityManager em, Faction faction, float3 position, int2 size,
            Entity ignore = default)
            => FirstOverlap(em, position, size, faction, ownOnly: true, ignore) != Entity.Null;

        /// <summary>How many plans of this id the faction holds (caps count them).</summary>
        public static int CountOf(EntityManager em, Faction faction, string buildingId)
        {
            var q = QC_Plans.Get(em, QT_Plans);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction && IdOf(em, ents[i]) == buildingId) n++;
            return n;
        }

        /// <summary>Every plan the faction holds, of any kind.</summary>
        public static int CountAll(EntityManager em, Faction faction)
        {
            var q = QC_Plans.Get(em, QT_Plans);
            if (q.IsEmptyIgnoreFilter) return 0;
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < facs.Length; i++) if (facs[i].Value == faction) n++;
            return n;
        }

        /// <summary>How many plans of any of these ids the faction holds.</summary>
        public static int CountOf(EntityManager em, Faction faction, string[] buildingIds)
        {
            if (buildingIds == null || buildingIds.Length == 0) return 0;
            var q = QC_Plans.Get(em, QT_Plans);
            if (q.IsEmptyIgnoreFilter) return 0;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                string id = IdOf(em, ents[i]);
                for (int k = 0; k < buildingIds.Length; k++)
                    if (buildingIds[k] == id) { n++; break; }
            }
            return n;
        }

        /// <summary>
        /// The building ids a type tag stands for, so tag-counting callers (the
        /// AI's growth targets and one-at-a-time throttles) can count PLANS too
        /// — a plan carries no tags. Without it the AI re-places the same
        /// building every think until its worker breaks ground. Mirrors
        /// BuildingIds.Of; an unlisted tag has no plans.
        /// </summary>
        public static string[] IdsFor<T>() where T : unmanaged, IComponentData
            => TagIds.TryGetValue(typeof(T), out var ids) ? ids : System.Array.Empty<string>();

        private static readonly System.Collections.Generic.Dictionary<System.Type, string[]> TagIds =
            new System.Collections.Generic.Dictionary<System.Type, string[]>
        {
            { typeof(HallTag),           new[] { "Fortress" } },
            { typeof(FortressTag),       new[] { "Fortress" } },
            { typeof(BarracksTag),       new[] { "Barracks" } },
            { typeof(ArcheryRangeTag),   new[] { "ArcheryRange" } },
            { typeof(GathererHutTag),    new[] { "GatherersHut" } },
            { typeof(HutTag),            new[] { "Hut" } },
            { typeof(TempleOfRidanTag),  new[] { "TempleOfRidan" } },
            { typeof(MineTag),           new[] { "Mine" } },
            { typeof(VeilstoneMineTag),  new[] { "VeilstoneMine" } },
            { typeof(TradingOutpostTag), new[] { "Alanthor_TradingOutpost" } },
            { typeof(WatchTowerTag),     new[] { "Alanthor_Tower" } },
            { typeof(SiegeYardTag),      new[] { "Alanthor_SiegeYard" } },
            { typeof(RoyalStableTag),    new[] { "Alanthor_RoyalStable" } },
            { typeof(ReliquaryTag),      new[] { "Sect_Reliquary" } },
            { typeof(MendingHallTag),    new[] { "Sect_MendingHall" } },
            { typeof(StoneholdTag),      new[] { "Sect_Stonehold" } },
            { typeof(VeilworksTag),      new[] { "Sect_Veilworks" } },
            { typeof(MusterYardTag),     new[] { "Sect_MusterYard" } },
        };

        /// <summary>The faction's plan nearest <paramref name="position"/>
        /// within <paramref name="radius"/>, or Null.</summary>
        public static Entity FindOwnPlanNear(EntityManager em, Faction faction, float3 position, float radius)
        {
            var q = QC_Plans.Get(em, QT_Plans);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            Entity best = Entity.Null;
            float bestD = radius * radius;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                float dx = xfs[i].Position.x - position.x, dz = xfs[i].Position.z - position.z;
                float d = dx * dx + dz * dz;
                if (d <= bestD) { bestD = d; best = ents[i]; }
            }
            return best;
        }

        // ── Break ground ────────────────────────────────────────────────

        /// <summary>
        /// A worker has arrived: turn the plan into the real under-construction
        /// site. The spot is re-checked against the world as it is NOW — a real
        /// building, a resource node, ground the faction no longer holds — and a
        /// plan that can no longer stand is cancelled and refunded. On success,
        /// every OTHER faction's plan overlapping the site is cancelled and
        /// refunded, and every worker headed for this plan is re-pointed at the
        /// site. Returns the site, or Null when the plan was cancelled.
        /// </summary>
        public static Entity BreakGround(EntityManager em, Entity plan, Entity worker)
        {
            if (!em.Exists(plan) || !em.HasComponent<PlannedBuilding>(plan)) return Entity.Null;
            var data = em.GetComponentData<PlannedBuilding>(plan);
            string id = data.BuildingId.ToString();
            var faction = em.GetComponentData<FactionTag>(plan).Value;
            var pos = em.GetComponentData<LocalTransform>(plan).Position;
            var size = BuildingSizeConfig.GetSize(id);
            var paid = em.GetComponentData<PaidBuildCost>(plan).Value;

            var refusal = TheWaningBorder.Core.Commands.CommandRouter.CheckBreakGround(em, id, pos, faction, worker);
            float now = SimClock.Now;
            if (refusal != TheWaningBorder.World.Regions.PlacementRefusal.None)
            {
                // Grace, then cancel (see the constants above).
                if (data.RefusedSince <= 0f) data.RefusedSince = now;
                data.NextBreakGroundAt = now + BreakGroundRetrySeconds;
                if (now - data.RefusedSince < BreakGroundGraceSeconds)
                {
                    em.SetComponentData(plan, data);
                    return Entity.Null;
                }
                _refusedSpots.Add(new RefusedSpot { Faction = faction, Pos = pos, At = now });
                string why = TheWaningBorder.World.Regions.PlacementRefusalText.Of(refusal, id);
                UnityEngine.Debug.Log(string.Format("[Plans] {0} {1} at ({2:0},{3:0}) cancelled after {4:0}s: {5}",
                    faction, id, pos.x, pos.z, BreakGroundGraceSeconds, why));
                Cancel(em, plan, refund: true, reason: why);
                return Entity.Null;
            }

            // Whoever breaks ground first takes the spot.
            Entity other;
            while ((other = FirstOverlap(em, pos, size, faction, ownOnly: false, ignore: plan)) != Entity.Null)
                Cancel(em, other, refund: true,
                    reason: TheWaningBorder.Core.Localization.Loc.T("Another player started building there"));

            var site = TheWaningBorder.Core.Commands.CommandRouter.CreateConstructionSite(
                em, id, pos, faction, data.Yaw, paid);
            Retarget(em, plan, site);
            em.DestroyEntity(plan);
            return site;
        }

        /// <summary>
        /// Remove a plan, refunding what was paid for it when
        /// <paramref name="refund"/>, and stand down every worker headed for
        /// it. <paramref name="reason"/>, when given, is told to the owner if
        /// that is the local player.
        /// </summary>
        public static void Cancel(EntityManager em, Entity plan, bool refund, string reason = null)
        {
            if (!em.Exists(plan) || !em.HasComponent<PlannedBuilding>(plan)) return;
            var faction = em.GetComponentData<FactionTag>(plan).Value;
            if (refund)
            {
                FactionEconomy.Add(em, faction, em.GetComponentData<PaidBuildCost>(plan).Value);
                int rp = em.GetComponentData<PlannedBuilding>(plan).PaidReligion;
                if (rp > 0) FactionReligionPointsHelper.Refund(em, faction, rp);
            }
            if (reason != null && faction == GameSettings.LocalPlayerFaction)
                SimSignals.Notify(string.Format(
                    TheWaningBorder.Core.Localization.Loc.T("Planned {0} cancelled — {1}"),
                    DisplayNames.ForBuilding(IdOf(em, plan)), reason));
            Retarget(em, plan, Entity.Null);
            em.DestroyEntity(plan);
        }

        /// <summary>Point every worker's order (current and queued) that names
        /// <paramref name="plan"/> at <paramref name="site"/> — or, with Null,
        /// drop the current order so the worker is free again.</summary>
        private static void Retarget(EntityManager em, Entity plan, Entity site)
        {
            var bq = QC_Workers.Get(em, QT_Workers);
            using (var workers = bq.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < workers.Length; i++)
                {
                    var cmd = em.GetComponentData<BuildCommand>(workers[i]);
                    if (cmd.TargetBuilding != plan) continue;
                    if (site != Entity.Null)
                    {
                        cmd.TargetBuilding = site;
                        em.SetComponentData(workers[i], cmd);
                    }
                    else em.RemoveComponent<BuildCommand>(workers[i]);
                }
            }
            var qq = QC_Queues.Get(em, QT_Queues);
            using var queued = qq.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < queued.Length; i++)
            {
                var buf = em.GetBuffer<QueuedBuildSite>(queued[i]);
                for (int k = buf.Length - 1; k >= 0; k--)
                {
                    if (buf[k].TargetBuilding != plan) continue;
                    if (site == Entity.Null) { buf.RemoveAt(k); continue; }
                    var e = buf[k];
                    e.TargetBuilding = site;
                    buf[k] = e;
                }
            }
        }

        /// <summary>The first plan (query order) whose footprint overlaps the
        /// given one: the faction's own only, or every OTHER faction's.</summary>
        private static Entity FirstOverlap(EntityManager em, float3 position, int2 size, Faction faction,
            bool ownOnly, Entity ignore)
        {
            BuildCommandHelper.FootprintAabb(position, size, out float2 min, out float2 max);
            var q = QC_Plans.Get(em, QT_Plans);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (ents[i] == ignore) continue;
                if ((facs[i].Value == faction) != ownOnly) continue;
                var s = em.GetComponentData<BuildingSize>(ents[i]);
                BuildCommandHelper.FootprintAabb(xfs[i].Position, new int2(s.Width, s.Height),
                    out float2 pmin, out float2 pmax);
                if (min.x < pmax.x && max.x > pmin.x && min.y < pmax.y && max.y > pmin.y)
                    return ents[i];
            }
            return Entity.Null;
        }
    }
}
