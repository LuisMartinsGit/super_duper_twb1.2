// ConvertCommand.cs
// Command to convert a miner into a berserker at a Fiendstone Keep
// Location: Assets/Scripts/Core/Commands/CommandTypes/ConvertCommand.cs

using Unity.Entities;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing a convert command for a miner unit.
    /// When attached to a miner, BerserkerConversionSystem will process it.
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
        /// Execute a convert command on a miner unit.
        /// Clears conflicting commands and sets up conversion state.
        /// </summary>
        public static void Execute(EntityManager em, Entity miner, Entity keep)
        {
            if (!em.Exists(miner) || !em.Exists(keep)) return;

            // Verify miner is actually a miner
            if (!em.HasComponent<MinerTag>(miner)) return;

            // Verify keep is a Fiendstone Keep and not under construction
            if (!em.HasComponent<FiendstoneKeepTag>(keep)) return;
            if (em.HasComponent<UnderConstruction>(keep)) return;

            // Verify same faction
            if (!em.HasComponent<FactionTag>(miner) || !em.HasComponent<FactionTag>(keep)) return;
            if (em.GetComponentData<FactionTag>(miner).Value != em.GetComponentData<FactionTag>(keep).Value) return;

            // Clear conflicting commands
            CommandHelper.ClearAllCommands(em, miner);

            // Set up convert command
            var cmd = new ConvertCommand { TargetKeep = keep };

            if (!em.HasComponent<ConvertCommand>(miner))
                em.AddComponentData(miner, cmd);
                else
                    em.SetComponentData(miner, cmd);

            // Move toward keep
            if (em.HasComponent<LocalTransform>(keep))
            {
                var keepPos = em.GetComponentData<LocalTransform>(keep).Position;

                if (em.HasComponent<DesiredDestination>(miner))
                {
                    em.SetComponentData(miner, new DesiredDestination
                    {
                        Position = keepPos,
                        Has = 1
                    });
                }
                else
                {
                    em.AddComponentData(miner, new DesiredDestination
                    {
                        Position = keepPos,
                        Has = 1
                    });
                }
            }
        }
    }
}
