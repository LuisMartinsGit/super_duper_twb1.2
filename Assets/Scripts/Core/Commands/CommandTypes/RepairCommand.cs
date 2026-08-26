// RepairCommand.cs
// Repair command helper - assigns builders to repair damaged buildings

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Helper class for executing repair commands.
    /// Clears conflicting commands and sets up RepairOrder on the builder.
    /// </summary>
    public static class RepairCommandHelper
    {
        /// <summary>
        /// Execute a repair command on a builder unit targeting a damaged building.
        /// </summary>
        public static void Execute(EntityManager em, Entity builder, Entity building)
        {
            if (!em.Exists(builder) || !em.Exists(building)) return;
            if (!em.HasComponent<CanBuild>(builder)) return;
            if (!em.HasComponent<Health>(building)) return;

            var hp = em.GetComponentData<Health>(building);
            if (hp.Value >= hp.Max) return; // Not damaged

            // Clear conflicting commands
            CommandHelper.ClearAllCommands(em, builder);

            // A drafted mining worker must leave the mining state machine —
            // same fix as BuildCommandHelper.Execute (MinerState kept driving
            // the worker's DesiredDestination toward its deposit while the
            // repair mover drove it toward the building).
            if (em.HasComponent<MinerState>(builder))
            {
                var ms = em.GetComponentData<MinerState>(builder);
                if (ms.State != MinerWorkState.Idle)
                {
                    ms.State = MinerWorkState.Idle;
                    ms.AssignedDeposit = Entity.Null;
                    em.SetComponentData(builder, ms);
                }
            }

            // Set up repair order
            if (!em.HasComponent<RepairOrder>(builder))
                em.AddComponentData(builder, new RepairOrder
                {
                    Site = building,
                    CostPaid = 0,
                    TargetHP = hp.Max,
                    StartHP = hp.Value
                });
                else
                    em.SetComponentData(builder, new RepairOrder
                {
                    Site = building,
                    CostPaid = 0,
                    TargetHP = hp.Max,
                    StartHP = hp.Value
                });

            // Set destination to building position
            var buildingPos = em.GetComponentData<LocalTransform>(building).Position;
            if (em.HasComponent<DesiredDestination>(builder))
            {
                em.SetComponentData(builder, new DesiredDestination
                {
                    Position = buildingPos,
                    Has = 1
                });
            }
            else
            {
                em.AddComponentData(builder, new DesiredDestination
                {
                    Position = buildingPos,
                    Has = 1
                });
            }
        }
    }
}
