// File: Assets/Scripts/Core/Components/BuildingUpgradeComponents.cs
// Components for the per-culture building upgrade system.
//
// Player flow: pick a culture (age-up), then click an Upgrade button on
// the building portrait. Each upgrade level costs more, takes longer, and
// applies cumulative stat bonuses (HP, training speed, attack rate) plus
// a few per-building specials (Hall multi-target, Barracks gains attack
// at lvl 3, House gains pop). Visual swap to the matching prefab is
// handled in a follow-up PR.
//
// Levels run 0..3. 0 = uncultured (base stats). 1..3 = cultured tiers.
// Components live in the global namespace to match the rest of
// Core/Components/*.cs (BuildingComponents, CoreComponents, etc.).

using Unity.Entities;

/// <summary>
/// Marker — this building type is upgradeable. Stamped at creation
/// time on Hall, Barracks, Hut. Buildings without this tag never show
/// an upgrade button and are skipped by the upgrade system.
/// </summary>
public struct BuildingUpgradeable : IComponentData { }

/// <summary>
/// Current upgrade level + captured base stats so re-applying a level
/// always recomputes from base (idempotent — no double-bumping if a
/// frame races or save/load replays the level).
///
/// Lazy-stamped by UpgradeBuildingCommandHelper.Execute on first
/// upgrade attempt; absent on uncultured (lvl 0) buildings until then.
/// </summary>
public struct BuildingUpgradeState : IComponentData
{
    /// <summary>Current applied level (0..3). Bumped after BuildingUpgrading completes.</summary>
    public byte Level;

    /// <summary>Base Health.Max captured at first upgrade. Used to recompute scaled HP.</summary>
    public int BaseHpMax;

    /// <summary>
    /// Base BuildingRangedAttack.Cooldown captured at first upgrade. 0 if the
    /// building had no ranged attack at base (e.g. Barracks — gains attack
    /// at lvl 3, so the system stamps a fresh cooldown at that point).
    /// </summary>
    public float BaseAttackCooldown;

    /// <summary>
    /// Base PopulationProvider.Amount captured at first upgrade. 0 if the
    /// building wasn't a pop provider (e.g. Barracks). Used by Hut's
    /// +5 pop per level rule.
    /// </summary>
    public int BasePopulationProvider;
}

/// <summary>
/// Active upgrade in progress. Functions like UnderConstruction —
/// blocks training and attack while ticking, and the upgrade system
/// removes it when Progress >= Total and applies the level.
///
/// Time scales per level — higher levels cost more elapsed time.
/// </summary>
public struct BuildingUpgrading : IComponentData
{
    public float Progress;
    public float Total;
    public byte TargetLevel;
}

/// <summary>
/// Result codes from UpgradeBuildingCommandHelper.Execute. Surfaced
/// in AILogger output and (eventually) UI feedback. Lets the UI tell
/// the player WHY a click didn't take.
/// </summary>
public enum UpgradeBuildingResult : byte
{
    Ok = 0,
    NotUpgradeable,
    AlreadyMaxLevel,
    NoCulture,
    AlreadyUpgrading,
    UnderConstruction,
    CannotAfford,
}
