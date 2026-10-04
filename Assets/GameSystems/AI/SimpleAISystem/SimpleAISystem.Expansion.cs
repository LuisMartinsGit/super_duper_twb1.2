// SimpleAISystem.Expansion.cs
// Territory claiming: the AI marches soldiers onto ground to take it.
// Partial of SimpleAISystem.cs.
//
// ─────────────────────────────────────────────────────────────────────────
// docs/Design/Territory_Claims.md (2026-09-29): ground belongs to whoever
// STANDS on it. There is no claim building any more — the Hall is removed —
// so the AI's expansion loop is:
//
//   1. rank every territory next to its Fortress-linked ground that has
//      resource nodes (only node ground can be LOCKED, so only node ground is
//      worth taking);
//   2. send claim squads of idle soldiers to stand on them, IN PARALLEL, each
//      sized to the known threat there, until the army is stretched (there
//      is no territory cap — Territory_Claims.md §10, 2026-10-04: a faction
//      holds what it can defend); squads pre-stage before the age-up lands;
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
            /// <summary>The point the squad was last ORDERED to as a group.
            /// A member still attack-moving while this equals
            /// <see cref="Point"/> is marching under that order and is left in
            /// its formation — re-issuing re-plans (and halts) the group.</summary>
            public float3 OrderedPoint;
            /// <summary>Sent to LOOK at ground it has never seen, not to take
            /// known nodes; released the moment that ground is seen.</summary>
            public bool Exploring;
            /// <summary>Sent to CLEAR the curse nodes off curse-held ground
            /// with veilstone in it; becomes an ordinary claim the moment the
            /// last node there falls (Game_AI.md § Veilstone-driven conquest).</summary>
            public bool CurseAssault;
            /// <summary>Sent before the age-up landed (claims open at age-up,
            /// Territory_Claims.md §10): waits on the ground, and its clock
            /// starts when the era turns.</summary>
            public bool Staged;
            /// <summary>Sim time the ground became claimed by us (0 = not yet);
            /// the squad then waits claimHoldForLockSeconds for a lock.</summary>
            public float ClaimedAt;
        }

        /// <summary>A territory the claim round may send a squad to.</summary>
        private struct ClaimCandidate
        {
            public int Region;
            public float3 Point;
            public bool CurseHeld;
            public bool Exploring;
            public float Score;
            public int Outcrops;
        }

        // Host-only managed state, same as _missions. Per faction: every
        // claim squad it has out (Game_AI.md § 5b — claims go out in PARALLEL).
        private readonly Dictionary<int, List<ClaimSquad>> _claimSquads = new Dictionary<int, List<ClaimSquad>>();
        private readonly Dictionary<int, float> _nextClaimTime = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nextClaimLog = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _nextExpandLog = new Dictionary<int, float>();

        /// <summary>
        /// (faction, region) -> sim time a failed claim there expires. A squad
        /// wiped out or timed out on a territory marks it, so the scorer tries
        /// the runner-up instead of feeding the same ground forever.
        /// </summary>
        private readonly Dictionary<(int faction, int region), float> _siteBlocked = new();

        /// <summary>Members of any live claim squad, for the draft exclusion.</summary>
        private readonly HashSet<Entity> _claimSquadMembers = new HashSet<Entity>();

        /// <summary>Per faction: the CLAIMED territories it held last think —
        /// a territory gone from it since is a loss (consolidate).</summary>
        private readonly Dictionary<int, HashSet<int>> _claimedLastThink = new Dictionary<int, HashSet<int>>();
        private readonly Dictionary<int, (float At, int Region)> _lastTerritoryLoss = new Dictionary<int, (float, int)>();

        /// <summary>Per faction: true while expansion is consolidating
        /// (stretched or just lost ground) — the Fortress picker saves then.</summary>
        private readonly Dictionary<int, bool> _consolidating = new Dictionary<int, bool>();

        /// <summary>Per faction: last sim time a claim round could not staff a
        /// candidate for want of idle soldiers — the waves yield to it.</summary>
        private readonly Dictionary<int, float> _claimWantsSoldiersAt = new Dictionary<int, float>();
        private readonly Dictionary<int, float> _claimWaveYieldSince = new Dictionary<int, float>();

        private int _claimEpoch = -1;

        // Host scratch (main thread only).
        private readonly List<ClaimCandidate> _claimCandidates = new List<ClaimCandidate>();
        private readonly List<Entity> _claimPool = new List<Entity>();
        private readonly List<Entity> _claimDraft = new List<Entity>();
        private readonly List<float3> _claimSupplyNodes = new List<float3>();
        private readonly System.Text.StringBuilder _claimRoundLog = new System.Text.StringBuilder();

        /// <summary>True while this unit is holding ground for a claim —
        /// wave and merge drafts must leave it where it stands.</summary>
        private bool IsClaimSquadMember(Entity e) => _claimSquadMembers.Contains(e);

        private bool IsEnrolledInMission(Faction faction, Entity e)
        {
            foreach (var m in MissionsFor(faction))
                if (m.Members.Contains(e)) return true;
            return false;
        }

        private List<ClaimSquad> ClaimSquadsOf(int key)
        {
            if (!_claimSquads.TryGetValue(key, out var list))
                _claimSquads[key] = list = new List<ClaimSquad>();
            return list;
        }

        /// <summary>
        /// DEFEND-BASED EXPANSION (Game_AI.md § 5b; Territory_Claims.md §10,
        /// 2026-10-04: there is no territory cap — a faction holds what it can
        /// defend). Every think: tick the squads out; before the age-up lands,
        /// pre-stage squads on the best targets once the landmark stands; after
        /// it, send a squad to every free claimable territory the army can
        /// spare one for, in parallel, sized to the known threat there — until
        /// the army is STRETCHED (power per held territory below the floor) or
        /// just lost ground, when it consolidates instead.
        /// </summary>
        private void EnsureTerritoryClaim(EntityManager em, Faction faction, AIPosture posture,
            in AIDifficultyProfile profile, float now)
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return;

            if (_claimEpoch != SimCadence.Epoch)
            {
                _claimEpoch = SimCadence.Epoch;
                _claimSquads.Clear();
                _claimSquadMembers.Clear();
                _nextClaimTime.Clear();
                _nextClaimLog.Clear();
                _nextExpandLog.Clear();
                _siteBlocked.Clear();
                _claimedLastThink.Clear();
                _lastTerritoryLoss.Clear();
                _consolidating.Clear();
                _claimWantsSoldiersAt.Clear();
                _claimWaveYieldSince.Clear();
            }

            int key = (int)faction;
            bool aged = HasAgedUp(em, faction);
            var squads = ClaimSquadsOf(key);
            for (int i = squads.Count - 1; i >= 0; i--)
                if (TickClaimSquad(em, faction, squads[i], aged, now))
                    squads.RemoveAt(i);

            TrackTerritoryLosses(faction, now);
            _consolidating[key] = false;

            // APPETITE IS THE PLAN'S, with a floor: territory is the income
            // that pays for every plan's army, so nobody opts out of eating.
            float appetite = math.max(PlanProfileOf(faction).ClaimAppetite, 1f);
            int maxParallel = math.max(1, (int)math.round(
                Cfg.claimMaxParallelSquads * math.max(0.1f, profile.ExpansionDrive) * appetite));

            if (!aged)
            {
                // CLAIMS OPEN AT AGE-UP. Once the landmark stands the age-up
                // is a build timer away: pre-stage the squads on their targets
                // now, so the meters start filling the moment the era turns.
                if (!FactionHasLandmark(em, faction))
                {
                    LogClaimBlocked(faction, now, "Age 0 — claims open at age-up");
                    return;
                }
                if (squads.Count > 0) return;   // staged already
                if (_nextClaimTime.TryGetValue(key, out float nextStage) && now < nextStage) return;
                _nextClaimTime[key] = now + Cfg.claimAttemptInterval;
                LaunchClaimRound(em, faction, maxParallel, prestage: true, now);
                return;
            }

            if (_nextClaimTime.TryGetValue(key, out float next) && now < next) return;
            bool surplus = IsVeilstoneSurplus(em, faction);
            float interval = Cfg.claimAttemptInterval;
            if (surplus && Cfg.surplusClaimIntervalScale > 0f) interval *= Cfg.surplusClaimIntervalScale;
            _nextClaimTime[key] = now + interval;

            // A THREAT AT HOME OUTRANKS NEW GROUND: squads already out stay,
            // nothing new leaves while the base is being defended.
            if (posture == AIPosture.Defend)
            {
                LogClaimBlocked(faction, now, "home under threat — new claims wait");
                return;
            }

            // CONSOLIDATE AFTER A LOSS: ground just lost says the army is not
            // holding what it has.
            if (_lastTerritoryLoss.TryGetValue(key, out var loss)
                && now - loss.At < Cfg.expandLossConsolidateSeconds)
            {
                _consolidating[key] = true;
                LogExpand(faction, now,
                    $"lost {RegionMap.NameOf(loss.Region)} {(int)(now - loss.At)}s ago — consolidating " +
                    $"for {(int)(Cfg.expandLossConsolidateSeconds - (now - loss.At))}s");
                return;
            }

            // STRETCHED: army power per held territory (claims under way
            // included) below the floor. Each new claim adds one territory,
            // so the round may only add as many as the floor still covers.
            int held = TheWaningBorder.Systems.World.TerritoryClaimSystem.TerritoriesHeldBy(faction);
            int armyPower = ArmyPowerOf(em, faction);
            float floor = Cfg.expandPowerPerTerritoryFloor / appetite;
            int room = floor > 0f ? (int)math.floor(armyPower / floor) - held : int.MaxValue;
            if (held >= Cfg.expandStretchMinTerritories && room <= 0)
            {
                _consolidating[key] = true;
                LogExpand(faction, now,
                    $"stretched (power/territory {armyPower / math.max(1, held)} < {floor:0}, " +
                    $"{held} territories, army power {armyPower}) — consolidating");
                return;
            }
            if (held < Cfg.expandStretchMinTerritories)
                room = math.max(room, Cfg.expandStretchMinTerritories - held);

            int slots = math.min(maxParallel - squads.Count, room);
            if (slots <= 0) return;
            LaunchClaimRound(em, faction, slots, prestage: false, now);
        }

        /// <summary>
        /// One claim round: rank every candidate, then staff as many as
        /// <paramref name="slots"/> allows from the idle army, nearest
        /// soldiers first, each squad sized to the known threat at its target.
        /// </summary>
        private void LaunchClaimRound(EntityManager em, Faction faction, int slots, bool prestage, float now)
        {
            int key = (int)faction;
            var squads = ClaimSquadsOf(key);
            CollectClaimCandidates(em, faction, now);
            if (_claimCandidates.Count == 0)
            {
                LogClaimBlocked(faction, now, "no claimable territory next to Fortress-linked ground");
                return;
            }

            int armyCount = BuildClaimPool(em, faction);
            int inSquads = 0;
            bool exploringOut = false, curseOut = false;
            for (int i = 0; i < squads.Count; i++)
            {
                inSquads += squads[i].Members.Count;
                exploringOut |= squads[i].Exploring;
                curseOut |= squads[i].CurseAssault;
            }
            int budget = (int)math.floor((armyCount + inSquads) * Cfg.claimArmyShare) - inSquads;
            bool surplus = !prestage && IsVeilstoneSurplus(em, faction);

            _claimRoundLog.Clear();
            int launched = 0;
            bool shortOfSoldiers = false;
            for (int c = 0; c < _claimCandidates.Count && launched < slots; c++)
            {
                var cand = _claimCandidates[c];
                if (cand.Exploring && exploringOut) continue;

                var squad = new ClaimSquad
                {
                    Territory = cand.Region, Point = cand.Point, StartedAt = now,
                    Exploring = cand.Exploring, CurseAssault = cand.CurseHeld, Staged = prestage,
                };

                if (cand.CurseHeld)
                {
                    // A curse node is guarded by its garrison (§6.7): the free
                    // army goes only when it wins there, and one at a time.
                    if (curseOut || prestage) continue;
                    if (!DraftCurseAssault(em, faction, squad, now)) continue;
                    curseOut = true;
                    for (int m = 0; m < squad.Members.Count; m++) _claimPool.Remove(squad.Members[m]);
                    budget -= squad.Members.Count;
                }
                else
                {
                    // SIZED TO THE KNOWN THREAT: what the faction has seen
                    // there (unseen ground counts as empty — the squad looks).
                    int threat = 0;
                    if (AICommon.IsKnownGround(faction, cand.Point))
                        threat = TacticalQuery.EnemyStrengthInRadius(em, faction, cand.Point, Cfg.claimThreatRadius)
                               + AIEngagement.StaticDefencePower(em, faction, cand.Point, Cfg.claimThreatRadius);
                    int need = (int)math.ceil(threat * Cfg.claimThreatMargin);
                    int cap = math.min(Cfg.claimSquadMaxSize, budget);
                    _claimDraft.Clear();
                    int power = 0;
                    while (_claimDraft.Count < cap && _claimPool.Count > 0
                           && (_claimDraft.Count < Cfg.claimSquadMinSize || power < need))
                    {
                        int pick = NearestInPool(em, cand.Point);
                        var e = _claimPool[pick];
                        _claimPool.RemoveAt(pick);
                        _claimDraft.Add(e);
                        power += TacticalQuery.UnitStrength(em, e);
                    }
                    bool enough = _claimDraft.Count >= Cfg.claimSquadMinSize && power >= need;
                    if (!enough)
                    {
                        // Give the soldiers back for the next candidate.
                        for (int m = 0; m < _claimDraft.Count; m++) _claimPool.Add(_claimDraft[m]);
                        if (_claimPool.Count < Cfg.claimSquadMinSize || budget < Cfg.claimSquadMinSize)
                        {
                            shortOfSoldiers = true;
                            break;
                        }
                        _siteBlocked[(key, cand.Region)] = now + Cfg.claimHeldBackSeconds;
                        AILogger.Log(faction, "CLAIM",
                            $"{RegionMap.NameOf(cand.Region)} held back: the {_claimDraft.Count} soldiers " +
                            $"the army can spare have power {power} vs known threat {threat}");
                        continue;
                    }
                    for (int m = 0; m < _claimDraft.Count; m++) squad.Members.Add(_claimDraft[m]);
                    budget -= _claimDraft.Count;
                    // The squad marches as one formation (AICommon.IssueGroupOrder).
                    AICommon.IssueGroupOrder(em, squad.Members, squad.Point, attackMove: true, Cfg.waveArrivedRadius);
                    squad.OrderedPoint = squad.Point;
                }

                if (squad.Members.Count == 0) continue;
                squad.NextReorderAt = now + ClaimReorderSeconds;
                squads.Add(squad);
                foreach (var m in squad.Members) _claimSquadMembers.Add(m);
                if (cand.Exploring) exploringOut = true;
                launched++;
                if (_claimRoundLog.Length > 0) _claimRoundLog.Append(", ");
                _claimRoundLog.Append(RegionMap.NameOf(cand.Region)).Append(" (")
                    .Append(squad.Members.Count)
                    .Append(cand.CurseHeld ? ", curse assault" : cand.Exploring ? ", exploring" : "")
                    .Append(cand.Outcrops > 0 ? $", {cand.Outcrops} outcrop(s)" : "").Append(')');
                if (surplus && cand.Outcrops > 0)
                    LogSurplus(faction,
                        $"veilstone claim -> {RegionMap.NameOf(cand.Region)} ({cand.Outcrops} outcrop(s))");
            }

            if (launched == 0 && (shortOfSoldiers || budget < Cfg.claimSquadMinSize))
                shortOfSoldiers = true;
            if (shortOfSoldiers) _claimWantsSoldiersAt[key] = now;

            if (launched > 0)
                AILogger.Log(faction, "CLAIM",
                    (prestage ? $"pre-staged {launched} squad(s) for the age-up -> " : $"parallel {launched} squad(s) -> ")
                    + _claimRoundLog + $" | {squads.Count} out, {_claimCandidates.Count} candidate(s)");
            else if (shortOfSoldiers)
                LogClaimBlocked(faction, now,
                    $"no idle soldiers for a claim squad ({_claimCandidates.Count} candidate(s), " +
                    $"{_claimPool.Count} idle, claim budget {math.max(0, budget)})");
        }

        /// <summary>Soldiers free for a claim: the claim squad's eligibility
        /// (every other AI draft's). Returns the faction's combat-unit count.</summary>
        private int BuildClaimPool(EntityManager em, Faction faction)
        {
            _claimPool.Clear();
            int army = 0;
            var mq = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using var ents = mq.ToEntityArray(Allocator.Temp);
            using var tags = mq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                Entity e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (IsVerbUnit(em, e)) continue;
                if (IsClaimSquadMember(e)) continue;
                army++;
                if (IsEnrolledInMission(faction, e)) continue;
                if (TransientState.Active<AttackMoveTag>(em, e)) continue;
                if (TransientState.Active<MoveCommand>(em, e)) continue;
                if (TransientState.Active<AttackCommand>(em, e)) continue;
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                _claimPool.Add(e);
            }
            return army;
        }

        /// <summary>Index of the pooled soldier nearest <paramref name="at"/>
        /// (strict &lt; over pool order: deterministic).</summary>
        private int NearestInPool(EntityManager em, float3 at)
        {
            int best = 0;
            float bestD = float.MaxValue;
            for (int i = 0; i < _claimPool.Count; i++)
            {
                float d = math.distancesq(em.GetComponentData<LocalTransform>(_claimPool[i]).Position.xz, at.xz);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>The faction's whole army power (AIEngagement scale),
        /// claim squads and missions included — what defends the ground held.</summary>
        private static int ArmyPowerOf(EntityManager em, Faction faction)
        {
            int sum = 0;
            var mq = QC_UnitTagFactionTagLocalTransform.Get(em, QT_UnitTagFactionTagLocalTransform);
            using var ents = mq.ToEntityArray(Allocator.Temp);
            using var tags = mq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (IsVerbUnit(em, ents[i])) continue;
                sum += TacticalQuery.UnitStrength(em, ents[i]);
            }
            return sum;
        }

        /// <summary>Record a CLAIMED territory of ours that is no longer ours
        /// (decayed, drained or collapsed) — expansion consolidates after it.</summary>
        private void TrackTerritoryLosses(Faction faction, float now)
        {
            int key = (int)faction;
            if (!_claimedLastThink.TryGetValue(key, out var prev))
                _claimedLastThink[key] = prev = new HashSet<int>();
            for (int t = 0; t < RegionMap.Count; t++)
            {
                bool mineNow = TerritoryOwnership.OwnerOf(t) == key && TerritoryOwnership.IsClaimed(t);
                if (mineNow) { prev.Add(t); continue; }
                if (prev.Remove(t))
                {
                    _lastTerritoryLoss[key] = (now, t);
                    AILogger.Log(faction, "EXPAND", $"territory lost: {RegionMap.NameOf(t)}");
                }
            }
        }

        /// <summary>Is expansion consolidating (stretched, or ground just
        /// lost)? The Fortress picker saves for a lock then.</summary>
        private bool IsConsolidating(Faction faction)
            => _consolidating.TryGetValue((int)faction, out bool c) && c;

        /// <summary>
        /// CLAIMS OUTRANK WAVES (Game_AI.md § 5b): while a claim round recently
        /// found no idle soldiers for open ground, the attack waves leave the
        /// idle army to the claims — for at most claimWaveYieldMaxSeconds in a
        /// row, then one wave may go anyway. Never while defending.
        /// </summary>
        private bool ClaimsYieldWave(Faction faction, AIPosture posture, float now)
        {
            int key = (int)faction;
            if (posture == AIPosture.Defend
                || !_claimWantsSoldiersAt.TryGetValue(key, out float at)
                || now - at > Cfg.claimWaveYieldWindowSeconds)
            {
                _claimWaveYieldSince.Remove(key);
                return false;
            }
            if (!_claimWaveYieldSince.TryGetValue(key, out float since))
                _claimWaveYieldSince[key] = since = now;
            if (now - since > Cfg.claimWaveYieldMaxSeconds)
            {
                _claimWaveYieldSince.Remove(key);
                _claimWantsSoldiersAt.Remove(key);
                return false;
            }
            LogClaimBlocked(faction, now, "attack wave held — open ground needs the idle army");
            return true;
        }

        /// <summary>Seconds between re-issuing the move to squad members
        /// that finished a fight elsewhere and went idle off the ground.</summary>
        private const float ClaimReorderSeconds = 15f;

        /// <summary>A failed or timed-out target is skipped this long.</summary>
        private const float SiteBlockSeconds = 150f;

        /// <summary>One think of one squad. True when it was released.</summary>
        private bool TickClaimSquad(EntityManager em, Faction faction, ClaimSquad squad, bool aged, float now)
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

            // A PRE-STAGED squad waits on its target for the age-up; its clock
            // starts when the era turns. A landmark that never finishes
            // releases it.
            if (squad.Staged)
            {
                if (aged)
                {
                    squad.Staged = false;
                    squad.StartedAt = now;
                    squad.NextReorderAt = now;
                    AILogger.Log(faction, "CLAIM",
                        $"{RegionMap.NameOf(t)}: age-up landed, staged squad of {squad.Members.Count} claims");
                }
                else if (squad.Members.Count == 0 || !FactionHasLandmark(em, faction)
                         || now - squad.StartedAt > Cfg.claimPrestageMaxSeconds)
                {
                    ReleaseClaimSquad(squad);
                    AILogger.Log(faction, "CLAIM", $"{RegionMap.NameOf(t)}: staged squad released (age-up not landing)");
                    return true;
                }
            }

            // An exploring squad's job ends when the ground is SEEN: release it
            // and re-pick at once, now knowing what stands there.
            if (squad.Exploring && squad.Members.Count > 0)
            {
                var seedAt = RegionMap.SeedOf(t);
                if (AICommon.IsKnownGround(faction, new float3(seedAt.x, 0f, seedAt.y)))
                {
                    ReleaseClaimSquad(squad);
                    _nextClaimTime[key] = now;
                    AILogger.Log(faction, "CLAIM", $"{RegionMap.NameOf(t)}: scouted, re-picking");
                    return true;
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
                    return false;
                }
            }

            bool owned = TerritoryOwnership.OwnerOf(t) == (int)faction;
            bool claimed = owned && TerritoryOwnership.IsClaimed(t);
            if (claimed && squad.ClaimedAt <= 0f) squad.ClaimedAt = now;
            bool done = owned && TerritoryOwnership.IsLocked(t);
            bool wiped = squad.Members.Count == 0;
            // Claimed ground waits for its lock (an extractor or a Fortress
            // going up there); unclaimed ground has the claim timeout.
            bool timedOut = !squad.Staged && (squad.ClaimedAt > 0f
                ? now - squad.ClaimedAt > Cfg.claimHoldForLockSeconds
                : now - squad.StartedAt >
                  (squad.CurseAssault ? Cfg.claimCurseTimeoutSeconds : Cfg.claimSquadTimeoutSeconds));

            // A rival or the curse locked it under us: nothing a squad can do
            // — except a curse assault, whose whole job is breaking that lock.
            // A curse lock with no live curse node left in the territory is
            // the claim system's last-tick reading of a node that just died.
            bool lockedAgainst = !owned && TerritoryOwnership.IsLocked(t) && !squad.CurseAssault
                && !(TerritoryOwnership.OwnerOf(t) == TerritoryOwnership.Curse
                     && !TryNearestCurseNodeIn(em, t, false, faction, squad.Point, out _));

            if (done || wiped || timedOut || lockedAgainst)
            {
                ReleaseClaimSquad(squad);
                if (!done && !claimed) _siteBlocked[(key, t)] = now + SiteBlockSeconds;
                if (done) _nextClaimTime[key] = math.min(
                    _nextClaimTime.TryGetValue(key, out float nt) ? nt : now, now + Cfg.claimSuccessCooldown);
                AILogger.Log(faction, "CLAIM",
                    $"{RegionMap.NameOf(t)}: " +
                    (done ? "locked, squad released" : wiped ? "squad lost" :
                     lockedAgainst ? "locked by another side" :
                     claimed ? "claimed but no lock came — squad released" : "timed out"));
                return true;
            }

            // Keep the squad ON the ground: members that fought their way off
            // it and went idle walk back. Attack-move, so they fight on the way.
            // Sent as ONE formation (AICommon.IssueGroupOrder); a member still
            // marching on the point it was last sent to is left in its
            // formation, so the 15 s reorder never re-plans a moving group.
            if (now >= squad.NextReorderAt)
            {
                squad.NextReorderAt = now + ClaimReorderSeconds;
                bool pointMoved = math.distancesq(squad.OrderedPoint.xz, squad.Point.xz) > 1f;
                _claimReorder.Clear();
                for (int i = 0; i < squad.Members.Count; i++)
                {
                    var m = squad.Members[i];
                    var p = em.GetComponentData<LocalTransform>(m).Position;
                    // An assault keeps walking from node to node; a claim
                    // only pulls back members who wandered off the ground.
                    if (!squad.CurseAssault && RegionMap.RegionAt(p.x, p.z) == t) continue;
                    if (TransientState.Active<AttackCommand>(em, m)) continue;
                    if (!pointMoved && TransientState.Active<AttackMoveTag>(em, m)) continue;
                    _claimReorder.Add(m);
                }
                AICommon.IssueGroupOrder(em, _claimReorder, squad.Point, attackMove: true, Cfg.waveArrivedRadius);
                squad.OrderedPoint = squad.Point;
            }
            return false;
        }

        private void ReleaseClaimSquad(ClaimSquad squad)
        {
            foreach (var m in squad.Members) _claimSquadMembers.Remove(m);
        }

        /// <summary>Host scratch for the claim squad's reorder (main thread only).</summary>
        private readonly List<Entity> _claimReorder = new List<Entity>();

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

        /// <summary>Why expansion is consolidating, at most once per
        /// claimLogInterval per faction.</summary>
        private void LogExpand(Faction faction, float now, string why)
        {
            int key = (int)faction;
            if (_nextExpandLog.TryGetValue(key, out float next) && now < next) return;
            _nextExpandLog[key] = now + Cfg.claimLogInterval;
            AILogger.Log(faction, "EXPAND", why);
        }

        /// <summary>
        /// Every territory the faction could send a claim squad to, best
        /// first: next to ground it holds that is LINKED to one of its
        /// Fortresses (Territory_Claims.md §10 — only such ground can be
        /// taken), not ours, not already a squad's target, not locked by
        /// anyone else (locked ground is taken by razing, which is the attack
        /// waves' job), and carrying at least one KNOWN resource node — node
        /// ground is the only ground an extractor can lock. Closest first,
        /// richest breaks the tie: every node scores claimNodeBonus, a supply
        /// node claimSupplyNodeBonus more (supply slots are the income), an
        /// uncursed outcrop claimOutcropBonus more. Unseen neighbours follow
        /// as exploring candidates (nearest first).
        ///
        /// VEILSTONE DECIDES WHEN IT IS THE WALL (2026-10-03, Game_AI.md §
        /// Veilstone-driven conquest). While an Alanthor army is short of
        /// veilstone every outcrop is worth claimVeilstoneNodeBonus more,
        /// which outweighs the distance spread between neighbours — and
        /// CURSE-HELD ground with outcrops becomes a candidate too, even
        /// though the curse nodes lock it (an assault, not a squad).
        /// </summary>
        private void CollectClaimCandidates(EntityManager em, Faction faction, float now)
        {
            _claimCandidates.Clear();

            var mine = TerritoryOwnership.TerritoriesOf(faction);
            if (mine.Count == 0) return;
            var squads = ClaimSquadsOf((int)faction);

            var nodeXfs = new List<float3>();
            // Only nodes it has SEEN (AICommon.IsKnownGround): a territory it
            // never scouted has no known node — the scouts, the claim squads
            // and the armies are how it learns.
            CollectNodePositions<IronMineTag>(em, faction, nodeXfs);
            CollectNodePositions<VeilstoneOutcroppingTag>(em, faction, nodeXfs);
            CollectNodePositions<VeilsteelDepositTag>(em, faction, nodeXfs);
            CollectNodePositions<SupplyNodeTag>(em, faction, nodeXfs);
            _claimSupplyNodes.Clear();
            CollectNodePositions<SupplyNodeTag>(em, faction, _claimSupplyNodes);

            _claimOutcrops.Clear();
            _claimAllOutcrops.Clear();
            CollectUncursedOutcrops(em, faction, _claimOutcrops);
            bool wantOutcrops = CultureConfig.GetCompletedCulture(em, faction) == Cultures.Alanthor
                && AIBudget.IsMilitaryShort(faction, AIBudget.ResVeilstone);
            if (wantOutcrops)
                CollectNodePositions<VeilstoneOutcroppingTag>(em, faction, _claimAllOutcrops);
            float3 home = HomeAnchor(em, faction);
            // VEILSTONE SURPLUS (Game_AI.md 5e): outcrops weigh more still.
            float shortBonus = Cfg.claimVeilstoneNodeBonus;
            if (wantOutcrops && IsVeilstoneSurplus(em, faction) && Cfg.surplusClaimOutcropScale > 0f)
                shortBonus *= Cfg.surplusClaimOutcropScale;

            int firstExplore = -1;
            for (int r = 0; r < RegionMap.Count; r++)
            {
                int owner = TerritoryOwnership.OwnerOf(r);
                if (owner == (int)faction) continue;
                if (RegionMap.KindBlocks(RegionMap.KindOf(r))) continue;
                if (!BordersFortressGround(faction, r, mine)) continue;
                if (_siteBlocked.TryGetValue(((int)faction, r), out float until) && now < until) continue;
                bool targeted = false;
                for (int s = 0; s < squads.Count && !targeted; s++) targeted = squads[s].Territory == r;
                if (targeted) continue;

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
                    if (!curseTarget && !AICommon.IsKnownGround(faction, seedPos))
                    {
                        _claimCandidates.Add(new ClaimCandidate
                        {
                            Region = r, Exploring = true, Score = -nearest,
                            Point = new float3(seed.x, TerrainUtility.GetHeight(seed.x, seed.y), seed.y),
                        });
                        if (firstExplore < 0) firstExplore = r;
                    }
                    continue;
                }

                float3 standAt = firstNode;
                // The assault walks to the KNOWN curse node nearest home first.
                if (curseTarget && !TryNearestCurseNodeIn(em, r, true, faction, home, out standAt))
                    continue;

                int outcrops = CountIn(curseTarget ? _claimAllOutcrops : _claimOutcrops, r);
                int supply = CountIn(_claimSupplyNodes, r);

                // A rival's unlocked ground is still a fight; prefer free land.
                // The curse's garrison is a fight too.
                float score = -nearest + nodes * Cfg.claimNodeBonus
                    + supply * Cfg.claimSupplyNodeBonus
                    + outcrops * (Cfg.claimOutcropBonus + (wantOutcrops ? shortBonus : 0f))
                    - (curseTarget ? Cfg.claimCurseTargetPenalty
                       : owner != TerritoryOwnership.Natural ? Cfg.claimNodeBonus * 2f : 0f);
                // Stand by a node: the seed can sit in a lake or a forest, a
                // node never does.
                _claimCandidates.Add(new ClaimCandidate
                {
                    Region = r, CurseHeld = curseTarget, Score = score, Outcrops = outcrops,
                    Point = new float3(standAt.x, TerrainUtility.GetHeight(standAt.x, standAt.z), standAt.z),
                });
            }

            // Known-node ground first (best score), then unseen ground
            // (nearest first); region index breaks ties — deterministic.
            _claimCandidates.Sort((a, b) =>
            {
                if (a.Exploring != b.Exploring) return a.Exploring ? 1 : -1;
                int c = b.Score.CompareTo(a.Score);
                return c != 0 ? c : a.Region.CompareTo(b.Region);
            });
        }

        /// <summary>
        /// May the faction take <paramref name="r"/> by standing on it? It has
        /// to border ground the faction holds that is LINKED to one of its
        /// Fortresses (Territory_Claims.md §10) — exactly
        /// TerritoryClaimSystem.MayTake's Borders test, read from the same
        /// per-tick link table, so the picker never sends a squad to ground
        /// the meter would refuse. Held ground cut off from every Fortress
        /// does not count (it is wearing down, not a base to grow from); a new
        /// Fortress there re-links it and its neighbours become candidates on
        /// the claim system's next tick. No linked ground at all (the capital
        /// lost) means nothing is claimable — there is no plain-adjacency
        /// fallback any more (2026-10-04: it sent squads to ground the meter
        /// refused, which then timed out and blacklisted it).
        /// </summary>
        private static bool BordersFortressGround(Faction faction, int r, List<int> mine)
        {
            for (int i = 0; i < mine.Count; i++)
                if (TheWaningBorder.Systems.World.TerritoryClaimSystem.IsConnected(mine[i], faction)
                    && RegionMap.AreAdjacent(mine[i], r))
                    return true;
            return false;
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
                squad.Members.Add(_curseAssaultArmy[i]);
            // The assault marches as one formation (AICommon.IssueGroupOrder).
            AICommon.IssueGroupOrder(em, _curseAssaultArmy, squad.Point, attackMove: true, Cfg.waveArrivedRadius);
            squad.OrderedPoint = squad.Point;
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
        // There is no territory cap (Territory_Claims.md §10, 2026-10-04): a
        // faction holds what it can defend. A Fortress is how it keeps what
        // its army cannot garrison — it LOCKS its territory and re-links
        // cut-off ground, and new claims may start from its neighbours. It
        // never gates claiming. So after age-up the AI raises one in a held,
        // non-home territory that has none: outcrops first, then ground
        // bordering rivals or the curse; one in flight at a time; only when
        // the bank still covers the reserve after paying. While expansion is
        // CONSOLIDATING (stretched or just lost ground) it saves for one.
        // ─────────────────────────────────────────────────────────────────

        /// <summary>AIPivotalReserve key of a Fortress the faction is saving
        /// for while its expansion consolidates.</summary>
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

            // VEILSTONE SURPLUS (Game_AI.md 5e): outcrop ground weighs more.
            bool surplus = IsVeilstoneSurplus(em, faction);
            float outcropScale = surplus && Cfg.surplusFortressOutcropScale > 0f
                ? Cfg.surplusFortressOutcropScale : 1f;

            int best = RegionMap.None;
            float bestScore = float.MinValue;
            int bestOutcrops = 0, bestFrontier = 0;
            int developing = 0;
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
                // STEP 2 (Game_AI.md 5g): the Fortress goes as soon as the
                // territory's resource buildings are up — towers and
                // production wait for IT, not the other way round — unless
                // the ground is cut off from every Fortress, when the
                // Fortress is what re-links it.
                if (Cfg.fortressAfterResources && !cutOff && !TerritoryResourcesDone(faction, r))
                {
                    developing++;
                    continue;
                }
                var seed = RegionMap.SeedOf(r);
                float dist = math.distance(new float2(seed.x, seed.y), home.xz);

                float score = (outcrops * Cfg.fortressOutcropWeight
                               + frontier * Cfg.fortressFrontierOutcropWeight) * outcropScale
                    + (hostileBorder ? Cfg.fortressBorderBonus : 0f)
                    + (cutOff ? Cfg.fortressDisconnectedBonus : 0f)
                    - dist * Cfg.fortressDistanceWeight;
                // Strict > over held territories in index order: deterministic.
                if (score > bestScore)
                {
                    bestScore = score;
                    best = r;
                    bestOutcrops = outcrops;
                    bestFrontier = frontier;
                }
            }
            if (best == RegionMap.None)
            {
                AIPivotalReserve.Clear(faction, FortressReserveKey);
                LogFortress(faction, now, developing > 0
                    ? $"{developing} territory(ies) without one still on step 1 (resource buildings)"
                    : "no held territory without a Fortress");
                return;
            }

            int held = TheWaningBorder.Systems.World.TerritoryClaimSystem.TerritoriesHeldBy(faction);
            bool consolidating = IsConsolidating(faction);

            var cost = TheWaningBorder.Data.BuildCosts.For(em, faction, "Fortress");
            var need = cost + Cost.Of(Cfg.fortressReserveSupplies, Cfg.fortressReserveIron,
                                      Cfg.fortressReserveVeilstone);
            if (!FactionEconomy.CanAfford(em, faction, need))
            {
                // Consolidating, the Fortress IS the expansion — it locks
                // ground the army cannot garrison: save for it (non-strict,
                // so the hold breathes). Otherwise just wait for the bank.
                if (consolidating) AIPivotalReserve.Set(faction, FortressReserveKey, need);
                else AIPivotalReserve.Clear(faction, FortressReserveKey);
                LogFortress(faction, now,
                    $"{(consolidating ? "saving" : "waiting")} for a Fortress in {RegionMap.NameOf(best)} " +
                    $"({held} territories; needs {need.Supplies}s {need.Iron}i {need.Veilstone}v " +
                    $"with the reserve)");
                return;
            }
            AIPivotalReserve.Clear(faction, FortressReserveKey);

            var at = RegionMap.SeedOf(best);
            var anchor = new float3(at.x, TerrainUtility.GetHeight(at.x, at.y), at.y);
            // THE RESERVED SPOT (Game_AI.md 5g) is tried first, as it is.
            bool onSpot = AIBaseLayout.TryGetFortressSpot(faction, best, out float3 spotAt);
            if (onSpot) anchor = spotAt;
            bool ok;
            string reason;
            // A surplus Fortress on (or bordering) outcrop ground may use the
            // army's veilstone earmark: the ground is more Outposts, the
            // income the earmark is waiting on.
            bool carve = surplus && bestOutcrops + bestFrontier > 0;
            _siteRegionLock = best;
            _surplusEarmarkCarve = carve;
            _fortressSpotExact = onSpot;
            try { ok = TryBuildBuildingWithReason(em, faction, "Fortress", out reason, anchor); }
            finally { _siteRegionLock = RegionMap.None; _surplusEarmarkCarve = false; _fortressSpotExact = false; }

            if (ok)
            {
                string line = $"Fortress ordered in {RegionMap.NameOf(best)} — {bestOutcrops} outcrop(s), " +
                              $"score {bestScore:0}, territories {held}, Fortresses {own + 1}" +
                              (onSpot ? $", on its reserved spot ({spotAt.x:F0},{spotAt.z:F0})" : "");
                AILogger.Log(faction, "TERRITORY", $"{RegionMap.NameOf(best)}: step 2 Fortress ordered");
                AILogger.Log(faction, "EXPANSION", line);
                AILogger.Log(faction, "BUILDING", line);
                if (surplus && bestOutcrops + bestFrontier > 0)
                    LogSurplus(faction, $"veilstone Fortress -> {RegionMap.NameOf(best)} " +
                        $"({bestOutcrops} outcrop(s) inside, {bestFrontier} on the frontier" +
                        (carve ? ", army earmark usable)" : ")"));
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
