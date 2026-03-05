// HoldPositionCommand.cs
// Hold position command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/HoldPositionCommand.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing a hold position command for a unit.
    /// Units stop moving and attack enemies within range, but do not chase.
    /// Consumed immediately by the command helper.
    /// </summary>
    public struct HoldPositionCommand : IComponentData { }

    /// <summary>
    /// Helper class for executing hold position commands
    /// </summary>
    public static class HoldPositionCommandHelper
    {
        /// <summary>
        /// Execute a hold position command on a unit.
        /// Clears all existing commands, then adds HoldPositionTag
        /// so combat systems know not to chase targets.
        /// </summary>
        public static void Execute(EntityManager em, Entity unit)
        {
            if (!em.Exists(unit)) return;

            // Clear all existing commands (this also removes HoldPositionTag,
            // but we re-add it immediately below)
            CommandHelper.ClearAllCommands(em, unit);

            // Add HoldPositionTag marker
            if (!em.HasComponent<HoldPositionTag>(unit))
                em.AddComponent<HoldPositionTag>(unit);

            // Set guard point to current position (unit holds here)
            if (em.HasComponent<LocalTransform>(unit))
            {
                var pos = em.GetComponentData<LocalTransform>(unit).Position;
                if (em.HasComponent<GuardPoint>(unit))
                {
                    em.SetComponentData(unit, new GuardPoint { Position = pos, Has = 1 });
                }
                else
                {
                    em.AddComponentData(unit, new GuardPoint { Position = pos, Has = 1 });
                }
            }
        }
    }
}
