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

        /// <summary>True while a time this manager pushed is still on the
        /// world's time stack (popped on the NEXT ShouldGroupUpdate call).
        /// Uninstall must pop it explicitly — tearing the manager down with a
        /// push pending leaves a stale TimeData on the stack FOREVER, and the
        /// next match's non-stepped code reads the previous match's final sim
        /// time from it (seen 2026-08-16: 'tick 766/636' veil-init events).</summary>
        public bool HasPendingPush => _didPushTime;

        /// <summary>Pop the pending pushed time, if any. Called by
        /// LockstepFixedStep.Uninstall with the world the push went to.</summary>
        public void PopPendingPush(Unity.Entities.World world)
        {
            if (!_didPushTime || world == null || !world.IsCreated) return;
            world.PopTime();
            _didPushTime = false;
        }

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
        private static Unity.Entities.World _world;

        /// <summary>
        /// TRUE only when the driver is genuinely holding the sim group —
        /// i.e. the group's RateManager IS our manager. `Active` alone lied
        /// during desync #5: GameBootstrap's NetCode-defense sweep detached
        /// the manager from the group while these statics stood, so the flag
        /// said deterministic while the world ran frame-driven. Anything
        /// asserting lockstep health must check THIS, not Active.
        /// </summary>
        public static bool IsAttached =>
            Active && SimGroup != null && ReferenceEquals(SimGroup.RateManager, RateManager);

        public static void Install(Unity.Entities.World world, float timestep)
        {
            // Re-phase every periodic system before the first tick.
            //
            // SimulationSystemGroup has been running per-frame, with real
            // delta time, since the world was created — so every
            // `_acc += SystemAPI.Time.DeltaTime` scheduler in the sim carries
            // an arbitrary, machine-dependent phase by the time we get here.
            // From this point both peers advance in perfect lockstep, which
            // preserves that difference forever instead of correcting it.
            //
            // Desync 2026-08-21: StuckRedirectSystem fired its detour on tick
            // 3492 on one peer and 3495 on the other; the detour target is
            // computed from the unit's current position, so the two peers sent
            // a 6 m/s scout to points 0.6 m apart and the worlds forked. See
            // SimCadence.
            SimCadence.BeginMatch();

            // LOUD failures: a silent early-return here means the whole match
            // runs frame-driven while believing it is deterministic — the
            // worst possible failure mode, detectable only as a desync.
            if (world == null || !world.IsCreated)
            {
                UnityEngine.Debug.LogError(
                    "[LockstepFixedStep] Install FAILED: no ECS world. The match will run " +
                    "frame-driven and desync.");
                LockstepLog.NoteFixedStep(false, timestep);
                return;
            }
            SimGroup = world.GetExistingSystemManaged<SimulationSystemGroup>();
            if (SimGroup == null)
            {
                UnityEngine.Debug.LogError(
                    "[LockstepFixedStep] Install FAILED: no SimulationSystemGroup. The match " +
                    "will run frame-driven and desync.");
                LockstepLog.NoteFixedStep(false, timestep);
                return;
            }
            _world = world;
            RateManager = new LockstepFixedRateManager(timestep);
            SimGroup.RateManager = RateManager;
            Active = true;
            // The presentation layer interpolates views only while this is on —
            // a fixed-step world publishes transforms in discrete jumps.
            Core.Multiplayer.LockstepTiming.FixedStepActive = true;
            LockstepLog.NoteFixedStep(true, timestep);
            UnityEngine.Debug.Log(
                $"[LockstepFixedStep] Installed on '{world.Name}' at {1f / timestep:0} Hz.");
        }

        public static void Uninstall()
        {
            // Pop a pending pushed time BEFORE dropping the manager — a push
            // left on the stack outlives the match and every later non-stepped
            // read of World.Time returns this match's final sim time.
            RateManager?.PopPendingPush(_world);
            if (SimGroup != null) SimGroup.RateManager = null;
            SimGroup = null;
            RateManager = null;
            _world = null;
            Active = false;
            Core.Multiplayer.LockstepTiming.FixedStepActive = false;
        }

        /// <summary>
        /// Advance the deterministic simulation exactly one fixed step. Called
        /// from LockstepManager.ProcessTick after that tick's commands apply.
        /// No-op when the flag is off (the player loop drives the sim instead).
        /// </summary>
        public static void Step()
        {
            if (!Active || RateManager == null || SimGroup == null) return;
            // A detached manager means Update() below would run the group
            // UNGATED with wall dt — repair on the spot. This is the same
            // defense as the world-ready assertion, one level deeper.
            if (!ReferenceEquals(SimGroup.RateManager, RateManager))
            {
                UnityEngine.Debug.LogError(
                    "[LockstepFixedStep] Driver was detached from the sim group mid-match — " +
                    "re-attaching. Whatever cleared SimulationSystemGroup.RateManager is a bug.");
                SimGroup.RateManager = RateManager;
            }
            RateManager.RequestStep();
            SimGroup.Update();
        }
    }
}
