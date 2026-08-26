// ECS components for the Muster Yard (War sect building).
// Global namespace per project convention.

using Unity.Entities;

/// <summary>Marker for the Muster Yard (Sect of War). Counted by the per-faction build cap of 5.</summary>
public struct MusterYardTag : IComponentData { }
