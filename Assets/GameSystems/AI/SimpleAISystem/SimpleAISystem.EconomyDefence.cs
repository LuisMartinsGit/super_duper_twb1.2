// SimpleAISystem.EconomyDefence.cs
// Defending the economy, rebuilding it, and pausing the savings while it is
// in distress (docs/Design/Game_AI.md § 5i).
// Partial of SimpleAISystem.cs.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY (2026-10-05, SunderedCrown in-editor match)
//
// Blue (Expert) started next to a curse node. The node's garrison raided its
// supply sites all match: 13 Gatherer's Huts, 8 Huts and 6 Mines lost, the
// supply income down from ~19/s to ~4/s while Yellow earned 28/s. Nothing
// answered those raids:
//
//   * the posture's Defend response named its threat from the units in the
//     defend ring EXCLUDING the curse, so a curse raid on a hut entered Defend,
//     disbanded every mission, and dispatched no defender at all;
//   * the reclaim squad sent whoever was idle at parity, one Spearman at a
//     time (38 RECLAIM orders, "1 units vs curse node" eleven times in one
//     minute) into a node it could not beat;
//   * a lost hut was rebuilt only when the 15 s extractor walk found the bank
//     at 120 supplies — and the capital's L2 savings goal (447 supplies), the
//     Fortress pot and the Outpost pot held every trickle, the army floor
//     included ("pivotal hold (saving) short supplies"). Huts came back 13
//     minutes after their claim; the army stayed at 3-17 against a desired
//     45, and Yellow's wave razed the Fortress at 25:47.
//
// So: an attack on an extractor, a house or a worker in held ground gets a
// RESPONSE sized to the attacker (AIEngagement, with the tier's margin, never
// at parity); a lost extractor is owed a REBUILD that goes first (a strict
// savings goal, the extractor walk every few seconds, its kind first); a
// faction with no worker retrains one bank-direct; and while the economy is
// in DISTRESS (supply income collapsed against what it has earned, under
// attack, or owing a rebuild) its ordinary savings goals pause
// (AIPivotalReserve.SetSuspended), the capital's level saving among them.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;
using RebuildOutpost = TheWaningBorder.Entities.TradingOutpost;   // avoids the DC0062 Entities.ForEach misread

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        #region Cached queries

        static readonly ComponentType[] QT_WorkerUnderAttack =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<CanBuild>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<LastAttackerEntity>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_WorkerUnderAttack;

        #endregion

        // ── Match-scoped host state (the AI runs host-only) ────────────────

        /// <summary>A standing-army detachment answering one attack on the
        /// economy. Its members are held out of every other draft
        /// (IsClaimSquadMember) until it is released.</summary>
        private sealed class EconResponse
        {
            public float3 FightAt;
            public float StartedAt;
            public float NextTopUpAt;
            public readonly List<Entity> Members = new List<Entity>();
        }

        private int _econDefEpoch = -1;
        private readonly Dictionary<int, List<EconResponse>> _econResponses = new Dictionary<int, List<EconResponse>>();
        private readonly HashSet<Entity> _econResponders = new HashSet<Entity>();
        /// <summary>(faction, 20 m cell) -> first / last time an attack was
        /// seen there: the tier's response delay runs from First.</summary>
        private readonly Dictionary<(int faction, int cx, int cz), (float First, float Last)> _econAttackSeen
            = new Dictionary<(int, int, int), (float, float)>();
        private readonly Dictionary<int, float> _econLastAttackAt = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nextEconHeldLog = new Dictionary<int, float>();
        /// <summary>Per faction: next "no workers left — retrain blocked" line.</summary>
        private readonly Dictionary<Faction, float> _nextCrewLog = new Dictionary<Faction, float>();

        // Rebuild bookkeeping: extractor counts last think, and what is owed.
        private readonly Dictionary<(int faction, int kind), int> _extractorLast = new Dictionary<(int, int), int>();
        private readonly Dictionary<(int faction, int kind), (int Target, float Until)> _rebuildOwed
            = new Dictionary<(int, int), (int, float)>();
        private readonly Dictionary<(int faction, int kind), int> _rebuildRefusals = new Dictionary<(int, int), int>();

        /// <summary>Every extractor entity seen last think (faction, kind,
        /// where it stood) — one gone since is a loss AT A SITE.</summary>
        private readonly Dictionary<Entity, (int Faction, int Kind, float3 Pos)> _extractorSeen
            = new Dictionary<Entity, (int, int, float3)>();
        /// <summary>Extractor sites lost, with how often and when last.</summary>
        private readonly List<(int Faction, float3 Pos, int Losses, float LastAt)> _lostSites
            = new List<(int, float3, int, float)>();
        private readonly List<Entity> _extractorGone = new List<Entity>();
        private readonly HashSet<Entity> _extractorNow = new HashSet<Entity>();

        // Distress bookkeeping.
        private readonly Dictionary<int, (float Value, float At)> _supplyExpected = new Dictionary<int, (float, float)>();
        private readonly HashSet<int> _supplyCollapsed = new HashSet<int>();
        private readonly Dictionary<int, float> _nextDistressLog = new Dictionary<int, float>();

        // Host scratch (main thread only).
        private readonly List<(float3 Site, float3 FightAt)> _econIncidents = new List<(float3, float3)>();
        private readonly List<(Entity E, float D2)> _econPool = new List<(Entity, float)>();
        private readonly List<Entity> _econDraft = new List<Entity>();
        private readonly HashSet<Entity> _econEnrolled = new HashSet<Entity>();
        private readonly List<(int, int, int)> _econSeenDrop = new List<(int, int, int)>();

        /// <summary>AIPivotalReserve key of an owed extractor rebuild (strict:
        /// it outranks the capital, the Fortress and the Outpost pots).</summary>
        private const string ExtractorRebuildKey = "ExtractorRebuild";

        /// <summary>The extractors a loss makes owed, by kind index. A roster
        /// table of building ids, not tuning.</summary>
        private static readonly string[] RebuildIds =
            { "GatherersHut", "Mine", "VeilstoneMine", RebuildOutpost.BuildingId };

        private void ResetEconomyDefenceIfNewMatch()
        {
            if (_econDefEpoch == SimCadence.Epoch) return;
            _econDefEpoch = SimCadence.Epoch;
            _econResponses.Clear();
            _econResponders.Clear();
            _econAttackSeen.Clear();
            _econLastAttackAt.Clear();
            _nextEconHeldLog.Clear();
            _nextCrewLog.Clear();
            _extractorLast.Clear();
            _rebuildOwed.Clear();
            _rebuildRefusals.Clear();
            _extractorSeen.Clear();
            _lostSites.Clear();
            _reclaimCooldown.Clear();
            _supplyExpected.Clear();
            _supplyCollapsed.Clear();
            _nextDistressLog.Clear();
            _waveIntelHold.Clear();
            _nextCurseClear.Clear();
            _nextCurseClearLog.Clear();
            _curseNodeSeen.Clear();
            _nextCurseIntelLog.Clear();
            _religionHuntRoster.Clear();
            _outpostBuyOff.Clear();
        }

        private List<EconResponse> EconResponsesOf(int key)
        {
            if (!_econResponses.TryGetValue(key, out var list))
                _econResponses[key] = list = new List<EconResponse>();
            return list;
        }

        /// <summary>True while this soldier serves an economy response.</summary>
        private bool IsEconResponder(Entity e) => _econResponders.Contains(e);

        // ─────────────────────────────────────────────────────────────────
        // 1. THE RESPONSE
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Once a think: release finished responses, find every extractor,
        /// house and worker in held ground under live attack, and answer each
        /// (after the tier's delay) with standing soldiers whose power, with
        /// ours already there, beats the attacker's by the tier's margin.
        /// Not enough free power: nothing is sent — a feeder squad is the
        /// failure this replaces.
        /// </summary>
        private void TickEconomyDefence(EntityManager em, Faction faction, in AIDifficultyProfile profile, float now)
        {
            ResetEconomyDefenceIfNewMatch();
            int key = (int)faction;
            var list = EconResponsesOf(key);
            float3 home = HomeAnchor(em, faction);

            // ── Release what is done ──
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var r = list[i];
                for (int m = r.Members.Count - 1; m >= 0; m--)
                {
                    var u = r.Members[m];
                    if (em.Exists(u) && em.HasComponent<Health>(u) && em.GetComponentData<Health>(u).Value > 0) continue;
                    r.Members.RemoveAt(m);
                    _econResponders.Remove(u);
                }
                bool lost = r.Members.Count == 0;
                bool cleared = TacticalQuery.EnemyStrengthInRadius(em, faction, r.FightAt,
                                   Cfg.economyDefenceAssessRadius) <= 0;
                bool timedOut = now - r.StartedAt > Cfg.economyDefenceTimeoutSeconds;
                if (!lost && !cleared && !timedOut) continue;
                for (int m = 0; m < r.Members.Count; m++) _econResponders.Remove(r.Members[m]);
                if (!lost && FindFactionBuilding<HallTag>(em, faction) != Entity.Null)
                    AICommon.IssueGroupOrder(em, r.Members, home, attackMove: true, Cfg.waveArrivedRadius);
                AILogger.Log(faction, "DEFEND",
                    $"response at ({r.FightAt.x:0},{r.FightAt.z:0}) released " +
                    (lost ? "(all fell)" : cleared ? $"(attackers gone, {r.Members.Count} return home)"
                                                   : $"(timed out, {r.Members.Count} return home)"));
                list.RemoveAt(i);
            }

            // ── Find the attacks ──
            CollectEconomyIncidents(em, faction);
            if (_econIncidents.Count > 0) _econLastAttackAt[key] = now;

            // Forget cells not attacked for a while, so the next raid there
            // waits out the tier's delay afresh.
            _econSeenDrop.Clear();
            foreach (var kv in _econAttackSeen)
                if (kv.Key.faction == key && now - kv.Value.Last > Cfg.economyAttackLingerSeconds)
                    _econSeenDrop.Add(kv.Key);
            for (int i = 0; i < _econSeenDrop.Count; i++) _econAttackSeen.Remove(_econSeenDrop[i]);

            // ── Answer them ──
            int answered = 0;
            float assessR2 = Cfg.economyDefenceAssessRadius * Cfg.economyDefenceAssessRadius;
            for (int n = 0; n < _econIncidents.Count && answered < math.max(1, Cfg.economyDefenceSitesPerThink); n++)
            {
                var (site, fightAt) = _econIncidents[n];
                var cell = (key, (int)math.floor(site.x / 20f), (int)math.floor(site.z / 20f));
                if (!_econAttackSeen.TryGetValue(cell, out var seen)) seen = (now, now);
                else seen.Last = now;
                _econAttackSeen[cell] = seen;
                // A slower tier lets a raid run a while before it answers.
                if (now - seen.First < profile.EconomyDefenceDelaySeconds) continue;

                EconResponse resp = null;
                for (int i = 0; i < list.Count && resp == null; i++)
                    if (math.distancesq(list[i].FightAt.xz, fightAt.xz) <= assessR2) resp = list[i];
                if (resp != null && now < resp.NextTopUpAt) continue;

                var live = AIEngagement.Assess(em, faction, fightAt, Cfg.economyDefenceAssessRadius);
                if (live.EnemyPower <= 0) continue;
                float margin = math.max(1.05f, profile.EconomyDefenceMargin);
                int need = (int)math.ceil(live.EnemyPower * margin);
                // Ours already in the band, plus responders still on the road.
                int have = live.MyPower;
                if (resp != null)
                    for (int m = 0; m < resp.Members.Count; m++)
                    {
                        var u = resp.Members[m];
                        if (math.distancesq(em.GetComponentData<LocalTransform>(u).Position.xz, fightAt.xz) > assessR2)
                            have += TacticalQuery.UnitStrength(em, u);
                    }
                if (have >= need)
                {
                    if (resp != null) resp.NextTopUpAt = now + Cfg.economyDefenceTopUpSeconds;
                    continue;
                }

                BuildEconDefencePool(em, faction, fightAt);
                _econDraft.Clear();
                int power = have;
                for (int i = 0; i < _econPool.Count && power < need; i++)
                {
                    _econDraft.Add(_econPool[i].E);
                    power += TacticalQuery.UnitStrength(em, _econPool[i].E);
                }
                if (power < need)
                {
                    // NEVER A FEEDER: the attacker outweighs everything free.
                    if (!_nextEconHeldLog.TryGetValue(key, out float nl) || now >= nl)
                    {
                        _nextEconHeldLog[key] = now + Cfg.claimLogInterval;
                        AILogger.Log(faction, "DEFEND",
                            $"held at ({fightAt.x:0},{fightAt.z:0}): {_econPool.Count} free unit(s) bring " +
                            $"power {power} vs {live.EnemyPower} (needs x{margin:0.00})");
                    }
                    continue;
                }

                // One formation, arrivals re-poked (AICommon.IssueGroupOrder).
                AICommon.IssueGroupOrder(em, _econDraft, fightAt, attackMove: true, Cfg.waveArrivedRadius);
                if (resp == null)
                {
                    resp = new EconResponse { StartedAt = now };
                    list.Add(resp);
                }
                resp.FightAt = fightAt;
                resp.NextTopUpAt = now + Cfg.economyDefenceTopUpSeconds;
                for (int i = 0; i < _econDraft.Count; i++)
                {
                    resp.Members.Add(_econDraft[i]);
                    _econResponders.Add(_econDraft[i]);
                }
                answered++;
                AILogger.Log(faction, "DEFEND",
                    $"response {_econDraft.Count} vs attacker at ({fightAt.x:0},{fightAt.z:0}) " +
                    $"(power {power} vs {live.EnemyPower})");
            }
        }

        /// <summary>Every extractor, house and worker of ours in held ground
        /// whose last attacker is alive, hostile (the curse included) and
        /// still beside it — one incident per 20 m, in query order.</summary>
        private void CollectEconomyIncidents(EntityManager em, Faction faction)
        {
            _econIncidents.Clear();
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;

            var bq = QC_BuildingTagFactionTagHealthLastAttackerEntityLocalTransform.Get(
                em, QT_BuildingTagFactionTagHealthLastAttackerEntityLocalTransform);
            using (var ents = bq.ToEntityArray(Allocator.Temp))
            using (var facs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var hps = bq.ToComponentDataArray<Health>(Allocator.Temp))
            using (var atk = bq.ToComponentDataArray<LastAttackerEntity>(Allocator.Temp))
            using (var xfs = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction || hps[i].Value <= 0) continue;
                    var e = ents[i];
                    if (!(em.HasComponent<GathererHutTag>(e) || em.HasComponent<MineTag>(e)
                          || em.HasComponent<VeilstoneMineTag>(e) || em.HasComponent<TradingOutpostTag>(e)
                          || em.HasComponent<HutTag>(e))) continue;
                    TryAddEconIncident(em, faction, xfs[i].Position, atk[i].Value);
                }

            var wq = QC_WorkerUnderAttack.Get(em, QT_WorkerUnderAttack);
            using (var facs = wq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var hps = wq.ToComponentDataArray<Health>(Allocator.Temp))
            using (var atk = wq.ToComponentDataArray<LastAttackerEntity>(Allocator.Temp))
            using (var xfs = wq.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < facs.Length; i++)
                {
                    if (facs[i].Value != faction || hps[i].Value <= 0) continue;
                    TryAddEconIncident(em, faction, xfs[i].Position, atk[i].Value);
                }

            // GROUND BEING TAKEN (2026-10-05, Game_AI.md 5i). A hostile
            // standing on held ground drains its meter (Territory_Claims.md
            // §2) without hitting a single building, and nothing answered
            // it: Expert lost the strip north of its home at minute 11 and
            // never reacted. The challenger's unit nearest the territory's
            // seed is the incident, on the same delay and margin as a raid.
            var mine = TerritoryOwnership.TerritoriesOf(faction);
            if (mine.Count > 0)
            {
                var uq = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
                using var uents = uq.ToEntityArray(Allocator.Temp);
                using var ufacs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var uxfs = uq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int t = 0; t < mine.Count; t++)
                {
                    int r = mine[t];
                    int ch = TerritoryOwnership.ChallengerOf(r);
                    if (ch < 0 || ch == (int)faction || !Alliances.AreHostile(faction, (Faction)ch)) continue;
                    var seed = RegionMap.SeedOf(r);
                    Entity best = Entity.Null;
                    float3 bestAt = default;
                    float bd = float.MaxValue;
                    for (int i = 0; i < uents.Length; i++)
                    {
                        if ((int)ufacs[i].Value != ch) continue;
                        var p = uxfs[i].Position;
                        if (RegionMap.RegionAt(p.x, p.z) != r) continue;
                        float dd = (p.x - seed.x) * (p.x - seed.x) + (p.z - seed.y) * (p.z - seed.y);
                        if (dd < bd) { bd = dd; best = uents[i]; bestAt = p; }
                    }
                    if (best != Entity.Null) TryAddEconIncident(em, faction, bestAt, best);
                }
            }
        }

        private void TryAddEconIncident(EntityManager em, Faction faction, float3 site, Entity attacker)
        {
            int r = RegionMap.RegionAt(site.x, site.z);
            if (r == RegionMap.None || TerritoryOwnership.OwnerOf(r) != (int)faction) return;
            if (attacker == Entity.Null || !em.Exists(attacker)) return;
            if (!em.HasComponent<FactionTag>(attacker) || !em.HasComponent<LocalTransform>(attacker)) return;
            if (!Alliances.AreHostile(faction, em.GetComponentData<FactionTag>(attacker).Value)) return;
            if (em.HasComponent<Health>(attacker) && em.GetComponentData<Health>(attacker).Value <= 0) return;
            float3 at = em.GetComponentData<LocalTransform>(attacker).Position;
            float probe = Cfg.economyDefenceProbeRadius;
            if (math.distancesq(at.xz, site.xz) > probe * probe) return;
            for (int i = 0; i < _econIncidents.Count; i++)
                if (math.distancesq(_econIncidents[i].Site.xz, site.xz) < 400f) return;   // one per 20 m
            _econIncidents.Add((site, at));
        }

        /// <summary>Standing soldiers free to answer: combat units within
        /// economyDefenceDraftRadius, not serving a mission, a claim, another
        /// response or a player's order, and not already fighting or on an
        /// attack errand — nearest first (index breaks ties).</summary>
        private void BuildEconDefencePool(EntityManager em, Faction faction, float3 at)
        {
            _econPool.Clear();
            _econEnrolled.Clear();
            foreach (var m in MissionsFor(faction))
                foreach (var member in m.Members)
                    _econEnrolled.Add(member);

            float r2 = Cfg.economyDefenceDraftRadius * Cfg.economyDefenceDraftRadius;
            var mq = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using var ents = mq.ToEntityArray(Allocator.Temp);
            using var tags = mq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = mq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                Entity e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (IsVerbUnit(em, e)) continue;
                if (IsClaimSquadMember(e)) continue;          // claims, responses, curse sorties
                if (_econEnrolled.Contains(e)) continue;      // a wave or a defence mission
                if (em.HasComponent<NotControllableTag>(e)) continue;
                if (TransientState.Active<AttackCommand>(em, e)) continue;
                if (TransientState.Active<AttackMoveTag>(em, e)) continue;
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                float d2 = math.distancesq(xfs[i].Position.xz, at.xz);
                if (d2 > r2) continue;
                _econPool.Add((e, d2));
            }
            _econPool.Sort((a, b) =>
            {
                int c = a.D2.CompareTo(b.D2);
                return c != 0 ? c : a.E.Index.CompareTo(b.E.Index);
            });
        }

        // ─────────────────────────────────────────────────────────────────
        // 2. THE REBUILD
        // ─────────────────────────────────────────────────────────────────

        private static int CountExtractorsOfKind(EntityManager em, Faction faction, int kind)
        {
            switch (kind)
            {
                case 0: return CountFactionBuildings<GathererHutTag>(em, faction);
                case 1: return CountFactionBuildings<MineTag>(em, faction);
                case 2: return CountFactionBuildings<VeilstoneMineTag>(em, faction);
                default: return CountFactionBuildings<TradingOutpostTag>(em, faction);
            }
        }

        /// <summary>Can this faction raise this extractor at all (culture:
        /// Alanthor trades beside veilstone, everyone else mines it)?</summary>
        private static bool RebuildAllowed(EntityManager em, Faction faction, int kind)
        {
            bool alanthor = CultureConfig.GetCompletedCulture(em, faction) == Cultures.Alanthor;
            if (kind == 2 && alanthor) return false;
            if (kind == 3 && !alanthor) return false;
            return TechCatalog.TryGetBuilding(RebuildIds[kind], out var def) && def != null;
        }

        /// <summary>
        /// Once a think: a drop in an extractor count is a loss, and the lost
        /// kind is owed a rebuild for extractorRebuildWindowSeconds (or until
        /// the count is back). While owed, a strict savings goal holds its
        /// price — above the capital's level, the Fortress and the Outposts —
        /// and the extractor walk runs every few seconds with it first.
        /// Ground lost with the building (no free node of the kind left in
        /// held territory) drops the debt.
        /// </summary>
        private void TrackExtractorLosses(EntityManager em, Faction faction, float now)
        {
            ResetEconomyDefenceIfNewMatch();
            int key = (int)faction;
            TrackLostExtractorSites(em, faction, now);
            for (int k = 0; k < RebuildIds.Length; k++)
            {
                int c = CountExtractorsOfKind(em, faction, k);
                var id = (key, k);
                if (_extractorLast.TryGetValue(id, out int last) && c < last && RebuildAllowed(em, faction, k))
                {
                    int target = _rebuildOwed.TryGetValue(id, out var o) && now < o.Until
                        ? math.max(o.Target, last) : last;
                    _rebuildOwed[id] = (target, now + Cfg.extractorRebuildWindowSeconds);
                    AILogger.Log(faction, "ECON",
                        $"lost {last - c} {RebuildIds[k]} — rebuilding first ({c}/{target})");
                }
                _extractorLast[id] = c;
                if (_rebuildOwed.TryGetValue(id, out var owed) && (c >= owed.Target || now >= owed.Until))
                {
                    _rebuildOwed.Remove(id);
                    _rebuildRefusals.Remove(id);
                }
            }

            // The strict savings goal for the first kind still owed.
            if (TryGetRebuildOwed(faction, now, out int kind))
            {
                string bid = RebuildIds[kind];
                if (!RebuildAllowed(em, faction, kind))
                {
                    _rebuildOwed.Remove((key, kind));
                    AIPivotalReserve.Clear(faction, ExtractorRebuildKey);
                    return;
                }
                var owned = _ownedTerritories;
                owned.Clear();
                owned.UnionWith(TerritoryOwnership.TerritoriesOf(faction));
                _freeNodes.Clear();
                CollectFreeNodes(em, faction, bid, owned, _freeNodes);
                if (_freeNodes.Count == 0)
                {
                    _rebuildOwed.Remove((key, kind));
                    AIPivotalReserve.Clear(faction, ExtractorRebuildKey);
                    AILogger.Log(faction, "ECON", $"rebuild of {bid} dropped — no free node in held ground");
                    return;
                }
                var cost = kind == 3
                    ? BuildCosts.For(em, faction, bid, _freeNodes[0])
                    : (TechCatalog.TryGetBuilding(bid, out var def) && def != null ? AICommon.ToCost(def.cost) : default);
                AIPivotalReserve.Set(faction, ExtractorRebuildKey, cost, strict: true);
            }
            else AIPivotalReserve.Clear(faction, ExtractorRebuildKey);
        }

        // ── Sites that keep dying (2026-10-05, Game_AI.md § 5i) ─────────
        //
        // Blue's Gatherer's Hut at (50,-50), 35 m from a curse node, was
        // razed by the node's garrison TEN times: every rebuild walked back
        // into the same reach. A site lost once while a live curse node
        // stands within extractorCurseKeepoutRadius is not rebuilt until that
        // node is gone (the reclaim squad and the curse-clearing sortie are
        // what clear it); a site lost extractorSiteMaxLosses times to anyone
        // rests for extractorSiteBlockSeconds.

        private void TrackLostExtractorSites(EntityManager em, Faction faction, float now)
        {
            int key = (int)faction;
            _extractorNow.Clear();
            NoteExtractorEntities(em, AIQueryCache.TagFactionXf<GathererHutTag>(em), faction, 0);
            NoteExtractorEntities(em, AIQueryCache.TagFactionXf<MineTag>(em), faction, 1);
            NoteExtractorEntities(em, AIQueryCache.TagFactionXf<VeilstoneMineTag>(em), faction, 2);
            NoteExtractorEntities(em, AIQueryCache.TagFactionXf<TradingOutpostTag>(em), faction, 3);

            _extractorGone.Clear();
            foreach (var kv in _extractorSeen)
                if (kv.Value.Faction == key && !_extractorNow.Contains(kv.Key)) _extractorGone.Add(kv.Key);
            // Entity order: deterministic.
            _extractorGone.Sort((a, b) => a.Index.CompareTo(b.Index));
            for (int g = 0; g < _extractorGone.Count; g++)
            {
                var e = _extractorGone[g];
                var lost = _extractorSeen[e];
                _extractorSeen.Remove(e);
                // A building still standing under another tag set (age-up
                // conversion) is no loss.
                if (em.Exists(e) && em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value > 0) continue;
                int found = -1;
                for (int i = 0; i < _lostSites.Count && found < 0; i++)
                    if (_lostSites[i].Faction == key && math.distancesq(_lostSites[i].Pos.xz, lost.Pos.xz) <= 9f)
                        found = i;
                if (found < 0) _lostSites.Add((key, lost.Pos, 1, now));
                else
                {
                    var s = _lostSites[found];
                    // A rested site starts its count afresh.
                    int losses = now - s.LastAt > Cfg.extractorSiteBlockSeconds ? 1 : s.Losses + 1;
                    _lostSites[found] = (key, s.Pos, losses, now);
                }
            }
        }

        private void NoteExtractorEntities(EntityManager em, EntityQuery q, Faction faction, int kind)
        {
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                _extractorNow.Add(ents[i]);
                _extractorSeen[ents[i]] = ((int)faction, kind, xfs[i].Position);
            }
        }

        /// <summary>
        /// May an extractor go on <paramref name="site"/>? No while the site
        /// was lost before and a live curse node still stands within
        /// extractorCurseKeepoutRadius, nor while it has been lost
        /// extractorSiteMaxLosses times inside extractorSiteBlockSeconds.
        /// </summary>
        private bool ExtractorSiteBlocked(EntityManager em, Faction faction, float3 site, float now)
        {
            int key = (int)faction;
            for (int i = 0; i < _lostSites.Count; i++)
            {
                var s = _lostSites[i];
                if (s.Faction != key || math.distancesq(s.Pos.xz, site.xz) > 9f) continue;
                if (s.Losses >= math.max(1, Cfg.extractorSiteMaxLosses)
                    && now - s.LastAt < Cfg.extractorSiteBlockSeconds) return true;
                if (LiveCurseNodeWithin(em, site, Cfg.extractorCurseKeepoutRadius)) return true;
            }
            return false;
        }

        private static bool LiveCurseNodeWithin(EntityManager em, float3 p, float radius)
        {
            var nq = QC_SmallNodeTagLocalTransformHealth.Get(em, QT_SmallNodeTagLocalTransformHealth);
            using var xfs = nq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = nq.ToComponentDataArray<Health>(Allocator.Temp);
            float r2 = radius * radius;
            for (int i = 0; i < xfs.Length; i++)
                if (hps[i].Value > 0 && math.distancesq(xfs[i].Position.xz, p.xz) <= r2) return true;
            return false;
        }

        /// <summary>The extractor walk could not place the owed kind on any
        /// free node; after extractorRebuildMaxRefusals such walks the debt
        /// (and its strict savings goal) is dropped.</summary>
        private void NoteRebuildRefused(Faction faction, int kind, string reason, float now)
        {
            var id = ((int)faction, kind);
            _rebuildRefusals.TryGetValue(id, out int n);
            if (++n < math.max(1, Cfg.extractorRebuildMaxRefusals)) { _rebuildRefusals[id] = n; return; }
            _rebuildRefusals.Remove(id);
            _rebuildOwed.Remove(id);
            AIPivotalReserve.Clear(faction, ExtractorRebuildKey);
            AILogger.Log(faction, "ECON",
                $"rebuild of {RebuildIds[kind]} dropped after {n} refused walks (last: {reason})");
        }

        /// <summary>The first extractor kind (RebuildIds index) still owed a
        /// rebuild.</summary>
        private bool TryGetRebuildOwed(Faction faction, float now, out int kind)
        {
            int key = (int)faction;
            for (int k = 0; k < RebuildIds.Length; k++)
                if (_rebuildOwed.TryGetValue((key, k), out var o) && now < o.Until)
                { kind = k; return true; }
            kind = -1;
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // 3. DISTRESS: THE SAVINGS PAUSE
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Once a think, after the budget measured the income: is the economy
        /// in distress? COLLAPSED (supply income below
        /// economyCollapseIncomeFraction of the expected income — the best
        /// the faction has measured, decaying slowly — until it recovers past
        /// economyRecoveredIncomeFraction), UNDER ATTACK (Defend posture, an
        /// economy response out, or an economy attack in the last
        /// economyAttackLingerSeconds), or REBUILDING (an extractor owed).
        /// While it is, every ordinary savings goal pauses
        /// (AIPivotalReserve.SetSuspended) — the capital's level saving, the
        /// Fortress and Outpost pots — and the Vault releases its supplies;
        /// strict goals (the age-up landmark, a lost trainer, the rebuild)
        /// keep holding.
        /// </summary>
        /// <summary>Since when the collapse reading has disagreed with the
        /// confirmed state (EvaluateEconomyDistress).</summary>
        private readonly Dictionary<int, float> _collapseFlipSince = new Dictionary<int, float>();

        private void EvaluateEconomyDistress(EntityManager em, Faction faction, AIPosture posture, float now)
        {
            ResetEconomyDefenceIfNewMatch();
            int key = (int)faction;
            float income = AIBudget.IncomePerSecond(faction, AIBudget.ResSupplies);

            // The expected income: a decaying high-water mark that may only
            // climb so fast (a windfall is not the norm). While it is still
            // below the collapse floor (the opening) it follows the income.
            float expected;
            if (!_supplyExpected.TryGetValue(key, out var ex)) ex = (income, now);
            else
            {
                float dt = math.max(0f, now - ex.At);
                float decayed = ex.Value * math.pow(0.5f, dt / math.max(1f, Cfg.economyExpectedHalfLifeSeconds));
                float next = decayed;
                if (income > decayed)
                    next = decayed < Cfg.economyCollapseMinExpected
                        ? income
                        : math.min(income, decayed * (1f + math.max(0f, Cfg.economyExpectedRisePerSecond) * dt));
                ex = (next, now);
            }
            _supplyExpected[key] = ex;
            expected = ex.Value;

            bool wasCollapsed = _supplyCollapsed.Contains(key);
            bool reading = expected >= Cfg.economyCollapseMinExpected
                && income < expected * (wasCollapsed ? Cfg.economyRecoveredIncomeFraction
                                                     : Cfg.economyCollapseIncomeFraction);
            // CONFIRMED, NOT SAMPLED (2026-10-05, Mirror Marches M2). The
            // income EMA read spiky against the 15 s ledger tick, and the
            // pause flipped 19 times in each of the first two five-minute
            // spans — every flip a window for the upgrades to spend the
            // Fortress savings. A change of state has to hold for
            // economyCollapseConfirmSeconds before it counts.
            bool collapsed = wasCollapsed;
            if (reading != wasCollapsed)
            {
                if (!_collapseFlipSince.TryGetValue(key, out float since)) _collapseFlipSince[key] = since = now;
                if (now - since >= math.max(0f, Cfg.economyCollapseConfirmSeconds)) collapsed = reading;
            }
            else _collapseFlipSince.Remove(key);
            if (collapsed) _supplyCollapsed.Add(key); else _supplyCollapsed.Remove(key);

            bool underAttack = posture == AIPosture.Defend
                || EconResponsesOf(key).Count > 0
                || (_econLastAttackAt.TryGetValue(key, out float lastAttack)
                    && now - lastAttack <= Cfg.economyAttackLingerSeconds);
            bool rebuilding = TryGetRebuildOwed(faction, now, out int owedKind);
            bool distressed = collapsed || underAttack || rebuilding;

            bool was = AIPivotalReserve.IsSuspended(faction);
            AIPivotalReserve.SetSuspended(faction, distressed);

            if (distressed)
            {
                if (was && _nextDistressLog.TryGetValue(key, out float nextLog) && now < nextLog) return;
                _nextDistressLog[key] = now + System.Math.Max(1f, Cfg.econLogInterval);
                string why = (collapsed ? "" : "income holding")
                    + (underAttack ? "; economy under attack" : "")
                    + (rebuilding ? $"; rebuilding {RebuildIds[owedKind]}" : "");
                AILogger.Log(faction, "ECON",
                    $"savings paused (supply income {income:0.0}/s of expected {expected:0.0}/s" +
                    (why.Length > 0 ? $"; {why.TrimStart(';', ' ')}" : "") + ")");
            }
            else if (was)
            {
                _nextDistressLog.Remove(key);
                AILogger.Log(faction, "ECON",
                    $"savings resumed (supply income {income:0.0}/s of expected {expected:0.0}/s)");
            }
        }

        /// <summary>Is this faction's economy in distress (savings paused)?</summary>
        private static bool EconomyDistressed(Faction faction) => AIPivotalReserve.IsSuspended(faction);
    }
}
