// ICommand.cs
// Base interface and shared types for the command system
// Location: Assets/Scripts/Core/Commands/ICommand.cs

using Unity.Entities;
using Unity.Mathematics;

namespace TheWaningBorder.Core.Commands
{
    // ═══════════════════════════════════════════════════════════════
    // COMMAND SOURCE IDENTIFICATION
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Identifies where a command originated from.
    /// Used for determining lockstep routing behavior.
    /// </summary>
    public enum CommandSource
    {
        /// <summary>Local human player (RTSInput, UI clicks)</summary>
        LocalPlayer,
        
        /// <summary>Remote human player (received via network)</summary>
        RemotePlayer,
        
        /// <summary>AI system (AITacticalManager, etc.)</summary>
        AI,
        
        /// <summary>Internal system (auto-targeting, spawning, etc.)</summary>
        System
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMAND INTERFACE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Interface for command pattern implementation.
    /// Commands encapsulate all information needed to execute an action.
    /// </summary>
    public interface IGameCommand
    {
        /// <summary>The entity this command targets</summary>
        Entity TargetEntity { get; }
        
        /// <summary>Where the command originated from</summary>
        CommandSource Source { get; }
        
        /// <summary>Execute the command immediately</summary>
        void Execute(EntityManager em);
        
        /// <summary>Check if the command can be executed</summary>
        bool CanExecute(EntityManager em);
    }

    /// <summary>
    /// Interface for commands that can be undone (future feature).
    /// </summary>
    public interface IUndoableCommand : IGameCommand
    {
        /// <summary>Undo the command's effects</summary>
        void Undo(EntityManager em);
    }

    // ═══════════════════════════════════════════════════════════════
    // COMMAND RESULT
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Result of a command execution attempt
    /// </summary>
    public enum CommandResult
    {
        Success,
        EntityNotFound,
        TargetNotFound,
        InvalidState,
        InsufficientResources,
        NoPermission,
        QueuedForLockstep
    }

    // ═══════════════════════════════════════════════════════════════
    // HELPER MARKER COMPONENTS
    // ═══════════════════════════════════════════════════════════════
    // NOTE: All ECS IComponentData structs are defined in the GLOBAL namespace
    // (Core/Components/CoreComponents.cs, UnitComponents.cs, etc.)
    // to ensure Unity's ECS source generator can resolve them in generated code.
    // Do NOT define IComponentData structs in namespaced code.
}