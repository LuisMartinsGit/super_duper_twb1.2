// NavMeshPathRequestSystem.cs
// PR2 of the navmesh migration. Per-unit driver that queries the
// NavMeshManager for paths and writes the resulting corner list into
// each unit's NavMeshWaypoint buffer.
//
// Throttled to a small number of path computations per tick so a wave
// of move orders doesn't hitch — same pattern as AStarPathStore.
//
// Not Burst-compiled: NavMesh.CalculatePath is a managed API on the
// main thread and returns a managed NavMeshPath. MovementSystem (Burst)
// only reads the resulting NativeArray-style buffer afterwards.
//
// Uses a manual EntityQuery instead of Entities.ForEach because
// the source generator rejects LocalTransform as a lambda parameter
// type (DC0005). Manual iteration is fine — managed API anyway.
//
// Location: Assets/Scripts/Systems/Movement/NavMeshPathRequestSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

namespace TheWaningBorder.Systems.Movement
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(MovementSystem))]
    public partial class NavMeshPathRequestSystem : SystemBase
    {
        // Per-frame budget — match the legacy AStarPathStore's 20.
        private const int MaxRequestsPerFrame = 20;
        // If the goal moved less than this, don't bother repathing.
        private const float GoalChangeThreshold = 1.0f;

        private NavMeshPath _scratchPath;
        private EntityQuery _unitQuery;

        protected override void OnCreate()
        {
            _scratchPath = new NavMeshPath();
            _unitQuery = GetEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadWrite<DesiredDestination>(),
                ComponentType.ReadOnly<LocalTransform>());
        }

        protected override void OnUpdate()
        {
            var nmm = NavMeshManager.Instance;
            if (nmm == null || !nmm.IsBaked) return;

            var em = EntityManager;
            using var entities = _unitQuery.ToEntityArray(Allocator.Temp);

            // Pass 1: lazy-stamp NavMeshPathfollowState + buffer, detect goal-change.
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var dd = em.GetComponentData<DesiredDestination>(entity);
                if (dd.Has == 0) continue;

                if (!em.HasComponent<NavMeshPathfollowState>(entity))
                {
                    em.AddComponentData(entity, new NavMeshPathfollowState
                    {
                        LastRequestedGoal = dd.Position,
                        CurrentWaypoint = 0,
                        HasPath = 0,
                        RequestPending = 1,
                    });
                    em.AddBuffer<NavMeshWaypoint>(entity);
                    continue;
                }

                var state = em.GetComponentData<NavMeshPathfollowState>(entity);
                if (math.distancesq(state.LastRequestedGoal, dd.Position)
                        > GoalChangeThreshold * GoalChangeThreshold
                    || (state.HasPath == 0 && state.RequestPending == 0))
                {
                    state.LastRequestedGoal = dd.Position;
                    state.RequestPending = 1;
                    em.SetComponentData(entity, state);
                }
            }

            // Pass 2: compute paths for entities flagged RequestPending.
            // Throttled per frame.
            int requestsThisFrame = 0;
            for (int i = 0; i < entities.Length; i++)
            {
                if (requestsThisFrame >= MaxRequestsPerFrame) break;
                var entity = entities[i];
                if (!em.HasComponent<NavMeshPathfollowState>(entity)) continue;

                var state = em.GetComponentData<NavMeshPathfollowState>(entity);
                if (state.RequestPending == 0) continue;

                requestsThisFrame++;
                state.RequestPending = 0;

                var xf = em.GetComponentData<LocalTransform>(entity);
                var fromW = new Vector3(xf.Position.x, xf.Position.y, xf.Position.z);
                var toW = new Vector3(state.LastRequestedGoal.x,
                    state.LastRequestedGoal.y, state.LastRequestedGoal.z);

                bool ok = nmm.RequestPath(fromW, toW, _scratchPath);
                var waypoints = em.GetBuffer<NavMeshWaypoint>(entity);
                waypoints.Clear();

                if (!ok || _scratchPath.corners == null || _scratchPath.corners.Length == 0)
                {
                    state.HasPath = 0;
                    state.CurrentWaypoint = 0;
                    em.SetComponentData(entity, state);
                    continue;
                }

                // Skip the first corner (= unit's own position).
                var corners = _scratchPath.corners;
                int start = corners.Length > 1 ? 1 : 0;
                for (int c = start; c < corners.Length; c++)
                {
                    waypoints.Add(new NavMeshWaypoint
                    {
                        Position = new float3(corners[c].x, corners[c].y, corners[c].z),
                    });
                }
                state.HasPath = (byte)(waypoints.Length > 0 ? 1 : 0);
                state.CurrentWaypoint = 0;
                em.SetComponentData(entity, state);
            }
        }
    }
}
