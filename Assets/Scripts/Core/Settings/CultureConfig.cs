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

    // ==================== Age-Up Cost ====================

    /// <summary>
    /// Resource cost to advance from Era 1 to Era 2.
    /// </summary>
    public static readonly Cost AgeUpCost = Cost.Of(supplies: 1000, iron: 200, crystal: 150);

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
