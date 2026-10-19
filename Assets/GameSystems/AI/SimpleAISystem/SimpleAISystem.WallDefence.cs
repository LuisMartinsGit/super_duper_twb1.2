// SimpleAISystem.WallDefence.cs
// The AI mans its fortifications (2026-10-20, developer: "Make sure the AI
// places garrisons in towers and places units on walls";
// docs/Design/Game_AI.md § 6s).
//
//   1. TOWER GARRISONS — every finished building of ours with garrison slots
//      (the Alanthor Watch Tower today) is kept filled with ranged foot
//      units, threatened towers first, then the ones farthest from the
//      capital (the border), up to a share of the ranged army and never
//      below a field reserve. Refilled as occupants die. The order is the
//      player's: CommandRouter.IssueGarrisonWall (walk there, go in).
//   2. MANNING THE WALL — when hostile strength gathers near a stretch of
//      our STONE wall, nearby ranged (and, when outnumbered there, some
//      melee to hold the walk) go up onto that stretch's wall-walk with the
//      player's wall-top order (WallTopOrders.IssueWallTopMove: the same
//      deck slots, the same CommandRouter layered move). They come down —
//      a plain move to the ground inside the wall — once the stretch has
//      been quiet for wallReleaseAfterSeconds.
//   3. SENTRIES — in peacetime a few ranged stand on the long walls
//      (wallSentryPiecesPerSentry pieces each, at most wallSentryMax).
//
// A POSTED UNIT IS NOBODY ELSE'S. Every draft in this system already skips
// IsClaimSquadMember; that test now includes IsWallPosted, so a post is out
// of the wave, its reinforcements, the raid party, claim and curse squads,
// the economy defence, the wall guard and the Shardroot strike. Defend's
// recall skips posts too, and the endgame systems' idle test
// (AIEndgameCommon.IsIdleSoldier) asks the same set. A garrisoned unit is
// disabled and is invisible to every query anyway. THE ALL-IN releases the
// sentries; the towers keep their men and a threatened wall is still manned
// (the home guard the all-in leaves behind).
//
// Host-only AI state, like every other AI dictionary here: the orders are
// lockstep commands, the bookkeeping is not simulation.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Issuing;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private const byte PostTower = 0;
        private const byte PostManned = 1;
        private const byte PostSentry = 2;

        private sealed class WallPost
        {
            public Faction Faction;
            public byte Kind;
            /// <summary>The tower (PostTower).</summary>
            public Entity Building;
            /// <summary>The wall stretch's bucket key (PostManned / PostSentry).</summary>
            public long Stretch;
            /// <summary>Match second the order was given.</summary>
            public float Since;
        }

        /// <summary>Every posted unit, all factions. Static so the endgame
        /// systems' idle test can ask it (AIEndgameCommon.IsIdleSoldier).</summary>
        private static readonly Dictionary<Entity, WallPost> _wallPosts = new Dictionary<Entity, WallPost>();
        private static int _wallPostEpoch = int.MinValue;

        /// <summary>Last second each manned stretch saw a threat, per faction.</summary>
        private readonly Dictionary<int, Dictionary<long, float>> _mannedStretches
            = new Dictionary<int, Dictionary<long, float>>();
        private readonly Dictionary<int, float> _nextWallDefence = new Dictionary<int, float>();
        private int _wallDefenceEpoch = -1;

        // Scratch, pooled.
        private readonly List<Entity> _wdScratch = new List<Entity>();
        private readonly List<Entity> _wdGroup = new List<Entity>();
        private readonly List<(Entity E, float D2)> _wdPool = new List<(Entity, float)>();
        private readonly Dictionary<long, WallStretch> _wdStretches = new Dictionary<long, WallStretch>();
        private readonly List<long> _wdKeys = new List<long>();

        private struct WallStretch
        {
            public float3 Sum;
            public int Pieces;
            public float3 Anchor;      // the piece nearest the centroid, filled in later
            public float3 Deck;        // a deck point on that piece's walk
            public bool HasDeck;
            public int Threat;
            public int Posted;
        }

        /// <summary>True while <paramref name="e"/> holds an AI tower or wall
        /// post — it is out of every draft (see the file header).</summary>
        internal static bool IsWallPosted(Entity e)
        {
            if (_wallPostEpoch != SimCadence.Epoch) return false;
            return _wallPosts.ContainsKey(e);
        }

        static readonly ComponentType[] QT_WdGarrisonable =
        {
            ComponentType.ReadOnly<WallGarrisonSlot>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_WdGarrisonable;

        static readonly ComponentType[] QT_WdStoneWall =
        {
            ComponentType.ReadOnly<WallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.Exclude<PalisadeTag>(),
            ComponentType.Exclude<UnderConstruction>(),
        };
        static CachedEntityQuery QC_WdStoneWall;

        private void TickWallDefence(EntityManager em, Faction faction, in SimpleAIState aiState, float now)
        {
            var cfg = Cfg;
            if (cfg.wallDefenceIntervalSeconds <= 0f) return;
            if (_wallPostEpoch != SimCadence.Epoch) { _wallPostEpoch = SimCadence.Epoch; _wallPosts.Clear(); }
            if (_wallDefenceEpoch != SimCadence.Epoch)
            {
                _wallDefenceEpoch = SimCadence.Epoch;
                _nextWallDefence.Clear();
                _mannedStretches.Clear();
            }
            int fk = (int)faction;
            if (_nextWallDefence.TryGetValue(fk, out float at) && now < at) return;
            _nextWallDefence[fk] = now + cfg.wallDefenceIntervalSeconds;

            PruneWallPosts(em, faction, now);

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall)) return;
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;

            // THE ALL-IN (§ 6n): the idle army is the wave's, so the peacetime
            // sentries come down and no new ones go up. The towers keep their
            // men and a threatened wall is still manned: that is the home
            // guard the all-in leaves behind ("the home guard defends, the
            // army stays in the field").
            bool allIn = AllInArmed(faction);
            if (allIn)
            {
                int down = ReleaseSentries(em, faction, hallPos);
                if (down > 0) AILogger.Log(faction, "WALLS", $"all-in — {down} sentry(ies) down off the walls to join the army");
            }

            TickTowerGarrisons(em, faction, hallPos, now);
            TickWallManning(em, faction, hallPos, now, sentries: !allIn);
        }

        // ─────────────────────────────────────────────────────────────────
        // BOOKKEEPING
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Drop posts whose unit died, was put out of its tower, or
        /// never reached / fell off the wall (no deck, no layered order, past
        /// the grace for the lockstep command to land).</summary>
        private void PruneWallPosts(EntityManager em, Faction faction, float now)
        {
            _wdScratch.Clear();
            foreach (var kv in _wallPosts)
            {
                if (kv.Value.Faction != faction) continue;
                var u = kv.Key;
                var p = kv.Value;
                bool keep;
                if (!em.Exists(u)) keep = false;
                else if (p.Kind == PostTower)
                {
                    keep = em.HasComponent<WallGarrisonedIn>(u)
                        || (WallGarrison.IsApproaching(em, u) && em.Exists(p.Building))
                        || now - p.Since < Cfg.wallPostGraceSeconds;
                }
                else
                {
                    bool onDeck = em.HasComponent<NavLayerIndex>(u)
                        && em.GetComponentData<NavLayerIndex>(u).Layer == NavLayerIndex.LayerRampart;
                    bool climbing = em.HasComponent<LayeredMoveOrder>(u)
                        && em.GetComponentData<LayeredMoveOrder>(u).TargetLayer == NavLayerIndex.LayerRampart;
                    keep = onDeck || climbing || now - p.Since < Cfg.wallPostGraceSeconds;
                }
                if (!keep) _wdScratch.Add(u);
            }
            for (int i = 0; i < _wdScratch.Count; i++) _wallPosts.Remove(_wdScratch[i]);
        }

        /// <summary>Order every sentry of the faction down. Returns how many.</summary>
        private int ReleaseSentries(EntityManager em, Faction faction, float3 hallPos)
        {
            _wdKeys.Clear();
            foreach (var kv in _wallPosts)
                if (kv.Value.Faction == faction && kv.Value.Kind == PostSentry && !_wdKeys.Contains(kv.Value.Stretch))
                    _wdKeys.Add(kv.Value.Stretch);
            _wdKeys.Sort();
            int n = 0;
            for (int i = 0; i < _wdKeys.Count; i++)
                n += ReleaseWallPosts(em, faction, hallPos, _wdKeys[i], includeSentries: true, onlySentries: true);
            return n;
        }

        /// <summary>Order a faction's wall posts down (one stretch, or every
        /// stretch with <paramref name="stretch"/> -1) and forget them. They
        /// walk to the ground just inside the wall, toward the capital, and are
        /// free for the army. Returns how many.</summary>
        private int ReleaseWallPosts(EntityManager em, Faction faction, float3 hallPos,
            long stretch, bool includeSentries, bool onlySentries = false)
        {
            _wdScratch.Clear();
            foreach (var kv in _wallPosts)
            {
                var p = kv.Value;
                if (p.Faction != faction || p.Kind == PostTower) continue;
                if (!includeSentries && p.Kind == PostSentry) continue;
                if (onlySentries && p.Kind != PostSentry) continue;
                if (stretch != -1 && p.Stretch != stretch) continue;
                _wdScratch.Add(kv.Key);
            }
            if (_wdScratch.Count == 0) return 0;
            _wdScratch.Sort((a, b) => a.Index.CompareTo(b.Index));

            // One group per stretch, each to the ground inside its own stretch.
            int n = 0;
            while (_wdScratch.Count > 0)
            {
                long key = _wallPosts[_wdScratch[0]].Stretch;
                _wdGroup.Clear();
                float3 c = float3.zero;
                for (int i = _wdScratch.Count - 1; i >= 0; i--)
                {
                    var u = _wdScratch[i];
                    if (_wallPosts[u].Stretch != key) continue;
                    _wdScratch.RemoveAt(i);
                    _wallPosts.Remove(u);
                    if (!em.Exists(u) || !em.HasComponent<LocalTransform>(u)) continue;
                    _wdGroup.Add(u);
                    c += em.GetComponentData<LocalTransform>(u).Position;
                }
                if (_wdGroup.Count == 0) continue;
                _wdGroup.Sort((a, b) => a.Index.CompareTo(b.Index));
                c /= _wdGroup.Count;
                float3 inward = hallPos - c; inward.y = 0f;
                float len = math.length(inward);
                float3 dest = len > 1e-3f
                    ? c + inward / len * math.min(len, math.max(0f, Cfg.wallReleaseInsetMeters))
                    : c;
                dest.y = TerrainUtility.GetHeight(dest.x, dest.z);
                AICommon.IssueGroupOrder(em, _wdGroup, dest, attackMove: false);
                n += _wdGroup.Count;
            }
            return n;
        }

        /// <summary>The free foot soldiers a post may take, nearest
        /// <paramref name="near"/> first: the drafts' usual rules (not on a
        /// mission, a claim, a post; not fighting under an order; not the
        /// player's), foot only (WallGarrison.IsFootUnit — no cavalry, siege,
        /// heroes, workers, scouts), of <paramref name="cls"/>, within
        /// <paramref name="radius"/>. Peacetime posts also skip anyone walking
        /// or in a fight.</summary>
        private void CollectPostCandidates(EntityManager em, Faction faction, float3 near,
            float radius, UnitClass cls, bool peacetime)
        {
            _wdPool.Clear();
            float r2 = radius * radius;
            var roster = OwnCombatRoster(em, faction);
            for (int i = 0; i < roster.Count; i++)
            {
                Entity e = roster.E[i];
                if (roster.UnderConstruction(i) || roster.Verb(i) || roster.NoControl(i) || roster.Plunderer(i)) continue;
                float d2 = math.distancesq(roster.P[i].xz, near.xz);
                if (d2 > r2) continue;
                if (!em.Exists(e) || em.GetComponentData<UnitTag>(e).Class != cls) continue;
                if (!WallGarrison.IsFootUnit(em, e)) continue;
                if (IsClaimSquadMember(e)) continue;          // claims, responses, posts
                if (IsEnrolledInMission(faction, e)) continue;
                if (WallGarrison.IsApproaching(em, e)) continue;
                if (TransientState.Active<AttackCommand>(em, e)) continue;
                if (TransientState.Active<AttackMoveTag>(em, e)) continue;
                if (TransientState.Active<UserMoveOrder>(em, e)) continue;
                if (peacetime)
                {
                    if (TransientState.Active<MoveCommand>(em, e)) continue;
                    if (em.HasComponent<Target>(e) && em.GetComponentData<Target>(e).Value != Entity.Null) continue;
                }
                _wdPool.Add((e, d2));
            }
            _wdPool.Sort((a, b) =>
            {
                int c = a.D2.CompareTo(b.D2);
                return c != 0 ? c : a.E.Index.CompareTo(b.E.Index);
            });
        }

        /// <summary>Combat units free for the field (the roster less posts,
        /// missions and claims) — what a peacetime post must leave behind.</summary>
        private int FreeFieldArmy(EntityManager em, Faction faction)
        {
            int n = 0;
            var roster = OwnCombatRoster(em, faction);
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster.UnderConstruction(i) || roster.Verb(i) || roster.NoControl(i) || roster.Plunderer(i)) continue;
                if (IsClaimSquadMember(roster.E[i])) continue;
                n++;
            }
            return n;
        }

        private int CountPosts(Faction faction, byte kind)
        {
            int n = 0;
            foreach (var kv in _wallPosts)
                if (kv.Value.Faction == faction && kv.Value.Kind == kind) n++;
            return n;
        }

        // ─────────────────────────────────────────────────────────────────
        // 1. TOWER GARRISONS
        // ─────────────────────────────────────────────────────────────────

        private void TickTowerGarrisons(EntityManager em, Faction faction, float3 hallPos, float now)
        {
            var cfg = Cfg;
            if (cfg.towerGarrisonMaxRangedShare <= 0f) return;

            var q = QC_WdGarrisonable.Get(em, QT_WdGarrisonable);
            if (q.IsEmptyIgnoreFilter) return;
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Budget: a share of the ranged army (field + already garrisoned).
            int rangedField = 0;
            var roster = OwnCombatRoster(em, faction);
            for (int i = 0; i < roster.Count; i++)
                if (!roster.UnderConstruction(i) && !roster.Verb(i)
                    && em.GetComponentData<UnitTag>(roster.E[i]).Class == UnitClass.Ranged)
                    rangedField++;
            int garrisoned = CountPosts(faction, PostTower);
            int budget = (int)math.floor((rangedField + garrisoned) * math.saturate(cfg.towerGarrisonMaxRangedShare)) - garrisoned;
            int reserve = FreeFieldArmy(em, faction) - cfg.wallPostMinFieldArmy;
            budget = math.min(budget, reserve);
            if (budget <= 0) return;

            // Towers in need, threatened first, then farthest from the capital.
            var needs = new List<(Entity T, float3 P, int Free, float Score)>();
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var t = ents[i];
                int slots = WallGarrison.SlotCount(em, t);
                if (slots <= 0) continue;
                int pending = 0;
                foreach (var kv in _wallPosts)
                    if (kv.Value.Kind == PostTower && kv.Value.Building == t && !em.HasComponent<WallGarrisonedIn>(kv.Key)) pending++;
                int free = slots - WallGarrison.OccupantCount(em, t) - pending;
                if (free <= 0) continue;
                float3 p = xfs[i].Position;
                int threat = TacticalQuery.EnemyStrengthInRadius(em, faction, p, cfg.towerThreatRadius);
                float score = threat * 1000f + math.distance(p.xz, hallPos.xz);
                needs.Add((t, p, free, score));
            }
            if (needs.Count == 0) return;
            needs.Sort((a, b) =>
            {
                int c = b.Score.CompareTo(a.Score);
                return c != 0 ? c : a.T.Index.CompareTo(b.T.Index);
            });

            for (int n = 0; n < needs.Count && budget > 0; n++)
            {
                var need = needs[n];
                CollectPostCandidates(em, faction, need.P, cfg.towerGarrisonDraftRadius, UnitClass.Ranged, peacetime: true);
                int take = math.min(math.min(need.Free, budget), _wdPool.Count);
                if (take <= 0) continue;
                for (int k = 0; k < take; k++)
                {
                    var u = _wdPool[k].E;
                    CommandRouter.IssueGarrisonWall(em, u, need.T, CommandSource.AI);
                    _wallPosts[u] = new WallPost { Faction = faction, Kind = PostTower, Building = need.T, Since = now };
                }
                budget -= take;
                AILogger.Log(faction, "WALLS",
                    $"garrisoned {take} in {TowerName(em, need.T)} at ({need.P.x:0},{need.P.z:0}) " +
                    $"({WallGarrison.OccupantCount(em, need.T)}/{WallGarrison.SlotCount(em, need.T)} in, " +
                    $"{CountPosts(faction, PostTower)} tower post(s) total)");
            }
        }

        private static string TowerName(EntityManager em, Entity t)
            => em.HasComponent<WatchTowerTag>(t) ? "WatchTower" : "garrison building";

        // ─────────────────────────────────────────────────────────────────
        // 2-3. MANNING THE WALL, AND THE SENTRIES
        // ─────────────────────────────────────────────────────────────────

        private static long StretchKey(float3 p, float size)
        {
            long x = (long)math.floor(p.x / size);
            long z = (long)math.floor(p.z / size);
            return (x << 32) ^ (z & 0xffffffffL);
        }

        private void TickWallManning(EntityManager em, Faction faction, float3 hallPos, float now, bool sentries)
        {
            var cfg = Cfg;
            float size = math.max(4f, cfg.wallStretchMeters);
            int fk = (int)faction;

            // The stone wall, cut into stretches (a grid bucket of pieces).
            _wdStretches.Clear();
            var q = QC_WdStoneWall.Get(em, QT_WdStoneWall);
            if (q.IsEmptyIgnoreFilter) { ReleaseAllWallPostsQuietly(em, faction, hallPos); return; }
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
            {
                int pieces = 0;
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    float3 p = xfs[i].Position;
                    long key = StretchKey(p, size);
                    _wdStretches.TryGetValue(key, out var s);
                    s.Sum += p; s.Pieces++;
                    _wdStretches[key] = s;
                    pieces++;
                }
                if (pieces == 0) { ReleaseAllWallPostsQuietly(em, faction, hallPos); return; }
                // Anchor each stretch on the piece nearest its centroid (a bend's
                // centroid can sit off the wall).
                var best = new Dictionary<long, float>();
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    float3 p = xfs[i].Position;
                    long key = StretchKey(p, size);
                    var s = _wdStretches[key];
                    float3 c = s.Sum / s.Pieces;
                    float d = math.distancesq(p.xz, c.xz);
                    if (best.TryGetValue(key, out float bd) && bd <= d) continue;
                    best[key] = d;
                    s.Anchor = p;
                    _wdStretches[key] = s;
                }
            }

            _wdKeys.Clear();
            foreach (var k in _wdStretches.Keys) _wdKeys.Add(k);
            _wdKeys.Sort();
            for (int i = 0; i < _wdKeys.Count; i++)
            {
                var s = _wdStretches[_wdKeys[i]];
                s.HasDeck = WallTopOrders.TryNearestDeckPoint(s.Anchor, 4f, out s.Deck);
                s.Threat = s.HasDeck ? TacticalQuery.EnemyStrengthInRadius(em, faction, s.Deck, cfg.wallThreatRadius) : 0;
                _wdStretches[_wdKeys[i]] = s;
            }
            foreach (var kv in _wallPosts)
                if (kv.Value.Faction == faction && kv.Value.Kind != PostTower
                    && _wdStretches.TryGetValue(kv.Value.Stretch, out var ps))
                {
                    ps.Posted++;
                    _wdStretches[kv.Value.Stretch] = ps;
                }

            if (!_mannedStretches.TryGetValue(fk, out var manned))
                _mannedStretches[fk] = manned = new Dictionary<long, float>();

            // Posts on a stretch that no longer stands (or lost its walk) come down.
            _wdKeys.Clear();
            foreach (var kv in _wallPosts)
                if (kv.Value.Faction == faction && kv.Value.Kind != PostTower
                    && (!_wdStretches.TryGetValue(kv.Value.Stretch, out var gs) || !gs.HasDeck)
                    && !_wdKeys.Contains(kv.Value.Stretch))
                    _wdKeys.Add(kv.Value.Stretch);
            for (int i = 0; i < _wdKeys.Count; i++)
            {
                int down = ReleaseWallPosts(em, faction, hallPos, _wdKeys[i], includeSentries: true);
                manned.Remove(_wdKeys[i]);
                if (down > 0)
                    AILogger.Log(faction, "WALLS", $"wall stretch gone — {down} unit(s) down, back to the army");
            }

            // ── THREAT: man the threatened stretches, strongest threat first ──
            _wdKeys.Clear();
            foreach (var kv in _wdStretches)
                if (kv.Value.HasDeck && kv.Value.Threat >= cfg.wallThreatMinStrength) _wdKeys.Add(kv.Key);
            _wdKeys.Sort((a, b) =>
            {
                int c = _wdStretches[b].Threat.CompareTo(_wdStretches[a].Threat);
                return c != 0 ? c : a.CompareTo(b);
            });
            int stretchesManned = 0;
            for (int i = 0; i < _wdKeys.Count && stretchesManned < cfg.wallMaxMannedStretches; i++)
            {
                long key = _wdKeys[i];
                var s = _wdStretches[key];
                manned[key] = now;
                stretchesManned++;

                // How many men the threat asks for, in own-ranged strength units.
                CollectPostCandidates(em, faction, s.Deck, cfg.wallManDraftRadius, UnitClass.Ranged, peacetime: false);
                int unitPower = 0;
                for (int k = 0; k < _wdPool.Count; k++) unitPower += TacticalQuery.UnitStrength(em, _wdPool[k].E);
                unitPower = _wdPool.Count > 0 ? math.max(1, unitPower / _wdPool.Count) : 30;
                int desired = (int)math.ceil(s.Threat * math.max(0f, cfg.wallManStrengthRatio) / unitPower);
                desired = math.clamp(desired, cfg.wallManMin, cfg.wallManMax);
                int need = desired - s.Posted;
                if (need <= 0) continue;

                _wdGroup.Clear();
                for (int k = 0; k < _wdPool.Count && _wdGroup.Count < need; k++) _wdGroup.Add(_wdPool[k].E);
                int ranged = _wdGroup.Count;

                // Outnumbered here: some melee hold the walk too.
                int melee = 0;
                int ownHere = TacticalQuery.FactionStrengthInRadius(em, faction, s.Deck, cfg.wallThreatRadius);
                if (s.Threat > ownHere * math.max(0f, cfg.wallMeleeOutnumberedRatio) && cfg.wallMeleeMax > 0)
                {
                    CollectPostCandidates(em, faction, s.Deck, cfg.wallManDraftRadius, UnitClass.Melee, peacetime: false);
                    for (int k = 0; k < _wdPool.Count && melee < cfg.wallMeleeMax; k++) { _wdGroup.Add(_wdPool[k].E); melee++; }
                }
                if (_wdGroup.Count == 0) continue;
                _wdGroup.Sort((a, b) => a.Index.CompareTo(b.Index));
                int sent = WallTopOrders.IssueWallTopMove(em, _wdGroup, s.Deck, cfg.wallPostSpacingMeters, CommandSource.AI);
                for (int k = 0; k < _wdGroup.Count; k++)
                    _wallPosts[_wdGroup[k]] = new WallPost { Faction = faction, Kind = PostManned, Stretch = key, Since = now };
                AILogger.Log(faction, "WALLS",
                    $"manned wall ({sent}: {ranged} ranged, {melee} melee) at ({s.Deck.x:0},{s.Deck.z:0}) " +
                    $"vs threat {s.Threat} (own {ownHere} here, {s.Posted} already up)");
            }

            // ── QUIET: a manned stretch quiet for long enough comes down ──
            _wdKeys.Clear();
            foreach (var kv in manned)
            {
                bool threatNow = _wdStretches.TryGetValue(kv.Key, out var ms) && ms.Threat >= cfg.wallThreatMinStrength;
                if (threatNow) continue;
                if (now - kv.Value < cfg.wallReleaseAfterSeconds) continue;
                _wdKeys.Add(kv.Key);
            }
            _wdKeys.Sort();
            for (int i = 0; i < _wdKeys.Count; i++)
            {
                manned.Remove(_wdKeys[i]);
                int down = ReleaseWallPosts(em, faction, hallPos, _wdKeys[i], includeSentries: false);
                if (down > 0)
                    AILogger.Log(faction, "WALLS",
                        $"threat gone — {down} unit(s) down off the wall, back to the army");
            }

            // ── PEACE: a few sentries on the long walls ──
            if (sentries) TickWallSentries(em, faction, hallPos, now);
        }

        private void ReleaseAllWallPostsQuietly(EntityManager em, Faction faction, float3 hallPos)
        {
            ReleaseWallPosts(em, faction, hallPos, -1, includeSentries: true);
            if (_mannedStretches.TryGetValue((int)faction, out var m)) m.Clear();
        }

        private void TickWallSentries(EntityManager em, Faction faction, float3 hallPos, float now)
        {
            var cfg = Cfg;
            if (cfg.wallSentryMax <= 0 || cfg.wallSentryPiecesPerSentry <= 0) return;

            int pieces = 0;
            foreach (var kv in _wdStretches) pieces += kv.Value.Pieces;
            int want = math.min(cfg.wallSentryMax, pieces / cfg.wallSentryPiecesPerSentry);
            int have = CountPosts(faction, PostSentry);
            if (want <= have) return;
            if (FreeFieldArmy(em, faction) - cfg.wallPostMinFieldArmy <= 0) return;

            // Spread round the walls: stretches by bearing from the capital,
            // every (count / want)-th one, skipping any that already has a man.
            _wdKeys.Clear();
            foreach (var kv in _wdStretches)
                if (kv.Value.HasDeck) _wdKeys.Add(kv.Key);
            if (_wdKeys.Count == 0) return;
            _wdKeys.Sort((a, b) =>
            {
                float2 da = _wdStretches[a].Deck.xz - hallPos.xz, db = _wdStretches[b].Deck.xz - hallPos.xz;
                int c = math.atan2(da.y, da.x).CompareTo(math.atan2(db.y, db.x));
                return c != 0 ? c : a.CompareTo(b);
            });
            int step = math.max(1, _wdKeys.Count / math.max(1, want));
            int added = 0;
            for (int i = 0; i < _wdKeys.Count && have + added < want; i += step)
            {
                long key = _wdKeys[i];
                var s = _wdStretches[key];
                if (s.Posted > 0) continue;
                if (FreeFieldArmy(em, faction) - cfg.wallPostMinFieldArmy <= 0) break;
                CollectPostCandidates(em, faction, s.Deck, cfg.towerGarrisonDraftRadius, UnitClass.Ranged, peacetime: true);
                if (_wdPool.Count == 0) continue;
                _wdGroup.Clear();
                _wdGroup.Add(_wdPool[0].E);
                WallTopOrders.IssueWallTopMove(em, _wdGroup, s.Deck, cfg.wallPostSpacingMeters, CommandSource.AI);
                _wallPosts[_wdGroup[0]] = new WallPost { Faction = faction, Kind = PostSentry, Stretch = key, Since = now };
                s.Posted++;
                _wdStretches[key] = s;
                added++;
            }
            if (added > 0)
                AILogger.Log(faction, "WALLS",
                    $"posted {added} sentry(ies) on the wall ({have + added}/{want}, {pieces} wall pieces)");
        }
    }
}
