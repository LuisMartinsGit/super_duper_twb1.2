// ScenarioDefinition.cs
// Data-driven description of one scenario. One of these lives next to each
// scenario's scene + thumbnail:
//
//   Assets/GameData/Scenarios/<Name>/<Name>.unity   (the scene)
//   Assets/GameData/Scenarios/<Name>/<Name>.jpg      (thumbnail)
//   Assets/GameData/Scenarios/<Name>/<Name>.asset     (this ScriptableObject)
//
// The scenario browser reads these (via ScenarioLibrary) to fill the selection
// list and preview pane, and loads SceneName when the player hits Start.

using UnityEngine;

[CreateAssetMenu(fileName = "Scenario", menuName = "TWB/Scenario Definition", order = 0)]
public class ScenarioDefinition : ScriptableObject
{
    [Tooltip("Name shown in the selection list and preview header.")]
    public string DisplayName;

    [TextArea(4, 12)]
    [Tooltip("Shown in the preview pane.")]
    public string Description;

    [Tooltip("Preview image — the <Name>.jpg beside this asset.")]
    public Sprite Thumbnail;

    [Tooltip("Scene loaded when the scenario starts. Must be in Build Settings " +
             "(no path, no extension), e.g. \"ScenarioA\".")]
    public string SceneName;

    [Tooltip("Optional: legacy spawn set used by ScenarioSetup when the scene " +
             "relies on code-driven spawning rather than baked-in content.")]
    public ScenarioType LegacySpawnType = ScenarioType.LargeMelee;

    [Tooltip("If true, Start sets GameMode.Scenario + LegacySpawnType so " +
             "ScenarioSetup spawns. If false, the scene is loaded as-is.")]
    public bool UseLegacySpawns = true;
}
