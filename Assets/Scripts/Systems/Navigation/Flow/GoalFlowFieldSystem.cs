// GoalFlowFieldSystem.cs
// Goal-centric WHOLE-MAP flow fields — the pathfinding redesign
// (directive 2026-07-05).
//
// Why: the M3-M7 hierarchical stack (portal graph + per-leg 16x16 flow
// slabs + per-unit path cursors) had no globally-correct base layer. Every
// unit-facing answer depended on a chain of fragile links (path result,
// cursor position, slab cache key), and any broken link silently degraded
// to reactive heuristics that gradient-descend on distance — which a
// concave obstacle (a "U") defeats by definition.
//
// SEEKER-BOUNDED SWEEPS (2026-09-03). The original design integrated the
// WHOLE grid per destination on the claim that one sweep is microsecond
// work "at this game's map sizes (50x50 .. 256x256 cells)". Shipped maps
// are 1026x1026 (1M+ cells): a full sweep is hundreds of milliseconds,
// and under veil-crust Generation churn (~1 bump/s) the re-integration
// treadmill was the profiled "GoalFlowFieldSystem=500ms" frame stall —
// 63 s of hitches in a 5-minute match. The sweep is now a settle-once
// dial-queue Dijkstra that STOPS once every SEEKER (unit actually wanting
// this field) is settled, plus a fixed cost margin: work is proportional
// to the region the units and their routes occupy, not to the map.
// Cells the sweep never reached are stamped NavFlowConstants.NotCovered;
// samplers fall back to the direct bearing there, and this system
// re-integrates a field as soon as a seeker stands on an uncovered cell,
// so coverage converges in one extra integration. The early-out is
// budgeted in COST UNITS, never wall-clock — bit-for-bit deterministic,
// so it is lockstep-safe in multiplayer. Only a sweep whose frontier
// exhausted naturally may stamp NoDirection ("provably unreachable").
//
// What it does: every tick, collect the distinct (goalCell, factionIdx)
// keys of all units holding an active DesiredDestination; for keys missing
// from the cache (or stale against NavCostField.Generation, or not
// covering one of their seekers), run one seeker-bounded integration
// (settle-once dial-queue Dijkstra from the goal, octile 10/14 costs,
// wall-clearance bias — the exact cost conventions of the retired
// per-tile slab integrator) and store a direction-byte field.
// FlowFollowSystem then answers every unit's "which way?" with one array
// read. Formation moves need nothing special: each unit's slot destination
// is its own goal key, and slots that share a cell share a field.
//
// Faction in the key: conditional gate cells (cost 254) are walkable only
// for the owner faction encoded in the cell flags, so fields are integrated
// per faction. Group moves are single-faction, so this costs nothing in
// practice. Factionless units use sentinel 0xFF (gates closed).
//
// Unreachable is EXPLICIT — but only a FULL sweep may claim it: when the
// frontier exhausts naturally, unreached cells get NoDirection and
// FlowFollow makes units hold position instead of grinding into the
// blocker. A sweep cut short by the seeker bound stamps NotCovered
// instead, which proves nothing. Goals clicked ON a blocker are snapped
// to the nearest walkable cell (bounded deterministic ring search)
// before integrating.
//
// Determinism: integer costs, fixed neighbour order, deterministic key
// collection (chunk-walk order), sequential Burst .Run() integration,
// LRU eviction with smallest-slot tie-break. No wall-clock, no floats in
// sim-affecting decisions (the dir byte is quantized once at bake).

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

    /// <summary>Total order over keys — REQUIRED for lockstep: any list of
    /// keys that feeds a budget or a slot allocator must be sorted with this
    /// before use, or peers pick different work on the same tick (their
    /// chunk-walk collection orders differ by construction).</summary>
    public struct Order : System.Collections.Generic.IComparer<GoalFlowKey>
    {
        public int Compare(GoalFlowKey a, GoalFlowKey b)
        {
            if (a.GoalCell.x != b.GoalCell.x) return a.GoalCell.x - b.GoalCell.x;
            if (a.GoalCell.y != b.GoalCell.y) return a.GoalCell.y - b.GoalCell.y;
            if (a.FactionIdx != b.FactionIdx) return a.FactionIdx - b.FactionIdx;
            return a.Variant - b.Variant;
        }
    }

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

        /// <summary>How far past the farthest seeker the bounded sweep keeps
        /// integrating, in integration cost units (StepCardinal = 10 per
        /// cell, so 400 ~= 40 cells of slack). Covers formation spread, the
        /// virtual formation leader, and units drifting off-path between
        /// refreshes without paying for the whole map.</summary>
        public const uint CoverageMarginCost = 400;

        /// <summary>Per-field cap on collected seeker cells. Beyond it the
        /// extras are dropped for the batch — the coverage re-trigger picks
        /// any genuinely uncovered straggler up next tick.</summary>
        public const int MaxSeekersPerField = 1024;

        private Entity _cacheEntity;
        private byte _initialised;
        private EntityQuery _unitQuery;

        // System-side mirrors of the cache's pools — the only way to free them
        // once the end-of-match wipe has taken the component that held them.
        private NativeHashMap<GoalFlowKey, int> _slotIndex;
        private NativeArray<GoalFlowSlot> _slots;
        private NativeArray<GoalFlowKey> _slotKeys;
        private NativeArray<byte> _dirPool;
        private NativeArray<uint> _integrationScratch;

        /// <summary>Free the cache pools via the mirrors.</summary>
        private void ReleaseCache()
        {
            if (_slotIndex.IsCreated) _slotIndex.Dispose();
            if (_slots.IsCreated) _slots.Dispose();
            if (_slotKeys.IsCreated) _slotKeys.Dispose();
            if (_dirPool.IsCreated) _dirPool.Dispose();
            if (_integrationScratch.IsCreated) _integrationScratch.Dispose();
            _slotIndex = default;
            _slots = default;
            _slotKeys = default;
            _dirPool = default;
            _integrationScratch = default;
        }

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
        // Under multiplayer deterministic lockstep the synchronous path is
        // kept: field availability must not depend on frame rate there.
        private NativeArray<byte> _costSnapshot;
        private NativeArray<byte> _flagsSnapshot;
        private NativeList<int> _pendingSlots;
        private Unity.Jobs.JobHandle _pendingHandle;
        private int _pendingGeneration;

        /// <summary>Last SimCadence epoch this system re-phased its cache
        /// TickCounter to. -1 forces a reset on the first update. See the
        /// TickCounter reset in OnUpdate.</summary>
        private int _lastEpoch;

        // Seeker cells for the in-flight batch, one disjoint slice per job
        // (same non-overlap guarantee as _integrationScratch: a new batch is
        // only written while no detached batch is pending).
        private NativeArray<int> _seekerPool;

        public void OnCreate(ref SystemState state)
        {
            _initialised = 0;
            _lastEpoch = -1;   // force a TickCounter re-phase on first update
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();

            _pendingSlots = new NativeList<int>(MaxIntegrationsPerTick, Allocator.Persistent);
            _seekerPool = new NativeArray<int>(MaxIntegrationsPerTick * MaxSeekersPerField,
                Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

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

            // Existence-gated: the end-of-match wipe destroys this cache while
            // the system survives, and the unguarded GetSingleton below would
            // then throw every frame. Rebuilding also re-sizes the pools to the
            // NEW grid, which a latch would have skipped on a different map.
            if (_initialised == 0
                || !em.Exists(_cacheEntity)
                || !em.HasComponent<GoalFlowFieldCache>(_cacheEntity))
            {
                // Detached integration jobs may still be reading the old pools.
                _pendingHandle.Complete();
                ReleaseCache();

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
                // Mirror the pools so a rebuild after a wipe can free them.
                _slotIndex = cache.SlotIndex;
                _slots = cache.Slots;
                _slotKeys = cache.SlotKeys;
                _dirPool = cache.DirPool;
                _integrationScratch = cache.IntegrationScratch;
                // Snapshot arrays are allocated lazily at the copy site —
                // Cost/Flags are LAYERED (Width*Height*LayerCount), NOT the
                // ground cellCount this cache works in; sizing them here at
                // cellCount threw "source and destination length must be the
                // same" every tick.
            }

            var cacheSingleton = SystemAPI.GetSingleton<GoalFlowFieldCache>();

            // PHASE-ALIGN THE COUNTER AT MATCH START (2026-09-04, MP harness
            // catch #5 — the tick-1956 fork). TickCounter drives the LRU and
            // the round-robin `rrStart = TickCounter % wanted.Length` that
            // picks WHICH goal fields integrate this tick when more are wanted
            // than the per-tick budget (4). But it was self-incremented every
            // OnUpdate — INCLUDING the pre-match bootstrap frames, of which
            // each peer runs a machine-dependent number before LockstepFixedStep
            // takes over. So two peers entered tick 0 with different TickCounter
            // phases; identical for as long as every tick wanted <= 4 fields,
            // then the moment a mid-game tick wanted 5+, the round-robin picked
            // DIFFERENT fields to refresh on each peer — one got a fresh flow
            // field, the other a stale one — and units sampled divergent
            // directions. The exact SimCadence bug class, so the exact
            // SimCadence fix: re-phase to 0 when the match epoch flips at
            // tick 0, after which every peer increments once per lockstep tick
            // from the same base.
            int epoch = SimCadence.Epoch;
            if (epoch != _lastEpoch)
            {
                _lastEpoch = epoch;
                cacheSingleton.TickCounter = 0;
            }
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
            using var xforms = _unitQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var wanted = new NativeList<GoalFlowKey>(64, Allocator.Temp);
            var seen = new NativeHashMap<GoalFlowKey, byte>(64, Allocator.Temp);

            for (int i = 0; i < dests.Length; i++)
            {
                if (dests[i].Has == 0) continue;

                int gx = (int)math.floor((dests[i].Position.x - grid.Origin.x) / grid.CellSize);
                int gz = (int)math.floor((dests[i].Position.z - grid.Origin.z) / grid.CellSize);
                gx = math.clamp(gx, 0, grid.Width - 1);
                gz = math.clamp(gz, 0, grid.Height - 1);

                // The unit's own cell — half of the coverage test below, and
                // the seeker cell the bounded sweep must settle.
                int ucx = (int)math.floor((xforms[i].Position.x - grid.Origin.x) / grid.CellSize);
                int ucz = (int)math.floor((xforms[i].Position.z - grid.Origin.z) / grid.CellSize);
                ucx = math.clamp(ucx, 0, grid.Width - 1);
                ucz = math.clamp(ucz, 0, grid.Height - 1);

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

                            // Coverage test: a NotCovered byte at the unit's
                            // own cell means the seeker-bounded sweep stopped
                            // short of this unit — its field cannot steer it,
                            // so re-integrate NOW (this unit is a seeker of
                            // the new sweep), bypassing both the fresh-hit
                            // skip and the stale cooldown.
                            bool covered = true;
                            if (meta.Valid == 1)
                                covered = cacheSingleton.DirPool[
                                        slot * cacheSingleton.CellCount
                                        + ucz * grid.Width + ucx]
                                    != NavFlowConstants.NotCovered;

                            if (covered && meta.Valid == 1
                                && meta.Generation == cost.Generation)
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
                            if (covered && meta.Valid == 1
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
            // DeterministicLockstep only means anything in multiplayer (its
            // own doc says so, and nothing ever sets it false) — the raw
            // read here kept every single-player match on the synchronous
            // main-thread stall path the detached design exists to avoid.
            bool synchronous = GameSettings.IsMultiplayer
                && GameSettings.DeterministicLockstep;
            if (!synchronous && _pendingSlots.Length > 0) budget = 0;

            if (budget > 0)
            {
                var handles = new NativeArray<Unity.Jobs.JobHandle>(budget, Allocator.Temp);
                var slots = new NativeArray<int>(budget, Allocator.Temp);

                // Batch keys are fixed before the seeker pass below needs
                // to match units against them.
                var batchKeys = new NativeArray<GoalFlowKey>(budget, Allocator.Temp);
                var seekerCounts = new NativeArray<int>(budget, Allocator.Temp,
                    NativeArrayOptions.ClearMemory);

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
                // TOTAL ORDER BEFORE THE BUDGET (2026-09-04, MP harness
                // catch #3). `wanted` collects in CHUNK-WALK order, which
                // legitimately differs between lockstep peers — so with a
                // budget smaller than the wish list, peers integrated
                // DIFFERENT fields on the same tick. Units then started
                // following a field 1-2 ticks apart across peers (identical
                // orders, ~0.4 u position skew at speed 6), forking the
                // match at tick 9257 on two different seeds. Sorting by the
                // key's own values makes batch selection peer-identical;
                // everything downstream (slots, seekers, round-robin) then
                // follows the same order everywhere.
                wanted.Sort(new GoalFlowKey.Order());

                int rrStart = cacheSingleton.TickCounter % wanted.Length;
                for (int i = 0; i < budget; i++)
                    batchKeys[i] = wanted[(rrStart + i) % wanted.Length];

                // ── Seeker pass: every unit whose key is in the batch
                // contributes its cell — the sweep must settle all of them
                // before it may stop. Duplicate cells collapse in the job's
                // seeker set; the per-field cap just drops extras (the
                // coverage re-trigger catches any genuinely uncovered
                // straggler next tick). Same clamp math as pass 1.
                for (int i = 0; i < dests.Length; i++)
                {
                    if (dests[i].Has == 0) continue;

                    int sgx = (int)math.floor((dests[i].Position.x - grid.Origin.x) / grid.CellSize);
                    int sgz = (int)math.floor((dests[i].Position.z - grid.Origin.z) / grid.CellSize);
                    sgx = math.clamp(sgx, 0, grid.Width - 1);
                    sgz = math.clamp(sgz, 0, grid.Height - 1);

                    int sf = (int)factions[i].Value;
                    byte sFactionIdx = (sf >= 0 && sf <= 7) ? (byte)sf : (byte)0xFF;
                    var sBucket = new int2(sgx / quant, sgz / quant);

                    int sucx = (int)math.floor((xforms[i].Position.x - grid.Origin.x) / grid.CellSize);
                    int sucz = (int)math.floor((xforms[i].Position.z - grid.Origin.z) / grid.CellSize);
                    sucx = math.clamp(sucx, 0, grid.Width - 1);
                    sucz = math.clamp(sucz, 0, grid.Height - 1);
                    int seekerCell = sucz * grid.Width + sucx;

                    for (int j = 0; j < budget; j++)
                    {
                        // Variant is deliberately ignored in the match: a
                        // unit needing this goal bucket is a seeker of BOTH
                        // its variants' sweeps.
                        if (!batchKeys[j].GoalCell.Equals(sBucket)
                            || batchKeys[j].FactionIdx != sFactionIdx) continue;
                        if (seekerCounts[j] >= MaxSeekersPerField) continue;
                        _seekerPool[j * MaxSeekersPerField + seekerCounts[j]] = seekerCell;
                        seekerCounts[j] = seekerCounts[j] + 1;
                    }
                }

                for (int i = 0; i < budget; i++)
                {
                    var key = batchKeys[i];

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
                        SeekerPool = _seekerPool,
                        SeekerOffset = i * MaxSeekersPerField,
                        SeekerCount = seekerCounts[i],
                        CoverageMargin = CoverageMarginCost,
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
                batchKeys.Dispose();
                seekerCounts.Dispose();
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
            if (_seekerPool.IsCreated) _seekerPool.Dispose();
            if (_costSnapshot.IsCreated) _costSnapshot.Dispose();
            if (_flagsSnapshot.IsCreated) _flagsSnapshot.Dispose();

            // Mirrors, not the component — the entity may already be wiped.
            ReleaseCache();
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
    /// Seeker-bounded integration sweep + direction-byte assignment for one
    /// (goalCell, faction) field. Settle-once dial-queue Dijkstra from the
    /// goal that stops once every seeker cell is settled plus a fixed cost
    /// margin; unreached cells are stamped NotCovered (partial sweep) or
    /// NoDirection (frontier exhausted — provably unreachable). Same cost
    /// conventions as the retired per-tile slab integrator: octile 10/14
    /// step costs, finite wall-clearance penalty, no corner cutting,
    /// weighted-gradient direction quantized to the 256-bin angle byte the
    /// DirectionTableBlob expands.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal struct IntegrateGoalFieldJob : IJob
    {
        [ReadOnly] public NativeArray<byte> Cost;
        [ReadOnly] public NativeArray<byte> Flags;
        // Disjoint slices per concurrently scheduled job (ScratchOffset /
        // DirOffset / SeekerOffset) — the container-level safety check
        // can't see that, so it is disabled explicitly.
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
        [Unity.Collections.LowLevel.Unsafe.NativeDisableContainerSafetyRestriction]
        [ReadOnly] public NativeArray<int> SeekerPool;
        public int SeekerOffset;
        public int SeekerCount;
        public uint CoverageMargin;

        // Mirrors IntegrateTileJob.WallClearancePenalty — a finite bias off
        // obstacle edges, never a hard block.
        private const uint WallClearancePenalty = 8;

        // Dial-queue bucket count. Must exceed the largest single relax
        // increment — StepDiagonal(14) + WallClearancePenalty(8) + max
        // per-cell premium (253) = 275 — so an entry's bucket index is
        // unambiguous among in-flight costs. Power of two for the mask.
        private const int BucketCount = 512;

        // Covered bounding box (tracked at relax time, so it includes the
        // one-cell frontier ring around the settled region).
        private int _minX, _maxX, _minZ, _maxZ;
        private long _pendingEntries;

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

            // Seeker set: the distinct cells the sweep must settle before it
            // may stop. Duplicate cells (stacked units) collapse here, so
            // the remaining-count is exact. An empty set (no seekers
            // collected for this key) disables the early-out — full sweep.
            var seekers = new Unity.Collections.LowLevel.Unsafe.UnsafeHashSet<int>(
                math.max(SeekerCount, 1), Allocator.Temp);
            for (int i = 0; i < SeekerCount; i++)
                seekers.Add(SeekerPool[SeekerOffset + i]);
            int seekersRemaining = seekers.Count;
            bool mayStop = seekersRemaining > 0;

            // Dial queue: one FIFO bucket per cost value modulo BucketCount.
            // Entries are (cellIdx); an entry is live iff its cell's stored
            // integration equals the bucket's cost when it is popped —
            // improved entries simply go stale. Deterministic: buckets are
            // processed in cost order and filled in relax order.
            var buckets = new NativeArray<Unity.Collections.LowLevel.Unsafe.UnsafeList<int>>(
                BucketCount, Allocator.Temp);
            for (int i = 0; i < BucketCount; i++)
                buckets[i] = new Unity.Collections.LowLevel.Unsafe.UnsafeList<int>(4, Allocator.Temp);

            Integration[ScratchOffset + goalIdx] = 0;
            {
                var b0 = buckets[0];
                b0.Add(goalIdx);
                buckets[0] = b0;
            }
            _pendingEntries = 1;
            _minX = goal.x; _maxX = goal.x; _minZ = goal.y; _maxZ = goal.y;

            uint stopCost = uint.MaxValue;
            bool stopped = false;

            for (uint sweep = 0; _pendingEntries > 0; sweep++)
            {
                if (sweep > stopCost) { stopped = true; break; }

                int bi = (int)(sweep & (BucketCount - 1));
                var bucket = buckets[bi];
                int n = bucket.Length;
                if (n == 0) continue;

                for (int i = 0; i < n; i++)
                {
                    int idx = bucket[i];
                    if (Integration[ScratchOffset + idx] != sweep) continue; // stale entry

                    if (mayStop && seekers.Remove(idx) && --seekersRemaining == 0)
                        stopCost = sweep + CoverageMargin;

                    int x = idx % Width;
                    int z = idx / Width;
                    byte hereCost = Cost[idx];

                    Relax(x + 1, z, sweep + NavFlowConstants.StepCardinal, hereCost, buckets);
                    Relax(x - 1, z, sweep + NavFlowConstants.StepCardinal, hereCost, buckets);
                    Relax(x, z + 1, sweep + NavFlowConstants.StepCardinal, hereCost, buckets);
                    Relax(x, z - 1, sweep + NavFlowConstants.StepCardinal, hereCost, buckets);

                    if (IsOpen(x + 1, z) && IsOpen(x, z + 1))
                        Relax(x + 1, z + 1, sweep + NavFlowConstants.StepDiagonal, hereCost, buckets);
                    if (IsOpen(x + 1, z) && IsOpen(x, z - 1))
                        Relax(x + 1, z - 1, sweep + NavFlowConstants.StepDiagonal, hereCost, buckets);
                    if (IsOpen(x - 1, z) && IsOpen(x, z + 1))
                        Relax(x - 1, z + 1, sweep + NavFlowConstants.StepDiagonal, hereCost, buckets);
                    if (IsOpen(x - 1, z) && IsOpen(x, z - 1))
                        Relax(x - 1, z - 1, sweep + NavFlowConstants.StepDiagonal, hereCost, buckets);
                }

                _pendingEntries -= n;
                bucket.Clear();
                buckets[bi] = bucket;
            }

            // A partial sweep leaves NotCovered (no verdict — samplers use
            // the direct bearing and the producer re-integrates on demand);
            // a naturally exhausted frontier proves unreachability, which is
            // NoDirection (units hold instead of grinding into blockers).
            byte unreachedSentinel = stopped
                ? NavFlowConstants.NotCovered
                : NavFlowConstants.NoDirection;
            for (int i = 0; i < cellCount; i++)
                DirPool[DirOffset + i] = unreachedSentinel;

            // Direction bytes: weighted gradient over walkable neighbours,
            // quantized to the 256-bin angle byte — over the covered
            // bounding box only. Frontier cells abandoned by the early-out
            // hold near-final tentative values; their gradients are valid.
            for (int z = _minZ; z <= _maxZ; z++)
            {
                for (int x = _minX; x <= _maxX; x++)
                {
                    int idx = z * Width + x;
                    uint here = Integration[ScratchOffset + idx];

                    if (here == uint.MaxValue) continue; // keeps the sentinel

                    if (!IsOpen(x, z) || (x == goal.x && z == goal.y))
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
                    int dirByte = (int)math.round(angle / (2f * math.PI) * 256f) & 0xFF;
                    // 255/256 quantize to the ~0 rad bin; 254 is NotCovered.
                    if (dirByte == 255) dirByte = 0;
                    else if (dirByte == 254) dirByte = 253;
                    DirPool[DirOffset + idx] = (byte)dirByte;
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
                // A gate is passable to its owner AND to the owner's team —
                // GateStateSystem opens for allies, so the flow field has to
                // route them through it too. docs/Design/Teams.md
                return Alliances.AreAlliedBurst(ownerIdx, FactionIdx);
            }
            // Ground variant: the deck-only strip does not exist for pure
            // ground routing.
            if (GroundVariant != 0 && c == NavCostField.CostBridgeDeckOnly) return false;
            return true;
        }

        private void Relax(int x, int z, uint tentative, byte fromCost,
            NativeArray<Unity.Collections.LowLevel.Unsafe.UnsafeList<int>> buckets)
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
                int bi = (int)(tentative & (BucketCount - 1));
                var bucket = buckets[bi];
                bucket.Add(idx);
                buckets[bi] = bucket;
                _pendingEntries++;
                if (x < _minX) _minX = x; else if (x > _maxX) _maxX = x;
                if (z < _minZ) _minZ = z; else if (z > _maxZ) _maxZ = z;
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
