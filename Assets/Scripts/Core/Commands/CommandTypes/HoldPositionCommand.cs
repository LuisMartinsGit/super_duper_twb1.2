// HoldPositionCommand.cs
// Hold Position — the Hold STANCE (docs/Design/Stances.md). Kept as its own
// helper because the H key and the legacy HoldPosition lockstep opcode both
// land here; it is the stance plus "stop and hold this spot".

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// Legacy marker type, kept so old references compile. Nothing adds it:
    /// the command is executed immediately by the helper below.
    /// </summary>
    public struct HoldPositionCommand : IComponentData { }

    /// <summary>
    /// Helper class for executing hold position commands
    /// </summary>
    public static class HoldPositionCommandHelper
    {
        /// <summary>
        /// Stop the unit, put it in the Hold stance, and make where it stands
        /// its guard point. Stance lives on (<see cref="UnitStance"/> +
        /// <see cref="HoldPositionTag"/>) until another stance is chosen —
        /// Stop no longer clears it.
        /// </summary>
        public static void Execute(EntityManager em, Entity unit)
        {
            if (!em.Exists(unit)) return;

            CommandHelper.ClearAllCommands(em, unit);
            StanceCommandHelper.Apply(em, unit, UnitStanceMode.Hold);

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
