// RTSInput.cs
// Static accessor class for input state
// Location: Assets/Scripts/Input/RTSInput.cs

using System.Collections.Generic;
using System.Linq;
using Unity.Entities;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Static accessor for RTS input state.
    /// Provides a simplified interface for other systems to query selection and hover state.
    /// Bridges RTSInputManager instance state to static accessors.
    /// </summary>
    public static class RTSInput
    {
        // ═══════════════════════════════════════════════════════════════════════
        // SELECTION STATE
        // ═══════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Currently selected entities.
        /// Delegates to SelectionSystem for actual selection management.
        /// </summary>
        public static IReadOnlyList<Entity> CurrentSelection => SelectionSystem.CurrentSelection;
        
        /// <summary>
        /// Currently hovered entity (for UI highlighting).
        /// </summary>
        public static Entity HoveredEntity { get; set; } = Entity.Null;
        
        /// <summary>
        /// Whether any entities are currently selected.
        /// </summary>
        public static bool HasSelection => CurrentSelection != null && CurrentSelection.Count > 0;
        
        /// <summary>
        /// Number of currently selected entities.
        /// </summary>
        public static int SelectionCount => CurrentSelection?.Count ?? 0;

        // ═══════════════════════════════════════════════════════════════════════
        // INPUT STATE
        // ═══════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Whether input is currently blocked (e.g., by UI).
        /// </summary>
        public static bool InputBlocked { get; set; } = false;
        
        /// <summary>
        /// Whether the player is in building placement mode.
        /// </summary>
        public static bool IsPlacingBuilding { get; set; } = false;
        
        /// <summary>
        /// Whether clicks should be suppressed this frame.
        /// </summary>
        public static bool SuppressClicksThisFrame { get; set; } = false;

        // ═══════════════════════════════════════════════════════════════════════
        // HELPER METHODS
        // ═══════════════════════════════════════════════════════════════════════
        
        /// <summary>
        /// Check if an entity is currently selected.
        /// </summary>
        public static bool IsSelected(Entity entity)
        {
            return CurrentSelection != null && CurrentSelection.Contains(entity);
        }
        
        /// <summary>
        /// Check if an entity is currently hovered.
        /// </summary>
        public static bool IsHovered(Entity entity)
        {
            return entity != Entity.Null && entity == HoveredEntity;
        }
        
        /// <summary>
        /// Clear the current hover state.
        /// </summary>
        public static void ClearHover()
        {
            HoveredEntity = Entity.Null;
        }
        
        /// <summary>
        /// Set the hovered entity.
        /// </summary>
        public static void SetHovered(Entity entity)
        {
            HoveredEntity = entity;
        }
    }
}