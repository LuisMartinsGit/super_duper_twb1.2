// FrameHitchSentinel.cs
// Self-installing frame watchdog (2026-08-05): logs any frame over the
// threshold to logs/Perf.log, with the GC collection delta — so a hitch
// no instrumented system claims is immediately attributable to garbage
// collection (or to something still un-instrumented). Suppressed while
// the loading screen is up (loading frames are legitimately long).

using UnityEngine;

namespace TheWaningBorder.Core.Diagnostics
{
    public sealed class FrameHitchSentinel : MonoBehaviour
    {
        private const float FrameThresholdMs = 50f;

        private int _gc0, _gc1, _gc2;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("[FrameHitchSentinel]")
            { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<FrameHitchSentinel>();
        }

        // Reused across frames; FrameTimingManager fills it in place.
        private readonly FrameTiming[] _timing = new FrameTiming[1];

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            int g0 = System.GC.CollectionCount(0);
            int g1 = System.GC.CollectionCount(1);
            int g2 = System.GC.CollectionCount(2);

            if (ms >= FrameThresholdMs && !TheWaningBorder.Core.PresentationState.LoadingOverlayVisible)
            {
                // CPU vs GPU split (2026-08-31). A long frame with no
                // instrumented label and no GC used to be a dead end — the
                // Veilmarch build hitched 300-400 ms with every CPU counter
                // clean, and whether the GPU was the culprit was pure
                // inference. enableFrameTimingStats is on in Player
                // Settings; a gpu reading of 0 means the platform did not
                // deliver timings, not that the GPU was idle.
                FrameTimingManager.CaptureFrameTimings();
                double cpu = 0, gpu = 0;
                if (FrameTimingManager.GetLatestTimings(1, _timing) > 0)
                {
                    cpu = _timing[0].cpuFrameTime;
                    gpu = _timing[0].gpuFrameTime;
                }

                PerfSpikeLog.Report("FRAME", ms,
                    $"gc0+{g0 - _gc0} gc1+{g1 - _gc1} gc2+{g2 - _gc2} " +
                    $"cpu{cpu:F0} gpu{gpu:F0} | {PlayerLoopPhaseProfiler.Describe()}", 0.0);
            }

            _gc0 = g0; _gc1 = g1; _gc2 = g2;
        }
    }
}
