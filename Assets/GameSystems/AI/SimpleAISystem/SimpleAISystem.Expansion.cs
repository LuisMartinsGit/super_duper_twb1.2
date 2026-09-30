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

            if (!TryPickClaimTarget(em, faction, now, out int region, out float3 point))
            {
                LogClaimBlocked(faction, now, "no claimable territory next to held ground");
                return;
            }

            var seedAt = RegionMap.SeedOf(region);
            var fresh = new ClaimSquad
            {
                Territory = region, Point = point, StartedAt = now,
                Exploring = !AICommon.IsKnownGround(faction, new float3(seedAt.x, 0f, seedAt.y)),
            };
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

            bool owned = TerritoryOwnership.OwnerOf(t) == (int)faction;
            bool done = owned && TerritoryOwnership.IsLocked(t);
            bool wiped = squad.Members.Count == 0;
            bool timedOut = now - squad.StartedAt > Cfg.claimSquadTimeoutSeconds;

            // A rival or the curse locked it under us: nothing a squad can do.
            bool lockedAgainst = !owned && TerritoryOwnership.IsLocked(t);

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
                    if (RegionMap.RegionAt(p.x, p.z) == t) continue;
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
        /// Best territory to take next: next to ground we hold, not ours, not
        /// locked by anyone else (locked ground is taken by razing, which is
        /// the attack waves' job), and carrying at least one resource node —
        /// node ground is the only ground an extractor can lock, so it is the
        /// only ground a claim keeps. Closest first, richest breaks the tie.
        /// </summary>
        private bool TryPickClaimTarget(EntityManager em, Faction faction, float now,
            out int region, out float3 point)
        {
            region = RegionMap.None;
            point = default;

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
                if (TerritoryOwnership.IsLocked(r)) continue;
                if (RegionMap.KindBlocks(RegionMap.KindOf(r))) continue;
                if (!TerritoryOwnership.IsAdjacentToHeld(faction, r)) continue;
                if (_siteBlocked.TryGetValue(((int)faction, r), out float until) && now < until) continue;

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
                    if (!AICommon.IsKnownGround(faction, seedPos) && nearest < exploreBest)
                    {
                        exploreBest = nearest;
                        explore = r;
                        explorePoint = new float3(seed.x, TerrainUtility.GetHeight(seed.x, seed.y), seed.y);
                    }
                    continue;
                }

                // A rival's unlocked ground is still a fight; prefer free land.
                float score = -nearest + nodes * Cfg.claimNodeBonus
                    - (owner != TerritoryOwnership.Natural ? Cfg.claimNodeBonus * 2f : 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    region = r;
                    // Stand by a node: the seed can sit in a lake or a
                    // forest, a node never does.
                    point = new float3(firstNode.x,
                        TerrainUtility.GetHeight(firstNode.x, firstNode.z), firstNode.z);
                }
            }
            if (region == RegionMap.None && explore != RegionMap.None)
            {
                region = explore;
                point = explorePoint;
            }
            return region != RegionMap.None;
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
    }
}
