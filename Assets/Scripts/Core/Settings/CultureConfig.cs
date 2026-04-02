// CultureConfig.cs
// Culture palette and metadata for Era 2 age-up system
// Location: Assets/Scripts/Core/Settings/CultureConfig.cs

using UnityEngine;
using TheWaningBorder.Core;

/// <summary>
/// Static configuration for culture palettes and metadata.
/// Used by FactionColors for culture overrides and by the age-up popup.
/// Each culture has a primary color (used for faction identity) and secondary color (accents).
/// </summary>
public static class CultureConfig
{
    // ==================== Culture Palettes ====================

    // Alanthor: grey/green — industrial forgemasters, stone-and-moss aesthetic
    public static readonly Color AlanthorPrimary   = new Color(0.55f, 0.65f, 0.50f, 1f); // sage green
    public static readonly Color AlanthorSecondary  = new Color(0.45f, 0.45f, 0.42f, 1f); // warm grey

    // Feraldis: dark grey/red — fierce warband culture, iron-and-fire aesthetic
    public static readonly Color FeraldisPrimary   = new Color(0.70f, 0.18f, 0.15f, 1f); // crimson red
    public static readonly Color FeraldisSecondary  = new Color(0.28f, 0.26f, 0.24f, 1f); // dark grey

    // Runai: cyan/sandstone — nomadic traders, sky-and-desert aesthetic
    public static readonly Color RunaiPrimary      = new Color(0.25f, 0.75f, 0.80f, 1f); // cyan
    public static readonly Color RunaiSecondary    = new Color(0.76f, 0.65f, 0.45f, 1f); // sandstone

    // ==================== Culture Names ====================

    public static readonly string[] Names = { "None", "Runai", "Alanthor", "Feraldis" };

    // ==================== Culture Descriptions ====================

    public static readonly string[] Descriptions =
    {
        "",
        "Nomadic traders and explorers.\nBonus: Trade routes, mobile outposts.",
        "Industrial forgemasters.\nBonus: Superior metal processing, fortifications.",
        "Fierce warband culture.\nBonus: Hunting bonuses, aggressive units."
    };

    // ==================== Age-Up Cost & Duration ====================

    /// <summary>
    /// Resource cost to advance from Era 1 to Era 2.
    /// </summary>
    public static readonly Cost AgeUpCost = Cost.Of(supplies: 1000, iron: 200, crystal: 150);

    /// <summary>
    /// Time in seconds for the age-up process to complete after culture is chosen.
    /// </summary>
    public static float AgeUpDuration = 60f;

    // ==================== Building Material Palettes ====================
    // Distinct from Primary/Secondary (used for UI/identity).
    // These are the actual material colors applied to procedural building geometry.

    // Era 1 (no culture) — neutral stone aesthetic
    private static readonly Color NoneWall  = new Color(0.60f, 0.58f, 0.55f, 1f); // stone grey
    private static readonly Color NoneRoof  = new Color(0.45f, 0.32f, 0.18f, 1f); // wood brown
    private static readonly Color NoneTrim  = new Color(0.30f, 0.22f, 0.12f, 1f); // dark wood

    // Runai — sandstone + cyan fabric
    private static readonly Color RunaiWall  = new Color(0.76f, 0.65f, 0.45f, 1f); // sandstone
    private static readonly Color RunaiRoof  = new Color(0.25f, 0.75f, 0.80f, 1f); // cyan fabric
    private static readonly Color RunaiTrim  = new Color(0.80f, 0.65f, 0.30f, 1f); // gold trim

    // Alanthor — grey stone + sage moss
    private static readonly Color AlanthorWall  = new Color(0.45f, 0.45f, 0.42f, 1f); // warm grey stone
    private static readonly Color AlanthorRoof  = new Color(0.55f, 0.65f, 0.50f, 1f); // sage moss
    private static readonly Color AlanthorTrim  = new Color(0.35f, 0.35f, 0.38f, 1f); // iron

    // Feraldis — dark stone + crimson
    private static readonly Color FeraldisWall  = new Color(0.28f, 0.26f, 0.24f, 1f); // dark stone
    private static readonly Color FeraldisRoof  = new Color(0.70f, 0.18f, 0.15f, 1f); // crimson
    private static readonly Color FeraldisTrim  = new Color(0.15f, 0.13f, 0.12f, 1f); // charcoal

    /// <summary>Base wall/structure color for a culture's buildings.</summary>
    public static Color GetWallColor(byte culture)
    {
        return culture switch
        {
            Cultures.Runai    => RunaiWall,
            Cultures.Alanthor => AlanthorWall,
            Cultures.Feraldis => FeraldisWall,
            _ => NoneWall
        };
    }

    /// <summary>Roof/accent color for a culture's buildings.</summary>
    public static Color GetRoofColor(byte culture)
    {
        return culture switch
        {
            Cultures.Runai    => RunaiRoof,
            Cultures.Alanthor => AlanthorRoof,
            Cultures.Feraldis => FeraldisRoof,
            _ => NoneRoof
        };
    }

    /// <summary>Trim/detail color for a culture's buildings.</summary>
    public static Color GetTrimColor(byte culture)
    {
        return culture switch
        {
            Cultures.Runai    => RunaiTrim,
            Cultures.Alanthor => AlanthorTrim,
            Cultures.Feraldis => FeraldisTrim,
            _ => NoneTrim
        };
    }

    // ==================== Lookup Methods ====================

    /// <summary>
    /// Get the primary (identity) color for a culture.
    /// </summary>
    public static Color GetPrimary(byte culture)
    {
        return culture switch
        {
            Cultures.Runai    => RunaiPrimary,
            Cultures.Alanthor => AlanthorPrimary,
            Cultures.Feraldis => FeraldisPrimary,
            _ => Color.gray
        };
    }

    /// <summary>
    /// Get the secondary (accent) color for a culture.
    /// </summary>
    public static Color GetSecondary(byte culture)
    {
        return culture switch
        {
            Cultures.Runai    => RunaiSecondary,
            Cultures.Alanthor => AlanthorSecondary,
            Cultures.Feraldis => FeraldisSecondary,
            _ => Color.gray
        };
    }

    /// <summary>
    /// Get display name for a culture.
    /// </summary>
    public static string GetName(byte culture)
    {
        return (culture >= 0 && culture < Names.Length) ? Names[culture] : "Unknown";
    }

    /// <summary>
    /// Get description for a culture.
    /// </summary>
    public static string GetDescription(byte culture)
    {
        return (culture >= 0 && culture < Descriptions.Length) ? Descriptions[culture] : "";
    }
}
