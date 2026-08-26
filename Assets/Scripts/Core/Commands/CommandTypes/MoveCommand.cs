// MoveCommand.cs
// Move command component and execution logic

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Systems.Navigation;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing a move command for a unit.
    /// When attached to an entity, MovementSystem will process it.
    /// </summary>
    public struct MoveCommand : IComponentData
    {
        /// <summary>The world position to move to</summary>
        public float3 Destination;
    }

    /// <summary>
    /// Helper class for executing move commands
    /// </summary>
    public static class MoveCommandHelper
    {
        // How far an off-navmesh move target may be from the navmesh and still
        // be pulled onto it. Beyond this the click is left as-is (the navmesh
        // confinement / fallback gate keep the unit on the surface anyway).
        internal const float MoveTargetSnapRadius = 30f;

        /// <summary>
        /// Execute a move command on a unit.
        /// Clears conflicting commands and sets up movement state.
        /// </summary>
        public static void Execute(EntityManager em, Entity unit, float3 destination)
        {
            if (!em.Exists(unit)) return;

            // A move order on a wall-garrisoning unit brings it back DOWN off
            // the rampart so it can leave the wall: drop to the ground layer,
            // snap y to terrain, and clear any garrison order/state.
            if (em.HasComponent<NavLayerIndex>(unit))
            {
                var nli = em.GetComponentData<NavLayerIndex>(unit);
                if (nli.Layer == NavLayerIndex.LayerRampart)
                {
                    nli.Layer = 0;
                    em.SetComponentData(unit, nli);
                    if (em.HasComponent<LocalTransform>(unit))
                    {
                        var dxf = em.GetComponentData<LocalTransform>(unit);
                        dxf.Position = new float3(
                            dxf.Position.x,
                            TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(dxf.Position.x, dxf.Position.z),
                            dxf.Position.z);
                        em.SetComponentData(unit, dxf);
                    }
                }
            }
            if (em.HasComponent<LayeredMoveOrder>(unit)) em.RemoveComponent<LayeredMoveOrder>(unit);

            // task-112 M3: snap onto the nearest walkable cell on the cost
            // field via NavGridQuery -- replaces the legacy NavMeshManager
            // snap from M1/M2. NavGridQuery walks a deterministic
            // row-major ring around the click cell so two callers on
            // different machines pick the same snap target.
            NavGridQuery.SnapToWalkable(destination, out var snapped, out var snapOk);
            if (snapOk)
                destination = snapped;

            // task-112 M3: emit a NavPathRequest on the unit so
            // AbstractPathfinderSystem produces a NavPathResult + buffer
            // this tick. Replaces the M1 NavFlowGoalRequest tag emit.
            //
            // Reset any prior NavPathResult/NavPathPortal so the follower
            // does NOT keep walking the previous goal's cached flow slabs
            // while the new path is in the scheduler queue (the M6 budget
            // can release new requests several ticks later). Without this
            // a unit that already arrived at its previous goal reports
            // ignoring the next click for the length of that scheduler
            // delay -- visually indistinguishable from "movement orders
            // are being dropped".
            if (em.HasComponent<NavPathResult>(unit))
            {
                em.SetComponentData(unit, new NavPathResult
                {
                    Status = NavPathRequest.StatusPending,
                    Length = 0,
                    CurrentPortalIndex = -1,
                    Generation = 0,
                });
            }
            if (em.HasBuffer<NavPathPortal>(unit))
            {
                em.GetBuffer<NavPathPortal>(unit).Clear();
            }
            // Force FlowDesiredDir to stop so the follower doesn't sample a
            // stale cache slab pointing at the old goal until the new path
            // resolves. SteeringSystem reads FlowDesiredDir as its forward
            // bias -- with HasValue=0 it leaves the unit at rest, which is
            // the correct behaviour for "you clicked, but the new path
            // hasn't computed yet".
            if (em.HasComponent<FlowDesiredDir>(unit))
            {
                em.SetComponentData(unit, new FlowDesiredDir { HasValue = 0, Value = float3.zero });
            }

            EmitNavPathRequest(em, unit, destination);

            // Clear conflicting commands
            ClearConflictingCommands(em, unit);

            // An individual move order detaches the unit from any formation
            // group it was travelling with (FormationMoveCommandHelper
            // re-attaches AFTER calling Execute when this move IS part of a
            // formation order). Stale group-speed overrides go with it.
            if (em.HasComponent<FormationMemberState>(unit))
                em.RemoveComponent<FormationMemberState>(unit);
            if (em.HasComponent<FormationSpeedOverride>(unit))
                em.RemoveComponent<FormationSpeedOverride>(unit);

            // Add MoveCommand for MovementSystem to process
            if (!em.HasComponent<MoveCommand>(unit))
                em.AddComponent<MoveCommand>(unit);
            em.SetComponentData(unit, new MoveCommand { Destination = destination });

            // Also set DesiredDestination directly for immediate response
            if (!em.HasComponent<DesiredDestination>(unit))
                em.AddComponent<DesiredDestination>(unit);
            em.SetComponentData(unit, new DesiredDestination { Position = destination, Has = 1 });

            // Reset stuck/smoothing state so a previously-stuck unit immediately
            // accepts the new order instead of cancelling it on the next frame
            // (MovementSystem stuck recovery cancels DesiredDestination at counter > 30).
            if (em.HasComponent<StuckState>(unit))
                em.SetComponentData(unit, new StuckState { Counter = 0, LastAttempt = 0 });
            if (em.HasComponent<SmoothedDirection>(unit))
                em.SetComponentData(unit, new SmoothedDirection { Value = float3.zero });
            // Pre-warm: NavMeshPathRequestSystem picks up the new
            // DesiredDestination next frame and computes the path lazily.
            // The legacy MovementCache + flow-field/A* pre-warm shims are
            // gone with the navmesh migration (PR3).

            // Add UserMoveOrder to prevent auto-targeting from overriding
            if (!em.HasComponent<UserMoveOrder>(unit))
                em.AddComponent<UserMoveOrder>(unit);

            // Update guard point to new destination
            if (em.HasComponent<GuardPoint>(unit))
                em.SetComponentData(unit, new GuardPoint { Position = destination, Has = 1 });
                else
                    em.AddComponentData(unit, new GuardPoint { Position = destination, Has = 1 });
        }

        /// <summary>
        /// Check if a move command can be executed
        /// </summary>
        public static bool CanExecute(EntityManager em, Entity unit)
        {
            if (!em.Exists(unit)) return false;
            
            // Buildings can't move
            if (em.HasComponent<BuildingTag>(unit)) return false;
            
            return true;
        }

        /// <summary>
        /// task-112 M3 / M6: enqueue a path request via the
        /// <c>NavRequestSchedulerSystem</c> so the pathfinder picks it
        /// up on the next tick. Replaces the M3 direct-attach path
        /// with the M6 budgeted scheduler -- duplicate requests sharing
        /// the same (goal, profile) coalesce and only consume one
        /// per-tick budget slot.
        ///
        /// Resolves start/goal cells via NavGridQuery so the request
        /// carries integer cells, not world coordinates -- the pathfinder
        /// works in cell coordinates throughout. Silently skips when the
        /// grid singleton hasn't been bootstrapped yet (the unit's first
        /// MoveCommand may race past initialisation); the scheduler
        /// helper falls back to the M3 direct-attach in that window.
        /// </summary>
        internal static void EmitNavPathRequest(EntityManager em, Entity unit, float3 destination)
        {
            if (!em.Exists(unit) || !em.HasComponent<LocalTransform>(unit)) return;

            // Pathfinding redesign 2026-07-05: ground movement is driven by
            // whole-map goal flow fields (GoalFlowFieldSystem); portal paths
            // are only consumed by LayerTransitionSystem for rampart/climb
            // traversal. Skipping the A* pipeline for plain ground orders
            // removes the per-click pathfinder spike (a formation move used
            // to fire one portal A* PER UNIT).
            if (!NeedsPortalPath(em, unit, destination)) return;

            var transform = em.GetComponentData<LocalTransform>(unit);

            var startCell = NavGridQuery.WorldToCellInt2(transform.Position);
            var goalCell = NavGridQuery.WorldToCellInt2(destination);
            if (startCell.x == int.MinValue || goalCell.x == int.MinValue) return;

            // Pull the current graph generation so the pathfinder can
            // reject stale requests after a graph swap (CCD-5).
            int generation = 0;
            var portalQuery = em.CreateEntityQuery(typeof(PortalGraphSingleton));
            if (!portalQuery.IsEmptyIgnoreFilter)
            {
                generation = portalQuery.GetSingleton<PortalGraphSingleton>().Generation;
            }
            portalQuery.Dispose();

            // task-112 M6 -- enqueue via the scheduler's
            // NavRequestQueueSingleton. The helper falls back to
            // direct-attach when the singleton hasn't bootstrapped yet
            // (race with first frame).
            NavRequestSchedulerSystem.EnqueueRequest(em, unit,
                startCell, goalCell, profileHash: 0,
                priority: PendingNavRequest.PriorityUser,
                generation: generation);
        }

        /// <summary>
        /// True only when the order involves the rampart layer: the unit is
        /// already up on a wall, or the destination cell is wall-top (ground
        /// layer impassable, rampart layer walkable). Only those orders need
        /// a portal path (climb/gate traversal via LayerTransitionSystem);
        /// everything else rides the goal flow fields.
        /// </summary>
        private static bool NeedsPortalPath(EntityManager em, Entity unit, float3 destination)
        {
            // Unit already on the rampart layer -> needs portal legs to get
            // around/down.
            if (em.HasComponent<NavLayerIndex>(unit)
                && em.GetComponentData<NavLayerIndex>(unit).Layer != 0)
                return true;

            var goalCell = NavGridQuery.WorldToCellInt2(destination);
            if (goalCell.x == int.MinValue) return false;

            var fieldQuery = em.CreateEntityQuery(typeof(NavCostField));
            if (fieldQuery.IsEmptyIgnoreFilter)
            {
                fieldQuery.Dispose();
                return false;
            }
            var field = fieldQuery.GetSingleton<NavCostField>();
            fieldQuery.Dispose();

            if (field.LayerCount < 2 || !field.Cost.IsCreated) return false;

            int layerArea = field.Width * field.Height;
            int idx = goalCell.y * field.Width + goalCell.x;
            if (idx < 0 || idx >= layerArea) return false;
            if (field.Cost.Length < layerArea * 2) return false;

            bool groundBlocked = field.Cost[idx] == NavCostField.CostImpassable;
            bool rampartWalkable = field.Cost[layerArea + idx] != NavCostField.CostImpassable;
            return groundBlocked && rampartWalkable;
        }

        /// <summary>Cancel whatever this unit was doing before the new order.
        /// Steps are shared via CommandCleanup — see that file for why the four
        /// order helpers clear different sets.</summary>
        private static void ClearConflictingCommands(EntityManager em, Entity unit)
        {
            CommandCleanup.ClearCombat(em, unit);
            CommandCleanup.ClearWorkOrders(em, unit);
        }
    }
}