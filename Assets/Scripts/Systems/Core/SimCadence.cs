// SimCadence.cs
// Periodic-system cadence that starts from a KNOWN PHASE every match.
// Location: Assets/Scripts/Systems/Core/SimCadence.cs
//
// THE BUG THIS EXISTS TO PREVENT (desync 2026-08-21, build 0.0.14)
//
// A dozen simulation systems schedule themselves like this:
//
//     _acc += SystemAPI.Time.DeltaTime;
//     if (_acc < Interval) return;
//     _acc = 0f;                       // ... do the periodic work
//
// Under the lockstep fixed step that looks airtight: every tick adds exactly
// 1/30, so both peers reach the interval on the same tick. It is airtight only
// if both peers ENTER the match with the same _acc.
//
// They do not. The ECS world exists — and SimulationSystemGroup runs, per
// frame, with real delta time — from long before the match starts:
// LockstepFixedStep.Install() happens during bootstrap, after the world has
// already been updating. Whatever each machine accumulated in that window is
// arbitrary, machine-dependent, and PERMANENT: once the fixed step takes over,
// both peers advance in perfect lockstep from different starting phases.
//
// _acc is system state. It is in no checksum and no dump. So the two worlds
// stay bit-identical for as long as every periodic decision happens to produce
// the same answer whenever it fires — thousands of ticks, typically — and then
// one system makes a decision that depends on WHERE A MOVING UNIT IS, and the
// worlds fork with nothing in the logs to explain it.
//
// That is exactly what happened. StuckRedirectSystem (Interval 0.5 s = 15
// ticks) fired its detour on tick 3492 on one peer and tick 3495 on the other.
// Its detour destination is computed from the unit's CURRENT position, the
// scout was moving at 6 m/s, and three ticks is 0.6 m — so the two peers sent
// it to two points 0.6 m apart. Everything after that was consequence.
//
// The fix is not to make the accumulators agree by luck. It is to give every
// one of them the same phase at tick 0: SimCadence.BeginMatch() stamps a new
// epoch when the match clock starts, and the first update of each timer after
// that zeroes itself. From tick 0 on, both peers are in phase by construction.

/// <summary>
/// Match-scoped cadence for periodic simulation systems. Use
/// <see cref="Periodic"/> instead of a bare float accumulator in anything
/// inside <c>SimulationSystemGroup</c>.
/// </summary>
public static class SimCadence
{
    /// <summary>
    /// Bumped once per match, before the first tick. Starts at 0 so that a
    /// default-initialised <see cref="Periodic"/> (whose _epoch is also 0)
    /// resets on its first update of the first match — and so that a build
    /// which never calls <see cref="BeginMatch"/> behaves exactly as it did
    /// before this type existed.
    /// </summary>
    public static int Epoch { get; private set; }

    /// <summary>
    /// Start a new match epoch. Call once, after the systems exist and before
    /// the first simulation tick — LockstepFixedStep.Install is that moment in
    /// multiplayer. Every <see cref="Periodic"/> re-phases to zero on its next
    /// update, so all peers share a phase from tick 0.
    /// </summary>
    public static void BeginMatch() => Epoch++;

    /// <summary>
    /// A periodic timer that cannot carry a phase across matches.
    ///
    /// A struct with no constructor, so it drops into a system as a field
    /// exactly where a <c>float _acc</c> used to sit — including in unmanaged
    /// <c>ISystem</c> structs.
    /// </summary>
    public struct Periodic
    {
        private float _acc;
        private int _epoch;

        /// <summary>
        /// Subtract-style tick: true when the interval is due, keeping the
        /// remainder so a long frame does not lose accumulated time. Replaces
        /// <c>_acc += dt; if (_acc &lt; I) return; _acc -= I;</c>
        /// </summary>
        public bool Due(float dt, float interval)
        {
            ReSync();
            _acc += dt;
            if (_acc < interval) return false;
            _acc -= interval;
            return true;
        }

        /// <summary>
        /// Reset-style tick: returns the time elapsed since the last fire when
        /// the interval is due, otherwise 0. Replaces
        /// <c>_acc += dt; if (_acc &lt; I) return; float step = _acc; _acc = 0f;</c>
        ///
        /// Callers that used the elapsed value to scale work (heal-per-second,
        /// progress-per-second) keep getting it, so their arithmetic is
        /// unchanged.
        /// </summary>
        public float DueStep(float dt, float interval)
        {
            ReSync();
            _acc += dt;
            if (_acc < interval) return 0f;
            float step = _acc;
            _acc = 0f;
            return step;
        }

        /// <summary>Zero the accumulator the first time it is used in a new
        /// match, so whatever the menu and the loading screen put into it is
        /// discarded before tick 0 rather than carried into the match.</summary>
        private void ReSync()
        {
            if (_epoch == Epoch) return;
            _epoch = Epoch;
            _acc = 0f;
        }
    }
}
