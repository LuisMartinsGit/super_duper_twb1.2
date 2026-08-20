// NavComponents.Portals.cs
// Portal graph (M3, the HPA* abstraction) plus the path request/result pair
// and the flow cache the solved legs land in.
// Split out of NavComponents.cs (2026-08-12): that file had grown to 35
// unrelated declarations across seven milestones. Global namespace, matching
// the project's ECS-component convention.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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
