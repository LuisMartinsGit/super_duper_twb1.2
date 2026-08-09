// TechnologyDef.cs
// Technology definition and supporting data structures parsed from TechTree JSON
// Part of: Data/TechTree/Definitions/

using System;
using System.Collections.Generic;

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
        public string[] prerequisites;  // tech IDs that must be researched first

        /// <summary>
        /// Culture name ("Alanthor" etc.) this tech is restricted to.
        /// Empty/null = available to all cultures.
        /// </summary>
        public string culture;

        /// <summary>
        /// Minimum BuildingUpgradeState.Level of the RESEARCH HOST building.
        /// 0 or 1 = no level gate (base buildings count as level 1).
        /// </summary>
        public int minBuildingLevel;

        // ==================== Economy ====================
        public CostBlock cost;

        // ==================== Effects ====================
        /// <summary>
        /// Stat modifiers applied when this technology is researched.
        /// Parsed from the "effects" sub-object in TechTree.json.
        /// Null if the technology has no stat effects (e.g. age-up techs).
        /// </summary>
        public TechEffects effects;

        /// <summary>
        /// Generic target/op/stat effects (calculator model, Wave 2). Parsed
        /// from the "effectsList" array in TechTree.json. Null or empty if the
        /// technology carries no generic effects (e.g. ability-unlock techs —
        /// those must be harmless no-ops until their behavior is wired).
        /// </summary>
        public List<TechEffectEntry> effectsList;

        // ==================== Helpers ====================
        
        /// <summary>
        /// Returns true if this technology has a cost.
        /// </summary>
        public bool HasCost => cost != null && !cost.IsZero;

        /// <summary>
        /// Returns true if this technology has prerequisites.
        /// </summary>
        public bool HasPrerequisites => prerequisites != null && prerequisites.Length > 0;
    }
    
    // ═══════════════════════════════════════════════════════════════════════
    // TECHNOLOGY EFFECTS
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// One generic tech effect: WHO it hits (Target), WHAT stat it moves
    /// (Stat), and HOW (Op + Value). Replaces the fixed six-field TechEffects
    /// model for the Wave 2 tech sweep; both models coexist.
    /// </summary>
    [Serializable]
    public class TechEffectEntry
    {
        /// <summary>"type:Melee" | "type:Ranged" | "type:Cavalry" | "type:Siege" | "unit:&lt;Id&gt;".</summary>
        public string Target;

        /// <summary>"Hp" | "Damage" | "Speed" | "DefenseAll" | "AttackRange" | "AttackCooldown" | "LineOfSight".</summary>
        public string Stat;

        /// <summary>"Add" (+= Value) or "Pct" (*= 1 + Value/100).</summary>
        public string Op;

        public float Value;
    }

    /// <summary>
    /// Stat modifiers granted by researching a technology.
    /// Each field corresponds to a JSON key in the "effects" block of TechTree.json.
    /// Values of 0 mean "no effect" for that stat.
    /// </summary>
    [Serializable]
    public class TechEffects
    {
        /// <summary>Multiplier for miner gather speed (e.g. 1.15 = 15% faster).</summary>
        public float gatherSpeedMult;

        /// <summary>Multiplier for melee attack speed (e.g. 1.1 = 10% faster attacks).</summary>
        public float meleeAttackSpeedMult;

        /// <summary>Flat bonus added to melee defense (e.g. 1).</summary>
        public int meleeDefenseAdd;

        /// <summary>Flat bonus added to melee unit damage (e.g. 2 — Stone Weapons).</summary>
        public int meleeDamageAdd;

        /// <summary>Flat bonus added to ranged unit damage (e.g. 2 — Stone-Tipped Arrows).</summary>
        public int rangedDamageAdd;

        /// <summary>Multiplier for Archer-class max attack range (e.g. 1.15 — Fletching).</summary>
        public float archerRangeMult;

        /// <summary>
        /// Returns true if at least one effect field has a non-zero value.
        /// </summary>
        public bool HasAnyEffect =>
            gatherSpeedMult != 0f ||
            meleeAttackSpeedMult != 0f || meleeDefenseAdd != 0 ||
            meleeDamageAdd != 0 || rangedDamageAdd != 0 || archerRangeMult != 0f;
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
        [UnityEngine.Serialization.FormerlySerializedAs("Crystal")]
        public int Veilstone;
        public int Veilsteel;
        // Glow removed from build costs: it is an item/pickup, not a spendable cost resource.

        /// <summary>
        /// Returns true if all costs are zero.
        /// </summary>
        public bool IsZero => Supplies == 0 && Iron == 0 && Veilstone == 0 &&
                              Veilsteel == 0;

        /// <summary>
        /// Create a CostBlock with specified values.
        /// </summary>
        public static CostBlock Of(int supplies = 0, int iron = 0, int veilstone = 0,
                                   int veilsteel = 0)
        {
            return new CostBlock
            {
                Supplies = supplies,
                Iron = iron,
                Veilstone = veilstone,
                Veilsteel = veilsteel,
            };
        }

        /// <summary>
        /// Get total "value" of resources (simple sum for AI evaluation).
        /// </summary>
        public int TotalValue => Supplies + (Iron * 2) + (Veilstone * 3) +
                                 (Veilsteel * 5);

        /// <summary>
        /// Returns a human-readable string of non-zero costs.
        /// </summary>
        public override string ToString()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Supplies > 0) parts.Add($"S:{Supplies}");
            if (Iron > 0) parts.Add($"Fe:{Iron}");
            if (Veilstone > 0) parts.Add($"Cr:{Veilstone}");
            if (Veilsteel > 0) parts.Add($"Vs:{Veilsteel}");
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