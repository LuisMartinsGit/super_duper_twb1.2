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
//
// Location: Assets/Scripts/Core/Components/NavComponents.cs

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

// ==================== M2: Spatial hash + steering ====================

/// <summary>
/// M2 — uniform-grid spatial hash of unit positions, rebuilt every tick by
/// <c>SpatialHashRebuildSystem</c>. The hash maps a packed integer cell key
/// (see <see cref="PackKey"/>) to the entities whose XZ centres lie in that
/// cell. Shared by <c>SteeringSystem</c> (S7) and -- in later milestones --
/// formation movement (S8).
///
/// CCD: <see cref="Map"/> is allocated with <see cref="Allocator.Persistent"/>
/// and disposed in <c>SpatialHashRebuildSystem.OnDestroy</c>. The map is
/// <c>Clear()</c>ed every tick and re-populated via a single-thread
/// <see cref="Unity.Jobs.IJob"/>; multi-thread inserts on a
/// <c>NativeParallelMultiHashMap</c> are not deterministic without
/// per-bucket locks (DR-2), so M2 takes the simpler single-thread route.
///
/// <see cref="CellSize"/> matches the unit-avoidance neighbourhood
/// (<see cref="DefaultCellSize"/> = 2 world units = ~4 swordsman radii).
/// <see cref="BucketCount"/> is the requested capacity at last rebuild.
/// </summary>
public struct NavSpatialHash : IComponentData
{
    /// <summary>Multimap from packed cell key to entity. Keys produced by
    /// <see cref="PackKey"/> against the unit's <c>LocalTransform.Position</c>.</summary>
    public NativeParallelMultiHashMap<int, Entity> Map;
    /// <summary>World units per spatial-hash cell. Matches the steering
    /// neighbour ring radius (3x3 = ~6 world units around each unit).</summary>
    public float CellSize;
    /// <summary>The map's capacity at the most recent rebuild. Used by the
    /// rebuild system to grow capacity when the unit count climbs.</summary>
    public int BucketCount;
    /// <summary>Bumped every tick the hash is rebuilt. Steering reads this
    /// to detect "did the hash get refreshed this tick" sanity-checks.</summary>
    public int Generation;

    /// <summary>Default cell size (m). Tuned for ~0.5 m radius units; the
    /// 3x3 neighbour ring covers a 6 m square which is ~10 unit diameters.</summary>
    public const float DefaultCellSize = 2f;

    /// <summary>
    /// Pack an (x, z) integer cell into a single int hash key. Uses the
    /// well-known "interleaved bit-mix" with prime offsets so adjacent
    /// cells aren't collision-clustered. Deterministic: identical (x, z)
    /// produces identical bits across every machine.
    /// </summary>
    public static int PackKey(int cellX, int cellZ)
    {
        unchecked
        {
            // Multiplicative + additive combine. XOR was the original
            // choice but XOR is symmetric: (cellX*A) ^ (cellZ*B) collides
            // between (a, b) and (-a, -b) when the two products are bit-
            // wise complements (caught by M2 SpatialHashBucketTests on the
            // 3x3 ring around the origin). Addition with unchecked wrap
            // preserves determinism, preserves purity, and the chosen
            // primes guarantee no collisions in any 3x3 ring of cells.
            return cellX * 73856093 + cellZ * 19349663;
        }
    }

    /// <summary>
    /// World-to-cell helper. Floors X/Z by <see cref="CellSize"/>. Matches
    /// the math used by the steering job and the populate job so all three
    /// agree on which bucket a unit lives in.
    /// </summary>
    public static void WorldToCell(in Unity.Mathematics.float3 worldPos, float cellSize,
        out int cellX, out int cellZ)
    {
        cellX = (int)Unity.Mathematics.math.floor(worldPos.x / cellSize);
        cellZ = (int)Unity.Mathematics.math.floor(worldPos.z / cellSize);
    }
}

/// <summary>
/// M2 -- per-unit final desired direction after steering force blending.
/// Written by <c>SteeringSystem</c>; preferred by <c>MovementSystem</c>
/// over <see cref="FlowDesiredDir"/> when present and valid. Falls back
/// to <see cref="FlowDesiredDir"/> -> NavMesh corridor when steering has
/// nothing to say (no neighbours + no flow yet).
///
/// Force-accumulation order is LOCKED at the writer (DR-1):
///   separation -> unit-avoidance -> obstacle-avoidance -> cohesion -> flow blend.
/// Consumers should not re-order or post-process this vector.
/// </summary>
public struct SteeringDesiredDir : IComponentData
{
    /// <summary>XZ-plane unit vector (y == 0). Length == 1 when
    /// <see cref="HasValue"/> != 0; length == 0 when no steering input
    /// applied this tick.</summary>
    public Unity.Mathematics.float3 Value;
    /// <summary>1 when <see cref="Value"/> holds a valid direction this tick.</summary>
    public byte HasValue;
}

// ==================== M3: Portal graph (HPA* abstraction) ====================

/// <summary>
/// M3 -- a single portal node in the hierarchical portal graph.
/// Stored in <see cref="PortalGraphBlob.Nodes"/> as a <see cref="Unity.Entities.BlobArray{T}"/>.
///
/// A portal is a single walkable cell at the boundary between two
/// adjacent <see cref="NavGridSingleton"/> tiles. M3 only emits the
/// <see cref="PortalKindInterTile"/> kind; M5 adds climb / gateGround
/// / gateRampart kinds in this same struct shape.
/// </summary>
public struct PortalNode
{
    /// <summary>Stable portal-id (== blob node index). Used for A* tie-break.</summary>
    public int Id;
    /// <summary>Row-major cell index in the layer-0 cost slab
    /// (z * Width + x). Lets consumers reverse-resolve to int2 / world.</summary>
    public int CellIndex;
    /// <summary>Index of the tile this portal sits ON (the portal cell's tile).
    /// Tiles are numbered tileZ * TilesX + tileX in row-major.</summary>
    public int TileIndex;
    /// <summary>One of <see cref="PortalKindInterTile"/> / <c>PortalKindClimb</c> /
    /// <c>PortalKindGateGround</c> / <c>PortalKindGateRampart</c>. M3 only emits 0.</summary>
    public byte PortalKind;
    /// <summary>Owner faction bits (M5 uses for gates). M3 leaves at 0.</summary>
    public int OwnerId;
    /// <summary>task-112 M5 -- layer this portal cell lives on. 0 = Ground,
    /// 1 = Rampart. M3 portals (KindInterTile) default to 0; the cost
    /// field had only one layer before M5 so existing reads still see
    /// the layer-0 cells. M5 climb / gate portals split their two
    /// endpoints into separate <see cref="PortalNode"/>s -- one per
    /// layer -- linked by a <see cref="PortalEdge"/> that crosses
    /// layers.</summary>
    public byte Layer;

    /// <summary>M3-only: portal at a tile-tile boundary (no special semantics).</summary>
    public const byte KindInterTile = 0;
    /// <summary>M5: climb portal between ground and rampart layers.</summary>
    public const byte KindClimb = 1;
    /// <summary>M5: gate portal at ground level (inside/outside).</summary>
    public const byte KindGateGround = 2;
    /// <summary>M5: gate portal at rampart level (across the gatehouse roof).</summary>
    public const byte KindGateRampart = 3;
}

/// <summary>
/// M3 -- a directed edge in the portal graph CSR. Edges are stored in
/// <see cref="PortalGraphBlob.Edges"/>; each node's run starts at
/// <c>PortalGraphBlob.NodeFirstEdge[node]</c> and ends at
/// <c>NodeFirstEdge[node + 1]</c> (the sentinel last entry equals
/// <c>Edges.Length</c>).
///
/// Within a node's run, edges are sorted by <see cref="ToPortalId"/>
/// ascending (DR-5).
/// </summary>
public struct PortalEdge
{
    /// <summary>Source portal node index. Redundant with the CSR slot
    /// position but kept for assertion / debug.</summary>
    public int FromPortalId;
    /// <summary>Target portal node index.</summary>
    public int ToPortalId;
    /// <summary>Octile cost (cardinal=10, diagonal=14, integer Manhattan
    /// distance for intra-tile flood). Capped at <c>ushort.MaxValue</c>.</summary>
    public ushort Cost;
    /// <summary>Bitmask of traversal profiles this edge admits. M3 leaves at
    /// 0xFF (all profiles); M5 narrows per portal kind.</summary>
    public byte ProfileMask;
}

/// <summary>
/// M3 -- BlobAsset wrapping the entire portal graph as CSR arrays
/// (<see cref="NodeFirstEdge"/> offsets + flat <see cref="Edges"/>).
/// Built once by <see cref="TheWaningBorder.Systems.Navigation.PortalGraphBuildSystem"/>
/// at world init; M4 incrementally rebuilds it.
///
/// All arrays are deterministically ordered:
///   * <see cref="Nodes"/> by (tileIndex asc, cellIndex asc).
///   * <see cref="NodeFirstEdge"/> indexed by portal id (asc by construction).
///   * <see cref="Edges"/> within a node's run by ToPortalId asc (DR-5).
/// </summary>
public struct PortalGraphBlob
{
    /// <summary>One slot per portal, by node index.</summary>
    public BlobArray<PortalNode> Nodes;
    /// <summary>Flat edge list (CSR). Length == sum of out-degrees.</summary>
    public BlobArray<PortalEdge> Edges;
    /// <summary>Length == Nodes.Length + 1. <c>NodeFirstEdge[i]</c> is the
    /// start of node i's outgoing edges in <see cref="Edges"/>;
    /// <c>NodeFirstEdge[i + 1]</c> is one past the last. The trailing
    /// sentinel equals <c>Edges.Length</c>.</summary>
    public BlobArray<int> NodeFirstEdge;
    /// <summary>Grid metadata duplicated into the blob so consumers don't
    /// need to also look up <see cref="NavGridSingleton"/>.</summary>
    public int TileSize;
    /// <summary>Number of tiles along X.</summary>
    public int TilesX;
    /// <summary>Number of tiles along Z.</summary>
    public int TilesZ;
}

/// <summary>
/// M3 singleton holding the <see cref="PortalGraphBlob"/>. Owned by
/// <see cref="TheWaningBorder.Systems.Navigation.PortalGraphBuildSystem"/>;
/// disposed in its <c>OnDestroy</c>. The <see cref="Generation"/> counter
/// is bumped on every successful swap (CCD-5) so requesters can detect
/// stale paths.
/// </summary>
public struct PortalGraphSingleton : IComponentData
{
    public BlobAssetReference<PortalGraphBlob> Graph;
    /// <summary>Bumped every time a new graph blob is published.</summary>
    public int Generation;
    /// <summary>1 once a blob has been built at least once. 0 means
    /// <see cref="Graph"/> is the default uninitialised reference.</summary>
    public byte Built;

    /// <summary>Locked tile-size for M3..M7 per CCD-4.</summary>
    public const int TileSize = 16;
}

// ==================== M3: Path requests + results ====================

/// <summary>
/// M3 -- request for an abstract A* path on the portal graph. Emitted by
/// <see cref="TheWaningBorder.Systems.Navigation.MoveCommandHelper"/> /
/// migrated <see cref="TheWaningBorder.Core.Commands.Types.MoveCommandHelper"/>
/// or any future per-unit pathing caller. Consumed by
/// <see cref="TheWaningBorder.Systems.Navigation.AbstractPathfinderSystem"/>.
///
/// One request per emit; the system removes the component after it
/// writes the matching <see cref="NavPathResult"/>.
/// </summary>
public struct NavPathRequest : IComponentData
{
    /// <summary>Start cell on the cost field (typically the unit's current
    /// cell). Layer-0 for M3.</summary>
    public int2 StartCell;
    /// <summary>Goal cell on the cost field. Layer-0 for M3.</summary>
    public int2 GoalCell;
    /// <summary>Traversal profile index (M5 indexes <c>TraversalProfileBlob</c>).
    /// M3 ignores this and uses a single all-admitting profile (0).</summary>
    public byte ProfileHash;
    /// <summary>Stamped at emit time. The pathfinder rejects requests whose
    /// generation doesn't match the current graph generation (CCD-5).</summary>
    public int Generation;
    /// <summary>Status: 0 pending, 1 success, 2 unreachable, 3 generation
    /// mismatch.</summary>
    public byte Status;

    public const byte StatusPending = 0;
    public const byte StatusSuccess = 1;
    public const byte StatusUnreachable = 2;
    public const byte StatusStale = 3;
}

/// <summary>
/// M3 -- result of an abstract A* path. The dynamic buffer
/// <see cref="NavPathPortal"/> on the same entity carries the ordered
/// sequence of portal node ids the path traverses.
/// </summary>
public struct NavPathResult : IComponentData
{
    /// <summary>Number of portals along the path (== buffer length when
    /// <see cref="Status"/> == <see cref="NavPathRequest.StatusSuccess"/>).</summary>
    public int Length;
    /// <summary>Mirror of <see cref="NavPathRequest.Status"/> after solve.</summary>
    public byte Status;
    /// <summary>Generation of the portal graph the path was solved
    /// against. Consumers check this against the current
    /// <see cref="PortalGraphSingleton.Generation"/> to invalidate stale
    /// paths after graph swaps.</summary>
    public int Generation;
    /// <summary>Cursor into the portal buffer -- next portal the unit is
    /// walking toward. Updated by <c>FlowSegmentSystem</c> /
    /// <c>FlowFollowSystem</c> as the unit advances.</summary>
    public int CurrentPortalIndex;
}

/// <summary>
/// M3 -- dynamic buffer of portal node ids along an abstract A* path.
/// One element per portal traversed (head = start cell's tile-portal,
/// tail = goal cell's tile-portal). Buffer length matches
/// <see cref="NavPathResult.Length"/>.
/// </summary>
[InternalBufferCapacity(8)]
public struct NavPathPortal : IBufferElementData
{
    public int PortalId;
}

// ==================== M3: Flow cache ====================

/// <summary>
/// M3 -- key for the per-tile flow cache. Encodes the tile, the exit
/// portal the flow is integrated AWAY from, and the traversal-profile
/// hash. Hash function is integer-only:
///   <c>hash = (TileIndex &lt;&lt; 16) | (ExitPortalId &lt;&lt; 8) | ProfileHash</c>.
/// </summary>
public struct NavFlowCacheKey : System.IEquatable<NavFlowCacheKey>
{
    public int TileIndex;
    public int ExitPortalId;
    public byte ProfileHash;

    public bool Equals(NavFlowCacheKey other) =>
        TileIndex == other.TileIndex
        && ExitPortalId == other.ExitPortalId
        && ProfileHash == other.ProfileHash;

    public override bool Equals(object obj) => obj is NavFlowCacheKey k && Equals(k);

    public override int GetHashCode()
    {
        unchecked
        {
            // Deterministic integer-only hash per the architecture's M3
            // CCD: (TileIndex << 16) | (ExitPortalId << 8) | ProfileHash.
            return (TileIndex << 16) ^ (ExitPortalId << 8) ^ ProfileHash;
        }
    }
}

/// <summary>
/// M3 -- one slab of the flow cache. <see cref="DirOffset"/> /
/// <see cref="IntegrationOffset"/> point into the shared
/// <see cref="NavFlowCache.DirPool"/> / <see cref="NavFlowCache.IntegrationPool"/>.
/// Slab size is <see cref="PortalGraphSingleton.TileSize"/> ^ 2 cells.
/// </summary>
public struct NavFlowCacheSlot
{
    /// <summary>Byte offset of this slab's start in the dir pool.</summary>
    public int DirOffset;
    /// <summary>Byte offset (in <c>uint</c> elements) of this slab's start
    /// in the integration pool.</summary>
    public int IntegrationOffset;
    /// <summary>Tick (or monotonic counter) the slab was last hit. Used by
    /// LRU eviction. Deterministic: bumped only inside sim ticks.</summary>
    public int LastUsedTick;
    /// <summary>1 when the slab holds a valid build; 0 means the slot is
    /// free.</summary>
    public byte Valid;
}

/// <summary>
/// M3 singleton -- LRU cache of per-tile segmented flow fields.
/// Pool sizing per architecture's M3 section (256 slabs * 16x16 cells * 1B
/// dir + 4B integration ~= 320 KB resident). Allocated at startup, never
/// grows.
///
/// Slabs are keyed by <see cref="NavFlowCacheKey"/> (tile + exit portal +
/// profile). On a cache miss, a slab is allocated from the free list
/// (or evicted from the LRU); on a hit, <see cref="NavFlowCacheSlot.LastUsedTick"/>
/// is bumped.
/// </summary>
public struct NavFlowCache : IComponentData
{
    /// <summary>Hashmap from key to slot index in <see cref="Slots"/>.</summary>
    public NativeHashMap<NavFlowCacheKey, int> SlotIndex;
    /// <summary>Slab metadata. Length == <see cref="SlotCount"/>.</summary>
    public NativeArray<NavFlowCacheSlot> Slots;
    /// <summary>Slab key reverse-lookup (slot index -> key). Length ==
    /// <see cref="SlotCount"/>; valid only when <c>Slots[i].Valid != 0</c>.
    /// Used by LRU eviction to remove the displaced entry from
    /// <see cref="SlotIndex"/>.</summary>
    public NativeArray<NavFlowCacheKey> SlotKeys;
    /// <summary>Shared flat pool for direction bytes. Size ==
    /// <see cref="SlotCount"/> * tileArea bytes.</summary>
    public NativeArray<byte> DirPool;
    /// <summary>Shared flat pool for integration uints. Size ==
    /// <see cref="SlotCount"/> * tileArea uints.</summary>
    public NativeArray<uint> IntegrationPool;
    /// <summary>Fixed slab count. M3 ships 256.</summary>
    public int SlotCount;
    /// <summary>Tile-area cells per slab (TileSize * TileSize).</summary>
    public int TileArea;
    /// <summary>Monotonic tick counter. Bumped each sim tick by
    /// <c>FlowSegmentSystem</c>. Used as <see cref="NavFlowCacheSlot.LastUsedTick"/>
    /// on hit / fill.</summary>
    public int TickCounter;

    /// <summary>Fixed slab pool size (M3). Architecture-locked.</summary>
    public const int DefaultSlotCount = 256;
}

// ==================== M4: Dirty-tile tracking + generation counter ====================

/// <summary>
/// M4 -- singleton tracking which 16x16 nav tiles are dirty this tick.
/// Populated by <c>BuildingCostStampSystem</c> when a building footprint
/// changes the underlying cost cells; drained by
/// <c>IncrementalPortalRebuildSystem</c> after it rebuilds those tiles'
/// portals + invalidates their cache slabs.
///
/// <see cref="DirtyTileIndices"/> is a <see cref="NativeHashSet{T}"/>
/// (per architecture's M4 section + DR-6 risk row). Hash-set iteration
/// is non-deterministic by default; readers snapshot it into a
/// <see cref="NativeList{T}"/> and sort ascending before consuming.
/// <see cref="Generation"/> is bumped every time the dirty set is drained,
/// so cache consumers can detect "did the dirty set change since I
/// last looked".
///
/// Allocator.Persistent; disposed in <c>BuildingCostStampSystem.OnDestroy</c>
/// (the owner of the dirty set).
/// </summary>
public struct NavDirtyTiles : IComponentData
{
    /// <summary>Tile indices (tileZ * tilesX + tileX) that need a
    /// portal-graph rebuild + cache-slab invalidation this tick.</summary>
    public NativeHashSet<int> DirtyTileIndices;
    /// <summary>Bumped every drain. Lets stale readers detect they
    /// missed a generation and force a full refresh.</summary>
    public int Generation;
}

/// <summary>
/// M4 -- monotonic counter the portal-graph swap protocol (CCD-5) uses
/// to stamp each request with the graph generation it was solved against.
/// The pathfinder rejects requests whose <see cref="NavPathRequest.Generation"/>
/// doesn't match <see cref="CurrentGeneration"/>; downstream caches
/// invalidate slabs whose generation doesn't match <see cref="CommittedGeneration"/>.
///
/// Lives on its own singleton entity so allocation / disposal is
/// independent of the cost field's lifetime.
/// </summary>
public struct NavGenerationCounter : IComponentData
{
    /// <summary>The generation of the graph as PUBLISHED.</summary>
    public int CurrentGeneration;
    /// <summary>The generation of the graph as observed by the LAST
    /// completed swap (after Dispose of the old blob). For M4 this
    /// equals <see cref="CurrentGeneration"/> -- single-tick swap.</summary>
    public int CommittedGeneration;
}

// ==================== M5: Rampart layer + traversal profiles + gates ====================

/// <summary>
/// task-112 M5 -- per-unit layer index. 0 = Ground, 1 = Rampart.
/// Attached at unit-factory creation (every unit gets one). Read by
/// <see cref="TheWaningBorder.Systems.Navigation.UnitIntegratorSystem"/>
/// for height snap (ground = terrain, rampart = <c>DeckY</c>).
/// Updated by <c>LayerTransitionSystem</c> when a unit completes a climb /
/// gate-rampart traversal.
/// </summary>
public struct NavLayerIndex : IComponentData
{
    /// <summary>Current layer (0 = Ground, 1 = Rampart). Default 0.</summary>
    public byte Layer;

    public const byte LayerGround = 0;
    public const byte LayerRampart = 1;
    public const int LayerCount = 2;
}

/// <summary>
/// task-112 M5 -- a single traversal profile. Lives in
/// <see cref="TraversalProfileBlob"/> as a <see cref="BlobArray{T}"/>;
/// per-unit profile slot picked by <see cref="NavTraversalProfile.ProfileId"/>.
/// All fields integer / byte / int so the profile lookup is
/// machine-deterministic.
/// </summary>
public struct TraversalProfile
{
    /// <summary>Footprint cells along one axis (1, 2, 3...). M5 uses 1 for
    /// every shipped profile; M6 introduces larger footprints.</summary>
    public byte FootprintSize;
    /// <summary>Bitmask of admissible layers. Bit 0 = Ground, bit 1 =
    /// Rampart. <c>0x01</c> = ground-only, <c>0x02</c> = rampart-only,
    /// <c>0x03</c> = both layers.</summary>
    public byte AllowedLayersMask;
    /// <summary>1 = unit may use climb portals (stairs / wall doors). 0 =
    /// no climb access (siege, mounted before dismount, etc.).</summary>
    public byte CanClimb;
    /// <summary>Owner faction id (matches <see cref="Faction"/> enum value
    /// cast to int). -1 = "any owner" (e.g. neutral profiles).</summary>
    public int OwnerId;
    /// <summary>Per-terrain-class cost multiplier as Q8 fixed-point
    /// (multiplier = value / 256). M5 ships length 4 with default 256
    /// (1.0 multiplier) for every class.</summary>
    public BlobArray<byte> TerrainCostMultipliers;
}

/// <summary>
/// task-112 M5 -- blob asset holding every traversal profile shipped at
/// world init. Indexed by <see cref="NavTraversalProfile.ProfileId"/>.
/// M5 ships 3 profiles (DefaultGround=0, DefaultRampart=1, Climbable=2).
/// </summary>
public struct TraversalProfileBlob
{
    public BlobArray<TraversalProfile> Profiles;
}

/// <summary>
/// task-112 M5 -- singleton holding the traversal profile blob. Built
/// once at world init by <c>TraversalProfileBootstrapSystem</c>;
/// disposed in its <c>OnDestroy</c>.
/// </summary>
public struct TraversalProfileSingleton : IComponentData
{
    public BlobAssetReference<TraversalProfileBlob> Profiles;

    /// <summary>Default ground-only profile (LayerMask=0x01, CanClimb=1).
    /// Used by every plain ground unit before climb-aware promotion.</summary>
    public const byte ProfileDefaultGround = 0;
    /// <summary>Rampart-only profile (LayerMask=0x02). For units that
    /// spawn directly on a wall deck (garrison spawns, etc.).</summary>
    public const byte ProfileDefaultRampart = 1;
    /// <summary>Climbable profile (LayerMask=0x03, CanClimb=1). The
    /// default for swordsmen, archers, and any infantry that should be
    /// able to climb stairs and patrol parapets.</summary>
    public const byte ProfileClimbable = 2;
}

/// <summary>
/// task-112 M5 -- per-unit traversal profile reference. Stores only the
/// blob index so the per-unit component stays 4 bytes.
/// </summary>
public struct NavTraversalProfile : IComponentData
{
    /// <summary>Index into <see cref="TraversalProfileBlob.Profiles"/>.
    /// Default 0 (DefaultGround).</summary>
    public byte ProfileId;
}

/// <summary>
/// task-112 M5 -- singleton holding the per-tick output of
/// <c>WallPortalDetectionSystem</c>. The list is rebuilt every tick a
/// wall structural change is detected; consumers (portal-graph build
/// + incremental rebuild) read it as the seed list of wall-derived
/// portals to append to the graph blob.
///
/// Allocator.Persistent; disposed in
/// <c>WallPortalDetectionSystem.OnDestroy</c>.
/// </summary>
public struct WallPortalSpecList : IComponentData
{
    /// <summary>Sorted by source entity index asc (DR-10) before
    /// consumption.</summary>
    public NativeList<WallPortalSpec> Specs;
    /// <summary>Bumped every refill so consumers can detect "did the
    /// wall topology change since I last rebuilt".</summary>
    public int Generation;
}

/// <summary>
/// task-112 M5 -- one wall-derived portal candidate. Produced by
/// <c>WallPortalDetectionSystem</c> at world init / structural change;
/// consumed by the next portal-graph build pass (in M5 the build still
/// runs on a single-thread main-thread path -- the structural-change
/// integration happens at world init only). Lists each climb / gate
/// portal so the graph build can append it to the inter-tile portal
/// node list.
///
/// Sort key for the consumer is <c>(SourceEntity.Index asc)</c> (DR-10)
/// so portal node indices stay deterministic across machines.
/// </summary>
public struct WallPortalSpec
{
    /// <summary>Portal kind: <see cref="PortalNode.KindClimb"/>,
    /// <see cref="PortalNode.KindGateGround"/>, or
    /// <see cref="PortalNode.KindGateRampart"/>.</summary>
    public byte Kind;
    /// <summary>Cell on the source side (Ground for climb / gate-ground;
    /// Rampart for gate-rampart).</summary>
    public int2 SourceCell;
    /// <summary>Layer of the source cell (0 = ground, 1 = rampart).</summary>
    public byte SourceLayer;
    /// <summary>Cell on the target side (Rampart for climb; Ground other
    /// side of the gatehouse for gate-ground; Rampart other side for
    /// gate-rampart).</summary>
    public int2 TargetCell;
    /// <summary>Layer of the target cell.</summary>
    public byte TargetLayer;
    /// <summary>Owner faction id (-1 = any). Used by gate portals;
    /// climb portals ship with OwnerId = -1.</summary>
    public int OwnerId;
    /// <summary>The structural entity (wall stair / wall door / wall
    /// gate) this portal was emitted from. Used as the deterministic
    /// tie-break key + as the runtime handle for gate state.</summary>
    public Entity SourceEntity;
}

/// <summary>
/// task-112 M5 -- per-gate runtime state. One per <c>WallGateTag</c>
/// entity that has been registered with the portal graph. Toggled by
/// <c>GateStateSystem</c>; consumed by <c>AbstractPathfinderSystem</c>
/// (via the mutable owner-bits mirror) and <c>LayerTransitionSystem</c>
/// (as a backstop eligibility check).
///
/// <see cref="LastChangedTick"/> bumps every time
/// <see cref="OpenState"/> flips -- consumers can detect "did the gate
/// change since I last sampled".
/// </summary>
public struct GateRuntimeState : IComponentData
{
    /// <summary>Entity id of the gate (== the source <c>WallGateTag</c>
    /// instance entity).</summary>
    public int GateEntityId;
    /// <summary>1 = open (admissible by owner faction), 0 = closed.</summary>
    public byte OpenState;
    /// <summary>Owner faction id (matches <see cref="Faction"/> enum value
    /// cast to int). Used by <c>AbstractPathfinderSystem</c> /
    /// <c>LayerTransitionSystem</c> to reject ineligible units.</summary>
    public int OwnerId;
    /// <summary>Portal node id (in <see cref="PortalGraphSingleton.Graph"/>)
    /// of this gate's GROUND-layer portal. -1 until
    /// <c>WallGateRegistrationSystem</c> resolves it after the next
    /// graph rebuild.</summary>
    public int PortalNodeGround;
    /// <summary>Portal node id of this gate's RAMPART-layer portal.</summary>
    public int PortalNodeRampart;
    /// <summary>Monotonic tick the open state last flipped. Sim-tick
    /// driven, not wall-clock.</summary>
    public uint LastChangedTick;
}

/// <summary>
/// task-112 M5 -- mirror of per-portal-node owner-bits that
/// <c>GateStateSystem</c> mutates without rebuilding the portal-graph
/// blob (the blob is structural; gate open/close is per-tick state).
/// Length matches <see cref="PortalGraphBlob.Nodes"/>; reads happen in
/// <c>AbstractPathfinderSystem</c> and <c>LayerTransitionSystem</c>.
///
/// Bit layout per slot (ushort):
///   * bit 0..6 -- owner faction id (mirrors <see cref="GateRuntimeState.OwnerId"/>)
///   * bit 15   -- open/closed (1 = open)
///   * bits 7..14 reserved.
/// Non-gate portals carry <c>0x8000</c> (open + neutral owner) so the
/// pathfinder treats them as freely traversable.
/// </summary>
public struct PortalOwnerBitsMirror : IComponentData
{
    /// <summary>Parallel to <see cref="PortalGraphBlob.Nodes"/>. Length
    /// == Nodes.Length. Allocator.Persistent; reallocated on every
    /// graph swap to match the new node count.</summary>
    public NativeArray<ushort> Bits;
    /// <summary>Graph generation this mirror was built against. Stale
    /// mirrors are rebuilt by <c>WallGateRegistrationSystem</c> on
    /// generation mismatch.</summary>
    public int Generation;

    /// <summary>Bit-15 of the mirror entry: 1 = portal is OPEN /
    /// admissible by its owner.</summary>
    public const ushort BitOpen = 0x8000;
    /// <summary>Owner faction mask (lo 7 bits). 0x7F = "neutral / any
    /// owner" sentinel.</summary>
    public const ushort OwnerMask = 0x007F;
    /// <summary>Sentinel "any owner" stored in the owner field.</summary>
    public const ushort OwnerAny = 0x007F;

    /// <summary>Pack an (owner, open) pair into a mirror slot value.</summary>
    public static ushort Pack(int ownerId, bool open)
    {
        ushort ownerBits;
        if (ownerId < 0) ownerBits = OwnerAny;
        else ownerBits = (ushort)(ownerId & OwnerMask);
        return (ushort)(ownerBits | (open ? BitOpen : 0));
    }

    /// <summary>Owner faction id stored in a mirror slot. Returns -1 for
    /// the OwnerAny sentinel.</summary>
    public static int UnpackOwner(ushort slot)
    {
        int owner = slot & OwnerMask;
        if (owner == OwnerAny) return -1;
        return owner;
    }

    /// <summary>True when the slot's open bit is set.</summary>
    public static bool UnpackOpen(ushort slot) => (slot & BitOpen) != 0;
}

/// <summary>
/// task-112 M5 -- per-unit transient component tracking a climb / gate
/// portal traversal in flight. Added by <c>LayerTransitionSystem</c>
/// when the unit arrives at a layer-changing portal; removed once
/// <see cref="Progress"/> reaches 1.0.
///
/// During traversal the unit's <see cref="Unity.Transforms.LocalTransform.Position"/>
/// is animated by <c>LayerTransitionSystem</c> -- the integrator
/// (<c>UnitIntegratorSystem</c>) skips units holding this component so
/// the two systems don't fight.
///
/// Deterministic: <see cref="Progress"/> is integrated by
/// <c>state.WorldUnmanaged.Time.DeltaTime</c> (fixed step in the sim
/// group); no wall-clock reads.
/// </summary>
public struct LayerTraversalState : IComponentData
{
    /// <summary>1 = traversal active. 0 = component is being removed
    /// this tick (set when Progress >= 1).</summary>
    public byte InProgress;
    /// <summary>Layer the unit is leaving (0 ground / 1 rampart).</summary>
    public byte FromLayer;
    /// <summary>Layer the unit is entering.</summary>
    public byte ToLayer;
    /// <summary>Portal node id (in the current portal graph) being
    /// traversed. Used by the backstop eligibility check.</summary>
    public int PortalId;
    /// <summary>Normalised progress 0..1. Integrated each tick at
    /// <c>TransitionRate * dt</c>. At 0.5 the layer flips
    /// (<see cref="ToLayer"/> takes effect on <see cref="NavLayerIndex"/>).</summary>
    public float Progress;
    /// <summary>World-space start of the traversal animation (the cell
    /// the unit entered the portal at).</summary>
    public float3 StartPos;
    /// <summary>World-space end of the traversal animation (the cell on
    /// the other side of the portal).</summary>
    public float3 EndPos;
}

// ==================== M6: Request scheduler + extended flow + formations ====================

/// <summary>
/// task-112 M6 -- one entry in the <see cref="NavRequestQueueSingleton"/>
/// pending-request queue. Created by callers (chiefly
/// <c>MoveCommandHelper</c>) when they want a unit to receive a path,
/// drained by <c>NavRequestSchedulerSystem</c> which sorts the queue
/// + coalesces duplicate (goal, profile) pairs + releases up to
/// <see cref="NavRequestQueueSingleton.DefaultMaxRequestsPerTick"/>
/// entries per tick to the pathfinder.
///
/// Sort order is LOCKED at (Priority asc, EnqueueTick asc,
/// Requester.Index asc) -- DR-12 / scheduler contract. Reordering
/// breaks lockstep determinism.
/// </summary>
public struct PendingNavRequest : System.IEquatable<PendingNavRequest>
{
    /// <summary>Entity that wants the path. Used as the deterministic
    /// tie-break key.</summary>
    public Entity Requester;
    /// <summary>Start cell on the cost field. Snapshotted at enqueue
    /// time so the scheduler can dispatch even if the unit moves
    /// before its slot comes up.</summary>
    public int2 StartCell;
    /// <summary>Goal cell on the cost field.</summary>
    public int2 GoalCell;
    /// <summary>Traversal profile hash (matches
    /// <see cref="NavPathRequest.ProfileHash"/>). Used by the
    /// coalescing key.</summary>
    public byte ProfileHash;
    /// <summary>Priority: lower = sooner. Sorted ascending. User-issued
    /// moves use <see cref="PriorityUser"/>; AI / re-routes use
    /// <see cref="PriorityNormal"/>; opportunistic / formation members
    /// use <see cref="PriorityFormation"/>.</summary>
    public byte Priority;
    /// <summary>Sim-tick the request was enqueued (matches
    /// <c>NavRequestQueueSingleton.CurrentTick</c> at enqueue).
    /// Secondary sort key.</summary>
    public uint EnqueueTick;
    /// <summary>Graph generation observed at enqueue. The scheduler
    /// drops requests whose generation is stale (CCD-5).</summary>
    public int Generation;

    /// <summary>Priority for direct user move orders.</summary>
    public const byte PriorityUser = 0;
    /// <summary>Priority for AI / re-issued requests.</summary>
    public const byte PriorityNormal = 1;
    /// <summary>Priority for formation members following a leader.</summary>
    public const byte PriorityFormation = 2;

    public bool Equals(PendingNavRequest other) =>
        Requester == other.Requester
        && StartCell.Equals(other.StartCell)
        && GoalCell.Equals(other.GoalCell)
        && ProfileHash == other.ProfileHash
        && Priority == other.Priority
        && EnqueueTick == other.EnqueueTick
        && Generation == other.Generation;

    public override bool Equals(object obj) => obj is PendingNavRequest r && Equals(r);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = Requester.Index * 397;
            h = (h ^ GoalCell.x) * 397;
            h = (h ^ GoalCell.y) * 397;
            h = (h ^ ProfileHash) * 397;
            return h;
        }
    }
}

/// <summary>
/// task-112 M6 -- coalescing key for the scheduler. Two pending
/// requests with the same key map to the same A* run; the scheduler
/// solves once and broadcasts the result to every requester in the
/// equivalence class.
///
/// Hash is integer-only: packs goal cell + profile into a single int.
/// </summary>
public struct NavRequestCoalesceKey : System.IEquatable<NavRequestCoalesceKey>
{
    public int2 GoalCell;
    public byte ProfileHash;

    public bool Equals(NavRequestCoalesceKey other) =>
        GoalCell.Equals(other.GoalCell) && ProfileHash == other.ProfileHash;

    public override bool Equals(object obj) => obj is NavRequestCoalesceKey k && Equals(k);

    public override int GetHashCode()
    {
        unchecked
        {
            // Pack (gx, gz, profile) into a 32-bit hash. Goal coords
            // fit in 13 bits each at 8192-cell maps; profile in 8 bits.
            return (GoalCell.x * 73856093) ^ (GoalCell.y * 19349663) ^ (ProfileHash * 83492791);
        }
    }
}

/// <summary>
/// task-112 M6 -- singleton holding the per-tick navigation request
/// scheduler state. Owned by <c>NavRequestSchedulerSystem</c>;
/// allocated <see cref="Allocator.Persistent"/> + disposed in the
/// system's <c>OnDestroy</c>.
///
/// Producers (chiefly <c>MoveCommandHelper.Execute</c>) push entries
/// onto <see cref="Pending"/> via the helper on
/// <c>NavRequestQueueSingleton</c>; the scheduler drains the queue in
/// sorted order each tick, coalesces duplicate (goal, profile)
/// entries, and emits up to <see cref="MaxRequestsPerTick"/>
/// <see cref="NavPathRequest"/> components via ECB.
/// </summary>
public struct NavRequestQueueSingleton : IComponentData
{
    /// <summary>Pending request list. Sort order is locked at
    /// (Priority asc, EnqueueTick asc, Requester.Index asc); the
    /// scheduler enforces the order on each tick before dispatch.</summary>
    public NativeList<PendingNavRequest> Pending;
    /// <summary>Per-tick budget. Default
    /// <see cref="DefaultMaxRequestsPerTick"/>.</summary>
    public int MaxRequestsPerTick;
    /// <summary>Number of requests released this tick. Bumped during
    /// dispatch; reset on each <c>OnUpdate</c> entry.</summary>
    public int ReleasedThisTick;
    /// <summary>Monotonic sim-tick counter (incremented at scheduler
    /// <c>OnUpdate</c> entry). Used as the <see cref="PendingNavRequest.EnqueueTick"/>
    /// stamp and as the secondary sort key.</summary>
    public uint CurrentTick;

    /// <summary>Default per-tick release budget (DR-12). Sized larger
    /// than the M3 pathfinder budget so the scheduler can saturate the
    /// pathfinder on a mass-move tick.</summary>
    public const int DefaultMaxRequestsPerTick = 16;
}

// ==================== M7: Determinism replay log ====================

/// <summary>
/// task-112 M7 -- one recorded snapshot in the determinism replay log.
/// Uses integer MILLIMETRE coordinates (<see cref="PositionMillimeters"/>)
/// instead of floats so the byte-identical comparison can't be defeated
/// by float ULP drift across machines / Burst versions (DR-15). One
/// snapshot per (sim-tick, entity-index) pair; the log is a flat
/// append-only <see cref="NativeList{T}"/>.
///
/// Sort order: ascending <see cref="Tick"/>, then ascending
/// <see cref="EntityIndex"/> -- mirrors the order the recorder writes
/// them in (chunk-walk visits archetypes in stable order, the recorder
/// sorts within a tick by entity index before append).
/// </summary>
public struct UnitPositionSnapshot : System.IEquatable<UnitPositionSnapshot>
{
    /// <summary>Sim tick this snapshot was taken at (monotonic).</summary>
    public uint Tick;
    /// <summary>Entity index of the unit (Entity.Index). Stable within a
    /// world per the lockstep contract.</summary>
    public int EntityIndex;
    /// <summary>Position in MILLIMETRES (1mm = 0.001 world units). int3
    /// so the byte comparison is exact regardless of float
    /// representation. Range: +-2^31 mm = +- 2.14 million km, well
    /// beyond any plausible map size.</summary>
    public int3 PositionMillimeters;

    /// <summary>Conversion factor: 1 world unit = 1000 millimetres.</summary>
    public const int MillimetersPerUnit = 1000;

    /// <summary>Round-to-nearest float -> int conversion.</summary>
    public static int3 ToMillimeters(float3 worldPos) => new int3(
        (int)math.round(worldPos.x * MillimetersPerUnit),
        (int)math.round(worldPos.y * MillimetersPerUnit),
        (int)math.round(worldPos.z * MillimetersPerUnit));

    /// <summary>Inverse of <see cref="ToMillimeters"/>. Editor diagnostic
    /// only -- the sim never reads from the snapshot directly.</summary>
    public static float3 FromMillimeters(int3 mm) => new float3(
        mm.x / (float)MillimetersPerUnit,
        mm.y / (float)MillimetersPerUnit,
        mm.z / (float)MillimetersPerUnit);

    public bool Equals(UnitPositionSnapshot other) =>
        Tick == other.Tick
        && EntityIndex == other.EntityIndex
        && PositionMillimeters.Equals(other.PositionMillimeters);

    public override bool Equals(object obj) => obj is UnitPositionSnapshot s && Equals(s);

    public override int GetHashCode()
    {
        unchecked
        {
            int h = (int)Tick * 397;
            h = (h ^ EntityIndex) * 397;
            h = (h ^ PositionMillimeters.x) * 397;
            h = (h ^ PositionMillimeters.y) * 397;
            h = (h ^ PositionMillimeters.z) * 397;
            return h;
        }
    }
}

/// <summary>
/// task-112 M7 -- singleton owning the per-tick replay log. One entry
/// per (tick, unit) pair; the log grows monotonically while
/// <see cref="GameSettings.NavReplayMode"/> is <see cref="NavReplayMode.Record"/>
/// or <see cref="NavReplayMode.Replay"/>.
///
/// Allocator.Persistent (DR-17); disposed in
/// <c>DeterminismReplaySystem.OnDestroy</c>. The buffer is intentionally
/// NOT cleared on world tear-down via any other path -- the singleton's
/// OnDestroy is the only legitimate dispose site.
///
/// The recorder writes in (tick asc, entityIndex asc) order; the
/// replayer reads the same range it wrote previously and compares
/// byte-for-byte via <see cref="UnitPositionSnapshot.Equals"/>.
/// </summary>
public struct DeterminismReplayLog : IComponentData
{
    /// <summary>Append-only buffer of position snapshots. Allocator.Persistent.</summary>
    public NativeList<UnitPositionSnapshot> Log;
    /// <summary>Current tick the recorder will write into next (bumped at
    /// the end of each recorded tick). Replayer's cursor for the
    /// next comparison.</summary>
    public uint CurrentTick;
    /// <summary>Index into <see cref="Log"/> the replayer's next
    /// comparison starts at. Lets the comparator linear-scan a single
    /// tick's worth of entries instead of binary-searching the whole
    /// log.</summary>
    public int ReplayCursor;
    /// <summary>1 once at least one tick has been recorded. Lets the
    /// system distinguish "fresh log" from "log used for replay".</summary>
    public byte HasData;
    /// <summary>Bumped every divergence the replayer detects. 0 in the
    /// happy-path; > 0 means the sim has diverged from the recorded log
    /// and the editor should halt.</summary>
    public int DivergenceCount;
}
