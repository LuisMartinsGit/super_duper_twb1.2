// SimClock.cs
// The simulation's own clock — seconds of SIMULATED time since the match began.
//
// WHY NOT Time.time
//   Time.time is wall-clock: it advances with rendered frames, at whatever rate
//   the machine happens to manage. Anything that makes a game decision from it
//   makes that decision at a different moment on every machine, which under
//   lockstep means the two peers' worlds fork. Victory timing and AI budgeting
//   both read it, and both are simulation.
//
//   SimClock advances by the SIMULATION's delta instead: the fixed lockstep
//   timestep in multiplayer, the frame delta in single-player (where it is the
//   same thing the simulation itself is using, so nothing changes). Two peers
//   stepping the same number of ticks read the same value, always.
//
// Advanced once per simulation update by SimClockSystem. MonoBehaviours that
// need simulated time — the ones that cannot reach SystemAPI.Time — read it
// here. docs/Multiplayer_LAN_Readiness.md
//
// Location: Assets/Scripts/Core/Types/SimClock.cs

namespace TheWaningBorder.Core
{
    public static class SimClock
    {
        /// <summary>
        /// Seconds of simulated time since <see cref="Reset"/>. Under lockstep
        /// this is exactly (ticks elapsed / tick rate) on every peer.
        /// </summary>
        public static double Elapsed { get; private set; }

        /// <summary>Simulation updates since the match began.</summary>
        public static long Steps { get; private set; }

        /// <summary>Advance by one simulation step. Called by SimClockSystem only.</summary>
        public static void Advance(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            Elapsed += deltaTime;
            Steps++;
        }

        /// <summary>Restart the clock. Called when a match begins.</summary>
        public static void Reset()
        {
            Elapsed = 0d;
            Steps = 0;
        }

        /// <summary>Convenience for callers that want a float, as Time.time was.</summary>
        public static float Now => (float)Elapsed;
    }
}
