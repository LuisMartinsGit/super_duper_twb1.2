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
    public struct AttackCommand : IComponentData, IEnableableComponent
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
            // Placed by an order: the idle form-up leaves it where it ends up.
            TransientState.SetFlag<PlacedByOrder>(em, unit);
            // The order supersedes whatever the unit was doing — its standing
            // move, its formation march, its work.
            ClearConflictingCommands(em, unit);
            // Set up attack
            SetupAttack(em, unit, target);
            // THE FIGHT IS WHERE THE UNIT WAS TOLD TO BE (2026-10-22,
            // Stances.md §2): the guard point moves to the target. It used to
            // keep the previous move's destination, so an army ordered onto
            // one enemy killed it and then walked off to the old destination,
            // ignoring the rest of the enemy army around it.
            SetGuardPointAtTarget(em, unit, target);
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
            // clear combat here — only the work orders, and (2026-10-22) the
            // standing movement: the plain move or attack-move the unit was
            // on, the player-move shield, and its formation membership. An
            // attack order is a new order; nothing of the old one may pull
            // the unit back to the old destination once the target is dead.
            CommandCleanup.ClearWorkOrders(em, unit);
            CommandCleanup.ClearMovement(em, unit);
            TransientState.Clear<FormationMemberState>(em, unit);
            TransientState.Clear<FormationSpeedOverride>(em, unit);
            TransientState.Clear<FormationSlotMemory>(em, unit);
        }

        private static void SetupAttack(EntityManager em, Entity unit, Entity target)
        {
            // Activate AttackCommand
            TransientState.Set(em, unit, new AttackCommand { Target = target });

            // Also set Target component for combat system
            if (em.HasComponent<Target>(unit))
                em.SetComponentData(unit, new Target { Value = target });
                else
                    em.AddComponentData(unit, new Target { Value = target });

            // An ORDERED target, not an automatic one: never leashed, never
            // dropped by stuck recovery (docs/Design/Stances.md §5). Clearing
            // the auto record is what makes it so — even when the order names
            // the very enemy the unit had auto-acquired.
            if (TransientState.Active<UnitEngagement>(em, unit))
            {
                var eng = em.GetComponentData<UnitEngagement>(unit);
                eng.AutoTarget = Entity.Null;
                em.SetComponentData(unit, eng);
            }
        }

        /// <summary>The guard point goes to the target: where the unit was
        /// told to fight. When the target dies the targeting system moves it
        /// again, to wherever the unit then stands (TargetingSystem.Commands),
        /// so the unit holds the field and takes on what is left of the
        /// enemy within its leash instead of walking off.</summary>
        private static void SetGuardPointAtTarget(EntityManager em, Entity unit, Entity target)
        {
            if (!em.HasComponent<LocalTransform>(target)) return;
            var pos = em.GetComponentData<LocalTransform>(target).Position;
            if (TransientState.Active<GuardPoint>(em, unit))
                em.SetComponentData(unit, new GuardPoint { Position = pos, Has = 1 });
            else
                TransientState.Set(em, unit, new GuardPoint { Position = pos, Has = 1 });
        }
    }
}