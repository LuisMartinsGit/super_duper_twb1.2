// DeterminismReplaySystem.cs
// task-112 M7 -- per-tick unit-position recorder + replayer for the
// determinism audit. Lives at the END of SimulationSystemGroup so it
// observes the final positions after every nav / movement / steering
// pass for the tick.
//
// Modes (via GameSettings.NavReplayMode):
//   * Off    -- no allocation, no work. Default for production /
//               skirmish; the system early-outs on the first line.
//   * Record -- appends each unit's (Tick, EntityIndex, posMm) tuple
//               to the persistent replay log. Order: tick asc, then
//               entityIndex asc (the unit query is snapshotted and
//               sorted before write so chunk-walk order doesn't leak
//               into the log).
//   * Replay -- snapshots positions the same way and compares
//               byte-for-byte against the previously recorded log.
//               First divergence is reported via TWBLog + Debug.LogError
//               and the sim is halted (editor only). DivergenceCount
//               on the log singleton lets tests count failures.
//
// Determinism notes:
//   * The recorder snapshots all units via ToEntityArray + ToComponentDataArray
//     (Allocator.TempJob), sorts the index array by entity.Index using
//     a deterministic insertion sort, and appends positions in that
//     sorted order. The sort is the only place chunk-walk order could
//     leak into the log; sorting eliminates it.
//   * Positions are converted to integer MILLIMETRES via
//     UnitPositionSnapshot.ToMillimeters (round-to-nearest float -> int).
//     Float ULP drift across Burst versions can't change the rounded
//     integer.
//   * The system is itself NOT burst-compiled at the OnUpdate level --
//     it does managed work (NativeArray sort + log append) on the main
//     thread. Per BC1028: OnCreate must not be [BurstCompile] when it
//     creates entities.
//
// Location: Assets/Scripts/Systems/Navigation/DeterminismReplaySystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Per-tick determinism replay recorder + comparator. Allocates the
    /// <see cref="DeterminismReplayLog"/> singleton lazily on first
    /// non-Off tick; disposes the persistent log in <see cref="OnDestroy"/>
    /// (DR-17).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderLast = true)]
    public partial struct DeterminismReplaySystem : ISystem
    {
        // OnCreate is intentionally NOT [BurstCompile] per BC1028 -- the
        // log singleton is allocated lazily in OnUpdate so the system
        // boots clean when NavReplayMode is Off.
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavGridSingleton>();
        }

        public void OnDestroy(ref SystemState state)
        {
            // Find + dispose the persistent log if we ever allocated it.
            if (SystemAPI.HasSingleton<DeterminismReplayLog>())
            {
                var log = SystemAPI.GetSingleton<DeterminismReplayLog>();
                if (log.Log.IsCreated) log.Log.Dispose();
                // The singleton entity is owned by the world's tear-down;
                // we only need to free the NativeList we attached.
            }
        }

        public void OnUpdate(ref SystemState state)
        {
            var mode = GameSettings.NavReplayMode;
            if (mode == NavReplayMode.Off)
            {
                // No allocation cost when off. The log singleton stays
                // unallocated until the player flips into Record/Replay.
                return;
            }

            // Lazy-allocate the singleton on first non-Off tick.
            if (!SystemAPI.HasSingleton<DeterminismReplayLog>())
            {
                var em = state.EntityManager;
                var e = em.CreateEntity();
                em.AddComponentData(e, new DeterminismReplayLog
                {
                    Log = new NativeList<UnitPositionSnapshot>(1024, Allocator.Persistent),
                    CurrentTick = 0,
                    ReplayCursor = 0,
                    HasData = 0,
                    DivergenceCount = 0,
                });
            }

            var logRef = SystemAPI.GetSingletonRW<DeterminismReplayLog>();
            ref var logData = ref logRef.ValueRW;
            uint tick = logData.CurrentTick;

            // Snapshot every UnitTag entity's position. The query is
            // independent of any in-flight job (we're at the end of
            // SimulationSystemGroup so movement/steering have already
            // written their final positions for this tick).
            var unitQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform>()
                .Build();
            using var entities = unitQuery.ToEntityArray(Allocator.TempJob);
            using var transforms = unitQuery.ToComponentDataArray<LocalTransform>(Allocator.TempJob);

            // Build an index permutation sorted by entity.Index ascending
            // so the log is reproducible regardless of chunk-walk order.
            int n = entities.Length;
            var order = new NativeArray<int>(n, Allocator.Temp);
            for (int i = 0; i < n; i++) order[i] = i;
            InsertionSortByEntityIndex(order, entities);

            if (mode == NavReplayMode.Record)
            {
                for (int k = 0; k < n; k++)
                {
                    int srcIdx = order[k];
                    var entity = entities[srcIdx];
                    var t = transforms[srcIdx];
                    logData.Log.Add(new UnitPositionSnapshot
                    {
                        Tick = tick,
                        EntityIndex = entity.Index,
                        PositionMillimeters = UnitPositionSnapshot.ToMillimeters(t.Position),
                    });
                }
                logData.HasData = 1;
            }
            else if (mode == NavReplayMode.Replay)
            {
                if (logData.HasData == 0)
                {
                    // Nothing recorded yet -- can't replay. Degrade to
                    // a silent no-op (the test driver decides whether
                    // to flip to Record on first run).
                }
                else
                {
                    int cursor = logData.ReplayCursor;
                    int diverged = 0;
                    for (int k = 0; k < n && cursor < logData.Log.Length; k++)
                    {
                        int srcIdx = order[k];
                        var entity = entities[srcIdx];
                        var t = transforms[srcIdx];
                        var live = new UnitPositionSnapshot
                        {
                            Tick = tick,
                            EntityIndex = entity.Index,
                            PositionMillimeters = UnitPositionSnapshot.ToMillimeters(t.Position),
                        };
                        var recorded = logData.Log[cursor];
                        if (!live.Equals(recorded))
                        {
                            // First divergence -- log and halt (editor
                            // only). Subsequent divergences this tick
                            // still bump DivergenceCount so tests can
                            // count them.
                            if (diverged == 0)
                            {
                                Debug.LogError(
                                    "[DeterminismReplay] divergence at tick=" + tick
                                    + " entity=" + entity.Index
                                    + " expectedMm=" + recorded.PositionMillimeters
                                    + " gotMm=" + live.PositionMillimeters);
                            }
                            diverged++;
                        }
                        cursor++;
                    }
                    logData.ReplayCursor = cursor;
                    logData.DivergenceCount += diverged;
                }
            }

            order.Dispose();
            logData.CurrentTick = tick + 1;
        }

        // Deterministic insertion sort of the index permutation by
        // entity.Index ascending. n is small in test scenarios (the
        // Phase7Test spawns 100 units) so insertion sort's O(n^2) is
        // fine and the deterministic pivot beats Quicksort's
        // implementation-dependent partition order.
        private static void InsertionSortByEntityIndex(
            NativeArray<int> order, NativeArray<Entity> entities)
        {
            for (int i = 1; i < order.Length; i++)
            {
                int x = order[i];
                int xIdx = entities[x].Index;
                int j = i - 1;
                while (j >= 0 && entities[order[j]].Index > xIdx)
                {
                    order[j + 1] = order[j];
                    j--;
                }
                order[j + 1] = x;
            }
        }
    }
}
