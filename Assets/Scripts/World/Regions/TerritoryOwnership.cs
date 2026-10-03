// TerritoryOwnership.cs
// Who holds each territory.
//
// docs/Design/Territory_Claims.md (2026-09-29, FOURTH MODEL): ground belongs to
// whoever STANDS on it. Each territory carries one ownership meter — a holder
// and a value 0..100 — advanced once a second by TerritoryClaimSystem from the
// military units standing in it. Reaching 100 claims the territory; falling
// back to 0 from claimed loses it (and collapses every building the loser had
// there). Finished buildings HOLD ground against decay; extractors on resource
// nodes, Fortresses and curse nodes LOCK it against draining.
//
// This class is the meter's STATE and the placement rules that read it. The
// meter is genuine simulation state (the Hall-claim model derived ownership
// from live Halls and stored nothing), so it is advanced only on the lockstep
// clock, reset per match, and hashed by LockstepStateHash.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.World.Regions
{
    public static class TerritoryOwnership
    {
        #region Cached queries

        // Every query in here used to be a fresh CreateEntityQuery (and a
        // Dispose) per call — and OnFreeNodeFor / TrySnapToNode /
        // HallCapReached run per CANDIDATE in the AI's site search and per
        // frame under the placement ghost. Creating a query walks every
        // archetype in the world. See Core/CachedEntityQuery.cs; these are
        // never disposed (world teardown owns them).

        /// <summary>One cached query per runtime-typed shape (the node and
        /// extractor tags are only known as ComponentType values).</summary>
        private sealed class TypedQuery
        {
            public ComponentType[] Types;
            public TheWaningBorder.Core.CachedEntityQuery Q;
        }

        private static readonly Dictionary<TypeIndex, TypedQuery> _withTransform =
            new Dictionary<TypeIndex, TypedQuery>();

        /// <summary>Cached {tag, LocalTransform} query for a runtime tag.</summary>
        internal static EntityQuery TagWithTransform(EntityManager em, ComponentType tag)
        {
            if (!_withTransform.TryGetValue(tag.TypeIndex, out var box))
            {
                box = new TypedQuery
                {
                    Types = new[] { tag, ComponentType.ReadOnly<LocalTransform>() },
                };
                _withTransform[tag.TypeIndex] = box;
            }
            return box.Q.Get(em, box.Types);
        }

        /// <summary>Per-T cache for the generic claim scan.</summary>
        private static class ClaimQuery<T> where T : unmanaged, IComponentData
        {
            public static readonly ComponentType[] Types =
            {
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
            };
            public static TheWaningBorder.Core.CachedEntityQuery Q;
        }

        private static readonly ComponentType[] QT_HallTagLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static TheWaningBorder.Core.CachedEntityQuery QC_HallTagLocalTransform;

        /// <summary>Every Hall (finished or not) — the hall-cap scan, shared
        /// with the AI's placement snapshot.</summary>
        internal static EntityQuery HallQuery(EntityManager em)
            => QC_HallTagLocalTransform.Get(em, QT_HallTagLocalTransform);

        #endregion

        /// <summary>Unowned. Natural ground -- claimable by anyone.</summary>
        public const int Natural = -1;

        /// <summary>Held by the curse (Territory_Claims.md §6).</summary>
        public const int Curse = -2;

        /// <summary>The meter's full scale, in thousandths of a point. Stored
        /// as an integer so every lockstep peer agrees on it to the unit and
        /// the state hash can read it exactly.</summary>
        public const int MeterMax = 100_000;

        private static int[] _owner = System.Array.Empty<int>();

        // ── The meter (Territory_Claims.md §2). Advanced ONLY by
        //    TerritoryClaimSystem, on the lockstep clock. ──
        private static int[] _holder = System.Array.Empty<int>();     // side, or Natural
        private static int[] _value = System.Array.Empty<int>();      // 0..MeterMax
        private static byte[] _claimed = System.Array.Empty<byte>();  // reached 100, not back to 0 since
        private static byte[] _locked = System.Array.Empty<byte>();   // last tick's lock verdict
        private static byte[] _contested = System.Array.Empty<byte>(); // last tick: frozen by hostiles
        private static int[] _challenger = System.Array.Empty<int>(); // last tick: the side draining it, or Natural

        /// <summary>The side that drained (or tried to drain — a locked
        /// territory refuses it) this territory on the last claim tick, or
        /// <see cref="Natural"/>. Presentation only: it is a per-tick reading
        /// of who stood there, not state the meter depends on.</summary>
        public static int ChallengerOf(int t) => t >= 0 && t < _challenger.Length ? _challenger[t] : Natural;

        /// <summary>The side filling (or holding) this territory's meter: a
        /// Faction cast to int, <see cref="Curse"/>, or <see cref="Natural"/>
        /// when nobody has put weight on it.</summary>
        public static int HolderOf(int t) => t >= 0 && t < _holder.Length ? _holder[t] : Natural;

        /// <summary>The meter, 0..100.</summary>
        public static float ValueOf(int t) => t >= 0 && t < _value.Length ? _value[t] / 1000f : 0f;

        /// <summary>Raw meter in thousandths (sim and hash use).</summary>
        public static int RawValueOf(int t) => t >= 0 && t < _value.Length ? _value[t] : 0;

        public static bool IsClaimed(int t) => t >= 0 && t < _claimed.Length && _claimed[t] != 0;

        /// <summary>Locked on the last claim tick: a finished extractor,
        /// Fortress or curse node of the owner stands in it.</summary>
        public static bool IsLocked(int t) => t >= 0 && t < _locked.Length && _locked[t] != 0;

        /// <summary>Frozen on the last claim tick: hostile sides shared it.</summary>
        public static bool IsContested(int t) => t >= 0 && t < _contested.Length && _contested[t] != 0;

        /// <summary>Sizes the meter for the current partition. A new
        /// partition (a new map) starts every territory unclaimed.</summary>
        internal static bool EnsureMeter()
        {
            int count = RegionMap.Count;
            if (count == 0) return false;
            if (_holder.Length == count) return true;
            _holder = new int[count];
            _value = new int[count];
            _claimed = new byte[count];
            _locked = new byte[count];
            _contested = new byte[count];
            _challenger = new int[count];
            for (int i = 0; i < count; i++) { _holder[i] = Natural; _challenger[i] = Natural; }
            return true;
        }

        /// <summary>Every territory back to unclaimed — a new match on the
        /// same partition must not inherit the last one's meters.</summary>
        internal static void ResetMeter()
        {
            if (!EnsureMeter()) return;
            for (int i = 0; i < _holder.Length; i++)
            {
                _holder[i] = Natural;
                _value[i] = 0;
                _claimed[i] = 0;
                _locked[i] = 0;
                _contested[i] = 0;
                _challenger[i] = Natural;
            }
            Publish();
        }

        /// <summary>The meter write TerritoryClaimSystem makes each tick.</summary>
        internal static void SetMeter(int t, int holder, int value, bool claimed, bool locked, bool contested,
                                      int challenger = Natural)
        {
            _challenger[t] = challenger;
            _holder[t] = holder;
            _value[t] = value;
            _claimed[t] = (byte)(claimed ? 1 : 0);
            _locked[t] = (byte)(locked ? 1 : 0);
            _contested[t] = (byte)(contested ? 1 : 0);
        }

        /// <summary>
        /// Claim a territory outright for <paramref name="side"/> (a Faction
        /// cast to int, or <see cref="Curse"/>): meter full, claimed. Used for
        /// a home territory under its Fortress at match start and for the
        /// curse's seeded nodes (Territory_Claims.md §4, §6.4) — ground that
        /// is owned from tick 0 rather than stood on.
        /// </summary>
        public static void ForceClaim(int t, int side)
        {
            if (!EnsureMeter() || t < 0 || t >= _holder.Length) return;
            _holder[t] = side;
            _value[t] = MeterMax;
            _claimed[t] = 1;
            Publish();
        }

        /// <summary>
        /// Legacy hook of the Hall-claim era. The curse now claims on the
        /// meter like everyone else (Territory_Claims.md §6): an explicit
        /// "held" is a <see cref="ForceClaim"/>, and a release is left to the
        /// meter, which decays or is drained like any other claim.
        /// </summary>
        public static void MarkCurseHeld(int territory, bool held)
        {
            if (held) ForceClaim(territory, Curse);
        }

        public static bool IsCurseHeld(int territory) => OwnerOf(territory) == Curse;

        public static int CurseHeldCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _owner.Length; i++) if (_owner[i] == Curse) n++;
                return n;
            }
        }

        /// <summary>
        /// Bumped whenever a Recompute actually CHANGES who owns something
        /// (and on Reset). Ownership is the only variable territory state
        /// (Regions.md §3b — no influence maps), so everything that draws or
        /// derives from territory gates its rebuild on this number instead
        /// of recomputing per frame.
        /// </summary>
        public static int Version { get; private set; }

        private static int[] _prevOwner = System.Array.Empty<int>();

        public static bool Ready => _owner.Length > 0;

        /// <summary>
        /// Owner of a territory: a Faction cast to int, or
        /// <see cref="Natural"/> / <see cref="Curse"/>.
        /// </summary>
        public static int OwnerOf(int territory) =>
            territory >= 0 && territory < _owner.Length ? _owner[territory] : Natural;

        public static bool IsOwnedBy(int territory, Faction f) =>
            OwnerOf(territory) == (int)f;

        /// <summary>Owner of the territory under a world position.</summary>
        public static int OwnerAt(float worldX, float worldZ)
        {
            int t = RegionMap.RegionAt(worldX, worldZ);
            return t == RegionMap.None ? Natural : OwnerOf(t);
        }

        /// <summary>Territories held by a faction. Allocates; UI/AI use only.</summary>
        public static List<int> TerritoriesOf(Faction f)
        {
            var list = new List<int>();
            for (int i = 0; i < _owner.Length; i++)
                if (_owner[i] == (int)f) list.Add(i);
            return list;
        }

        public static int CountOf(Faction f)
        {
            int n = 0;
            for (int i = 0; i < _owner.Length; i++) if (_owner[i] == (int)f) n++;
            return n;
        }

        public static void Reset()
        {
            _owner = System.Array.Empty<int>();
            _prevOwner = System.Array.Empty<int>();
            _holder = System.Array.Empty<int>();
            _value = System.Array.Empty<int>();
            _claimed = System.Array.Empty<byte>();
            _locked = System.Array.Empty<byte>();
            _contested = System.Array.Empty<byte>();
            _challenger = System.Array.Empty<int>();
            Version++;
        }

        /// <summary>
        /// Can <paramref name="f"/> plant a claim structure at this position?
        ///
        /// False on a LIVE enemy claim: Regions.md §2 requires the existing
        /// structure to be destroyed first, so taking ground is always two acts
        /// -- break, then build -- with a window in between where the territory
        /// belongs to nobody and either side can take it.
        ///
        /// True on your own territory: a second fortification in ground you
        /// already hold is a defensive choice, not a claim, and blocking it
        /// would be a strange rule.
        /// </summary>
        public static bool CanClaim(Faction f, float worldX, float worldZ)
        {
            int owner = OwnerAt(worldX, worldZ);
            return owner == Natural || owner == (int)f;
        }

        /// <summary>
        /// The ONE building that takes ground: the HALL, for every culture
        /// (Regions.md §2). It is therefore the only building placeable outside
        /// territory you already hold — gate it like the rest and no player
        /// could ever expand.
        ///
        /// The per-culture claim structures are retired. An Alanthor
        /// fortification, a Runai trade post and a Feraldis totem were three
        /// names for one mechanic, they arrived only at age-up, and they made
        /// "can I build here" a question with a different answer per culture.
        /// They are ordinary buildings now, and go inside your own ground.
        /// </summary>
        public static bool IsClaimStructure(string buildingId) => false;

        /// <summary>
        /// The Hall is REMOVED (Territory_Claims.md §4): its roster and
        /// research live on the Fortress. The id stays in the catalog
        /// (scenarios, legacy references), but no one may place one.
        /// </summary>
        public static bool IsRetiredBuilding(string buildingId) => buildingId == "Hall";

        /// <summary>
        /// One Fortress per territory (Territory_Claims.md §4). Counts
        /// Fortresses under construction too, or a double-click slips a
        /// second one past.
        /// </summary>
        public static bool FortressCapReached(EntityManager em, float worldX, float worldZ)
        {
            if (!RegionMap.Ready) return false;
            int here = RegionMap.RegionAt(worldX, worldZ);
            if (here == RegionMap.None) return false;
            var q = QC_Fortresses.Get(em, QT_Fortresses);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
                if (RegionMap.NearestRegion(xfs[i].Position.x, xfs[i].Position.z) == here)
                    return true;
            return false;
        }

        private static readonly ComponentType[] QT_Fortresses =
        {
            ComponentType.ReadOnly<FortressTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        private static TheWaningBorder.Core.CachedEntityQuery QC_Fortresses;

        /// <summary>
        /// May <paramref name="faction"/> raise <paramref name="buildingId"/>
        /// here? The single authority — the placement preview, the local spawn
        /// guard, the command router and the AI's site picker all ask this, so
        /// none of them can disagree about where a building is legal.
        ///
        /// Regions.md §6 supersedes Overview.md's "Alanthor players cannot
        /// build outside their own influence": the gate is TERRITORY now, and
        /// it applies to every culture and to Age 0, where you hold exactly the
        /// region your start sits in (§2). A claim structure may additionally
        /// go on Natural ground — that is <see cref="CanClaim"/>, and it is the
        /// whole expansion loop.
        ///
        /// FAIL-OPEN on a map with no partition (scenarios, the sandbox, a map
        /// missing its seeds): a gate that cannot answer must not be the reason
        /// nothing can be built.
        /// </summary>
        public static bool CanBuildAt(EntityManager em, Faction faction,
                                      string buildingId, float worldX, float worldZ)
            => TerritoryRefusal(em, faction, buildingId, worldX, worldZ) == PlacementRefusal.None;

        /// <summary>True when the territory rules are switched off for this
        /// match: scenarios and the sandbox are fixtures, not matches — they
        /// build their board wherever the author or the tester points. Same
        /// carve out VictoryConditionSystem makes for the same reason.</summary>
        private static bool RulesOff =>
            GameSettings.IsSandbox || GameSettings.Mode == GameMode.Scenario;

        /// <summary>
        /// <see cref="CanBuildAt"/> with its REASON — the placement ghost names
        /// the rule a red preview broke instead of a generic "invalid
        /// placement". Same rule, same order; <see cref="PlacementRefusal.None"/>
        /// is the only "yes".
        ///
        /// For a Hall this includes the ADJACENCY RULE (Regions.md §2,
        /// 2026-09-26): a claim goes only into a Natural territory that shares
        /// a border with one the faction already holds, so expansion grows
        /// outward from the ground you have instead of hopping across the map.
        /// </summary>
        public static PlacementRefusal TerritoryRefusal(EntityManager em, Faction faction,
                                      string buildingId, float worldX, float worldZ)
        {
            if (RulesOff) return PlacementRefusal.None;
            if (!RegionMap.Ready) return PlacementRefusal.None;

            // Ownership is recomputed on TerritoryIncomeSystem's 5 s tick, so
            // for the first few seconds of a match nothing is owned yet. Derive
            // it once here rather than letting that window be a free-for-all.
            if (!Ready) Recompute(em);
            if (!Ready) return PlacementRefusal.None;

            // RegionAt, matching what the borders DRAW. Ground no region can
            // own is unowned for everyone (Regions.md §1), so the gate must not
            // quietly hand it to whoever is nearest — a player would be refused
            // at a spot inside their painted border, or allowed at one outside
            // it, and either way the line would be lying.
            //
            // None means unclaimable: mountain, cliff, water, the rim. Answering
            // TRUE there is not a hole in the rule — that band is exactly what
            // PassabilityGrid marks impassable, so nothing can be built on it
            // anyway, and a gate that cannot say whose ground it is must not be
            // the thing that refuses.
            int t = RegionMap.RegionAt(worldX, worldZ);
            if (t == RegionMap.None) return PlacementRefusal.None;

            int owner = OwnerOf(t);
            if (owner == (int)faction) return PlacementRefusal.None;

            // EVERY building goes on ground you own (Territory_Claims.md §5).
            // There is no claim structure any more: ground is taken by
            // standing on it, so nothing may be raised outside it.
            if (owner == Curse) return PlacementRefusal.HeldByCurse;
            if (owner != Natural) return PlacementRefusal.HeldByRival;
            return PlacementRefusal.NotYourTerritory;
        }

        /// <summary>
        /// Does <paramref name="territory"/> share a border with a territory
        /// <paramref name="faction"/> holds right now? The Hall adjacency rule
        /// (Regions.md §2). Holding means a FINISHED claim — a Hall still under
        /// construction claims nothing (see <see cref="Claim{T}"/>), so it
        /// cannot be the stepping stone for the next one either.
        /// </summary>
        public static bool IsAdjacentToHeld(Faction faction, int territory)
        {
            if (territory < 0) return false;
            for (int i = 0; i < _owner.Length; i++)
                if (_owner[i] == (int)faction && RegionMap.AreAdjacent(i, territory))
                    return true;
            return false;
        }

        // ── Hall worker proximity (Regions.md §2, 2026-09-26) ───────────

        private static TerritoryOwnershipConfig _cfg;
        private static TerritoryOwnershipConfig Cfg =>
            _cfg != null ? _cfg
            : (_cfg = TheWaningBorder.Core.Settings.ComponentConfig.Require<TerritoryOwnershipConfig>());

        /// <summary>How close (metres, XZ) one of the placing faction's
        /// workers must stand to a Hall site for the claim to be accepted.
        /// Read from TerritoryOwnership.asset.</summary>
        public static float HallWorkerRange => Cfg.hallWorkerRange;

        /// <summary>
        /// True when this building's placement must name a worker standing
        /// near the site: the Hall, and only when the territory rules are on.
        /// A claim is the one purchase that takes ground, so it is the one
        /// that has to be MADE there — a player cannot drop a Hall on the far
        /// side of the map from a worker standing at home.
        /// </summary>
        public static bool NeedsWorkerNearby(string buildingId)
            => IsClaimStructure(buildingId) && !RulesOff && RegionMap.Ready;

        /// <summary>
        /// Is <paramref name="worker"/> a live worker of <paramref name="faction"/>
        /// within <see cref="HallWorkerRange"/> of the site AND standing
        /// inside the territory the site is in (Regions.md §2, 2026-09-27)?
        /// Reads replicated simulation state only, so the lockstep executor
        /// reaches the same verdict on every peer at the execution tick.
        /// </summary>
        public static PlacementRefusal CheckHallWorker(EntityManager em, Faction faction,
            Entity worker, float worldX, float worldZ)
        {
            if (!IsLiveWorker(em, faction, worker)) return PlacementRefusal.NoWorker;
            var p = em.GetComponentData<LocalTransform>(worker).Position;
            float dx = p.x - worldX, dz = p.z - worldZ;
            float r = HallWorkerRange;
            if (dx * dx + dz * dz > r * r) return PlacementRefusal.WorkerTooFar;
            return IsInSiteTerritory(p.x, p.z, worldX, worldZ)
                ? PlacementRefusal.None
                : PlacementRefusal.WorkerOutsideTerritory;
        }

        /// <summary>
        /// Does the point (<paramref name="x"/>, <paramref name="z"/>) lie in
        /// the same territory as the site? The claim is made from INSIDE the
        /// ground it takes — a worker standing across the border in the
        /// faction's own region is not on the ground being claimed. A site no
        /// region owns (RegionMap.None — already answered "no gate" by
        /// <see cref="TerritoryRefusal"/>) asks nothing more of the worker.
        /// </summary>
        public static bool IsInSiteTerritory(float x, float z, float siteX, float siteZ)
        {
            if (!RegionMap.Ready) return true;
            int site = RegionMap.RegionAt(siteX, siteZ);
            if (site == RegionMap.None) return true;
            return RegionMap.RegionAt(x, z) == site;
        }

        /// <summary>A worker (CanBuild, not conscripted) owned by the
        /// faction, alive, with a transform.</summary>
        public static bool IsLiveWorker(EntityManager em, Faction faction, Entity e)
        {
            if (e == Entity.Null || !em.Exists(e)) return false;
            if (!em.HasComponent<CanBuild>(e) || !em.HasComponent<LocalTransform>(e)) return false;
            if (em.HasComponent<ConscriptedTag>(e)) return false;
            if (!em.HasComponent<FactionTag>(e) || em.GetComponentData<FactionTag>(e).Value != faction)
                return false;
            if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) return false;
            return true;
        }

        /// <summary>
        /// The worker of <paramref name="faction"/> among
        /// <paramref name="candidates"/> (the local player's selection) the
        /// Hall command should carry, or Entity.Null. The nearest worker that
        /// PASSES <see cref="CheckHallWorker"/> (in range and inside the
        /// site's territory) wins; failing that, the nearest live worker, so
        /// the refusal names the rule it broke. Distance ties break on list
        /// order — this is a UI helper; the chosen id then rides the command,
        /// so peers never pick.
        /// </summary>
        public static Entity NearestWorker(EntityManager em, Faction faction,
            System.Collections.Generic.IReadOnlyList<Entity> candidates, float worldX, float worldZ)
        {
            Entity best = Entity.Null, bestOk = Entity.Null;
            float bestD = float.MaxValue, bestOkD = float.MaxValue;
            if (candidates == null) return best;
            for (int i = 0; i < candidates.Count; i++)
            {
                var e = candidates[i];
                if (!IsLiveWorker(em, faction, e)) continue;
                var p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - worldX, dz = p.z - worldZ;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = e; }
                if (d < bestOkD
                    && CheckHallWorker(em, faction, e, worldX, worldZ) == PlacementRefusal.None)
                { bestOkD = d; bestOk = e; }
            }
            return bestOk != Entity.Null ? bestOk : best;
        }

        // ── Hall cost escalation (Regions.md §2 "No territory hopping") ──

        private static readonly ComponentType[] QT_ExpansionHalls =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<FortressTag>(),
        };
        private static TheWaningBorder.Core.CachedEntityQuery QC_ExpansionHalls;

        /// <summary>The escalation step from TerritoryOwnership.asset.</summary>
        public static float HallCostStep => Cfg.hallCostStep;

        /// <summary>
        /// N in the Hall price: the faction's live and under-construction
        /// Halls, NOT counting the starting Fortress (which carries HallTag
        /// but was never bought). Reads replicated entity state only, so every
        /// lockstep peer counts the same N at the execution tick.
        /// </summary>
        public static int ExpansionHallCount(EntityManager em, Faction faction)
        {
            var q = QC_ExpansionHalls.Get(em, QT_ExpansionHalls);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            int n = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) n++;
            return n;
        }

        /// <summary>
        /// The multiplier on the Hall's base price for the next Hall this
        /// faction places: 1 + <see cref="HallCostStep"/> x N. Applies in
        /// every mode — the escalation is a price, not a territory rule, so it
        /// does not switch off with <see cref="RulesOff"/>.
        /// </summary>
        public static float HallCostMultiplier(EntityManager em, Faction faction)
            => 1f + HallCostStep * ExpansionHallCount(em, faction);

        /// <summary>
        /// True when this territory already has a Hall. One Hall claims the
        /// ground; a second claims nothing, so there is no reason to allow it.
        /// Replaces the old flat six-per-faction cap — how wide you spread is
        /// now limited by how much ground you can hold, not by a number.
        ///
        /// Counts Halls UNDER CONSTRUCTION too, or a double-click during the
        /// build slips a second one past.
        /// </summary>
        public static bool HallCapReached(EntityManager em, float worldX, float worldZ)
        {
            if (!RegionMap.Ready) return false;
            int here = RegionMap.RegionAt(worldX, worldZ);
            if (here == RegionMap.None) return false;

            var q = HallQuery(em);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
            bool found = false;
            for (int i = 0; i < xfs.Length && !found; i++)
            {
                var p = xfs[i].Position;
                found = RegionMap.RegionAt(p.x, p.z) == here;
            }
            return found;
        }

        /// <summary>
        /// How close a Gatherer's Hut has to be to a supply node to count as
        /// standing ON it. One build cell of slack — the hut snaps to the grid
        /// and the node snaps to its cell centre, so a strict test would refuse
        /// placements that visually land dead on the node.
        /// </summary>
        private const float SupplyNodeSnapRange = 4f;

        /// <summary>
        /// A Gatherer's Hut may ONLY be raised on a supply node, one hut per
        /// node (docs/Design/Regions.md §4). That single rule replaced two
        /// crutches: a magic per-territory hut cap, and the gather-area yield
        /// the player had to survey the ground for. How many huts a territory
        /// supports is now map data, and can differ between a rich territory
        /// and a poor one.
        ///
        /// Answers TRUE on a map with NO supply nodes at all, so a scene that
        /// has not been seeded yet is merely unbalanced rather than unplayable.
        /// </summary>
        /// <summary>
        /// EVERY RESOURCE HAS ITS OWN EXTRACTION BUILDING, AND IT STANDS ON THE
        /// NODE. Returns the node tag a building must sit on, or null when the
        /// building is not an extractor.
        ///
        /// Supplies were already gated this way. Iron, veilstone and veilsteel
        /// were not: a Mine could be raised anywhere and counted toward ANY node
        /// within 12 m, so one generic building served all three resources and
        /// the choice of what to extract did not exist. Naming the pairing in
        /// one place is what makes the placement rule, the income tick and the
        /// AI's site picker agree about it.
        /// </summary>
        //
        // EVERY ARM IS CAST. ComponentType defines an implicit conversion FROM
        // System.Type, so a bare `_ => null` does not make the switch nullable
        // — the compiler picks ComponentType as the common type and compiles
        // the null arm into op_Implicit((Type)null), which reaches
        // TypeManager.GetTypeIndex(null) and throws
        // "Unknown Type:`null`" at RUNTIME.
        //
        // It threw for every building that is NOT an extractor, inside the
        // placement candidate loop, so the AI could not site anything at all.
        // The cast on each arm forces ComponentType? and cannot be undone by
        // an implicit conversion.
        public static ComponentType? RequiredNodeFor(string buildingId) => buildingId switch
        {
            "GatherersHut"     => (ComponentType?)ComponentType.ReadOnly<SupplyNodeTag>(),
            "Mine"             => (ComponentType?)ComponentType.ReadOnly<IronMineTag>(),
            "VeilstoneMine"    => (ComponentType?)ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
            // Alanthor's veilstone building stands ON the outcrop too
            // (docs/Design/Veilstone_Economy.md §3.1, 2026-10-01).
            "Alanthor_TradingOutpost" => (ComponentType?)ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
            _ => null,
        };

        /// <summary>The tag that identifies an already-built extractor of this
        /// kind. Buildings carry tags, not ids, so occupancy is tested by tag.</summary>
        /// Cast on every arm — same implicit-conversion trap as RequiredNodeFor.
        private static ComponentType? ExtractorTagFor(string buildingId) => buildingId switch
        {
            "GatherersHut"     => (ComponentType?)ComponentType.ReadOnly<GathererHutTag>(),
            "Mine"             => (ComponentType?)ComponentType.ReadOnly<MineTag>(),
            "VeilstoneMine"    => (ComponentType?)ComponentType.ReadOnly<VeilstoneMineTag>(),
            "Alanthor_TradingOutpost" => (ComponentType?)ComponentType.ReadOnly<TradingOutpostTag>(),
            _ => null,
        };

        /// <summary>
        /// ONE MINE BUTTON (2026-09-29): the ore extractors — iron Mine and
        /// Veilstone Mine — are one "Mine" in the build menu, and the node
        /// under the cursor decides which is raised. Returns the concrete id
        /// whose free node is nearest <paramref name="pos"/> (within snap
        /// reach), or false when none is. The Veilstone Mine is not offered to
        /// Alanthor, who trade for veilstone (docs/Design/Veilstone_Economy.md §3.1).
        /// UI-side: the AI and the executor always deal in the concrete ids.
        /// </summary>
        public static bool ResolveExtractorAt(EntityManager em, Faction faction, float3 pos,
                                              out string buildingId, out float3 snapped)
        {
            buildingId = null;
            snapped = pos;
            float best = float.MaxValue;
            for (int i = 0; i < MineIds.Length; i++)
            {
                string id = MineIds[i];
                if (!MayBuildMine(em, faction, id)) continue;
                // The one Mine button raises Alanthor's Trading Outpost on a
                // veilstone outcrop, and only Alanthor's.
                if (id == "Alanthor_TradingOutpost"
                    && CultureConfig.GetCompletedCulture(em, faction) != Cultures.Alanthor) continue;
                if (!TrySnapToNode(em, id, pos, out var at)) continue;
                float d = math.lengthsq(new float2(at.x - pos.x, at.z - pos.z));
                if (d < best) { best = d; buildingId = id; snapped = at; }
            }
            return buildingId != null;
        }

        /// <summary>The ids the one Mine button stands for.</summary>
        public static readonly string[] MineIds = { "Mine", "VeilstoneMine", "Alanthor_TradingOutpost" };

        /// <summary>
        /// False for a Veilstone Mine under an Alanthor faction: Alanthor do
        /// not mine the curse's crystal, they trade beside it — and their Age 0
        /// Veilstone Mines become Trading Outposts at age-up. The iron Mine is
        /// every culture's (docs/Design/Veilstone_Economy.md §3.1).
        /// </summary>
        public static bool MayBuildMine(EntityManager em, Faction faction, string buildingId)
            => buildingId != "VeilstoneMine"
               || CultureConfig.GetCompletedCulture(em, faction) != Cultures.Alanthor;

        /// <summary>True when this building must be raised on a resource node.</summary>
        public static bool IsExtractor(string buildingId)
            => RequiredNodeFor(buildingId) != null;

        /// <summary>
        /// Is there a FREE node of the kind <paramref name="buildingId"/> needs
        /// within snap range of this point?
        ///
        /// One extractor per node: the node count is what limits how many a
        /// territory supports, which is the whole reason nodes replaced the old
        /// area-based caps.
        ///
        /// Defined as "<see cref="TrySnapToNode"/> found one", so the rule that
        /// decides WHERE a building lands and the rule that decides WHETHER it
        /// may cannot drift apart. They were separate passes with separate
        /// radii once and the placement preview and the command router
        /// disagreed about the same click.
        ///
        /// Answers TRUE on a map with no nodes of that kind at all, so an
        /// unseeded scene is merely unbalanced rather than unplayable.
        /// </summary>
        public static bool OnFreeNodeFor(EntityManager em, string buildingId,
                                         float worldX, float worldZ)
        {
            var required = RequiredNodeFor(buildingId);
            if (required == null) return true;   // not an extractor — no node rule

            if (TagWithTransform(em, required.Value).IsEmpty)
                return true;                     // unseeded map — stay buildable

            return TrySnapToNode(em, buildingId,
                                 new float3(worldX, 0f, worldZ), out _);
        }

        /// <summary>
        /// An extractor does not stand NEAR its node, it stands ON it: move
        /// <paramref name="pos"/> onto the free node this building needs, so
        /// its footprint covers the node's own cells.
        ///
        /// Returns false — and leaves <paramref name="snapped"/> at
        /// <paramref name="pos"/> — for anything that is not an extractor, and
        /// for an extractor with no free node in reach. This is the single
        /// implementation behind <see cref="OnFreeNodeFor"/> too, so "it
        /// snapped" and "it is legal" are the same question asked twice.
        ///
        /// The result is then put through the ordinary build-grid snap for the
        /// BUILDING's own footprint, so an extractor is still grid-aligned like
        /// everything else. Every resource node AND every resource building is
        /// 2 x 2 cells with even parity (docs/Design/Build_Grid.md §3,
        /// 2026-09-29), so the extractor lands EXACTLY on its node.
        ///
        /// Deterministic: nearest node wins, ties broken on the node's own
        /// coordinates rather than on entity order, so every lockstep peer
        /// picks the same one.
        /// </summary>
        public static bool TrySnapToNode(EntityManager em, string buildingId,
                                         float3 pos, out float3 snapped)
        {
            snapped = pos;

            var required = RequiredNodeFor(buildingId);
            if (required == null) return false;

            var nodes = TagWithTransform(em, required.Value).ToComponentDataArray<LocalTransform>(
                Unity.Collections.Allocator.Temp);

            // Occupancy is read ONCE, not re-queried per candidate node. The
            // per-candidate version created and disposed an entity query for
            // every node in range, on a path the placement ghost runs every
            // frame, and query matching walks every archetype in the world.
            var taken = ExtractorPositions(em, buildingId);

            int nodeCount = Fill(ref _scratchNodes, nodes);
            int takenCount = taken.IsCreated ? Fill(ref _scratchTaken, taken) : 0;
            if (taken.IsCreated) taken.Dispose();
            nodes.Dispose();

            return SnapAmong(buildingId, pos, _scratchNodes, nodeCount,
                             _scratchTaken, takenCount, out snapped);
        }

        // Managed scratch for the live path, so it and the AI's snapshot path
        // share ONE implementation of the nearest-free-node rule. Main-thread
        // only (every caller is managed ECS / UI code).
        private static LocalTransform[] _scratchNodes = System.Array.Empty<LocalTransform>();
        private static LocalTransform[] _scratchTaken = System.Array.Empty<LocalTransform>();

        private static int Fill(ref LocalTransform[] into,
            Unity.Collections.NativeArray<LocalTransform> from)
        {
            if (into.Length < from.Length) into = new LocalTransform[math.max(from.Length, 16)];
            for (int i = 0; i < from.Length; i++) into[i] = from[i];
            return from.Length;
        }

        /// <summary>
        /// The nearest-free-node rule over caller-supplied node and occupancy
        /// lists — <see cref="TrySnapToNode"/> reads them live, the AI's
        /// placement snapshot reads them once per tick. Same rule, same
        /// tie-break, so the two cannot disagree.
        /// </summary>
        internal static bool SnapAmong(string buildingId, float3 pos,
            LocalTransform[] nodes, int nodeCount,
            LocalTransform[] taken, int takenCount, out float3 snapped)
        {
            snapped = pos;

            float r2 = SupplyNodeSnapRange * SupplyNodeSnapRange;
            bool found = false;
            float bestD2 = float.MaxValue;
            float3 best = default;

            for (int i = 0; i < nodeCount; i++)
            {
                var np = nodes[i].Position;
                float dx = np.x - pos.x, dz = np.z - pos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                if (Occupied(taken, takenCount, np.x, np.z, r2)) continue;

                // Strictly-better, then a coordinate tie-break: two nodes at
                // the same distance must resolve identically on every peer.
                if (!found || d2 < bestD2
                    || (d2 == bestD2 && (np.x < best.x || (np.x == best.x && np.z < best.z))))
                {
                    found = true;
                    bestD2 = d2;
                    best = np;
                }
            }

            if (!found) return false;

            snapped = BuildGrid.Snap(new float3(best.x, pos.y, best.z), buildingId);
            return true;
        }

        /// <summary>Where this kind of extractor already stands. Caller disposes.</summary>
        private static Unity.Collections.NativeArray<LocalTransform> ExtractorPositions(
            EntityManager em, string buildingId)
        {
            var tag = ExtractorTagFor(buildingId);
            if (tag == null) return default;

            return TagWithTransform(em, tag.Value)
                .ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);
        }

        /// <summary>Tag of an already-built extractor of this kind, for the
        /// AI's placement snapshot (null for non-extractors).</summary>
        internal static ComponentType? ExtractorTagOf(string buildingId) => ExtractorTagFor(buildingId);

        private static bool Occupied(LocalTransform[] taken, int count, float x, float z, float r2)
        {
            for (int i = 0; i < count; i++)
            {
                var p = taken[i].Position;
                float dx = p.x - x, dz = p.z - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        /// <summary>
        /// Refresh the resolved owner array from the meter. Kept under its old
        /// name because a dozen readers call it to make sure ownership is
        /// current; the meter itself only moves in TerritoryClaimSystem.
        /// </summary>
        public static void Recompute(EntityManager em)
        {
            if (RegionMap.Count == 0) { Reset(); return; }
            EnsureMeter();
            Publish();
        }

        /// <summary>
        /// Owner = holder while claimed, else Natural. Version bumps only on a
        /// REAL change, so everything gated on it (the border ribbon, the
        /// ground mask, the rasterized ownership grid) stays untouched across
        /// the no-op refreshes.
        /// </summary>
        internal static void Publish()
        {
            int count = _holder.Length;
            if (_owner.Length != count) _owner = new int[count];
            for (int i = 0; i < count; i++)
                _owner[i] = _claimed[i] != 0 ? _holder[i] : Natural;

            bool changed = _prevOwner.Length != _owner.Length;
            if (!changed)
                for (int i = 0; i < _owner.Length; i++)
                    if (_prevOwner[i] != _owner[i]) { changed = true; break; }
            if (changed)
            {
                if (_prevOwner.Length != _owner.Length) _prevOwner = new int[_owner.Length];
                System.Array.Copy(_owner, _prevOwner, _owner.Length);
                Version++;
            }
        }
    }

    /// <summary>
    /// WHY a placement was refused. Every placement stage that can say no
    /// answers with one of these, so the ghost can name the rule instead of a
    /// generic "invalid placement", and the executor can tell the local player
    /// why a queued order was dropped. Localised by the UI; the simulation only
    /// ever compares against <see cref="None"/>.
    /// </summary>
    public enum PlacementRefusal : byte
    {
        None = 0,
        /// <summary>Id unknown to the catalog — never silently a Hut.</summary>
        UnknownBuilding,
        /// <summary>Slope, water, map edge, impassable ground or an obstacle.</summary>
        Terrain,
        /// <summary>The footprint overlaps an existing building.</summary>
        Overlap,
        /// <summary>Veil crust (only the Veilworks may stand on it).</summary>
        CursedGround,
        /// <summary>An ordinary building outside ground you hold.</summary>
        NotYourTerritory,
        /// <summary>The territory is held by another player.</summary>
        HeldByRival,
        /// <summary>The territory is held by the curse.</summary>
        HeldByCurse,
        /// <summary>One Hall per territory.</summary>
        HallAlreadyHere,
        /// <summary>A Hall must go next to a territory you hold.</summary>
        NotAdjacent,
        /// <summary>A Hall needs one of your workers within range.</summary>
        WorkerTooFar,
        /// <summary>No live worker of yours was named for the Hall.</summary>
        NoWorker,
        /// <summary>An extractor off a free node of its own kind.</summary>
        OffNode,
        /// <summary>A War Totem off blood.</summary>
        NotOnBlood,
        /// <summary>A Sawyer away from a forest.</summary>
        NotByForest,
        /// <summary>A per-faction cap (Smelters, sect buildings).</summary>
        CapReached,
        /// <summary>The Hall's worker is in range but not standing inside
        /// the territory the Hall would claim.</summary>
        WorkerOutsideTerritory,
        /// <summary>One Fortress per territory.</summary>
        FortressAlreadyHere,
        /// <summary>A building that can no longer be placed (the Hall).</summary>
        Retired,
        /// <summary>The footprint covers a resource node it was not made for
        /// (Build_Grid.md §3) — only a node's own extractor stands on it.</summary>
        OnResourceNode,
        /// <summary>This culture may not raise this building (Alanthor and the
        /// Veilstone Mine — Veilstone_Economy.md §3.1).</summary>
        WrongCulture,
        /// <summary>The veilstone outcrop is cursed or mined out.</summary>
        OutcropUnavailable,
        /// <summary>A Trading Outpost with no free, uncursed outcrop beside it.</summary>
        NoOutcropNearby,
    }

    /// <summary>
    /// The player-facing line for each <see cref="PlacementRefusal"/>,
    /// localised (Loc.Pt.Notifications.cs holds the Portuguese). Shared by
    /// the placement ghost and the lockstep executor's refusal notice, so a
    /// refusal reads the same whichever side caught it.
    /// </summary>
    public static class PlacementRefusalText
    {
        public static string Of(PlacementRefusal r, string buildingId = null)
        {
            string en = r switch
            {
                PlacementRefusal.UnknownBuilding  => "That building cannot be placed",
                PlacementRefusal.Terrain          => "The ground here is unsuitable",
                PlacementRefusal.Overlap          => "Something is already built here",
                PlacementRefusal.CursedGround     => "Cannot build on cursed ground",
                PlacementRefusal.NotYourTerritory => "You can only build in territory you own — stand your army on it to claim it",
                PlacementRefusal.HeldByRival      => "Cannot claim ground another player holds",
                PlacementRefusal.HeldByCurse      => "The curse holds this territory",
                PlacementRefusal.HallAlreadyHere  => "This territory already has a Hall",
                PlacementRefusal.NotAdjacent      => "A Hall must border a territory you hold",
                PlacementRefusal.WorkerTooFar    => "Worker too far from the Hall site",
                PlacementRefusal.NoWorker        => "Select a worker to place a Hall",
                PlacementRefusal.OffNode          => ExtractorLine(buildingId),
                PlacementRefusal.NotOnBlood       => "War Totems must be planted on blood",
                PlacementRefusal.NotByForest      => "Sawyers must be built against a forest",
                PlacementRefusal.CapReached       => "You have the most of that building you may hold",
                PlacementRefusal.WorkerOutsideTerritory
                    => "The worker must stand inside the territory the Hall will claim",
                PlacementRefusal.FortressAlreadyHere => "This territory already has a Fortress",
                PlacementRefusal.Retired          => "That building can no longer be built",
                PlacementRefusal.OnResourceNode   => "Cannot build on a resource node — only its own extractor may stand there",
                PlacementRefusal.WrongCulture     => "Alanthor do not mine veilstone — raise a Trading Outpost beside it",
                PlacementRefusal.OutcropUnavailable => "This veilstone outcrop is cursed or mined out",
                PlacementRefusal.NoOutcropNearby  => "Trading Outposts must stand on an uncursed veilstone outcrop",
                _                                 => "Invalid placement",
            };
            return TheWaningBorder.Core.Localization.Loc.T(en);
        }

        /// <summary>Why an extractor was refused, named by the node it
        /// wanted — the iron Mine and the Veilstone Mine are different
        /// buildings wanting different ground.</summary>
        private static string ExtractorLine(string buildingId) => buildingId switch
        {
            "GatherersHut"     => "Gatherer's Huts must be built on a free supply node",
            "Mine"             => "Mines must be built on a free iron or veilstone node",
            "VeilstoneMine"    => "Veilstone Mines must be built on a free, uncursed veilstone outcropping",
            "Alanthor_TradingOutpost" => "Trading Outposts must be built on a free, uncursed veilstone outcrop",
            _                  => "This building must stand on a free resource node",
        };
    }
}
