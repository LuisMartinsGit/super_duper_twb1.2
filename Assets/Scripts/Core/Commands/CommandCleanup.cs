// CommandCleanup.cs
// Shared "cancel what the unit was doing" steps for the order helpers.
//
// Every order helper opened with its own private ClearConflictingCommands, and
// there were FOUR copies (Attack / Move / AttackMove / Patrol). They were not
// identical — each cleared a different set — so the copies could not simply be
// merged: Attack must NOT clear AttackCommand/Target (it is setting them), and
// only Patrol cleared the attack-move and move orders. Merging blindly would
// have changed behaviour for three of the four.
//
// So this splits along what the copies actually agreed on:
//   ClearWorkOrders  — the part all four shared (gather / build / heal)
//   ClearCombat      — what Move, AttackMove and Patrol additionally shared
//   ClearMovement    — what only Patrol did
// Each helper now composes the pieces it always used. Behaviour is unchanged;
// the point is that adding a new order component is now ONE edit, not four.

using Unity.Entities;

namespace TheWaningBorder.Core.Commands
{
    public static class CommandCleanup
    {
        /// <summary>
        /// Drop the unit's economy/support work: gather, build (command AND the
        /// BuildOrder that outlives it), heal, and the Litharch's internal
        /// healing state — that last one is easy to forget, because a Litharch
        /// keeps healing off LitharchState even after HealCommand is gone.
        /// Shared by every order helper.
        /// </summary>
        public static void ClearWorkOrders(EntityManager em, Entity unit)
        {
            if (em.HasComponent<Types.BuildCommand>(unit))
                em.RemoveComponent<Types.BuildCommand>(unit);
            if (em.HasComponent<BuildOrder>(unit))
                em.RemoveComponent<BuildOrder>(unit);

            if (em.HasComponent<Types.HealCommand>(unit))
                em.RemoveComponent<Types.HealCommand>(unit);
            if (em.HasComponent<LitharchState>(unit))
            {
                var ls = em.GetComponentData<LitharchState>(unit);
                if (ls.IsHealing != 0)
                {
                    ls.HealTarget = Entity.Null;
                    ls.IsHealing = 0;
                    em.SetComponentData(unit, ls);
                }
            }
        }

        /// <summary>
        /// Break off the current fight: drop the attack order and null the
        /// target. NOT used by the attack helpers themselves — they are
        /// installing exactly these.
        /// </summary>
        public static void ClearCombat(EntityManager em, Entity unit)
        {
            if (em.HasComponent<Types.AttackCommand>(unit))
                em.RemoveComponent<Types.AttackCommand>(unit);

            if (em.HasComponent<Target>(unit))
                em.SetComponentData(unit, new Target { Value = Entity.Null });
        }

        /// <summary>
        /// Drop any standing movement order — attack-move (command + tag),
        /// plain move, and the UserMoveOrder tag that shields a player move
        /// from auto-targeting.
        /// </summary>
        public static void ClearMovement(EntityManager em, Entity unit)
        {
            if (em.HasComponent<Types.AttackMoveCommand>(unit))
                em.RemoveComponent<Types.AttackMoveCommand>(unit);
            if (em.HasComponent<AttackMoveTag>(unit))
                em.RemoveComponent<AttackMoveTag>(unit);

            if (em.HasComponent<Types.MoveCommand>(unit))
                em.RemoveComponent<Types.MoveCommand>(unit);
            if (em.HasComponent<UserMoveOrder>(unit))
                em.RemoveComponent<UserMoveOrder>(unit);
        }
    }
}
