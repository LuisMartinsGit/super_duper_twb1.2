// TechnologyDef.cs
// Technology definition and supporting data structures parsed from TechTree JSON
// Part of: Data/TechTree/Definitions/

using System;

namespace TheWaningBorder.Data
{
    /// <summary>
    /// Defines a technology that can be researched to unlock upgrades or abilities.
    /// Loaded from TechTree.json at runtime.
    /// </summary>
    [Serializable]
    public class TechnologyDef
    {
        // ==================== Identity ====================
        public string id;
        public string name;
        public string role;             // e.g., "upgrade", "unlock", "passive"
        
        // ==================== Description ====================
        public string effect;           // what this technology does
        public string desc;             // flavor/lore description
        
        // ==================== Research Requirements ====================
        public float researchTime;      // seconds to research
        public string researchAt;       // building ID where this is researched
        
        // ==================== Economy ====================
        public CostBlock cost;
        
        // ==================== Helpers ====================
        
        /// <summary>
        /// Returns true if this technology has a cost.
        /// </summary>
        public bool HasCost => cost != null && !cost.IsZero;
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // SUPPORTING DATA STRUCTURES
    // ═══════════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Defines a sect (religious/magical faction variant).
    /// Used for Era progression and special abilities.
    /// </summary>
    [Serializable]
    public class SectDef
    {
        public string id;
        public string order;            // which order this sect belongs to
        public string affinity;         // magical/elemental affinity
    }
    
    /// <summary>
    /// Defense values against different damage types.
    /// Higher values reduce incoming damage of that type.
    /// </summary>
    [Serializable]
    public class DefenseBlock
    {
        public int melee;
        public int ranged;
        public int siege;
        public int magic;
        
        /// <summary>
        /// Get defense value for a specific damage type.
        /// </summary>
        public int GetDefense(string damageType)
        {
            return damageType?.ToLowerInvariant() switch
            {
                "melee" => melee,
                "ranged" => ranged,
                "siege" => siege,
                "magic" => magic,
                _ => melee  // default to melee defense
            };
        }
        
        /// <summary>
        /// Returns the highest defense value.
        /// </summary>
        public int MaxDefense => Math.Max(Math.Max(melee, ranged), Math.Max(siege, magic));
        
        /// <summary>
        /// Returns the average defense value.
        /// </summary>
        public float AverageDefense => (melee + ranged + siege + magic) / 4f;
    }
    
    /// <summary>
    /// Resource cost for units, buildings, and technologies.
    /// All game economy resources in one block.
    /// </summary>
    [Serializable]
    public class CostBlock
    {
        public int Supplies;
        public int Iron;
        public int Crystal;
        public int Veilsteel;
        public int Glow;
        
        /// <summary>
        /// Returns true if all costs are zero.
        /// </summary>
        public bool IsZero => Supplies == 0 && Iron == 0 && Crystal == 0 && 
                              Veilsteel == 0 && Glow == 0;
        
        /// <summary>
        /// Create a CostBlock with specified values.
        /// </summary>
        public static CostBlock Of(int supplies = 0, int iron = 0, int crystal = 0, 
                                   int veilsteel = 0, int glow = 0)
        {
            return new CostBlock
            {
                Supplies = supplies,
                Iron = iron,
                Crystal = crystal,
                Veilsteel = veilsteel,
                Glow = glow
            };
        }
        
        /// <summary>
        /// Get total "value" of resources (simple sum for AI evaluation).
        /// </summary>
        public int TotalValue => Supplies + (Iron * 2) + (Crystal * 3) + 
                                 (Veilsteel * 5) + (Glow * 4);
        
        /// <summary>
        /// Returns a human-readable string of non-zero costs.
        /// </summary>
        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Supplies > 0) parts.Add($"S:{Supplies}");
            if (Iron > 0) parts.Add($"Fe:{Iron}");
            if (Crystal > 0) parts.Add($"Cr:{Crystal}");
            if (Veilsteel > 0) parts.Add($"Vs:{Veilsteel}");
            if (Glow > 0) parts.Add($"Gl:{Glow}");
            return parts.Count > 0 ? string.Join(" ", parts) : "Free";
        }
    }
    
    /// <summary>
    /// Combat profile defining damage calculation rules and modifiers.
    /// Used for the combat system's damage formula.
    /// </summary>
    [Serializable]
    public class CombatProfile
    {
        public string defenseFormulaHint;   // hint for defense calculation formula
        
        // Future expansion:
        // public Dictionary<string, float> damageTypeModifiers;
        // public Dictionary<string, float> armorTypeModifiers;
    }
}