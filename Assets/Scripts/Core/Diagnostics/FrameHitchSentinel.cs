// FrameHitchSentinel.cs
// Self-installing frame watchdog (2026-08-05): logs any frame over the
// threshold to logs/Perf.log, with the GC collection delta — so a hitch
// no instrumented system claims is immediately attributable to garbage
// collection (or to something still un-instrumented). Suppressed while
// the loading screen is up (loading frames are legitimately long).
// Location: Assets/Scripts/Core/Diagnostics/

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

        private void Update()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            int g0 = System.GC.CollectionCount(0);
            int g1 = System.GC.CollectionCount(1);
            int g2 = System.GC.CollectionCount(2);

            if (ms >= FrameThresholdMs && !TheWaningBorder.UI.Menus.LoadingScreen.IsVisible)
                PerfSpikeLog.Report("FRAME", ms,
                    $"gc0+{g0 - _gc0} gc1+{g1 - _gc1} gc2+{g2 - _gc2}", 0.0);

            _gc0 = g0; _gc1 = g1; _gc2 = g2;
        }
    }
}
