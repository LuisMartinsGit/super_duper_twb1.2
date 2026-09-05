// EcsGroupProfiler.cs
// Per-ComponentSystemGroup timing (2026-09-03).
//
// WHY: PlayerLoopPhaseProfiler narrowed the 0.0.19 Veilmarch hitches to the
// Update phase — "Update=423" on a 473 ms frame — but Update is where the
// ENTIRE ECS world runs, so that is still a haystack. Only 10 files in the
// project call PerfSpikeLog.Report, and every one of them read flat across
// a 13-minute match (FogStamp ~6.7 ms, VeilPulse ~12 ms from start to end)
// while measured FPS fell 46.7 -> 6.9. The cost is in a system nobody
// instrumented, and adding stopwatches one system at a time is a guessing
// game with a match-length feedback loop.
//
// HOW: ComponentSystemGroup.OnUpdate is
//
//     if (RateManager == null) UpdateAllSystems();
//     else while (RateManager.ShouldGroupUpdate(this)) UpdateAllSystems();
//
// so an IRateManager sees the group's update bracketed: the first call is
// entry, the call that returns false is exit. Installing one on every group
// times every group, with no per-system patching.
//
// SAFE ON A GROUP THAT HAD NO RATE MANAGER: the group itself never reads
// Timestep and never pushes time — RateUtils' own managers call
// World.PushTime/PopTime from inside ShouldGroupUpdate. A wrapper that only
// reads a clock therefore cannot change the group's timing or update count:
// with no inner manager it returns true exactly once, which is precisely
// what the RateManager == null branch does.
//
// ONLY groups with a NULL RateManager are probed, and that restriction is
// not caution — it is required. LockstepFixedRateManager asserts
// ReferenceEquals(SimGroup.RateManager, RateManager) both to decide whether
// it is driving the sim and to detect tampering (it logs an error and
// re-attaches). Wrapping it would make lockstep believe it had been
// detached, every frame, in every multiplayer match. FixedStep /
// VariableRate group managers own their own time pushes for the same
// reason. In single-player GameBootstrap explicitly nulls the Simulation,
// Initialization and Presentation group managers, so the groups that matter
// are probed anyway — and if lockstep later claims a group, the probe is
// dropped rather than fought over (see the validation pass below).
//
// Presentation/diagnostic only — never read by the sim.

using System.Collections.Generic;
using System.Diagnostics;
using Unity.Entities;

namespace TheWaningBorder.Core.Diagnostics
{
    public static class EcsGroupProfiler
    {
        /// <summary>Groups whose self time is under this are not worth a line.</summary>
        private const double ReportFloorMs = 2.0;

        /// <summary>How many groups a hitch line names, worst first.</summary>
        private const int MaxNamed = 5;

        private sealed class Probe : IRateManager
        {
            public readonly ComponentSystemGroup Group;

            /// <summary>The manager we displaced, or null. Every call is
            /// forwarded to it; this probe only reads a clock.</summary>
            public readonly IRateManager Inner;

            /// <summary>Wall time of the group's whole update, INCLUDING
            /// nested groups. Live — written the moment the group finishes.</summary>
            public double TotalMs;

            /// <summary>TotalMs as it stood at the last frame boundary. The
            /// hitch line and PlayerLoopPhaseProfiler must describe the SAME
            /// completed frame; reading the live value would mix a frame that
            /// has already run its groups with one that has not.</summary>
            public double FrameMs;

            /// <summary>Iterations as of the last frame boundary.</summary>
            public int FrameIterations;

            /// <summary>Direct child groups, for subtracting nested time.</summary>
            public readonly List<Probe> Children = new();

            private long _start;
            private bool _open;
            private bool _ranOnce;

            public Probe(ComponentSystemGroup group, IRateManager inner)
            {
                Group = group;
                Inner = inner;
            }

            /// <summary>Forwarded, because FixedStepSimulationSystemGroup.Timestep
            /// is a pass-through to its rate manager and gameplay reads it.
            /// The local value is only used when nothing was displaced.</summary>
            public float Timestep
            {
                get => Inner != null ? Inner.Timestep : _timestep;
                set { if (Inner != null) Inner.Timestep = value; else _timestep = value; }
            }
            private float _timestep;

            /// <summary>Times the group's systems ran this frame. Above 1 the
            /// group is CATCHING UP — the fixed-step spiral where a slow frame
            /// makes the next one slower still.</summary>
            public int Iterations;
            private int _running;

            public bool ShouldGroupUpdate(ComponentSystemGroup group)
            {
                if (!_open)
                {
                    _open = true;
                    _ranOnce = false;
                    _running = 0;
                    _start = Stopwatch.GetTimestamp();
                }

                // With an inner manager, its answer is the group's behaviour
                // unchanged. Without one, mimic the RateManager == null
                // branch exactly: update once, then stop.
                bool go = Inner != null ? Inner.ShouldGroupUpdate(group) : !_ranOnce;
                _ranOnce = true;
                if (go) _running++;

                if (!go)
                {
                    TotalMs = (Stopwatch.GetTimestamp() - _start) * 1000.0 / Stopwatch.Frequency;
                    Iterations = _running;
                    _open = false;
                }
                return go;
            }

            /// <summary>Time spent in THIS group's own systems — the nested
            /// groups are reported on their own lines, so counting them here
            /// too would blame every parent for its children.</summary>
            public double SelfMs
            {
                get
                {
                    double self = FrameMs;
                    for (int i = 0; i < Children.Count; i++) self -= Children[i].FrameMs;
                    return self < 0 ? 0 : self;
                }
            }
        }

        private static readonly List<Probe> _probes = new();
        private static Unity.Entities.World _world;
        private static float _nextScan;

        /// <summary>Seconds between rescans. A group can acquire a rate
        /// manager mid-match (lockstep attaching), which silently evicts our
        /// probe; rescanning drops the orphan instead of reporting its last
        /// frozen number for the rest of the match.</summary>
        private const float RescanInterval = 2f;

        /// <summary>Install probes on every eligible group of the default
        /// world, and re-install when the world is replaced (back to menu,
        /// new match). Cheap enough to call every frame.</summary>
        public static void EnsureInstalled()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                if (_world != null) { _probes.Clear(); _world = null; }
                return;
            }

            bool newWorld = !ReferenceEquals(world, _world);
            if (!newWorld && UnityEngine.Time.realtimeSinceStartup < _nextScan) return;
            _nextScan = UnityEngine.Time.realtimeSinceStartup + RescanInterval;

            _probes.Clear();
            _world = world;

            var byGroup = new Dictionary<ComponentSystemGroup, Probe>();
            foreach (var system in world.Systems)
            {
                if (system is not ComponentSystemGroup group) continue;

                if (group.RateManager is Probe existing)
                {
                    byGroup[group] = existing;
                    existing.Children.Clear();
                    _probes.Add(existing);
                    continue;
                }

                // Unity's own RateUtils managers (fixed-step catch-up,
                // variable rate) are safe to wrap: they push and pop time
                // from inside ShouldGroupUpdate and nothing checks their
                // identity. Anything WE wrote is left alone —
                // LockstepFixedRateManager asserts it is still installed by
                // reference and would log an error every frame. Timing the
                // fixed-step group matters because a catch-up spiral is
                // exactly the failure that makes a slow frame slower.
                var inner = group.RateManager;
                if (inner != null && inner.GetType().Namespace != "Unity.Entities") continue;

                var probe = new Probe(group, inner);
                group.RateManager = probe;
                byGroup[group] = probe;
                _probes.Add(probe);
            }

            // Link parents to children so each line reports its own cost.
            foreach (var probe in _probes)
                foreach (var child in probe.Group.ManagedSystems)
                    if (child is ComponentSystemGroup cg && byGroup.TryGetValue(cg, out var cp))
                        probe.Children.Add(cp);
        }

        /// <summary>Close the frame: freeze every group's live total so
        /// Describe reports one coherent frame. Called from
        /// PlayerLoopPhaseProfiler at the top of Initialization, the same
        /// boundary that closes the phase timings.</summary>
        public static void EndFrame()
        {
            for (int i = 0; i < _probes.Count; i++)
            {
                _probes[i].FrameMs = _probes[i].TotalMs;
                _probes[i].FrameIterations = _probes[i].Iterations;
            }
        }

        /// <summary>The slowest groups of the frame just measured, worst
        /// first, as "Name=12". Empty when nothing crossed the floor.</summary>
        public static string Describe()
        {
            if (_probes.Count == 0) return "";

            var sb = new System.Text.StringBuilder(96);
            for (int printed = 0; printed < MaxNamed; printed++)
            {
                Probe worst = null;
                double worstMs = ReportFloorMs;
                foreach (var probe in _probes)
                {
                    double self = probe.SelfMs;
                    if (self <= worstMs) continue;
                    if (_named.Contains(probe)) continue;
                    worst = probe;
                    worstMs = self;
                }
                if (worst == null) break;
                _named.Add(worst);
                sb.Append(worst.Group.GetType().Name).Append('=')
                  .Append(worstMs.ToString("F0"));
                if (worst.FrameIterations > 1) sb.Append('x').Append(worst.FrameIterations);
                sb.Append(' ');
            }
            _named.Clear();
            return sb.ToString();
        }

        private static readonly HashSet<Probe> _named = new();
    }
}
