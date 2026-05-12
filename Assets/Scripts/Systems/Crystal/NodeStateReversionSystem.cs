// NodeStateReversionSystem.cs
// Ticks the per-node state timer. When the timer crosses the state-specific
// duration, the node reverts to Active. Implements the spec rule:
// "Every non-Active state is temporary. The map wants to be Active."
//
// Spec §9 (Node State Machine), §11 (Tuning Parameters).
//
// Location: Assets/Scripts/Systems/Crystal/

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Entities;
using static TheWaningBorder.Core.Config.CrystalConstants;

namespace TheWaningBorder.Systems.Crystal
{
    /// <summary>
    /// Advances <see cref="CrystalNodeState.StateTimer"/> for every main node
    /// not in the Active state and reverts to Active when the per-state
    /// duration is reached.
    ///
    /// SystemBase (not ISystem) so we can call <see cref="CrystalNodeStateHelper.SetState"/>,
    /// which performs structural changes (NodeDormant add/remove). Same
    /// pattern as <see cref="CrystalExtinctionSystem"/>.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class NodeStateReversionSystem : SystemBase
    {
        private EntityQuery _nodeQuery;

        protected override void OnCreate()
        {
            _nodeQuery = EntityManager.CreateEntityQuery(
                ComponentType.ReadWrite<CrystalNodeState>(),
                ComponentType.ReadOnly<CrystalMainNodeTag>()
            );
            RequireForUpdate(_nodeQuery);
        }

        protected override void OnUpdate()
        {
            float dt = (float)SystemAPI.Time.DeltaTime;

            // First pass: advance timers (no structural change). Collect any
            // node that has crossed its reversion threshold so we can flip
            // it after the iteration.
            var toRevert = new NativeList<Entity>(8, Allocator.Temp);

            foreach (var (stateRW, entity) in SystemAPI
                .Query<RefRW<CrystalNodeState>>()
                .WithAll<CrystalMainNodeTag>()
                .WithEntityAccess())
            {
                ref var s = ref stateRW.ValueRW;
                if (s.State == NodeState.Active) continue;

                s.StateTimer += dt;

                float threshold = s.State switch
                {
                    NodeState.Cleansed  => NodeCleansedRevertTime,
                    NodeState.Converted => NodeConvertedRevertTime,
                    NodeState.Destroyed => NodeDestroyedRegrowTime,
                    _ => float.MaxValue,
                };

                if (s.StateTimer >= threshold)
                    toRevert.Add(entity);
            }

            // Second pass: apply reversions. SetState performs structural
            // changes (NodeDormant remove, Health restore), so it has to run
            // outside the query iteration.
            for (int i = 0; i < toRevert.Length; i++)
            {
                CrystalNodeStateHelper.SetState(
                    EntityManager,
                    toRevert[i],
                    NodeState.Active,
                    Cultures.None,
                    Faction.Curse);
            }

            toRevert.Dispose();
        }
    }
}
