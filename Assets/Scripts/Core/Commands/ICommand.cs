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

    // Fix #237: IGameCommand / IUndoableCommand / CommandResult were defined
    // but never implemented or used — the actual command system uses static
    // helper classes (MoveCommandHelper.Execute, etc.). Removed rather than
    // leaving dead interfaces that imply a pattern the codebase doesn't
    // follow. Resurrect fresh if the static-helper style is ever revisited.

    // ═══════════════════════════════════════════════════════════════
    // HELPER MARKER COMPONENTS
    // ═══════════════════════════════════════════════════════════════
    // NOTE: All ECS IComponentData structs are defined in the GLOBAL namespace
    // (Core/Components/CoreComponents.cs, UnitComponents.cs, etc.)
    // to ensure Unity's ECS source generator can resolve them in generated code.
    // Do NOT define IComponentData structs in namespaced code.
}