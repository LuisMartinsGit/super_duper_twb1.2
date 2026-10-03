// RepairCommand.cs
// Repair command helper - assigns workers to repair damaged buildings

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Helper class for executing repair commands.
    /// Clears conflicting commands and sets up RepairOrder on the worker.
    /// </summary>
    public static class RepairCommandHelper
    {
        /// <summary>
        /// Execute a repair command on a worker unit targeting a damaged building.
        /// </summary>
        public static void Execute(EntityManager em, Entity worker, Entity building)
        {
            if (!em.Exists(worker) || !em.Exists(building)) return;
            if (!em.HasComponent<CanBuild>(worker)) return;
            if (!em.HasComponent<Health>(building)) return;

            var hp = em.GetComponentData<Health>(building);
            if (hp.Value >= hp.Max) return; // Not damaged

            // Clear conflicting commands
            CommandHelper.ClearAllCommands(em, worker);

            // A drafted mining worker must leave the mining state machine —
            // same fix as BuildCommandHelper.Execute (WorkerState kept driving
            // the worker's DesiredDestination toward its deposit while the
            // repair mover drove it toward the building).
            if (em.HasComponent<WorkerState>(worker))
            {
                var ms = em.GetComponentData<WorkerState>(worker);
                if (ms.State != WorkerActivity.Idle)
                {
                    ms.State = WorkerActivity.Idle;
                    ms.AssignedDeposit = Entity.Null;
                    em.SetComponentData(worker, ms);
                }
            }

            // Set up repair order
            if (!em.HasComponent<RepairOrder>(worker))
                em.AddComponentData(worker, new RepairOrder
                {
                    Site = building,
                    CostPaid = 0,
                    TargetHP = hp.Max,
                    StartHP = hp.Value
                });
                else
                    em.SetComponentData(worker, new RepairOrder
                {
                    Site = building,
                    CostPaid = 0,
                    TargetHP = hp.Max,
                    StartHP = hp.Value
                });

            // Set destination to building position
            var buildingPos = em.GetComponentData<LocalTransform>(building).Position;
            if (em.HasComponent<DesiredDestination>(worker))
            {
                em.SetComponentData(worker, new DesiredDestination
                {
                    Position = buildingPos,
                    Has = 1
                });
            }
            else
            {
                em.AddComponentData(worker, new DesiredDestination
                {
                    Position = buildingPos,
                    Has = 1
                });
            }
        }
    }
}
