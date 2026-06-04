// ShrineOfRidanComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Shrine of Ahridan — choice building that trains litharchs and grants +1 RP.</summary>
public struct ShrineTag : IComponentData { }

/// <summary>Tracks whether the shrine has already granted its one-time +1 RP bonus.</summary>
public struct ShrineRPGranted : IComponentData { public byte Granted; }
