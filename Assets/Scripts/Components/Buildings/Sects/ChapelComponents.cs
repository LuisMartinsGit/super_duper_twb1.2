// File: Assets/GameData/TechTree/Buildings/Sects/Chapel/ChapelComponents.cs
// ECS components for the sect chapels and, set-level, the marker shared by
// every sect-unique building. Split out of Buildings/BuildingComponents.cs
// 2026-08-12 when sect buildings got their own branch. Global namespace.

using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Chapel building tag — generic across all 12 sects.
/// SectId identifies which sect this chapel belongs to (e.g., "Sect_Antiquity"
/// in the task-063 roster). Chapels are the adoption marker + per-sect lever
/// upgrade host. TODO(task-063 phase 2): kept for reuse — Phase 2 chapel
/// creators will tag chapels with this and call SectAdoption.OnChapelCompleted.
/// </summary>
public struct ChapelTag : IComponentData
{
    public FixedString64Bytes SectId;
}

/// <summary>Unique sect-specific building.</summary>
public struct SectUniqueBuildingTag : IComponentData { }
