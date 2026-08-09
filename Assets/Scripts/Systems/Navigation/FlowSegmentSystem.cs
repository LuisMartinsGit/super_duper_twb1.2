// FlowSegmentSystem.cs
// task-112 M3 -- per-tile segmented flow generation with LRU caching.
//
// Replaces the M1 WholeMapFlowSystem -- M3 deletes the whole-map flow
// generator (and the NavFlowFieldM1 singleton) per the architecture.
// For each unit that holds a NavPathResult, the system ensures the
// current tile + the next-tile-along-the-path are present in the
// NavFlowCache. On miss, an integrate + write job is scheduled to
// compute the tile-local integration field (Dijkstra back from the
// exit-portal cell) and the per-cell direction byte.
//
// The cache is a fixed-size pool of 256 slabs (16x16 cells each).
// Eviction is LRU keyed by tick counter; slot picked = the live slot
// with the lowest LastUsedTick.
//
// Determinism:
//   * Tick counter is sim-tick driven (incremented at OnUpdate start),
//     not wall-clock.
//   * Integration sweep is a single-thread Burst IJob per missing tile
//     (per-request parallelism comes from scheduling multiple
//     IntegrateTileJobs in flight at once -- safe because each writes
//     to a disjoint slab in the pool).
//   * Hash key = (TileIndex << 16) | (ExitPortalId << 8) | ProfileHash --
//     integer only.
//
// Location: Assets/Scripts/Systems/Navigation/FlowSegmentSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Owns the <see cref="NavFlowCache"/> singleton. Allocates the slab
    /// pool at first OnUpdate; disposes in OnDestroy.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(AbstractPathfinderSystem))]
    public partial struct FlowSegmentSystem : ISystem
    {
        private Entity _cacheEntity;
        private byte _initialised;
        private EntityQuery _pathQuery;

        // true = the per-leg slab integration in OnUpdate is retired
        // (pathfinding redesign 2026-07-05). static readonly, NOT const:
        // a const guard makes the retired loop provably unreachable, and
        // the resulting CS0162 cannot be pragma-suppressed — Entities
        // source-gen re-emits the method body into a generated file
        // without pragmas.
        private static readonly bool SlabIntegrationRetired = true;

        // NOT [BurstCompile]: BC1028 -- CreateEntity is managed.
        public void OnCreate(ref SystemState state)
        {
            _initialised = 0;
            state.RequireForUpdate<PortalGraphSingleton>();
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();

            _pathQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, NavPathResult, NavPathPortal>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            if (_initialised == 0)
            {
                _initialised = 1;
                int tileArea = PortalGraphSingleton.TileSize * PortalGraphSingleton.TileSize;
                int slots = NavFlowCache.DefaultSlotCount;

                var cache = new NavFlowCache
                {
                    SlotIndex = new NativeHashMap<NavFlowCacheKey, int>(slots * 2, Allocator.Persistent),
                    Slots = new NativeArray<NavFlowCacheSlot>(slots, Allocator.Persistent,
                        NativeArrayOptions.ClearMemory),
                    SlotKeys = new NativeArray<NavFlowCacheKey>(slots, Allocator.Persistent,
                        NativeArrayOptions.ClearMemory),
                    DirPool = new NativeArray<byte>(slots * tileArea, Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory),
                    IntegrationPool = new NativeArray<uint>(slots * tileArea, Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory),
                    SlotCount = slots,
                    TileArea = tileArea,
                    TickCounter = 0,
                };

                _cacheEntity = em.CreateEntity(typeof(NavFlowCache));
                em.SetComponentData(_cacheEntity, cache);
            }

            // RETIRED (pathfinding redesign 2026-07-05): per-leg slab
            // integration is no longer consumed — FlowFollowSystem samples
            // the whole-map goal fields from GoalFlowFieldSystem instead.
            // The NavFlowCache singleton stays allocated (created above)
            // because IncrementalPortalRebuildSystem still evicts against
            // it; the per-unit integrate loop below is skipped entirely.
            if (SlabIntegrationRetired) return;

            if (_pathQuery.IsEmpty) return;

            // Pull singletons + bump tick counter.
            var cacheSingleton = SystemAPI.GetSingleton<NavFlowCache>();
            cacheSingleton.TickCounter++;
            SystemAPI.SetSingleton(cacheSingleton);

            var portalSingleton = SystemAPI.GetSingleton<PortalGraphSingleton>();
            if (portalSingleton.Built == 0) return;

            var cost = SystemAPI.GetSingleton<NavCostField>();
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();

            // Drain in-flight nav dependencies before we read shared state.
            state.Dependency.Complete();

            // For each unit, ensure (currentTile, exitPortalId, profile) is
            // present in the cache; if not, integrate + cache.
            //
            // We iterate the entities snapshot deterministically (entity
            // order from the query is chunk-walk order, which is stable
            // across machines per project memory).
            using var entities = _pathQuery.ToEntityArray(Allocator.Temp);
            using var transforms = _pathQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var results = _pathQuery.ToComponentDataArray<NavPathResult>(Allocator.Temp);

            ref var graph = ref portalSingleton.Graph.Value;
            int tileSize = graph.TileSize;
            int tilesX = graph.TilesX;

            for (int i = 0; i < entities.Length; i++)
            {
                var res = results[i];
                if (res.Status != NavPathRequest.StatusSuccess) continue;
                if (res.Generation != portalSingleton.Generation) continue;

                var buf = em.GetBuffer<NavPathPortal>(entities[i], true);
                if (buf.Length <= res.CurrentPortalIndex + 1) continue;

                int nextPortalIdx = res.CurrentPortalIndex + 1;
                if (nextPortalIdx >= buf.Length) continue;
                int nextPortalId = buf[nextPortalIdx].PortalId;

                // Virtual nodes (start/goal) live at id >= realNodeCount.
                // For caching purposes we use the portal id as-is (positive
                // virtual ids still hash deterministically); the integrate
                // job needs a cell index for the exit -- derive that.
                int exitCellIndex = ResolveExitCellIndex(ref graph, in grid, nextPortalId,
                    buf, res, entities[i], em);
                if (exitCellIndex < 0) continue;

                int exitX = exitCellIndex % grid.Width;
                int exitZ = exitCellIndex / grid.Width;
                int tileX = exitX / tileSize;
                int tileZ = exitZ / tileSize;
                // M3 caches the slab keyed by the tile the EXIT portal sits
                // on (== where the unit is heading next). Same-tile follows
                // can reuse a slab for any unit pointed at the same exit.
                int tileIndex = tileZ * tilesX + tileX;

                var key = new NavFlowCacheKey
                {
                    TileIndex = tileIndex,
                    ExitPortalId = nextPortalId,
                    ProfileHash = 0,
                };

                int slot;
                if (cacheSingleton.SlotIndex.TryGetValue(key, out slot))
                {
                    // Cache HIT -- bump LastUsedTick.
                    var s = cacheSingleton.Slots[slot];
                    s.LastUsedTick = cacheSingleton.TickCounter;
                    cacheSingleton.Slots[slot] = s;
                    continue;
                }

                // Cache MISS -- allocate a slot (or evict LRU) + integrate.
                slot = AllocateSlot(ref cacheSingleton);
                IntegrateTile(ref graph, in cost, in grid, ref cacheSingleton, slot, key,
                    tileX, tileZ, exitX, exitZ);
            }

            // Persist the bumped slot metadata back to the singleton.
            SystemAPI.SetSingleton(cacheSingleton);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_initialised == 0) return;
            var em = state.EntityManager;
            if (em.Exists(_cacheEntity) && em.HasComponent<NavFlowCache>(_cacheEntity))
            {
                var c = em.GetComponentData<NavFlowCache>(_cacheEntity);
                if (c.SlotIndex.IsCreated) c.SlotIndex.Dispose();
                if (c.Slots.IsCreated) c.Slots.Dispose();
                if (c.SlotKeys.IsCreated) c.SlotKeys.Dispose();
                if (c.DirPool.IsCreated) c.DirPool.Dispose();
                if (c.IntegrationPool.IsCreated) c.IntegrationPool.Dispose();
            }
        }

        // Find the cell index a portal id corresponds to. Virtual nodes
        // (id >= realNodeCount) need to consult the unit's path buffer to
        // recover their cell (they were created with the unit's start /
        // goal cell at solve time, but the cache doesn't have access to
        // that history -- we reconstruct from the request's StartCell /
        // GoalCell if those are still present).
        private static int ResolveExitCellIndex(
            ref PortalGraphBlob graph,
            in NavGridSingleton grid,
            int portalId,
            in DynamicBuffer<NavPathPortal> buf,
            in NavPathResult res,
            Entity entity,
            EntityManager em)
        {
            if (portalId < graph.Nodes.Length)
                return graph.Nodes[portalId].CellIndex;

            // Virtual node -- can be start or goal. The path always has
            // start as buf[0] and goal as buf[Length-1]. If the requested
            // portal is the last, it's the goal virtual; if it's index 0,
            // it's the start virtual.
            if (portalId == graph.Nodes.Length + 1 /* goal */)
            {
                // We don't carry GoalCell on the result. Fall back to the
                // unit's MoveCommand or DesiredDestination if available.
                if (em.HasComponent<DesiredDestination>(entity))
                {
                    var dd = em.GetComponentData<DesiredDestination>(entity);
                    int cx = (int)math.floor((dd.Position.x - grid.Origin.x) / grid.CellSize);
                    int cz = (int)math.floor((dd.Position.z - grid.Origin.z) / grid.CellSize);
                    cx = math.clamp(cx, 0, grid.Width - 1);
                    cz = math.clamp(cz, 0, grid.Height - 1);
                    return cz * grid.Width + cx;
                }
            }
            // startVirtual or unresolved -- pick the unit's current cell.
            if (em.HasComponent<LocalTransform>(entity))
            {
                var t = em.GetComponentData<LocalTransform>(entity);
                int cx = (int)math.floor((t.Position.x - grid.Origin.x) / grid.CellSize);
                int cz = (int)math.floor((t.Position.z - grid.Origin.z) / grid.CellSize);
                cx = math.clamp(cx, 0, grid.Width - 1);
                cz = math.clamp(cz, 0, grid.Height - 1);
                return cz * grid.Width + cx;
            }
            return -1;
        }

        // Picks a free slot if any, else evicts the LRU. Marks the chosen
        // slot Valid and records the key in SlotKeys; the caller is
        // responsible for filling the pool data + inserting the index.
        private static int AllocateSlot(ref NavFlowCache cache)
        {
            // Pass 1: free slot scan.
            for (int i = 0; i < cache.SlotCount; i++)
            {
                if (cache.Slots[i].Valid == 0)
                {
                    return InitSlot(ref cache, i);
                }
            }
            // Pass 2: LRU scan -- smallest LastUsedTick wins. Ties break
            // by smallest slot index (DR-12-shaped: deterministic).
            int lru = 0;
            int lruTick = cache.Slots[0].LastUsedTick;
            for (int i = 1; i < cache.SlotCount; i++)
            {
                int t = cache.Slots[i].LastUsedTick;
                if (t < lruTick)
                {
                    lru = i;
                    lruTick = t;
                }
            }
            // Evict.
            cache.SlotIndex.Remove(cache.SlotKeys[lru]);
            return InitSlot(ref cache, lru);
        }

        private static int InitSlot(ref NavFlowCache cache, int idx)
        {
            int tileArea = cache.TileArea;
            var s = new NavFlowCacheSlot
            {
                DirOffset = idx * tileArea,
                IntegrationOffset = idx * tileArea,
                LastUsedTick = cache.TickCounter,
                Valid = 1,
            };
            cache.Slots[idx] = s;
            return idx;
        }

        // Schedules / runs a per-tile integration sweep. M3 ships this
        // inline (Burst Schedule().Complete()) to keep the iteration
        // deterministic for the cache tests; later phases can batch
        // multiple misses into one parallel kick.
        private static void IntegrateTile(
            ref PortalGraphBlob graph,
            in NavCostField cost,
            in NavGridSingleton grid,
            ref NavFlowCache cache,
            int slot,
            NavFlowCacheKey key,
            int tileX,
            int tileZ,
            int exitX,
            int exitZ)
        {
            int tileSize = graph.TileSize;
            int tileArea = cache.TileArea;
            int x0 = tileX * tileSize;
            int z0 = tileZ * tileSize;

            // Local goal in tile-local coordinates.
            int goalLocalX = exitX - x0;
            int goalLocalZ = exitZ - z0;

            var job = new IntegrateTileJob
            {
                Cost = cost.Cost,
                IntegrationPool = cache.IntegrationPool,
                DirPool = cache.DirPool,
                IntegrationOffset = slot * tileArea,
                DirOffset = slot * tileArea,
                TileSize = tileSize,
                GridWidth = grid.Width,
                GridHeight = grid.Height,
                TileX0 = x0,
                TileZ0 = z0,
                GoalLocalX = goalLocalX,
                GoalLocalZ = goalLocalZ,
            };
            job.Schedule().Complete();

            cache.SlotKeys[slot] = key;
            cache.SlotIndex.TryAdd(key, slot);
        }
    }

    /// <summary>
    /// Per-tile Dijkstra integration sweep + direction-byte assignment.
    /// Single-thread IJob: writes to the slab's region of the
    /// integration / dir pools. Multiple instances can run in parallel
    /// over different slots (disjoint pool ranges) -- M3 schedules
    /// sequentially per miss to keep behaviour byte-identical to the
    /// cache tests.
    /// </summary>
    [BurstCompile]
    internal struct IntegrateTileJob : IJob
    {
        [ReadOnly] public NativeArray<byte> Cost;
        [NativeDisableParallelForRestriction] public NativeArray<uint> IntegrationPool;
        [NativeDisableParallelForRestriction] public NativeArray<byte> DirPool;
        public int IntegrationOffset;
        public int DirOffset;
        public int TileSize;
        public int GridWidth;
        public int GridHeight;
        public int TileX0;
        public int TileZ0;
        public int GoalLocalX;
        public int GoalLocalZ;

        // Wall-clearance: extra integration cost charged for STEPPING INTO a
        // cell that touches an impassable cell (8-neighbourhood). Bows the
        // flow gradient one cell off walls/buildings so units stop hugging
        // obstacle edges and clipping their corners while travelling parallel
        // to them (the root cause of the "stuck running alongside a wall"
        // reports). Finite, so it is only a PREFERENCE -- a one-cell corridor
        // with walls on both sides still routes through (Dijkstra picks the
        // least-cost REACHABLE path). Tuned relative to StepCardinal (10):
        // ~10 cells of hugging (cost +80) easily loses to a 2-cell detour, so
        // open-ground paths keep their distance while pinch points still pass.
        // Deterministic: const + the lockstep-identical Cost array.
        private const uint WallClearancePenalty = 8;

        public void Execute()
        {
            int area = TileSize * TileSize;
            // Init integration to unreachable.
            for (int i = 0; i < area; i++)
                IntegrationPool[IntegrationOffset + i] = uint.MaxValue;

            // Goal init.
            if (GoalLocalX < 0 || GoalLocalX >= TileSize
                || GoalLocalZ < 0 || GoalLocalZ >= TileSize)
                return;
            int goalLocalIdx = GoalLocalZ * TileSize + GoalLocalX;
            IntegrationPool[IntegrationOffset + goalLocalIdx] = 0;

            // FIFO double-buffered relaxation (mirror of M1's IntegrationDijkstraJob).
            var fa = new NativeQueue<int>(Allocator.Temp);
            var fb = new NativeQueue<int>(Allocator.Temp);
            fa.Enqueue(goalLocalIdx);

            var read = fa;
            var write = fb;

            while (read.Count > 0)
            {
                while (read.TryDequeue(out int localIdx))
                {
                    uint here = IntegrationPool[IntegrationOffset + localIdx];
                    int lx = localIdx % TileSize;
                    int lz = localIdx / TileSize;

                    Relax(lx + 1, lz, here + NavFlowConstants.StepCardinal, write);
                    Relax(lx - 1, lz, here + NavFlowConstants.StepCardinal, write);
                    Relax(lx, lz + 1, here + NavFlowConstants.StepCardinal, write);
                    Relax(lx, lz - 1, here + NavFlowConstants.StepCardinal, write);

                    if (IsOpen(lx + 1, lz) && IsOpen(lx, lz + 1))
                        Relax(lx + 1, lz + 1, here + NavFlowConstants.StepDiagonal, write);
                    if (IsOpen(lx + 1, lz) && IsOpen(lx, lz - 1))
                        Relax(lx + 1, lz - 1, here + NavFlowConstants.StepDiagonal, write);
                    if (IsOpen(lx - 1, lz) && IsOpen(lx, lz + 1))
                        Relax(lx - 1, lz + 1, here + NavFlowConstants.StepDiagonal, write);
                    if (IsOpen(lx - 1, lz) && IsOpen(lx, lz - 1))
                        Relax(lx - 1, lz - 1, here + NavFlowConstants.StepDiagonal, write);
                }
                var tmp = read; read = write; write = tmp;
            }

            fa.Dispose();
            fb.Dispose();

            // Assign direction bytes -- weighted-gradient over all 8
            // walkable neighbours, mapped to a 256-bin angle byte via
            // atan2. Replaces the M3 "pick best of 8 octile neighbours"
            // logic which made units zig-zag on open ground (8-direction
            // quantization). Now Dir[idx] indexes the full 256-entry
            // DirectionTableBlob.Dirs, giving a true bearing-to-goal field
            // on flat Dijkstra integration.
            for (int z = 0; z < TileSize; z++)
            {
                for (int x = 0; x < TileSize; x++)
                {
                    int idx = z * TileSize + x;
                    uint here = IntegrationPool[IntegrationOffset + idx];

                    if (here == uint.MaxValue || !IsOpen(x, z))
                    {
                        DirPool[DirOffset + idx] = NavFlowConstants.NoDirection;
                        continue;
                    }
                    if (x == GoalLocalX && z == GoalLocalZ)
                    {
                        DirPool[DirOffset + idx] = NavFlowConstants.NoDirection;
                        continue;
                    }

                    float gx = 0f, gz = 0f;
                    for (int dzz = -1; dzz <= 1; dzz++)
                    for (int dxx = -1; dxx <= 1; dxx++)
                    {
                        if (dxx == 0 && dzz == 0) continue;
                        int nx = x + dxx, nz = z + dzz;
                        if (!IsOpen(nx, nz)) continue;
                        // Diagonals require both adjacent cardinals walkable
                        // so the gradient doesn't tunnel through corners.
                        if (dxx != 0 && dzz != 0)
                        {
                            if (!IsOpen(x + dxx, z)) continue;
                            if (!IsOpen(x, z + dzz)) continue;
                        }
                        uint nCost = IntegrationPool[IntegrationOffset + nz * TileSize + nx];
                        if (nCost == uint.MaxValue || nCost >= here) continue;
                        float weight = (float)(here - nCost);
                        float inv = (dxx != 0 && dzz != 0) ? 0.70710678f : 1f;
                        gx += dxx * weight * inv;
                        gz += dzz * weight * inv;
                    }

                    if (gx == 0f && gz == 0f)
                    {
                        DirPool[DirOffset + idx] = NavFlowConstants.NoDirection;
                        continue;
                    }
                    float angle = math.atan2(gz, gx);
                    if (angle < 0f) angle += 2f * math.PI;
                    int dirByte = (int)math.round(angle / (2f * math.PI) * 256f);
                    DirPool[DirOffset + idx] = (byte)(dirByte & 0xFF);
                }
            }
        }

        private bool IsOpen(int lx, int lz)
        {
            if (lx < 0 || lx >= TileSize || lz < 0 || lz >= TileSize) return false;
            int gx = TileX0 + lx;
            int gz = TileZ0 + lz;
            if (gx < 0 || gx >= GridWidth || gz < 0 || gz >= GridHeight) return false;
            return Cost[gz * GridWidth + gx] != NavCostField.CostImpassable;
        }

        private void Relax(int lx, int lz, uint tentative, NativeQueue<int> writeFrontier)
        {
            if (!IsOpen(lx, lz)) return;
            // Charge the wall-clearance penalty for entering a cell adjacent to
            // a wall, so the gradient prefers routes that keep a cell of slack.
            tentative += WallClearancePenalty * NearWall(lx, lz);
            int idx = lz * TileSize + lx;
            if (tentative < IntegrationPool[IntegrationOffset + idx])
            {
                IntegrationPool[IntegrationOffset + idx] = tentative;
                writeFrontier.Enqueue(idx);
            }
        }

        /// <summary>
        /// 1 when the local cell (<paramref name="lx"/>,<paramref name="lz"/>)
        /// has an impassable cell in its global 8-neighbourhood, else 0. Only
        /// fully-impassable cells (255) count -- conditional gate cells (254)
        /// are deliberately excluded so clearance never pushes a unit away from
        /// the gate it is trying to pass through.
        /// </summary>
        private uint NearWall(int lx, int lz)
        {
            int gx = TileX0 + lx;
            int gz = TileZ0 + lz;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = gx + dx, nz = gz + dz;
                    if (nx < 0 || nx >= GridWidth || nz < 0 || nz >= GridHeight) continue;
                    if (Cost[nz * GridWidth + nx] == NavCostField.CostImpassable) return 1;
                }
            }
            return 0;
        }

        private void TryDir(int lx, int lz, ref uint best, ref byte bestDir, byte dirByte)
        {
            if (!IsOpen(lx, lz)) return;
            uint cost = IntegrationPool[IntegrationOffset + lz * TileSize + lx];
            if (cost < best)
            {
                best = cost;
                bestDir = dirByte;
            }
        }
    }
}
