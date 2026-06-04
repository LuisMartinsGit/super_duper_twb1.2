// LongbowmanComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Marker tag for Longbowman units (Era 1 Archery Range L3 tier — very long
/// range and damage, very slow rate of fire). Reuses ArcherState for the
/// ranged combat state machine.
/// </summary>
public struct LongbowmanTag : IComponentData { }
