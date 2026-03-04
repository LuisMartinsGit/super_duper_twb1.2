// BuildingDef.cs
// Building definition data structure parsed from TechTree JSON
// Part of: Data/TechTree/Definitions/

using System;

namespace TheWaningBorder.Data
{
    /// <summary>
    /// Defines a building type's base stats, training capabilities, and research options.
    /// Loaded from TechTree.json at runtime.
    /// </summary>
    [Serializable]
    public class BuildingDef
    {
        // ==================== Identity ====================
        public string id;
        public string name;
        public string role;             // e.g., "production", "military", "economic", "defensive"
        
        // ==================== Core Stats ====================
        public float hp;
        public string armorType;        // e.g., "structure_human", "structure_feraldis"
        public DefenseBlock defense;
        
        // ==================== Spatial ====================
        public float radius;            // building footprint radius
        public float lineOfSight;       // vision range
        
        // ==================== Capabilities ====================
        public string[] trains;         // unit IDs this building can train
        public string[] research;       // technology IDs this building can research
        
        // ==================== Economy ====================
        public CostBlock cost;
        
        // ==================== Helpers ====================
        
        /// <summary>
        /// Returns true if this building can train units.
        /// </summary>
        public bool CanTrain => trains != null && trains.Length > 0;
        
        /// <summary>
        /// Returns true if this building can research technologies.
        /// </summary>
        public bool CanResearch => research != null && research.Length > 0;
        
        /// <summary>
        /// Returns true if this is a military production building.
        /// </summary>
        public bool IsMilitaryProduction => string.Equals(role, "military", StringComparison.OrdinalIgnoreCase) && CanTrain;
        
        /// <summary>
        /// Returns true if this is an economic building.
        /// </summary>
        public bool IsEconomic => string.Equals(role, "economic", StringComparison.OrdinalIgnoreCase);
        
        /// <summary>
        /// Returns true if this is a defensive structure.
        /// </summary>
        public bool IsDefensive => string.Equals(role, "defensive", StringComparison.OrdinalIgnoreCase);
        
        /// <summary>
        /// Check if this building can train a specific unit type.
        /// </summary>
        public bool CanTrainUnit(string unitId)
        {
            if (trains == null) return false;
            foreach (var id in trains)
            {
                if (string.Equals(id, unitId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        
        /// <summary>
        /// Check if this building can research a specific technology.
        /// </summary>
        public bool CanResearchTech(string techId)
        {
            if (research == null) return false;
            foreach (var id in research)
            {
                if (string.Equals(id, techId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}