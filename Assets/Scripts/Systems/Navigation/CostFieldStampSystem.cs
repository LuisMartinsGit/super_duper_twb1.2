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

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavCostField>();

            _buildingQuery = SystemAPI.QueryBuilder()
                .WithAll<BuildingTag, LocalTransform>()
                .WithNone<WallTag>()
                .Build();

            // task-112 follow-up: ObstacleTag entities (iron deposits,
            // crystal nodes, cadavers, forest rocks, trees that are
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
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!SystemAPI.HasSingleton<NavCostField>()) return;

            var field = SystemAPI.GetSingleton<NavCostField>();
            int rows = field.Height;
            int layerArea = field.Width * field.Height;

            // Clear layer-0 (Ground) in parallel.
            var clearJob = new ClearLayer0Job
            {
                Cost = field.Cost,
                Flags = field.Flags,
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

            // Stamp ObstacleTag entities (iron deposits, crystal nodes,
            // cadavers, forest rocks) with the same impassable 3x3 the
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
            }

            state.Dependency = prevHandle;

            // Bump generation. Write directly back — the singleton lives in
            // a chunk so SetSingleton is cheap.
            field.Generation++;
            SystemAPI.SetSingleton(field);
        }
    }
}
