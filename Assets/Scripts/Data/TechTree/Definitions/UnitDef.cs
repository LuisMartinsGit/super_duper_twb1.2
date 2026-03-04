// UnitDef.cs
// Unit definition data structure parsed from TechTree JSON
// Part of: Data/TechTree/Definitions/

using System;

namespace TheWaningBorder.Data
{
    /// <summary>
    /// Defines a unit type's base stats and attributes.
    /// Loaded from TechTree.json at runtime.
    /// </summary>
    [Serializable]
    public class UnitDef
    {
        // ==================== Identity ====================
        public string id;
        public string name;
        public string unitClass;        // e.g., "melee", "ranged", "support", "siege"
        
        // ==================== Core Stats ====================
        public float hp;
        public float speed;
        public float trainingTime;      // seconds to train
        
        // ==================== Combat Stats ====================
        public float damage;
        public string damageType;       // e.g., "melee", "ranged", "siege", "magic"
        public string armorType;        // e.g., "infantry", "cavalry", "structure_human"
        public DefenseBlock defense;
        public float attackCooldown;
        
        // ==================== Range & Vision ====================
        public float attackRange;
        public float minAttackRange;    // minimum attack range (for archers, siege)
        public float lineOfSight;
        
        // ==================== Economy ====================
        public CostBlock cost;
        
        // ==================== Support Unit Fields ====================
        public float buildSpeed;        // for builders
        public float gatheringSpeed;    // for miners/gatherers
        public int carryCapacity;       // resource carry capacity
        public float healsPerSecond;    // for healers
        
        // ==================== Helpers ====================
        
        /// <summary>
        /// Returns true if this is a melee combat unit.
        /// </summary>
        public bool IsMelee => string.Equals(unitClass, "melee", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(damageType, "melee", StringComparison.OrdinalIgnoreCase);
        
        /// <summary>
        /// Returns true if this is a ranged combat unit.
        /// </summary>
        public bool IsRanged => string.Equals(unitClass, "ranged", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(damageType, "ranged", StringComparison.OrdinalIgnoreCase);
        
        /// <summary>
        /// Returns true if this is a support unit (builder, healer, etc).
        /// </summary>
        public bool IsSupport => string.Equals(unitClass, "support", StringComparison.OrdinalIgnoreCase) ||
                                 buildSpeed > 0 || gatheringSpeed > 0 || healsPerSecond > 0;
        
        /// <summary>
        /// Returns true if this is a siege unit.
        /// </summary>
        public bool IsSiege => string.Equals(unitClass, "siege", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(damageType, "siege", StringComparison.OrdinalIgnoreCase);
    }
}