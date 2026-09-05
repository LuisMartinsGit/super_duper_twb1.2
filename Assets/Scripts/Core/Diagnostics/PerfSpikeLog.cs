// PerfSpikeLog.cs
// Dead-simple spike attribution (2026-08-05, "something lags every 2
// seconds" in 8-player FFA): suspect systems wrap their tick in a
// stopwatch and Report() here; anything over its threshold lands in
// logs/Perf.log with wall time + frame number. One match later the
// hitch has a name and a milliseconds figure instead of a vibe.
// Presentation/diagnostic only — never read by the sim.
//
// IT USED TO CAUSE THE HITCHES IT REPORTED (fixed 2026-09-03).
// Report() called File.AppendAllText, which OPENS, writes and CLOSES the
// file — on the main thread, once per line. On Windows, with a virus
// scanner watching the folder, that open/close is worth anywhere from
// nothing to several hundred milliseconds.
//
// The 2026-09-03 Veilmarch log proves it: two reports on the SAME FRAME are
// 300 ms apart in wall clock, and the only work between them is the write.
//
//   [    42.5s f1995] FRAME: 584.2 ms ... Update=567
//   [    42.8s f1995] FogStamp: 5.3 ms          <- same frame, 300 ms later
//
// It was also a feedback loop. FogStamp reports at 5.6 ms against a 5 ms
// threshold, so it fired 63 times in one match; each write stretched the
// frame past the sentinel's 50 ms, which logged a FRAME line, which was
// another slow write. Every one of those frames showed no GC and cpu=22 on a
// 584 ms frame — the signature of a main thread BLOCKED in I/O rather than
// computing.
//
// Now: one StreamWriter, opened once and held, buffered, flushed on a timer
// (and on close). Writing a line costs a memcpy.

using System.IO;
using UnityEngine;

namespace TheWaningBorder.Core.Diagnostics
{
    public static class PerfSpikeLog
    {
        public const double DefaultThresholdMs = 5.0;

        /// <summary>Seconds between flushes. A crash loses at most this much
        /// of the tail; the alternative cost a frame every time.</summary>
        private const float FlushInterval = 2f;

        private static StreamWriter _writer;
        private static bool _ready;
        private static float _nextFlush;

        /// <summary>
        /// Forget the resolved path so the next spike re-resolves it against
        /// the current match folder. Called by MatchLogSession.Begin — without
        /// it the path is cached for the whole session and every match after
        /// the first would append to (and re-truncate) the first match's file.
        /// </summary>
        public static void Reset()
        {
            Close();
            _ready = false;
        }

        /// <summary>Flush and release the file. Safe to call twice.</summary>
        public static void Close()
        {
            try
            {
                if (_writer != null)
                {
                    _writer.Flush();
                    _writer.Dispose();
                }
            }
            catch { /* diagnostics must never throw into the game */ }
            _writer = null;
        }

        private static bool Ensure()
        {
            if (_ready) return _writer != null;
            _ready = true;
            try
            {
                // Into the current match's folder, so a tester's Perf.log sits
                // next to the AI / player / console logs for the same match
                // instead of being overwritten by the next one.
                string dir = MatchLogSession.CurrentFolder;
                Directory.CreateDirectory(dir);
                _writer = new StreamWriter(Path.Combine(dir, "Perf.log"), append: false)
                {
                    AutoFlush = false,   // the whole point — see the file header
                };
                _writer.WriteLine("=== Perf spikes (ms) ===");
                _nextFlush = Time.realtimeSinceStartup + FlushInterval;
            }
            catch { _writer = null; }
            return _writer != null;
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
                // Same line shape as before — only the DESTINATION changed, from
                // an open/write/close per line to a held buffer.
                _writer.Write(
                    $"[{Time.realtimeSinceStartup,8:0.0}s f{Time.frameCount}] "
                    + $"{label}: {ms:0.0} ms {detail}\n");

                // Flushed on a timer rather than per line: the flush is the
                // expensive half, and a spike burst must not pay for it once
                // per report.
                float now = Time.realtimeSinceStartup;
                if (now >= _nextFlush)
                {
                    _writer.Flush();
                    _nextFlush = now + FlushInterval;
                }
            }
            catch { }
        }
    }
}
