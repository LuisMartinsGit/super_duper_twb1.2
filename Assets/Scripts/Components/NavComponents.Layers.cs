// NavComponents.Layers.cs
// Dirty-tile tracking, the generation counter, and the M5 rampart layer:
// traversal profiles, wall portals and gate runtime state.
// Split out of NavComponents.cs (2026-08-12): that file had grown to 35
// unrelated declarations across seven milestones. Global namespace, matching
// the project's ECS-component convention.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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
