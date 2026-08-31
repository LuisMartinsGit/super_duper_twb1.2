// CultureConfig.cs
// Culture palette and metadata for Era 2 age-up system

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
    /// Balance 2026-07: reduced 30% (was 1000/200/150) alongside the
    /// choice-building cost cut — see docs/Design/Age_0.md and
    /// TechTree.json (Research_Era2: 700 S + 140 I + 105 V).
    /// Veilstone is back in the gate (2026-07-25 techtree pass): the AI
    /// veil-mines inside its base tether, so the old Age-0 stall no longer applies.
    /// </summary>
    // 250/100/0 (2026-08-29; was 700/140/105). Two reasons, both design:
    //   * The age-up is EARLY TECH, and early tech is supplies + iron under
    //     the resource-domain rule (Regions.md) - the 105 veilstone violated
    //     it, and with a starting bank of 0 veilstone it also chained the
    //     age-up behind mid-game mining infrastructure. On Veilmarch, where
    //     veilstone is centre-only, that stretched Age 0 (a one-unit melee
    //     age) past minute 15 in most matches.
    //   * Median age-up time target is 3-6 minutes by difficulty
    //     (Age_0.md): from a 400-supply start, Shrine (257) + this must be
    //     reachable inside that window against ~72-120 supplies/min.
    public static readonly Cost AgeUpCost = Cost.Of(supplies: 250, iron: 100);

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

    // ==================== Age-up completion state ====================
    // FactionColors.SetFactionCulture fires at CLICK time (the popup commits
    // it so unit tints preview immediately); the ECS FactionProgress.Culture
    // on the Hall is only written by AgeUpSystem when the research COMPLETES.
    // Anything gated on "age-up is done" (UI palette, building influence)
    // must read these helpers, not FactionColors.

    // These helpers run per-frame from managed code (placement checks, the
    // prefab swap scan), so the queries must be cached — CreateEntityQuery
    // per call is the query-registry leak behind the old FPS decay.
    //
    // LAZY on purpose: ComponentType.ReadOnly<T>() asks the ECS TypeManager
    // for a type index. As a static FIELD initializer it ran inside this
    // class's cctor at whatever moment the class was first touched — in the
    // player that was the skirmish lobby (SkirmishPanel's own cctor), before
    // the TypeManager was ready, and the TypeInitializationException killed
    // the lobby. Built on first use instead: every caller passes a live
    // EntityManager, which guarantees the TypeManager is up by then.
    private static Unity.Entities.ComponentType[] _completedCultureTypes;
    private static TheWaningBorder.Core.CachedEntityQuery _completedCultureQuery;

    private static Unity.Entities.ComponentType[] _ageUpProgressTypes;
    private static TheWaningBorder.Core.CachedEntityQuery _ageUpProgressQuery;

    /// <summary>
    /// The faction's culture as committed by a COMPLETED age-up
    /// (Hall FactionProgress). Cultures.None while Age 0 or mid-research.
    /// </summary>
    public static byte GetCompletedCulture(Unity.Entities.EntityManager em, Faction faction)
    {
        if (em.Equals(default(Unity.Entities.EntityManager))) return Cultures.None;
        _completedCultureTypes ??= new[] {
            Unity.Entities.ComponentType.ReadOnly<HallTag>(),
            Unity.Entities.ComponentType.ReadOnly<FactionTag>(),
            Unity.Entities.ComponentType.ReadOnly<FactionProgress>() };
        var q = _completedCultureQuery.Get(em, _completedCultureTypes);
        using var tags = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
        using var prog = q.ToComponentDataArray<FactionProgress>(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < tags.Length; i++)
            if (tags[i].Value == faction) return prog[i].Culture;
        return Cultures.None;
    }

    /// <summary>
    /// True while the faction's Hall carries an in-progress AgeUpState.
    /// progress01 = completed fraction (0..1); culture = the pending pick.
    /// </summary>
    public static bool TryGetAgeUpProgress(Unity.Entities.EntityManager em, Faction faction,
        out float progress01, out byte culture)
    {
        progress01 = 0f;
        culture = Cultures.None;
        if (em.Equals(default(Unity.Entities.EntityManager))) return false;
        _ageUpProgressTypes ??= new[] {
            Unity.Entities.ComponentType.ReadOnly<HallTag>(),
            Unity.Entities.ComponentType.ReadOnly<FactionTag>(),
            Unity.Entities.ComponentType.ReadOnly<AgeUpState>() };
        var q = _ageUpProgressQuery.Get(em, _ageUpProgressTypes);
        using var tags = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
        using var states = q.ToComponentDataArray<AgeUpState>(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i].Value != faction) continue;
            var s = states[i];
            progress01 = s.Duration > 0f ? Mathf.Clamp01(1f - s.Remaining / s.Duration) : 0f;
            culture = s.Culture;
            return true;
        }
        return false;
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
    /// True if the culture is currently locked behind a "Coming Soon"
    /// gate and cannot be adopted by the player. Single source of truth
    /// for the IMGUI popup, the web HUD overlay, and the age-up guards.
    ///
    /// Ship gate (2026-08-09): the first build shipped ONE culture, so Runai
    /// and Feraldis were both locked and Alanthor was the only choice.
    ///
    /// FERALDIS RE-ENABLED (2026-08-18) after its mechanic audit: the culture
    /// is wired end to end — frenzy-on-blood, the BloodMap, bleeding, warpath,
    /// raider camps and the plunder ladder, batch training, the pop override,
    /// the Mine and the Corruptor ritual all run (see
    /// docs/Design/Age_1_Feraldis.md). Both the player's age-up choice and the
    /// AI's pick read this test, so dropping it here opens the culture on both
    /// sides at once. Runai stays locked — its trade-lane core is still stubbed.
    ///
    /// Drop a culture from this test to ship it.
    /// </summary>
    public static bool IsComingSoon(byte culture)
        => culture == Cultures.Runai || culture == Cultures.Feraldis;

    /// <summary>
    /// Coerces a desired culture to one this build actually ships.
    ///
    /// Anything that PICKS a culture rather than reading one — the AI's
    /// culture choice in particular — must run its pick through here. The
    /// player is blocked by <see cref="IsComingSoon"/> at the UI, so without
    /// this an AI would happily age up into a culture the build does not
    /// ship and field content the player can never see or counter.
    /// </summary>
    public static byte Playable(byte culture)
    {
        if (!IsComingSoon(culture)) return culture;
        if (!IsComingSoon(Cultures.Alanthor)) return Cultures.Alanthor;
        if (!IsComingSoon(Cultures.Feraldis)) return Cultures.Feraldis;
        if (!IsComingSoon(Cultures.Runai)) return Cultures.Runai;
        return Cultures.Alanthor; // every culture gated — keep the game running.
    }

    /// <summary>
    /// Get description for a culture.
    /// </summary>
    public static string GetDescription(byte culture)
    {
        return (culture >= 0 && culture < Descriptions.Length) ? Descriptions[culture] : "";
    }
}
