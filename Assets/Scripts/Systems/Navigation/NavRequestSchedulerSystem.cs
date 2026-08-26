// NavRequestSchedulerSystem.cs
// task-112 M6 -- per-tick navigation request scheduler (S9).
//
// Owns the NavRequestQueueSingleton; drains the pending queue each
// tick:
//   1. Sort pending requests by (Priority asc, EnqueueTick asc,
//      Requester.Index asc) -- DR-12 locked order.
//   2. Coalesce duplicate (GoalCell, ProfileHash) keys. The first
//      occurrence (lowest in the sort order) becomes the "primary"
//      request that actually gets a NavPathRequest emitted; subsequent
//      requests with the same key inherit the path result via
//      AbstractPathfinderSystem's coalesce-broadcast pass.
//      For M6 the coalesce broadcast is implemented in the SCHEDULER
//      itself: it emits one NavPathRequest for the primary AND a
//      NavPathRequest for every duplicate too (same start/goal/profile)
//      -- the pathfinder solves identical inputs identically, so the
//      result is byte-identical even without explicit broadcast. The
//      number of unique (goal, profile) keys that get A* runs is still
//      reduced via the coalesce -- the scheduler "charges" only ONE
//      unit of the per-tick budget for an entire equivalence class.
//   3. Release up to MaxRequestsPerTick *equivalence classes* per tick
//      (so 16 unique destinations per tick, no matter how many units
//      share each destination). Stale-generation requests dropped at
//      drain time (CCD-5 mitigation).
//
// Producers enqueue via the static EnqueueRequest helper instead of
// directly attaching NavPathRequest -- MoveCommandHelper is migrated
// in M6.
//
// Determinism notes:
//   * Sort order is integer-key insertion sort -- byte-stable.
//   * Coalesce map iteration is bounded by the dedup count; we walk
//     the SORTED list, not the hash map, so iteration order is
//     deterministic.
//   * Per-tick budget release order matches sort order -- no random
//     selection.
//   * Stale request filter compares against CurrentGeneration; if no
//     NavGenerationCounter singleton exists yet, all requests are
//     considered current (M3 fall-through).

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Drains the <see cref="NavRequestQueueSingleton"/>'s pending
    /// queue each tick, sorts + coalesces requests, and emits up to
    /// <see cref="NavRequestQueueSingleton.MaxRequestsPerTick"/>
    /// equivalence classes of <see cref="NavPathRequest"/> components
    /// via ECB. Runs BEFORE <see cref="AbstractPathfinderSystem"/>.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AbstractPathfinderSystem))]
    public partial struct NavRequestSchedulerSystem : ISystem
    {
        private Entity _queueEntity;
        private byte _initialised;
        /// <summary>System-side mirror of the singleton's pending list, so a
        /// rebuild after the end-of-match entity wipe can dispose the orphaned
        /// allocation instead of leaking it.</summary>
        private NativeList<PendingNavRequest> _pending;

        // NOT [BurstCompile]: BC1028 -- CreateEntity is managed.
        public void OnCreate(ref SystemState state)
        {
            _initialised = 0;
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            // Gate on the singleton ACTUALLY EXISTING, not on a one-shot latch.
            // This entity is an ordinary gameplay entity, so GameBootstrap's
            // end-of-match wipe destroys it — while this system survives, so
            // OnCreate never runs again. With a `_initialised == 0` latch the
            // singleton was therefore never rebuilt, and every match after the
            // first threw "GetSingleton<NavRequestQueueSingleton>() ... but
            // there are none" on the line below: the whole nav stack dead,
            // workers frozen, nothing pathing.
            if (_initialised == 0
                || !em.Exists(_queueEntity)
                || !em.HasComponent<NavRequestQueueSingleton>(_queueEntity))
            {
                // The wipe took the component but NOT the native list it
                // referenced. Dispose our own mirror of it first, or we leak
                // one persistent list per match.
                if (_pending.IsCreated) _pending.Dispose();
                _pending = new NativeList<PendingNavRequest>(64, Allocator.Persistent);

                _initialised = 1;
                _queueEntity = em.CreateEntity(typeof(NavRequestQueueSingleton));
                em.SetComponentData(_queueEntity, new NavRequestQueueSingleton
                {
                    Pending = _pending,
                    MaxRequestsPerTick = NavRequestQueueSingleton.DefaultMaxRequestsPerTick,
                    ReleasedThisTick = 0,
                    CurrentTick = 0,
                });
            }

            var queue = SystemAPI.GetSingleton<NavRequestQueueSingleton>();
            if (!queue.Pending.IsCreated) return;

            // Bump current tick + reset release counter.
            queue.CurrentTick++;
            queue.ReleasedThisTick = 0;

            // Pull the current graph generation -- requests with a stale
            // generation are dropped on entry (CCD-5).
            int currentGeneration = 0;
            if (SystemAPI.HasSingleton<NavGenerationCounter>())
            {
                currentGeneration = SystemAPI.GetSingleton<NavGenerationCounter>().CurrentGeneration;
            }
            else if (SystemAPI.HasSingleton<PortalGraphSingleton>())
            {
                currentGeneration = SystemAPI.GetSingleton<PortalGraphSingleton>().Generation;
            }

            int n = queue.Pending.Length;
            if (n == 0)
            {
                SystemAPI.SetSingleton(queue);
                return;
            }

            // ── Drop stale-generation requests + non-existent requesters
            //    in place by compacting the list. ────────────────────────
            int writeIdx = 0;
            for (int i = 0; i < n; i++)
            {
                var p = queue.Pending[i];
                // Generation 0 = graph not yet built; let it through so
                // the request can be replayed when the graph appears.
                bool stale = currentGeneration != 0 && p.Generation != 0
                    && p.Generation != currentGeneration;
                bool requesterDead = !em.Exists(p.Requester);
                if (stale || requesterDead) continue;
                queue.Pending[writeIdx++] = p;
            }
            if (writeIdx < n) queue.Pending.Length = writeIdx;
            n = writeIdx;

            if (n == 0)
            {
                SystemAPI.SetSingleton(queue);
                return;
            }

            // ── Sort pending by (Priority asc, EnqueueTick asc,
            //    Requester.Index asc) per DR-12. Insertion sort -- the
            //    queue is typically small (a few dozen entries on a
            //    mass-move tick). ────────────────────────────────────────
            for (int i = 1; i < n; i++)
            {
                var key = queue.Pending[i];
                int j = i - 1;
                while (j >= 0 && ComparePending(queue.Pending[j], key) > 0)
                {
                    queue.Pending[j + 1] = queue.Pending[j];
                    j--;
                }
                queue.Pending[j + 1] = key;
            }

            // ── Coalesce + release in sorted order. ─────────────────────
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // Track which equivalence classes (keys) have been "released"
            // this tick. We charge the budget per unique key (so 100
            // units to the same destination = 1 budget unit). The
            // primary request emits the NavPathRequest; duplicates
            // ALSO emit the same request (so each requester ends up
            // with a NavPathRequest component this tick) -- the
            // pathfinder solves identical inputs identically, so the
            // results are byte-identical. The coalesce just gates the
            // budget so a mass-move doesn't burn the entire pathfinder
            // budget on one destination.
            var seenKeys = new NativeHashMap<NavRequestCoalesceKey, byte>(n, Allocator.Temp);

            int released = 0;
            int remaining = 0;
            for (int i = 0; i < n; i++)
            {
                var p = queue.Pending[i];
                var key = new NavRequestCoalesceKey
                {
                    GoalCell = p.GoalCell,
                    ProfileHash = p.ProfileHash,
                };

                bool newKey = !seenKeys.ContainsKey(key);
                if (newKey)
                {
                    if (released >= queue.MaxRequestsPerTick)
                    {
                        // Carry the rest to next tick.
                        queue.Pending[remaining++] = p;
                        continue;
                    }
                    seenKeys.Add(key, 1);
                    released++;
                }
                // Either a brand-new key (charged above) or a duplicate
                // of an already-released key (free piggy-back) --
                // emit the NavPathRequest either way.
                EmitRequest(ecb, p, currentGeneration);
            }
            seenKeys.Dispose();

            queue.Pending.Length = remaining;
            queue.ReleasedThisTick = released;
            SystemAPI.SetSingleton(queue);
        }

        public void OnDestroy(ref SystemState state)
        {
            // Dispose the system-side mirror, not the component's copy: after
            // an end-of-match wipe the entity is already gone and reading the
            // component would miss the allocation entirely.
            if (_pending.IsCreated) _pending.Dispose();
        }

        /// <summary>
        /// Strict total-order comparator for the queue sort. Returns
        /// negative when a should come before b, positive when after,
        /// 0 only for byte-identical entries. Order:
        ///   1) Priority ascending,
        ///   2) EnqueueTick ascending,
        ///   3) Requester.Index ascending,
        ///   4) Requester.Version ascending (final fallback so two
        ///      different entities with the same Index never collapse).
        /// </summary>
        public static int ComparePending(in PendingNavRequest a, in PendingNavRequest b)
        {
            if (a.Priority != b.Priority) return a.Priority - b.Priority;
            if (a.EnqueueTick != b.EnqueueTick)
                return a.EnqueueTick < b.EnqueueTick ? -1 : 1;
            if (a.Requester.Index != b.Requester.Index)
                return a.Requester.Index - b.Requester.Index;
            return a.Requester.Version - b.Requester.Version;
        }

        // Emit one NavPathRequest for the given pending entry. Uses ECB
        // so the structural change defers to end-of-sim-group. If the
        // requester already holds a NavPathRequest (e.g. multiple
        // enqueues this tick by different code paths), the latest one
        // wins -- we still use SetComponent so the queue resolves to
        // the most-recent enqueue.
        private static void EmitRequest(EntityCommandBuffer ecb, PendingNavRequest p,
            int generation)
        {
            var req = new NavPathRequest
            {
                StartCell = p.StartCell,
                GoalCell = p.GoalCell,
                ProfileHash = p.ProfileHash,
                Generation = generation,
                Status = NavPathRequest.StatusPending,
            };
            ecb.AddComponent(p.Requester, req);
        }

        /// <summary>
        /// Managed-side enqueue helper used by command callers
        /// (MoveCommandHelper, AttackMoveCommandHelper, etc.). Looks up
        /// the queue singleton in the default world and appends to its
        /// Pending list.
        ///
        /// Silently no-ops when the queue singleton hasn't been
        /// bootstrapped yet (the scheduler creates it lazily on its
        /// first OnUpdate, so a command issued during world init may
        /// race past). When the queue isn't available, falls back to
        /// directly attaching a <see cref="NavPathRequest"/> so the M3
        /// fast-path continues to work in those rare windows.
        /// </summary>
        public static void EnqueueRequest(EntityManager em, Entity requester,
            int2 startCell, int2 goalCell, byte profileHash, byte priority,
            int generation)
        {
            if (!em.Exists(requester)) return;

            var queueQuery = em.CreateEntityQuery(typeof(NavRequestQueueSingleton));
            if (queueQuery.IsEmptyIgnoreFilter)
            {
                queueQuery.Dispose();
                // Fall-back: attach the request directly so the unit
                // still gets a path on the next AbstractPathfinder tick.
                // The scheduler will pick up the slack as soon as it
                // initialises.
                var fallback = new NavPathRequest
                {
                    StartCell = startCell,
                    GoalCell = goalCell,
                    ProfileHash = profileHash,
                    Generation = generation,
                    Status = NavPathRequest.StatusPending,
                };
                if (em.HasComponent<NavPathRequest>(requester))
                    em.SetComponentData(requester, fallback);
                else
                    em.AddComponentData(requester, fallback);
                return;
            }

            var queue = queueQuery.GetSingleton<NavRequestQueueSingleton>();
            queueQuery.Dispose();
            if (!queue.Pending.IsCreated) return;

            queue.Pending.Add(new PendingNavRequest
            {
                Requester = requester,
                StartCell = startCell,
                GoalCell = goalCell,
                ProfileHash = profileHash,
                Priority = priority,
                EnqueueTick = queue.CurrentTick,
                Generation = generation,
            });

            // Write the queue back so the new entry sticks even though
            // NativeList is a reference type internally -- SetSingleton
            // is required for the singleton bookkeeping (no auto-write).
            var entity = em.CreateEntityQuery(typeof(NavRequestQueueSingleton)).GetSingletonEntity();
            em.SetComponentData(entity, queue);
        }
    }
}
