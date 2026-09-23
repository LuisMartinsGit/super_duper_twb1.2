// WallSegmentCleanupSystem.cs
// Retires wall segments whose last cell has died: destroys the segment
// entity and removes its WallHubLink entries from both hubs.
//
// Walls fall in SECTIONS (docs/Design/Age_1_Alanthor.md § Walls fall in
// sections, 2026-09-19). A hub's death removes the hub and nothing else —
// the segments attached to it keep standing on their own cells. The old
// Phase 2 here cascade-destroyed every segment touching a dead hub, and
// with a drawn wall being one segment end to end (WallDrawTool.asset
// hubSpacing 0) that meant one 400 HP bastion kill erased a whole stroke.
// Do not reintroduce it.

using Unity.Entities;
using Unity.Collections;

namespace TheWaningBorder.Systems.Buildings
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TheWaningBorder.Systems.Combat.DeathSystem))]
    public partial struct WallSegmentCleanupSystem : ISystem
    {
        private const float PollInterval = 0.5f;
        // SimCadence-phased, NOT a raw float accumulator (2026-09-04, MP
        // harness catch #7 class): a raw `_timer -= dt` carries a
        // machine-dependent phase in from the pre-match frame-driven updates,
        // so periodic work lands on different ticks per lockstep peer.
        private SimCadence.Periodic _acc;

        public void OnCreate(ref SystemState state)
        {

        }

        public void OnUpdate(ref SystemState state)
        {
            if (!_acc.Due(SystemAPI.Time.DeltaTime, PollInterval)) return;

            var em = state.EntityManager;
            var toDestroy = new NativeList<Entity>(16, Allocator.Temp);

            // A segment is a graph edge with no HP of its own; it lives
            // exactly as long as one of its cells does.
            foreach (var (conn, entity) in SystemAPI
                         .Query<RefRO<WallConnection>>()
                         .WithAll<WallSegmentTag>()
                         .WithEntityAccess())
            {
                if (!em.HasBuffer<WallInstanceRef>(entity)) continue;

                var instances = em.GetBuffer<WallInstanceRef>(entity);
                bool anyAlive = false;

                for (int i = 0; i < instances.Length; i++)
                {
                    if (em.Exists(instances[i].Instance) &&
                        em.HasComponent<Health>(instances[i].Instance))
                    {
                        var hp = em.GetComponentData<Health>(instances[i].Instance);
                        if (hp.Value > 0)
                        {
                            anyAlive = true;
                            break;
                        }
                    }
                }

                if (!anyAlive && instances.Length > 0)
                    toDestroy.Add(entity);
            }

            for (int i = 0; i < toDestroy.Length; i++)
                DestroySegment(em, toDestroy[i]);
            toDestroy.Dispose();
        }

        /// <summary>
        /// True when DeathSystem already owns this entity (dead or collapsing).
        /// Destroying it synchronously here races DeathSystem's EndSimulation
        /// buffer and throws "entity does not exist" at playback.
        /// </summary>
        private static bool IsDying(EntityManager em, Entity e)
        {
            if (em.HasComponent<BuildingCollapseState>(e)) return true;
            if (TransientState.Active<DeathAnimationState>(em, e)) return true;
            return em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0;
        }

        /// <summary>
        /// Destroy a segment entity and clean up WallHubLink entries on both hubs.
        /// Does NOT destroy child instances (they're already dead). Either hub
        /// may itself be dead by now — RemoveHubLink tolerates that.
        /// </summary>
        private static void DestroySegment(EntityManager em, Entity segment)
        {
            if (!em.Exists(segment)) return;

            if (em.HasComponent<WallConnection>(segment))
            {
                var conn = em.GetComponentData<WallConnection>(segment);
                TheWaningBorder.Entities.AlanthorWall.RemoveHubLink(em, conn.HubA, segment);
                TheWaningBorder.Entities.AlanthorWall.RemoveHubLink(em, conn.HubB, segment);
            }

            if (!IsDying(em, segment))
                em.DestroyEntity(segment);
        }
    }
}
