// TempleLevelConfig.cs
// Configuration for temple level-up costs, era mapping, and religion point grants
// Location: Assets/Scripts/Core/Settings/TempleLevelConfig.cs

using TheWaningBorder.Core;

/// <summary>
/// Static configuration for the Temple of Ridan level-up progression.
/// Temple levels drive era advancement (Era 3-5) and grant Religion Points.
///
/// Level 1 (on build):  Era 2 prerequisite, grants 2 RP
/// Level 2 (upgrade):   Era 3, costs 500S+200I+150C, grants 3 RP
/// Level 3 (upgrade):   Era 4, costs 800S+350I+250C, grants 3 RP
/// Level 4 (upgrade):   Era 5, costs 1200S+500I+400C, grants 4 RP
/// </summary>
public static class TempleLevelConfig
{
    /// <summary>Maximum temple level (4 = Era 5)</summary>
    public const int MaxLevel = 4;

    /// <summary>
    /// Get the resource cost to upgrade from the given level to level+1.
    /// Returns zero cost if already at max level.
    /// </summary>
    public static Cost GetUpgradeCost(int currentLevel) => currentLevel switch
    {
        1 => Cost.Of(supplies: 500, iron: 200, crystal: 150),
        2 => Cost.Of(supplies: 800, iron: 350, crystal: 250),
        3 => Cost.Of(supplies: 1200, iron: 500, crystal: 400),
        _ => default
    };

    /// <summary>
    /// Get the era that corresponds to a temple level.
    /// </summary>
    public static int GetEraForLevel(int level) => level switch
    {
        1 => 2,
        2 => 3,
        3 => 4,
        4 => 5,
        _ => 1
    };

    /// <summary>
    /// Get the Religion Points granted when reaching the given temple level.
    /// Level 1 RP is granted on Era 2 advance (culture choice).
    /// </summary>
    public static int GetRPGranted(int level) => level switch
    {
        1 => 2,
        2 => 3,
        3 => 3,
        4 => 4,
        _ => 0
    };

    /// <summary>
    /// Bonus RP granted when a Shrine (ChapelSmall) is built while a temple exists.
    /// </summary>
    public const int ShrineBonus = 1;
}
