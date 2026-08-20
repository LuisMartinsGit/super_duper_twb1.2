// LockstepTiming.cs
// Tick rate and input delay for the lockstep simulation — shared by the
// LockstepManager that drives it and the MatchSettingsSync that puts it on the
// wire, so the two can never disagree about how fast the world runs.
//
// WHY THE TICK RATE MOVED OFF 10 Hz
//   Under DeterministicLockstep the ECS SimulationSystemGroup runs exactly ONCE
//   PER TICK. At the old 10 ticks/second that meant the whole world advanced ten
//   times a second, and because views are snapped straight to ECS transforms,
//   every unit on screen moved in 100 ms jumps — the game looked like it was
//   running at 10 fps. It also put 200 ms between a click and its effect on a
//   LAN link with a fraction of a millisecond of latency.
//
//   30 Hz costs no more CPU than the old free-running per-frame simulation (it
//   is a CAP, not an addition: at 60+ fps the fixed step runs FEWER sim updates
//   than before), and it takes command latency to ~66 ms while making the
//   remaining stutter small enough for interpolation to hide entirely.
//
// docs/Multiplayer_LAN_Readiness.md
// Location: Assets/Scripts/Core/Multiplayer/LockstepTiming.cs

namespace TheWaningBorder.Core.Multiplayer
{
    public static class LockstepTiming
    {
        /// <summary>Simulation ticks per second. 30 Hz — see the file header.</summary>
        public const int DefaultTicksPerSecond = 30;

        /// <summary>Slowest tick rate the game will accept from a host.</summary>
        public const int MinTicksPerSecond = 10;
        /// <summary>Fastest. Above this the per-tick datagram overhead dominates.</summary>
        public const int MaxTicksPerSecond = 60;

        private static int _ticksPerSecond = DefaultTicksPerSecond;

        /// <summary>
        /// Ticks per second for THIS match. Set once from the host's match
        /// settings before the simulation starts; changing it mid-match would
        /// change every peer's timestep and fork the world.
        /// </summary>
        public static int TicksPerSecond
        {
            get => _ticksPerSecond;
            set => _ticksPerSecond = value < MinTicksPerSecond ? MinTicksPerSecond
                 : value > MaxTicksPerSecond ? MaxTicksPerSecond
                 : value;
        }

        /// <summary>Seconds of simulated time per tick — the fixed timestep.</summary>
        public static float TickDuration => 1f / _ticksPerSecond;

        // ═══════════════════════════════════════════════════════════════
        // INPUT DELAY
        // ═══════════════════════════════════════════════════════════════
        //
        // How many ticks ahead a command is stamped. It has to cover the
        // round-trip to the slowest peer, or that peer's confirmation arrives
        // after the tick it belongs to has already run and the command is
        // dropped on the floor.
        //
        // It used to be a hard-coded 2 regardless of the link. On a LAN — where
        // the round trip is a fraction of a millisecond — that is pure added
        // latency; over a slow link it is not enough. LatencyTracker measures
        // the real round trip and Recommend() turns it into a tick count.

        /// <summary>Never go below this: one tick of slack absorbs jitter.</summary>
        public const int MinInputDelayTicks = 1;
        /// <summary>Beyond this the game feels unresponsive; better to stall.</summary>
        public const int MaxInputDelayTicks = 10;

        private static int _inputDelayTicks = 2;

        /// <summary>
        /// Ticks between issuing a command and executing it. Read every time a
        /// command is stamped, so an adjustment takes effect immediately.
        /// </summary>
        public static int InputDelayTicks
        {
            get => _inputDelayTicks;
            set => _inputDelayTicks = value < MinInputDelayTicks ? MinInputDelayTicks
                 : value > MaxInputDelayTicks ? MaxInputDelayTicks
                 : value;
        }

        /// <summary>Current command latency in milliseconds, for the HUD.</summary>
        public static float InputDelayMs => _inputDelayTicks * TickDuration * 1000f;

        /// <summary>
        /// Ticks of input delay that cover a round trip of
        /// <paramref name="rttMs"/>, with a tick of headroom for jitter.
        /// </summary>
        public static int RecommendInputDelay(float rttMs)
        {
            if (rttMs <= 0f) return MinInputDelayTicks;
            // Half the round trip is the one-way cost; add a whole tick of slack
            // so ordinary jitter does not push a command past its deadline.
            float oneWaySeconds = (rttMs * 0.5f) / 1000f;
            int ticks = (int)UnityEngine.Mathf.Ceil(oneWaySeconds / TickDuration) + 1;
            return ticks < MinInputDelayTicks ? MinInputDelayTicks
                 : ticks > MaxInputDelayTicks ? MaxInputDelayTicks
                 : ticks;
        }

        /// <summary>
        /// True while the ECS simulation is being driven at a FIXED timestep,
        /// one step per lockstep tick, instead of free-running per frame. Set by
        /// LockstepBootstrap when it installs the rate manager.
        ///
        /// The presentation layer reads this to decide whether views need
        /// interpolating: a fixed-step world publishes new transforms
        /// <see cref="TicksPerSecond"/> times a second, and views snapped
        /// straight to them move in visible steps between those moments. When
        /// the simulation free-runs per frame there is nothing to interpolate
        /// and doing so would only add a frame of lag.
        /// </summary>
        public static bool FixedStepActive { get; set; }

        /// <summary>Restore match defaults. Called when a match starts.</summary>
        public static void Reset()
        {
            _ticksPerSecond = DefaultTicksPerSecond;
            _inputDelayTicks = 2;
            FixedStepActive = false;
        }
    }
}
