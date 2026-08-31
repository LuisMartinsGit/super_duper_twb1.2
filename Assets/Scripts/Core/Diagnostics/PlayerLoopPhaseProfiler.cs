// PlayerLoopPhaseProfiler.cs
// Times every TOP-LEVEL player-loop phase each frame so a hitch can name
// the phase that ate it (2026-09-01).
//
// WHY: the 0.0.18 Veilmarch build stalled 100-600 ms about once a second —
// 157 SECONDS of hitches in an 11-minute session — while every instrumented
// system read <=14 ms, GC was silent, and FrameTimingManager's cpu/gpu
// samples (delivered a few frames late) showed a healthy steady state. The
// stall was invisible to everything we had. This closes the gap at the
// engine-loop level: Initialization / EarlyUpdate / FixedUpdate / PreUpdate /
// Update / PreLateUpdate / PostLateUpdate each get a stopwatch, and
// FrameHitchSentinel prints the breakdown for any frame it reports.
//
// Implementation: a timing sample is INSERTED at the head of each top-level
// phase's subsystem list. Each marker records "now" on entry to its phase;
// the previous phase's duration is the delta from the previous marker. The
// tail (after PostLateUpdate, i.e. render submission + present + next-frame
// wait) is the remainder up to the next frame's first marker.

using System.Diagnostics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.LowLevel;

namespace TheWaningBorder.Core.Diagnostics
{
    public static class PlayerLoopPhaseProfiler
    {
        private static readonly string[] PhaseNames =
        {
            "Init", "EarlyUpd", "FixedUpd", "PreUpd", "Update", "PreLate", "PostLate",
        };

        // Ticks at entry of each phase this frame; [7] = entry of NEXT frame's
        // first phase, closing the tail.
        private static readonly long[] _entry = new long[8];
        private static readonly double[] _lastMs = new double[8];
        private static int _cursor;
        private static bool _installed;

        /// <summary>The previous COMPLETE frame's phase durations, in ms:
        /// Init, EarlyUpdate, FixedUpdate, PreUpdate, Update, PreLateUpdate,
        /// PostLateUpdate, and Tail (render submit + present + vsync wait).
        /// Formatted for the hitch log; empty until two frames have run.</summary>
        public static string Describe()
        {
            var sb = new System.Text.StringBuilder(96);
            for (int i = 0; i < 7; i++)
            {
                if (_lastMs[i] < 1.0) continue;   // keep the line readable
                sb.Append(PhaseNames[i]).Append('=').Append(_lastMs[i].ToString("F0")).Append(' ');
            }
            sb.Append("Tail=").Append(_lastMs[7].ToString("F0"));
            return sb.ToString();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            if (_installed) return;
            _installed = true;

            var loop = PlayerLoop.GetCurrentPlayerLoop();
            if (loop.subSystemList == null) return;

            int marker = 0;
            for (int i = 0; i < loop.subSystemList.Length && marker < 7; i++)
            {
                var phase = loop.subSystemList[i];
                var name = phase.type?.Name;
                int index = name switch
                {
                    "Initialization" => 0,
                    "EarlyUpdate" => 1,
                    "FixedUpdate" => 2,
                    "PreUpdate" => 3,
                    "Update" => 4,
                    "PreLateUpdate" => 5,
                    "PostLateUpdate" => 6,
                    _ => -1,
                };
                if (index < 0) continue;
                marker++;

                var subs = phase.subSystemList ?? System.Array.Empty<PlayerLoopSystem>();
                var extended = new PlayerLoopSystem[subs.Length + 1];
                int captured = index;
                extended[0] = new PlayerLoopSystem
                {
                    type = typeof(PlayerLoopPhaseProfiler),
                    updateDelegate = () => Mark(captured),
                };
                System.Array.Copy(subs, 0, extended, 1, subs.Length);
                phase.subSystemList = extended;
                loop.subSystemList[i] = phase;
            }

            PlayerLoop.SetPlayerLoop(loop);
        }

        private static void Mark(int phase)
        {
            long now = Stopwatch.GetTimestamp();

            if (phase == 0)
            {
                // New frame: close the previous one. Phase i ran from its
                // entry to the next marker's entry; the tail runs from the
                // last marker to THIS entry.
                if (_entry[0] != 0)
                {
                    for (int i = 0; i < 7; i++)
                    {
                        long from = _entry[i];
                        long to = i < 6 ? _entry[i + 1] : _entry[7];
                        _lastMs[i] = from != 0 && to != 0
                            ? (to - from) * 1000.0 / Stopwatch.Frequency : 0;
                    }
                    _lastMs[7] = _entry[7] != 0
                        ? (now - _entry[7]) * 1000.0 / Stopwatch.Frequency : 0;
                }
                System.Array.Clear(_entry, 0, _entry.Length);
                _entry[0] = now;
                _cursor = 0;
                return;
            }

            // FixedUpdate can run its list several times a frame; keep the
            // FIRST entry so the phase spans all iterations.
            if (_entry[phase] == 0) _entry[phase] = now;
            if (phase == 6) _entry[7] = 0;   // reset; set below on exit-marker

            _cursor = phase;

            // PostLateUpdate is the last phase we mark the entry of; its end
            // (= tail start) is approximated by the entry of the next frame,
            // so also stamp a "last marker seen" for the tail computation.
            _entry[7] = now;
        }
    }
}
