// AttackCommand.cs
// Attack command component and execution logic

using Unity.Collections;
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
            if (!em.HasComponent<LocalTransform>(target)) return;

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

            // Verify not attacking a friendly OR allied unit. An explicit
            // attack order on an ally is rejected outright — allied damage
            // does not happen by any route. docs/Design/Teams.md
            if (em.HasComponent<FactionTag>(unit) && em.HasComponent<FactionTag>(target))
            {
                var unitFaction = em.GetComponentData<FactionTag>(unit).Value;
                var targetFaction = em.GetComponentData<FactionTag>(target).Value;
                if (!Alliances.AreHostile(unitFaction, targetFaction)) return false;
            }

            return true;
        }

        /// <summary>Cancel whatever this unit was doing before the new order.
        /// Steps are shared via CommandCleanup — see that file for why the four
        /// order helpers clear different sets.</summary>
        private static void ClearConflictingCommands(EntityManager em, Entity unit)
        {
            // Attack installs AttackCommand/Target itself, so it must NOT
            // clear combat here — only the work orders, plus the player-move
            // shield so the combat system can take the unit over.
            CommandCleanup.ClearWorkOrders(em, unit);
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

            // If the unit already has a guard point (e.g., from a move command),
            // keep it — the unit should return to its intended destination after combat,
            // not to where it happened to be when attacked.
            if (em.HasComponent<GuardPoint>(unit))
            {
                var existing = em.GetComponentData<GuardPoint>(unit);
                if (existing.Has != 0) return; // Preserve existing guard point
            }

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