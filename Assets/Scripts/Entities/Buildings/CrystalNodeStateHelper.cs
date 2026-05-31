// CrystalNodeStateHelper.cs
// State-transition API for crystal main nodes.
// Ritual systems (purification, conversion, destruction) call into these
// helpers when their channel completes. Centralising the transitions keeps
// the side-effects (Enabled flag, NodeDormant tag, Health reset, victory
// state snapshot) in one place.
//
// Location: Assets/Scripts/Entities/Buildings/

using Unity.Entities;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// State transitions for crystal main nodes. Use these instead of
    /// poking <see cref="CrystalNodeState"/> directly so the dependent
    /// flags stay in sync.
    /// </summary>
    public static class CrystalNodeStateHelper
    {
        /// <summary>
        /// Transition <paramref name="node"/> to <paramref name="newState"/>.
        /// Resets state timer, toggles curse spread, manages NodeDormant tag.
        /// </summary>
        public static void SetState(
            EntityManager em,
            Entity node,
            NodeState newState,
            byte ownerCulture,
            Faction ownerFaction)
        {
            if (!em.HasComponent<CrystalNodeState>(node)) return;

            var prev = em.GetComponentData<CrystalNodeState>(node);
            if (prev.State == newState && prev.OwnerCulture == ownerCulture) return;

            em.SetComponentData(node, new CrystalNodeState
            {
                State = newState,
                OwnerCulture = ownerCulture,
                OwnerFaction = ownerFaction,
                StateTimer = 0f,
            });

            // Disable curse spread for any non-Active state. Converted nodes
            // produce Runai-allied curse via a separate (future) system; this
            // generic spreader is for hostile Active nodes only.
            if (em.HasComponent<CrystalNode>(node))
            {
                var cn = em.GetComponentData<CrystalNode>(node);
                cn.Enabled = (byte)(newState == NodeState.Active ? 1 : 0);
                em.SetComponentData(node, cn);
            }

            // Destroyed state freezes the entity at 0 HP. DeathSystem skips
            // entities carrying NodeDormant, so the node persists until the
            // regrowth timer revives it.
            if (newState == NodeState.Destroyed)
            {
                if (!em.HasComponent<NodeDormant>(node))
                    em.AddComponent<NodeDormant>(node);

                if (em.HasComponent<Health>(node))
                {
                    var h = em.GetComponentData<Health>(node);
                    h.Value = 0;
                    em.SetComponentData(node, h);
                }
            }
            else
            {
                if (em.HasComponent<NodeDormant>(node))
                    em.RemoveComponent<NodeDormant>(node);

                // Reverting to Active from Destroyed restores full HP. Other
                // transitions (Cleansed/Converted while alive) leave HP alone.
                if (newState == NodeState.Active && prev.State == NodeState.Destroyed
                    && em.HasComponent<Health>(node))
                {
                    var h = em.GetComponentData<Health>(node);
                    h.Value = h.Max;
                    em.SetComponentData(node, h);
                }
            }
        }

        /// <summary>
        /// ECB-deferred variant for systems that run inside Burst-compiled
        /// foreach loops. Does not touch the entity in-place; instead it
        /// queues SetComponent / AddComponent / RemoveComponent commands.
        /// Caller is responsible for setting Health (no read-back available).
        /// </summary>
        public static void SetStateDeferred(
            EntityCommandBuffer ecb,
            Entity node,
            NodeState newState,
            byte ownerCulture,
            Faction ownerFaction)
        {
            ecb.SetComponent(node, new CrystalNodeState
            {
                State = newState,
                OwnerCulture = ownerCulture,
                OwnerFaction = ownerFaction,
                StateTimer = 0f,
            });

            if (newState == NodeState.Destroyed)
            {
                ecb.AddComponent<NodeDormant>(node);
            }
            else
            {
                ecb.RemoveComponent<NodeDormant>(node);
            }
        }
    }
}
