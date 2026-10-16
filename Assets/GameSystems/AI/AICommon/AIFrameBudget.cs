// AIFrameBudget.cs
// The AI's per-frame TIME budget (2026-10-16 AI perf pass).
//
// AIThinkBudget capped HOW MANY heavy thinks ran per frame, but a count is
// not a cost: one SimpleAISystem stage measured 3-9 ms late game, and with
// starving brains forced through every frame (thinkStarvationIntervals 1 at
// Expert's 0.25 s interval is "always starving") the cap never bound — the
// development player showed SimpleAISystem 9.25 ms + AITacticsMicroSystem
// 4.98 ms per frame. This is a Stopwatch shared by every AI system:
//
//   * each AI system wraps its OnUpdate in Enter() / Exit(), so the time it
//     spends is charged to the rendered frame (several lockstep catch-up
//     ticks inside one frame share ONE budget — frame time is what it bounds);
//   * work that can wait asks HasRoom(estimate) before it starts, with the
//     caller's running estimate of what the next piece costs;
//   * the first piece of work in a frame is always allowed, and a caller that
//     is starving may Force() past an exhausted budget — at most
//     maxForcedAIWorkPerFrame times a frame — so a crowded match slows the AI
//     down but never stops it, and never stacks several forced pieces on one
//     frame (the spike the count cap used to allow).
//
// WALL CLOCK IS SAFE HERE: the AI runs only where
// GameSettings.ShouldRunAIBrains() (the host), and everything it decides
// leaves as lockstep commands. WHEN it thinks never touches the simulation's
// determinism; only WHAT it orders does, and that is replicated.

using System.Diagnostics;

namespace TheWaningBorder.AI
{
    public static class AIFrameBudget
    {
        private static readonly double TicksPerMs = Stopwatch.Frequency / 1000.0;

        private static int _frame = -1;
        private static long _spentTicks;
        private static long _openedAt;
        private static int _depth;
        private static int _grants;
        private static int _forced;

        // Diagnostics: what the AI spent per frame, averaged over a window.
        private static double _windowSpentMs;
        private static int _windowFrames;
        private static double _windowPeakMs;
        private static double _lastFrameMs;

        static SimpleAISystemConfig Cfg => SimpleAISystemConfig.I;

        private static void Roll()
        {
            int f = UnityEngine.Time.frameCount;
            if (f == _frame) return;
            if (_frame >= 0)
            {
                _lastFrameMs = _spentTicks / TicksPerMs;
                _windowSpentMs += _lastFrameMs;
                _windowFrames++;
                if (_lastFrameMs > _windowPeakMs) _windowPeakMs = _lastFrameMs;
            }
            _frame = f;
            _spentTicks = 0;
            _grants = 0;
            _forced = 0;
            // A section left open by an exception would charge forever.
            if (_depth > 0) _openedAt = Stopwatch.GetTimestamp();
        }

        /// <summary>Start charging this frame (nestable).</summary>
        public static void Enter()
        {
            Roll();
            if (_depth++ == 0) _openedAt = Stopwatch.GetTimestamp();
        }

        /// <summary>Stop charging (pairs with <see cref="Enter"/>).</summary>
        public static void Exit()
        {
            if (_depth <= 0) return;
            if (--_depth == 0) _spentTicks += Stopwatch.GetTimestamp() - _openedAt;
        }

        /// <summary>AI time charged to this rendered frame so far, ms.</summary>
        public static double SpentMs
        {
            get
            {
                Roll();
                long t = _spentTicks;
                if (_depth > 0) t += Stopwatch.GetTimestamp() - _openedAt;
                return t / TicksPerMs;
            }
        }

        /// <summary>The whole AI's per-frame allowance, ms (config).</summary>
        public static double BudgetMs => System.Math.Max(0.1, Cfg.aiFrameBudgetMs);

        /// <summary>
        /// May a piece of deferrable work estimated at
        /// <paramref name="estimateMs"/> start now? Always yes for the first
        /// granted piece of the frame (progress is guaranteed); afterwards
        /// only while spent + estimate stays inside the budget.
        /// </summary>
        public static bool HasRoom(double estimateMs = 0.0)
        {
            Roll();
            if (_grants == 0) return true;
            return SpentMs + System.Math.Max(0.0, estimateMs) <= BudgetMs;
        }

        /// <summary><see cref="HasRoom"/>, and record the grant when yes.</summary>
        public static bool TryGrant(double estimateMs = 0.0)
        {
            if (!HasRoom(estimateMs)) return false;
            _grants++;
            return true;
        }

        /// <summary>
        /// A starving caller's way past an exhausted budget: granted at most
        /// maxForcedAIWorkPerFrame times per rendered frame. Within budget it
        /// is an ordinary grant and does not use up the forced quota.
        /// </summary>
        public static bool TryGrantOrForce(double estimateMs = 0.0)
        {
            if (TryGrant(estimateMs)) return true;
            if (_forced >= System.Math.Max(1, Cfg.maxForcedAIWorkPerFrame)) return false;
            _forced++;
            _grants++;
            return true;
        }

        /// <summary>Take the averaged per-frame spend since the last call
        /// (diagnostic summary line).</summary>
        public static void TakeWindow(out double avgMs, out double peakMs, out int frames)
        {
            frames = _windowFrames;
            avgMs = frames > 0 ? _windowSpentMs / frames : 0.0;
            peakMs = _windowPeakMs;
            _windowSpentMs = 0.0;
            _windowFrames = 0;
            _windowPeakMs = 0.0;
        }
    }
}
