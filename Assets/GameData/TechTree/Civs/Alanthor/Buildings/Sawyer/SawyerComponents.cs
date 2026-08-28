// SawyerComponents.cs
// The Sawyer's marker. Lives beside its factory, per the co-location rule in
// CLAUDE.md ("a component file lives with the system it was split out of").

using Unity.Entities;

/// <summary>
/// Marks a Sawyer. Read by TerritoryIncomeSystem, which multiplies the forest
/// supply of the territory the Sawyer stands in.
/// </summary>
public struct SawyerTag : IComponentData { }
