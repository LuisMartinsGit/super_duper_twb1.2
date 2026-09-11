// MatchSimGate.cs
// The simulation does not run until the match has started, and the match
// clock starts at zero — in EVERY mode.
//
// WHY (2026-09-11 directive: "Match (and clock) must only start after
// everything is loaded and the loading screen is disposed", and "in some
// instances the clock references the time the game has been running").
//
// Under lockstep, LockstepFixedRateManager already does both: nothing steps
// before tick 0 and ElapsedTime counts from 0. Every other mode — skirmish,
// campaign, scenarios, the headless batches — let SimulationSystemGroup run
// frame-driven from the moment the world existed: through the loading
// screen (only held by timeScale = 0, which the fixed-step comment in
// SimCadence.cs shows is not a gate at all) and on a clock that started when
// the WORLD was created. Every system that keys behaviour on
// SystemAPI.Time.ElapsedTime (wave timers, escalation curves, aura ticks)
// therefore saw "seconds since the process started" in single-player and
// "seconds since tick 0" in multiplayer.
//
// This rate manager is installed on SimulationSystemGroup the moment the
// match world is created (GameBootstrap.RecreateECSWorld). It refuses to
// run the group until the map is populated and the loading overlay is gone,
// and from that moment pushes its own TimeData — elapsed from 0, scaled
// delta time — exactly the way the lockstep driver does. LockstepFixedStep
// .Install replaces it for deterministic matches (popping any pending push).
//
// One update per frame: IRateManager.ShouldGroupUpdate is polled in a loop
// until it returns false, so a plain "return Open" would spin forever.

using Unity.Core;
using Unity.Entities;
using Unity.Mathematics;
// TheWaningBorder.World is a namespace in scope here; the codebase's usual alias.
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Core
{
    public sealed class MatchSimGate : IRateManager
    {
        private float _timestep;
        private bool _didPushTime;
        private double _elapsed;
        private int _lastFrame = -1;

        public float Timestep { get => _timestep; set => _timestep = value; }

        /// <summary>Seconds of match simulated so far (0 until the gate opens).</summary>
        public double Elapsed => _elapsed;

        /// <summary>The match may simulate: the map exists and the player can
        /// see it. Both flags are published by their owners (SpawnDelayHelper /
        /// GameBootstrap, LoadingScreen).</summary>
        public static bool Open =>
            MatchLifecycle.MapPopulated && !PresentationState.LoadingOverlayVisible;

        public bool ShouldGroupUpdate(ComponentSystemGroup group)
        {
            if (_didPushTime)
            {
                group.World.PopTime();
                _didPushTime = false;
            }

            if (!Open) return false;

            int frame = UnityEngine.Time.frameCount;
            if (frame == _lastFrame) return false;   // already ran this frame
            _lastFrame = frame;

            float dt = math.min(UnityEngine.Time.deltaTime, group.World.MaximumDeltaTime);
            _elapsed += dt;
            group.World.PushTime(new TimeData(_elapsed, dt));
            _didPushTime = true;
            return true;
        }

        /// <summary>Undo a push left on the world's time stack — needed before
        /// another rate manager takes the group over, or the world is disposed.</summary>
        public void PopPendingPush(EntityWorld world)
        {
            if (!_didPushTime || world == null || !world.IsCreated) return;
            world.PopTime();
            _didPushTime = false;
        }

        // ── Install / uninstall ────────────────────────────────────────

        public static MatchSimGate Current { get; private set; }
        private static EntityWorld _world;

        public static void Install(EntityWorld world)
        {
            if (world == null || !world.IsCreated) return;
            var group = world.GetExistingSystemManaged<SimulationSystemGroup>();
            if (group == null) return;
            Current = new MatchSimGate();
            _world = world;
            group.RateManager = Current;
        }

        /// <summary>Detach without touching the group's RateManager if someone
        /// else (the lockstep driver) has already replaced it.</summary>
        public static void Uninstall()
        {
            if (Current == null) return;
            Current.PopPendingPush(_world);
            if (_world != null && _world.IsCreated)
            {
                var group = _world.GetExistingSystemManaged<SimulationSystemGroup>();
                if (group != null && ReferenceEquals(group.RateManager, Current))
                    group.RateManager = null;
            }
            Current = null;
            _world = null;
        }
    }
}
