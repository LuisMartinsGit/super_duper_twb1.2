// AIWallCorridor.cs
// The home wall ring as RESERVED GROUND, and the router that lays a curtain
// round whatever already stands on it (2026-10-04).
//
// The bug this exists for (SunderedCrown headless batch, Blue): the home ring
// was left open on the west side because Barracks, Archery Ranges and a Vault
// had been placed on the line the ring runs along. The only keep-out was the
// border band (AIWallPlanner.FootprintClearOfBorder): a building's EDGE kept
// buildingBorderClearance (5 m) from foreign ground, while the wall's centre
// line runs borderInset (4 m) inside it -- so a building could legally stand
// one metre from the wall's centre line, which is inside the wall. Once the
// plan existed the placer only tested the ring's bounding box. And the link
// detour was one quadratic bulge, which cannot wind past two buildings: four
// links were "blocked straight and round both sides; left open".
//
// Now:
//   * the ring is planned as soon as the home territory is held (Age 0
//     included) by the same deterministic trace the wall doctrine uses, and
//     every cell within the wall's half-depth + corridorClearanceCells of it
//     is reserved -- every AI placer refuses a footprint on it;
//   * a base building must stand INSIDE the ring polygon (extractors and
//     claims excepted);
//   * a link that is blocked anyway is routed round the blocker by a grid
//     search inside own ground (TryRoute).
// Pure functions of replicated state (region map, ownership, terrain, the
// brain's plan buffer); the frame stamp only gates the cache.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Data;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Navigation;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.AI
{
    public static class AIWallCorridor
    {
        static AIWallPlannerConfig Cfg => AIWallPlannerConfig.I;

        #region Cached queries

        static readonly ComponentType[] QT_HallTagFactionTagLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagLocalTransform;

        static readonly ComponentType[] QT_AIBrainFactionTag =
        {
            ComponentType.ReadOnly<AIBrain>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_AIBrainFactionTag;

        static readonly ComponentType[] QT_BuildingTagLocalTransformBuildingSize =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<BuildingSize>(),
            ComponentType.Exclude<WallInstanceTag>(),
            ComponentType.Exclude<WallHubTag>(),
            ComponentType.Exclude<WallSegmentTag>(),
        };
        static CachedEntityQuery QC_BuildingTagLocalTransformBuildingSize;

        #endregion

        // ── The ring, per faction ─────────────────────────────────────────

        sealed class Ring
        {
            public int Frame = -1;
            public uint Key;
            public bool Has;
            public int2 Origin;          // build cell of Cells[0]
            public int W, H;
            public bool[] Cells = System.Array.Empty<bool>();
            /// <summary>The wall's OWN cells: within its half-depth (hub
            /// radius at a hub) of the line, no walkway. A subset of Cells.</summary>
            public bool[] Core = System.Array.Empty<bool>();
            public int CellCount;
            public float2[] Poly = System.Array.Empty<float2>();
        }

        static readonly Dictionary<int, Ring> _rings = new Dictionary<int, Ring>();
        static readonly List<float2> _segA = new List<float2>(64);
        static readonly List<float2> _segB = new List<float2>(64);
        static readonly List<float2> _poly = new List<float2>(64);

        /// <summary>The home capital (lowest NetworkId Hall the faction owns —
        /// the same rule the endgame's home anchor uses) and its culture.</summary>
        public static bool TryGetHome(EntityManager em, Faction faction, out float3 pos, out byte culture)
        {
            pos = default; culture = Cultures.None;
            var q = QC_HallTagFactionTagLocalTransform.Get(em, QT_HallTagFactionTagLocalTransform);
            using var ents = q.ToEntityArray(Allocator.Temp);
            long best = long.MaxValue;
            bool found = false;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                long nid = em.HasComponent<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(ents[i])
                    ? em.GetComponentData<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(ents[i]).NetworkId
                    : long.MaxValue - 1;
                if (found && nid >= best) continue;
                best = nid;
                found = true;
                pos = em.GetComponentData<LocalTransform>(ents[i]).Position;
                culture = em.HasComponent<FactionProgress>(ents[i])
                    ? em.GetComponentData<FactionProgress>(ents[i]).Culture : Cultures.None;
            }
            return found;
        }

        static Entity FindBrain(EntityManager em, Faction faction)
        {
            var q = QC_AIBrainFactionTag.Get(em, QT_AIBrainFactionTag);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (facs[i].Value == faction) return ents[i];
            return Entity.Null;
        }

        static uint Mix(uint h, int v) => (h ^ (uint)v) * 16777619u;

        /// <summary>The faction's ring, refreshed at most once per frame and
        /// rebuilt only when its inputs change.</summary>
        static Ring Get(EntityManager em, Faction faction)
        {
            int f = (int)faction;
            if (!_rings.TryGetValue(f, out var ring)) { ring = new Ring(); _rings[f] = ring; }
            int frame = UnityEngine.Time.frameCount;
            if (ring.Frame == frame) return ring;
            ring.Frame = frame;

            if (!TryGetHome(em, faction, out float3 home, out byte culture))
            {
                Clear(ring, 0u);
                return ring;
            }

            // Source 1: the frozen plan on the brain.
            Entity brain = FindBrain(em, faction);
            if (brain != Entity.Null && em.HasComponent<AIWallPlan>(brain)
                && em.HasBuffer<AIWallPlanSlot>(brain))
            {
                byte mode = em.GetComponentData<AIWallPlan>(brain).Mode;
                var buf = em.GetBuffer<AIWallPlanSlot>(brain, true);
                uint key = Mix(2166136261u, 1);
                key = Mix(key, mode);
                for (int i = 0; i < buf.Length; i++)
                {
                    key = Mix(key, (int)math.round(buf[i].Position.x * 4f));
                    key = Mix(key, (int)math.round(buf[i].Position.z * 4f));
                    key = Mix(key, buf[i].Chain);
                    key = Mix(key, buf[i].Flags & (AIWallPlanner.FlagDead | AIWallPlanner.FlagTerrainSealed));
                }
                if (key == ring.Key) return ring;
                var slots = buf.ToNativeArray(Allocator.Temp);
                Build(em, faction, ring, key, slots, mode, "plan");
                slots.Dispose();
                return ring;
            }

            // Source 2: the ring the doctrine WILL draw. Only a culture that
            // walls (Alanthor) or one still to choose (Age 0) reserves it.
            if (culture != Cultures.None && culture != Cultures.Alanthor) { Clear(ring, 3u); return ring; }
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) { Clear(ring, 0u); return ring; }

            uint sig = AIWallPlanner.WallTerritorySignature(em, faction, home);
            uint pkey = Mix(Mix(Mix(Mix(2166136261u, 2), (int)sig),
                (int)math.floor(home.x)), (int)math.floor(home.z));
            if (pkey == ring.Key) return ring;

            var planned = new NativeList<AIWallPlanSlot>(Allocator.Temp);
            byte pmode = AIWallPlanner.BuildPlan(em, faction, home, planned, out _);
            if (pmode == AIWallPlanner.ModeBorder && planned.Length >= 2)
            {
                var arr = planned.AsArray();
                Build(em, faction, ring, pkey, arr, pmode, "pre-plan");
            }
            else Clear(ring, pkey);
            planned.Dispose();
            return ring;
        }

        static void Clear(Ring ring, uint key)
        {
            ring.Key = key;
            ring.Has = false;
            ring.CellCount = 0;
            ring.Poly = System.Array.Empty<float2>();
        }

        static void Build(EntityManager em, Faction faction, Ring ring, uint key,
            NativeArray<AIWallPlanSlot> slots, byte mode, string source)
        {
            Clear(ring, key);
            bool cyclic = mode == AIWallPlanner.ModeBorder || mode == AIWallPlanner.ModePerimeter;
            _segA.Clear(); _segB.Clear(); _poly.Clear();

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Chain == slots[0].Chain && cyclic)
                    _poly.Add(slots[i].Position.xz);
                if ((slots[i].Flags & AIWallPlanner.FlagDead) != 0) continue;
                // The hub itself is reserved as a zero-length segment.
                _segA.Add(slots[i].Position.xz); _segB.Add(slots[i].Position.xz);
                if ((slots[i].Flags & AIWallPlanner.FlagTerrainSealed) != 0) continue;
                int j = NextLive(slots, i, cyclic);
                if (j < 0 || SealedBetween(slots, i, j)) continue;
                _segA.Add(slots[i].Position.xz); _segB.Add(slots[j].Position.xz);
            }
            if (_poly.Count >= 3) ring.Poly = _poly.ToArray();
            if (_segA.Count == 0) return;

            float halfDepth = AlanthorWall.DepthOf(false) * 0.5f;
            float R = math.max(halfDepth, AlanthorWall.HubRadius)
                    + math.max(0, Cfg.corridorClearanceCells) * BuildGrid.CellSize;
            float reach = R + BuildGrid.HalfCell;
            float coreReach = math.max(halfDepth, AlanthorWall.HubRadius) + BuildGrid.HalfCell;

            float2 mn = new float2(float.MaxValue), mx = new float2(float.MinValue);
            for (int s = 0; s < _segA.Count; s++)
            {
                mn = math.min(mn, math.min(_segA[s], _segB[s]));
                mx = math.max(mx, math.max(_segA[s], _segB[s]));
            }
            int2 c0 = BuildGrid.WorldToCell(new float3(mn.x - reach - BuildGrid.CellSize, 0f, mn.y - reach - BuildGrid.CellSize));
            int2 c1 = BuildGrid.WorldToCell(new float3(mx.x + reach + BuildGrid.CellSize, 0f, mx.y + reach + BuildGrid.CellSize));
            int W = c1.x - c0.x + 1, H = c1.y - c0.y + 1;
            if (W <= 0 || H <= 0 || (long)W * H > 1_000_000) return;

            var cells = new bool[W * H];
            var core = new bool[W * H];
            int count = 0, coreCount = 0;
            float reach2 = reach * reach;
            float coreReach2 = coreReach * coreReach;
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    float2 p = BuildGrid.CellCentre(new int2(c0.x + x, c0.y + z));
                    float best = float.MaxValue;
                    for (int s = 0; s < _segA.Count; s++)
                    {
                        best = math.min(best, DistSq(p, _segA[s], _segB[s]));
                        if (best <= coreReach2) break;
                    }
                    if (best > reach2) continue;
                    cells[z * W + x] = true;
                    count++;
                    if (best <= coreReach2) { core[z * W + x] = true; coreCount++; }
                }
            ring.Origin = c0; ring.W = W; ring.H = H;
            ring.Cells = cells; ring.Core = core; ring.CellCount = count; ring.Has = count > 0;
            AILogger.Log(faction, "WALL",
                $"corridor reserved ({count} cells, {coreCount} of them the wall's own; {source}, " +
                $"{_segA.Count} pieces, {R:F1} m each side of the line)");
        }

        static int NextLive(NativeArray<AIWallPlanSlot> slots, int i, bool cyclic)
        {
            byte chain = slots[i].Chain;
            for (int k = i + 1; k < slots.Length; k++)
            {
                if (slots[k].Chain != chain) break;
                if ((slots[k].Flags & AIWallPlanner.FlagDead) != 0) continue;
                return k;
            }
            if (!cyclic) return -1;
            for (int k = 0; k < i; k++)
            {
                if (slots[k].Chain != chain) continue;
                if ((slots[k].Flags & AIWallPlanner.FlagDead) != 0) continue;
                return k;
            }
            return -1;
        }

        static bool SealedBetween(NativeArray<AIWallPlanSlot> slots, int i, int j)
        {
            int n = slots.Length;
            for (int k = (i + 1) % n, guard = 0; k != j && guard < n; k = (k + 1) % n, guard++)
                if ((slots[k].Flags & AIWallPlanner.FlagTerrainSealed) != 0) return true;
            return false;
        }

        static float DistSq(float2 p, float2 a, float2 b)
        {
            float2 ab = b - a;
            float l2 = math.lengthsq(ab);
            float t = l2 > 1e-8f ? math.saturate(math.dot(p - a, ab) / l2) : 0f;
            return math.distancesq(p, a + ab * t);
        }

        static bool InPoly(float2[] poly, float2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                float2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y)
                    && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        // ── Placement API ─────────────────────────────────────────────────

        /// <summary>
        /// True when no build cell of the footprint lies on the faction's
        /// reserved wall corridor (or there is no ring).
        ///
        /// A SMALL FOOTPRINT (2026-10-04, Game_AI.md § Walls) — at most
        /// corridorSmallFootprintCells build cells, a Hut or a Watch Tower —
        /// is tested against the wall's OWN cells only: it can stand in the
        /// walkway band and the curtain router winds past it.
        /// <paramref name="coreOnly"/> asks the same for any footprint (the
        /// lost-sole-trainer rebuild, which outranks the walkway).
        /// </summary>
        public static bool FootprintClear(EntityManager em, Faction faction, float3 centre, int2 size,
            bool coreOnly = false)
        {
            var ring = Get(em, faction);
            if (!ring.Has) return true;
            if (!coreOnly) coreOnly = IsSmallFootprint(size);
            var test = coreOnly ? ring.Core : ring.Cells;
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            int2 a = BuildGrid.WorldToCell(new float3(centre.x - hx + 0.01f, 0f, centre.z - hz + 0.01f));
            int2 b = BuildGrid.WorldToCell(new float3(centre.x + hx - 0.01f, 0f, centre.z + hz - 0.01f));
            for (int z = a.y; z <= b.y; z++)
                for (int x = a.x; x <= b.x; x++)
                {
                    int lx = x - ring.Origin.x, lz = z - ring.Origin.y;
                    if (lx < 0 || lz < 0 || lx >= ring.W || lz >= ring.H) continue;
                    if (test[lz * ring.W + lx]) return false;
                }
            return true;
        }

        /// <summary>A footprint of at most corridorSmallFootprintCells 2 m
        /// build cells (0 = no exemption).</summary>
        public static bool IsSmallFootprint(int2 sizeMetres)
        {
            int max = Cfg.corridorSmallFootprintCells;
            if (max <= 0) return false;
            int2 c = BuildingSizeConfig.ToCells(sizeMetres);
            return c.x * c.y <= max;
        }

        /// <summary>
        /// <see cref="FootprintClear"/> for the faction that HOLDS the ground
        /// under <paramref name="centre"/> — for placers that do not carry a
        /// faction (the endgame ring scans, the tower scan). Unowned ground,
        /// or a map with no partition, reserves nothing.
        /// </summary>
        public static bool FootprintClearForOwner(EntityManager em, float3 centre, int2 size)
        {
            if (!RegionMap.Ready || !TerritoryOwnership.Ready) return true;
            int t = RegionMap.RegionAt(centre.x, centre.z);
            if (t == RegionMap.None) return true;
            int owner = TerritoryOwnership.OwnerOf(t);
            if (owner < 0) return true;
            return FootprintClear(em, (Faction)owner, centre, size);
        }

        /// <summary>True when <paramref name="p"/> lies inside the faction's
        /// home ring (false when no ring is known).</summary>
        public static bool InsideRing(EntityManager em, Faction faction, float3 p)
        {
            var ring = Get(em, faction);
            return ring.Poly.Length >= 3 && InPoly(ring.Poly, p.xz);
        }

        /// <summary>True when all four footprint corners lie inside the ring
        /// (true when no ring is known).</summary>
        public static bool FootprintInsideRing(EntityManager em, Faction faction, float3 c, int2 size)
        {
            var ring = Get(em, faction);
            if (ring.Poly.Length < 3) return true;
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            return InPoly(ring.Poly, new float2(c.x - hx, c.z - hz))
                && InPoly(ring.Poly, new float2(c.x + hx, c.z - hz))
                && InPoly(ring.Poly, new float2(c.x - hx, c.z + hz))
                && InPoly(ring.Poly, new float2(c.x + hx, c.z + hz));
        }

        static readonly Dictionary<long, float> _nextRejectLog = new Dictionary<long, float>();

        /// <summary>"BUILD: placement rejected — on wall corridor", throttled
        /// per faction and building to once per corridorLogInterval. Called
        /// only when the whole site search FAILED (2026-10-04): it used to be
        /// written on success too, so the line could not say whether the
        /// corridor had cost a building anything.</summary>
        public static void NoteRejected(Faction faction, string buildingId, int candidates)
        {
            if (candidates <= 0 || !AILogger.Enabled) return;
            long k = ((long)(int)faction << 32) ^ (uint)(buildingId ?? "").GetHashCode();
            float now = SimClock.Now;
            if (_nextRejectLog.TryGetValue(k, out float next) && now < next && now >= next - Cfg.corridorLogInterval)
                return;
            _nextRejectLog[k] = now + Cfg.corridorLogInterval;
            AILogger.Log(faction, "BUILD",
                $"placement rejected — on wall corridor ({buildingId}, {candidates} candidate(s); search failed)");
        }

        // ── Routing a curtain round a blocker ─────────────────────────────

        /// <summary>The building nearest <paramref name="pa"/> whose
        /// footprint (grown by the wall's half-depth) the straight run
        /// pa -> pb crosses. Walls excluded.</summary>
        public static bool TryFindBlocker(EntityManager em, float3 pa, float3 pb,
            out string id, out float3 pos)
        {
            id = null; pos = default;
            float grow = AlanthorWall.DepthOf(false) * 0.5f;
            var q = QC_BuildingTagLocalTransformBuildingSize.Get(em, QT_BuildingTagLocalTransformBuildingSize);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var sizes = q.ToComponentDataArray<BuildingSize>(Allocator.Temp);
            float bestT = float.MaxValue;
            float2 a = pa.xz, d = pb.xz - pa.xz;
            for (int i = 0; i < ents.Length; i++)
            {
                float2 c = xfs[i].Position.xz;
                float2 h = new float2(sizes[i].Width * 0.5f + grow, sizes[i].Height * 0.5f + grow);
                float2 mn = c - h, mx = c + h;
                float t0 = 0f, t1 = 1f;
                bool hit = true;
                for (int ax = 0; ax < 2 && hit; ax++)
                {
                    float o = ax == 0 ? a.x : a.y, dd = ax == 0 ? d.x : d.y;
                    float lo = ax == 0 ? mn.x : mn.y, hi = ax == 0 ? mx.x : mx.y;
                    if (math.abs(dd) < 1e-6f) { if (o < lo || o > hi) hit = false; continue; }
                    float ta = (lo - o) / dd, tb = (hi - o) / dd;
                    if (ta > tb) { float tmp = ta; ta = tb; tb = tmp; }
                    t0 = math.max(t0, ta); t1 = math.min(t1, tb);
                    if (t0 > t1) hit = false;
                }
                if (!hit || t0 >= bestT) continue;
                bestT = t0;
                pos = xfs[i].Position;
                id = BuildCosts.IdFromEntity(em, ents[i]);
                if (string.IsNullOrEmpty(id)) id = "building";
            }
            return id != null;
        }

        struct HeapItem { public float F; public int Idx; }

        static bool Less(HeapItem x, HeapItem y) => x.F < y.F || (x.F == y.F && x.Idx < y.Idx);

        static void Push(List<HeapItem> h, HeapItem it)
        {
            h.Add(it);
            int i = h.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) / 2;
                if (!Less(h[i], h[p])) break;
                (h[i], h[p]) = (h[p], h[i]);
                i = p;
            }
        }

        static HeapItem Pop(List<HeapItem> h)
        {
            var top = h[0];
            int last = h.Count - 1;
            h[0] = h[last];
            h.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = 2 * i + 1, r = l + 1, m = i;
                if (l < h.Count && Less(h[l], h[m])) m = l;
                if (r < h.Count && Less(h[r], h[m])) m = r;
                if (m == i) break;
                (h[i], h[m]) = (h[m], h[i]);
                i = m;
            }
            return top;
        }

        static readonly int[] Dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] Dz = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>
        /// A wall line from hub <paramref name="pa"/> to hub <paramref name="pb"/>
        /// that the executor will accept — on own ground the whole way and
        /// clear of every building, node, plan and cliff — found by a grid
        /// search (8-connected, fixed neighbour order and index tie-break, so
        /// deterministic) in a box round the two hubs, then string-pulled with
        /// the executor's own line tests. Fills <paramref name="pts"/> (pa
        /// first, pb last) and returns true when such a line exists and is no
        /// longer than <paramref name="maxLength"/>.
        /// </summary>
        public static bool TryRoute(EntityManager em, Faction faction, float3 pa, float3 pb,
            float maxLength, List<float3> pts)
        {
            pts.Clear();
            float cs = math.max(0.5f, Cfg.wallRerouteCellSize);
            float margin = math.max(4f, Cfg.wallRerouteMargin);
            float2 mn = math.min(pa.xz, pb.xz) - margin, mx = math.max(pa.xz, pb.xz) + margin;
            int W = (int)math.ceil((mx.x - mn.x) / cs) + 1, H = (int)math.ceil((mx.y - mn.y) / cs) + 1;
            if ((long)W * H > 60000) return false;
            int N = W * H;

            float jr = CommandRouter.WallJunctionClearance;
            float h = AlanthorWall.DepthOf(false) * 0.5f + 0.25f;
            var blocked = new bool[N];
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    var p = new float3(mn.x + x * cs, 0f, mn.y + z * cs);
                    if (math.distancesq(p.xz, pa.xz) < jr * jr || math.distancesq(p.xz, pb.xz) < jr * jr)
                        continue;
                    if (!CommandRouter.WallPointOnOwnGround(em, faction, p)
                        || NavGridQuery.WallBlockedAt(p)
                        || NavGridQuery.WallBlockedAt(p + new float3(h, 0f, 0f))
                        || NavGridQuery.WallBlockedAt(p + new float3(-h, 0f, 0f))
                        || NavGridQuery.WallBlockedAt(p + new float3(0f, 0f, h))
                        || NavGridQuery.WallBlockedAt(p + new float3(0f, 0f, -h)))
                        blocked[z * W + x] = true;
                }

            int2 s = new int2((int)math.round((pa.x - mn.x) / cs), (int)math.round((pa.z - mn.y) / cs));
            int2 g = new int2((int)math.round((pb.x - mn.x) / cs), (int)math.round((pb.z - mn.y) / cs));
            int si = s.y * W + s.x, gi = g.y * W + g.x;
            blocked[si] = false; blocked[gi] = false;

            var gc = new float[N];
            var parent = new int[N];
            var closed = new bool[N];
            for (int i = 0; i < N; i++) { gc[i] = float.MaxValue; parent[i] = -1; }
            var heap = new List<HeapItem>(256);
            gc[si] = 0f;
            Push(heap, new HeapItem { F = Octile(s, g), Idx = si });
            bool found = false;
            while (heap.Count > 0)
            {
                var it = Pop(heap);
                if (closed[it.Idx]) continue;
                closed[it.Idx] = true;
                if (it.Idx == gi) { found = true; break; }
                int cx = it.Idx % W, cz = it.Idx / W;
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + Dx[d], nz = cz + Dz[d];
                    if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                    int ni = nz * W + nx;
                    if (blocked[ni] || closed[ni]) continue;
                    // No corner cutting past a blocked cell.
                    if (d >= 4 && (blocked[cz * W + nx] || blocked[nz * W + cx])) continue;
                    float ng = gc[it.Idx] + (d >= 4 ? 1.41421356f : 1f);
                    if (ng >= gc[ni]) continue;
                    gc[ni] = ng;
                    parent[ni] = it.Idx;
                    Push(heap, new HeapItem { F = ng + Octile(new int2(nx, nz), g), Idx = ni });
                }
            }
            if (!found) return false;

            var raw = new List<float3>(128);
            for (int c = gi; c >= 0; c = parent[c])
                raw.Add(new float3(mn.x + (c % W) * cs, 0f, mn.y + (c / W) * cs));
            raw.Reverse();
            raw[0] = pa; raw[raw.Count - 1] = pb;

            // String-pull with the executor's own tests.
            var joints = new[] { pa, pb };
            var seg = new float3[2];
            pts.Add(pa);
            int anchor = 0;
            for (int k = 1; k < raw.Count - 1; k++)
            {
                seg[0] = raw[anchor]; seg[1] = raw[k + 1];
                if (CommandRouter.WallLineOnOwnGround(em, faction, seg)
                    && CommandRouter.WallLineClear(em, faction, seg, joints, palisade: false))
                    continue;
                pts.Add(raw[k]);
                anchor = k;
            }
            pts.Add(pb);
            for (int k = 1; k < pts.Count - 1; k++)
            {
                var p = pts[k];
                p.y = TerrainUtility.GetHeight(p.x, p.z);
                pts[k] = p;
            }

            if (CommandRouter.PolylineLength(pts) > maxLength
                || !CommandRouter.WallLineOnOwnGround(em, faction, pts)
                || !CommandRouter.WallLineClear(em, faction, pts, joints, palisade: false))
            {
                pts.Clear();
                return false;
            }
            return true;
        }

        static float Octile(int2 a, int2 b)
        {
            int dx = math.abs(a.x - b.x), dz = math.abs(a.y - b.y);
            return math.max(dx, dz) + 0.41421356f * math.min(dx, dz);
        }
    }
}
