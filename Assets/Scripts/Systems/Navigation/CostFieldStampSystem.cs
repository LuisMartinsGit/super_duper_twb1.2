// CostFieldStampSystem.cs
// Each tick, clears the layer-0 cost slab and stamps every BuildingTag
// entity's footprint into it. M1 takes the snapshot approach — full
// re-stamp every tick — so structural-change accounting stays out of the
// way. M4 will replace this with dirty-tile incremental rebuild driven by
// stamp events.
//
// Location: Assets/Scripts/Systems/Navigation/CostFieldStampSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Runs in <see cref="SimulationSystemGroup"/> after
    /// <see cref="NavGridBootstrapSystem"/>. Owns no allocations — borrows
    /// the singleton arrays. Bumps <see cref="NavCostField.Generation"/>
    /// after each restamp so downstream consumers can detect change.
    /// </summary>
    // NavGridBootstrapSystem lives in InitializationSystemGroup which runs
    // before SimulationSystemGroup every frame, so the singleton is already
    // present here -- no [UpdateAfter] needed (cross-group ordering is
    // ignored by Unity anyway, which would emit a warning).
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct CostFieldStampSystem : ISystem
    {
        private EntityQuery _buildingQuery;
        private EntityQuery _buildingSizedQuery;
        // task-112 M5 -- separate queries for wall entities so we can stamp
        // BOTH layers (ground = impassable / conditional at gates; rampart
        // = walkable top). Walls exclude WallHubTag from the climb-access
        // path (hubs are stair cores in the AlanthorWall factory layout).
        private EntityQuery _wallQuery;
        private EntityQuery _wallGateQuery;
        private EntityQuery _wallClimbQuery;
        private EntityQuery _obstacleQuery;
        // Overpass bridges: deck walkable on layer 1, ground underneath
        // untouched, ramp ends as climb-access transition cells.
        private EntityQuery _overpassQuery;

        // Perf change-gate: signature of the stampable entity set at the last
        // stamp. While it's unchanged the cost field is already correct, so we
        // skip the whole clear+stamp pass and the Generation bump.
        private ulong _lastSignature;
        private byte _stampedOnce;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavCostField>();

            // Default 3x3 footprint — ONLY for buildings that carry no
            // BuildingSize. Sized buildings are stamped exactly by
            // _buildingSizedQuery below. WithNone<BuildingSize> was missing, so
            // every sized building ALSO got the blanket 3x3: for 1x1 and 2x2
            // structures (walls' towers, Gatherer's Hut, chapels, Border sub-nodes)
            // the impassable region was strictly larger than the BuildingSize rect
            // that combat measures reach against, so melee units were physically
            // stopped ~1.5 m from a surface the combat system believed was 1.0 m
            // away — they never entered range and slid around the footprint instead.
            _buildingQuery = SystemAPI.QueryBuilder()
                .WithAll<BuildingTag, LocalTransform>()
                .WithNone<WallTag, BuildingSize>()
                .Build();

            // task-112 follow-up: ObstacleTag entities (iron deposits,
            // veilstone nodes, outcroppings, forest rocks, trees that are
            // spawned as ECS entities) must also stamp the cost field
            // or units treat them as empty ground. Default 3x3 footprint.
            _obstacleQuery = SystemAPI.QueryBuilder()
                .WithAll<ObstacleTag, LocalTransform>()
                .WithNone<BuildingTag, WallTag>()
                .Build();

            _buildingSizedQuery = SystemAPI.QueryBuilder()
                .WithAll<BuildingTag, BuildingSize, LocalTransform>()
                .WithNone<WallTag>()
                .Build();

            // task-112 M5: stamp walls into BOTH layers via dedicated jobs.
            // Plain walls + tower walls: ground=impassable, rampart=walkable.
            // FactionTag is required because StampWallLayersJob.Execute reads
            // it to encode the gate-owner faction in the cell flags.
            _wallQuery = SystemAPI.QueryBuilder()
                .WithAll<WallTag, LocalTransform, FactionTag>()
                .WithNone<WallGateTag>()
                .Build();
            // Gates: ground=conditional (254), rampart=walkable.
            _wallGateQuery = SystemAPI.QueryBuilder()
                .WithAll<WallTag, WallGateTag, LocalTransform, FactionTag>()
                .Build();
            // Climb access (wall hubs in the M5 layout): ground=walkable
            // (cost 1, so units can approach), rampart=walkable, plus the
            // FlagClimbAccess bit.
            _wallClimbQuery = SystemAPI.QueryBuilder()
                .WithAll<WallTag, WallHubTag, LocalTransform, FactionTag>()
                .Build();

            _overpassQuery = SystemAPI.QueryBuilder()
                .WithAll<OverpassBridge>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<NavCostField>()) return;

            var field = SystemAPI.GetSingleton<NavCostField>();

            // ── Change gate (perf) ─────────────────────────────────────────
            // The cost field only changes when a stampable entity is created /
            // destroyed / resized, or when the terrain mask is first baked.
            // Buildings, walls and obstacles never MOVE, so a signature over
            // their per-query counts + building footprint sizes + the
            // TerrainBaked latch captures every real change. On the (vast
            // majority of) ticks where nothing changed, skip the whole
            // clear+stamp pass AND the Generation bump — which in turn lets
            // BuildingCostStampSystem skip its full-field diff. The signature
            // is a deterministic function of the lockstep-identical entity set,
            // so every client gates identically (no desync); a same-tick
            // create+destroy that nets zero is harmless (all clients miss it
            // identically and the next real change re-stamps).
            ulong sig = ComputeStampSignature(field.TerrainBaked);
            if (_stampedOnce != 0 && sig == _lastSignature) return;
            _lastSignature = sig;
            _stampedOnce = 1;

            int rows = field.Height;
            int layerArea = field.Width * field.Height;

            // Clear layer-0 (Ground) in parallel.
            var clearJob = new ClearLayer0Job
            {
                Cost = field.Cost,
                Flags = field.Flags,
                TerrainCost = field.TerrainCost,
                Width = field.Width,
            };
            var clearHandle = clearJob.Schedule(rows, 8, state.Dependency);

            // task-112 M5 -- clear layer-1 (Rampart) to IMPASSABLE so units
            // can only walk on it where a wall stamps a walkable cell.
            JobHandle clearRampHandle = clearHandle;
            if (field.LayerCount > 1)
            {
                var clearRampJob = new ClearLayerImpassableJob
                {
                    Cost = field.Cost,
                    Flags = field.Flags,
                    Width = field.Width,
                    LayerOffset = layerArea,
                };
                clearRampHandle = clearRampJob.Schedule(rows, 8, clearHandle);
            }

            // Stamp building footprints. The two passes cover (a) the
            // default-3x3 footprint for buildings without BuildingSize and
            // (b) the BuildingSize-aware footprint. Both run with the
            // [NativeDisableParallelForRestriction] knob: writes to the same
            // cell from overlapping footprints are idempotent (always 255 /
            // FlagBuildingFootprint), so the race is harmless for M1. M4
            // tightens this with Interlocked.Or per DR-6.
            var defaultStamp = new StampBuildingFootprintJob
            {
                Cost = field.Cost,
                Flags = field.Flags,
                Width = field.Width,
                Height = field.Height,
                CellSize = SystemAPI.HasSingleton<NavGridSingleton>()
                    ? SystemAPI.GetSingleton<NavGridSingleton>().CellSize : 1f,
                Origin = SystemAPI.HasSingleton<NavGridSingleton>()
                    ? SystemAPI.GetSingleton<NavGridSingleton>().Origin : float3.zero,
            };
            var defaultHandle = defaultStamp.ScheduleParallel(_buildingQuery, clearRampHandle);

            var sizedStamp = new StampBuildingFootprintSizedJob
            {
                Cost = field.Cost,
                Flags = field.Flags,
                Width = field.Width,
                Height = field.Height,
                CellSize = SystemAPI.HasSingleton<NavGridSingleton>()
                    ? SystemAPI.GetSingleton<NavGridSingleton>().CellSize : 1f,
                Origin = SystemAPI.HasSingleton<NavGridSingleton>()
                    ? SystemAPI.GetSingleton<NavGridSingleton>().Origin : float3.zero,
            };
            var sizedHandle = sizedStamp.ScheduleParallel(_buildingSizedQuery, defaultHandle);

            // Stamp ObstacleTag entities (iron deposits, veilstone nodes,
            // outcroppings, forest rocks) with the same impassable 3x3 the
            // building default-stamp uses. Without this, the entire
            // resource economy reads as walkable ground and units cut
            // straight through forests / mines / corpses.
            var obstacleStamp = new StampObstacleFootprintJob
            {
                Cost = field.Cost,
                Flags = field.Flags,
                Width = field.Width,
                Height = field.Height,
                CellSize = SystemAPI.HasSingleton<NavGridSingleton>()
                    ? SystemAPI.GetSingleton<NavGridSingleton>().CellSize : 1f,
                Origin = SystemAPI.HasSingleton<NavGridSingleton>()
                    ? SystemAPI.GetSingleton<NavGridSingleton>().Origin : float3.zero,
            };
            JobHandle prevHandle = obstacleStamp.ScheduleParallel(_obstacleQuery, sizedHandle);

            // task-112 M5: stamp walls into both layers. Order matters --
            // the climb-access pass overwrites any wall-instance stamp at
            // the hub cells so the ground stays walkable for approaching
            // units; the gate pass writes 254 (conditional) onto the gate
            // footprint after the plain-wall pass would have written 255.
            if (field.LayerCount > 1)
            {
                float cs = SystemAPI.HasSingleton<NavGridSingleton>()
                    ? SystemAPI.GetSingleton<NavGridSingleton>().CellSize : 1f;
                float3 org = SystemAPI.HasSingleton<NavGridSingleton>()
                    ? SystemAPI.GetSingleton<NavGridSingleton>().Origin : float3.zero;

                // Pass 1: plain walls (instances / towers / segments).
                var stampWall = new StampWallLayersJob
                {
                    Cost = field.Cost,
                    Flags = field.Flags,
                    Width = field.Width,
                    Height = field.Height,
                    CellSize = cs,
                    Origin = org,
                    LayerArea = layerArea,
                    HasBuildingSizeFootprint = 0,
                    IsGate = 0,
                    IsClimbAccess = 0,
                };
                prevHandle = stampWall.ScheduleParallel(_wallQuery, prevHandle);

                // Pass 2: gates -- ground = 254 (conditional), rampart = 1.
                var stampGate = new StampWallLayersJob
                {
                    Cost = field.Cost,
                    Flags = field.Flags,
                    Width = field.Width,
                    Height = field.Height,
                    CellSize = cs,
                    Origin = org,
                    LayerArea = layerArea,
                    HasBuildingSizeFootprint = 0,
                    IsGate = 1,
                    IsClimbAccess = 0,
                };
                prevHandle = stampGate.ScheduleParallel(_wallGateQuery, prevHandle);

                // Pass 3: climb access (hubs / stair cells) -- ground stays
                // walkable (cost 1), rampart walkable, FlagClimbAccess set
                // so WallPortalDetectionSystem can find them.
                var stampClimb = new StampWallLayersJob
                {
                    Cost = field.Cost,
                    Flags = field.Flags,
                    Width = field.Width,
                    Height = field.Height,
                    CellSize = cs,
                    Origin = org,
                    LayerArea = layerArea,
                    HasBuildingSizeFootprint = 0,
                    IsGate = 0,
                    IsClimbAccess = 1,
                };
                prevHandle = stampClimb.ScheduleParallel(_wallClimbQuery, prevHandle);

                // Pass 4: overpass bridges -- deck strip walkable on the
                // rampart layer (ground beneath untouched: units walk UNDER
                // the span), ramp discs at both ends flagged as climb
                // access on both layers.
                if (!_overpassQuery.IsEmptyIgnoreFilter)
                {
                    var bridges = _overpassQuery.ToComponentDataArray<OverpassBridge>(Allocator.TempJob);
                    var stampOverpass = new StampOverpassJob
                    {
                        Cost = field.Cost,
                        Flags = field.Flags,
                        Width = field.Width,
                        Height = field.Height,
                        LayerArea = layerArea,
                        CellSize = cs,
                        Origin = org,
                        Bridges = bridges,
                    };
                    prevHandle = stampOverpass.Schedule(prevHandle);
                    prevHandle = bridges.Dispose(prevHandle);
                }
            }

            state.Dependency = prevHandle;

            // Bump generation. Write directly back — the singleton lives in
            // a chunk so SetSingleton is cheap.
            field.Generation++;
            SystemAPI.SetSingleton(field);
        }

        /// <summary>
        /// FNV-1a hash over the stampable entity set: per-query counts (catch
        /// create/destroy), building footprint sizes (catch upgrades that
        /// resize a footprint without a count change), and the TerrainBaked
        /// latch. Cheap — a few CalculateEntityCount calls plus a short walk of
        /// the (few-hundred at most) sized buildings. Deterministic across
        /// machines (counts/sizes are a function of the simulation state).
        /// </summary>
        private ulong ComputeStampSignature(byte terrainBaked)
        {
            unchecked
            {
                const ulong P = 1099511628211UL;
                ulong h = 1469598103934665603UL;
                int sized = _buildingSizedQuery.CalculateEntityCount();
                h = (h ^ (uint)_buildingQuery.CalculateEntityCount()) * P;
                h = (h ^ (uint)sized) * P;
                h = (h ^ (uint)_obstacleQuery.CalculateEntityCount()) * P;
                h = (h ^ (uint)_wallQuery.CalculateEntityCount()) * P;
                h = (h ^ (uint)_wallGateQuery.CalculateEntityCount()) * P;
                h = (h ^ (uint)_wallClimbQuery.CalculateEntityCount()) * P;
                h = (h ^ (uint)_overpassQuery.CalculateEntityCount()) * P;
                h = (h ^ terrainBaked) * P;
                if (sized > 0)
                {
                    using var sizes = _buildingSizedQuery.ToComponentDataArray<BuildingSize>(Allocator.Temp);
                    for (int i = 0; i < sizes.Length; i++)
                    {
                        h = (h ^ (uint)sizes[i].Width) * P;
                        h = (h ^ (uint)sizes[i].Height) * P;
                    }
                }
                return h;
            }
        }
    }
}
