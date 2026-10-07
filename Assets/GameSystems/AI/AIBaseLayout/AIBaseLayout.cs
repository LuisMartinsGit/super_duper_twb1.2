// AIBaseLayout.cs
// Two pieces of base-layout knowledge every AI placer shares (2026-10-04,
// docs/Design/Game_AI.md 6b + 5g):
//
//   1. THE SELF-LOCK CHECK. Buildings may now sit flush (Build_Grid.md § 2,
//      "Buildings may touch"), so the edge-to-edge lane that used to keep an
//      AI base walkable is gone as a hard rule. What replaces it is a check of
//      the thing the lane was standing in for: after a candidate placement,
//      can the base still be walked? A bounded flood fill on the 2 m build
//      grid, in a square window around the territory's core (its capital or
//      Fortress, else its seed), must still reach everything it reached before:
//        * every building's last free side, and a production building's EXIT
//          (its trained units walk out of its east side — TrainingSystem);
//        * every gate cell (a ring the army cannot leave is a bug);
//        * the way out — the window's edge or another territory;
//        * every open cell, give or take sealPocketToleranceCells (no
//          enclosed pockets);
//      and the candidate itself must keep a free side (and, as a production
//      building, its exit). A FLUSH cluster — footprints touching edge to
//      edge — may hold at most maxFlushClusterBuildings before the next one
//      must leave a lane (Houses in their quarter exempt).
//      Cheap by construction: the "before" picture is built once per
//      (faction, territory) and reused until the building / plan set or the
//      territory map changes (or sealCacheSeconds pass); each check is ONE
//      flood of at most (2 x sealWindowHalfCells + 1)^2 cells, run only on a
//      candidate that already passed every other rule.
//
//   2. THE RESERVED FORTRESS SPOT. From the moment a non-home territory is
//      held, one legal Fortress footprint in it is reserved (SimpleAISystem
//      .Territories). Every AI placer refuses a footprint on it (plus
//      fortressSpotMarginCells), exactly as the wall corridor is kept clear,
//      so the Fortress — step 4 of the territory's build order — always has
//      somewhere to stand.
//
// Host-side decision helper: reads replicated state only (the per-tick
// BuildSiteSnapshot, the nav cost field, the region map); everything it
// decides reaches the simulation only through CommandRouter. Single-threaded
// (the AI think loop and the endgame systems run on the main thread), so the
// scratch arrays are static.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Systems.Navigation;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.AI
{
    public static class AIBaseLayout
    {
        static SimpleAISystemConfig Cfg => SimpleAISystemConfig.I;

        static int _epoch = int.MinValue;

        static void EnsureEpoch()
        {
            if (_epoch == SimCadence.Epoch) return;
            _epoch = SimCadence.Epoch;
            _spots.Clear();
            _windows.Clear();
            _nextSealLog.Clear();
        }

        // ═════════════════════════════════════════════════════════════════
        // 2. RESERVED FORTRESS SPOTS
        // ═════════════════════════════════════════════════════════════════

        struct Spot
        {
            public int Faction, Region;
            public float3 Pos;
            public float2 Min, Max;     // footprint + margin
        }

        static readonly List<Spot> _spots = new List<Spot>();

        /// <summary>The faction's reserved Fortress spot in <paramref name="region"/>.</summary>
        public static bool TryGetFortressSpot(Faction faction, int region, out float3 pos)
        {
            EnsureEpoch();
            for (int i = 0; i < _spots.Count; i++)
                if (_spots[i].Faction == (int)faction && _spots[i].Region == region)
                {
                    pos = _spots[i].Pos;
                    return true;
                }
            pos = default;
            return false;
        }

        /// <summary>Reserve (or move) the faction's Fortress spot in a territory.</summary>
        public static void SetFortressSpot(Faction faction, int region, float3 pos, int2 size)
        {
            EnsureEpoch();
            ClearFortressSpot(faction, region);
            float m = math.max(0, Cfg.fortressSpotMarginCells) * BuildGrid.CellSize;
            _spots.Add(new Spot
            {
                Faction = (int)faction, Region = region, Pos = pos,
                Min = new float2(pos.x - size.x * 0.5f - m, pos.z - size.y * 0.5f - m),
                Max = new float2(pos.x + size.x * 0.5f + m, pos.z + size.y * 0.5f + m),
            });
        }

        public static void ClearFortressSpot(Faction faction, int region)
        {
            EnsureEpoch();
            for (int i = _spots.Count - 1; i >= 0; i--)
                if (_spots[i].Faction == (int)faction && _spots[i].Region == region)
                    _spots.RemoveAt(i);
        }

        /// <summary>
        /// True when the footprint touches no reserved Fortress spot — any
        /// faction's (a rival's spot only ever lies in the rival's own ground).
        /// A Fortress is never refused by a spot: the spot is FOR it.
        /// </summary>
        public static bool FootprintClearOfFortressSpots(float3 centre, int2 size, string buildingId)
        {
            EnsureEpoch();
            if (_spots.Count == 0 || buildingId == "Fortress") return true;
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            float2 mn = new float2(centre.x - hx, centre.z - hz);
            float2 mx = new float2(centre.x + hx, centre.z + hz);
            for (int i = 0; i < _spots.Count; i++)
            {
                var s = _spots[i];
                if (mn.x < s.Max.x && mx.x > s.Min.x && mn.y < s.Max.y && mx.y > s.Min.y) return false;
            }
            return true;
        }

        // ═════════════════════════════════════════════════════════════════
        // 1. THE SELF-LOCK CHECK
        // ═════════════════════════════════════════════════════════════════

        struct Box
        {
            public int2 C0, C1;          // build cells covered (inclusive)
            public float2 Min, Max;      // world footprint
            public byte Flags;
            public bool SideBefore, ExitBefore;
        }

        sealed class Window
        {
            public int Faction, Region;
            public int2 Origin, CoreCell;
            public int W;
            public int KBox, KPlan, KBuild, KTerr;
            public float BuiltAt;
            public byte[] Blocked = System.Array.Empty<byte>();
            public byte[] Gate = System.Array.Empty<byte>();
            public byte[] Outside = System.Array.Empty<byte>();   // sampled: another territory
            public int[] Before = System.Array.Empty<int>();       // 1 = reached
            public readonly List<int> Sources = new List<int>();
            public readonly List<Box> Boxes = new List<Box>();
            public bool ExitBefore;
            public int GatesBefore;
            public bool Valid;
        }

        static readonly Dictionary<long, Window> _windows = new Dictionary<long, Window>();

        // Flood scratch.
        static int[] _after = System.Array.Empty<int>();
        static int[] _queue = System.Array.Empty<int>();
        static byte[] _cand = System.Array.Empty<byte>();
        static readonly List<int> _src = new List<int>();
        static readonly List<int> _cluster = new List<int>();

        /// <summary>The flood's 4-neighbourhood.</summary>
        static readonly int2[] N4 = { new int2(1, 0), new int2(-1, 0), new int2(0, 1), new int2(0, -1) };

        static long WinKey(Faction f, int region) => ((long)(int)f << 32) | (uint)(region + 1);

        /// <summary>
        /// The territory's core — where the base grows from: the faction's
        /// capital or Fortress standing in it, else the region's seed.
        /// </summary>
        public static float3 CoreOf(EntityManager em, Faction faction, int region)
        {
            if (AIWallCorridor.TryGetHome(em, faction, out float3 home, out _)
                && RegionMap.RegionAt(home.x, home.z) == region)
                return home;
            if (TryGetOwnFortressIn(em, faction, region, out float3 fort)) return fort;
            var s = RegionMap.SeedOf(region);
            return new float3(s.x, 0f, s.y);
        }

        static readonly ComponentType[] QT_Fortresses =
        {
            ComponentType.ReadOnly<FortressTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Unity.Transforms.LocalTransform>(),
        };
        static CachedEntityQuery QC_Fortresses;

        public static bool TryGetOwnFortressIn(EntityManager em, Faction faction, int region, out float3 pos)
        {
            pos = default;
            var q = QC_Fortresses.Get(em, QT_Fortresses);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var xfs = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var p = xfs[i].Position;
                if (RegionMap.RegionAt(p.x, p.z) != region) continue;
                pos = p;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Would placing <paramref name="buildingId"/> at
        /// <paramref name="centre"/> seal part of <paramref name="faction"/>'s
        /// base? Null when it would not (or the check cannot judge: switched
        /// off, no region map, the footprint outside the window); otherwise
        /// WHAT it would seal, for the "BUILD: rejected — would seal" line.
        /// </summary>
        public static string WouldSeal(EntityManager em, Faction faction, float3 centre, int2 size,
            string buildingId, float now)
        {
            if (!Cfg.sealCheckEnabled || !RegionMap.Ready) return null;
            EnsureEpoch();
            int region = RegionMap.RegionAt(centre.x, centre.z);
            if (region == RegionMap.None) return null;

            // One clock for the cache whoever calls (the brain's think clock
            // is match-relative, the endgame systems' is the world's).
            var w = GetWindow(em, faction, region, TheWaningBorder.Core.SimClock.Now);
            if (!w.Valid) return null;

            // The candidate's cells, local to the window.
            FootprintCells(centre, size, out int2 g0, out int2 g1);
            int2 l0 = g0 - w.Origin, l1 = g1 - w.Origin;
            if (l0.x < 1 || l0.y < 1 || l1.x >= w.W - 1 || l1.y >= w.W - 1) return null;

            int n = w.W * w.W;
            if (_cand.Length < n) _cand = new byte[n];
            else System.Array.Clear(_cand, 0, n);
            int candCells = 0;
            for (int z = l0.y; z <= l1.y; z++)
                for (int x = l0.x; x <= l1.x; x++)
                {
                    int i = z * w.W + x;
                    _cand[i] = 1;
                    candCells++;
                }

            // Sources: the core's sources the candidate leaves free; with
            // none left (the candidate stands ON the core) the candidate's own
            // perimeter cells the base reached before.
            _src.Clear();
            for (int s = 0; s < w.Sources.Count; s++)
                if (_cand[w.Sources[s]] == 0) _src.Add(w.Sources[s]);
            if (_src.Count == 0)
                ForPerimeter(w, l0, l1, i => { if (w.Before[i] != 0 && _cand[i] == 0) _src.Add(i); });
            if (_src.Count == 0) return null;   // was never reachable: nothing to seal

            Flood(w, _src, _cand, ref _after);

            // 1. No enclosed pockets.
            int lost = 0;
            for (int i = 0; i < n; i++)
                if (w.Before[i] != 0 && _after[i] == 0 && _cand[i] == 0) lost++;

            // 2. The way out.
            bool exitAfter = ReachesExit(w, _after);
            if (w.ExitBefore && !exitAfter)
                return $"the way out of {RegionMap.NameOf(region)}";

            // 3. Gates.
            if (w.GatesBefore > 0)
            {
                int gatesAfter = 0;
                for (int i = 0; i < n; i++) if (w.Gate[i] != 0 && _after[i] != 0) gatesAfter++;
                if (gatesAfter < w.GatesBefore) return "a gate";
            }

            // 4. Every building keeps a free side; production keeps its exit.
            for (int b = 0; b < w.Boxes.Count; b++)
            {
                var box = w.Boxes[b];
                if (box.SideBefore && !SideReached(w, box.C0 - w.Origin, box.C1 - w.Origin, _after))
                    return $"the last free side of {KindName(box.Flags)} at " +
                           $"({(box.Min.x + box.Max.x) * 0.5f:F0},{(box.Min.y + box.Max.y) * 0.5f:F0})";
                if ((box.Flags & BuildSiteSnapshot.FlagProduction) != 0 && box.ExitBefore
                    && !ExitReached(w, box.C0 - w.Origin, box.C1 - w.Origin, _after))
                    return $"the unit exit of {KindName(box.Flags)} at " +
                           $"({(box.Min.x + box.Max.x) * 0.5f:F0},{(box.Min.y + box.Max.y) * 0.5f:F0})";
            }

            if (lost > math.max(0, Cfg.sealPocketToleranceCells))
                return $"a pocket of {lost} open cell(s)";

            // 5. The candidate itself.
            if (!SideReached(w, l0, l1, _after)) return "its own last free side";
            if (BuildSiteSnapshot.IsProductionId(buildingId) && !ExitReached(w, l0, l1, _after))
                return "its own unit exit";

            // 6. A flush row may only grow so long before a lane.
            int maxRun = Cfg.maxFlushClusterBuildings;
            if (maxRun > 0 && buildingId != "Hut")
            {
                int run = FlushClusterSize(w, centre, size);
                if (run > maxRun) return $"a lane (flush row of {run} buildings, max {maxRun})";
            }
            return null;
        }

        static string KindName(byte flags)
            => (flags & BuildSiteSnapshot.FlagProduction) != 0 ? "a production building"
             : (flags & BuildSiteSnapshot.FlagHouse) != 0 ? "a House"
             : (flags & BuildSiteSnapshot.FlagGathererHut) != 0 ? "a Gatherer's Hut"
             : "a building";

        static readonly Dictionary<int, float> _nextSealLog = new Dictionary<int, float>();

        /// <summary>The throttled "BUILD: rejected — would seal" line.</summary>
        public static void LogSeal(Faction faction, string buildingId, float3 at, string what, float now)
        {
            EnsureEpoch();
            // The world clock, whichever clock the caller passed.
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated) now = TheWaningBorder.Core.SimClock.Now;
            int key = (int)faction;
            if (_nextSealLog.TryGetValue(key, out float next) && now < next) return;
            _nextSealLog[key] = now + math.max(0f, Cfg.sealLogInterval);
            AILogger.Log(faction, "BUILD",
                $"rejected — would seal {what} ({buildingId} at ({at.x:F0},{at.z:F0}))");
        }

        // ── The window ────────────────────────────────────────────────────

        static Window GetWindow(EntityManager em, Faction faction, int region, float now)
        {
            long key = WinKey(faction, region);
            if (!_windows.TryGetValue(key, out var w)) { w = new Window(); _windows[key] = w; }
            var snap = BuildSiteSnapshot.Current(em);
            float3 core = CoreOf(em, faction, region);
            int2 coreCell = BuildGrid.WorldToCell(core);
            if (w.Valid && math.all(w.CoreCell == coreCell)
                && w.KBox == snap.BoxCount && w.KPlan == snap.PlanCount
                && w.KBuild == snap.BuildingCount && w.KTerr == TerritoryOwnership.Version
                && now - w.BuiltAt <= Cfg.sealCacheSeconds)
                return w;
            Build(em, w, faction, region, core, coreCell, snap, now);
            return w;
        }

        static void Build(EntityManager em, Window w, Faction faction, int region, float3 core,
            int2 coreCell, BuildSiteSnapshot snap, float now)
        {
            w.Faction = (int)faction;
            w.Region = region;
            w.CoreCell = coreCell;
            w.KBox = snap.BoxCount;
            w.KPlan = snap.PlanCount;
            w.KBuild = snap.BuildingCount;
            w.KTerr = TerritoryOwnership.Version;
            w.BuiltAt = now;
            w.Valid = false;

            int half = math.max(8, Cfg.sealWindowHalfCells);
            w.W = half * 2 + 1;
            w.Origin = coreCell - half;
            int n = w.W * w.W;
            if (w.Blocked.Length != n)
            {
                w.Blocked = new byte[n];
                w.Gate = new byte[n];
                w.Outside = new byte[n];
                w.Before = new int[n];
            }
            else
            {
                System.Array.Clear(w.Blocked, 0, n);
                System.Array.Clear(w.Gate, 0, n);
                System.Array.Clear(w.Outside, 0, n);
            }

            // Ground: the nav cost field (terrain, obstacles, nodes, stamped
            // buildings, curtain walls); a gate cell is walkable.
            bool anyKnown = false;
            for (int z = 0; z < w.W; z++)
                for (int x = 0; x < w.W; x++)
                {
                    int i = z * w.W + x;
                    float2 c = BuildGrid.CellCentre(w.Origin + new int2(x, z));
                    bool walk = NavGridQuery.IsWalkableAt(c.x, c.y, out bool gate, out bool known);
                    anyKnown |= known;
                    if (!walk) w.Blocked[i] = 1;
                    if (gate) w.Gate[i] = 1;
                    // Another territory, sampled every 4th cell (the exit test
                    // only needs to know the flood got out, not exactly where).
                    if ((x & 3) == 0 && (z & 3) == 0 && RegionMap.RegionAt(c.x, c.y) != region)
                        w.Outside[i] = 1;
                }
            if (!anyKnown) return;   // no nav field yet: cannot judge

            // Footprints the field has not stamped yet (sites, this think's
            // placements) and the faction's own plans.
            w.Boxes.Clear();
            for (int b = 0; b < snap.BoxCount; b++)
            {
                snap.GetBox(b, out float2 mn, out float2 mx, out byte flags);
                if ((flags & BuildSiteSnapshot.FlagWall) != 0) continue;   // on the field already
                AddBox(w, mn, mx, flags);
            }
            for (int p = 0; p < snap.PlanCount; p++)
            {
                snap.GetPlan(p, out float2 mn, out float2 mx, out byte pf, out byte flags);
                if (pf != (byte)faction) continue;
                AddBox(w, mn, mx, flags);
            }

            // Sources: the free cells around the core's footprint (a capital
            // or Fortress), else the free cell nearest the core.
            w.Sources.Clear();
            int2 lc = coreCell - w.Origin;
            int coreBox = -1;
            for (int b = 0; b < w.Boxes.Count; b++)
            {
                var bx = w.Boxes[b];
                if (core.x > bx.Min.x && core.x < bx.Max.x && core.z > bx.Min.y && core.z < bx.Max.y)
                { coreBox = b; break; }
            }
            if (coreBox >= 0)
                ForPerimeter(w, w.Boxes[coreBox].C0 - w.Origin, w.Boxes[coreBox].C1 - w.Origin,
                    i => { if (w.Blocked[i] == 0) w.Sources.Add(i); });
            if (w.Sources.Count == 0)
            {
                // Spiral out (fixed order: deterministic) for the nearest open cell.
                for (int r = 0; r <= 8 && w.Sources.Count == 0; r++)
                    for (int dz = -r; dz <= r && w.Sources.Count == 0; dz++)
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (math.max(math.abs(dx), math.abs(dz)) != r) continue;
                            int x = lc.x + dx, z = lc.y + dz;
                            if (x < 0 || z < 0 || x >= w.W || z >= w.W) continue;
                            int i = z * w.W + x;
                            if (w.Blocked[i] != 0) continue;
                            w.Sources.Add(i);
                            break;
                        }
            }
            if (w.Sources.Count == 0) return;

            if (_cand.Length < n) _cand = new byte[n];
            else System.Array.Clear(_cand, 0, n);
            Flood(w, w.Sources, _cand, ref w.Before);

            w.ExitBefore = ReachesExit(w, w.Before);
            w.GatesBefore = 0;
            for (int i = 0; i < n; i++) if (w.Gate[i] != 0 && w.Before[i] != 0) w.GatesBefore++;
            for (int b = 0; b < w.Boxes.Count; b++)
            {
                var bx = w.Boxes[b];
                bx.SideBefore = SideReached(w, bx.C0 - w.Origin, bx.C1 - w.Origin, w.Before);
                bx.ExitBefore = (bx.Flags & BuildSiteSnapshot.FlagProduction) != 0
                    && ExitReached(w, bx.C0 - w.Origin, bx.C1 - w.Origin, w.Before);
                w.Boxes[b] = bx;
            }
            w.Valid = true;
        }

        static void AddBox(Window w, float2 mn, float2 mx, byte flags)
        {
            int2 c0 = BuildGrid.WorldToCell(new float3(mn.x + 0.01f, 0f, mn.y + 0.01f));
            int2 c1 = BuildGrid.WorldToCell(new float3(mx.x - 0.01f, 0f, mx.y - 0.01f));
            int2 l0 = c0 - w.Origin, l1 = c1 - w.Origin;
            if (l1.x < 0 || l1.y < 0 || l0.x >= w.W || l0.y >= w.W) return;   // outside the window
            for (int z = math.max(0, l0.y); z <= math.min(w.W - 1, l1.y); z++)
                for (int x = math.max(0, l0.x); x <= math.min(w.W - 1, l1.x); x++)
                    w.Blocked[z * w.W + x] = 1;
            w.Boxes.Add(new Box { C0 = c0, C1 = c1, Min = mn, Max = mx, Flags = flags });
        }

        // ── Flood + tests ─────────────────────────────────────────────────

        static void Flood(Window w, List<int> sources, byte[] extraBlocked, ref int[] reach)
        {
            int n = w.W * w.W;
            if (reach.Length < n) reach = new int[n];
            else System.Array.Clear(reach, 0, n);
            if (_queue.Length < n) _queue = new int[n];
            int head = 0, tail = 0;
            for (int s = 0; s < sources.Count; s++)
            {
                int i = sources[s];
                if (reach[i] != 0) continue;
                reach[i] = 1;
                _queue[tail++] = i;
            }
            while (head < tail)
            {
                int i = _queue[head++];
                int x = i % w.W, z = i / w.W;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + N4[k].x, nz = z + N4[k].y;
                    if (nx < 0 || nz < 0 || nx >= w.W || nz >= w.W) continue;
                    int j = nz * w.W + nx;
                    if (reach[j] != 0 || w.Blocked[j] != 0 || extraBlocked[j] != 0) continue;
                    reach[j] = 1;
                    _queue[tail++] = j;
                }
            }
        }

        /// <summary>Did the flood get out: the window's edge, or another territory?</summary>
        static bool ReachesExit(Window w, int[] reach)
        {
            int n = w.W * w.W;
            for (int i = 0; i < n; i++)
            {
                if (reach[i] == 0) continue;
                if (w.Outside[i] != 0) return true;
                int x = i % w.W, z = i / w.W;
                if (x == 0 || z == 0 || x == w.W - 1 || z == w.W - 1) return true;
            }
            return false;
        }

        /// <summary>The cells edge-adjacent to a footprint (corners excluded —
        /// a unit cannot slip out diagonally between two buildings).</summary>
        static void ForPerimeter(Window w, int2 l0, int2 l1, System.Action<int> visit)
        {
            for (int x = l0.x; x <= l1.x; x++)
            {
                Visit(w, x, l0.y - 1, visit);
                Visit(w, x, l1.y + 1, visit);
            }
            for (int z = l0.y; z <= l1.y; z++)
            {
                Visit(w, l0.x - 1, z, visit);
                Visit(w, l1.x + 1, z, visit);
            }
        }

        static void Visit(Window w, int x, int z, System.Action<int> visit)
        {
            if (x < 0 || z < 0 || x >= w.W || z >= w.W) return;
            visit(z * w.W + x);
        }

        static bool SideReached(Window w, int2 l0, int2 l1, int[] reach)
        {
            for (int x = l0.x; x <= l1.x; x++)
                if (Reached(w, x, l0.y - 1, reach) || Reached(w, x, l1.y + 1, reach)) return true;
            for (int z = l0.y; z <= l1.y; z++)
                if (Reached(w, l0.x - 1, z, reach) || Reached(w, l1.x + 1, z, reach)) return true;
            return false;
        }

        /// <summary>The production EXIT: the column just east of the footprint,
        /// around its middle row — where TrainingSystem walks a new unit out
        /// when no rally point turns it.</summary>
        static bool ExitReached(Window w, int2 l0, int2 l1, int[] reach)
        {
            int mid = (l0.y + l1.y) / 2;
            for (int z = mid - 1; z <= mid + 1; z++)
                if (Reached(w, l1.x + 1, z, reach) || Reached(w, l1.x + 2, z, reach)) return true;
            return false;
        }

        static bool Reached(Window w, int x, int z, int[] reach)
            => x >= 0 && z >= 0 && x < w.W && z < w.W && reach[z * w.W + x] != 0;

        /// <summary>How many buildings the candidate's flush cluster would
        /// hold, itself included: footprints sharing an edge (touching, not
        /// merely a corner). Houses and walls do not count.</summary>
        static int FlushClusterSize(Window w, float3 centre, int2 size)
        {
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            float2 cmn = new float2(centre.x - hx, centre.z - hz);
            float2 cmx = new float2(centre.x + hx, centre.z + hz);
            _cluster.Clear();
            // Seed with every box touching the candidate.
            for (int b = 0; b < w.Boxes.Count; b++)
                if ((w.Boxes[b].Flags & BuildSiteSnapshot.FlagHouse) == 0
                    && Touch(cmn, cmx, w.Boxes[b].Min, w.Boxes[b].Max))
                    _cluster.Add(b);
            for (int head = 0; head < _cluster.Count && _cluster.Count < 64; head++)
            {
                var a = w.Boxes[_cluster[head]];
                for (int b = 0; b < w.Boxes.Count; b++)
                {
                    if (_cluster.Contains(b)) continue;
                    if ((w.Boxes[b].Flags & BuildSiteSnapshot.FlagHouse) != 0) continue;
                    if (Touch(a.Min, a.Max, w.Boxes[b].Min, w.Boxes[b].Max)) _cluster.Add(b);
                }
            }
            return _cluster.Count + 1;
        }

        static bool Touch(float2 amn, float2 amx, float2 bmn, float2 bmx)
        {
            const float E = 0.05f;
            bool overlapX = amn.x < bmx.x - E && amx.x > bmn.x + E;
            bool overlapZ = amn.y < bmx.y - E && amx.y > bmn.y + E;
            bool flushX = math.abs(amx.x - bmn.x) <= E || math.abs(bmx.x - amn.x) <= E;
            bool flushZ = math.abs(amx.y - bmn.y) <= E || math.abs(bmx.y - amn.y) <= E;
            return (flushX && overlapZ) || (flushZ && overlapX);
        }

        /// <summary>Footprint -> covered build cells (the snap rule's cells).</summary>
        static void FootprintCells(float3 centre, int2 size, out int2 c0, out int2 c1)
        {
            float hx = size.x * 0.5f, hz = size.y * 0.5f;
            c0 = BuildGrid.WorldToCell(new float3(centre.x - hx + 0.01f, 0f, centre.z - hz + 0.01f));
            c1 = BuildGrid.WorldToCell(new float3(centre.x + hx - 0.01f, 0f, centre.z + hz - 0.01f));
        }
    }
}
