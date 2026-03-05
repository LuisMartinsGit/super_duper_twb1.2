// FeraldisComponents.cs
// ECS components for the Feraldis battle economy system
// Location: Assets/Scripts/Core/Components/FeraldisComponents.cs

using Unity.Entities;

// ==================== Feraldis Economy Components ====================

/// <summary>
/// Marker tag for entities belonging to a Feraldis-culture faction.
/// Reserved for future use — efficient query filtering for Feraldis-specific behaviors.
/// Currently the PillageSystem uses FactionColors.GetFactionCulture() instead.
/// </summary>
public struct PillageBonusTag : IComponentData { }
