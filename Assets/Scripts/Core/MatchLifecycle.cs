// MatchLifecycle.cs
// Where a match is in its life, for the code that has to wait on it.
//
// Two flags and a phase string that lived on Bootstrap types, and so forced
// the lockstep gate, a curse system and the debug overlay to depend on the
// layer that boots the game. What they actually need is a fact -- "has the map
// finished populating", "is this a new match", "how far did boot get" -- and
// facts belong under everyone rather than above them.
//
// Written by the bootstrap; read by anything that has to wait.

namespace TheWaningBorder.Core
{
    /// <summary>Match lifecycle facts, published by the bootstrap.</summary>
    public static class MatchLifecycle
    {
        /// <summary>
        /// True once the frame-paced spawn coroutine has placed EVERY starting
        /// entity. The lockstep world-ready gate refuses tick 0 until it is
        /// set: a tick that elapses mid-population sees a world the other peer
        /// does not have yet, and any spawn after tick 0 gets a NetworkId
        /// stamped with this machine's local tick. Both fork the first
        /// checksum (the instant tick-30 desync, 2026-08-16).
        /// </summary>
        public static bool MapPopulated;

        /// <summary>
        /// Bumped once per match, at the start of the bootstrap coroutine.
        /// System objects survive scene loads, so anything holding per-match
        /// state -- an accumulator, a seeded RNG's stream position, a cached
        /// singleton -- compares against this and resets when it changes.
        ///
        /// NOT the same as SimCadence.Epoch, which is bumped again when the
        /// fixed-step clock actually starts. This one fires too early to
        /// re-phase a periodic system; see SimCadence for why that matters.
        /// </summary>
        public static int MatchEpoch;

        /// <summary>
        /// How far the bootstrap got, for the crash overlay. A boot that dies
        /// in a coroutine dies silently, so this is often the only evidence of
        /// where it stopped.
        /// </summary>
        public static string BootPhase = "(not started)";
    }
}
