// CommandRouter.Formation.cs
// Partial class extension: AoE4-style formation group orders.
//
// A formation order fans one clicked destination out into per-unit slot
// destinations (FormationMoveCommandHelper.BuildPlan) and — on the direct
// execution path — creates the persistent formation group whose virtual
// leader FormationGroupSystem advances every tick.
//
// Lockstep multiplayer: there is no multi-entity lockstep command type, so
// formation orders degrade to the per-unit slot moves (each serializes as
// an ordinary Move/AttackMove; wall-top units as a LayeredMove to ground).
// Units still arrive in formation shape; they just don't hold it en route.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Core.Commands
{
    public static partial class CommandRouter
    {
        /// <summary>
        /// Issue a formation move to a group of units: type-ranked slots
        /// around the destination, slowest-member group speed, cohesion
        /// gate, virtual-leader travel (single-player / host-AI path).
        /// </summary>
        public static void IssueFormationMove(EntityManager em, IReadOnlyList<Entity> units,
            float3 destination, FormationShape shape,
            CommandSource source = CommandSource.LocalPlayer)
        {
            IssueFormationOrder(em, units, destination, shape, attackMove: false, source);
        }

        /// <summary>
        /// Formation attack-move: same layout and travel rules; members
        /// auto-engage enemies encountered (and detach from the group the
        /// moment they acquire a target).
        /// </summary>
        public static void IssueFormationAttackMove(EntityManager em, IReadOnlyList<Entity> units,
            float3 destination, FormationShape shape,
            CommandSource source = CommandSource.LocalPlayer)
        {
            IssueFormationOrder(em, units, destination, shape, attackMove: true, source);
        }

        private static void IssueFormationOrder(EntityManager em, IReadOnlyList<Entity> units,
            float3 destination, FormationShape shape, bool attackMove, CommandSource source)
        {
            if (units == null || units.Count == 0) return;

            // Per-unit controllability filter (same rule as IssueMove).
            var eligible = new List<Entity>(units.Count);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == Entity.Null || !em.Exists(u)) continue;
                if (IsBlockedByNotControllable(em, u, source)) continue;
                eligible.Add(u);
            }
            if (eligible.Count == 0) return;

            if (ShouldQueueForLockstep(source))
            {
                // Degrade to per-unit slot orders through the lockstep queue.
                if (!FormationMoveCommandHelper.BuildPlan(em, eligible, destination, shape, out var plan))
                    return;
                for (int i = 0; i < plan.Units.Count; i++)
                {
                    if (plan.OnRampart[i])
                    {
                        // Wall-top units climb down to ground (layer 0) via a
                        // layered move — replicated like everything else.
                        QueueLayeredMoveForLockstep(em, plan.Units[i], plan.SlotWorld[i], 0);
                        continue;
                    }
                    if (attackMove)
                        QueueAttackMoveForLockstep(em, plan.Units[i], plan.SlotWorld[i]);
                    else
                        QueueMoveForLockstep(em, plan.Units[i], plan.SlotWorld[i]);
                }
                return;
            }

            FormationMoveCommandHelper.Execute(em, eligible, destination, shape, attackMove);
        }
    }
}
