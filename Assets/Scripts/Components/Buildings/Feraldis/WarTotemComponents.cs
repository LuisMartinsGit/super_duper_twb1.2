// Canon: docs/Design/Age_1_Feraldis.md — "Blood, Frenzy & War Totems",
// and Curse_And_Shardroot.md §2.6 (per-culture influence anchors).

using Unity.Entities;

/// <summary>
/// Feraldis War Totem — the culture's territory engine. Feraldis buildings
/// project no civic influence; the claim comes from totems planted on blood.
/// </summary>
public struct WarTotemTag : IComponentData { }

/// <summary>
/// Blood the totem has drunk and banked, permanently. Drives both the
/// influence rate and radius (see FeraldisConstants.TotemInfluence*).
///
/// Banking matters: blood inside player influence FADES (BloodMap
/// .DecayInsideInfluence, §2.5b rev.3), so a totem that merely projected
/// influence over its own pool would erase the thing feeding it. Drinking
/// converts the pool into Fervor before the decay can take it — which is
/// also exactly the "feedable, non-decaying" totem §2.6 asks for.
/// </summary>
/// <summary>
/// On a friendly unit standing inside a War Totem's aura. Added and removed by
/// WarTotemAuraSystem as units enter and leave, so combat systems can read a
/// flat bonus instead of re-scanning totems per attack.
/// </summary>
public struct TotemAuraBuff : IComponentData
{
    /// <summary>Fractional attack bonus, e.g. 0.20 = +20 %.</summary>
    public float AttackBonus;
}

public struct TotemFervor : IComponentData
{
    /// <summary>Banked blood, 0..FeraldisConstants.TotemFervorMax.</summary>
    public float Value;

    /// <summary>Seconds until the next drink pulse.</summary>
    public float DrinkTimer;
}
