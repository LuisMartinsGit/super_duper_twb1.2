// EcsSystemProfiler.cs
// Per-SYSTEM timing for the ECS world (2026-09-03).
//
// WHY: EcsGroupProfiler narrowed the 0.0.19 hitches from "the Update phase"
// to "SimulationSystemGroup" — 794 ms of an 817 ms frame — which is progress
// but still names a group holding most of the game. Two facts make the
// remaining haystack worth a purpose-built tool rather than another round of
// hand-placed stopwatches:
//
//   * the world holds ~226 entities. Nothing that iterates entities can
//     honestly cost 800 ms, so the culprit is O(map), O(archetype) or a
//     blocking wait — none of which the entity count predicts.
//   * the spikes are BURSTY, not periodic (quiet for 22 s, then five in two
//     seconds), so they are event-driven and a fixed sampling interval can
//     miss them entirely.
//
// HOW: Unity.Entities already wraps every system update in a ProfilerMarker
// named "<World Name> <system debug name>" (SystemState.GetProfilerMarkerName,
// under ENABLE_PROFILER — on in the editor and in development builds).
// ProfilerRecorder reads those markers at runtime without the Profiler window
// attached, so every system in the world is timed with no patching, no
// per-system edits and nothing to keep in sync as systems are added.
//
// SumAllSamplesInFrame is deliberate: a group that catches up by running its
// systems several times in one frame (FixedStepSimulationSystemGroup does
// exactly this when it falls behind) would otherwise report only its last
// iteration and look innocent.
//
// Presentation/diagnostic only — never read by the sim.

using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;

namespace TheWaningBorder.Core.Diagnostics
{
    public static class EcsSystemProfiler
    {
        /// <summary>Systems under this are not worth a line on a hitch.</summary>
        private const double ReportFloorMs = 2.0;

        /// <summary>How many systems a hitch line names, worst first.</summary>
        private const int MaxNamed = 6;

        private readonly struct Probe
        {
            public readonly string Name;
            public readonly ProfilerRecorder Recorder;
            public Probe(string name, ProfilerRecorder recorder)
            {
                Name = name;
                Recorder = recorder;
            }
        }

        private static readonly List<Probe> _probes = new();
        private static readonly HashSet<string> _attached = new();
        private static Unity.Entities.World _world;
        private static float _nextScan;

        /// <summary>Seconds between rescans. A system's ProfilerMarker is
        /// registered when the system is CREATED, and systems are created
        /// through the whole boot — so a single scan on the first frame a
        /// world exists would attach to whatever happened to exist by then
        /// and silently miss the rest. Rescanning only ever ADDS.</summary>
        private const float RescanInterval = 2f;

        /// <summary>Attach a recorder to every system marker of the default
        /// world, and re-attach when the world is replaced. Cheap enough to
        /// call every frame.</summary>
        public static void EnsureInstalled()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                if (_world != null) Clear();
                return;
            }

            bool newWorld = !ReferenceEquals(world, _world);
            if (newWorld) { Clear(); _world = world; }
            else if (UnityEngine.Time.realtimeSinceStartup < _nextScan) return;
            _nextScan = UnityEngine.Time.realtimeSinceStartup + RescanInterval;

            // Every system's marker is prefixed with the world's name, which
            // is also what keeps a second world (netcode, thin clients) from
            // being mixed into these numbers.
            string prefix = world.Name + " ";

            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);

            for (int i = 0; i < handles.Count; i++)
            {
                var description = ProfilerRecorderHandle.GetDescription(handles[i]);
                string name = description.Name;
                if (name == null || !name.StartsWith(prefix)) continue;

                // A Burst-compiled system registers a SECOND marker under the
                // same name in the Burst category; both are kept and summed by
                // name at report time, because which of the two carries the
                // sample depends on whether the system ran bursted. The key
                // therefore includes the category, or the second one would be
                // treated as already attached.
                string key = description.Category.Name + "|" + name;
                if (!_attached.Add(key)) continue;

                // WrapAroundWhenCapacityReached is NOT optional. Passing
                // SumAllSamplesInFrame alone REPLACES the default option set
                // and drops it, so a capacity-1 recorder fills after its
                // first sample and never collects again — every system then
                // reports the same frozen boot-frame number for the whole
                // match, which is exactly what the 13:52 log did.
                var recorder = new ProfilerRecorder(handles[i], 1,
                    ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
                recorder.Start();
                _probes.Add(new Probe(name.Substring(prefix.Length), recorder));
            }
        }

        /// <summary>How many system markers are being watched. Reported by
        /// WorldCensus rather than from here: this class installs during boot,
        /// before MatchLogSession truncates the match's Perf.log, so anything
        /// it logged itself was thrown away with the previous file.</summary>
        public static int ProbeCount => _probes.Count;

        private static void Clear()
        {
            for (int i = 0; i < _probes.Count; i++)
            {
                var recorder = _probes[i].Recorder;
                if (recorder.Valid) recorder.Dispose();
            }
            _probes.Clear();
            _attached.Clear();
            _world = null;
        }

        // Reused across frames so a hitch report allocates nothing beyond the
        // string it returns.
        private static readonly Dictionary<string, double> _totals = new();

        /// <summary>The slowest systems of the frame just measured, worst
        /// first, as "Name=12". Empty when nothing crossed the floor.</summary>
        public static string Describe()
        {
            if (_probes.Count == 0) return "";

            _totals.Clear();
            for (int i = 0; i < _probes.Count; i++)
            {
                var probe = _probes[i];
                if (!probe.Recorder.Valid) continue;
                long ns = probe.Recorder.LastValue;
                if (ns <= 0) continue;
                _totals.TryGetValue(probe.Name, out double ms);
                _totals[probe.Name] = ms + ns / 1e6;
            }

            var sb = new System.Text.StringBuilder(128);
            for (int printed = 0; printed < MaxNamed; printed++)
            {
                string worst = null;
                double worstMs = ReportFloorMs;
                foreach (var kv in _totals)
                {
                    if (kv.Value <= worstMs) continue;
                    worst = kv.Key;
                    worstMs = kv.Value;
                }
                if (worst == null) break;
                _totals[worst] = 0;   // consumed
                sb.Append(worst).Append('=').Append(worstMs.ToString("F0")).Append(' ');
            }
            return sb.ToString();
        }
    }
}
