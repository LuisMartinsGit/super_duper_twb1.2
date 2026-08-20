// File: Assets/GameData/TechTree/Buildings/Sects/Reliquary/ReliquaryComponents.cs
// ECS components for The Reliquary — the Antiquity sect's unique building
// (its Building lever). Split out of AntiquityComponents.cs 2026-08-12 so the
// building's data sits with the building. Global namespace per convention.

using Unity.Entities;

/// <summary>Marker for The Reliquary (Antiquity building lever).</summary>
public struct ReliquaryTag : IComponentData { }

/// <summary>
/// Per-Reliquary ability cooldowns. Ability availability is level-gated
/// (Building lever): Lv I = Scry only; Lv II = all three; Lv III = base
/// cooldowns -30% and garrison effects doubled. A Lorekeeper standing
/// within garrison range further multiplies cooldown recovery.
/// </summary>
public struct ReliquaryState : IComponentData
{
    public float ScryCooldown;      // remaining seconds (0 = ready)
    public float LockoutCooldown;
    public float VisionCooldown;
}
