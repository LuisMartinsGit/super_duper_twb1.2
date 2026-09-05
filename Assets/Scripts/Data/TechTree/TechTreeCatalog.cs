// TechTreeCatalog.cs
// Single ScriptableObject that aggregates every UnitDefSO + BuildingDefSO.
// Part of: Data/TechTree/
//
// TechTreeDB references one catalog asset (Assets/GameData/TechTree/TechTreeCatalog.asset).
// When assigned, units/buildings are loaded from these SOs instead of TechTree.json.
// Technologies are SO-backed too: their assets live in the folder of the building
// that researches them (Buildings/<Culture>/<Building>/Research/<Tech>.asset), and
// this catalog holds the references so they are reachable at runtime without
// living under a magic Resources/ folder. Sects still load from JSON.
// The TechTreeSOGenerator editor tool creates/refreshes this catalog from the JSON.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Data
{
    [CreateAssetMenu(fileName = "TechTreeCatalog", menuName = "Waning Border/Tech Tree Catalog", order = 2)]
    public class TechTreeCatalog : ScriptableObject
    {
        public List<UnitDefSO> units = new List<UnitDefSO>();

        public List<BuildingDefSO> buildings = new List<BuildingDefSO>();

        public List<TechDefSO> technologies = new List<TechDefSO>();

        /// <summary>True if this catalog actually carries data (used to decide SO-vs-JSON mode).</summary>
        public bool HasEntries => (units != null && units.Count > 0) ||
                                  (buildings != null && buildings.Count > 0);

        /// <summary>True if the technology SOs have been generated. Kept separate from
        /// <see cref="HasEntries"/> so units/buildings can be SO-backed while
        /// technologies still fall back to JSON (and vice versa).</summary>
        public bool HasTechnologies => technologies != null && technologies.Count > 0;
    }
}
