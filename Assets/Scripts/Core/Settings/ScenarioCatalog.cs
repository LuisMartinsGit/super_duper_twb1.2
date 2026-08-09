// ScenarioCatalog.cs
// Single source of truth for the list of playable test scenarios and the
// shared "launch a scenario" entry point.
//
// This list + launch logic used to be copy-pasted in every menu front-end
// (MainMenuUI / IMGUI, MenuToolkit / UI Toolkit) and drifted apart. All menus
// — including the Synty uGUI ScenarioListPopulator — now read from here, so a
// new scenario only has to be added in one place.
//
// Place in: Assets/Scripts/Core/Settings/ScenarioCatalog.cs

using TheWaningBorder.UI.Menus;

/// <summary>
/// The canonical, ordered list of test scenarios shown in the menus, plus the
/// single method that starts one.
/// </summary>
public static class ScenarioCatalog
{
    /// <summary>Display label + scenario type, in menu order.</summary>
    public static readonly (string label, ScenarioType type)[] All =
    {
        ("Large Melee Battle (6v6)", ScenarioType.LargeMelee),
        ("Large Ranged Battle (6v6)", ScenarioType.LargeRanged),
        ("Large Mixed Battle (6v6)", ScenarioType.LargeMixed),
        ("Healer Test", ScenarioType.HealerTest),
        ("Four-Way Cultures (4 armies)", ScenarioType.FourWayCultures),
        ("Full Army (Archers + Swords + Siege)", ScenarioType.FullArmy),
        ("Wall Siege (Walls vs Siege)", ScenarioType.WallSiege),
        ("Spell Showcase (all spells, flat map)", ScenarioType.SpellShowcase),
        ("Sect Showcase (12 Sect Abilities)", ScenarioType.SectShowcase),
        ("Building Showcase (every culture)", ScenarioType.BuildingShowcase),
        ("The Border Combat Test", ScenarioType.BorderCombatTest),
        ("Patrol Defense (6 Veilstingers vs Wave)", ScenarioType.PatrolDefense),
        ("Alanthor vs Veilstone Horde (6 batt. vs 50)", ScenarioType.AlanthorVsBorder),
        ("Phase 1 Nav Test (1 unit flat grid)", ScenarioType.Phase1Test),
        ("Phase 2 Nav Test (300 swords flow + steering)", ScenarioType.Phase2Test),
        ("Phase 3 Nav Test (1 unit 512x512 SW->NE)", ScenarioType.Phase3Test),
        ("Phase 4 Nav Test (50 swords + wall place/destroy)", ScenarioType.Phase4Test),
        ("Phase 5 Nav Test (wall ring + 10 Blue + 10 Red)", ScenarioType.Phase5Test),
        ("Phase 7 Nav Test (determinism replay 100 units)", ScenarioType.Phase7Test),
        ("Wall Climb Test (stairs + rampart garrison)", ScenarioType.WallClimbTest),
        ("Longbowman Showcase (idle/patrol/shoot/spawn)", ScenarioType.LongbowmanShowcase),
        ("Longbowman Battle (30v30, 2x 3x5 blocks)", ScenarioType.LongbowmanBattle),
        ("Building Damage Test (Alanthor row, 5%/s)", ScenarioType.BuildingDamageTest),
        ("Building Damage Showcase (all cultures, 5%/s)", ScenarioType.BuildingDamageShowcase),
        ("Guild Defense (fully-upgraded Guild vs swarm)", ScenarioType.GuildDefenseTest),
        ("Hut Evolution (5s self-build, 3s upgrades)", ScenarioType.HutEvolution),
    };

    /// <summary>
    /// The gameplay scene for a scenario. Each scenario now owns a dedicated
    /// scene under Assets/GameData/Scenes/Scenarios/ (a copy of ScenarioMap
    /// with its own TerrainData) named "Scenario_&lt;EnumName&gt;", so scenarios
    /// are independent of each other and of the heavy YielLymwérra map.
    /// MapRegistry.IsGameplayScene recognises these so GameBootstrap runs.
    /// </summary>
    public static string SceneFor(ScenarioType scenario) => "Scenario_" + scenario;

    /// <summary>
    /// Configure GameSettings for a single-player scenario match and load that
    /// scenario's own scene.
    /// </summary>
    public static void Launch(ScenarioType scenario)
    {
        GameSettings.Mode = GameMode.Scenario;
        GameSettings.ActiveScenario = scenario;
        GameSettings.IsMultiplayer = false;
        GameSettings.NetworkRole = NetworkRole.None;
        GameSettings.TotalPlayers = 2;
        GameSettings.LocalPlayerFaction = Faction.Blue;
        GameSettings.FogOfWarEnabled = false;

        LoadingScreen.Show(SceneFor(scenario));
    }
}
