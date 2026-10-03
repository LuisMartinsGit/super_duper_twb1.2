// TowerComponents.cs
// Auto-organized by tools/split_components.py. All types are in the
// global namespace (single assembly), so location is organizational only.

using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

/// <summary>Alanthor ranged defensive tower. Its garrison (Tower.asset
/// garrisonSlots) is a WallGarrisonSlot buffer — see WallGarrison.</summary>
public struct WatchTowerTag : IComponentData { }
