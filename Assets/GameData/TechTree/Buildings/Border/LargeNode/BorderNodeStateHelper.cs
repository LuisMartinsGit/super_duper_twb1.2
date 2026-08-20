// BorderNodeStateHelper.cs
// State-transition API for veilstone main nodes.
// Ritual systems (purification, conversion, destruction) call into these
// helpers when their channel completes. Centralising the transitions keeps
// the side-effects (Enabled flag, NodeDormant tag, Health reset, victory
// state snapshot) in one place.
//
// Location: Assets/GameData/TechTree/Buildings/Border/LargeNode/BorderNodeStateHelper.cs

using Unity.Entities;

namespace TheWaningBorder.Entities
{
    /// <summary>
    /// State transitions for veilstone main nodes. Use these instead of
    /// poking <see cref="BorderNodeState"/> directly so the dependent
    /// flags stay in sync.
    /// </summary>
    public static class BorderNodeStateHelper
    {
        /// <summary>
        /// Transition <paramref name="node"/> to <paramref name="newState"/>.
        /// Resets state timer, toggles border spread, manages NodeDormant tag.
        /// </summary>
        public static void SetState(
            EntityManager em,
            Entity node,
            NodeState newState,
            byte ownerCulture,
            Faction ownerFaction)
        {
            if (!em.HasComponent<BorderNodeState>(node)) return;

            var prev = em.GetComponentData<BorderNodeState>(node);
            if (prev.State == newState && prev.OwnerCulture == ownerCulture) return;

            em.SetComponentData(node, new BorderNodeState
            {
                State = newState,
                OwnerCulture = ownerCulture,
                OwnerFaction = ownerFaction,
                StateTimer = 0f,
            });

            // Post-game chart milestone: a node claimed from the Border
            // (Alanthor cleanse or Runai conversion) counts for the acting
            // faction. Reversions to Active and Feraldis kills don't.
            if (prev.State != newState
                && (newState == NodeState.Cleansed || newState == NodeState.Converted))
            {
                TheWaningBorder.UI.HUD.GameStatsTracker.RecordEvent(
                    ownerFaction, TheWaningBorder.UI.HUD.GameEventKind.NodeConverted);
            }

            // ── TEMPO RULE (Curse & Shardroot canon §2.2) ────────────────
            // Applying your verb to ANY well refreshes ALL of your existing
            // holds to a full hold timer. Activity is map control; ten
            // minutes of inactivity and the curse returns everywhere.
            // Only player verbs refresh (reversions pass Faction.Border).
            if (newState != NodeState.Active && ownerFaction != Faction.Border)
            {
                var holdQuery = em.CreateEntityQuery(
                    ComponentType.ReadWrite<BorderNodeState>(),
                    ComponentType.ReadOnly<BorderMainNodeTag>());
                using var holdNodes = holdQuery.ToEntityArray(
                    Unity.Collections.Allocator.Temp);
                for (int i = 0; i < holdNodes.Length; i++)
                {
                    if (holdNodes[i] == node) continue;
                    var hs = em.GetComponentData<BorderNodeState>(holdNodes[i]);
                    if (hs.State == NodeState.Active) continue;
                    if (hs.OwnerFaction != ownerFaction) continue;
                    // Destroyed nodes already in the REBUILD phase are past
                    // their hold — refreshing those would yo-yo the build.
                    if (hs.State == NodeState.Destroyed
                        && em.HasComponent<NodeRebuilding>(holdNodes[i])) continue;
                    hs.StateTimer = 0f;
                    em.SetComponentData(holdNodes[i], hs);
                }
            }

            // Disable border spread for any non-Active state. Converted nodes
            // produce Runai-allied border via a separate (future) system; this
            // generic spreader is for hostile Active nodes only.
            if (em.HasComponent<BorderNode>(node))
            {
                var cn = em.GetComponentData<BorderNode>(node);
                cn.Enabled = (byte)(newState == NodeState.Active ? 1 : 0);
                em.SetComponentData(node, cn);
            }

            // The node's FACTION follows its claim: a Pacified/Purified well
            // belongs to its owner (so the owner's attack-move units don't
            // auto-hack their own tether/font, while ENEMIES can — the break
            // matrix). Active and Destroyed wells revert to the Border.
            if (em.HasComponent<FactionTag>(node))
            {
                Faction visualFaction =
                    (newState == NodeState.Cleansed || newState == NodeState.Converted)
                        ? ownerFaction
                        : Faction.Border;
                em.SetComponentData(node, new FactionTag { Value = visualFaction });
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
            ecb.SetComponent(node, new BorderNodeState
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
