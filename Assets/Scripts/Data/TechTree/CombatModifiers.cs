// CombatModifiers.cs
// Damage formula + damage-type-vs-armor matrix. Extracted from the former TechTreeDB.cs
// (which is being removed); callers reference CombatModifiers directly so they are
// unaffected by the move.
// Part of: Data/TechTree/

using Unity.Mathematics;

// ═══════════════════════════════════════════════════════════════════════════════
// COMBAT MODIFIER MATRIX
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Static lookup for damage-type x armor-type modifier matrix and final damage calculation.
/// Lazy-initialized on first access. Thread-safe via static initializer.
///
/// Matrix layout:
///   Rows = DamageType (Melee, Ranged, Siege, Magic, True)
///   Cols = ArmorType  (InfantryLight, InfantryHeavy, Ranged, Cavalry, Structure, StructureHuman)
/// </summary>
public static class CombatModifiers
{
    // 5 damage types x 6 armor types
    private static readonly float[,] _modifiers;

    static CombatModifiers()
    {
        _modifiers = new float[5, 6];

        // Melee vs: Light=1.0, Heavy=1.0, Ranged=1.1, Cavalry=0.9, Structure=0.2, StructHuman=0.2
        _modifiers[0, 0] = 1.0f;  _modifiers[0, 1] = 1.0f;  _modifiers[0, 2] = 1.1f;
        _modifiers[0, 3] = 0.9f;  _modifiers[0, 4] = 0.2f;  _modifiers[0, 5] = 0.2f;

        // Ranged vs: 1.1, 0.9, 1.0, 0.8, 0.15, 0.15
        _modifiers[1, 0] = 1.1f;  _modifiers[1, 1] = 0.9f;  _modifiers[1, 2] = 1.0f;
        _modifiers[1, 3] = 0.8f;  _modifiers[1, 4] = 0.15f; _modifiers[1, 5] = 0.15f;

        // Siege vs: 0.6, 0.8, 0.8, 0.7, 3.0, 2.4
        _modifiers[2, 0] = 0.6f;  _modifiers[2, 1] = 0.8f;  _modifiers[2, 2] = 0.8f;
        _modifiers[2, 3] = 0.7f;  _modifiers[2, 4] = 3.0f;  _modifiers[2, 5] = 2.4f;

        // Magic vs: 1.1, 0.9, 1.1, 1.0, 0.5, 0.45
        _modifiers[3, 0] = 1.1f;  _modifiers[3, 1] = 0.9f;  _modifiers[3, 2] = 1.1f;
        _modifiers[3, 3] = 1.0f;  _modifiers[3, 4] = 0.5f;  _modifiers[3, 5] = 0.45f;

        // True vs: all 1.0 (ignores armor type)
        _modifiers[4, 0] = 1.0f;  _modifiers[4, 1] = 1.0f;  _modifiers[4, 2] = 1.0f;
        _modifiers[4, 3] = 1.0f;  _modifiers[4, 4] = 1.0f;  _modifiers[4, 5] = 1.0f;
    }

    /// <summary>
    /// Look up the damage modifier for a given damage-type attacking a given armor-type.
    /// </summary>
    public static float GetModifier(DamageType dmg, ArmorType armor)
    {
        return _modifiers[(int)dmg, (int)armor];
    }

    /// <summary>
    /// Extract the defense value relevant to the incoming damage type.
    /// True damage always returns 0 (bypasses defense).
    /// </summary>
    public static int GetDefenseValue(Defense def, DamageType dmgType)
    {
        return dmgType switch
        {
            DamageType.Melee  => def.Melee,
            DamageType.Ranged => def.Ranged,
            DamageType.Siege  => def.Siege,
            DamageType.Magic  => def.Magic,
            DamageType.True   => 0, // True damage ignores defense
            _ => 0
        };
    }

    /// <summary>
    /// Legacy combat-pacing scalar. NO LONGER APPLIED — the AoE4-style flat-armor
    /// formula below does not use it. Kept only so external references still compile.
    /// </summary>
    public const float GlobalDamageMultiplier = 0.5f;

    /// <summary>
    /// AoE4-style damage:
    ///   final = max(1, baseDamage − armor) + bonusDamage
    /// Armor is a FLAT subtraction with a hard floor of 1 (chip damage always lands);
    /// bonusDamage (vs the target's tags, e.g. siege +vs Building) is added afterwards
    /// and IGNORES armor. The result is then scaled by this game's height / veilstone
    /// modifiers (1.0 = neutral).
    ///
    /// `defenseValue` is the flat armor for the incoming damage type (melee armor for
    /// melee, ranged armor for ranged — see GetDefenseValue). `armorType` and the
    /// GetModifier matrix are retained for counter-hint/UI lookups but no longer feed
    /// the damage number.
    /// </summary>
    public static int CalculateFinalDamage(int baseDamage, DamageType dmgType,
        ArmorType armorType, int defenseValue, float heightMod, float borderMod)
        => CalculateFinalDamage(baseDamage, dmgType, armorType, defenseValue, heightMod, borderMod, 0);

    /// <summary>
    /// Overload adding flat bonus damage vs the target's tags (added after armor,
    /// armor-ignoring). See the no-bonus overload for the full formula description.
    /// </summary>
    public static int CalculateFinalDamage(int baseDamage, DamageType dmgType,
        ArmorType armorType, int defenseValue, float heightMod, float borderMod, int bonusDamage)
    {
        int   afterArmor = math.max(1, baseDamage - math.max(0, defenseValue));
        float scaled     = (afterArmor + math.max(0, bonusDamage)) * heightMod * borderMod;
        return math.max(1, (int)math.round(scaled));
    }
}
