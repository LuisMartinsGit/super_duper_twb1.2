// AttackCommand.cs
// Attack command component and execution logic
// Location: Assets/Scripts/Core/Commands/CommandTypes/AttackCommand.cs

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Core.Commands.Types
{
    /// <summary>
    /// ECS Component representing an attack command for a unit.
    /// When attached to an entity, combat systems will process it.
    /// </summary>
    public struct AttackCommand : IComponentData
    {
        /// <summary>The entity to attack</summary>
        public Entity Target;
    }

    /// <summary>
    /// Helper class for executing attack commands
    /// </summary>
    public static class AttackCommandHelper
    {
        /// <summary>
        /// Execute an attack command on a unit.
        /// Clears conflicting commands and sets up combat state.
        /// </summary>
        public static void Execute(EntityManager em, Entity unit, Entity target)
        {
            if (!em.Exists(unit) || !em.Exists(target)) return;

            // Clear conflicting commands (but NOT MoveCommand - combat system handles chasing)
            ClearConflictingCommands(em, unit);

            // Set up attack
            SetupAttack(em, unit, target);

            // Set guard point to current position (unit will return here after combat)
            SetGuardPointToCurrent(em, unit);
        }

        /// <summary>
        /// Check if an attack command can be executed
        /// </summary>
        public static bool CanExecute(EntityManager em, Entity unit, Entity target)
        {
            if (!em.Exists(unit) || !em.Exists(target)) return false;
            if (!em.HasComponent<Damage>(unit)) return false;
            
            // Verify not attacking friendly unit
            if (em.HasComponent<FactionTag>(unit) && em.HasComponent<FactionTag>(target))
            {
                var unitFaction = em.GetComponentData<FactionTag>(unit).Value;
                var targetFaction = em.GetComponentData<FactionTag>(target).Value;
                if (unitFaction == targetFaction) return false;
            }

            return true;
        }

        private static void ClearConflictingCommands(EntityManager em, Entity unit)
        {
            // Clear build
            if (em.HasComponent<BuildCommand>(unit))
                em.RemoveComponent<BuildCommand>(unit);

            // Clear gather
            if (em.HasComponent<GatherCommand>(unit))
                em.RemoveComponent<GatherCommand>(unit);

            // Clear heal
            if (em.HasComponent<HealCommand>(unit))
                em.RemoveComponent<HealCommand>(unit);

            // Clear UserMoveOrder to allow combat system to take over
            if (em.HasComponent<UserMoveOrder>(unit))
                em.RemoveComponent<UserMoveOrder>(unit);
        }

        private static void SetupAttack(EntityManager em, Entity unit, Entity target)
        {
            // Add or update AttackCommand component
            if (!em.HasComponent<AttackCommand>(unit))
                em.AddComponentData(unit, new AttackCommand { Target = target });
            else
                em.SetComponentData(unit, new AttackCommand { Target = target });

            // Also set Target component for combat system
            if (em.HasComponent<Target>(unit))
                em.SetComponentData(unit, new Target { Value = target });
            else
                em.AddComponentData(unit, new Target { Value = target });
        }

        private static void SetGuardPointToCurrent(EntityManager em, Entity unit)
        {
            if (!em.HasComponent<LocalTransform>(unit)) return;

            var pos = em.GetComponentData<LocalTransform>(unit).Position;

            if (em.HasComponent<GuardPoint>(unit))
            {
                em.SetComponentData(unit, new GuardPoint
                {
                    Position = pos,
                    Has = 1
                });
            }
            else
            {
                em.AddComponentData(unit, new GuardPoint
                {
                    Position = pos,
                    Has = 1
                });
            }
        }
    }
}