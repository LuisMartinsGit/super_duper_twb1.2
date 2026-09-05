// WallSegmentCleanupSystem.cs
// Monitors wall segments and handles cascade destruction:
// - When all instances in a segment die, destroy the segment and clean up hub links.
// - When a hub dies, destroy connected segments and cascade to their instances.

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

            // === Phase 1: Check segments for dead instances ===
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
                {
                    // All instances dead — mark segment for destruction
                    toDestroy.Add(entity);
                }
            }

            // Destroy dead segments and clean up hub links
            for (int i = 0; i < toDestroy.Length; i++)
            {
                DestroySegment(em, toDestroy[i]);
            }
            toDestroy.Dispose();

            // === Phase 2: Check for destroyed hubs — cascade to connected segments ===
            var segmentsToKill = new NativeList<Entity>(8, Allocator.Temp);

            foreach (var (conn, entity) in SystemAPI
                         .Query<RefRO<WallConnection>>()
                         .WithAll<WallSegmentTag>()
                         .WithEntityAccess())
            {
                bool hubADead = !em.Exists(conn.ValueRO.HubA) ||
                                (em.HasComponent<Health>(conn.ValueRO.HubA) &&
                                 em.GetComponentData<Health>(conn.ValueRO.HubA).Value <= 0);
                bool hubBDead = !em.Exists(conn.ValueRO.HubB) ||
                                (em.HasComponent<Health>(conn.ValueRO.HubB) &&
                                 em.GetComponentData<Health>(conn.ValueRO.HubB).Value <= 0);

                if (hubADead || hubBDead)
                {
                    segmentsToKill.Add(entity);
                }
            }

            for (int i = 0; i < segmentsToKill.Length; i++)
            {
                DestroySegmentWithInstances(em, segmentsToKill[i]);
            }
            segmentsToKill.Dispose();
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
        /// Does NOT destroy child instances (they're already dead).
        /// </summary>
        private static void DestroySegment(EntityManager em, Entity segment)
        {
            if (!em.Exists(segment)) return;

            if (em.HasComponent<WallConnection>(segment))
            {
                var conn = em.GetComponentData<WallConnection>(segment);
                RemoveHubLink(em, conn.HubA, segment);
                RemoveHubLink(em, conn.HubB, segment);
            }

            if (!IsDying(em, segment))
                em.DestroyEntity(segment);
        }

        /// <summary>
        /// Destroy a segment and all its child instances, then clean up hub links.
        /// Used when a hub is destroyed and connected segments must cascade.
        /// </summary>
        private static void DestroySegmentWithInstances(EntityManager em, Entity segment)
        {
            if (!em.Exists(segment)) return;

            // Copy instance refs to local array BEFORE any structural changes.
            // DestroyEntity invalidates live buffer handles.
            if (em.HasBuffer<WallInstanceRef>(segment))
            {
                var buffer = em.GetBuffer<WallInstanceRef>(segment);
                var instanceCopy = new NativeArray<Entity>(buffer.Length, Allocator.Temp);
                for (int i = 0; i < buffer.Length; i++)
                    instanceCopy[i] = buffer[i].Instance;

                // Now destroy instances (structural changes are safe — we hold a copy)
                for (int i = 0; i < instanceCopy.Length; i++)
                {
                    if (em.Exists(instanceCopy[i]) && !IsDying(em, instanceCopy[i]))
                        em.DestroyEntity(instanceCopy[i]);
                }
                instanceCopy.Dispose();
            }

            DestroySegment(em, segment);
        }

        /// <summary>
        /// Remove the WallHubLink entry that references the given segment from a hub.
        /// </summary>
        private static void RemoveHubLink(EntityManager em, Entity hub, Entity segment)
        {
            if (!em.Exists(hub)) return;
            if (!em.HasBuffer<WallHubLink>(hub)) return;

            var links = em.GetBuffer<WallHubLink>(hub);
            for (int i = links.Length - 1; i >= 0; i--)
            {
                if (links[i].Segment == segment)
                {
                    links.RemoveAt(i);
                    break;
                }
            }
        }
    }
}
