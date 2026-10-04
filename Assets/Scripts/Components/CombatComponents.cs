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
// ALWAYS PRESENT on combat units (factories add it with Null) — "no target"
// is Value == Entity.Null, never component absence. Do NOT RemoveComponent
// it: add/remove churn on transient components built 7,782 archetypes in one
// 76-minute match and crashed the EntityQueryManager's BlockAllocator (see
// TransientState.cs). Null the value instead, as CommandCleanup does.
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

    /// <summary>
    /// Build the runtime <see cref="TargetPreference"/> from the SO's
    /// preferTargets list (docs/Design/Combat_Pacing.md § Target preference).
    /// Each entry is one rule: tags joined by '+' (the candidate must carry
    /// ALL of them, e.g. "Cavalry+Heavy"), or the word "Hero" (any hero), or
    /// "Massed" (the candidate standing among the most enemies). Up to four
    /// tag rules; unknown words are ignored like unknown tags.
    /// </summary>
    public static TargetPreference Preference(string[] entries)
    {
        var result = default(TargetPreference);
        if (entries == null) return result;
        int slot = 0;
        for (int i = 0; i < entries.Length; i++)
        {
            string e = entries[i];
            if (string.IsNullOrEmpty(e)) continue;
            string lower = e.Trim().ToLowerInvariant();
            if (lower == "hero" || lower == "heroes") { result.Heroes = 1; continue; }
            if (lower == "massed") { result.Massed = 1; continue; }
            if (slot >= 4) continue;
            uint mask = 0;
            bool bad = false;
            foreach (var part in e.Split('+'))
            {
                uint t = Tag(part.Trim());
                if (t == 0) { bad = true; break; }
                mask |= t;
            }
            if (bad || mask == 0) continue;
            switch (slot)
            {
                case 0: result.Mask0 = mask; break;
                case 1: result.Mask1 = mask; break;
                case 2: result.Mask2 = mask; break;
                default: result.Mask3 = mask; break;
            }
            slot++;
        }
        return result;
    }
}

/// <summary>
/// What an attacker would rather shoot (docs/Design/Combat_Pacing.md
/// § Target preference), from the unit SO's preferTargets list. Read by
/// TargetingSystem's auto-acquire: a preferred candidate INSIDE the unit's
/// own attack reach is taken over the nearest one. Ordered targets are never
/// touched. A unit without the component (almost all of them) acquires as
/// before.
/// </summary>
public struct TargetPreference : IComponentData
{
    /// <summary>Tag rules: a candidate matches a rule when it carries every
    /// tag in the mask. Unused slots are 0.</summary>
    public uint Mask0, Mask1, Mask2, Mask3;
    /// <summary>1 = any hero (HeroLevel) is preferred.</summary>
    public byte Heroes;
    /// <summary>1 = among candidates (preferred by the rules above, or any
    /// unit when there are no rules), the one standing among the most
    /// enemies wins — the splash engine's pick.</summary>
    public byte Massed;

    public bool IsEmpty => Mask0 == 0 && Mask1 == 0 && Mask2 == 0 && Mask3 == 0 && Heroes == 0 && Massed == 0;
    public bool HasRules => Mask0 != 0 || Mask1 != 0 || Mask2 != 0 || Mask3 != 0 || Heroes != 0;

    /// <summary>Does a candidate with these tags match a rule?</summary>
    public bool Matches(uint tags, bool hero)
    {
        if (Heroes != 0 && hero) return true;
        if (Mask0 != 0 && (tags & Mask0) == Mask0) return true;
        if (Mask1 != 0 && (tags & Mask1) == Mask1) return true;
        if (Mask2 != 0 && (tags & Mask2) == Mask2) return true;
        if (Mask3 != 0 && (tags & Mask3) == Mask3) return true;
        return false;
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