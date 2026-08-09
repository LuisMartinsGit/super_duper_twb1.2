// NodeStateReversionSystem.cs
// Ticks the per-node state timer. When the timer crosses the state-specific
// duration, the node reverts to Active. Implements the spec rule:
// "Every non-Active state is temporary. The map wants to be Active."
//
// Spec §9 (Node State Machine), §11 (Tuning Parameters).
//
// Location: Assets/Scripts/Systems/Border/

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Entities;
using static TheWaningBorder.Core.Config.BorderConstants;

namespace TheWaningBorder.Systems.Border
{
    /// <summary>
    /// Advances <see cref="BorderNodeState.StateTimer"/> for every main node
    /// not in the Active state and reverts to Active when the per-state
    /// duration is reached.
    ///
    /// SystemBase (not ISystem) so we can call <see cref="BorderNodeStateHelper.SetState"/>,
    /// which performs structural changes (NodeDormant add/remove). Same
    /// pattern as <see cref="BorderExtinctionSystem"/>.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class NodeStateReversionSystem : SystemBase
    {
        private EntityQuery _nodeQuery;

        protected override void OnCreate()
        {
            _nodeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<BorderNodeState>(),
                ComponentType.ReadOnly<BorderMainNodeTag>()
            );
            RequireForUpdate(_nodeQuery);
        }

        protected override void OnUpdate()
        {
            float dt = (float)SystemAPI.Time.DeltaTime;

            // First pass: advance timers (no structural change). Collect nodes
            // crossing a phase boundary; the structural work (SetState, tag
            // add/remove) runs after the iteration.
            var toRevert       = new NativeList<Entity>(8, Allocator.Temp); // → Active (Converted revert / rebuild done)
            var toStartRebuild = new NativeList<Entity>(8, Allocator.Temp); // rubble → rebuild (add NodeRebuilding)

            var em = EntityManager;

            foreach (var (stateRW, entity) in SystemAPI
                .Query<RefRW<BorderNodeState>>()
                .WithAll<BorderMainNodeTag>()
                .WithEntityAccess())
            {
                ref var s = ref stateRW.ValueRW;
                if (s.State == NodeState.Active) continue;

                // Curse & Shardroot canon: EVERY hold expires. Cleansed
                // (Purified) is no longer permanent — it reverts to Active
                // after the well hold time like Converted (Pacified) does.
                // The tempo rule (BorderNodeStateHelper.SetState) resets
                // StateTimer on all of a player's holds whenever they claim
                // another well, so an ACTIVE player keeps their set alive.
                if (s.State == NodeState.Cleansed)
                {
                    s.StateTimer += dt;
                    if (s.StateTimer >= NodeCleansedRevertTime)
                        toRevert.Add(entity);
                    continue;
                }

                // Converted (Pacified) reverts to Active over time.
                if (s.State == NodeState.Converted)
                {
                    s.StateTimer += dt;
                    if (s.StateTimer >= NodeConvertedRevertTime)
                        toRevert.Add(entity);
                    continue;
                }

                // ── Destroyed: two-phase rubble → rebuild → Active ──────────
                if (s.State == NodeState.Destroyed)
                {
                    // Secondary border locations (from over-grown resource
                    // patches) are one-shot — they never rebuild.
                    if (em.HasComponent<SecondaryBorderLocationTag>(entity)) continue;

                    s.StateTimer += dt;

                    if (em.HasComponent<NodeRebuilding>(entity))
                    {
                        // Phase B — reconstructing. Finish after NodeRebuildTime.
                        if (s.StateTimer >= NodeRebuildTime)
                            toRevert.Add(entity);
                    }
                    else
                    {
                        // Phase A — rubble/dormant. Begin rebuild after NodeRubbleTime.
                        if (s.StateTimer >= NodeRubbleTime)
                        {
                            s.StateTimer = 0f;          // restart the clock for the build phase
                            toStartRebuild.Add(entity);
                        }
                    }
                }
            }

            // Phase A → B: mark the node rebuilding (structural add).
            for (int i = 0; i < toStartRebuild.Length; i++)
            {
                var node = toStartRebuild[i];
                if (!em.HasComponent<NodeRebuilding>(node))
                    em.AddComponent<NodeRebuilding>(node);
            }

            // Revert to Active — SetState restores HP + removes NodeDormant +
            // re-enables spread; also drop the rebuild marker.
            for (int i = 0; i < toRevert.Length; i++)
            {
                var node = toRevert[i];
                if (em.HasComponent<NodeRebuilding>(node))
                    em.RemoveComponent<NodeRebuilding>(node);

                BorderNodeStateHelper.SetState(
                    EntityManager,
                    node,
                    NodeState.Active,
                    Cultures.None,
                    Faction.Border);

                // A purification silences the node's self-defense turret;
                // now that Cleansed EXPIRES (canon: every hold does), the
                // reawakened well gets its turret back.
                if (em.HasComponent<BuildingRangedAttack>(node))
                {
                    var ra = em.GetComponentData<BuildingRangedAttack>(node);
                    if (ra.Damage <= 0)
                    {
                        ra.Damage = MainNodeAttackDamage;
                        em.SetComponentData(node, ra);
                    }
                }

                // A cleansed husk can sit at low HP; a reawakened well
                // returns whole.
                if (em.HasComponent<Health>(node))
                {
                    var h = em.GetComponentData<Health>(node);
                    if (h.Value < h.Max) { h.Value = h.Max; em.SetComponentData(node, h); }
                }
            }

            toRevert.Dispose();
            toStartRebuild.Dispose();
        }
    }
}
