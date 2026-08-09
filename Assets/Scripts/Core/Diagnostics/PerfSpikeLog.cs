// PerfSpikeLog.cs
// Dead-simple spike attribution (2026-08-05, "something lags every 2
// seconds" in 8-player FFA): suspect systems wrap their tick in a
// stopwatch and Report() here; anything over its threshold lands in
// logs/Perf.log with wall time + frame number. One match later the
// hitch has a name and a milliseconds figure instead of a vibe.
// Presentation/diagnostic only — never read by the sim.
// Location: Assets/Scripts/Core/Diagnostics/

using System.IO;
using UnityEngine;

namespace TheWaningBorder.Core.Diagnostics
{
    public static class PerfSpikeLog
    {
        public const double DefaultThresholdMs = 5.0;

        private static string _path;
        private static bool _ready;

        private static bool Ensure()
        {
            if (_ready) return true;
            try
            {
                string dir = Path.Combine(Application.dataPath, "..", "logs");
                Directory.CreateDirectory(dir);
                _path = Path.Combine(dir, "Perf.log");
                File.WriteAllText(_path, "=== Perf spikes (ms) ===\n");
                _ready = true;
            }
            catch { /* diagnostics must never throw into the game */ }
            return _ready;
        }

        /// <summary>Append one spike line when <paramref name="ms"/> is at or
        /// over the threshold. Main-thread only (uses UnityEngine.Time).</summary>
        public static void Report(string label, double ms, string detail = "",
            double thresholdMs = DefaultThresholdMs)
        {
            if (ms < thresholdMs) return;
            if (!Ensure()) return;
            try
            {
                File.AppendAllText(_path,
                    $"[{Time.realtimeSinceStartup,8:0.0}s f{Time.frameCount}] {label}: {ms:0.0} ms {detail}\n");
            }
            catch { }
        }
    }
}
