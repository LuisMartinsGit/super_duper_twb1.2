// UnitDef.cs
// Unit definition data structure parsed from TechTree JSON
// Part of: Data/TechTree/Definitions/

using System;
using System.Collections.Generic;

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
        /// <summary>Wind-up seconds between acquiring a target and releasing the
        /// shot. Was a per-factory DefaultAimTime constant in 16 unit factories
        /// until 2026-08-27.</summary>
        public float aimTime;

        // ==================== Spatial ====================
        /// <summary>Collision / selection radius in metres. Was a per-factory
        /// DefaultRadius constant in 29 unit factories until 2026-08-27 — the
        /// SO had no field for it, so no author could tune it.</summary>
        public float radius;

        // ==================== Projectile Profile (ranged units) ====================
        /// <summary>"low" (default shortbow arc) | "flat" (crossbow straight line) | "high" (longbow parabola).</summary>
        public string trajectory;
        /// <summary>Projectile speed override (m/s). 0 = combat system default.</summary>
        public float projectileSpeed;
        
        // ==================== Economy ====================
        public CostBlock cost;

        // ==================== Progression Gating ====================
        // Minimum level the trainer building must be to unlock this unit.
        // 0 / 1 = available immediately. 2 = needs L2 building, etc.
        public int minBuildingLevel;

        // ==================== Support Unit Fields ====================
        public float buildSpeed;        // for builders
        public float gatheringSpeed;    // for miners/gatherers
        public float healsPerSecond;    // for healers
        public float healRange;         // reach of the heal (Litharch)

        // ==================== Tags & Bonus Damage (AoE4-style) ====================
        /// <summary>Tags this unit HAS (targetable by others' bonus damage).</summary>
        public string[] tags;
        /// <summary>Flat bonus damage vs target tags (added after armor, ignores armor).</summary>
        public List<DamageBonus> bonusVsTags;

        // ==================== Abilities (data-driven ability system) ====================
        /// <summary>Ability card names attached to this unit (see AbilityCatalog).
        /// The unit factory builds a UnitAbilities component from these.</summary>
        public string[] abilities;

        // ==================== Siege Specials ====================
        // Optional second attack mode for siege-class units (currently the
        // Godsplinter). 0 = not used / keep the unit's built-in constants.
        /// <summary>Close-range direct siege attack range.</summary>
        public float siegeRange;
        /// <summary>Seconds between close-range siege attacks.</summary>
        public float siegeCooldown;
        /// <summary>Splash radius of the unit's AoE shots.</summary>
        public float aoeRadius;

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