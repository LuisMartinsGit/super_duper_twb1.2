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
        }

        public void OnCreate(ref SystemState state)
        {
            _timer = 0f;
            _knownBuildings = new NativeHashMap<Entity, BuildingRecord>(128, Allocator.Persistent);
            _knownObstacles = new NativeHashMap<Entity, BuildingRecord>(32, Allocator.Persistent);
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
                         .WithEntityAccess())
            {
                currentBuildings.Add(entity, new BuildingRecord
                {
                    Position = transform.ValueRO.Position,
                    Radius = radius.ValueRO.Value
                });
            }

            // Detect destroyed buildings: known but no longer present
            var toRemove = new NativeList<Entity>(16, Allocator.Temp);

            foreach (var kvp in _knownBuildings)
            {
                if (!currentBuildings.ContainsKey(kvp.Key))
                {
                    grid.UnblockBuilding(kvp.Value.Position, kvp.Value.Radius);
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
                    grid.BlockBuilding(kvp.Value.Position, kvp.Value.Radius);
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
            var currentObstacles = new NativeHashMap<Entity, BuildingRecord>(32, Allocator.Temp);

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

            // If anything changed, invalidate stale flow fields so units re-route
            if (gridChanged)
            {
                var ffm = FlowFieldManager.Instance;
                if (ffm != null) ffm.InvalidateAll();
            }
        }
    }
}
