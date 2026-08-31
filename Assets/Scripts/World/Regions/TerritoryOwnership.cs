// TerritoryOwnership.cs
// Who holds each territory.
//
// docs/Design/Regions.md §2: a territory is claimed by BUILDING your culture's
// claim structure inside it, not by dominating it on the influence map.
//
//   Alanthor   a fortification   -> Alanthor_Tower   (WatchTowerTag)
//   Runai      a trade post      -> Runai_TradingPost (TradingPostTag)
//   Feraldis   a totem           -> Feraldis_WarTotem (WarTotemTag)
//
// Ownership is DERIVED, never stored as an authoritative fact: it is recomputed
// from the claim structures that are alive right now. That is what makes the
// two design rules fall out for free rather than needing their own bookkeeping:
//
//   * "a claim decays back to Natural when its structure dies" -- the structure
//     stops existing, so the next recompute finds nothing and the territory is
//     unowned. No timer, no ownership record to clean up, and no way for a
//     territory to stay claimed by a faction that has nothing there.
//   * "you cannot claim over a live claim" -- enforced at BUILD time
//     (see CanClaim), not here.
//
// Deriving also means a territory is never owned by a player who has been wiped
// out, which a stored owner would have to be told about.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.World.Regions
{
    public static class TerritoryOwnership
    {
        /// <summary>Unowned. Natural ground -- claimable by anyone.</summary>
        public const int Natural = -1;

        /// <summary>Held by the curse (Regions.md §3 -- taken by wave, not built).</summary>
        public const int Curse = -2;

        private static int[] _owner = System.Array.Empty<int>();

        /// <summary>
        /// Territories the curse holds (Regions.md §3, 2026-08-31). Fed by
        /// CurseTerritorySystem — a territory is here while a live well or a
        /// live curse anchor stands in it, and leaves the moment the anchor
        /// dies. Stamped into <see cref="_owner"/> on every Recompute, AFTER
        /// the Halls: the curse never conquers Hall ground, so a conflict
        /// here is a state the rules already exclude and the Hall wins it.
        /// </summary>
        private static readonly HashSet<int> _curseHeld = new HashSet<int>();

        public static void MarkCurseHeld(int territory, bool held)
        {
            if (held) _curseHeld.Add(territory);
            else _curseHeld.Remove(territory);
        }

        public static bool IsCurseHeld(int territory) => _curseHeld.Contains(territory);

        public static int CurseHeldCount => _curseHeld.Count;

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
            _curseHeld.Clear();
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
        public static bool IsClaimStructure(string buildingId) => buildingId == "Hall";

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
        {
            // Scenarios and the sandbox are fixtures, not matches — they build
            // their board wherever the author or the tester points. Same carve
            // out VictoryConditionSystem makes for the same reason.
            if (GameSettings.IsSandbox || GameSettings.Mode == GameMode.Scenario) return true;
            if (!RegionMap.Ready) return true;

            // Ownership is recomputed on TerritoryIncomeSystem's 5 s tick, so
            // for the first few seconds of a match nothing is owned yet. Derive
            // it once here rather than letting that window be a free-for-all.
            if (!Ready) Recompute(em);
            if (!Ready) return true;

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
            if (t == RegionMap.None) return true;

            int owner = OwnerOf(t);
            if (owner == (int)faction) return true;
            return IsClaimStructure(buildingId) && owner == Natural;
        }

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

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            bool found = false;
            for (int i = 0; i < ents.Length && !found; i++)
            {
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                found = RegionMap.RegionAt(p.x, p.z) == here;
            }
            ents.Dispose();
            q.Dispose();
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
            "Alanthor_Smelter" => (ComponentType?)ComponentType.ReadOnly<VeilsteelDepositTag>(),
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
            "Alanthor_Smelter" => (ComponentType?)ComponentType.ReadOnly<SmelterTag>(),
            _ => null,
        };

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
        /// Answers TRUE on a map with no nodes of that kind at all, so an
        /// unseeded scene is merely unbalanced rather than unplayable.
        /// </summary>
        public static bool OnFreeNodeFor(EntityManager em, string buildingId,
                                         float worldX, float worldZ)
        {
            var required = RequiredNodeFor(buildingId);
            if (required == null) return true;   // not an extractor — no node rule

            var nodeQuery = em.CreateEntityQuery(
                required.Value,
                ComponentType.ReadOnly<LocalTransform>());
            var nodes = nodeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            if (nodes.Length == 0)
            {
                nodes.Dispose();
                nodeQuery.Dispose();
                return true;
            }

            float r2 = SupplyNodeSnapRange * SupplyNodeSnapRange;
            bool ok = false;
            for (int i = 0; i < nodes.Length && !ok; i++)
            {
                var np = em.GetComponentData<LocalTransform>(nodes[i]).Position;
                float dx = np.x - worldX, dz = np.z - worldZ;
                if (dx * dx + dz * dz > r2) continue;
                ok = !ExtractorOn(em, buildingId, np.x, np.z);
            }
            nodes.Dispose();
            nodeQuery.Dispose();
            return ok;
        }

        /// <summary>Is an extractor of this kind already standing on the node?</summary>
        private static bool ExtractorOn(EntityManager em, string buildingId, float x, float z)
        {
            var tag = ExtractorTagFor(buildingId);
            if (tag == null) return false;

            var q = em.CreateEntityQuery(
                tag.Value,
                ComponentType.ReadOnly<LocalTransform>());
            var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            float r2 = SupplyNodeSnapRange * SupplyNodeSnapRange;
            bool found = false;
            for (int i = 0; i < ents.Length && !found; i++)
            {
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                float dx = p.x - x, dz = p.z - z;
                found = dx * dx + dz * dz <= r2;
            }
            ents.Dispose();
            q.Dispose();
            return found;
        }

        public static bool OnFreeSupplyNode(EntityManager em, float worldX, float worldZ)
        {
            var nodeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<SupplyNodeTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var nodes = nodeQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            if (nodes.Length == 0)
            {
                nodes.Dispose();
                nodeQuery.Dispose();
                return true;   // unseeded map — do not make the hut unbuildable
            }

            float r2 = SupplyNodeSnapRange * SupplyNodeSnapRange;
            bool ok = false;
            for (int i = 0; i < nodes.Length && !ok; i++)
            {
                var np = em.GetComponentData<LocalTransform>(nodes[i]).Position;
                float dx = np.x - worldX, dz = np.z - worldZ;
                if (dx * dx + dz * dz > r2) continue;
                ok = !HutOn(em, np.x, np.z);   // the node has to be free
            }
            nodes.Dispose();
            nodeQuery.Dispose();
            return ok;
        }

        /// <summary>Is a Gatherer's Hut already standing on this node?</summary>
        private static bool HutOn(EntityManager em, float x, float z)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            float r2 = SupplyNodeSnapRange * SupplyNodeSnapRange;
            bool found = false;
            for (int i = 0; i < ents.Length && !found; i++)
            {
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                float dx = p.x - x, dz = p.z - z;
                found = dx * dx + dz * dz <= r2;
            }
            ents.Dispose();
            q.Dispose();
            return found;
        }

        /// <summary>
        /// Rebuild ownership from the claim structures currently alive.
        ///
        /// Cheap enough to run on a timer: it is one pass over the claim
        /// structures (a handful per player), not over the territories or the
        /// map. Territories with no structure in them are left Natural, which is
        /// why a destroyed claim reverts with no extra work.
        /// </summary>
        public static void Recompute(EntityManager em)
        {
            int count = RegionMap.Count;
            if (count == 0) { Reset(); return; }

            if (_owner.Length != count) _owner = new int[count];
            for (int i = 0; i < count; i++) _owner[i] = Natural;

            // The HALL claims its own ground, and it must be stamped FIRST.
            //
            // Every culture claim structure below is an AGE 1 building. Derived
            // purely from those, nobody would own anything for the whole of Age
            // 0 -- and with income coming from territory (Regions.md §4) that is
            // not "no territory bonus", it is NO ECONOMY AT ALL for the entire
            // opening, in the exact age the Gatherer's Hut belongs to.
            //
            // Regions.md §2 already says the answer: "you begin holding the
            // region your start sits in", granted rather than claimed. The Hall
            // is what marks that ground, so the Hall carries the grant. It keeps
            // working after age-up, where an extra Hall is an expensive and
            // legitimate way to hold ground, and it fails the right way -- lose
            // every Hall in a territory and it reverts like any other claim.
            // THE ONLY CLAIM (Regions.md §2, 2026-08-28). One rule for every
            // culture, and it works from Age 0 because the Hall is an Age 0
            // building — which is what lets the claim game start in the opening
            // instead of waiting on an age-up.
            Claim<HallTag>(em);

            // The curse's holdings (Regions.md §3, 2026-08-31). Stamped after
            // the Halls so a Hall on contested ground always wins — the curse
            // never conquers Hall ground in the first place, so this is a
            // tie-break for an excluded state, same as the Hall-vs-Hall one.
            foreach (int t in _curseHeld)
                if (t >= 0 && t < _owner.Length && _owner[t] == Natural)
                    _owner[t] = Curse;

            // Version bump only on a REAL change, so everything gated on it
            // (the border ribbon, the ground mask, the rasterized ownership
            // grid) stays untouched across the no-op recomputes that run on
            // the income tick.
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

        /// <summary>
        /// Stamp every territory containing a live structure of this type.
        ///
        /// Under construction counts as NOT claimed: a foundation is not a
        /// fortification, and letting it claim would mean a player could take
        /// ground by starting a building they never finish.
        /// </summary>
        private static void Claim<T>(EntityManager em) where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;

                var p = em.GetComponentData<LocalTransform>(e).Position;
                // NearestRegion, not RegionAt. RegionAt answers None on ground
                // no region can own (outside the 4-24 m claimable band), and a
                // structure that files nowhere claims nothing — for the HALL
                // that is a soft-lock, because the build gate then finds the
                // player owns no territory at all and refuses every placement.
                // A building that exists stands in the region it is nearest to.
                int t = RegionMap.NearestRegion(p.x, p.z);
                if (t == RegionMap.None) continue;

                // First claim wins. Two factions holding live structures in one
                // territory should be impossible (CanClaim forbids building the
                // second), so this is a tie-break for a state the rules already
                // exclude rather than a meaningful contest.
                if (_owner[t] == Natural)
                    _owner[t] = (int)em.GetComponentData<FactionTag>(e).Value;
            }
            ents.Dispose();
            q.Dispose();
        }
    }
}
