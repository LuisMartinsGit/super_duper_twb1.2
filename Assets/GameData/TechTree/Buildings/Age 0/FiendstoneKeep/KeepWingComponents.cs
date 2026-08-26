// KeepWingComponents.cs
// Fiendstone Keep wing system (choice-building leveling, design 2026-07-04):
// the Keep levels up by building WINGS — the player chooses up to three of
// six wing types, each at most once. See docs/Design/Age_0.md.

using Unity.Entities;

/// <summary>The six Keep wing types. None = empty slot.</summary>
public enum KeepWingType : byte
{
    None       = 0,
    War        = 1, // train Barracks / Archery Range / Stable units
    Civic      = 2, // Supplies trickle, trains Workers
    Engineers  = 3, // three ballista emplacements + more HP
    Economic   = 4, // gatherer-hut-like income (larger area)
    Librarians = 5, // Hall techs researchable here + global research speed
    Temple     = 6, // sect unit training (v1: Litharch) + 1 RP on build
}

/// <summary>
/// Shared lookups for choice-building level/wing effects. Managed-side
/// (creates throwaway queries) — call from non-Burst system code only, at
/// event cadence (research start, heal tick, income rebuild), not per frame
/// per entity.
/// </summary>
public static class ChoiceUpgradeQuery
{
    /// <summary>Highest BuildingUpgradeState.Level across the faction's Shrines/Temples (0 when none).</summary>
    public static int MaxShrineLevel(EntityManager em, Faction faction)
    {
        int best = 0;
        var q = em.CreateEntityQuery(
            ComponentType.ReadOnly<ShrineTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<BuildingUpgradeState>());
        Best(em, q, faction, ref best);
        var qt = em.CreateEntityQuery(
            ComponentType.ReadOnly<TempleOfRidanTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<BuildingUpgradeState>());
        Best(em, qt, faction, ref best);
        return best;
    }

    /// <summary>Highest BuildingUpgradeState.Level across the faction's Vaults (0 when none).</summary>
    public static int MaxVaultLevel(EntityManager em, Faction faction)
    {
        int best = 0;
        var q = em.CreateEntityQuery(
            ComponentType.ReadOnly<VaultTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<BuildingUpgradeState>());
        Best(em, q, faction, ref best);
        return best;
    }

    /// <summary>Does the faction own a Keep with the given completed wing?</summary>
    public static bool FactionHasWing(EntityManager em, Faction faction, KeepWingType wing)
    {
        var q = em.CreateEntityQuery(
            ComponentType.ReadOnly<KeepWings>(),
            ComponentType.ReadOnly<FactionTag>());
        using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < ents.Length; i++)
        {
            if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
            if (em.GetComponentData<KeepWings>(ents[i]).Has(wing)) return true;
        }
        return false;
    }

    private static void Best(EntityManager em, EntityQuery q, Faction faction, ref int best)
    {
        using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < ents.Length; i++)
        {
            if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
            int lv = em.GetComponentData<BuildingUpgradeState>(ents[i]).Level;
            if (lv > best) best = lv;
        }
    }
}
