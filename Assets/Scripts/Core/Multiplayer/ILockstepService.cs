// ILockstepService.cs
// Interface for lockstep service - allows Core to use lockstep without circular dependency
// Location: Assets/Scripts/Core/Multiplayer/ILockstepService.cs

namespace TheWaningBorder.Core.Multiplayer
{
    /// <summary>
    /// Interface for lockstep service.
    /// Implemented by LockstepManager in the Multiplayer assembly.
    /// This allows CommandRouter (in Core) to queue commands without
    /// directly referencing the Multiplayer assembly.
    /// </summary>
    public interface ILockstepService
    {
        /// <summary>
        /// Whether the lockstep simulation is currently running.
        /// </summary>
        bool IsSimulationRunning { get; }
        
        /// <summary>
        /// Whether this instance is the host (server).
        /// </summary>
        bool IsHost { get; }
        
        /// <summary>
        /// Queue a command for lockstep synchronization.
        /// </summary>
        void QueueCommand(LockstepCommand cmd);
    }

    /// <summary>
    /// Static service locator for lockstep.
    /// Allows Core systems to access lockstep without direct assembly reference.
    /// </summary>
    public static class LockstepServiceLocator
    {
        private static ILockstepService _instance;

        /// <summary>
        /// Get the current lockstep service instance.
        /// Returns null if no lockstep manager is active.
        /// </summary>
        public static ILockstepService Instance => _instance;

        /// <summary>
        /// Register a lockstep service instance.
        /// Called by LockstepManager on Awake.
        /// </summary>
        public static void Register(ILockstepService service)
        {
            _instance = service;
        }

        /// <summary>
        /// Unregister the lockstep service.
        /// Called by LockstepManager on OnDestroy.
        /// </summary>
        public static void Unregister(ILockstepService service)
        {
            if (_instance == service)
                _instance = null;
        }

        /// <summary>
        /// Check if lockstep is active and running.
        /// </summary>
        public static bool IsActive => _instance != null && _instance.IsSimulationRunning;
    }
}