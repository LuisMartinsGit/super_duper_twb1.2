// ConvertCommand.cs
// Command to convert a worker into a berserker at a Fiendstone Keep

using Unity.Entities;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing a convert command for a worker unit.
    /// When attached to a worker, BerserkerConversionSystem will process it.
    /// </summary>
    public struct ConvertCommand : IComponentData
    {
        /// <summary>The Fiendstone Keep to convert at</summary>
        public Entity TargetKeep;
    }

    /// <summary>
    /// Helper class for executing convert commands
    /// </summary>
    public static class ConvertCommandHelper
    {
        /// <summary>
        /// Execute a convert command on a worker unit.
        /// Clears conflicting commands and sets up conversion state.
        /// </summary>
        public static void Execute(EntityManager em, Entity worker, Entity keep)
        {
            if (!em.Exists(worker) || !em.Exists(keep)) return;

            // Verify worker is actually a worker
            if (!em.HasComponent<WorkerTag>(worker)) return;

            // Verify keep is a Fiendstone Keep and not under construction
            if (!em.HasComponent<FiendstoneKeepTag>(keep)) return;
            if (em.HasComponent<UnderConstruction>(keep)) return;

            // Verify same faction
            if (!em.HasComponent<FactionTag>(worker) || !em.HasComponent<FactionTag>(keep)) return;
            if (em.GetComponentData<FactionTag>(worker).Value != em.GetComponentData<FactionTag>(keep).Value) return;

            // Clear conflicting commands
            CommandHelper.ClearAllCommands(em, worker);

            // Set up convert command
            var cmd = new ConvertCommand { TargetKeep = keep };

            if (!em.HasComponent<ConvertCommand>(worker))
                em.AddComponentData(worker, cmd);
                else
                    em.SetComponentData(worker, cmd);

            // Move toward keep
            if (em.HasComponent<LocalTransform>(keep))
            {
                var keepPos = em.GetComponentData<LocalTransform>(keep).Position;

                if (em.HasComponent<DesiredDestination>(worker))
                {
                    em.SetComponentData(worker, new DesiredDestination
                    {
                        Position = keepPos,
                        Has = 1
                    });
                }
                else
                {
                    em.AddComponentData(worker, new DesiredDestination
                    {
                        Position = keepPos,
                        Has = 1
                    });
                }
            }
        }
    }
}
