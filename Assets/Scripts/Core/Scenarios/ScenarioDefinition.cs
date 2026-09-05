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
    public string DisplayName;

    [TextArea(4, 12)]
    public string Description;

    public Sprite Thumbnail;

    public string SceneName;

    public ScenarioType LegacySpawnType = ScenarioType.LargeMelee;

    public bool UseLegacySpawns = true;
}
