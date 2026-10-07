// AIBaseTemplate.cs
// The AI's drawn base layouts (2026-10-06, docs/Design/Game_AI.md § 6g).
//
// The developer drew two bases cell by cell: the MAIN CAMP round the home
// Fortress (houses, four kinds of production, Temple, Vault, towers, and a
// closed ring of wall hubs) and a smaller OUTPOST round every other Fortress
// (production, towers, a ring). This class turns those grids
// (AIBaseTemplate.asset) into slots anchored on the territory's Fortress:
//
//   * a placer asking for a slotted building takes the first FREE slot of
//     its kind, nearest the Fortress first (SimpleAISystem.TryTemplateSlot);
//   * a slot that cannot be used as drawn is moved to the nearest legal spot
//     and REMEMBERED there, so the next building of that kind moves on to the
//     next slot instead of hunting round the same blocked one;
//   * every other placement keeps off the free slots, as it keeps off the
//     wall corridor and the reserved Fortress spots;
//   * the wall plan for a templated territory is the drawn hub ring
//     (AIWallPlanner.BuildPlan), not a trace of the border.
//
// A slot is FILLED when a building or one of the faction's own plans stands
// with exactly the slot's footprint at the slot's position (as drawn, or as
// moved). Host-side decision helper over replicated state; everything it
// decides reaches the simulation through CommandRouter. Single-threaded (the
// AI think loop and the endgame systems run on the main thread).

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.AI
{
    public static class AIBaseTemplate
    {
        static AIBaseTemplateConfig Cfg => AIBaseTemplateConfig.I;

        public static bool Enabled => Cfg != null && Cfg.enabled;

        public static int SearchRadiusCells => Cfg.slotSearchRadiusCells;

        public static bool AlternateTrebuchet => Cfg.alternateTrebuchet;

        // ── The ring ──────────────────────────────────────────────────────

        static readonly ComponentType[] QT_Fortresses =
        {
            ComponentType.ReadOnly<FortressTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<Unity.Transforms.LocalTransform>(),
        };
        static CachedEntityQuery QC_Fortresses;

        /// <summary>
        /// Every territory other than <paramref name="homeRegion"/> that the
        /// faction holds and has a Fortress standing in, with that Fortress,
        /// ascending by territory index (the same order on every peer). These
        /// get the outpost layout, ring included.
        /// </summary>
        public static void CollectOutpostRegions(EntityManager em, Faction faction, int homeRegion,
            List<int> regions, List<float3> anchors)
        {
            if (!Enabled || !RegionMap.Ready || !TerritoryOwnership.Ready) return;
            if (Outpost.Hubs.Count < 3) return;
            var q = QC_Fortresses.Get(em, QT_Fortresses);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var xfs = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp);
            int start = regions.Count;
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var p = xfs[i].Position;
                int r = RegionMap.RegionAt(p.x, p.z);
                if (r == RegionMap.None || r == homeRegion) continue;
                if (TerritoryOwnership.OwnerOf(r) != (int)faction) continue;
                if (regions.IndexOf(r) >= 0) continue;
                // Keep the list sorted by region from `start` on.
                int at = regions.Count;
                while (at > start && regions[at - 1] > r) at--;
                regions.Insert(at, r);
                anchors.Insert(at, p);
            }
        }

        /// <summary>
        /// The drawn hub ring for a layout, round <paramref name="origin"/>:
        /// one slot per hub in bearing order (a closed chain), a gate on the
        /// link nearest each of gatesPerRing bearings from north, and an
        /// emplacement on every emplacementEveryNthLink-th other link.
        /// </summary>
        public static void EmitRing(EntityManager em, Faction faction, int region, Layout layout, float3 origin,
            byte chain, NativeList<AIWallPlanSlot> slots)
        {
            if (layout.Hubs.Count < 3) return;
            int2 hubSize = BuildingSizeConfig.GetSize("Alanthor_Wall");
            var hubs = HubPositions(em, faction, region, layout, origin, hubSize);
            int n = hubs.Count;
            if (n < 3) return;
            var pos = new float3[n];
            for (int i = 0; i < n; i++)
            {
                pos[i] = hubs[i];
                pos[i].y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(pos[i].x, pos[i].z);
            }

            var gate = new bool[n];
            int gates = math.min(math.max(0, Cfg.gatesPerRing), n);
            for (int g = 0; g < gates; g++)
            {
                float want = math.PI * 0.5f + g * 2f * math.PI / gates;
                int best = -1;
                float bestErr = float.MaxValue;
                for (int i = 0; i < n; i++)
                {
                    if (gate[i]) continue;
                    float3 mid = (pos[i] + pos[(i + 1) % n]) * 0.5f - origin;
                    float a = math.atan2(mid.z, mid.x);
                    float err = math.abs(math.atan2(math.sin(a - want), math.cos(a - want)));
                    if (err < bestErr) { bestErr = err; best = i; }
                }
                if (best >= 0) gate[best] = true;
            }

            int every = math.max(0, Cfg.emplacementEveryNthLink);
            int plain = 0;
            for (int i = 0; i < n; i++)
            {
                byte flags = 0;
                if (gate[i]) flags |= AIWallPlanner.FlagGateAfter;
                else if (every > 0 && (plain++ % every) == 0) flags |= AIWallPlanner.FlagEmplacement;
                slots.Add(new AIWallPlanSlot { Position = pos[i], Chain = chain, Flags = flags });
            }
        }

        // ── Parsed layouts ────────────────────────────────────────────────

        public struct Slot
        {
            /// <summary>Index into the config's legend.</summary>
            public int Legend;
            /// <summary>Footprint centre relative to the Fortress centre, metres (x, z).</summary>
            public float2 Offset;
            /// <summary>Footprint, metres.</summary>
            public int2 Size;
            /// <summary>Distance of the offset from the Fortress (fill order).</summary>
            public float Reach;
        }

        public sealed class Layout
        {
            public readonly List<Slot> Slots = new List<Slot>();
            /// <summary>Hub centres relative to the Fortress centre, metres,
            /// in ring order (by bearing, counter-clockwise from east).</summary>
            public readonly List<float2> Hubs = new List<float2>();
        }

        static Layout _main, _outpost;
        static AIBaseTemplateConfig _parsedFrom;

        static void EnsureParsed()
        {
            var cfg = Cfg;
            if (_parsedFrom == cfg && _main != null) return;
            _parsedFrom = cfg;
            _main = Parse(cfg, cfg.mainCamp, "mainCamp");
            _outpost = Parse(cfg, cfg.outpost, "outpost");
        }

        public static Layout Main { get { EnsureParsed(); return _main; } }
        public static Layout Outpost { get { EnsureParsed(); return _outpost; } }

        static Layout Parse(AIBaseTemplateConfig cfg, string[] rows, string name)
        {
            var layout = new Layout();
            if (rows == null || rows.Length == 0) return layout;
            int H = rows.Length, W = 0;
            for (int r = 0; r < H; r++) W = math.max(W, rows[r]?.Length ?? 0);
            char At(int x, int z) => z >= 0 && z < H && x >= 0 && rows[z] != null && x < rows[z].Length ? rows[z][x] : '.';

            char fort = string.IsNullOrEmpty(cfg.fortressSymbol) ? 'F' : cfg.fortressSymbol[0];
            char hub = string.IsNullOrEmpty(cfg.wallHubSymbol) ? 'W' : cfg.wallHubSymbol[0];

            // The anchor: the centre of the Fortress block, in cell units
            // (x right, z DOWN the rows).
            int fx0 = int.MaxValue, fz0 = int.MaxValue, fx1 = int.MinValue, fz1 = int.MinValue;
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                    if (At(x, z) == fort)
                    {
                        fx0 = math.min(fx0, x); fz0 = math.min(fz0, z);
                        fx1 = math.max(fx1, x); fz1 = math.max(fz1, z);
                    }
            if (fx0 == int.MaxValue)
            {
                UnityEngine.Debug.LogError($"[AIBaseTemplate] layout '{name}' has no Fortress block ('{fort}')");
                return layout;
            }
            float acx = (fx0 + fx1 + 1) * 0.5f, acz = (fz0 + fz1 + 1) * 0.5f;
            float cs = BuildGrid.CellSize;
            // A block covering cells [x0, x0+w) x [z0, z0+h) -> centre offset, metres.
            float2 OffsetOf(int x0, int z0, int w, int h)
                => new float2((x0 + w * 0.5f - acx) * cs, -(z0 + h * 0.5f - acz) * cs);

            var used = new bool[W * H];

            // Hubs: every connected hub block is ONE hub at its centre.
            var hubs = new List<float2>();
            for (int z = 0; z < H; z++)
                for (int x = 0; x < W; x++)
                {
                    if (At(x, z) != hub || used[z * W + x]) continue;
                    int bx0 = x, bz0 = z, bx1 = x, bz1 = z;
                    var stack = new Stack<int2>();
                    stack.Push(new int2(x, z));
                    used[z * W + x] = true;
                    while (stack.Count > 0)
                    {
                        var c = stack.Pop();
                        bx0 = math.min(bx0, c.x); bz0 = math.min(bz0, c.y);
                        bx1 = math.max(bx1, c.x); bz1 = math.max(bz1, c.y);
                        for (int d = 0; d < 4; d++)
                        {
                            int nx = c.x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                            int nz = c.y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                            if (nx < 0 || nz < 0 || nx >= W || nz >= H) continue;
                            if (used[nz * W + nx] || At(nx, nz) != hub) continue;
                            used[nz * W + nx] = true;
                            stack.Push(new int2(nx, nz));
                        }
                    }
                    hubs.Add(OffsetOf(bx0, bz0, bx1 - bx0 + 1, bz1 - bz0 + 1));
                }
            hubs.Sort((a, b) =>
            {
                float aa = math.atan2(a.y, a.x), bb = math.atan2(b.y, b.x);
                if (aa < 0f) aa += 2f * math.PI;
                if (bb < 0f) bb += 2f * math.PI;
                return aa.CompareTo(bb);
            });
            layout.Hubs.AddRange(hubs);

            // Buildings: each legend symbol's cells tiled, row by row, into
            // footprints of its first building.
            var legend = cfg.legend ?? System.Array.Empty<AIBaseTemplateLegendEntry>();
            for (int li = 0; li < legend.Length; li++)
            {
                var e = legend[li];
                if (e == null || string.IsNullOrEmpty(e.symbol) || e.buildingIds == null || e.buildingIds.Length == 0)
                    continue;
                char sym = e.symbol[0];
                int2 sizeM = BuildingSizeConfig.GetSize(e.buildingIds[0]);
                int2 cells = BuildingSizeConfig.ToCells(sizeM);
                if (cells.x <= 0 || cells.y <= 0) continue;
                for (int z = 0; z < H; z++)
                    for (int x = 0; x < W; x++)
                    {
                        if (At(x, z) != sym || used[z * W + x]) continue;
                        bool fits = true;
                        for (int dz = 0; dz < cells.y && fits; dz++)
                            for (int dx = 0; dx < cells.x && fits; dx++)
                                if (At(x + dx, z + dz) != sym || used[(z + dz) * W + x + dx]) fits = false;
                        if (!fits) continue;
                        for (int dz = 0; dz < cells.y; dz++)
                            for (int dx = 0; dx < cells.x; dx++)
                                used[(z + dz) * W + x + dx] = true;
                        var off = OffsetOf(x, z, cells.x, cells.y);
                        layout.Slots.Add(new Slot { Legend = li, Offset = off, Size = sizeM, Reach = math.length(off) });
                    }
            }
            return layout;
        }

        // ── Origins ───────────────────────────────────────────────────────

        /// <summary>
        /// The layout and anchor for <paramref name="region"/>: the main camp
        /// round the home capital, an outpost round any other own Fortress.
        /// False where the faction has no Fortress (no anchor, no template).
        /// </summary>
        public static bool TryGetOrigin(EntityManager em, Faction faction, int region,
            out float3 origin, out Layout layout)
        {
            origin = default; layout = null;
            if (!Enabled || region == RegionMap.None) return false;
            if (AIWallCorridor.TryGetHome(em, faction, out float3 home, out _)
                && RegionMap.RegionAt(home.x, home.z) == region)
            {
                origin = home; layout = Main;
                return layout.Slots.Count > 0 || layout.Hubs.Count > 0;
            }
            if (AIBaseLayout.TryGetOwnFortressIn(em, faction, region, out float3 fort))
            {
                origin = fort; layout = Outpost;
                return layout.Slots.Count > 0 || layout.Hubs.Count > 0;
            }
            return false;
        }

        public static bool IsMain(Layout layout) => ReferenceEquals(layout, _main);

        // -- Variants (2026-10-06) ----------------------------------------
        // Every base is drawn in one of the eight orientations of its grid
        // (four quarter turns, mirrored or not) and with its production
        // blocks dealt out to the production buildings in one of the 24
        // orders, so no two towns look alike. The choice is a hash of the
        // match seed, the faction and the territory (the main camp ignores
        // the territory, so it is the same from the first second, before
        // the region map exists): every peer draws the same town.

        public struct Variant
        {
            public int Rotation;     // quarter turns, counter-clockwise
            public bool Mirror;      // mirror x before turning
            public int[] Legend;     // legend index -> the legend its slots serve
        }

        static readonly Dictionary<(int, int), Variant> _variants = new Dictionary<(int, int), Variant>();
        static int _variantSeed = int.MinValue;

        public static Variant VariantOf(Faction faction, int region, Layout layout)
        {
            int key = IsMain(layout) ? -1 : region;
            if (_variantSeed != GameSettings.SpawnSeed)
            {
                _variantSeed = GameSettings.SpawnSeed;
                _variants.Clear();
            }
            if (_variants.TryGetValue(((int)faction, key), out var v)) return v;

            uint h = Mix((uint)GameSettings.SpawnSeed * 0x9E3779B1u);
            h = Mix(h ^ ((uint)((int)faction + 1) * 0x85EBCA77u));
            h = Mix(h ^ ((uint)(key + 2) * 0xC2B2AE3Du));
            // The orientation: a main camp steps through the eight by faction
            // (3 is coprime to 8, so no two factions share one in a match);
            // an outpost takes its territory's hash.
            uint orient = key == -1
                ? (Mix((uint)GameSettings.SpawnSeed) + 3u * (uint)(int)faction) & 7u
                : h & 7u;

            var legend = Cfg.legend ?? System.Array.Empty<AIBaseTemplateLegendEntry>();
            var map = new int[legend.Length];
            for (int i = 0; i < map.Length; i++) map[i] = i;
            if (Cfg.variants)
            {
                // Deal the production symbols out among themselves
                // (Fisher-Yates driven by the hash).
                var prod = new List<int>();
                for (int i = 0; i < legend.Length; i++) if (IsProductionLegend(i)) prod.Add(i);
                var dealt = new List<int>(prod);
                uint r = h >> 3;
                for (int i = dealt.Count - 1; i > 0; i--)
                {
                    int j = (int)(r % (uint)(i + 1));
                    r = r / (uint)(i + 1) + (r % 7u) * 2654435761u;
                    int t = dealt[i]; dealt[i] = dealt[j]; dealt[j] = t;
                }
                for (int i = 0; i < prod.Count; i++) map[prod[i]] = dealt[i];
            }
            v = new Variant
            {
                Rotation = Cfg.variants ? (int)(orient & 3u) : 0,
                Mirror = Cfg.variants && (orient & 4u) != 0,
                Legend = map,
            };
            _variants[((int)faction, key)] = v;
            return v;
        }

        /// <summary>Murmur3's 32-bit finaliser.</summary>
        static uint Mix(uint h)
        {
            h ^= h >> 16; h *= 0x85EBCA6Bu; h ^= h >> 13; h *= 0xC2B2AE35u; h ^= h >> 16;
            return h;
        }

        public static float2 Orient(float2 off, Variant v)
        {
            if (v.Mirror) off.x = -off.x;
            for (int k = 0; k < v.Rotation; k++) off = new float2(-off.y, off.x);
            return off;
        }

        /// <summary>The building symbol slot <paramref name="i"/> serves in this variant.</summary>
        public static int SlotLegend(Layout layout, int i, Variant v)
        {
            int l = layout.Slots[i].Legend;
            return v.Legend != null && l >= 0 && l < v.Legend.Length ? v.Legend[l] : l;
        }

        /// <summary>Slot <paramref name="i"/>'s footprint centre as drawn
        /// (in this base's variant), snapped.</summary>
        public static float3 DrawnPosition(Faction faction, int region, Layout layout, int i, float3 origin)
        {
            var s = layout.Slots[i];
            var o = Orient(s.Offset, VariantOf(faction, region, layout));
            return BuildGrid.Snap(new float3(origin.x + o.x, 0f, origin.z + o.y), s.Size);
        }

        /// <summary>
        /// The layout's hub centres round <paramref name="origin"/>, snapped,
        /// in ring order (by bearing) for this base's variant.
        /// </summary>
        /// <remarks>
        /// THE TURTLE'S WIDER RING (2026-10-07, Game_AI.md § 3b): a main camp's
        /// hub offsets are multiplied by the personality's homeRingScale
        /// (read unblended — the shape of the base is the same at every
        /// tier). A scaled hub that would stand off the territory, or within
        /// the wall inset of its border, is pulled back toward its drawn spot
        /// (never inside it); and a scaled ring gets an extra hub on every
        /// link longer than ringMaxLinkMeters, so the doctrine's link radius
        /// still spans every link. The building slots are not scaled: the
        /// drawn town keeps its shape and the ordinary site search fills the
        /// extra ground inside the ring.
        /// </remarks>
        public static List<float3> HubPositions(EntityManager em, Faction faction, int region, Layout layout,
            float3 origin, int2 hubSize)
        {
            var v = VariantOf(faction, region, layout);
            float scale = IsMain(layout) ? RingScale(em, faction) : 1f;
            var list = new List<float3>(layout.Hubs.Count);
            for (int i = 0; i < layout.Hubs.Count; i++)
            {
                var o = Orient(layout.Hubs[i], v);
                list.Add(ScaledHub(origin, o, scale, region, hubSize));
            }
            list.Sort((a, b) =>
            {
                float aa = math.atan2(a.z - origin.z, a.x - origin.x), bb = math.atan2(b.z - origin.z, b.x - origin.x);
                if (aa < 0f) aa += 2f * math.PI;
                if (bb < 0f) bb += 2f * math.PI;
                return aa.CompareTo(bb);
            });
            if (scale > 1f && Cfg.ringMaxLinkMeters > 0f && list.Count >= 3)
            {
                var dense = new List<float3>(list.Count * 2);
                for (int i = 0; i < list.Count; i++)
                {
                    float3 a = list[i], b = list[(i + 1) % list.Count];
                    dense.Add(a);
                    float d = math.distance(a.xz, b.xz);
                    int parts = (int)math.ceil(d / Cfg.ringMaxLinkMeters);
                    for (int k = 1; k < parts; k++)
                        dense.Add(BuildGrid.Snap(math.lerp(a, b, k / (float)parts), hubSize));
                }
                list = dense;
            }
            return list;
        }

        /// <summary>The faction's homeRingScale (its personality row as
        /// authored); 1 for a faction with no brain or an unset row.</summary>
        static float RingScale(EntityManager em, Faction faction)
        {
            float s = AIPersonalityLookup.Row(em, faction).homeRingScale;
            return s > 0f ? s : 1f;
        }

        /// <summary>One hub at <paramref name="scale"/> times its drawn offset,
        /// stepped back toward the drawn spot (scale 1, always accepted, as
        /// before) while it would stand off <paramref name="region"/> or within
        /// the wall inset of its border.</summary>
        static float3 ScaledHub(float3 origin, float2 off, float scale, int region, int2 hubSize)
        {
            const float Step = 0.1f;   // loop resolution, not tuning
            for (float s = scale; s > 1f; s -= Step)
            {
                var p = BuildGrid.Snap(new float3(origin.x + off.x * s, 0f, origin.z + off.y * s), hubSize);
                if (OnOwnTerritory(p, region, hubSize)) return p;
            }
            return BuildGrid.Snap(new float3(origin.x + off.x, 0f, origin.z + off.y), hubSize);
        }

        static bool OnOwnTerritory(float3 p, int region, int2 hubSize)
        {
            if (region == RegionMap.None || !RegionMap.Ready) return true;
            float m = math.max(hubSize.x, hubSize.y) * 0.5f + AIWallPlannerConfig.I.borderInset;
            return RegionMap.RegionAt(p.x, p.z) == region
                && RegionMap.RegionAt(p.x + m, p.z) == region
                && RegionMap.RegionAt(p.x - m, p.z) == region
                && RegionMap.RegionAt(p.x, p.z + m) == region
                && RegionMap.RegionAt(p.x, p.z - m) == region;
        }

        /// <summary>
        /// Where the faction's starting House stands: the main camp's House
        /// slot nearest the Fortress, in this faction's variant, if legal.
        /// Called by the player spawn before the region map exists, so it
        /// needs only the Fortress position.
        /// </summary>
        public static bool TryStartingHouseSlot(EntityManager em, Faction faction, float3 fortress,
            string houseId, out float3 pos)
        {
            pos = default;
            if (!Enabled) return false;
            var layout = Main;
            int2 size = BuildingSizeConfig.GetSize(houseId);
            var list = new List<int>(24);
            CandidateSlots(faction, RegionMap.None, layout, houseId, size, list);
            for (int k = 0; k < list.Count; k++)
            {
                float3 p = DrawnPosition(faction, RegionMap.None, layout, list[k], fortress);
                p.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.z);
                if (!BuildCommandHelper.IsValidBuildPosition(em, p, size, houseId)) continue;
                pos = p;
                return true;
            }
            return false;
        }

        // ── Moved slots ───────────────────────────────────────────────────

        static int _epoch = int.MinValue;
        static readonly Dictionary<(int, int, int), float3> _moved = new Dictionary<(int, int, int), float3>();

        static void EnsureEpoch()
        {
            if (_epoch == SimCadence.Epoch) return;
            _epoch = SimCadence.Epoch;
            _moved.Clear();
            _origins.Clear();
        }

        /// <summary>Where slot <paramref name="i"/> stands now: as drawn, or
        /// where it was moved to.</summary>
        public static float3 SlotPosition(Faction faction, int region, Layout layout, int i, float3 origin)
        {
            EnsureEpoch();
            return _moved.TryGetValue(((int)faction, region, i), out var p) ? p : DrawnPosition(faction, region, layout, i, origin);
        }

        public static bool IsMoved(Faction faction, int region, int i)
        {
            EnsureEpoch();
            return _moved.ContainsKey(((int)faction, region, i));
        }

        public static void MoveSlot(Faction faction, int region, int i, float3 pos)
        {
            EnsureEpoch();
            _moved[((int)faction, region, i)] = pos;
        }

        // ── Occupancy ─────────────────────────────────────────────────────

        static void Aabb(float3 c, int2 size, out float2 mn, out float2 mx)
        {
            mn = new float2(c.x - size.x * 0.5f, c.z - size.y * 0.5f);
            mx = new float2(c.x + size.x * 0.5f, c.z + size.y * 0.5f);
        }

        /// <summary>A building, or one of the faction's own plans, stands with
        /// exactly this footprint here.</summary>
        public static bool IsFilled(BuildSiteSnapshot snap, Faction faction, float3 pos, int2 size)
        {
            Aabb(pos, size, out float2 mn, out float2 mx);
            const float tol = 0.25f;
            for (int i = 0; i < snap.BoxCount; i++)
            {
                snap.GetBox(i, out float2 bmn, out float2 bmx, out _);
                if (math.all(math.abs(bmn - mn) < tol) && math.all(math.abs(bmx - mx) < tol)) return true;
            }
            for (int i = 0; i < snap.PlanCount; i++)
            {
                snap.GetPlan(i, out float2 pmn, out float2 pmx, out byte f, out _);
                if (f != (byte)faction) continue;
                if (math.all(math.abs(pmn - mn) < tol) && math.all(math.abs(pmx - mx) < tol)) return true;
            }
            return false;
        }

        /// <summary>The legend entry for a building id (its own symbol), or -1.</summary>
        public static int LegendOf(string buildingId)
        {
            var legend = Cfg.legend;
            if (legend == null || string.IsNullOrEmpty(buildingId)) return -1;
            for (int i = 0; i < legend.Length; i++)
            {
                var ids = legend[i]?.buildingIds;
                if (ids == null) continue;
                for (int k = 0; k < ids.Length; k++)
                    if (ids[k] == buildingId) return i;
            }
            return -1;
        }

        public static bool IsProductionLegend(int legend)
            => legend >= 0 && Cfg.legend != null && legend < Cfg.legend.Length
               && Cfg.legend[legend] != null && Cfg.legend[legend].production;

        /// <summary>
        /// The slots a building may take, in order: its own symbol's slots
        /// nearest the Fortress first, then (production only) every other
        /// production symbol's. Only slots whose footprint matches the
        /// building's are offered.
        /// </summary>
        public static void CandidateSlots(Faction faction, int region, Layout layout, string buildingId,
            int2 size, List<int> into)
        {
            into.Clear();
            int own = LegendOf(buildingId);
            if (own < 0) return;
            var variant = VariantOf(faction, region, layout);
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1 && !IsProductionLegend(own)) break;
                int start = into.Count;
                for (int i = 0; i < layout.Slots.Count; i++)
                {
                    var s = layout.Slots[i];
                    if (!math.all(s.Size == size)) continue;
                    int serves = SlotLegend(layout, i, variant);
                    bool mine = serves == own;
                    if (pass == 0 ? !mine : (mine || !IsProductionLegend(serves))) continue;
                    into.Add(i);
                }
                int count = into.Count - start;
                var range = into.GetRange(start, count);
                range.Sort((a, b) =>
                {
                    int c = layout.Slots[a].Reach.CompareTo(layout.Slots[b].Reach);
                    return c != 0 ? c : a.CompareTo(b);
                });
                for (int k = 0; k < count; k++) into[start + k] = range[k];
            }
        }

        /// <summary>
        /// The first free slot for <paramref name="buildingId"/> in the
        /// territory's layout that is legal where it stands (as drawn, or
        /// where it was moved) — for placers outside SimpleAISystem, which do
        /// not move slots.
        /// </summary>
        public static bool TryFirstFreeDrawnSlot(EntityManager em, Faction faction, int region,
            string buildingId, int2 size, out float3 pos)
        {
            pos = default;
            if (!TryGetOrigin(em, faction, region, out float3 origin, out Layout layout)) return false;
            var list = new List<int>(16);
            CandidateSlots(faction, region, layout, buildingId, size, list);
            var snap = BuildSiteSnapshot.Current(em);
            for (int k = 0; k < list.Count; k++)
            {
                float3 p = SlotPosition(faction, region, layout, list[k], origin);
                if (IsFilled(snap, faction, p, size)) continue;
                if (RegionMap.RegionAt(p.x, p.z) != region) continue;
                if (!TerritoryOwnership.CanBuildAt(em, faction, buildingId, p.x, p.z)) continue;
                if (snap.Overlaps(p, size, 0f, ignoreWalls: false)) continue;
                if (snap.OverlapsOwnPlan(faction, p, size)) continue;
                p.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.z);
                if (!snap.IsValidBuildPosition(em, p, size, buildingId)) continue;
                pos = p;
                return true;
            }
            return false;
        }

        // ── Keeping the free slots free ──────────────────────────────────

        struct OriginCache { public int Frame; public bool Has; public float3 Origin; public Layout Layout; }
        static readonly Dictionary<(int, int), OriginCache> _origins = new Dictionary<(int, int), OriginCache>();

        static bool CachedOrigin(EntityManager em, Faction faction, int region, out float3 origin, out Layout layout)
        {
            EnsureEpoch();
            int frame = UnityEngine.Time.frameCount;
            var key = ((int)faction, region);
            if (_origins.TryGetValue(key, out var c) && c.Frame == frame)
            {
                origin = c.Origin; layout = c.Layout;
                return c.Has;
            }
            bool has = TryGetOrigin(em, faction, region, out origin, out layout);
            _origins[key] = new OriginCache { Frame = frame, Has = has, Origin = origin, Layout = layout };
            return has;
        }

        /// <summary><see cref="FootprintClearOfFreeSlots"/> for the faction
        /// that HOLDS the ground — for placers that carry no faction (the
        /// endgame ring scans, the tower scan).</summary>
        public static bool FootprintClearOfFreeSlotsForOwner(EntityManager em, float3 centre, int2 size)
        {
            if (!Enabled || !RegionMap.Ready || !TerritoryOwnership.Ready) return true;
            int t = RegionMap.RegionAt(centre.x, centre.z);
            if (t == RegionMap.None) return true;
            int owner = TerritoryOwnership.OwnerOf(t);
            return owner < 0 || FootprintClearOfFreeSlots(em, (Faction)owner, centre, size);
        }

        /// <summary>
        /// True when the footprint covers no FREE slot of the layout of the
        /// territory it stands in (and, when <paramref name="exceptSlot"/> is
        /// set, ignores that one slot — the one being moved). A filled slot
        /// is no longer reserved.
        /// </summary>
        public static bool FootprintClearOfFreeSlots(EntityManager em, Faction faction, float3 centre,
            int2 size, int exceptSlot = -1)
        {
            if (!Enabled || !RegionMap.Ready) return true;
            int region = RegionMap.RegionAt(centre.x, centre.z);
            if (!CachedOrigin(em, faction, region, out float3 origin, out Layout layout)) return true;
            var snap = BuildSiteSnapshot.Current(em);
            Aabb(centre, size, out float2 mn, out float2 mx);
            for (int i = 0; i < layout.Slots.Count; i++)
            {
                if (i == exceptSlot) continue;
                var s = layout.Slots[i];
                float3 p = SlotPosition(faction, region, layout, i, origin);
                Aabb(p, s.Size, out float2 smn, out float2 smx);
                if (!(mn.x < smx.x && mx.x > smn.x && mn.y < smx.y && mx.y > smn.y)) continue;
                if (IsFilled(snap, faction, p, s.Size)) continue;
                return false;
            }
            return true;
        }
    }
}
