// NavMeshPathRequestSystem.cs
// PR2 of the navmesh migration. Per-unit driver that queries the
// NavMeshManager for paths and writes the resulting corner list into
// each unit's NavMeshWaypoint buffer.
//
// Runs only when GameSettings.UseNavMesh is on. Throttled to a small
// number of path computations per tick so a wave of move orders doesn't
// hitch — same pattern as AStarPathStore.
//
// Not Burst-compiled: NavMesh.CalculatePath is a managed API on the
// main thread and returns a managed NavMeshPath. MovementSystem (Burst)
// only reads the resulting NativeArray-style buffer afterwards.
//
// Location: Assets/Scripts/Systems/Movement/NavMeshPathRequestSystem.cs

using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

namespace TheWaningBorder.Systems.Movement
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(MovementSystem))]
    public partial class NavMeshPathRequestSystem : SystemBase
    {
        // Per-frame budget — match AStarPathStore's 20.
        private const int MaxRequestsPerFrame = 20;
        // If the goal moved less than this, don't bother repathing.
        private const float GoalChangeThreshold = 1.0f;

        private NavMeshPath _scratchPath;

        protected override void OnCreate()
        {
            _scratchPath = new NavMeshPath();
        }

        protected override void OnUpdate()
        {
            if (!GameSettings.UseNavMesh) return;
            var nmm = NavMeshManager.Instance;
            if (nmm == null || !nmm.IsBaked) return;

            int requestsThisFrame = 0;
            var em = EntityManager;

            // Pass 1: scan units with DesiredDestination, ensure the navmesh
            // companion components exist (lazy stamp), and detect goal-change
            // or first-time path needs.
            Entities
                .WithoutBurst()
                .WithAll<UnitTag>()
                .WithStructuralChanges()
                .ForEach((Entity entity, ref DesiredDestination dd, in LocalTransform xf) =>
                {
                    if (dd.Has == 0) return;

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
                    }
                    else
                    {
                        var state = em.GetComponentData<NavMeshPathfollowState>(entity);
                        if (math.distancesq(state.LastRequestedGoal, dd.Position)
                                > GoalChangeThreshold * GoalChangeThreshold
                            || state.HasPath == 0 && state.RequestPending == 0)
                        {
                            state.LastRequestedGoal = dd.Position;
                            state.RequestPending = 1;
                            em.SetComponentData(entity, state);
                        }
                    }
                }).Run();

            // Pass 2: actually compute paths for entities flagged
            // RequestPending. Throttled per frame.
            Entities
                .WithoutBurst()
                .WithAll<UnitTag>()
                .ForEach((Entity entity, ref NavMeshPathfollowState state,
                    ref DynamicBuffer<NavMeshWaypoint> waypoints,
                    in LocalTransform xf) =>
                {
                    if (state.RequestPending == 0) return;
                    if (requestsThisFrame >= MaxRequestsPerFrame) return;

                    requestsThisFrame++;
                    state.RequestPending = 0;

                    var fromW = new Vector3(xf.Position.x, xf.Position.y, xf.Position.z);
                    var toW = new Vector3(state.LastRequestedGoal.x,
                        state.LastRequestedGoal.y, state.LastRequestedGoal.z);

                    bool ok = nmm.RequestPath(fromW, toW, _scratchPath);
                    waypoints.Clear();
                    if (!ok || _scratchPath.corners == null || _scratchPath.corners.Length == 0)
                    {
                        state.HasPath = 0;
                        state.CurrentWaypoint = 0;
                        return;
                    }

                    // Skip the first corner (it's the unit's own position).
                    var corners = _scratchPath.corners;
                    int start = corners.Length > 1 ? 1 : 0;
                    for (int i = start; i < corners.Length; i++)
                    {
                        waypoints.Add(new NavMeshWaypoint
                        {
                            Position = new float3(corners[i].x, corners[i].y, corners[i].z),
                        });
                    }
                    state.HasPath = (byte)(waypoints.Length > 0 ? 1 : 0);
                    state.CurrentWaypoint = 0;
                }).Run();
        }
    }
}
