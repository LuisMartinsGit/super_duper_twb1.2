// Age0BuildingComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Marker for the mutually exclusive landmarks (Vault, Keep). Build limit: 1.</summary>
public struct ChoiceBuildingTag : IComponentData { }
