// NavComponents.cs
// ECS components for the new (M1-M7) deterministic flow-field navigation
// stack. All components live in the global namespace to match the project's
// existing ECS-component convention (see CoreComponents.cs / UnitComponents.cs
// / BuildingComponents.cs / NavMeshComponents.cs).
//
// M1 introduces:
//   - NavGridSingleton (read-only world dims)
//   - NavCostField     (byte cost + byte flags, owned by NavGridBootstrapSystem)
//   - (M3 deleted NavFlowFieldM1; flow direction now lives in NavFlowCache slabs)
//   - DirectionTableBlob (256-entry unit-vector lookup, built once)
//   - FlowDesiredDir   (per-unit desired direction written by FlowFollowSystem)
//   - (M3 deleted NavFlowGoalRequest; MoveCommandHelper now emits NavPathRequest)
//
// Later phases extend this file. M1 keeps it minimal so phase 2-7 additions
// drop in alongside without churning the existing types.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

// ==================== Singletons ====================

/// <summary>
/// Immutable description of the navigation grid. Created once by
/// <c>NavGridBootstrapSystem</c> on world init; never mutated after. World
/// position of cell (x, z) on layer 0 is
/// <c>Origin + new float3((x + 0.5f) * CellSize, 0, (z + 0.5f) * CellSize)</c>.
/// </summary>
public struct NavGridSingleton : IComponentData
{
    /// <summary>Number of cells along the X axis.</summary>
    public int Width;
    /// <summary>Number of cells along the Z axis.</summary>
    public int Height;
    /// <summary>World units per cell (M1 uses 1).</summary>
    public float CellSize;
    /// <summary>World position of the cell-(0,0) corner on layer 0.</summary>
    public float3 Origin;
    /// <summary>Number of layers. M1 = 1 (ground only); M5 grows to 2.</summary>
    public int LayerCount;
}

/// <summary>
/// Per-cell traversal cost + flags. Row-major within a layer, layer-major
/// across layers (see CCD-2). Sentinels on the cost byte:
///   <list type="bullet">
///   <item>0       — nominal walkable</item>
///   <item>1..200  — weighted walkable (terrain blend, unused in M1)</item>
///   <item>254     — conditional passable (gate cell — M5)</item>
///   <item>255     — impassable</item>
///   </list>
/// The companion <see cref="Flags"/> byte stores per-cell tags
/// (<c>IsBuildingFootprint</c> etc.) in the high nibble. M1 uses only
/// <c>IsBuildingFootprint = 0x10</c>.
///
/// Allocated with <see cref="Allocator.Persistent"/> by
/// <c>NavGridBootstrapSystem.OnCreate</c>; disposed in its <c>OnDestroy</c>.
/// </summary>
public struct NavCostField : IComponentData
{
    public NativeArray<byte> Cost;
    public NativeArray<byte> Flags;
    /// <summary>
    /// task-terrain-traversability — baked layer-0 terrain mask. One byte
    /// per ground cell (length == Width * Height, layer-0 only): <c>0</c>
    /// for walkable terrain, <see cref="CostImpassable"/> for cells the
    /// terrain itself blocks (deep water, slopes over the design's incline
    /// budget). Baked ONCE by <c>TerrainCostBakeSystem</c> from the
    /// terrain-derived <c>PassabilityGrid</c>, then used as the per-tick
    /// CLEAR value for layer-0 (<c>ClearLayer0Job</c>) so building / obstacle
    /// / wall stamps land on top of real terrain instead of flat ground.
    ///
    /// Allocated (ClearMemory → all-walkable) alongside <see cref="Cost"/> in
    /// <c>NavGridBootstrapSystem</c>; disposed in its OnDestroy. Stays
    /// all-zero on terrain-less scenes (nav-stack test scenarios), so those
    /// behave exactly as before the bake existed.
    /// </summary>
    public NativeArray<byte> TerrainCost;
    /// <summary>
    /// 1 once <c>TerrainCostBakeSystem</c> has baked the terrain mask into
    /// <see cref="TerrainCost"/>. Folded into the stamp-change signature so the
    /// (otherwise change-gated) <c>CostFieldStampSystem</c> re-runs once when
    /// terrain becomes available and seeds it into layer-0.
    /// </summary>
    public byte TerrainBaked;
    public int Width;
    public int Height;
    public int LayerCount;
    /// <summary>Bumped every time the cost field is restamped. Consumers
    /// can compare to detect a rebuild without scanning the array.</summary>
    public int Generation;

    /// <summary>Cost-byte sentinel: cell is impassable.</summary>
    public const byte CostImpassable = 255;
    /// <summary>Cost-byte sentinel: cell is conditionally passable (e.g. gate).</summary>
    public const byte CostConditional = 254;

    /// <summary>
    /// Cost weight for cells that are only crossable via a bridge DECK (the
    /// ground beneath is cliff / NoWalk — see PassabilityGrid's deck-only
    /// mask). Planning treats them as walkable but expensive, so ground
    /// routes prefer real terrain and only genuine bridge crossings pay the
    /// premium; the movement integrator enforces "deck height only" when a
    /// unit actually steps in. Flow-field integration additionally only
    /// connects these cells to the rest of the map through
    /// <see cref="CostBridgeMount"/> cells, so a route can never enter the
    /// deck strip from its side at ground level.
    /// </summary>
    public const byte CostBridgeDeckOnly = 60;

    /// <summary>
    /// Marker weight for walkable cells where a bridge deck touches down
    /// (deck within step-up reach of the ground — ramp toes). These are the
    /// ONLY legal flow-field entrances/exits of a CostBridgeDeckOnly strip.
    /// Small non-zero value so the marker survives the bake while barely
    /// influencing route cost.
    /// </summary>
    public const byte CostBridgeMount = 5;

    /// <summary>Flag-byte bit: cell is inside a building's footprint.</summary>
    // Low 3 bits (0x07) of Flags encode the owner faction for cells whose
    // Cost == CostConditional (254). LOS / obstacle-avoidance checks read
    // this to decide whether the gate is passable for the unit's own
    // faction; non-owner units treat the cell as impassable. Higher 4 bits
    // are the existing flag-bit set (FlagBuildingFootprint, FlagGate, etc.).
    public const byte FlagOwnerMask = 0x07;

    /// <summary>Flag-byte bit: cell was stamped impassable by the Veil crust
    /// (<c>VeilNavStampSystem</c>), NOT by a building / wall / terrain. Kept
    /// distinct so the stamp system can cleanly REVERT a cell to its terrain
    /// baseline when the crust recedes (mined out / decayed) without touching
    /// cells that are impassable for a structural reason. Occupies the free
    /// bit between <see cref="FlagOwnerMask"/> (0x07) and
    /// <see cref="FlagBuildingFootprint"/> (0x10).</summary>
    public const byte FlagCrust = 0x08;

    public const byte FlagBuildingFootprint = 0x10;
    /// <summary>Flag-byte bit: cell is a climb-access point (M5).</summary>
    public const byte FlagClimbAccess = 0x20;
    /// <summary>Flag-byte bit: cell is a gate (M5).</summary>
    public const byte FlagGate = 0x40;
    /// <summary>Flag-byte bit: cell is a static wall (M5).</summary>
    public const byte FlagStaticWall = 0x80;

    /// <summary>
    /// Row-major-within-layer / layer-major-across-layers indexing helper
    /// (matches CCD-2). Caller is responsible for bounds checks.
    /// </summary>
    public int Index(int x, int z, int layer)
    {
        return layer * (Width * Height) + z * Width + x;
    }

    /// <summary>2D convenience for the layer-0 case used everywhere in M1.</summary>
    public int Index(int x, int z) => z * Width + x;
}

/// <summary>
/// Pure-constants helper for the flow integration math. The M1
/// whole-map flow singleton was deleted in task-112 M3 -- per-tile
/// cached flow (<see cref="NavFlowCache"/>) replaced it. The constants
/// here are shared by every flow-related job (per-tile integrate,
/// direction assignment, the EditMode tests of the underlying
/// algorithm).
/// </summary>
public static class NavFlowConstants
{
    /// <summary>Sentinel direction-byte meaning "no flow vector at this cell".</summary>
    public const byte NoDirection = 255;
    /// <summary>Integration sentinel for unreachable cells.</summary>
    public const uint UnreachableIntegration = uint.MaxValue;
    /// <summary>Cardinal step cost in integration units.</summary>
    public const uint StepCardinal = 10;
    /// <summary>Diagonal step cost in integration units (octile, ~= sqrt(2) * 10).</summary>
    public const uint StepDiagonal = 14;
}

// ==================== Per-unit components ====================

/// <summary>
/// Desired-direction vector written by <c>FlowFollowSystem</c> after sampling
/// the flow field at the unit's current cell. M1's surgical edit to
/// <c>MovementSystem</c> reads this BEFORE the NavMesh corridor when present
/// and falls through to the existing NavMesh path otherwise.
///
/// <see cref="HasValue"/> = 1 means <see cref="Value"/> is a valid unit
/// vector in the XZ plane (y component held at 0).
/// </summary>
public struct FlowDesiredDir : IComponentData
{
    public float3 Value;
    public byte HasValue;
}

// ==================== Direction lookup blob ====================

/// <summary>
/// Static 256-entry direction lookup (CCD-3). Indexed by the
/// per-cell direction byte (M3 cache slabs). Entries are 2D unit vectors in
/// the XZ plane; consumers expand to <c>float3(x, 0, z)</c> when needed.
/// Built once at world init from <c>(cos, sin)(i * 2π / 256)</c>.
/// </summary>
public struct DirectionTableBlob
{
    public BlobArray<float2> Dirs;
}

/// <summary>
/// Singleton holding the <see cref="DirectionTableBlob"/>. Built once at
/// world init; disposed in <c>NavGridBootstrapSystem.OnDestroy</c>.
/// </summary>
public struct DirectionTableSingleton : IComponentData
{
    public BlobAssetReference<DirectionTableBlob> Table;
}

