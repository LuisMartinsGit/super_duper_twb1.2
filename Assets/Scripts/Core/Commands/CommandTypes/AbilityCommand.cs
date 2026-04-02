// AbilityCommand.cs
// Command component for activating a unit's ability
// Location: Assets/Scripts/Core/Commands/CommandTypes/AbilityCommand.cs

using Unity.Entities;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing an ability activation command.
    /// When attached to an entity, UnitAbilitySystem will process it.
    /// </summary>
    public struct AbilityCommand : IComponentData
    {
        /// <summary>Target entity for targeted abilities (Entity.Null for self-cast)</summary>
        public Entity Target;
    }
}
