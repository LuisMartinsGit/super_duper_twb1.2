// CombatComponents.cs
// Components for combat, targeting, and damage systems
// Place in: Assets/Scripts/Core/Components/Combat/

using Unity.Entities;

// ==================== Basic Combat Stats ====================

/// <summary>
/// Base damage output of an entity.
/// </summary>
public struct Damage : IComponentData
{
    public int Value;
}

/// <summary>
/// Attack speed cooldown management.
/// </summary>
public struct AttackCooldown : IComponentData
{
    public float Cooldown;  // Seconds between attacks
    public float Timer;     // Current countdown timer
}

// ==================== Targeting System ====================

/// <summary>
/// Current combat target.
/// </summary>
public struct Target : IComponentData
{
    public Entity Value; // Entity.Null if no target
}

// ==================== Damage & Armor Type System ====================

/// <summary>
/// Categorizes a unit's or building's outgoing damage.
/// Used by CombatModifiers for damage-type vs armor-type modifier lookups.
/// </summary>
public enum DamageType : byte
{
    Melee  = 0,
    Ranged = 1,
    Siege  = 2,
    Magic  = 3,
    True   = 4
}

/// <summary>
/// Categorizes a unit's or building's incoming damage resistance profile.
/// Used by CombatModifiers for damage-type vs armor-type modifier lookups.
/// </summary>
public enum ArmorType : byte
{
    InfantryLight  = 0,
    InfantryHeavy  = 1,
    Ranged         = 2,
    Cavalry        = 3,
    Structure      = 4,
    StructureHuman = 5
}

/// <summary>
/// Tags an entity with its outgoing damage type.
/// Default: Melee if component is absent.
/// </summary>
public struct DamageTypeData : IComponentData
{
    public DamageType Value;
}

/// <summary>
/// Tags an entity with its armor type for incoming damage calculations.
/// Default: InfantryLight if component is absent.
/// </summary>
public struct ArmorTypeData : IComponentData
{
    public ArmorType Value;
}

/// <summary>
/// Parses the string forms used by the TechTree SOs / JSON ("melee",
/// "infantry_light", ...) into the combat enums. Unknown or empty strings
/// return the caller's fallback so factories keep their tuned defaults.
/// </summary>
public static class CombatTypeParse
{
    public static DamageType Damage(string s, DamageType fallback)
    {
        switch (s)
        {
            case "melee":  return DamageType.Melee;
            case "ranged": return DamageType.Ranged;
            case "siege":  return DamageType.Siege;
            case "magic":  return DamageType.Magic;
            case "true":   return DamageType.True;
            default:       return fallback;
        }
    }

    public static ArmorType Armor(string s, ArmorType fallback)
    {
        switch (s)
        {
            case "infantry":
            case "infantry_light":  return ArmorType.InfantryLight;
            case "infantry_heavy":  return ArmorType.InfantryHeavy;
            case "ranged":          return ArmorType.Ranged;
            case "cavalry":         return ArmorType.Cavalry;
            case "structure":       return ArmorType.Structure;
            case "structure_human": return ArmorType.StructureHuman;
            default:                return fallback;
        }
    }
}

// ==================== Unit Tags & Bonus Damage (AoE4-style) ====================

/// <summary>Bit flags for the TechTree unit tags ("Infantry", "Heavy", ...).</summary>
[System.Flags]
public enum UnitTagBits : uint
{
    None      = 0,
    Infantry  = 1u << 0,
    Cavalry   = 1u << 1,
    Ranged    = 1u << 2,
    Siege     = 1u << 3,
    Heavy     = 1u << 4,
    Light     = 1u << 5,
    Building  = 1u << 6,
    Worker    = 1u << 7,
    Religious = 1u << 8,
    Ship      = 1u << 9,
}

/// <summary>
/// Tags this entity HAS (targets of others' bonus damage), from the unit
/// SO's tags list. Entities with BuildingTag implicitly count as Building
/// even without this component (see <see cref="TagBonus.Compute"/>).
/// </summary>
public struct UnitTagsData : IComponentData
{
    public uint Mask;
}

/// <summary>
/// Flat bonus damage vs target tags, from the unit SO's bonusVsTags list
/// (added after armor, armor-ignoring — see CombatModifiers). Up to four
/// (tag-mask, amount) pairs; unused slots have Mask == 0.
/// </summary>
public struct BonusVsTags : IComponentData
{
    public uint Mask0; public int Amount0;
    public uint Mask1; public int Amount1;
    public uint Mask2; public int Amount2;
    public uint Mask3; public int Amount3;

    public bool IsEmpty => Mask0 == 0 && Mask1 == 0 && Mask2 == 0 && Mask3 == 0;

    /// <summary>Sum of the bonus amounts whose tag mask intersects the target's tags.</summary>
    public int AmountAgainst(uint targetMask)
    {
        int bonus = 0;
        if ((Mask0 & targetMask) != 0) bonus += Amount0;
        if ((Mask1 & targetMask) != 0) bonus += Amount1;
        if ((Mask2 & targetMask) != 0) bonus += Amount2;
        if ((Mask3 & targetMask) != 0) bonus += Amount3;
        return bonus;
    }
}

/// <summary>Parses the TechTree SO/JSON tag strings into <see cref="UnitTagBits"/>.</summary>
public static class UnitTagParse
{
    public static uint Tag(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        switch (s.ToLowerInvariant())
        {
            case "infantry":  return (uint)UnitTagBits.Infantry;
            case "cavalry":   return (uint)UnitTagBits.Cavalry;
            case "ranged":    return (uint)UnitTagBits.Ranged;
            case "siege":     return (uint)UnitTagBits.Siege;
            case "heavy":     return (uint)UnitTagBits.Heavy;
            case "light":     return (uint)UnitTagBits.Light;
            case "building":  return (uint)UnitTagBits.Building;
            case "worker":    return (uint)UnitTagBits.Worker;
            case "religious": return (uint)UnitTagBits.Religious;
            case "ship":      return (uint)UnitTagBits.Ship;
            default:          return 0; // unknown tag — ignored
        }
    }

    public static uint Mask(string[] tags)
    {
        if (tags == null) return 0;
        uint mask = 0;
        for (int i = 0; i < tags.Length; i++) mask |= Tag(tags[i]);
        return mask;
    }

    /// <summary>Build the runtime bonus component from the SO's bonusVsTags list
    /// (first four entries with a known tag; amounts rounded to ints).</summary>
    public static BonusVsTags Bonus(System.Collections.Generic.List<TheWaningBorder.Data.DamageBonus> list)
    {
        var result = default(BonusVsTags);
        if (list == null) return result;
        int slot = 0;
        for (int i = 0; i < list.Count && slot < 4; i++)
        {
            if (list[i] == null) continue;
            uint mask = Tag(list[i].vsTag);
            if (mask == 0) continue;
            int amount = (int)System.Math.Round(list[i].amount);
            if (amount == 0) continue;
            switch (slot)
            {
                case 0: result.Mask0 = mask; result.Amount0 = amount; break;
                case 1: result.Mask1 = mask; result.Amount1 = amount; break;
                case 2: result.Mask2 = mask; result.Amount2 = amount; break;
                default: result.Mask3 = mask; result.Amount3 = amount; break;
            }
            slot++;
        }
        return result;
    }
}

/// <summary>
/// Shared bonus-damage lookup for the combat systems: the attacker's
/// <see cref="BonusVsTags"/> against the target's tags. BuildingTag counts
/// as the Building tag implicitly so every building is a valid bonus target
/// without touching each factory.
/// </summary>
public static class TagBonus
{
    public static int Compute(Unity.Entities.EntityManager em, Unity.Entities.Entity attacker, Unity.Entities.Entity target)
    {
        if (attacker == Unity.Entities.Entity.Null || !em.Exists(attacker)) return 0;
        if (!em.HasComponent<BonusVsTags>(attacker)) return 0;
        if (target == Unity.Entities.Entity.Null || !em.Exists(target)) return 0;

        uint mask = 0;
        if (em.HasComponent<UnitTagsData>(target)) mask = em.GetComponentData<UnitTagsData>(target).Mask;
        if (em.HasComponent<BuildingTag>(target)) mask |= (uint)UnitTagBits.Building;
        if (mask == 0) return 0;

        return em.GetComponentData<BonusVsTags>(attacker).AmountAgainst(mask);
    }
}

// ==================== Command Components ====================
// Command types consolidated into TheWaningBorder.Core.Commands.Types namespace.
// See: Core/Commands/CommandTypes/AttackCommand.cs, BuildCommand.cs, GatherCommand.cs, HealCommand.cs
// Use: using TheWaningBorder.Core.Commands.Types;