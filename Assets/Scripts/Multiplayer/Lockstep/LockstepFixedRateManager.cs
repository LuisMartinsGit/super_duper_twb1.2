// LockstepFixedRateManager.cs
// Flag-gated fixed-timestep driver for TRUE deterministic lockstep.
//
// THE PROBLEM IT SOLVES: by default the ECS SimulationSystemGroup is appended
// to the Unity player loop and updates EVERY rendered frame with a variable
// Time.deltaTime. That makes every continuous quantity (unit positions, combat
// cooldowns, AI cadence, income) frame-rate dependent, so two clients running
// at different fps diverge — the simulation is NOT deterministic, only the
// input/commands are. (The lockstep checksum even excludes positions for this
// reason.)
//
// THE FIX: when GameSettings.DeterministicLockstep is on (multiplayer only),
// install this rate manager on SimulationSystemGroup. It returns false for the
// normal per-frame player-loop call (so the group does NOTHING on its own), and
// instead the group runs EXACTLY ONCE — with a FIXED delta — each time
// LockstepManager.ProcessTick calls LockstepFixedStep.Step() (i.e. once per
// confirmed lockstep tick). Commands for the tick are already applied by
// ProcessTick immediately before the step, so "apply tick T's commands → step
// the sim for T" happens atomically and identically on every client.
//
// SAFETY: off by default. When off, nothing is installed and the game runs
// exactly as before. MP-only — single-player is unaffected (it keeps running
// the sim per-frame). This is the foundation; the per-system determinism fixes
// and the SimulationSystemGroup vs PresentationSystemGroup partition build on
// top of it.
//
// Location: Assets/Scripts/Multiplayer/Lockstep/LockstepFixedRateManager.cs

using Unity.Core;
using Unity.Entities;

namespace TheWaningBorder.Multiplayer
{
    /// <summary>
    /// Runs <see cref="SimulationSystemGroup"/> exactly once per lockstep tick at
    /// a fixed delta. <see cref="ShouldGroupUpdate"/> returns false on ordinary
    /// player-loop frames; a single step is unlocked by <see cref="RequestStep"/>.
    /// </summary>
    public sealed class LockstepFixedRateManager : IRateManager
    {
        private float _timestep;
        private bool _stepRequested;
        private bool _didPushTime;
        private double _elapsed;

        public float Timestep { get => _timestep; set => _timestep = value; }

        public LockstepFixedRateManager(float timestep) { _timestep = timestep; }

        /// <summary>Unlock exactly one fixed step on the next group Update().</summary>
        public void RequestStep() => _stepRequested = true;

        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
            // Pop the time we pushed on the previous successful call.
            if (_didPushTime)
            {
                group.World.PopTime();
                _didPushTime = false;
            }

            // Ordinary player-loop frame with no pending tick → run nothing.
            if (!_stepRequested) return false;
            _stepRequested = false;

            _elapsed += _timestep;
            group.World.PushTime(new TimeData(_elapsed, _timestep));
            _didPushTime = true;
            return true; // run the group once with the fixed dt
        }
    }

    /// <summary>
    /// Process-wide handle so the lockstep MonoBehaviour can drive the ECS sim
    /// group step. Installed by <c>LockstepBootstrap</c> when the determinism
    /// flag is on; a no-op otherwise.
    /// </summary>
    public static class LockstepFixedStep
    {
        public static bool Active { get; private set; }
        public static LockstepFixedRateManager RateManager { get; private set; }
        public static ComponentSystemGroup SimGroup { get; private set; }

        public static void Install(Unity.Entities.World world, float timestep)
        {
            if (world == null || !world.IsCreated) return;
            SimGroup = world.GetExistingSystemManaged<SimulationSystemGroup>();
            if (SimGroup == null) return;
            RateManager = new LockstepFixedRateManager(timestep);
            SimGroup.RateManager = RateManager;
            Active = true;
        }

        public static void Uninstall()
        {
            if (SimGroup != null) SimGroup.RateManager = null;
            SimGroup = null;
            RateManager = null;
            Active = false;
        }

        /// <summary>
        /// Advance the deterministic simulation exactly one fixed step. Called
        /// from LockstepManager.ProcessTick after that tick's commands apply.
        /// No-op when the flag is off (the player loop drives the sim instead).
        /// </summary>
        public static void Step()
        {
            if (!Active || RateManager == null || SimGroup == null) return;
            RateManager.RequestStep();
            SimGroup.Update();
        }
    }
}
