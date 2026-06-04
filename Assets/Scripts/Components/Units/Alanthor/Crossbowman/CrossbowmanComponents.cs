// CrossbowmanComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>
/// Marker tag for Crossbowman units (Era 1 Archery Range L2 tier — high damage,
/// short range, slow rate of fire). Reuses ArcherState for the ranged combat
/// state machine; the tag distinguishes them from baseline Archers for
/// selection / extractor / population queries.
/// </summary>
public struct CrossbowmanTag : IComponentData { }
