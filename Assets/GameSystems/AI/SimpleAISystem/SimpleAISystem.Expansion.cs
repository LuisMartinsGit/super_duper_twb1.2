// SimpleAISystem.Expansion.cs
// Territory claiming: the AI marches soldiers onto ground to take it.
// Partial of SimpleAISystem.cs.
//
// ─────────────────────────────────────────────────────────────────────────
// docs/Design/Territory_Claims.md (2026-09-29): ground belongs to whoever
// STANDS on it. There is no claim building any more — the Hall is removed —
// so the AI's expansion loop is:
//
//   1. pick a territory next to ground it holds, one with resource nodes
//      (only node ground can be LOCKED, so only node ground is worth taking);
//   2. send a claim squad of idle soldiers to stand on it until the meter
//      fills;
//   3. the ordinary extractor pass then builds on the new ground's nodes
//      (TerritoriesOf now includes it), and the first finished extractor
//      LOCKS the territory;
//   4. locked ground no longer needs its garrison, so the squad is released
//      back to the army.
//
// The squad is a SEPARATE draft from the attack waves: its members are
// excluded from every wave/merge draft while they hold (IsClaimSquadMember),
// or the next wave would pull them off the ground mid-claim and the meter
// would drain back.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Regions;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        #region Cached queries

        static readonly ComponentType[] QT_HallTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagLocalTransform;

        #endregion

        /// <summary>AIPivotalReserve key of the retired Hall claim pot. Kept
        /// so the economy's reserve bookkeeping still compiles against it; no
        /// one sets it any more.</summary>
        private const string ClaimReserveKey = "ClaimHall";

        /// <summary>A squad standing on a territory to claim it.</summary>
        private sealed class ClaimSquad
        {
            public int Territory;
            public float3 Point;
            public readonly List<Entity> Members = new List<Entity>();
            public float StartedAt;
            public float NextReorderAt;
            /// <summary>Sent to LOOK at ground it has never seen, not to take
            /// known nodes; released the moment that ground is seen.</summary>
            public bool Exploring;
            /// <summary>Sent to CLEAR the curse nodes off curse-held ground
            /// with veilstone in it; becomes an ordinary claim the moment the
            /// last node there falls (Game_AI.md § Veilstone-driven conquest).</summary>
            public bool CurseAssault;
        }

        // Host-only managed state, same as _missions.
        private readonly Dictionary<int, ClaimSquad> _claimSquads = new Dictionary<int, ClaimSquad>();
        private readonly Dictionary<int, float> _nextClaimTime = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nextClaimLog = new Dictionary<int, float>();

        /// <summary>
        /// (faction, region) -> sim time a failed claim there expires. A squad
        /// wiped out or timed out on a territory marks it, so the scorer tries
        /// the runner-up instead of feeding the same ground forever.
        /// </summary>
        private readonly Dictionary<(int faction, int region), float> _siteBlocked = new();

        /// <summary>Members of any live claim squad, for the draft exclusion.</summary>
        private readonly HashSet<Entity> _claimSquadMembers = new HashSet<Entity>();

        private int _claimEpoch = -1;

        /// <summary>True while this unit is holding ground for a claim —
        /// wave and merge drafts must leave it where it stands.</summary>
        private bool IsClaimSquadMember(Entity e) => _claimSquadMembers.Contains(e);

        private bool IsEnrolledInMission(Faction faction, Entity e)
        {
            foreach (var m in MissionsFor(faction))
                if (m.Members.Contains(e)) return true;
            return false;
        }

        private void EnsureTerritoryClaim(EntityManager em, Faction faction, float now)
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;

            if (_claimEpoch != SimCadence.Epoch)
            {
                _claimEpoch = SimCadence.Epoch;
                _claimSquads.Clear();
                _claimSquadMembers.Clear();
                _nextClaimTime.Clear();
                _siteBlocked.Clear();
            }

            int key = (int)faction;
            if (_claimSquads.TryGetValue(key, out var squad))
            {
                TickClaimSquad(em, faction, squad, now);
                return;
            }

            // THE TERRITORY LIMIT (Territory_Claims.md §10): a faction may hold
            // its Fortress levels + 2 once aged up, and a claim under way
            // counts. At the limit a squad standing on new ground claims
            // nothing — it used to march anyway, stand there until
            // claimSquadTimeoutSeconds and mark the ground "failed". The way
            // past the limit is a Fortress (EnsureFortressExpansion) or a
            // Fortress level (AIBuildingUpgradeSystem), so say so and wait.
            int cap = TheWaningBorder.Systems.World.TerritoryClaimSystem.TerritoryCapOf(faction);
            int held = TheWaningBorder.Systems.World.TerritoryClaimSystem.TerritoriesHeldBy(faction);
            if (cap > 0 && held >= cap)
            {
                LogClaimBlocked(faction, now, $"territory limit {held}/{cap} — a Fortress or a Fortress level first");
                return;
            }

            // APPETITE IS THE PLAN'S, with a floor: territory is the income
            // that pays for every plan's army, so nobody opts out of eating.
            float appetite = math.max(PlanProfileOf(faction).ClaimAppetite, 1f);

            // ARMY BEFORE THE NEXT CLAIM: once a faction holds a real economy,
            // the next squad waits until the army is back to the wave bar —
            // a claim squad is soldiers the army does not have.
            int army = CountAliveMilitary(em, faction);
            if (TerritoryOwnership.CountOf(faction) >= 3 && army < Cfg.minArmyForNextClaim)
            {
                LogClaimBlocked(faction, now, $"army first ({army}/{Cfg.minArmyForNextClaim})");
                return;
            }

            if (_nextClaimTime.TryGetValue(key, out float next) && now < next) return;
            _nextClaimTime[key] = now + Cfg.claimAttemptInterval / appetite;

            if (!TryPickClaimTarget(em, faction, now, out int region, out float3 point, out bool curseHeld))
            {
                LogClaimBlocked(faction, now, "no claimable territory next to held ground");
                return;
            }

            var seedAt = RegionMap.SeedOf(region);
            var fresh = new ClaimSquad
            {
                Territory = region, Point = point, StartedAt = now,
                Exploring = !curseHeld && !AICommon.IsKnownGround(faction, new float3(seedAt.x, 0f, seedAt.y)),
                CurseAssault = curseHeld,
            };
            if (curseHeld)
            {
                // A curse node is guarded by its garrison (§6.7): send the
                // free army only when it wins there, never a 5-man squad.
                if (!DraftCurseAssault(em, faction, fresh, now))
                    return;
            }
            else
                DraftClaimSquad(em, faction, fresh, Cfg.claimSquadSize);
            if (fresh.Members.Count == 0)
            {
                LogClaimBlocked(faction, now, "no idle soldiers for a claim squad");
                return;
            }
            _claimSquads[key] = fresh;
            foreach (var m in fresh.Members) _claimSquadMembers.Add(m);
            fresh.NextReorderAt = now + ClaimReorderSeconds;
            AILogger.Log(faction, "CLAIM",
                (curseHeld ? "curse assault: " : "") +
                $"squad of {fresh.Members.Count} -> {RegionMap.NameOf(region)} ({point.x:0},{point.z:0})");
        }

        /// <summary>Seconds between re-issuing the move to squad members
        /// that finished a fight elsewhere and went idle off the ground.</summary>
        private const float ClaimReorderSeconds = 15f;

        /// <summary>A failed or timed-out target is skipped this long.</summary>
        private const float SiteBlockSeconds = 150f;

        private void TickClaimSquad(EntityManager em, Faction faction, ClaimSquad squad, float now)
        {
            int key = (int)faction;

            for (int i = squad.Members.Count - 1; i >= 0; i--)
            {
                var m = squad.Members[i];
                if (!em.Exists(m) || !em.HasComponent<Health>(m)
                    || em.GetComponentData<Health>(m).Value <= 0)
                {
                    squad.Members.RemoveAt(i);
                    _claimSquadMembers.Remove(m);
                }
            }

            int t = squad.Territory;

            // An exploring squad's job ends when the ground is SEEN: release it
            // and re-pick at once, now knowing what stands there.
            if (squad.Exploring && squad.Members.Count > 0)
            {
                var seedAt = RegionMap.SeedOf(t);
                if (AICommon.IsKnownGround(faction, new float3(seedAt.x, 0f, seedAt.y)))
                {
                    ReleaseClaimSquad(key, squad);
                    _nextClaimTime[key] = now;
                    AILogger.Log(faction, "CLAIM", $"{RegionMap.NameOf(t)}: scouted, re-picking");
                    return;
                }
            }

            // A CURSE ASSAULT clears the nodes first. While one stands in the
            // territory the squad's point is the nearest of them; when the
            // last one falls the outcrops are pacified and the squad turns
            // into an ordinary claim on the same ground, its clock restarted.
            if (squad.CurseAssault && squad.Members.Count > 0)
            {
                if (TryNearestCurseNodeIn(em, t, false, faction, squad.Point, out float3 node))
                {
                    if (math.distancesq(node.xz, squad.Point.xz) > 1f) squad.NextReorderAt = now;
                    squad.Point = node;
                }
                else
                {
                    squad.CurseAssault = false;
                    squad.StartedAt = now;
                    squad.NextReorderAt = now;
                    if (TryFirstKnownNodeIn(em, faction, t, out float3 stand)) squad.Point = stand;
                    AILogger.Log(faction, "CLAIM",
                        $"{RegionMap.NameOf(t)}: curse nodes cleared, squad of {squad.Members.Count} now claims it");
                    // The lock is re-evaluated on the claim system's next
                    // tick; judging "locked against us" now would read the
                    // dead node's lock and abandon the ground it just freed.
                    return;
                }
            }

            bool owned = TerritoryOwnership.OwnerOf(t) == (int)faction;
            bool done = owned && TerritoryOwnership.IsLocked(t);
            bool wiped = squad.Members.Count == 0;
            bool timedOut = now - squad.StartedAt >
                (squad.CurseAssault ? Cfg.claimCurseTimeoutSeconds : Cfg.claimSquadTimeoutSeconds);

            // A rival or the curse locked it under us: nothing a squad can do
            // — except a curse assault, whose whole job is breaking that lock.
            // A curse lock with no live curse node left in the territory is
            // the claim system's last-tick reading of a node that just died.
            bool lockedAgainst = !owned && TerritoryOwnership.IsLocked(t) && !squad.CurseAssault
                && !(TerritoryOwnership.OwnerOf(t) == TerritoryOwnership.Curse
                     && !TryNearestCurseNodeIn(em, t, false, faction, squad.Point, out _));

            if (done || wiped || timedOut || lockedAgainst)
            {
                ReleaseClaimSquad(key, squad);
                if (!done) _siteBlocked[(key, t)] = now + SiteBlockSeconds;
                _nextClaimTime[key] = now + (done ? Cfg.claimSuccessCooldown : 0f);
                AILogger.Log(faction, "CLAIM",
                    $"{RegionMap.NameOf(t)}: " +
                    (done ? "locked, squad released" : wiped ? "squad lost" :
                     lockedAgainst ? "locked by another side" : "timed out"));
                return;
            }

            // Keep the squad ON the ground: members that fought their way off
            // it and went idle walk back. Attack-move, so they fight on the way.
            if (now >= squad.NextReorderAt)
            {
                squad.NextReorderAt = now + ClaimReorderSeconds;
                for (int i = 0; i < squad.Members.Count; i++)
                {
                    var m = squad.Members[i];
                    var p = em.GetComponentData<LocalTransform>(m).Position;
                    // An assault keeps walking from node to node; a claim
                    // only pulls back members who wandered off the ground.
                    if (!squad.CurseAssault && RegionMap.RegionAt(p.x, p.z) == t) continue;
                    if (TransientState.Active<AttackCommand>(em, m)) continue;
                    CommandRouter.IssueAttackMove(em, m, squad.Point, CommandSource.AI);
                }
            }
        }

        private void ReleaseClaimSquad(int key, ClaimSquad squad)
        {
            foreach (var m in squad.Members) _claimSquadMembers.Remove(m);
            _claimSquads.Remove(key);
        }

        /// <summary>Draft up to <paramref name="size"/> uncommitted soldiers
        /// and attack-move them onto the claim point — same eligibility as
        /// every other AI draft.</summary>
        private void DraftClaimSquad(EntityManager em, Faction faction, ClaimSquad squad, int size)
        {
            var mq = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using var ents = mq.ToEntityArray(Allocator.Temp);
            using var tags = mq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length && squad.Members.Count < size; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                Entity e = ents[i];
                if (IsVerbUnit(em, e)) continue;
                if (IsClaimSquadMember(e)) continue;
                if (IsEnrolledInMission(faction, e)) continue;
                if (TransientState.Active<AttackMoveTag>(em, e)) continue;
                if (TransientState.Active<MoveCommand>(em, e)) continue;
                if (TransientState.Active<AttackCommand>(em, e)) continue;
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                CommandRouter.IssueAttackMove(em, e, squad.Point, CommandSource.AI);
                squad.Members.Add(e);
            }
        }

        /// <summary>
        /// Say WHY no claim happened, at most once per claimLogInterval per
        /// faction — a blocked expansion must be diagnosable from the log.
        /// </summary>
        private void LogClaimBlocked(Faction faction, float now, string why)
        {
            int key = (int)faction;
            if (_nextClaimLog.TryGetValue(key, out float next) && now < next) return;
            _nextClaimLog[key] = now + Cfg.claimLogInterval;
            AILogger.Log(faction, "CLAIM", $"no claim: {why}");
        }

        /// <summary>
        /// Best territory to take next: next to ground we hold that is linked
        /// to one of our Fortresses (Territory_Claims.md §10 — only such ground
        /// can be taken), not ours, not locked by anyone else (locked ground is
        /// taken by razing, which is the attack waves' job), and carrying at
        /// least one resource node — node ground is the only ground an
        /// extractor can lock, so it is the only ground a claim keeps.
        /// Closest first, richest breaks the tie.
        ///
        /// VEILSTONE DECIDES WHEN IT IS THE WALL (2026-10-03, Game_AI.md §
        /// Veilstone-driven conquest). While an Alanthor army is short of
        /// veilstone every outcrop is worth claimNodeBonus +
        /// claimVeilstoneNodeBonus, which outweighs the distance spread
        /// between neighbours and any number of supply nodes — and CURSE-HELD
        /// ground with outcrops (the cursed Veilstone-rich centre, any ground
        /// the curse has spread onto) becomes a candidate too, even though the
        /// curse nodes lock it: <paramref name="curseHeld"/> tells the caller
        /// to send an assault on those nodes rather than a claim squad.
        /// </summary>
        private bool TryPickClaimTarget(EntityManager em, Faction faction, float now,
            out int region, out float3 point, out bool curseHeld)
        {
            region = RegionMap.None;
            point = default;
            curseHeld = false;

            var mine = TerritoryOwnership.TerritoriesOf(faction);
            if (mine.Count == 0) return false;

            var nodeXfs = new List<float3>();
            // Only nodes it has SEEN (AICommon.IsKnownGround): a territory it
            // never scouted has no known node and is simply not a candidate —
            // the scouts, the claim squads and the armies are how it learns.
            CollectNodePositions<IronMineTag>(em, faction, nodeXfs);
            CollectNodePositions<VeilstoneOutcroppingTag>(em, faction, nodeXfs);
            CollectNodePositions<VeilsteelDepositTag>(em, faction, nodeXfs);
            CollectNodePositions<SupplyNodeTag>(em, faction, nodeXfs);

            // OUTCROPS FIRST WHEN VEILSTONE IS THE WALL (2026-10-03). An
            // Alanthor army short of veilstone can only be fed by Trading
            // Outposts, one per uncursed outcrop in held ground — and a 0.0.33
            // batch held 1-3 Outposts per faction because its 3-6 territories
            // carried 1-3 outcrops between them. So while the army is short,
            // an outcrop scores extra (claimVeilstoneNodeBonus) on top of the
            // ordinary node bonus: uncursed ones on free ground, EVERY one on
            // curse-held ground (its node's fall pacifies it —
            // Veilstone_Economy.md §2).
            _claimOutcrops.Clear();
            _claimAllOutcrops.Clear();
            bool wantOutcrops = CultureConfig.GetCompletedCulture(em, faction) == Cultures.Alanthor
                && AIBudget.IsMilitaryShort(faction, AIBudget.ResVeilstone);
            if (wantOutcrops)
            {
                CollectUncursedOutcrops(em, faction, _claimOutcrops);
                CollectNodePositions<VeilstoneOutcroppingTag>(em, faction, _claimAllOutcrops);
            }
            float3 home = HomeAnchor(em, faction);

            float bestScore = float.MinValue;
            // Fallback when no KNOWN node is claimable: the nearest adjacent
            // territory it has never seen. The claim squad goes and looks —
            // it scouts by standing there (and starts the claim while it is
            // at it); whatever nodes it reveals are candidates next time.
            int explore = RegionMap.None;
            float exploreBest = float.MaxValue;
            float3 explorePoint = default;
            for (int r = 0; r < RegionMap.Count; r++)
            {
                int owner = TerritoryOwnership.OwnerOf(r);
                if (owner == (int)faction) continue;
                if (RegionMap.KindBlocks(RegionMap.KindOf(r))) continue;
                if (!BordersFortressGround(faction, r, mine)) continue;
                if (_siteBlocked.TryGetValue(((int)faction, r), out float until) && now < until) continue;

                // Locked ground is off the table — unless it is the curse's,
                // the army wants veilstone, and outcrops are known there.
                bool curseTarget = false;
                if (TerritoryOwnership.IsLocked(r))
                {
                    if (!wantOutcrops || owner != TerritoryOwnership.Curse) continue;
                    if (CountIn(_claimAllOutcrops, r) == 0) continue;
                    curseTarget = true;
                }

                int nodes = 0;
                float3 firstNode = default;
                for (int i = 0; i < nodeXfs.Count; i++)
                {
                    if (RegionMap.RegionAt(nodeXfs[i].x, nodeXfs[i].z) != r) continue;
                    if (nodes == 0) firstNode = nodeXfs[i];
                    nodes++;
                }
                var seed = RegionMap.SeedOf(r);
                float nearest = float.MaxValue;
                for (int i = 0; i < mine.Count; i++)
                {
                    var m2 = RegionMap.SeedOf(mine[i]);
                    float dx = seed.x - m2.x, dz = seed.y - m2.y;
                    nearest = math.min(nearest, math.sqrt(dx * dx + dz * dz));
                }

                if (nodes == 0)
                {
                    // No node it knows of. Unseen ground is worth a look;
                    // seen ground with nothing on it is not.
                    var seedPos = new float3(seed.x, 0f, seed.y);
                    if (!curseTarget && !AICommon.IsKnownGround(faction, seedPos) && nearest < exploreBest)
                    {
                        exploreBest = nearest;
                        explore = r;
                        explorePoint = new float3(seed.x, TerrainUtility.GetHeight(seed.x, seed.y), seed.y);
                    }
                    continue;
                }

                float3 standAt = firstNode;
                // The assault walks to the KNOWN curse node nearest home first.
                if (curseTarget && !TryNearestCurseNodeIn(em, r, true, faction, home, out standAt))
                    continue;

                int outcrops = CountIn(curseTarget ? _claimAllOutcrops : _claimOutcrops, r);

                // A rival's unlocked ground is still a fight; prefer free land.
                // The curse's garrison is a fight too.
                float score = -nearest + nodes * Cfg.claimNodeBonus
                    + outcrops * Cfg.claimVeilstoneNodeBonus
                    - (curseTarget ? Cfg.claimCurseTargetPenalty
                       : owner != TerritoryOwnership.Natural ? Cfg.claimNodeBonus * 2f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    region = r;
                    curseHeld = curseTarget;
                    // Stand by a node: the seed can sit in a lake or a
                    // forest, a node never does.
                    point = new float3(standAt.x,
                        TerrainUtility.GetHeight(standAt.x, standAt.z), standAt.z);
                }
            }
            if (region == RegionMap.None && explore != RegionMap.None)
            {
                region = explore;
                point = explorePoint;
            }
            return region != RegionMap.None;
        }

        /// <summary>
        /// May the faction take <paramref name="r"/> by standing on it? It has
        /// to border ground the faction holds that is LINKED to one of its
        /// Fortresses (Territory_Claims.md §10, TerritoryClaimSystem.MayTake).
        /// Falls back to plain adjacency before the claim system's first tick
        /// (no link computed yet), so the picker is never stricter than the rule.
        /// </summary>
        private static bool BordersFortressGround(Faction faction, int r, List<int> mine)
        {
            bool anyLinked = false;
            for (int i = 0; i < mine.Count; i++)
            {
                bool linked = TheWaningBorder.Systems.World.TerritoryClaimSystem.IsConnected(mine[i], faction);
                anyLinked |= linked;
                if (linked && RegionMap.AreAdjacent(mine[i], r)) return true;
            }
            return !anyLinked && TerritoryOwnership.IsAdjacentToHeld(faction, r);
        }

        private static int CountIn(List<float3> points, int r)
        {
            int n = 0;
            for (int i = 0; i < points.Count; i++)
                if (RegionMap.RegionAt(points[i].x, points[i].z) == r) n++;
            return n;
        }

        /// <summary>The home capital's position (the base anchor), or the
        /// origin when it is gone.</summary>
        private static float3 HomeAnchor(EntityManager em, Faction faction)
        {
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            return hall != Entity.Null && em.HasComponent<LocalTransform>(hall)
                ? em.GetComponentData<LocalTransform>(hall).Position : float3.zero;
        }

        /// <summary>The live curse node in territory <paramref name="r"/>
        /// nearest <paramref name="from"/>. With <paramref name="knownOnly"/>
        /// only nodes the faction has SEEN count (the picker); the squad
        /// standing on the ground takes every live one.</summary>
        private static bool TryNearestCurseNodeIn(EntityManager em, int r, bool knownOnly,
            Faction faction, float3 from, out float3 node)
        {
            node = default;
            var nq = QC_SmallNodeTagLocalTransformHealth.Get(em, QT_SmallNodeTagLocalTransformHealth);
            using var xfs = nq.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = nq.ToComponentDataArray<Health>(Allocator.Temp);
            float best = float.MaxValue;
            bool found = false;
            for (int i = 0; i < xfs.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                var p = xfs[i].Position;
                if (RegionMap.RegionAt(p.x, p.z) != r) continue;
                if (knownOnly && !AICommon.IsKnownGround(faction, p)) continue;
                float d = math.distancesq(p.xz, from.xz);
                // Strict < over a fixed query order: deterministic tie-break.
                if (d < best) { best = d; node = p; found = true; }
            }
            return found;
        }

        /// <summary>A known resource node in <paramref name="r"/> to stand by
        /// once its curse nodes are gone (outcrops first).</summary>
        private static bool TryFirstKnownNodeIn(EntityManager em, Faction faction, int r, out float3 at)
        {
            var pts = new List<float3>();
            CollectNodePositions<VeilstoneOutcroppingTag>(em, faction, pts);
            CollectNodePositions<IronMineTag>(em, faction, pts);
            CollectNodePositions<SupplyNodeTag>(em, faction, pts);
            for (int i = 0; i < pts.Count; i++)
                if (RegionMap.RegionAt(pts[i].x, pts[i].z) == r)
                {
                    at = new float3(pts[i].x, TerrainUtility.GetHeight(pts[i].x, pts[i].z), pts[i].z);
                    return true;
                }
            at = default;
            return false;
        }

        /// <summary>Host scratch: every known outcrop, cursed or not.</summary>
        private readonly List<float3> _claimAllOutcrops = new List<float3>();
        private readonly List<Entity> _curseAssaultArmy = new List<Entity>();

        /// <summary>
        /// Launch an assault on a curse-held territory's nodes: every free
        /// soldier (the claim squad's eligibility) up to claimCurseSquadMax,
        /// but only when AIEngagement says that army wins against the
        /// garrison at the first node. Too weak: log, skip the ground for a
        /// while (the picker tries the runner-up) and let the army grow.
        /// </summary>
        private bool DraftCurseAssault(EntityManager em, Faction faction, ClaimSquad squad, float now)
        {
            _curseAssaultArmy.Clear();
            var mq = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using (var ents = mq.ToEntityArray(Allocator.Temp))
            using (var tags = mq.ToComponentDataArray<UnitTag>(Allocator.Temp))
            using (var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ents.Length && _curseAssaultArmy.Count < Cfg.claimCurseSquadMax; i++)
                {
                    if (facs[i].Value != faction) continue;
                    if (!IsCombatClass(tags[i].Class)) continue;
                    Entity e = ents[i];
                    if (em.HasComponent<UnderConstruction>(e)) continue;
                    if (IsVerbUnit(em, e)) continue;
                    if (IsClaimSquadMember(e)) continue;
                    if (IsEnrolledInMission(faction, e)) continue;
                    if (TransientState.Active<AttackMoveTag>(em, e)) continue;
                    if (TransientState.Active<MoveCommand>(em, e)) continue;
                    if (TransientState.Active<AttackCommand>(em, e)) continue;
                    if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                    _curseAssaultArmy.Add(e);
                }

            int key = (int)faction;
            if (_curseAssaultArmy.Count == 0)
            {
                LogClaimBlocked(faction, now, "no idle soldiers for a curse assault");
                return false;
            }
            var a = AIEngagement.AssessAssault(em, faction, _curseAssaultArmy, squad.Point,
                Cfg.claimCurseAssessRadius);
            if (!a.ShouldFight)
            {
                _siteBlocked[(key, squad.Territory)] = now + SiteBlockSeconds;
                AILogger.Log(faction, "CLAIM",
                    $"curse assault on {RegionMap.NameOf(squad.Territory)} held back: " +
                    $"{_curseAssaultArmy.Count} free units, power {a.MyPower} vs {a.EnemyPower}");
                return false;
            }
            for (int i = 0; i < _curseAssaultArmy.Count; i++)
            {
                CommandRouter.IssueAttackMove(em, _curseAssaultArmy[i], squad.Point, CommandSource.AI);
                squad.Members.Add(_curseAssaultArmy[i]);
            }
            return true;
        }

        /// <summary>Host scratch for the claim picker's outcrop pass.</summary>
        private readonly List<float3> _claimOutcrops = new List<float3>();

        static readonly ComponentType[] QT_Outcrops =
        {
            ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_Outcrops;

        /// <summary>Known veilstone outcrops a Trading Outpost could trade
        /// beside: not Cursed (a cursed one idles its Outpost until it is
        /// pacified — Veilstone_Economy.md §3.1).</summary>
        private static void CollectUncursedOutcrops(EntityManager em, Faction faction, List<float3> into)
        {
            var q = QC_Outcrops.Get(em, QT_Outcrops);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (!AICommon.IsKnownGround(faction, xfs[i].Position)) continue;
                if (TheWaningBorder.Systems.Economy.VeilstoneNodeStateSystem.KindOf(em, ents[i])
                    == VeilstoneNodeKind.Cursed) continue;
                into.Add(xfs[i].Position);
            }
        }

        private static void CollectNodePositions<T>(EntityManager em, Faction faction, List<float3> into)
            where T : unmanaged, IComponentData
        {
            var q = AIQueryCache.TagXf<T>(em);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
                if (AICommon.IsKnownGround(faction, xfs[i].Position))
                    into.Add(xfs[i].Position);
        }

        // ─────────────────────────────────────────────────────────────────
        // FORTRESS EXPANSION (2026-10-03, Game_AI.md § Fortress expansion)
        //
        // The territory limit is the sum of the faction's Fortress levels + 2
        // once aged up (Territory_Claims.md §10), so an aged-up AI with only
        // its capital stops at three territories (five once the capital is
        // L3) — the 0.0.33 batch's "3-6 territories, 1-3 outcrops". Every
        // further Fortress is +1 (its own L1, more with levels), locks its
        // territory, re-links cut-off ground, and is what the wall doctrine
        // needs before it walls anything but the home. So after age-up the AI
        // raises one in a held, non-home territory that has none: outcrops
        // first, then ground bordering rivals or the curse; one in flight at
        // a time; only when the bank still covers the reserve after paying.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>AIPivotalReserve key of a Fortress the faction is saving
        /// for while the territory limit blocks every claim.</summary>
        private const string FortressReserveKey = "FortressExpansion";

        private readonly Dictionary<int, float> _nextFortressCheck = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nextFortressLog = new Dictionary<int, float>();
        /// <summary>(faction, region) -> sim time a failed Fortress site there expires.</summary>
        private readonly Dictionary<(int faction, int region), float> _fortressSiteBlocked = new();
        private int _fortressEpoch = -1;

        // Host scratch.
        private readonly List<int> _fortressRegions = new List<int>();
        private readonly List<float3> _fortressOutcrops = new List<float3>();

        /// <summary>When set, TryFindBuildPosition accepts only candidates in
        /// this territory (the Fortress must stand in the territory it was
        /// chosen for, not spill back into the home ring).</summary>
        private int _siteRegionLock = RegionMap.None;

        static readonly ComponentType[] QT_FortressFactionXf =
        {
            ComponentType.ReadOnly<FortressTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_FortressFactionXf;

        private void EnsureFortressExpansion(EntityManager em, Faction faction, float now)
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;

            if (_fortressEpoch != SimCadence.Epoch)
            {
                _fortressEpoch = SimCadence.Epoch;
                _nextFortressCheck.Clear();
                _nextFortressLog.Clear();
                _fortressSiteBlocked.Clear();
            }

            int key = (int)faction;
            if (_nextFortressCheck.TryGetValue(key, out float next) && now < next) return;
            _nextFortressCheck[key] = now + Cfg.fortressCheckInterval;

            // After age-up only: in Age 0 the limit is the start territory.
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)
                || !em.HasComponent<FactionEra>(bank)
                || em.GetComponentData<FactionEra>(bank).Value < 2)
                return;

            // Which territories already carry a Fortress (anyone's — one per
            // territory, §4), how many this faction owns, and whether one of
            // its own is still going up. Plans count: a placed Fortress is a
            // plan until a worker breaks ground (Planned_Buildings.md).
            _fortressRegions.Clear();
            int own = TheWaningBorder.Entities.PlannedBuildings.CountOf(em, faction, "Fortress");
            bool inFlight = own > 0;
            {
                var q = QC_FortressFactionXf.Get(em, QT_FortressFactionXf);
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    // NearestRegion, matching TerritoryOwnership.FortressCapReached.
                    int r = RegionMap.NearestRegion(xfs[i].Position.x, xfs[i].Position.z);
                    if (r != RegionMap.None && !_fortressRegions.Contains(r)) _fortressRegions.Add(r);
                    if (facs[i].Value != faction) continue;
                    own++;
                    if (em.HasComponent<UnderConstruction>(ents[i])) inFlight = true;
                }
            }
            if (inFlight)
            {
                AIPivotalReserve.Clear(faction, FortressReserveKey);
                LogFortress(faction, now, "a Fortress is already going up");
                return;
            }
            if (own >= Cfg.fortressMaxPerFaction)
            {
                AIPivotalReserve.Clear(faction, FortressReserveKey);
                LogFortress(faction, now, $"at the Fortress ceiling ({own}/{Cfg.fortressMaxPerFaction})");
                return;
            }

            float3 home = HomeAnchor(em, faction);
            int homeRegion = RegionMap.RegionAt(home.x, home.z);
            var mine = TerritoryOwnership.TerritoriesOf(faction);
            _fortressOutcrops.Clear();
            CollectNodePositions<VeilstoneOutcroppingTag>(em, faction, _fortressOutcrops);

            int best = RegionMap.None;
            float bestScore = float.MinValue;
            int bestOutcrops = 0;
            for (int i = 0; i < mine.Count; i++)
            {
                int r = mine[i];
                if (r == homeRegion) continue;
                if (!TerritoryOwnership.IsClaimed(r)) continue;
                if (RegionMap.KindBlocks(RegionMap.KindOf(r))) continue;
                if (_fortressRegions.Contains(r)) continue;
                if (_fortressSiteBlocked.TryGetValue((key, r), out float until) && now < until) continue;

                int outcrops = CountIn(_fortressOutcrops, r);
                int frontier = 0;
                bool hostileBorder = TerritoryOwnership.IsContested(r);
                for (int o = 0; o < RegionMap.Count; o++)
                {
                    if (o == r || !RegionMap.AreAdjacent(r, o)) continue;
                    int owner = TerritoryOwnership.OwnerOf(o);
                    if (owner == (int)faction) continue;
                    if (RegionMap.KindBlocks(RegionMap.KindOf(o))) continue;
                    frontier += CountIn(_fortressOutcrops, o);
                    if (owner == TerritoryOwnership.Curse
                        || (owner >= 0 && Alliances.AreHostile(faction, (Faction)owner)))
                        hostileBorder = true;
                }
                bool cutOff = !TheWaningBorder.Systems.World.TerritoryClaimSystem.IsConnected(r, faction);
                var seed = RegionMap.SeedOf(r);
                float dist = math.distance(new float2(seed.x, seed.y), home.xz);

                float score = outcrops * Cfg.fortressOutcropWeight
                    + frontier * Cfg.fortressFrontierOutcropWeight
                    + (hostileBorder ? Cfg.fortressBorderBonus : 0f)
                    + (cutOff ? Cfg.fortressDisconnectedBonus : 0f)
                    - dist * Cfg.fortressDistanceWeight;
                // Strict > over held territories in index order: deterministic.
                if (score > bestScore)
                {
                    bestScore = score;
                    best = r;
                    bestOutcrops = outcrops;
                }
            }
            if (best == RegionMap.None)
            {
                AIPivotalReserve.Clear(faction, FortressReserveKey);
                LogFortress(faction, now, "no held territory without a Fortress");
                return;
            }

            int cap = TheWaningBorder.Systems.World.TerritoryClaimSystem.TerritoryCapOf(faction);
            int held = TheWaningBorder.Systems.World.TerritoryClaimSystem.TerritoriesHeldBy(faction);
            bool atLimit = cap > 0 && held >= cap;

            var cost = TheWaningBorder.Data.BuildCosts.For(em, faction, "Fortress");
            var need = cost + Cost.Of(Cfg.fortressReserveSupplies, Cfg.fortressReserveIron,
                                      Cfg.fortressReserveVeilstone);
            if (!FactionEconomy.CanAfford(em, faction, need))
            {
                // At the limit the Fortress IS the expansion: save for it the
                // way the age-up and the claim pot are saved for (non-strict,
                // so the hold breathes). Below it, just wait for the bank.
                if (atLimit) AIPivotalReserve.Set(faction, FortressReserveKey, need);
                else AIPivotalReserve.Clear(faction, FortressReserveKey);
                LogFortress(faction, now,
                    $"{(atLimit ? "saving" : "waiting")} for a Fortress in {RegionMap.NameOf(best)} " +
                    $"(territories {held}/{cap}; needs {need.Supplies}s {need.Iron}i {need.Veilstone}v " +
                    $"with the reserve)");
                return;
            }
            AIPivotalReserve.Clear(faction, FortressReserveKey);

            var at = RegionMap.SeedOf(best);
            var anchor = new float3(at.x, TerrainUtility.GetHeight(at.x, at.y), at.y);
            bool ok;
            string reason;
            _siteRegionLock = best;
            try { ok = TryBuildBuildingWithReason(em, faction, "Fortress", out reason, anchor); }
            finally { _siteRegionLock = RegionMap.None; }

            if (ok)
            {
                string line = $"Fortress ordered in {RegionMap.NameOf(best)} — {bestOutcrops} outcrop(s), " +
                              $"score {bestScore:0}, territories {held}/{cap}, Fortresses {own + 1}";
                AILogger.Log(faction, "EXPANSION", line);
                AILogger.Log(faction, "BUILDING", line);
                return;
            }
            _fortressSiteBlocked[(key, best)] = now + Cfg.fortressSiteRetrySeconds;
            AILogger.Log(faction, "EXPANSION",
                $"Fortress in {RegionMap.NameOf(best)} refused: {reason} — skipped for " +
                $"{Cfg.fortressSiteRetrySeconds:0}s");
        }

        /// <summary>Why no Fortress was ordered, at most once per
        /// claimLogInterval per faction.</summary>
        private void LogFortress(Faction faction, float now, string why)
        {
            int key = (int)faction;
            if (_nextFortressLog.TryGetValue(key, out float next) && now < next) return;
            _nextFortressLog[key] = now + Cfg.claimLogInterval;
            AILogger.Log(faction, "EXPANSION", $"no Fortress: {why}");
        }
    }
}
