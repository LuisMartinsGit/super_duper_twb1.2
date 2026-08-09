// ScenarioLibrary.cs
// Runtime-loadable registry of every ScenarioDefinition. The definition assets
// live under Assets/GameData/Scenarios/<Name>/ (outside Resources), so this
// library — which DOES live in Resources — references them and is what the
// scenario browser loads at runtime (Resources.Load<ScenarioLibrary>).
//
// Rebuild it after adding/removing scenarios via
//   Tools ▸ TWB ▸ Scenarios ▸ Rebuild Scenario Library.

using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ScenarioLibrary", menuName = "TWB/Scenario Library", order = 1)]
public class ScenarioLibrary : ScriptableObject
{
    [Tooltip("All scenarios, in list order. Rebuilt from Assets/GameData/Scenarios/ " +
             "by the editor tool.")]
    public List<ScenarioDefinition> Scenarios = new List<ScenarioDefinition>();

    /// <summary>Resources path (without extension) the browser loads.</summary>
    public const string ResourcePath = "ScenarioLibrary";
}
