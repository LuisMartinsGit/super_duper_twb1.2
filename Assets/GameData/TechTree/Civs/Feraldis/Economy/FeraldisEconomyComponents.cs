// FeraldisEconomyComponents.cs
// The Feraldis economy's ECS vocabulary — slaves, crews, errands.
// Canon: docs/Design/Age_1_Feraldis.md §3-5 (2026-10-12).

using Unity.Entities;

/// <summary>
/// A Feraldis slave: the Worker after age-up (converted in place by
/// FeraldisCultureRetrofitSystem) or a Feraldis_Slave trained at the
/// Fortress. Still a builder (keeps WorkerTag / CanBuild); counts against the
/// slave cap, not the army population; may staff an extractor or the Great
/// Forge, feed the Soulforge, and be sacrificed in the field.
/// </summary>
public struct SlaveTag : IComponentData { }

/// <summary>The Overseer — slaves near it work faster (§3.3). One per faction.</summary>
public struct OverseerTag : IComponentData { }

/// <summary>
/// A slave assigned to work at <see cref="Building"/> (a Work Camp, Mine,
/// Veilstone Mine or the Great Forge). It counts as crew while it stands
/// within the crew radius of that building; ordered away, it drops the
/// assignment (FeraldisCrewSystem).
/// </summary>
public struct StaffAssignment : IComponentData
{
    public Entity Building;
}

/// <summary>
/// Computed once a second on every staffable building of a Feraldis faction:
/// how many slaves crew it and the overseer bonus they get. Read by the
/// territory income tick (extractors) and the Great Forge (bays).
/// </summary>
public struct ExtractorCrew : IComponentData
{
    public int Count;
    public int Slots;
    /// <summary>Crew-weighted bonus from Overseers in range and the Chains research, ≥ 1.</summary>
    public float WorkMultiplier;
}

/// <summary>What a walk-to-and-act errand does when the unit arrives.</summary>
public enum FeraldisErrandKind : byte
{
    Staff = 1,       // slave → extractor / Forge: become crew
    MeltDown = 2,    // soldier → Great Forge: melted for a share of its cost
    Sacrifice = 3,   // any unit → Blood Altar: killed for religion points
    Board = 4,       // infantry → War Rig: ride inside
}

/// <summary>
/// A unit walking to <see cref="Building"/> to do <see cref="Kind"/> there.
/// FeraldisErrandSystem performs it on arrival and drops it if the unit is
/// re-ordered elsewhere.
/// </summary>
public struct FeraldisErrand : IComponentData
{
    public Entity Building;
    public FeraldisErrandKind Kind;
    /// <summary>Seconds the unit has been standing still without arriving.</summary>
    public float StuckSeconds;
}

/// <summary>
/// Every Feraldis-only order that travels as one lockstep command
/// (LockstepCommandType.FeraldisOrder). The arg rides above the low byte.
/// </summary>
public enum FeraldisOp : byte
{
    None = 0,
    Staff = 1,           // actor: slave, target: extractor or Great Forge
    MeltDown = 2,        // actor: soldier, target: Great Forge
    Sacrifice = 3,       // actor: unit, target: Blood Altar
    FieldSacrifice = 4,  // actor: slave — dies where it stands, leaves blood
    ForgeAddBay = 5,     // actor: Great Forge
    ForgeSetRecipe = 6,  // actor: Great Forge, arg: bay * 8 + recipe
    ForgeUpgradeBay = 7, // actor: Great Forge, arg: bay
    TotemRite = 8,       // actor: War Totem, arg: rite
    RigLoad = 9,         // actor: infantry unit, target: War Rig
    RigUnload = 10,      // actor: War Rig
    Overcharge = 11,     // actor: War Rig (with the Overcharge tech)
    SanctuaryTrade = 12, // actor: Runai Sanctuary — RP for veilsteel (Age_1_Runai.md §5.1)
}
