// CurseGroundSystem.cs
// CURSED GROUND (Territory_Claims.md §6.9, 2026-10-16): the field the curse
// spreads over the map — the ONE answer to "is this ground cursed" for the
// hostile-ground debuff, the claim rule, the terrain crystal, the crystal
// shelf, the minimap and the AI's routes.
//
// A cell (one 2 m build cell) is clean or cursed; a cursed cell remembers the
// curse node that FEEDS it (by the node's cell index, which is stable across
// saves where entity indices are not).
//
//   * a node curses its nodeAuraRadius circle at once when it rises;
//   * every groundCreepSeconds the front creeps — each clean passable cell
//     beside cursed ground turns cursed with groundCreepChance, varied by a
//     seeded coarse noise so the edge grows in fingers — up to
//     groundCreepMaxDistance from the feeding node;
//   * standing curse units stain the cells around them (no distance limit);
//   * a sacrifice curses a disk where the creature died (CurseDisk);
//   * when a node dies its ground is orphaned: adopted by a neighbouring cell
//     still fed by a living node, else receding from the clean edge inward;
//   * (2026-10-18) in a territory OWNED by a faction that has chosen a
//     culture other than Runai, cursed ground fades from its clean edge
//     inward the same way, even while its node lives -- but never inside a
//     living node's own circle (section 6.9.8);
//   * (2026-10-18) a Litharch's Cleanse keeps a growing circle free of curse
//     and thins a falloff band around it (section 6.9.7, LitharchCleanse),
//     applied after the step's creep and stains so the circle always ends clean.
//
// Simulation state: lockstep clock only, integer-hashed chances, fixed scan
// order, every change applied after the scan (so a pass never reads its own
// writes). The checksum goes into the lockstep state hash; the arrays are
// plain fields, which SnapshotState saves with the system.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.World.Regions;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(VeilExposureSystem))]
    public partial class CurseGroundSystem : SystemBase
    {
        /// <summary>Cell size in metres: one build cell.</summary>
        public const float CellSize = BuildGrid.CellSize;
        private const int NoiseCell = 5;         // cells per creep-noise lattice step (10 m)

        private static CurseGroundSystem _instance;

        // ── State (saved with the system) ───────────────────────────────
        private bool _ready;
        private int _epoch = int.MinValue;
        private int _w, _h;
        private float _minX, _minZ;
        private int[] _feed;          // 0 = clean, else feeding node's cell index + 1
        private int[] _region;        // territory cache: int.MinValue unknown
        private int[] _regionCursed;  // cursed cells per territory
        private int[] _knownNodes = System.Array.Empty<int>();   // sorted node keys seen alive
        private int _step;
        private float _clock;
        private int _version;
        private uint _checksum;
        private uint _seed;

        private EntityQuery _nodeQuery;
        private EntityQuery _curseUnitQuery;
        private readonly byte[] _cultures = new byte[64];
        private bool[] _fades = System.Array.Empty<bool>();
        private readonly List<int> _changeIdx = new List<int>(1024);
        private readonly List<int> _changeFeed = new List<int>(1024);

        // ── Public read API ──────────────────────────────────────────────

        /// <summary>The field exists this match.</summary>
        public static bool Ready => _instance != null && _instance._ready;
        /// <summary>Bumped on every change; presentation repaints on it.</summary>
        public static int Version => _instance != null ? _instance._version : 0;
        /// <summary>Order-independent hash of every cursed cell and its feed.</summary>
        public static uint Checksum => _instance != null && _instance._ready ? _instance._checksum : 0u;
        public static int Width => _instance != null ? _instance._w : 0;
        public static int Height => _instance != null ? _instance._h : 0;
        public static float2 Origin => _instance != null ? new float2(_instance._minX, _instance._minZ) : float2.zero;

        /// <summary>Is the cell (x, y) cursed? Out of range is clean.</summary>
        public static bool CursedCell(int x, int y)
        {
            var s = _instance;
            if (s == null || !s._ready || (uint)x >= (uint)s._w || (uint)y >= (uint)s._h) return false;
            return s._feed[y * s._w + x] != 0;
        }

        /// <summary>Is the ground at world (x, z) cursed?</summary>
        public static bool IsCursed(float x, float z)
        {
            var s = _instance;
            if (s == null || !s._ready) return false;
            int cx = (int)math.floor((x - s._minX) / CellSize);
            int cz = (int)math.floor((z - s._minZ) / CellSize);
            return CursedCell(cx, cz);
        }

        /// <summary>Does territory <paramref name="t"/> have ANY cursed ground
        /// (§6.9.4: then it cannot gain claim for a player)?</summary>
        public static bool TerritoryHasCurse(int t)
        {
            var s = _instance;
            return s != null && s._ready && s._regionCursed != null
                   && (uint)t < (uint)s._regionCursed.Length && s._regionCursed[t] > 0;
        }

        /// <summary>
        /// Curse every passable cell within <paramref name="radius"/> of
        /// <paramref name="pos"/> at once — a sacrifice (§6.9.5). Fed by the
        /// nearest living curse node; with none alive, nothing happens (there
        /// is no node for the ground to belong to). Called from the
        /// simulation (the ability executor), so it is deterministic.
        /// </summary>
        public static void CurseDisk(float3 pos, float radius)
        {
            var s = _instance;
            if (s == null || !s._ready || radius <= 0f) return;
            s.StampDisk(pos.x, pos.z, radius, nodeKey: -1);
        }

        /// <summary>
        /// Clear every cursed cell within <paramref name="radius"/> of
        /// <paramref name="pos"/> at once -- a Litharch's Cleanse landing
        /// (section 6.9.7), with its <paramref name="falloff"/> band thinned
        /// at once too (2026-10-19: the cast is immediate). Called from the
        /// simulation (the ability executor), so it is deterministic.
        /// </summary>
        public static void ClearDisk(float3 pos, float radius, float falloff = 0f)
        {
            var s = _instance;
            if (s == null || !s._ready || radius <= 0f) return;
            s.ClearCircle(pos.x, pos.z, radius, falloff);
        }

        /// <summary>
        /// The cursed cell nearest <paramref name="from"/> within
        /// <paramref name="radius"/> whose territory passes
        /// <paramref name="territoryOk"/> — where an AI Litharch should aim
        /// its Cleanse. A bounded window, scanned in a fixed order (ties go to
        /// the first found), so every peer picks the same cell.
        /// </summary>
        public static bool TryNearestCursed(float3 from, float radius, System.Func<int, bool> territoryOk,
                                            out float3 cell)
        {
            cell = default;
            var s = _instance;
            if (s == null || !s._ready || radius <= 0f) return false;
            int r = (int)math.ceil(radius / CellSize);
            int ux = (int)math.floor((from.x - s._minX) / CellSize);
            int uy = (int)math.floor((from.z - s._minZ) / CellSize);
            float best = radius * radius;
            int bestI = -1;
            for (int y = math.max(0, uy - r); y <= math.min(s._h - 1, uy + r); y++)
            for (int x = math.max(0, ux - r); x <= math.min(s._w - 1, ux + r); x++)
            {
                int i = y * s._w + x;
                if (s._feed[i] == 0) continue;
                float ox = s._minX + (x + 0.5f) * CellSize - from.x, oz = s._minZ + (y + 0.5f) * CellSize - from.z;
                float d2 = ox * ox + oz * oz;
                if (d2 >= best) continue;
                if (territoryOk != null && !territoryOk(s.RegionOf(i))) continue;
                best = d2;
                bestI = i;
            }
            if (bestI < 0) return false;
            cell = new float3(s._minX + (bestI % s._w + 0.5f) * CellSize, from.y,
                              s._minZ + (bestI / s._w + 0.5f) * CellSize);
            return true;
        }

        // ── Lifecycle ────────────────────────────────────────────────────

        protected override void OnCreate()
        {
            _instance = this;
            _nodeQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<SmallNodeTag, FactionTag, LocalTransform, Health>()
                .Build(this);
            _curseUnitQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<BorderUnitTag, LocalTransform, Health>()
                .Build(this);
        }

        protected override void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        protected override void OnUpdate()
        {
            _instance = this;
            var lockstep = TheWaningBorder.Multiplayer.LockstepManager.Instance;
            if (lockstep != null && !lockstep.IsSimulationRunning) return;

            // A new match in the same process: everything starts over.
            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                _ready = false;
            }
            if (!_ready && !TryInit()) return;

            var cfg = TheWaningBorder.Data.Border.BorderSettings.Get();
            float interval = cfg != null ? math.max(0.5f, cfg.groundCreepSeconds) : 2f;
            _clock += SystemAPI.Time.DeltaTime;
            if (_clock < interval) return;
            _clock -= interval;
            _step++;
            Step(cfg);
        }

        private bool TryInit()
        {
            if (!RegionMap.Ready || !TheWaningBorder.Influence.PlayerInfluenceMap.Ready) return false;
            // Passable() reads the terrain mask: wait for it, so the first
            // stamp never lands on a mountain on one peer and not another.
            var grid = TheWaningBorder.World.Terrain.PassabilityGrid.Instance;
            if (grid != null && !grid.IsMaskReady) return false;
            var min = TheWaningBorder.Influence.PlayerInfluenceMap.WorldMin;
            var size = TheWaningBorder.Influence.PlayerInfluenceMap.WorldSize;
            if (size.x <= 0f || size.y <= 0f) return false;

            _minX = min.x;
            _minZ = min.y;
            _w = math.max(1, (int)math.ceil(size.x / CellSize));
            _h = math.max(1, (int)math.ceil(size.y / CellSize));
            int n = _w * _h;
            _feed = new int[n];
            _region = new int[n];
            for (int i = 0; i < n; i++) _region[i] = int.MinValue;
            _regionCursed = new int[math.max(1, RegionMap.Count)];
            _knownNodes = System.Array.Empty<int>();
            _step = 0;
            _clock = 0f;
            _checksum = 0u;
            _version++;
            _seed = (uint)GameSettings.SpawnSeed ^ 0x9E3779B9u;
            _ready = true;
            UnityEngine.Debug.Log($"[CurseGround] field ready: {_w}x{_h} cells of {CellSize} m from ({_minX:F0},{_minZ:F0}).");
            return true;
        }

        // ── One step ─────────────────────────────────────────────────────

        private void Step(TheWaningBorder.Data.Border.BorderSettingsSO cfg)
        {
            float radius = cfg != null ? cfg.nodeAuraRadius : 20f;
            float creepChance = cfg != null ? cfg.groundCreepChance : 0.08f;
            float creepMax = cfg != null ? cfg.groundCreepMaxDistance : 40f;
            float stainRadius = cfg != null ? cfg.groundStainRadius : 3f;
            float stainChance = cfg != null ? cfg.groundStainChance : 0.35f;
            float recedeChance = cfg != null ? cfg.groundRecedeChance : 0.1f;

            // Living curse nodes, by cell key, in key order.
            var nodes = LivingNodes(out var nodePos);

            // 1. A node that rose since the last step curses its circle at once.
            for (int k = 0; k < nodes.Length; k++)
                if (System.Array.BinarySearch(_knownNodes, nodes[k]) < 0)
                    StampDisk(nodePos[k].x, nodePos[k].y, radius, nodes[k]);
            _knownNodes = nodes;

            // Territories whose owner has chosen a culture other than Runai:
            // their cursed ground fades from the edge (section 6.9.8).
            ComputeFades();
            float auraSq = radius * radius;

            // 2. Creep, adoption and receding: one scan, changes applied after.
            uint creepQ = ToQ16(creepChance);
            uint recedeQ = ToQ16(recedeChance);
            float creepMaxSq = creepMax * creepMax;
            _changeIdx.Clear();
            _changeFeed.Clear();

            for (int y = 0; y < _h; y++)
            for (int x = 0; x < _w; x++)
            {
                int i = y * _w + x;
                int f = _feed[i];
                if (f != 0)
                {
                    if (IsAlive(f - 1))
                    {
                        // Owned by a cultured faction: the clean edge eats inward,
                        // but never inside the living node's own circle.
                        int t = RegionOf(i);
                        if ((uint)t < (uint)_fades.Length && _fades[t] && TouchesClean(x, y)
                            && NodeDistSq(x, y, f - 1) > auraSq && Roll(x, y, 5u) < recedeQ)
                        { _changeIdx.Add(i); _changeFeed.Add(0); }
                        continue;
                    }
                    // Orphaned: adopt a living neighbour's feed, else recede at the clean edge.
                    int adopt = LivingNeighbourFeed(x, y);
                    if (adopt != 0) { _changeIdx.Add(i); _changeFeed.Add(adopt); continue; }
                    if (TouchesClean(x, y) && Roll(x, y, 2u) < recedeQ)
                    { _changeIdx.Add(i); _changeFeed.Add(0); }
                    continue;
                }

                int g = LivingNeighbourFeed(x, y);
                if (g == 0 || !Passable(i, x, y)) continue;
                int node = g - 1;
                float cx = _minX + (x + 0.5f) * CellSize - (_minX + ((node % _w) + 0.5f) * CellSize);
                float cz = _minZ + (y + 0.5f) * CellSize - (_minZ + ((node / _w) + 0.5f) * CellSize);
                if (cx * cx + cz * cz > creepMaxSq) continue;
                uint chance = (uint)((ulong)creepQ * (uint)(64 + Noise(x, y)) / 192u);
                if (Roll(x, y, 1u) < chance) { _changeIdx.Add(i); _changeFeed.Add(g); }
            }

            // 3. Standing curse units stain the ground around them.
            Stain(stainRadius, ToQ16(stainChance));

            for (int c = 0; c < _changeIdx.Count; c++) SetFeed(_changeIdx[c], _changeFeed[c]);

            // 4. Litharchs' Cleanse: last, so a channelled circle ends every step clean.

            if (_step % 30 == 1)
            {
                int cursed = 0;
                for (int i = 0; i < _feed.Length; i++) if (_feed[i] != 0) cursed++;
                UnityEngine.Debug.Log($"[CurseGround] step {_step}: {cursed} cursed cells, {nodes.Length} living node(s).");
            }
        }

        /// <summary>Per territory: is it owned by a faction whose chosen
        /// culture lets its ground shed the curse (any culture but Runai --
        /// the Runai make their peace with the curse instead)?</summary>
        private void ComputeFades()
        {
            int n = RegionMap.Count;
            if (_fades.Length != n) _fades = new bool[n];
            CultureConfig.GetCompletedCultures(EntityManager, _cultures);
            for (int t = 0; t < n; t++)
            {
                int owner = TerritoryOwnership.OwnerOf(t);
                byte culture = owner >= 0 && owner < _cultures.Length ? _cultures[owner] : Cultures.None;
                _fades[t] = culture != Cultures.None && culture != Cultures.Runai;
            }
        }

        /// <summary>Clear the cursed cells within <paramref name="radius"/>; in
        /// the <paramref name="falloff"/> band beyond it, clear each with a
        /// chance falling from 1 at the circle to 0 at the band's outer edge.</summary>
        private void ClearCircle(float px, float pz, float radius, float falloff)
        {
            float outer = radius + math.max(0f, falloff);
            int r = (int)math.ceil(outer / CellSize);
            float in2 = radius * radius, out2 = outer * outer;
            int ux = (int)math.floor((px - _minX) / CellSize);
            int uy = (int)math.floor((pz - _minZ) / CellSize);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = ux + dx, y = uy + dy;
                if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) continue;
                int i = y * _w + x;
                if (_feed[i] == 0) continue;
                float ox = _minX + (x + 0.5f) * CellSize - px, oz = _minZ + (y + 0.5f) * CellSize - pz;
                float d2 = ox * ox + oz * oz;
                if (d2 > out2) continue;
                if (d2 > in2)
                {
                    // 0 at the circle, 1 at the band's outer edge
                    float keep = (math.sqrt(d2) - radius) / math.max(0.001f, falloff);
                    if (Roll(x, y, 6u) >= ToQ16(1f - keep)) continue;
                }
                SetFeed(i, 0);
            }
        }

        /// <summary>Squared distance (m) from cell (x, y) to the node keyed
        /// <paramref name="nodeKey"/> (its cell index).</summary>
        private float NodeDistSq(int x, int y, int nodeKey)
        {
            float dx = (x - nodeKey % _w) * CellSize, dz = (y - nodeKey / _w) * CellSize;
            return dx * dx + dz * dz;
        }

        private int[] LivingNodes(out float2[] positions)
        {
            using var xfs = _nodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var facs = _nodeQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = _nodeQuery.ToComponentDataArray<Health>(Allocator.Temp);
            var keys = new List<int>(xfs.Length);
            var pos = new List<float2>(xfs.Length);
            for (int i = 0; i < xfs.Length; i++)
            {
                if (facs[i].Value != Faction.Border || hps[i].Value <= 0) continue;
                int key = CellIndex(xfs[i].Position.x, xfs[i].Position.z);
                if (key < 0) continue;
                keys.Add(key);
                pos.Add(new float2(xfs[i].Position.x, xfs[i].Position.z));
            }
            // Sort by key so the result does not depend on chunk order.
            var order = new int[keys.Count];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            System.Array.Sort(order, (a, b) => keys[a].CompareTo(keys[b]));
            var outKeys = new int[order.Length];
            positions = new float2[order.Length];
            int n = 0;
            for (int i = 0; i < order.Length; i++)
            {
                if (n > 0 && outKeys[n - 1] == keys[order[i]]) continue;   // two nodes in one cell
                outKeys[n] = keys[order[i]];
                positions[n] = pos[order[i]];
                n++;
            }
            if (n != outKeys.Length)
            {
                System.Array.Resize(ref outKeys, n);
                System.Array.Resize(ref positions, n);
            }
            return outKeys;
        }

        private void Stain(float radius, uint chanceQ)
        {
            if (radius <= 0f || chanceQ == 0u || _knownNodes.Length == 0) return;
            using var ents = _curseUnitQuery.ToEntityArray(Allocator.Temp);
            using var xfs = _curseUnitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var hps = _curseUnitQuery.ToComponentDataArray<Health>(Allocator.Temp);
            // Unit order follows the query; each unit's cells are rolled with
            // cell-and-step hashes, and the change list resolves duplicates by
            // first write — so the result is order-independent anyway.
            var em = EntityManager;
            int r = (int)math.ceil(radius / CellSize);
            float r2 = radius * radius;
            for (int u = 0; u < ents.Length; u++)
            {
                if (hps[u].Value <= 0 || !IsStanding(em, ents[u])) continue;
                float3 p = xfs[u].Position;
                int ux = (int)math.floor((p.x - _minX) / CellSize);
                int uy = (int)math.floor((p.z - _minZ) / CellSize);
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    int x = ux + dx, y = uy + dy;
                    if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) continue;
                    float ox = _minX + (x + 0.5f) * CellSize - p.x, oz = _minZ + (y + 0.5f) * CellSize - p.z;
                    if (ox * ox + oz * oz > r2) continue;
                    int i = y * _w + x;
                    if (_feed[i] != 0 || !Passable(i, x, y)) continue;
                    if (Roll(x, y, 3u) >= chanceQ) continue;
                    int g = LivingNeighbourFeed(x, y);
                    if (g == 0) g = NearestNodeKey(x, y) + 1;
                    if (g == 0) continue;
                    _changeIdx.Add(i);
                    _changeFeed.Add(g);
                }
            }
        }

        /// <summary>§6.2's standing rule: idle, or ordered somewhere within
        /// a few metres of where it is (a unit marching through stains nothing).</summary>
        private static bool IsStanding(EntityManager em, Entity e)
        {
            float3 here = em.GetComponentData<LocalTransform>(e).Position;
            float3 dest;
            if (em.HasComponent<MoveCommand>(e) && em.IsComponentEnabled<MoveCommand>(e))
                dest = em.GetComponentData<MoveCommand>(e).Destination;
            else if (em.HasComponent<DesiredDestination>(e) && em.GetComponentData<DesiredDestination>(e).Has != 0)
                dest = em.GetComponentData<DesiredDestination>(e).Position;
            else
                return true;
            float dx = dest.x - here.x, dz = dest.z - here.z;
            return dx * dx + dz * dz < 36f;
        }

        // ── Cell helpers ─────────────────────────────────────────────────

        private int CellIndex(float x, float z)
        {
            int cx = (int)math.floor((x - _minX) / CellSize);
            int cz = (int)math.floor((z - _minZ) / CellSize);
            if ((uint)cx >= (uint)_w || (uint)cz >= (uint)_h) return -1;
            return cz * _w + cx;
        }

        private bool IsAlive(int nodeKey) => System.Array.BinarySearch(_knownNodes, nodeKey) >= 0;

        /// <summary>Feed (key + 1) of a 4-neighbour fed by a LIVING node, in a
        /// fixed order (W, E, S, N); 0 when there is none.</summary>
        private int LivingNeighbourFeed(int x, int y)
        {
            int f;
            if (x > 0 && (f = _feed[y * _w + x - 1]) != 0 && IsAlive(f - 1)) return f;
            if (x < _w - 1 && (f = _feed[y * _w + x + 1]) != 0 && IsAlive(f - 1)) return f;
            if (y > 0 && (f = _feed[(y - 1) * _w + x]) != 0 && IsAlive(f - 1)) return f;
            if (y < _h - 1 && (f = _feed[(y + 1) * _w + x]) != 0 && IsAlive(f - 1)) return f;
            return 0;
        }

        private bool TouchesClean(int x, int y)
        {
            if (x == 0 || y == 0 || x == _w - 1 || y == _h - 1) return true;
            return _feed[y * _w + x - 1] == 0 || _feed[y * _w + x + 1] == 0
                || _feed[(y - 1) * _w + x] == 0 || _feed[(y + 1) * _w + x] == 0;
        }

        private int NearestNodeKey(int x, int y)
        {
            int best = -1;
            long bestD = long.MaxValue;
            for (int k = 0; k < _knownNodes.Length; k++)
            {
                int key = _knownNodes[k];
                long dx = (key % _w) - x, dy = (key / _w) - y;
                long d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = key; }
            }
            return best;
        }

        /// <summary>Unclaimable ground (no territory) is never cursed, and
        /// neither is UNWALKABLE ground inside a territory (2026-10-17):
        /// mountains, cliff faces, deep water, forest — anything the
        /// PassabilityGrid's terrain mask blocks. The terrain mask is built once
        /// from the map, so every peer reads the same answer. Buildings are no
        /// obstacle (their cells are stamped, not terrain-blocked).</summary>
        private bool Passable(int i, int x, int y)
        {
            if (RegionOf(i) == RegionMap.None) return false;
            var grid = TheWaningBorder.World.Terrain.PassabilityGrid.Instance;
            if (grid == null || !grid.IsMaskReady) return true;
            var p = new float3(_minX + (x + 0.5f) * CellSize, 0f, _minZ + (y + 0.5f) * CellSize);
            return grid.GetCell(grid.WorldToCell(p)) != TheWaningBorder.World.Terrain.PassabilityGrid.TerrainBlocked;
        }

        private int RegionOf(int i)
        {
            int r = _region[i];
            if (r == int.MinValue)
            {
                int x = i % _w, y = i / _w;
                r = RegionMap.RegionAt(_minX + (x + 0.5f) * CellSize, _minZ + (y + 0.5f) * CellSize);
                _region[i] = r;
            }
            return r;
        }

        private void StampDisk(float px, float pz, float radius, int nodeKey)
        {
            int key = nodeKey >= 0 ? nodeKey
                : NearestNodeKey((int)math.floor((px - _minX) / CellSize), (int)math.floor((pz - _minZ) / CellSize));
            if (key < 0) return;
            int r = (int)math.ceil(radius / CellSize);
            float r2 = radius * radius;
            int ux = (int)math.floor((px - _minX) / CellSize);
            int uy = (int)math.floor((pz - _minZ) / CellSize);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int x = ux + dx, y = uy + dy;
                if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) continue;
                float ox = _minX + (x + 0.5f) * CellSize - px, oz = _minZ + (y + 0.5f) * CellSize - pz;
                if (ox * ox + oz * oz > r2) continue;
                int i = y * _w + x;
                int f = _feed[i];
                if (f != 0 && IsAlive(f - 1)) continue;   // never steal a living node's ground
                if (!Passable(i, x, y)) continue;
                SetFeed(i, key + 1);
            }
        }

        private void SetFeed(int i, int feed)
        {
            int old = _feed[i];
            if (old == feed) return;
            unchecked
            {
                if (old != 0) _checksum ^= Mix((uint)i, (uint)old);
                if (feed != 0) _checksum ^= Mix((uint)i, (uint)feed);
            }
            int t = RegionOf(i);
            if ((uint)t < (uint)_regionCursed.Length)
            {
                if (old == 0 && feed != 0) _regionCursed[t]++;
                else if (old != 0 && feed == 0) _regionCursed[t]--;
            }
            _feed[i] = feed;
            _version++;
        }

        // ── Deterministic randomness ─────────────────────────────────────

        private static uint ToQ16(float chance) => (uint)math.clamp((int)(chance * 65536f), 0, 65536);

        private uint Roll(int x, int y, uint salt) =>
            Mix(Mix((uint)x, (uint)y), (uint)_step * 2654435761u ^ salt ^ _seed) & 0xFFFFu;

        /// <summary>Coarse value noise, 0..255, bilinear between lattice
        /// points NoiseCell cells apart — the organic variation of the creep.</summary>
        private int Noise(int x, int y)
        {
            int gx = x / NoiseCell, gy = y / NoiseCell;
            int fx = x % NoiseCell, fy = y % NoiseCell;
            int a = (int)(Mix((uint)gx, (uint)gy ^ _seed) & 255u);
            int b = (int)(Mix((uint)(gx + 1), (uint)gy ^ _seed) & 255u);
            int c = (int)(Mix((uint)gx, (uint)(gy + 1) ^ _seed) & 255u);
            int d = (int)(Mix((uint)(gx + 1), (uint)(gy + 1) ^ _seed) & 255u);
            int top = a * (NoiseCell - fx) + b * fx;
            int bot = c * (NoiseCell - fx) + d * fx;
            return (top * (NoiseCell - fy) + bot * fy) / (NoiseCell * NoiseCell);
        }

        private static uint Mix(uint a, uint b)
        {
            unchecked
            {
                uint h = a * 0x85EBCA6Bu ^ (b + 0x9E3779B9u + (a << 6) + (a >> 2));
                h ^= h >> 16; h *= 0x7FEB352Du;
                h ^= h >> 15; h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }
    }
}
