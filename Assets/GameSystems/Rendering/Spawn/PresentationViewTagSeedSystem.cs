// PresentationViewTagSeedSystem.cs
// Pre-adds PresentationViewSpawned (DISABLED) to every PresentationId entity
// that does not carry it yet -- inside the sim tick, so the one structural
// change a viewed entity ever makes for its view happens on the SAME tick on
// every lockstep peer.
//
// WHY (2026-09-13): PresentationSpawnSystem is a MonoBehaviour under a
// per-frame spawn budget. It used to AddComponent the tag itself when a view
// landed, which put the structural change on frame time: the Veilmarch
// 15-31-23 match forked because a Hall foundation placed on tick 48565 was
// tagged on 48566 by two peers and on 48569/48571 by the other two. Units
// already carried the tag pre-added by TransientState.PreAddUnitSet; this
// covers buildings, nodes, walls, pickups and anything else that gets a view.
// The spawn system now only flips the enable bit, which is not structural.

using Unity.Collections;
using Unity.Entities;

namespace TheWaningBorder.Rendering
{
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    public partial struct PresentationViewTagSeedSystem : ISystem
    {
        private EntityQuery _unseeded;

        public void OnCreate(ref SystemState state)
        {
            // IgnoreComponentEnabledState: a plain None<T> on an enableable
            // component also matches entities that carry it DISABLED, which is
            // every unit -- this must match only entities that lack it entirely.
            _unseeded = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<PresentationId>()
                .WithNone<PresentationViewSpawned>()
                .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState)
                .Build(ref state);
            state.RequireForUpdate(_unseeded);
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_unseeded.IsEmptyIgnoreFilter) return;
            var em = state.EntityManager;
            var entities = _unseeded.ToEntityArray(Allocator.Temp);
            // One batch add (enabled by default), then clear the bit on each:
            // "no view yet" is the disabled state the spawn system looks for.
            em.AddComponent<PresentationViewSpawned>(_unseeded);
            for (int i = 0; i < entities.Length; i++)
                em.SetComponentEnabled<PresentationViewSpawned>(entities[i], false);
            entities.Dispose();
        }
    }
}
