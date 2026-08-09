// GoalFlowFieldSystem.cs
// Goal-centric WHOLE-MAP flow fields — the pathfinding redesign
// (directive 2026-07-05).
//
// Why: the M3-M7 hierarchical stack (portal graph + per-leg 16x16 flow
// slabs + per-unit path cursors) had no globally-correct base layer. Every
// unit-facing answer depended on a chain of fragile links (path result,
// cursor position, slab cache key), and any broken link silently degraded
// to reactive heuristics that gradient-descend on distance — which a
// concave obstacle (a "U") defeats by definition. At this game's map
// sizes (50x50 .. 256x256 cells) hierarchical decomposition is premature:
// ONE integration sweep over the whole grid per DESTINATION is microsecond
// -to-millisecond work in Burst and is correct by construction.
//
// What it does: every tick, collect the distinct (goalCell, factionIdx)
// keys of all units holding an active DesiredDestination; for keys missing
// from the cache (or stale against NavCostField.Generation), run one
// whole-map integration (label-correcting sweep from the goal, octile
// 10/14 costs, wall-clearance bias — the exact conventions of the retired
// per-tile slab integrator) and store a full-map direction-byte field.
// FlowFollowSystem then answers every unit's "which way?" with one array
// read. Formation moves need nothing special: each unit's slot destination
// is its own goal key, and slots that share a cell share a field.
//
// Faction in the key: conditional gate cells (cost 254) are walkable only
// for the owner faction encoded in the cell flags, so fields are integrated
// per faction. Group moves are single-faction, so this costs nothing in
// practice. Factionless units use sentinel 0xFF (gates closed).
//
// Unreachable is EXPLICIT: a unit whose cell the field cannot reach gets
// NoDirection — FlowFollow makes it hold position instead of grinding into
// the blocker. Goals clicked ON a blocker are snapped to the nearest
// walkable cell (bounded deterministic ring search) before integrating.
//
// Determinism: integer costs, fixed neighbour order, deterministic key
// collection (chunk-walk order), sequential Burst .Run() integration,
// LRU eviction with smallest-slot tie-break. No wall-clock, no floats in
// sim-affecting decisions (the dir byte is quantized once at bake).
//
// Location: Assets/Scripts/Systems/Navigation/GoalFlowFieldSystem.cs

using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Systems.Navigation;

// ═══════════════════════════════════════════════════════════════════════
// COMPONENTS (global namespace, matching the other Nav* components)
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Cache key: one whole-map flow field per destination BUCKET per
/// faction (gates are per-faction passable). GoalCell is QUANTIZED — see
/// GoalFlowQuant — so the per-slot destinations of one formation collapse
/// into a handful of shared fields instead of one field per unit.</summary>
public struct GoalFlowKey : IEquatable<GoalFlowKey>
{
    /// <summary>Ground variant: bridge deck-only cells are IMPASSABLE — pure
    /// ground routing (units go around cliffs, never through bridges).</summary>
    public const byte VariantGround = 0;
    /// <summary>Bridge variant: deck-only cells crossable via mount cells —
    /// used when the ground variant can't reach the unit (sealed ring, or
    /// the unit stands on a deck).</summary>
    public const byte VariantBridge = 1;

    public int2 GoalCell;     // quantized bucket coordinates, not raw cells
    public byte FactionIdx;   // 0..7, or 0xFF = no faction (gates closed)
    public byte Variant;      // VariantGround / VariantBridge

    public bool Equals(GoalFlowKey other)
        => GoalCell.Equals(other.GoalCell) && FactionIdx == other.FactionIdx
           && Variant == other.Variant;

    public override int GetHashCode()
        => (GoalCell.x * 73856093) ^ (GoalCell.y * 19349663)
           ^ (FactionIdx * 83492791) ^ (Variant * 668265263);
}

/// <summary>
/// Goal-bucket quantization shared by the field producer and the sampler
/// (the two MUST agree bit-for-bit). Buckets are ~8 m squares: fields are
/// only used to get a unit NEAR its destination (the final approach runs
/// on the LOS bearing to the exact DesiredDestination), so nearby slot
/// goals can share one field. Deterministic — pure integer math off the
/// lockstep-identical grid config.
/// </summary>
public static class GoalFlowQuant
{
    public const float BucketWorldSize = 8f;

    /// <summary>Cells per bucket edge for a given grid cell size.</summary>
    public static int CellsPerBucket(float cellSize)
    {
        int q = (int)(BucketWorldSize / cellSize + 0.5f);
        return q < 1 ? 1 : q;
    }
}

/// <summary>Per-slot metadata for one cached whole-map field.</summary>
public struct GoalFlowSlot
{
    public int DirOffset;       // into GoalFlowFieldCache.DirPool
    public int LastUsedTick;    // LRU
    public int Generation;      // NavCostField.Generation at integration time
    public int IntegratedTick;  // TickCounter at last integration (refresh throttle)
    public byte Valid;
}

/// <summary>
/// Singleton owning the pool of cached whole-map goal fields. Allocated by
/// GoalFlowFieldSystem; read by FlowFollowSystem's sampler job.
/// </summary>
public struct GoalFlowFieldCache : IComponentData
{
    public NativeHashMap<GoalFlowKey, int> SlotIndex;
    public NativeArray<GoalFlowSlot> Slots;
    public NativeArray<GoalFlowKey> SlotKeys;
    public NativeArray<byte> DirPool;          // SlotCount * CellCount
    public NativeArray<uint> IntegrationScratch; // CellCount (sequential reuse)
    public int SlotCount;
    public int CellCount;
    public int TickCounter;
}

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Computes and caches whole-map goal flow fields. Runs after the cost
    /// stamps (so integrations see this tick's world) and before
    /// FlowFollowSystem (so a freshly integrated field is sampled the same
    /// tick).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BuildingCostStampSystem))]
    [UpdateBefore(typeof(FlowFollowSystem))]
    public partial struct GoalFlowFieldSystem : ISystem
    {
        /// <summary>Cached whole-map fields. 48 distinct live destinations
        /// covers heavy multi-army play; beyond it LRU eviction recycles
        /// (a re-order simply re-integrates).</summary>
        public const int SlotCountDefault = 48;

        /// <summary>Max fields integrated per tick — keeps a mass re-order
        /// (or a stamp-generation bump invalidating everything) from
        /// spiking one frame. Units whose field is pending fall back to
        /// direct-bearing for the tick or two it takes.</summary>
        public const int MaxIntegrationsPerTick = 4;

        /// <summary>Ring radius (cells) for snapping a blocked goal cell to
        /// the nearest walkable one.</summary>
        public const int GoalSnapRadius = 8;

        /// <summary>Minimum ticks between re-integrations of a STALE (but
        /// valid) field. ~1 s at 60 fps — fields track the moving crust at
        /// that cadence instead of re-integrating every tick under
        /// Generation churn.</summary>
        public const int ReintegrateCooldownTicks = 60;

        private Entity _cacheEntity;
        private byte _initialised;
        private EntityQuery _unitQuery;

        // ── Detached (async) integration state ─────────────────────────
        // The old code scheduled the batch and Complete()d it in the same
        // update — a ~12 ms MAIN-THREAD stall on every mass move order
        // (profiler: JobHandle.Complete -> IntegrateGoalFieldJob). The
        // batch now runs DETACHED from the frame's dependency chain:
        //   * jobs read a private SNAPSHOT of the cost field, so later
        //     stamp writers never conflict with in-flight readers;
        //   * jobs write only DirPool slices of slots claimed Valid = 2,
        //     which the sampler ignores by contract (and the job's pool
        //     fields already disable container safety);
        //   * the slots flip to Valid = 1 at the top of the NEXT update —
        //     the jobs had the whole previous frame on worker threads, so
        //     the Complete() there is normally free.
        // Under GameSettings.DeterministicLockstep the synchronous path is
        // kept: field availability must not depend on frame rate there.
        private NativeArray<byte> _costSnapshot;
        private NativeArray<byte> _flagsSnapshot;
        private NativeList<int> _pendingSlots;
        private Unity.Jobs.JobHandle _pendingHandle;
        private int _pendingGeneration;

        public void OnCreate(ref SystemState state)
        {
            _initialised = 0;
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();

            _pendingSlots = new NativeList<int>(MaxIntegrationsPerTick, Allocator.Persistent);

            _unitQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, DesiredDestination, FactionTag>()
                .Build();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var cost = SystemAPI.GetSingleton<NavCostField>();
            if (!cost.Cost.IsCreated) return;

            if (_initialised == 0)
            {
                _initialised = 1;
                int cellCount = grid.Width * grid.Height;
                var cache = new GoalFlowFieldCache
                {
                    SlotIndex = new NativeHashMap<GoalFlowKey, int>(SlotCountDefault * 2, Allocator.Persistent),
                    Slots = new NativeArray<GoalFlowSlot>(SlotCountDefault, Allocator.Persistent,
                        NativeArrayOptions.ClearMemory),
                    SlotKeys = new NativeArray<GoalFlowKey>(SlotCountDefault, Allocator.Persistent,
                        NativeArrayOptions.ClearMemory),
                    DirPool = new NativeArray<byte>(SlotCountDefault * cellCount, Allocator.Persistent,
                        NativeArrayOptions.UninitializedMemory),
                    // One scratch slice per concurrent integration so the
                    // per-tick batch can run in PARALLEL on worker threads.
                    IntegrationScratch = new NativeArray<uint>(MaxIntegrationsPerTick * cellCount,
                        Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                    SlotCount = SlotCountDefault,
                    CellCount = cellCount,
                    TickCounter = 0,
                };
                _cacheEntity = em.CreateEntity(typeof(GoalFlowFieldCache));
                em.SetComponentData(_cacheEntity, cache);
                // Snapshot arrays are allocated lazily at the copy site —
                // Cost/Flags are LAYERED (Width*Height*LayerCount), NOT the
                // ground cellCount this cache works in; sizing them here at
                // cellCount threw "source and destination length must be the
                // same" every tick.
            }

            var cacheSingleton = SystemAPI.GetSingleton<GoalFlowFieldCache>();
            cacheSingleton.TickCounter++;

            if (_unitQuery.IsEmpty && _pendingSlots.Length == 0)
            {
                SystemAPI.SetSingleton(cacheSingleton);
                return;
            }

            // Drain in-flight stamp jobs before reading the cost field and
            // (below) writing the dir pool the sampler reads.
            state.Dependency.Complete();

            // Flip last tick's detached batch live once its jobs are done.
            // They ran on worker threads through the previous frame, so
            // IsCompleted is the overwhelmingly common case and the
            // Complete() is free (contrast: the old same-tick Complete
            // stalled the main thread for the full integration).
            if (_pendingSlots.Length > 0 && _pendingHandle.IsCompleted)
            {
                _pendingHandle.Complete();
                for (int i = 0; i < _pendingSlots.Length; i++)
                {
                    int pendingSlot = _pendingSlots[i];
                    var pendingMeta = cacheSingleton.Slots[pendingSlot];
                    if (pendingMeta.Valid != 2) continue; // reset meanwhile
                    pendingMeta.DirOffset = pendingSlot * cacheSingleton.CellCount;
                    pendingMeta.Generation = _pendingGeneration;
                    pendingMeta.LastUsedTick = cacheSingleton.TickCounter;
                    pendingMeta.IntegratedTick = cacheSingleton.TickCounter;
                    pendingMeta.Valid = 1;
                    cacheSingleton.Slots[pendingSlot] = pendingMeta;
                }
                _pendingSlots.Clear();
            }

            if (_unitQuery.IsEmpty)
            {
                SystemAPI.SetSingleton(cacheSingleton);
                return;
            }

            int quant = GoalFlowQuant.CellsPerBucket(grid.CellSize);

            // ── Collect distinct needed keys in deterministic order ───────
            // Pure array walks (no per-entity managed EntityManager calls —
            // this loop runs every tick over every moving unit).
            using var dests = _unitQuery.ToComponentDataArray<DesiredDestination>(Allocator.Temp);
            using var factions = _unitQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            var wanted = new NativeList<GoalFlowKey>(64, Allocator.Temp);
            var seen = new NativeHashMap<GoalFlowKey, byte>(64, Allocator.Temp);

            for (int i = 0; i < dests.Length; i++)
            {
                if (dests[i].Has == 0) continue;

                int gx = (int)math.floor((dests[i].Position.x - grid.Origin.x) / grid.CellSize);
                int gz = (int)math.floor((dests[i].Position.z - grid.Origin.z) / grid.CellSize);
                gx = math.clamp(gx, 0, grid.Width - 1);
                gz = math.clamp(gz, 0, grid.Height - 1);

                int f = (int)factions[i].Value;
                byte factionIdx = (f >= 0 && f <= 7) ? (byte)f : (byte)0xFF;

                // Which variants does this goal need?
                //   * Goal ON a bridge deck (deck-only cell): bridge variant
                //     only — the ground variant would snap the goal off the
                //     bridge and divert the order.
                //   * Otherwise: the ground variant always; plus the bridge
                //     variant when the map has bridges (consulted by units
                //     the ground variant cannot reach).
                int goalIdx = gz * grid.Width + gx;
                bool goalOnDeck = cost.Cost[goalIdx] == NavCostField.CostBridgeDeckOnly;
                bool bridges = TheWaningBorder.World.Terrain.BridgeSurface.HasAny;

                for (byte variant = 0; variant <= 1; variant++)
                {
                    if (variant == GoalFlowKey.VariantGround && goalOnDeck) continue;
                    if (variant == GoalFlowKey.VariantBridge && !bridges) continue;

                    var key = new GoalFlowKey
                    {
                        GoalCell = new int2(gx / quant, gz / quant),
                        FactionIdx = factionIdx,
                        Variant = variant,
                    };

                    if (cacheSingleton.SlotIndex.TryGetValue(key, out int slot))
                    {
                        var meta = cacheSingleton.Slots[slot];
                        if (meta.Valid != 0)
                        {
                            // Bump LRU for ANY actively wanted key — stale
                            // ones included. Under cost-field Generation
                            // churn (the veil crust stamp bumps it about
                            // once a second) every live key reads as stale
                            // most ticks; without this bump their slots
                            // decayed into eviction bait WHILE units were
                            // sampling them, and eviction yanked live
                            // fields out from under moving workers.
                            meta.LastUsedTick = cacheSingleton.TickCounter;
                            cacheSingleton.Slots[slot] = meta;

                            // Integration already in flight for this key —
                            // never double-book the slot (two detached jobs
                            // writing one DirPool slice is the exact double-
                            // assignment bug the claim state exists to stop).
                            if (meta.Valid == 2) continue;

                            if (meta.Valid == 1 && meta.Generation == cost.Generation)
                                continue; // fresh hit — nothing to integrate

                            // STALE-REFRESH THROTTLE (lag fix, 2026-07-12).
                            // The veil crust stamp bumps NavCostField.Generation
                            // about once a second while the front moves, which
                            // marked EVERY cached field stale — and this system
                            // re-integrated four whole-map fields every single
                            // tick, forever. A stale field still points at the
                            // right goal (the world shifted by a few crust
                            // cells at most), so serve it and refresh at most
                            // once per cooldown window instead of every tick.
                            // Brand-new keys (no field at all) are unaffected.
                            if (meta.Valid == 1
                                && cacheSingleton.TickCounter - meta.IntegratedTick
                                    < ReintegrateCooldownTicks)
                                continue;
                        }
                    }

                    if (!seen.ContainsKey(key))
                    {
                        seen.Add(key, 1);
                        wanted.Add(key);
                    }
                }
            }

            // ── Integrate up to the per-tick budget, in parallel ───────────
            // Each job writes a disjoint DirPool slice and owns a disjoint
            // scratch slice, so the batch fans out across worker threads;
            // the main thread only pays the sync at the end.
            int budget = math.min(wanted.Length, MaxIntegrationsPerTick);

            // Detached batches share one scratch buffer — never overlap two.
            // (A busy previous batch defers this tick's integrations; the
            // wanted keys re-collect next tick.)
            bool synchronous = GameSettings.DeterministicLockstep;
            if (!synchronous && _pendingSlots.Length > 0) budget = 0;

            if (budget > 0)
            {
                var handles = new NativeArray<Unity.Jobs.JobHandle>(budget, Allocator.Temp);
                var slots = new NativeArray<int>(budget, Allocator.Temp);

                if (!synchronous)
                {
                    // Coherent private snapshot for the detached jobs (the
                    // stamp jobs were drained above); later stamp writes to
                    // the LIVE arrays no longer touch what the jobs read.
                    // Sized from the SOURCE arrays: Cost/Flags are layered
                    // (Width*Height*LayerCount) — and rebuilt here if the
                    // grid is ever reallocated. Safe to dispose: the
                    // _pendingSlots gate above guarantees no detached job is
                    // in flight when this branch runs.
                    if (!_costSnapshot.IsCreated || _costSnapshot.Length != cost.Cost.Length)
                    {
                        if (_costSnapshot.IsCreated) _costSnapshot.Dispose();
                        _costSnapshot = new NativeArray<byte>(cost.Cost.Length,
                            Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    }
                    if (!_flagsSnapshot.IsCreated || _flagsSnapshot.Length != cost.Flags.Length)
                    {
                        if (_flagsSnapshot.IsCreated) _flagsSnapshot.Dispose();
                        _flagsSnapshot = new NativeArray<byte>(cost.Flags.Length,
                            Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                    }
                    _costSnapshot.CopyFrom(cost.Cost);
                    _flagsSnapshot.CopyFrom(cost.Flags);
                    _pendingGeneration = cost.Generation;
                }

                // Round-robin the batch start across ticks (deterministic —
                // TickCounter is sim state, wanted is chunk-walk ordered).
                // Under Generation churn every live key is stale every tick;
                // a fixed wanted[0..budget) meant the SAME first four keys
                // re-integrated forever and everyone else starved.
                int rrStart = cacheSingleton.TickCounter % wanted.Length;

                for (int i = 0; i < budget; i++)
                {
                    var key = wanted[(rrStart + i) % wanted.Length];

                    // Reuse the stale slot for this key when present; else
                    // allocate/evict.
                    int slot;
                    if (!cacheSingleton.SlotIndex.TryGetValue(key, out slot))
                    {
                        slot = AllocateSlot(ref cacheSingleton);
                        cacheSingleton.SlotIndex.TryAdd(key, slot);
                        cacheSingleton.SlotKeys[slot] = key;

                        // CLAIM the slot NOW (Valid = 2, "integrating").
                        // Valid was only set after the batch completed, so a
                        // SECOND fresh key in this same batch saw the slot
                        // still free (Valid == 0) and was handed the SAME
                        // slot: two jobs wrote one DirPool slice concurrently
                        // and one key permanently served the OTHER key's
                        // goal field. That was the "workers walk away from
                        // their destination toward the enemy base" bug — and
                        // a lockstep hazard, since thread timing picked the
                        // surviving field.
                        var claim = cacheSingleton.Slots[slot];
                        claim.Valid = 2;
                        claim.LastUsedTick = cacheSingleton.TickCounter;
                        cacheSingleton.Slots[slot] = claim;
                    }
                    else if (!synchronous)
                    {
                        // Stale-refresh of an existing slot: demote to
                        // "integrating" so the sampler stops reading the
                        // slice while the detached job rewrites it. Units
                        // on this field hold for the frame or two of the
                        // refresh (FlowFollow trusts Valid == 1 only).
                        var claim = cacheSingleton.Slots[slot];
                        claim.Valid = 2;
                        claim.LastUsedTick = cacheSingleton.TickCounter;
                        cacheSingleton.Slots[slot] = claim;
                    }
                    slots[i] = slot;

                    // Seed = centre cell of the goal bucket (the job ring-
                    // snaps to walkable if that centre is blocked).
                    int2 seedCell = new int2(
                        math.min(key.GoalCell.x * quant + quant / 2, grid.Width - 1),
                        math.min(key.GoalCell.y * quant + quant / 2, grid.Height - 1));

                    var job = new IntegrateGoalFieldJob
                    {
                        Cost = synchronous ? cost.Cost : _costSnapshot,
                        Flags = synchronous ? cost.Flags : _flagsSnapshot,
                        Integration = cacheSingleton.IntegrationScratch,
                        ScratchOffset = i * cacheSingleton.CellCount,
                        DirPool = cacheSingleton.DirPool,
                        DirOffset = slot * cacheSingleton.CellCount,
                        Width = grid.Width,
                        Height = grid.Height,
                        GoalCell = seedCell,
                        FactionIdx = key.FactionIdx,
                        SnapRadius = GoalSnapRadius,
                        GroundVariant = key.Variant == GoalFlowKey.VariantGround ? (byte)1 : (byte)0,
                    };
                    handles[i] = job.Schedule();
                }

                var combined = Unity.Jobs.JobHandle.CombineDependencies(handles);

                if (synchronous)
                {
                    // DeterministicLockstep: field availability may not
                    // depend on frame rate — pay the stall, flip in-tick.
                    combined.Complete();

                    for (int i = 0; i < budget; i++)
                    {
                        var meta = cacheSingleton.Slots[slots[i]];
                        meta.DirOffset = slots[i] * cacheSingleton.CellCount;
                        meta.Generation = cost.Generation;
                        meta.LastUsedTick = cacheSingleton.TickCounter;
                        meta.IntegratedTick = cacheSingleton.TickCounter;
                        meta.Valid = 1;
                        cacheSingleton.Slots[slots[i]] = meta;
                    }
                }
                else
                {
                    // Detached: run through the frame on worker threads;
                    // flip at the top of the next update.
                    _pendingHandle = combined;
                    for (int i = 0; i < budget; i++)
                        _pendingSlots.Add(slots[i]);
                }

                handles.Dispose();
                slots.Dispose();
            }

            wanted.Dispose();
            seen.Dispose();
            SystemAPI.SetSingleton(cacheSingleton);
        }

        public void OnDestroy(ref SystemState state)
        {
            // Detached jobs may still be running against our arrays — drain
            // them before anything is disposed.
            _pendingHandle.Complete();
            if (_pendingSlots.IsCreated) _pendingSlots.Dispose();
            if (_costSnapshot.IsCreated) _costSnapshot.Dispose();
            if (_flagsSnapshot.IsCreated) _flagsSnapshot.Dispose();

            if (_initialised == 0) return;
            var em = state.EntityManager;
            if (em.Exists(_cacheEntity) && em.HasComponent<GoalFlowFieldCache>(_cacheEntity))
            {
                var c = em.GetComponentData<GoalFlowFieldCache>(_cacheEntity);
                if (c.SlotIndex.IsCreated) c.SlotIndex.Dispose();
                if (c.Slots.IsCreated) c.Slots.Dispose();
                if (c.SlotKeys.IsCreated) c.SlotKeys.Dispose();
                if (c.DirPool.IsCreated) c.DirPool.Dispose();
                if (c.IntegrationScratch.IsCreated) c.IntegrationScratch.Dispose();
            }
        }

        // Free slot if any, else evict LRU (smallest LastUsedTick, ties by
        // smallest slot index — deterministic). Slots claimed THIS tick
        // (Valid == 2, mid-batch) are never free and never eviction
        // candidates — handing one out twice is exactly the double-
        // assignment this fix removed.
        private static int AllocateSlot(ref GoalFlowFieldCache cache)
        {
            for (int i = 0; i < cache.SlotCount; i++)
            {
                if (cache.Slots[i].Valid == 0) return i;
            }
            int lru = -1;
            int lruTick = int.MaxValue;
            for (int i = 0; i < cache.SlotCount; i++)
            {
                if (cache.Slots[i].Valid == 2) continue; // claimed mid-batch
                if (cache.Slots[i].LastUsedTick < lruTick)
                {
                    lru = i;
                    lruTick = cache.Slots[i].LastUsedTick;
                }
            }
            // Unreachable in practice (claims per tick <= MaxIntegrations-
            // PerTick << SlotCount), but never hand out a claimed slot.
            if (lru < 0) lru = 0;
            cache.SlotIndex.Remove(cache.SlotKeys[lru]);
            var s = cache.Slots[lru];
            s.Valid = 0;
            cache.Slots[lru] = s;
            return lru;
        }
    }

    /// <summary>
    /// Whole-map integration sweep + direction-byte assignment for one
    /// (goalCell, faction) field. Same conventions as the retired per-tile
    /// slab integrator: octile 10/14 step costs, finite wall-clearance
    /// penalty, no corner cutting, weighted-gradient direction quantized to
    /// the 256-bin angle byte the DirectionTableBlob expands.
    /// </summary>
    [BurstCompile]
    internal struct IntegrateGoalFieldJob : IJob
    {
        [ReadOnly] public NativeArray<byte> Cost;
        [ReadOnly] public NativeArray<byte> Flags;
        // Disjoint slices per concurrently scheduled job (ScratchOffset /
        // DirOffset) — the container-level safety check can't see that, so
        // it is disabled explicitly.
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
        public NativeArray<uint> Integration;
        public int ScratchOffset;
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
        public NativeArray<byte> DirPool;
        public int DirOffset;
        public int Width;
        public int Height;
        public int2 GoalCell;
        public byte FactionIdx;
        public int SnapRadius;
        /// <summary>1 = ground variant: bridge deck-only cells are treated
        /// as impassable (pure ground routing, no bridge crossings).</summary>
        public byte GroundVariant;

        // Mirrors IntegrateTileJob.WallClearancePenalty — a finite bias off
        // obstacle edges, never a hard block.
        private const uint WallClearancePenalty = 8;

        public void Execute()
        {
            int cellCount = Width * Height;
            for (int i = 0; i < cellCount; i++)
                Integration[ScratchOffset + i] = uint.MaxValue;

            // Snap a blocked goal cell to the nearest walkable cell inside
            // an expanding ring (deterministic scan order: ring radius asc,
            // then z asc, then x asc).
            int2 goal = GoalCell;
            if (!IsOpen(goal.x, goal.y))
            {
                bool found = false;
                for (int r = 1; r <= SnapRadius && !found; r++)
                {
                    for (int dz = -r; dz <= r && !found; dz++)
                    {
                        for (int dx = -r; dx <= r && !found; dx++)
                        {
                            // Ring shell only.
                            if (math.abs(dx) != r && math.abs(dz) != r) continue;
                            int nx = GoalCell.x + dx;
                            int nz = GoalCell.y + dz;
                            if (IsOpen(nx, nz))
                            {
                                goal = new int2(nx, nz);
                                found = true;
                            }
                        }
                    }
                }
                if (!found)
                {
                    // No walkable cell near the goal — whole field is
                    // unreachable (all NoDirection). Units will hold.
                    for (int i = 0; i < cellCount; i++)
                        DirPool[DirOffset + i] = NavFlowConstants.NoDirection;
                    return;
                }
            }

            int goalIdx = goal.y * Width + goal.x;
            Integration[ScratchOffset + goalIdx] = 0;

            // FIFO double-buffered label-correcting sweep (mirror of the
            // slab integrator — deterministic order, integer costs).
            var fa = new NativeQueue<int>(Allocator.Temp);
            var fb = new NativeQueue<int>(Allocator.Temp);
            fa.Enqueue(goalIdx);
            var read = fa;
            var write = fb;

            while (read.Count > 0)
            {
                while (read.TryDequeue(out int idx))
                {
                    uint here = Integration[ScratchOffset + idx];
                    int x = idx % Width;
                    int z = idx / Width;
                    byte hereCost = Cost[idx];

                    Relax(x + 1, z, here + NavFlowConstants.StepCardinal, hereCost, write);
                    Relax(x - 1, z, here + NavFlowConstants.StepCardinal, hereCost, write);
                    Relax(x, z + 1, here + NavFlowConstants.StepCardinal, hereCost, write);
                    Relax(x, z - 1, here + NavFlowConstants.StepCardinal, hereCost, write);

                    if (IsOpen(x + 1, z) && IsOpen(x, z + 1))
                        Relax(x + 1, z + 1, here + NavFlowConstants.StepDiagonal, hereCost, write);
                    if (IsOpen(x + 1, z) && IsOpen(x, z - 1))
                        Relax(x + 1, z - 1, here + NavFlowConstants.StepDiagonal, hereCost, write);
                    if (IsOpen(x - 1, z) && IsOpen(x, z + 1))
                        Relax(x - 1, z + 1, here + NavFlowConstants.StepDiagonal, hereCost, write);
                    if (IsOpen(x - 1, z) && IsOpen(x, z - 1))
                        Relax(x - 1, z - 1, here + NavFlowConstants.StepDiagonal, hereCost, write);
                }
                var tmp = read; read = write; write = tmp;
            }

            fa.Dispose();
            fb.Dispose();

            // Direction bytes: weighted gradient over walkable neighbours,
            // quantized to the 256-bin angle byte.
            for (int z = 0; z < Height; z++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int idx = z * Width + x;
                    uint here = Integration[ScratchOffset + idx];

                    if (here == uint.MaxValue || !IsOpen(x, z)
                        || (x == goal.x && z == goal.y))
                    {
                        DirPool[DirOffset + idx] = NavFlowConstants.NoDirection;
                        continue;
                    }

                    float gxv = 0f, gzv = 0f;
                    for (int dzz = -1; dzz <= 1; dzz++)
                    for (int dxx = -1; dxx <= 1; dxx++)
                    {
                        if (dxx == 0 && dzz == 0) continue;
                        int nx = x + dxx, nz = z + dzz;
                        if (!IsOpen(nx, nz)) continue;
                        if (dxx != 0 && dzz != 0)
                        {
                            if (!IsOpen(x + dxx, z)) continue;
                            if (!IsOpen(x, z + dzz)) continue;
                        }
                        uint nCost = Integration[ScratchOffset + nz * Width + nx];
                        if (nCost == uint.MaxValue || nCost >= here) continue;
                        float weight = (float)(here - nCost);
                        float inv = (dxx != 0 && dzz != 0) ? 0.70710678f : 1f;
                        gxv += dxx * weight * inv;
                        gzv += dzz * weight * inv;
                    }

                    if (gxv == 0f && gzv == 0f)
                    {
                        DirPool[DirOffset + idx] = NavFlowConstants.NoDirection;
                        continue;
                    }
                    float angle = math.atan2(gzv, gxv);
                    if (angle < 0f) angle += 2f * math.PI;
                    int dirByte = (int)math.round(angle / (2f * math.PI) * 256f);
                    DirPool[DirOffset + idx] = (byte)(dirByte & 0xFF);
                }
            }
        }

        // Walkable for THIS field's faction: 255 never, 254 only when the
        // gate owner matches, everything else yes.
        private bool IsOpen(int x, int z)
        {
            if (x < 0 || x >= Width || z < 0 || z >= Height) return false;
            int idx = z * Width + x;
            byte c = Cost[idx];
            if (c == NavCostField.CostImpassable) return false;
            if (c == NavCostField.CostConditional)
            {
                byte ownerIdx = (byte)(Flags[idx] & NavCostField.FlagOwnerMask);
                return ownerIdx == FactionIdx;
            }
            // Ground variant: the deck-only strip does not exist for pure
            // ground routing.
            if (GroundVariant != 0 && c == NavCostField.CostBridgeDeckOnly) return false;
            return true;
        }

        private void Relax(int x, int z, uint tentative, byte fromCost, NativeQueue<int> writeFrontier)
        {
            if (!IsOpen(x, z)) return;
            int idx = z * Width + x;
            byte c = Cost[idx];

            // Bridge deck-only strips (walkable ONLY at deck height) connect
            // to the rest of the map exclusively through MOUNT cells (deck
            // touchdowns / ramp toes). An edge between a deck-only cell and
            // an ordinary cell would let the 2D plan route ground units into
            // the strip's side, where the integrator rightly refuses them —
            // the "walk at the cliff and grind" bug.
            bool fromDeck = fromCost == NavCostField.CostBridgeDeckOnly;
            bool toDeck = c == NavCostField.CostBridgeDeckOnly;
            if (fromDeck != toDeck)
            {
                byte outsideCost = fromDeck ? c : fromCost;
                if (outsideCost != NavCostField.CostBridgeMount) return;
            }

            tentative += WallClearancePenalty * NearWall(x, z);
            // Per-cell entry weight (deck-only premium / tiny mount marker) —
            // steers ground routes away from deck cells without forbidding
            // genuine bridge crossings.
            if (c > 0 && c < NavCostField.CostConditional)
                tentative += c;
            if (tentative < Integration[ScratchOffset + idx])
            {
                Integration[ScratchOffset + idx] = tentative;
                writeFrontier.Enqueue(idx);
            }
        }

        private uint NearWall(int x, int z)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = x + dx, nz = z + dz;
                    if (nx < 0 || nx >= Width || nz < 0 || nz >= Height) continue;
                    if (Cost[nz * Width + nx] == NavCostField.CostImpassable) return 1;
                }
            }
            return 0;
        }
    }
}
