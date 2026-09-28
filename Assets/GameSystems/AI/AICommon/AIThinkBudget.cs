// AIThinkBudget.cs
// How many HEAVY AI thinks may run in one rendered frame, across every brain
// and every AI system (2026-09-25 AI perf pass).
//
// The brains were phase-staggered but nothing capped a frame: think phases of
// 0.1 + 0.37 x faction collide at the 0.25 s Expert interval, a long frame
// expires several timers at once, and the endgame systems (5 s cadence) land
// on top of whichever brain happens to think that frame — the "AIThink brains
// 4" spikes. This is the one shared counter all of them draw from. A caller
// that is refused simply tries again next frame; a caller overdue past its
// starvation limit is always granted, so a crowded match slows AI thinking
// down but never stops a brain.
//
// Host-only (the AI runs only where GameSettings.ShouldRunAIBrains()); keyed on
// the RENDERED frame, because the cost being bounded is frame time — several
// lockstep catch-up ticks inside one frame share one budget.

namespace TheWaningBorder.AI
{
    public static class AIThinkBudget
    {
        private static int _frame = -1;
        private static int _used;

        /// <summary>
        /// Claim one heavy think for this frame. <paramref name="force"/>
        /// (the caller is starving) always succeeds and still counts.
        /// </summary>
        public static bool TryClaim(bool force = false)
        {
            int frame = UnityEngine.Time.frameCount;
            if (frame != _frame) { _frame = frame; _used = 0; }
            int cap = System.Math.Max(1, SimpleAISystemConfig.I.maxBrainThinksPerFrame);
            if (_used >= cap && !force) return false;
            _used++;
            return true;
        }
    }
}
