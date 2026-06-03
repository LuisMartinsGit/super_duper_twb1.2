// TechTreeCatalog.cs
// Single ScriptableObject that aggregates every UnitDefSO + BuildingDefSO.
// Part of: Data/TechTree/
//
// TechTreeDB references one catalog asset (Assets/GameData/TechTree/TechTreeCatalog.asset).
// When assigned, units/buildings are loaded from these SOs instead of TechTree.json.
// Technologies and sects continue to load from JSON (out of scope for SO conversion).
// The TechTreeSOGenerator editor tool creates/refreshes this catalog from the JSON.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Data
{
    [CreateAssetMenu(fileName = "TechTreeCatalog", menuName = "Waning Border/Tech Tree Catalog", order = 2)]
    public class TechTreeCatalog : ScriptableObject
    {
        [Tooltip("Every unit stat asset. Edit a unit's HP/damage/etc. on its asset to tune on the fly.")]
        public List<UnitDefSO> units = new List<UnitDefSO>();

        [Tooltip("Every building stat asset.")]
        public List<BuildingDefSO> buildings = new List<BuildingDefSO>();

        /// <summary>True if this catalog actually carries data (used to decide SO-vs-JSON mode).</summary>
        public bool HasEntries => (units != null && units.Count > 0) ||
                                  (buildings != null && buildings.Count > 0);
    }
}
