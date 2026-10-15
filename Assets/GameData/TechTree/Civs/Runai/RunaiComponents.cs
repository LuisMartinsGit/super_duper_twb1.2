// RunaiComponents.cs
// The simulation vocabulary of the 2026-10-13 Runai redesign
// (docs/Design/Age_1_Runai.md): depots and their stock, caravans and their
// trader-warriors, the Runai buildings' identity tags and the per-unit state
// the Runai combat specials keep. Global namespace, like every ECS component.

using Unity.Entities;
using Unity.Mathematics;

// ── Buildings ───────────────────────────────────────────────────────────

/// <summary>Thessara's Crossing — the Runai landmark and grand market (§2, §3.3).</summary>
public struct ThessarasCrossingTag : IComponentData { }

/// <summary>The Grazing Grounds — the Runai mounted trainer (§4.4).</summary>
public struct GrazingGroundsTag : IComponentData { }

/// <summary>A Runai Sanctuary, built atop a veilstone outcrop (§5).</summary>
public struct RunaiSanctuaryTag : IComponentData { }

/// <summary>The outcrop under a Sanctuary, resolved once when it is built.</summary>
public struct SanctuaryState : IComponentData
{
    public Entity Outcrop;
    /// <summary>Fractional religion points not yet paid.</summary>
    public float RpCarry;
}

/// <summary>
/// A Runai depot: an extractor whose output is held as stock until a caravan
/// carries it to a market (§3.1). Added to the Trading Post (Gatherer's Hut),
/// the Mine and the Sanctuary of a Runai faction by RunaiTradeSystem.
/// </summary>
public struct RunaiDepot : IComponentData
{
    public float Supplies, Iron, Veilstone, Veilsteel;
    /// <summary>Counts down to the next caravan replacement.</summary>
    public float CaravanTimer;
    /// <summary>Counts down to the next escort replacement.</summary>
    public float WarriorTimer;

    public float Total => Supplies + Iron + Veilstone + Veilsteel;
}

// ── Units ───────────────────────────────────────────────────────────────

/// <summary>Every unit a Runai faction fields from its own rosters.</summary>
public struct RunaiUnitTag : IComponentData { }

public enum CaravanLeg : byte { Loading = 0, ToMarket = 1, Returning = 2 }

/// <summary>A caravan's trip (§3.2). The entity also carries CaravanTag.</summary>
public struct RunaiCaravan : IComponentData
{
    public Entity Depot;
    public Entity Market;
    public float Supplies, Iron, Veilstone, Veilsteel;
    public CaravanLeg Leg;
    /// <summary>Seconds spent at the depot on the current load.</summary>
    public float Waited;
    /// <summary>The caravan's speed before Waystations, the Caravan Master and Swift Wheels.</summary>
    public float BaseSpeed;
    /// <summary>Seconds of Caravan of Swords left (armed and toughened).</summary>
    public float Armed;
    /// <summary>Seconds before this wagon may pay another Waystation tax (§3.2).</summary>
    public float TaxCooldown;
    /// <summary>Hunker Down: seconds left stopped and armoured; 0 = not hunkered.</summary>
    public float Hunker;
    /// <summary>1 once Hunker Down has fired this time below the threshold; re-armed above it.</summary>
    public byte HunkerSpent;
    /// <summary>The road path the caravan is walking (RoadGraphSystem), and where on it.</summary>
    public int PathId;
    public int PathIndex;
    /// <summary>Progress watchdog for the current leg: the nearest it has been
    /// to its goal, seconds without getting nearer, and 1 once it has left
    /// the road for the rest of the leg (Leg + 1 marks which leg).</summary>
    public float BestDist;
    public float Stall;
    public byte OffRoad;
    public byte LegSeen;

    public float Cargo => Supplies + Iron + Veilstone + Veilsteel;
}

public enum EscortKind : byte { Guard = 0, Archer = 1, Rider = 2 }

/// <summary>A caravan escort (docs/Design/Age_1_Runai.md §3.4): rides with one
/// caravan of its depot. While The Call holds it (§8.1) it is controllable and
/// ignores its caravan; when the Call ends it walks back.</summary>
public struct RunaiEscort : IComponentData
{
    public Entity Depot;
    public Entity Caravan;
    public EscortKind Kind;
    /// <summary>Seconds of The Call left; 0 = not called.</summary>
    public float Called;
    /// <summary>1 while walking back to its caravan after a Call.</summary>
    public byte Returning;
}

/// <summary>A flying unit (§6.4): ignores pathfinding, targetable only by
/// ranged units and ballistas.</summary>
public struct FlyingTag : IComponentData { }

/// <summary>The Runai ranged trainer (§4).</summary>
public struct ArrowyardTag : IComponentData { }

/// <summary>A trader-warrior (§3.4): escorts one caravan of its depot.</summary>
public struct RunaiTraderWarrior : IComponentData
{
    public Entity Depot;
    public Entity Caravan;
}

/// <summary>Runai_CaravanMaster — one per faction (§6.1).</summary>
public struct CaravanMasterTag : IComponentData { }

/// <summary>Runai_WarWagon (§6.4). Also carries WarRigTag so the War Rig's
/// boarding machinery carries its squad.</summary>
public struct WarWagonTag : IComponentData { }

/// <summary>Runai_HorseArcher — gains FiresOnMove with Parthian Shot.</summary>
public struct HorseArcherTag : IComponentData { }

/// <summary>A decoy rider conjured by the Wayfinder's Mirage.</summary>
public struct MirageRiderTag : IComponentData { }

/// <summary>Per-unit memory the Runai specials read: how long the unit has
/// stood still (Wagon Laager) and how long since it last lost health (Remounts).</summary>
public struct RunaiUnitClock : IComponentData
{
    public float3 LastPos;
    public int LastHp;
    public float Still;
    public float Calm;
}
