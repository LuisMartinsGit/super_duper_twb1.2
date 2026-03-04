// FactionColors.cs
// Central palette for faction colors with configurable 12-color pool
// Place in: Assets/Scripts/Core/Settings/FactionColors.cs

using UnityEngine;

/// <summary>
/// Central palette for faction colors. Provides a pool of 12 colors that can be
/// assigned to factions via the lobby. Use everywhere that needs consistent per-player colors
/// (minimap blips, selection/indicator decals, UI, health bars, etc.).
/// </summary>
public static class FactionColors
{
    /// <summary>
    /// Pool of 12 available player colors.
    /// </summary>
    public static readonly Color[] ColorPool = new Color[]
    {
        new Color(0.20f, 0.55f, 1.00f, 1f),  // 0: Blue
        new Color(1.00f, 0.20f, 0.25f, 1f),  // 1: Red
        new Color(0.20f, 0.90f, 0.35f, 1f),  // 2: Green
        new Color(1.00f, 0.85f, 0.20f, 1f),  // 3: Yellow
        new Color(0.80f, 0.40f, 1.00f, 1f),  // 4: Purple
        new Color(1.00f, 0.55f, 0.15f, 1f),  // 5: Orange
        new Color(0.20f, 1.00f, 0.95f, 1f),  // 6: Teal
        new Color(0.75f, 0.75f, 0.75f, 1f),  // 7: Silver
        new Color(1.00f, 0.40f, 0.70f, 1f),  // 8: Pink
        new Color(0.60f, 0.35f, 0.15f, 1f),  // 9: Brown
        new Color(0.10f, 0.10f, 0.10f, 1f),  // 10: Black
        new Color(0.55f, 0.00f, 0.00f, 1f),  // 11: Maroon
    };

    /// <summary>
    /// Display names for each color in the pool.
    /// </summary>
    public static readonly string[] ColorNames = new string[]
    {
        "Blue", "Red", "Green", "Yellow", "Purple", "Orange",
        "Teal", "Silver", "Pink", "Brown", "Black", "Maroon"
    };

    /// <summary>
    /// Total number of colors in the pool.
    /// </summary>
    public const int ColorCount = 12;

    // Maps Faction index (0-7) to color pool index (0-11)
    // Default: faction 0 = Blue, 1 = Red, 2 = Green, etc.
    private static int[] _factionToColor = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 };

    // Culture override: when a faction ages up, Get() returns the culture color instead.
    // Value of Cultures.None (0) means no override — use the pool color.
    private static byte[] _factionCulture = new byte[8];

    /// <summary>
    /// Set the culture for a faction. Once set, Get(f) returns the culture primary color.
    /// Called when a faction advances to Era 2.
    /// </summary>
    public static void SetFactionCulture(Faction faction, byte culture)
    {
        int fi = (int)faction;
        if (fi >= 0 && fi < _factionCulture.Length)
            _factionCulture[fi] = culture;
    }

    /// <summary>
    /// Get the culture byte for a faction (0 = none / Era 1).
    /// </summary>
    public static byte GetFactionCulture(Faction faction)
    {
        int fi = (int)faction;
        if (fi >= 0 && fi < _factionCulture.Length)
            return _factionCulture[fi];
        return Cultures.None;
    }

    /// <summary>
    /// Assign a color from the pool to a faction.
    /// Called from the lobby when players choose colors.
    /// </summary>
    public static void SetFactionColor(int factionIndex, int colorIndex)
    {
        if (factionIndex >= 0 && factionIndex < _factionToColor.Length)
            _factionToColor[factionIndex] = Mathf.Clamp(colorIndex, 0, ColorPool.Length - 1);
    }

    /// <summary>
    /// Get which color pool index is assigned to a faction.
    /// </summary>
    public static int GetColorIndex(int factionIndex)
    {
        if (factionIndex >= 0 && factionIndex < _factionToColor.Length)
            return _factionToColor[factionIndex];
        return 0;
    }

    /// <summary>
    /// Reset all faction-to-color mappings to defaults.
    /// </summary>
    public static void ResetToDefaults()
    {
        _factionToColor = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 };
        _factionCulture = new byte[8]; // Clear all culture overrides
    }

    /// <summary>
    /// Get the primary color for a faction (uses configured mapping).
    /// </summary>
    public static Color Get(Faction f)
    {
        int fi = (int)f;

        // Standard pool lookup — culture choice does NOT override player color
        if (fi >= 0 && fi < _factionToColor.Length)
        {
            int ci = _factionToColor[fi];
            if (ci >= 0 && ci < ColorPool.Length)
                return ColorPool[ci];
        }
        // Creature/neutral fallback
        if (f == Faction.White) return new Color(1f, 1f, 1f, 1f);
        return ColorPool[0];
    }

    /// <summary>
    /// Alpha-tinted version for "revealed but not visible" (ghost) cases in fog of war.
    /// </summary>
    public static Color Ghost(Color baseColor, float alpha = 0.55f)
    {
        baseColor.a = Mathf.Clamp01(alpha);
        return baseColor;
    }

    /// <summary>
    /// Get a darker/desaturated version for UI backgrounds or shadows.
    /// </summary>
    public static Color GetDark(Faction f, float darkenFactor = 0.3f)
    {
        var c = Get(f);
        return new Color(
            c.r * darkenFactor,
            c.g * darkenFactor,
            c.b * darkenFactor,
            c.a
        );
    }

    /// <summary>
    /// Get the display name for a faction's currently assigned color.
    /// </summary>
    public static string GetColorName(Faction f)
    {
        int fi = (int)f;
        if (fi >= 0 && fi < _factionToColor.Length)
        {
            int ci = _factionToColor[fi];
            if (ci >= 0 && ci < ColorNames.Length)
                return ColorNames[ci];
        }
        return "Unknown";
    }

    /// <summary>
    /// Get faction name as a display string.
    /// </summary>
    public static string GetName(Faction f)
    {
        return f.ToString();
    }
}
