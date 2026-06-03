// Phase7TestSetup.cs
// task-112 M7 -- determinism replay scenario. Spawns 100 Blue Swordsmen
// in a 10x10 grid, runs a scripted sequence of move-commands over 30
// sim ticks, snapshots positions at each recorded tick, then expects a
// re-run of the same sequence to produce a byte-identical snapshot
// stream.
//
// The scenario itself only sets up the spawn + the scripted command
// runner; the determinism comparison happens via
// DeterminismReplaySystem reading GameSettings.NavReplayMode. The
// Phase7Test PlayMode test toggles the mode (Record on first run,
// Replay on second) and asserts DivergenceCount == 0.
//
// AC-P7 (R7, R8): the test driver runs the scenario, records 30
// scripted ticks, replays, and asserts byte-identical snapshots.
//
// Location: Assets/Scripts/Bootstrap/Phase7TestSetup.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// 100-unit Phase 7 determinism scenario. The setup is intentionally
    /// minimal so the only sources of nondeterminism are the systems we
    /// audit -- no random spawn positions, no float-driven scripting,
    /// no off-tick events.
    /// </summary>
    public static class Phase7TestSetup
    {
        public const int RowsX = 10;
        public const int RowsZ = 10;
        public const float Spacing = 2f;
        public const float SpawnOriginX = -10f;
        public const float SpawnOriginZ = -10f;

        public static void SpawnScenarioEntities(EntityManager em)
        {
            // task-112 M7 -- now in Replay/compare mode. The first run of
            // Phase7Test (when this was NavReplayMode.Record) populated
            // DeterminismReplayLog with the per-tick position snapshots
            // of all 100 units. This run reuses that log: at each tick
            // DeterminismReplaySystem compares the live unit positions
            // against the recorded ones and bumps DivergenceCount on the
            // first mismatch per tick.
            //
            // Expected result on a deterministic sim: DivergenceCount
            // stays 0 for the full 30+ tick scripted sequence and no
            // "[Determinism] mismatch at tick N" errors appear in the
            // Console.
            //
            // To re-record (e.g. after editing the script), flip this
            // line back to NavReplayMode.Record and run the scenario
            // once; the existing log is overwritten.
            GameSettings.NavReplayMode = NavReplayMode.Replay;

            // 10x10 grid of Blue Swordsmen. Deterministic spawn order
            // (row-major) so the resulting entity indices are the same
            // every run.
            for (int rz = 0; rz < RowsZ; rz++)
            {
                for (int rx = 0; rx < RowsX; rx++)
                {
                    float sx = SpawnOriginX + rx * Spacing;
                    float sz = SpawnOriginZ + rz * Spacing;
                    float sy = TerrainUtility.GetHeight(sx, sz);
                    var spawn = new float3(sx, sy, sz);
                    var entity = UnitFactory.Create(em, "Swordsman", spawn, Faction.Blue);
                    if (entity == Entity.Null) continue;

                    // Attach the scripted command runner -- one queue
                    // per unit so the scheduling is per-entity-stable.
                    em.AddComponentData(entity, new ScriptedCommandRunner
                    {
                        StartTick = 0,
                        ScriptIndex = 0,
                    });
                }
            }
        }

        /// <summary>
        /// task-112 M7 -- per-unit scripted-command playback state. The
        /// runner system (<see cref="Phase7ScriptedRunnerSystem"/>)
        /// reads the active script from
        /// <see cref="Phase7ScriptedCommands"/> and emits the matching
        /// MoveCommand on the right tick.
        /// </summary>
        public struct ScriptedCommandRunner : IComponentData
        {
            /// <summary>Sim tick the runner started at (set by the
            /// system on first observation).</summary>
            public uint StartTick;
            /// <summary>Cursor into the static script entries this
            /// runner has executed.</summary>
            public int ScriptIndex;
        }
    }

    /// <summary>
    /// task-112 M7 -- static script of (tick offset, goal cell) pairs
    /// every unit in the Phase7Test scenario walks through. Deterministic
    /// across machines because every value is a compile-time integer.
    ///
    /// At 60 Hz the 30-tick window covers 0.5 sim seconds -- not enough
    /// for units to reach the goals, but enough for the integrator +
    /// steering + flow pipeline to produce 30 ticks of consistent
    /// position deltas, which is exactly what the determinism audit
    /// needs.
    /// </summary>
    public static class Phase7ScriptedCommands
    {
        public struct ScriptEntry
        {
            public uint TickOffset;
            public float3 Goal;
        }

        /// <summary>
        /// Shared script every unit follows. Each entry's goal is in
        /// world space; the unit's start position offsets are
        /// position-independent for determinism (every unit gets the
        /// same goal vector, NOT a relative offset).
        /// </summary>
        public static readonly ScriptEntry[] Script = new ScriptEntry[]
        {
            new ScriptEntry { TickOffset = 0,  Goal = new float3(20f, 0f, 20f) },
            new ScriptEntry { TickOffset = 5,  Goal = new float3(-15f, 0f, 15f) },
            new ScriptEntry { TickOffset = 10, Goal = new float3(15f, 0f, -15f) },
            new ScriptEntry { TickOffset = 15, Goal = new float3(-20f, 0f, -20f) },
            new ScriptEntry { TickOffset = 20, Goal = new float3(25f, 0f, 0f) },
            new ScriptEntry { TickOffset = 25, Goal = new float3(0f, 0f, 25f) },
        };

        /// <summary>Number of ticks the Phase7Test scenario should run
        /// before snapshotting + replaying.</summary>
        public const uint TotalTicks = 30;
    }

    /// <summary>
    /// task-112 M7 -- ECS system that drives the
    /// <see cref="Phase7TestSetup.ScriptedCommandRunner"/> state per
    /// unit. Iterates the static script and emits
    /// <see cref="TheWaningBorder.Core.Commands.Types.MoveCommandHelper.Execute"/>
    /// on the right tick.
    ///
    /// Runs in <see cref="SimulationSystemGroup"/> BEFORE the nav
    /// systems so the command's NavPathRequest is observable on the
    /// same tick.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class Phase7ScriptedRunnerSystem : SystemBase
    {
        private uint _tick;

        protected override void OnUpdate()
        {
            if (GameSettings.ActiveScenario != ScenarioType.Phase7Test) return;

            var em = EntityManager;
            var script = Phase7ScriptedCommands.Script;

            // Collect entities into a deterministic order list first
            // (chunk walk + sort by entity.Index ascending). Then walk
            // and emit. The sort beats chunk-walk order leaking into
            // the per-tick command flow.
            var query = GetEntityQuery(
                ComponentType.ReadOnly<Phase7TestSetup.ScriptedCommandRunner>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());

            var entities = query.ToEntityArray(Allocator.TempJob);
            // Insertion-sort by entity.Index ascending.
            for (int i = 1; i < entities.Length; i++)
            {
                var x = entities[i];
                int j = i - 1;
                while (j >= 0 && entities[j].Index > x.Index)
                {
                    entities[j + 1] = entities[j];
                    j--;
                }
                entities[j + 1] = x;
            }

            for (int i = 0; i < entities.Length; i++)
            {
                var e = entities[i];
                var runner = em.GetComponentData<Phase7TestSetup.ScriptedCommandRunner>(e);
                uint local = _tick - runner.StartTick;
                while (runner.ScriptIndex < script.Length
                    && local >= script[runner.ScriptIndex].TickOffset)
                {
                    var entry = script[runner.ScriptIndex];
                    var goal = entry.Goal;
                    goal.y = TerrainUtility.GetHeight(goal.x, goal.z);
                    MoveCommandHelper.Execute(em, e, goal);
                    runner.ScriptIndex++;
                }
                em.SetComponentData(e, runner);
            }

            entities.Dispose();
            _tick++;
        }
    }
}
