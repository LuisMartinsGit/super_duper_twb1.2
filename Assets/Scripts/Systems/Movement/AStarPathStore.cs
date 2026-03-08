// AStarPathStore.cs
// Managed singleton storing A* per-unit paths.
// Location: Assets/Scripts/Systems/Movement/AStarPathStore.cs

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Systems.Movement
{
    /// <summary>
    /// MonoBehaviour singleton that stores computed A* paths for units.
    /// Provides AssignPath/TryGetWaypoint API for MovementSystem.
    /// Throttles path computation to avoid frame spikes.
    /// </summary>
    public class AStarPathStore : MonoBehaviour
    {
        public static AStarPathStore Instance { get; private set; }

        /// <summary>Maximum paths to compute per frame to avoid spikes.</summary>
        private const int MaxPathsPerFrame = 20;

        // Path storage
        private Dictionary<int, List<float3>> _paths;
        private Dictionary<Entity, int> _entityToPath;
        private int _nextPathId;

        // Pending requests (throttled)
        private Queue<PathRequest> _pendingRequests;
        private int _pathsComputedThisFrame;

        private struct PathRequest
        {
            public Entity Entity;
            public float3 Start;
            public float3 Goal;
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
            _paths = new Dictionary<int, List<float3>>(64);
            _entityToPath = new Dictionary<Entity, int>(64);
            _pendingRequests = new Queue<PathRequest>();
            _nextPathId = 1;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            _pathsComputedThisFrame = 0;
            ProcessPendingRequests();
        }

        // =====================================================================
        // PUBLIC API
        // =====================================================================

        /// <summary>
        /// Queue an A* path computation for a unit. If under throttle limit,
        /// computes immediately; otherwise queued for next frame.
        /// </summary>
        public void RequestPath(Entity entity, float3 start, float3 goal)
        {
            // Clear any existing path for this entity
            ClearPath(entity);

            if (_pathsComputedThisFrame < MaxPathsPerFrame)
            {
                ComputeAndAssign(entity, start, goal);
            }
            else
            {
                _pendingRequests.Enqueue(new PathRequest
                {
                    Entity = entity, Start = start, Goal = goal
                });
            }
        }

        /// <summary>
        /// Get the current waypoint for an entity's path.
        /// Returns false if no path exists or index is out of range.
        /// </summary>
        public bool TryGetWaypoint(Entity entity, int waypointIndex, out float3 waypoint)
        {
            waypoint = float3.zero;

            if (!_entityToPath.TryGetValue(entity, out int pathId))
                return false;

            if (!_paths.TryGetValue(pathId, out var path))
            {
                // Stale reference — clean up
                _entityToPath.Remove(entity);
                return false;
            }

            if (waypointIndex < 0 || waypointIndex >= path.Count)
                return false;

            waypoint = path[waypointIndex];
            return true;
        }

        /// <summary>Total number of waypoints in an entity's path.</summary>
        public int GetPathLength(Entity entity)
        {
            if (!_entityToPath.TryGetValue(entity, out int pathId)) return 0;
            if (!_paths.TryGetValue(pathId, out var path)) return 0;
            return path.Count;
        }

        /// <summary>Remove an entity's path from the store.</summary>
        public void ClearPath(Entity entity)
        {
            if (_entityToPath.TryGetValue(entity, out int pathId))
            {
                _paths.Remove(pathId);
                _entityToPath.Remove(entity);
            }
        }

        // =====================================================================
        // INTERNALS
        // =====================================================================

        private void ProcessPendingRequests()
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return;

            while (_pendingRequests.Count > 0 && _pathsComputedThisFrame < MaxPathsPerFrame)
            {
                var req = _pendingRequests.Dequeue();
                ComputeAndAssign(req.Entity, req.Start, req.Goal);
            }
        }

        private void ComputeAndAssign(Entity entity, float3 start, float3 goal)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return;

            var path = AStarPathfinder.FindPath(start, goal, grid);
            _pathsComputedThisFrame++;

            if (path == null || path.Count == 0)
                return;

            int pathId = _nextPathId++;
            _paths[pathId] = path;
            _entityToPath[entity] = pathId;

            // Ensure the entity has AStarPathIndex component
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                var em = world.EntityManager;
                if (em.Exists(entity))
                {
                    if (!em.HasComponent<AStarPathIndex>(entity))
                        em.AddComponent<AStarPathIndex>(entity);
                    em.SetComponentData(entity, new AStarPathIndex
                    {
                        CurrentWaypoint = 0,
                        PathId = pathId
                    });
                }
            }

            #if UNITY_EDITOR
            Debug.Log($"[AStarPathStore] Path computed for entity {entity.Index}: " +
                      $"{path.Count} waypoints (pathId={pathId})");
            #endif
        }
    }
}
