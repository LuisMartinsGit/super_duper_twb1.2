// NodeTargetabilitySystem.cs
// Destruction rework (2026-07): veilstone MAIN NODES are now destroyable by
// normal combat. A node is targetable whenever it is ACTIVE; in any other
// state (Destroyed rubble / rebuilding / Cleansed / Converted) it carries
// NodeUntargetable so units don't hack at an inert husk.
//
// This replaces the old Iconoclast-gated model (nodes untargetable unless a
// Feraldis Iconoclast stood in aura range). Anyone can now bring an Active
// node to 0 HP — NodeStateDeathInterceptSystem turns that into the Destroyed
// state, which leaves rubble and auto-rebuilds (NodeRubbleTime dormant +
// NodeRebuildTime build). Only an Alanthor Purification removes a node for
// good (Cleansed is permanent).
//
// TargetingSystem excludes NodeUntargetable from its enemy query, so toggling
// that tag is all this system does.
//
// Location: Assets/Scripts/Systems/Border/

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Systems.Combat;

namespace TheWaningBorder.Systems.Border
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(TargetingSystem))]
    public partial class NodeTargetabilitySystem : SystemBase
    {
        private EntityQuery _nodeQuery;
        private EntityQuery _cultureQuery;

        protected override void OnCreate()
        {
            _nodeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<BorderNodeState>());
            _cultureQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            RequireForUpdate(_nodeQuery);
        }

        protected override void OnUpdate()
        {
            var em = EntityManager;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            // Wells are FERALDIS-ONLY attack targets (2026-08-04): Age 0 and
            // Alanthor/Runai factions never attack them — their verbs are
            // Purify / Pacify. With no Feraldis-cultured faction in the
            // match, every well is flatly untargetable (towers, auto-fire,
            // splash targeting — all of it). Mixed matches keep wells
            // targetable node-side; CommandRouter + TargetingSystem gate the
            // per-attacker side there.
            bool feraldisInMatch = false;
            using (var progs = _cultureQuery.ToComponentDataArray<FactionProgress>(Allocator.Temp))
            {
                for (int i = 0; i < progs.Length; i++)
                    if (progs[i].Culture == Cultures.Feraldis) { feraldisInMatch = true; break; }
            }

            using var nodeEnts = _nodeQuery.ToEntityArray(Allocator.Temp);
            using var nodeStates = _nodeQuery.ToComponentDataArray<BorderNodeState>(Allocator.Temp);

            for (int i = 0; i < nodeEnts.Length; i++)
            {
                Entity node = nodeEnts[i];

                // Break matrix (Curse & Shardroot canon §2.2, amended
                // 2026-08-04): only FERALDIS breaks wells by force. When a
                // Feraldis faction is present, a well is breakable while
                // Active / Cleansed / Converted (breaking a rival hold);
                // Destroyed rubble and still-emerging nodes stay immune.
                var s = nodeStates[i].State;
                bool targetable = feraldisInMatch
                              && (s == NodeState.Active
                                   || s == NodeState.Cleansed
                                   || s == NodeState.Converted)
                              && !em.HasComponent<NodeDormant>(node)
                              && !em.HasComponent<UnderConstruction>(node);
                bool active = targetable;

                // A CORRUPTED well is always damageable for its window, even
                // if the matrix above would seal it (Feraldis Corruptor —
                // docs/Design/Age_1_Feraldis.md).
                bool corrupted = em.HasComponent<WellCorrupted>(node);
                if (corrupted) active = true;

                bool hasTag = em.HasComponent<NodeUntargetable>(node);
                if (active && hasTag)
                    ecb.RemoveComponent<NodeUntargetable>(node);
                else if (!active && !hasTag)
                    ecb.AddComponent<NodeUntargetable>(node);

                // Auto-acquire block. Wells are normally invisible to target
                // scanning entirely (TargetingSystem used to hard-exclude
                // BorderMainNodeTag), so even a "vulnerable" well would have
                // been ignored by every unit that wasn't hand-ordered onto
                // it. Carrying the block as a COMPONENT lets exactly one case
                // through: a corrupted well, which an army can now swarm by
                // simply attack-moving onto it.
                bool blocked = em.HasComponent<NodeNoAutoAcquire>(node);
                if (corrupted && blocked)
                    ecb.RemoveComponent<NodeNoAutoAcquire>(node);
                else if (!corrupted && !blocked)
                    ecb.AddComponent<NodeNoAutoAcquire>(node);
            }

            ecb.Playback(em);
            ecb.Dispose();
        }
    }
}
