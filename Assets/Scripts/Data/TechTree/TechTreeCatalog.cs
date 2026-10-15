// TechTreeCatalog.cs
// Single ScriptableObject that aggregates every UnitDefSO + BuildingDefSO.
// Part of: Data/TechTree/
//
// TechCatalog loads this one asset from Resources/TechTreeCatalog.asset. It is
// the ONLY source of game data: every UnitDefSO, BuildingDefSO, TechDefSO and
// BuildingLevelDefSO the game uses must be referenced here (there is no JSON
// fallback; TechTree.json was deleted 2026-10-03). The SO assets themselves
// live beside their entity under Assets/GameData/TechTree/ (a tech in its
// research host's Research/ folder), outside any magic Resources/ folder.
// Sect god powers are data on SectDefinition SOs, not here.

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

        /// <summary>Every cultured building level (BuildingLevelDefSO) — the
        /// assets live in the culture's building folders.</summary>
        public List<BuildingLevelDefSO> buildingLevels = new List<BuildingLevelDefSO>();

        /// <summary>
        /// Ids the player build's EDITION strip removed from the lists above
        /// (Editor/EditionContent: the demo drops every Feraldis and Runai def).
        /// Always empty in the project: the strip fills it only for the length
        /// of a build and restores the asset afterwards. TechCatalog prunes
        /// these ids out of every trains[] / research[] / prerequisite at load,
        /// so an Age 0 building that lists a Feraldis unit shows no dead button
        /// and the validator does not report a hole the strip made on purpose.
        /// </summary>
        public List<string> editionStripped = new List<string>();

        /// <summary>True if this catalog actually carries data (used to decide SO-vs-JSON mode).</summary>
        public bool HasEntries => (units != null && units.Count > 0) ||
                                  (buildings != null && buildings.Count > 0);

        /// <summary>True if the technology SOs have been generated. Kept separate from
        /// <see cref="HasEntries"/> so units/buildings can be SO-backed while
        /// technologies still fall back to JSON (and vice versa).</summary>
        public bool HasTechnologies => technologies != null && technologies.Count > 0;
    }
}
