// PassabilityBuildingSync.cs
// ECS system that periodically syncs building footprints with the passability grid.
// Tracks known buildings and updates the grid when buildings appear or are destroyed.
// Location: Assets/Scripts/Systems/Work/PassabilityBuildingSync.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Work
{
    /// <summary>
    /// Periodically scans all buildings and syncs their footprints with PassabilityGrid.
    /// - New buildings: calls BlockBuilding() to mark cells as building-blocked.
    /// - Destroyed buildings: calls UnblockBuilding() to restore cells to passable.
    /// Polls every 0.5 seconds to avoid per-frame overhead.
    /// </summary>
    // NOTE: No [BurstCompile] — accesses managed singleton (PassabilityGrid.Instance)
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct PassabilityBuildingSync : ISystem
    {
        private const float PollInterval = 0.5f;

        private float _timer;
        private NativeHashMap<Entity, BuildingRecord> _knownBuildings;

        /// <summary>
        /// Cached position and radius for a known building, used to unblock
        /// the correct cells when the building is destroyed.
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
        }

        public void OnDestroy(ref SystemState state)
        {
            if (_knownBuildings.IsCreated)
                _knownBuildings.Dispose();
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

            toRemove.Dispose();

            // Detect new buildings: present but not yet known
            foreach (var kvp in currentBuildings)
            {
                if (!_knownBuildings.ContainsKey(kvp.Key))
                {
                    grid.BlockBuilding(kvp.Value.Position, kvp.Value.Radius);
                    _knownBuildings.Add(kvp.Key, kvp.Value);
                }
            }

            currentBuildings.Dispose();
        }
    }
}
