// PassabilityBuildingSync.cs
// ECS system that periodically syncs building footprints with the passability grid.
// Tracks known buildings and updates the grid when buildings appear or are destroyed.
// Location: Assets/Scripts/Systems/Work/PassabilityBuildingSync.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Systems.Movement;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Periodically scans all buildings and obstacles, syncing their footprints with PassabilityGrid.
    /// - New buildings: calls BlockBuilding() to mark cells as building-blocked.
    /// - Destroyed buildings: calls UnblockBuilding() to restore cells to passable.
    /// - Destroyed obstacles: calls UnblockObstacle() to restore cells to passable.
    /// Polls every 0.5 seconds to avoid per-frame overhead.
    /// </summary>
    // NOTE: No [BurstCompile] — accesses managed singleton (PassabilityGrid.Instance)
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PassabilityBuildingSync : ISystem
    {
        private const float PollInterval = 0.5f;

        // Pads the blocked footprint outward so units keep a small gap from buildings.
        // Set to 0: with 1 m cells and ~0.5 m unit radii the grid block already gives
        // ~0.5 m clearance, and any +1 was doubling building footprints — corridors
        // collapsed in tight bases and trained units spawned/got trapped inside the
        // padding. If "scraping" returns, raise the unit-vs-building separation
        // force in CombatSeparationSystem instead of inflating the obstacle grid.
        private const int FootprintPaddingCells = 0;

        private float _timer;
        private NativeHashMap<Entity, BuildingRecord> _knownBuildings;
        private NativeHashMap<Entity, BuildingRecord> _knownObstacles;

        /// <summary>
        /// Cached position and radius for a known building/obstacle, used to unblock
        /// the correct cells when the entity is destroyed.
        /// </summary>
        private struct BuildingRecord
        {
            public float3 Position;
            public float Radius;
            public int2 Size;       // (0,0) means legacy circular mode
            public byte HasSize;    // 1 = has BuildingSize, 0 = legacy circular
        }

        public void OnCreate(ref SystemState state)
        {
            _timer = 0f;
            _knownBuildings = new NativeHashMap<Entity, BuildingRecord>(128, Allocator.Persistent);
            _knownObstacles = new NativeHashMap<Entity, BuildingRecord>(512, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_knownBuildings.IsCreated)
                _knownBuildings.Dispose();
            if (_knownObstacles.IsCreated)
                _knownObstacles.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return;

            _timer -= SystemAPI.Time.DeltaTime;
            if (_timer > 0f) return;
            _timer = PollInterval;

            var em = state.EntityManager;

            // Collect all current buildings with their position and radius
            var currentBuildings = new NativeHashMap<Entity, BuildingRecord>(64, Allocator.Temp);

            foreach (var (transform, radius, entity) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRO<Radius>>()
                         .WithAll<BuildingTag>()
                         .WithNone<UnderConstruction>()
                         .WithNone<WallGateTag>() // Gates managed by WallGatePassabilitySystem
                         .WithEntityAccess())
            {
                bool hasSize = em.HasComponent<BuildingSize>(entity);
                var record = new BuildingRecord
                {
                    Position = transform.ValueRO.Position,
                    Radius = radius.ValueRO.Value + FootprintPaddingCells,
                    HasSize = (byte)(hasSize ? 1 : 0)
                };
                if (hasSize)
                {
                    var bs = em.GetComponentData<BuildingSize>(entity);
                    record.Size = new int2(
                        bs.Width + FootprintPaddingCells * 2,
                        bs.Height + FootprintPaddingCells * 2);
                }
                currentBuildings.Add(entity, record);
            }

            // Detect destroyed buildings: known but no longer present
            var toRemove = new NativeList<Entity>(16, Allocator.Temp);

            foreach (var kvp in _knownBuildings)
            {
                if (!currentBuildings.ContainsKey(kvp.Key))
                {
                    // Missing braces here meant the circular UnblockBuilding ran
                    // unconditionally — fine for unblock, but a real bug at the
                    // matching block site below. Adding braces both places to
                    // make the intent explicit.
                    if (kvp.Value.HasSize == 1)
                    {
                        grid.UnblockBuildingRect(kvp.Value.Position, kvp.Value.Size);
                    }
                    else
                    {
                        grid.UnblockBuilding(kvp.Value.Position, kvp.Value.Radius);
                    }
                    toRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < toRemove.Length; i++)
            {
                _knownBuildings.Remove(toRemove[i]);
            }

            // Detect new buildings: present but not yet known
            bool newBuildingsAdded = false;
            foreach (var kvp in currentBuildings)
            {
                if (!_knownBuildings.ContainsKey(kvp.Key))
                {
                    // BUG FIX: missing braces here meant `BlockBuilding` (a
                    // CIRCULAR stamp) was called for EVERY building regardless
                    // of HasSize, so every rect-footprint building also got an
                    // overlapping circle. The circle's radius is derived from
                    // BuildingSize so it can extend WELL beyond the rect's
                    // footprint, jamming pathing around buildings the player
                    // can't see is "fat". Use the rect for sized buildings,
                    // fall back to circle only for legacy buildings without
                    // a BuildingSize component.
                    if (kvp.Value.HasSize == 1)
                    {
                        grid.BlockBuildingRect(kvp.Value.Position, kvp.Value.Size);
                    }
                    else
                    {
                        grid.BlockBuilding(kvp.Value.Position, kvp.Value.Radius);
                    }
                    _knownBuildings.Add(kvp.Key, kvp.Value);
                    newBuildingsAdded = true;
                }
            }

            currentBuildings.Dispose();

            bool gridChanged = toRemove.Length > 0 || newBuildingsAdded;
            toRemove.Dispose();

            // ─────────────────────────────────────────────────────────────────
            // OBSTACLE TRACKING (trees, rocks — detect destroyed obstacles)
            // ─────────────────────────────────────────────────────────────────

            // Collect current obstacles
            var currentObstacles = new NativeHashMap<Entity, BuildingRecord>(512, Allocator.Temp);

            foreach (var (transform, radius, entity) in SystemAPI
                         .Query<RefRO<LocalTransform>, RefRO<Radius>>()
                         .WithAll<ObstacleTag>()
                         .WithEntityAccess())
            {
                currentObstacles.Add(entity, new BuildingRecord
                {
                    Position = transform.ValueRO.Position,
                    Radius = radius.ValueRO.Value
                });
            }

            // Detect destroyed obstacles: known but no longer present
            var obstacleToRemove = new NativeList<Entity>(8, Allocator.Temp);

            foreach (var kvp in _knownObstacles)
            {
                if (!currentObstacles.ContainsKey(kvp.Key))
                {
                    grid.UnblockObstacle(kvp.Value.Position, kvp.Value.Radius);
                    obstacleToRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < obstacleToRemove.Length; i++)
            {
                _knownObstacles.Remove(obstacleToRemove[i]);
            }

            // Register new obstacles we haven't tracked yet
            // (ObstacleBootstrap already blocked them, but we need to track for cleanup)
            foreach (var kvp in currentObstacles)
            {
                if (!_knownObstacles.ContainsKey(kvp.Key))
                {
                    _knownObstacles.Add(kvp.Key, kvp.Value);
                }
            }

            currentObstacles.Dispose();

            if (obstacleToRemove.Length > 0)
                gridChanged = true;

            obstacleToRemove.Dispose();

            // PR3 — flow-field invalidation removed. NavMeshManager re-bakes
            // its navmesh when the building set changes via its own ECS sync.
            // gridChanged is still tracked for future use but no longer dispatched.
            _ = gridChanged;
        }
    }
}
