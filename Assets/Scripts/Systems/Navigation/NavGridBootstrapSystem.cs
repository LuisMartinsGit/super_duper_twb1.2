// NavGridBootstrapSystem.cs
// Allocates the NavGridSingleton, NavCostField, and DirectionTableSingleton
// entities at world init. Owns disposal in OnDestroy.
//
// task-112 M3: dropped the NavFlowFieldM1 singleton allocation -- the
// whole-map flow field was replaced by per-tile cached slabs in
// NavFlowCache (owned by FlowSegmentSystem). The cost field grew to
// 512x512 per architecture's R10 scale target.
//
// CCD-2: NavCostField uses Allocator.Persistent for both Cost and Flags
// arrays. CCD-3: DirectionTableBlob is built once from cos/sin via a
// BlobBuilder (NOT Burst — BlobBuilder is a managed disposable, and
// EntityManager.CreateEntity(typeof(...)) in OnCreate trips BC1028 if the
// method is [BurstCompile]'d). The math itself is deterministic across
// machines at a pinned Burst version (DR-15).
//
// Location: Assets/Scripts/Systems/Navigation/NavGridBootstrapSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// One-shot init system. Runs in <see cref="InitializationSystemGroup"/>
    /// once at world start; subsequent OnUpdate calls are no-ops (the system
    /// could be torn down after init but we keep it alive so OnDestroy fires
    /// during world tear-down and disposes the singleton arrays).
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial struct NavGridBootstrapSystem : ISystem
    {
        // task-112 M3 expanded the grid to 512x512 -- the spec's R10 scale
        // target -- so the new portal-graph stack has a realistic-sized
        // playground to validate against. The cost field is 256 KB resident
        // at this size (Width * Height * LayerCount bytes); the flow field
        // singleton is ~2.25 MB (byte dir + uint integration). Phase 5
        // grows LayerCount to 2 for ramparts.
        public const int GridWidth = 512;
        public const int GridHeight = 512;
        public const float DefaultCellSize = 1f;
        // task-112 M5: grid grew to two layers (0 = Ground, 1 = Rampart).
        // NavCostField now allocates Width * Height * LayerCount bytes per
        // cost / flags array (see NavCostField.Index(x, z, layer)).
        public const int LayerCount = 2;
        public const int DirectionTableSize = 256;

        private Entity _gridEntity;
        private Entity _costEntity;
        private Entity _dirTableEntity;
        private byte _initialised;

        // NOT [BurstCompile]: BC1028 — CreateEntity(typeof(...)) is a managed
        // call. The math we hand off to BuildDirectionTableJob is Burst-safe
        // on its own.
        public void OnCreate(ref SystemState state)
        {
            // Defer real init to OnUpdate so we have a Burst-safe entry path
            // for the rare case the system is created mid-frame. OnUpdate's
            // first call performs the allocations exactly once.
            _initialised = 0;
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_initialised != 0) return;

            // ── Size + position the grid to the ACTUAL scene Terrain ──────
            // The old hardcoded 512x512-centred-on-origin box only matched the
            // nav-test scenarios; hand-authored maps (e.g. YielLymwérra) live
            // elsewhere, so buildings/units fell outside the grid and the cost
            // field stamped nothing. Defer init until the Terrain exists, then
            // cover its full world bounds. (No Terrain yet -> wait; this runs
            // every frame in InitializationSystemGroup.)
            var terrains = UnityEngine.Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0) return;

            float minX = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxZ = float.MinValue;
            for (int i = 0; i < terrains.Length; i++)
            {
                var t = terrains[i];
                if (t == null || t.terrainData == null) continue;
                var p = t.transform.position;          // Terrain pos = min corner
                var s = t.terrainData.size;             // (x = width, z = length)
                if (p.x < minX) minX = p.x;
                if (p.z < minZ) minZ = p.z;
                if (p.x + s.x > maxX) maxX = p.x + s.x;
                if (p.z + s.z > maxZ) maxZ = p.z + s.z;
            }
            float spanX = maxX - minX;
            float spanZ = maxZ - minZ;
            if (spanX <= 0f || spanZ <= 0f) return; // terrainData not ready yet

            // Choose a cell size that keeps the grid <= MaxGridDim cells per
            // axis (bounds memory + keeps the integration sweep tractable).
            // 1 m cells for maps up to MaxGridDim m; coarser beyond that.
            const int MaxGridDim = 1024;
            float cellSize = DefaultCellSize;
            int gridW = (int)math.ceil(spanX / cellSize);
            int gridH = (int)math.ceil(spanZ / cellSize);
            while (gridW > MaxGridDim || gridH > MaxGridDim)
            {
                cellSize *= 2f;
                gridW = (int)math.ceil(spanX / cellSize);
                gridH = (int)math.ceil(spanZ / cellSize);
            }
            // Pad a couple of cells so footprints right at the edge don't clamp
            // away, and so the grid fully encloses the terrain.
            gridW += 2;
            gridH += 2;
            float originX = minX - cellSize; // shift origin out by the pad
            float originZ = minZ - cellSize;

            _initialised = 1;

            int n = gridW * gridH * LayerCount;

            // ── Cost field allocation ───────────────────────────────────
            var cost = new NativeArray<byte>(n, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            var flags = new NativeArray<byte>(n, Allocator.Persistent, NativeArrayOptions.ClearMemory);

            // ── Direction table blob ────────────────────────────────────
            // Build on the main thread; the math matches BuildDirectionTableJob
            // bit-for-bit.
            BlobAssetReference<DirectionTableBlob> tableRef;
            using (var builder = new BlobBuilder(Allocator.Temp))
            {
                ref var root = ref builder.ConstructRoot<DirectionTableBlob>();
                var arr = builder.Allocate(ref root.Dirs, DirectionTableSize);
                float twoPi = 2f * math.PI;
                float inv = twoPi / DirectionTableSize;
                for (int i = 0; i < DirectionTableSize; i++)
                {
                    float a = i * inv;
                    arr[i] = new float2(math.cos(a), math.sin(a));
                }
                tableRef = builder.CreateBlobAssetReference<DirectionTableBlob>(Allocator.Persistent);
            }

            var em = state.EntityManager;

            // Origin = the terrain's min corner (minus the edge pad above), so
            // cell (0,0) maps to world (originX, _, originZ) and the grid spans
            // the full terrain.
            _gridEntity = em.CreateEntity(typeof(NavGridSingleton));
            em.SetComponentData(_gridEntity, new NavGridSingleton
            {
                Width = gridW,
                Height = gridH,
                CellSize = cellSize,
                Origin = new float3(originX, 0f, originZ),
                LayerCount = LayerCount,
            });

            _costEntity = em.CreateEntity(typeof(NavCostField));
            em.SetComponentData(_costEntity, new NavCostField
            {
                Cost = cost,
                Flags = flags,
                Width = gridW,
                Height = gridH,
                LayerCount = LayerCount,
                Generation = 0,
            });

            UnityEngine.Debug.Log(
                $"[NavGrid] initialised {gridW}x{gridH} cell={cellSize:0.##} origin=({originX:0.0},{originZ:0.0}) " +
                $"covering world X[{minX:0}..{maxX:0}] Z[{minZ:0}..{maxZ:0}]");

            _dirTableEntity = em.CreateEntity(typeof(DirectionTableSingleton));
            em.SetComponentData(_dirTableEntity, new DirectionTableSingleton
            {
                Table = tableRef,
            });
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_initialised == 0) return;

            var em = state.EntityManager;

            if (em.Exists(_costEntity) && em.HasComponent<NavCostField>(_costEntity))
            {
                var field = em.GetComponentData<NavCostField>(_costEntity);
                if (field.Cost.IsCreated) field.Cost.Dispose();
                if (field.Flags.IsCreated) field.Flags.Dispose();
            }

            if (em.Exists(_dirTableEntity) && em.HasComponent<DirectionTableSingleton>(_dirTableEntity))
            {
                var table = em.GetComponentData<DirectionTableSingleton>(_dirTableEntity);
                if (table.Table.IsCreated) table.Table.Dispose();
            }
        }
    }
}
