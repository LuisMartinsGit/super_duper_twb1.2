// UnitIntegratorSystem.cs
// task-112 M4 -- replaces the deleted MovementSystem.cs from
// Assets/Scripts/Systems/Movement/. Reads SteeringDesiredDir (M2)
// with a FlowDesiredDir (M1) fallback, integrates LocalTransform,
// manages UserMoveOrder / DesiredDestination / AttackMoveTag /
// FormationSpeedOverride lifecycle, and applies SpellDebuff /
// SpellBuff / Fortified speed modifiers.
//
// What's GONE compared to the old MovementSystem:
//   * Every NavMesh.SamplePosition call (off-mesh gate + height snap).
//   * NavMeshPathfollowState / NavMeshWaypoint corridor walk.
//   * PassabilityGrid centre-cell + spiral-escape (M4 OOS for the
//     integrator; cost-field IsCellPassable is the source of truth).
//   * The "navmesh delivered us as close as it can" corridor-end branch
//     (the cost-field-driven flow always converges on the goal cell).
//
// What's KEPT:
//   * MoveCommand / AttackMoveCommand -> DesiredDestination conversion.
//   * Per-unit smoothed-direction blending (anti-jitter).
//   * Per-unit cosmetic rotation (turn-rate clamped).
//   * Stuck-counter escalation (sidestep -> escape -> cancel).
//   * Combat short-circuit for archers with in-range targets.
//
// BURST (perf pass 2026-10-15). The per-unit integration (Phase 2) was the
// largest slice of the late-game tick (35 ms of 49 at ~2,600 entities) as a
// main-thread foreach. It is now IntegrateUnitsJob, a Burst IJobEntity run
// ScheduleParallel. What made it managed, and what replaced each piece:
//   * TerrainUtility.GetSurfaceHeight (Terrain.SampleHeight + BridgeSurface)
//     and LayeredMoveSystem.RampartSurfaceY  -> NavSurface (NavSurfaceField):
//     a baked heightmap sampled bilinearly + a native copy of the bridge
//     deck pieces. Heights differ from SampleHeight by millimetres between
//     heightmap samples; they are identical on every peer.
//   * PassabilityGrid.GetCell / IsBridgeDeckOnly -> NavTerrainMask.
//   * NavGridQuery (managed static cache)  -> the NavGridSingleton +
//     NavCostField arrays, same index math (IsCellPassable* / SnapToWalkable
//     re-implemented below, bit-for-bit the same rules).
//   * TargetGeometry.SurfaceDistXZ(em, ...) -> its lookup overload.
//   * UnitSpeedModifiers -> its (now Burst-safe) Lookups overload.
//   * TransientState helpers -> the inline lookup / ParallelWriter twins
//     at the bottom of the job (same rules).
//
// Determinism notes (lockstep):
//   * Burst FloatMode.Deterministic / FloatPrecision.High, math.* only, no
//     Random, no wall clock. dt is the fixed sim step.
//   * READ-BEFORE-WRITE: the job never writes LocalTransform. It writes each
//     unit's new transform into a per-unit output slot (indexed by
//     EntityIndexInQuery), and a second job applies them. So every cross-
//     entity read (an archer's target, the crowded-arrival neighbours) sees
//     the PRE-TICK positions, whatever the thread schedule. (The old
//     main-thread loop saw neighbours earlier in chunk order already moved —
//     a deterministic but order-dependent read; the result now differs
//     slightly from it, by design, and is order-independent.)
//   * Each unit writes only its own DesiredDestination (query column).
//   * ECB commands go through a ParallelWriter with sortKey =
//     EntityIndexInQuery: playback order is the entity's query index, then
//     record order — fixed on every peer. Phase 1 (main thread) records into
//     its own ECB, created FIRST, so its commands still play back before
//     Phase 2's, exactly as when both shared one buffer.
//   * The neighbour test (GoalTakenByCloserNeighbour) is existential over
//     the hash buckets, so bucket iteration order cannot change its answer.
//   * The jobs complete inside OnUpdate: PassabilityGrid.Cells and the
//     NavSurfaceField snapshots are main-thread-owned and must never be
//     touched by managed code while a read is in flight.
//
// [UpdateAfter(typeof(...))] hooks that used to point at MovementSystem
// (BattalionSyncSystem, WallGarrisonSystem, WallDoorAccessSystem) now
// point at UnitIntegratorSystem.

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands.Types;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// task-112 M4 integrator. Phase 1 (command -> destination) stays a small
    /// main-thread pass; Phase 2 (the per-unit step) is a parallel Burst job.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct UnitIntegratorSystem : ISystem
    {
        // ── Per-unit lookups ───────────────────────────────────────────
        // Read-only for the job; LocalTransform is written ONLY by the apply
        // job through _xformRW (a RefRW query column would bump the change
        // version of every chunk, so every unit would read as "moved" to the
        // presentation sync each tick whether it moved or not).
        private ComponentLookup<LocalTransform> _xform;
        private ComponentLookup<LocalTransform> _xformRW;
        private ComponentLookup<LayerTraversalState> _traversal;
        private ComponentLookup<SmoothedDirection> _smoothed;
        private ComponentLookup<StuckState> _stuck;
        private ComponentLookup<FormationSpeedOverride> _speedOverride;
        private ComponentLookup<MoveSpeed> _moveSpeed;
        private ComponentLookup<ArcherTag> _archer;
        private ComponentLookup<ArcherState> _archerState;
        private ComponentLookup<Target> _target;
        private ComponentLookup<Health> _health;
        private ComponentLookup<UserMoveOrder> _userMove;
        private ComponentLookup<AttackMoveTag> _attackMove;
        private ComponentLookup<FormationMemberState> _member;
        private ComponentLookup<FormationSlotMemory> _slotMemory;
        private ComponentLookup<DeathAnimationState> _dying;
        private ComponentLookup<GuardPoint> _guard;
        private ComponentLookup<NavLayerIndex> _layer;
        private ComponentLookup<FlyingTag> _flying;
        private ComponentLookup<SteeringDesiredDir> _steerDir;
        private ComponentLookup<FlowDesiredDir> _flowDir;
        private ComponentLookup<FactionTag> _faction;
        private ComponentLookup<BuildingTag> _building;
        private ComponentLookup<EmplacedEngineTag> _emplaced;
        private ComponentLookup<BuildingSize> _buildingSize;
        private ComponentLookup<Radius> _radius;
        private EntityStorageInfoLookup _exists;
        private UnitSpeedModifiers.Lookups _speedMods;

        private EntityQuery _moveQuery;

        // Stand-ins for optional singletons: a job field must always hold an
        // allocated container, even on the ticks it is not consulted.
        private NativeArray<byte> _dummyBytes;
        private NativeParallelMultiHashMap<int, Entity> _dummyHash;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _moveQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform>()
                .WithAllRW<DesiredDestination>()
                .WithNone<DeathAnimationState>()
                .WithNone<TheWaningBorder.Entities.Launched>()   // airborne: LaunchSystem owns the transform
                .Build();

            _xform = state.GetComponentLookup<LocalTransform>(true);
            _xformRW = state.GetComponentLookup<LocalTransform>(false);
            _traversal = state.GetComponentLookup<LayerTraversalState>(true);
            _smoothed = state.GetComponentLookup<SmoothedDirection>(true);
            _stuck = state.GetComponentLookup<StuckState>(true);
            _speedOverride = state.GetComponentLookup<FormationSpeedOverride>(true);
            _moveSpeed = state.GetComponentLookup<MoveSpeed>(true);
            _archer = state.GetComponentLookup<ArcherTag>(true);
            _archerState = state.GetComponentLookup<ArcherState>(true);
            _target = state.GetComponentLookup<Target>(true);
            _health = state.GetComponentLookup<Health>(true);
            _userMove = state.GetComponentLookup<UserMoveOrder>(true);
            _attackMove = state.GetComponentLookup<AttackMoveTag>(true);
            _member = state.GetComponentLookup<FormationMemberState>(true);
            _slotMemory = state.GetComponentLookup<FormationSlotMemory>(true);
            _dying = state.GetComponentLookup<DeathAnimationState>(true);
            _guard = state.GetComponentLookup<GuardPoint>(true);
            _layer = state.GetComponentLookup<NavLayerIndex>(true);
            _flying = state.GetComponentLookup<FlyingTag>(true);
            _steerDir = state.GetComponentLookup<SteeringDesiredDir>(true);
            _flowDir = state.GetComponentLookup<FlowDesiredDir>(true);
            _faction = state.GetComponentLookup<FactionTag>(true);
            _building = state.GetComponentLookup<BuildingTag>(true);
            _emplaced = state.GetComponentLookup<EmplacedEngineTag>(true);
            _buildingSize = state.GetComponentLookup<BuildingSize>(true);
            _radius = state.GetComponentLookup<Radius>(true);
            _exists = state.GetEntityStorageInfoLookup();
            _speedMods = UnitSpeedModifiers.Lookups.Create(ref state);

            _dummyBytes = new NativeArray<byte>(1, Allocator.Persistent);
            _dummyHash = new NativeParallelMultiHashMap<int, Entity>(1, Allocator.Persistent);
        }

        public void OnDestroy(ref SystemState state)
        {
            _speedMods.Dispose();
            if (_dummyBytes.IsCreated) _dummyBytes.Dispose();
            if (_dummyHash.IsCreated) _dummyHash.Dispose();
            NavSurfaceField.Dispose();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            // Drain every in-flight job touching the types read here (flow /
            // steering write the direction components this consumes).
            state.CompleteDependency();

            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            // Phase 1's buffer is created FIRST so it plays back before the
            // job's (ECBs from one singleton play back in creation order).
            var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

            // ── PHASE 1: MoveCommand -> DesiredDestination ──────────────
            foreach (var (mc, entity) in SystemAPI.Query<RefRO<MoveCommand>>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<BuildingTag>(entity)
                    || SystemAPI.HasComponent<EmplacedEngineTag>(entity))   // bolted to a wall deck
                {
                    TransientState.Clear<MoveCommand>(em, ecb, entity);
                    if (em.HasComponent<DesiredDestination>(entity))
                    {
                        var dd = em.GetComponentData<DesiredDestination>(entity);
                        dd.Has = 0;
                        ecb.SetComponent(entity, dd);
                    }
                    continue;
                }

                if (TransientState.Active<GuardPoint>(em, entity))
                    ecb.SetComponent(entity, new GuardPoint { Position = mc.ValueRO.Destination, Has = 1 });

                if (!em.HasComponent<DesiredDestination>(entity))
                    ecb.AddComponent(entity, new DesiredDestination { Position = mc.ValueRO.Destination, Has = 1 });
                else
                    ecb.SetComponent(entity, new DesiredDestination { Position = mc.ValueRO.Destination, Has = 1 });

                if (em.HasComponent<Target>(entity))
                    ecb.SetComponent(entity, new Target { Value = Entity.Null });

                TransientState.SetFlag<UserMoveOrder>(ecb, entity);

                if (TransientState.Active<SmoothedDirection>(em, entity))
                    ecb.SetComponent(entity, new SmoothedDirection { Value = float3.zero });
                if (TransientState.Active<StuckState>(em, entity))
                    ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });

                TransientState.Clear<MoveCommand>(em, ecb, entity);
            }

            // ── PHASE 1b: AttackMoveCommand -> DesiredDestination ───────
            foreach (var (amc, entity) in SystemAPI.Query<RefRO<AttackMoveCommand>>().WithEntityAccess())
            {
                if (SystemAPI.HasComponent<BuildingTag>(entity)
                    || SystemAPI.HasComponent<EmplacedEngineTag>(entity))
                {
                    TransientState.Clear<AttackMoveCommand>(em, ecb, entity);
                    continue;
                }

                if (TransientState.Active<GuardPoint>(em, entity))
                    ecb.SetComponent(entity, new GuardPoint { Position = amc.ValueRO.Destination, Has = 1 });

                if (!em.HasComponent<DesiredDestination>(entity))
                    ecb.AddComponent(entity, new DesiredDestination { Position = amc.ValueRO.Destination, Has = 1 });
                else
                    ecb.SetComponent(entity, new DesiredDestination { Position = amc.ValueRO.Destination, Has = 1 });

                if (em.HasComponent<Target>(entity))
                    ecb.SetComponent(entity, new Target { Value = Entity.Null });

                TransientState.SetFlag<AttackMoveTag>(ecb, entity);

                TransientState.Clear<UserMoveOrder>(em, ecb, entity);

                if (TransientState.Active<SmoothedDirection>(em, entity))
                    ecb.SetComponent(entity, new SmoothedDirection { Value = float3.zero });
                if (TransientState.Active<StuckState>(em, entity))
                    ecb.SetComponent(entity, new StuckState { Counter = 0, LastAttempt = 0 });

                TransientState.Clear<AttackMoveCommand>(em, ecb, entity);
            }

            // ── PHASE 2: integrate units toward DesiredDestination ──────
            // Dying units (DeathAnimationState) are excluded so a corpse stops
            // instantly and doesn't slide through its death animation.
            int count = _moveQuery.CalculateEntityCount();
            if (count == 0) return;

            _xform.Update(ref state);
            _xformRW.Update(ref state);
            _traversal.Update(ref state);
            _smoothed.Update(ref state);
            _stuck.Update(ref state);
            _speedOverride.Update(ref state);
            _moveSpeed.Update(ref state);
            _archer.Update(ref state);
            _archerState.Update(ref state);
            _target.Update(ref state);
            _health.Update(ref state);
            _userMove.Update(ref state);
            _attackMove.Update(ref state);
            _member.Update(ref state);
            _slotMemory.Update(ref state);
            _dying.Update(ref state);
            _guard.Update(ref state);
            _layer.Update(ref state);
            _flying.Update(ref state);
            _steerDir.Update(ref state);
            _flowDir.Update(ref state);
            _faction.Update(ref state);
            _building.Update(ref state);
            _emplaced.Update(ref state);
            _buildingSize.Update(ref state);
            _radius.Update(ref state);
            _exists.Update(ref state);
            _speedMods.Update(ref state);

            // Neighbour lookup for the crowded-arrival test. Optional: the hash
            // is allocated lazily by SpatialHashRebuildSystem, so early ticks
            // simply fall back to the plain StopDistance arrival.
            bool hasHash = SystemAPI.TryGetSingleton<NavSpatialHash>(out var navHash)
                && navHash.Map.IsCreated;

            // The cost field (NavGridQuery's source): absent before the nav
            // bootstrap, in which case every cost test is skipped exactly as
            // NavGridQuery's "not bootstrapped" answers made it skip.
            bool hasGrid = SystemAPI.TryGetSingleton<NavGridSingleton>(out var grid)
                & SystemAPI.TryGetSingleton<NavCostField>(out var cost);
            hasGrid = hasGrid && cost.Cost.IsCreated && cost.Flags.IsCreated;

            var outXf = new NativeArray<LocalTransform>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var outEntity = new NativeArray<Entity>(count, Allocator.TempJob);
            var outFlag = new NativeArray<byte>(count, Allocator.TempJob);   // zeroed

            var job = new IntegrateUnitsJob
            {
                Dt = dt,
                HasHash = hasHash,
                HashMap = hasHash ? navHash.Map : _dummyHash,
                HashCellSize = hasHash ? navHash.CellSize : 1f,
                HasGrid = hasGrid,
                GridWidth = hasGrid ? grid.Width : 0,
                GridHeight = hasGrid ? grid.Height : 0,
                GridCellSize = hasGrid ? grid.CellSize : 1f,
                GridOrigin = hasGrid ? grid.Origin : float3.zero,
                Cost = hasGrid ? cost.Cost : _dummyBytes,
                CostFlags = hasGrid ? cost.Flags : _dummyBytes,
                CostWidth = hasGrid ? cost.Width : 0,
                CostHeight = hasGrid ? cost.Height : 0,
                CostLayerCount = hasGrid ? cost.LayerCount : 0,
                Surface = NavSurfaceField.GetSurface(),
                Mask = NavSurfaceField.GetMask(),
                Xform = _xform,
                Traversal = _traversal,
                Smoothed = _smoothed,
                Stuck = _stuck,
                SpeedOverride = _speedOverride,
                MoveSpeedL = _moveSpeed,
                Archer = _archer,
                ArcherStateL = _archerState,
                TargetL = _target,
                HealthL = _health,
                UserMove = _userMove,
                AttackMove = _attackMove,
                Member = _member,
                SlotMemory = _slotMemory,
                Dying = _dying,
                Guard = _guard,
                Layer = _layer,
                Flying = _flying,
                SteerDir = _steerDir,
                FlowDir = _flowDir,
                Faction = _faction,
                Building = _building,
                Emplaced = _emplaced,
                Sizes = _buildingSize,
                Radii = _radius,
                Exists = _exists,
                SpeedMods = _speedMods,
                Ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter(),
                OutXf = outXf,
                OutEntity = outEntity,
                OutFlag = outFlag,
            };
            var integrate = job.ScheduleParallel(_moveQuery, state.Dependency);

            var apply = new ApplyIntegratedTransformsJob
            {
                Entities = outEntity,
                Transforms = outXf,
                Written = outFlag,
                Xform = _xformRW,
            }.Schedule(count, 64, integrate);

            // Complete here: see "The jobs complete inside OnUpdate" above.
            apply.Complete();
            state.Dependency = apply;

            outXf.Dispose();
            outEntity.Dispose();
            outFlag.Dispose();
        }
    }

    /// <summary>
    /// Phase 2 of <see cref="UnitIntegratorSystem"/>: one unit's step. Writes
    /// only its own DesiredDestination, its own output slot, and ECB commands
    /// keyed by its own query index.
    /// </summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal partial struct IntegrateUnitsJob : IJobEntity
    {
        private const float StopDistance = 0.5f;

        /// <summary>Arrival window for a unit settling into a FORMATION SLOT.
        /// Tighter than the loose-unit window because the slot is a designed
        /// position rather than "somewhere near the flag", and eight units each
        /// parked anywhere in a 0.5 m disc do not read as a formation. Matches
        /// FormationGroupSystem.InPlaceDistance so the marching shape and the
        /// stopped shape are the same shape.</summary>
        private const float FormationStopDistance = 0.15f;
        private const float DefaultMoveSpeed = 3.5f;
        // 8 -> 6 rad/s (~460 -> ~344 deg/s). At 460 deg/s a unit changing
        // heading completed the turn inside a couple of frames, which reads
        // as the model snapping between facings rather than turning. Still
        // brisk enough that a unit is pointing where it walks before it has
        // covered its own radius, which is what stops it looking like it is
        // sliding sideways.
        private const float TurnSpeed = 6f;             // rad/s (~344 deg/s)

        // Mirror of SteeringSystem.SeparationRadius. Two units cannot stand
        // closer than this, which is the whole reason for the crowded-arrival
        // rule below: with a 0.5 m StopDistance and a 1.5 m separation ring,
        // two units sent to the SAME point can never both satisfy arrival.
        private const float SeparationClearance = 1.5f;
        /// <summary>Arrival window when the ordered point is already occupied
        /// by someone who got there first.</summary>
        private const float CrowdStopDistance = StopDistance + SeparationClearance;

        /// <summary>How near the goal another unit must be before it genuinely
        /// blocks arrival. A unit `d` from the goal keeps us at least
        /// (SeparationClearance - d) away, which only beats StopDistance while
        /// d &lt; SeparationClearance - StopDistance. Past that we can walk
        /// around it, and declaring arrival would be giving up early.</summary>
        private const float GoalBlockedRadius = SeparationClearance - StopDistance;

        // Arrival braking. SteeringSystem normalises its force vector, so a
        // unit one metre from its goal, with flow and separation very nearly
        // cancelling, still moved at FULL speed along whatever tiny residual
        // survived — the literal circling motion at a destination. Taper the
        // step over the last few metres so units decelerate into the goal
        // instead of skating around it.
        private const float ArriveSlowRadius = 2.5f;
        private const float MinArriveSpeedScale = 0.2f;
        // Per-step terrain backstop: the PassabilityGrid cell mask is the
        // single authority (see the TERRAIN CHECK in the step loop) — no
        // independent slope constants here any more.
        private const float SmoothRate = 12f;

        // Max walking-surface drop per step. Terrain is continuous so real
        // slopes never trip this; only ledges (bridge deck edges) do.
        private const float MaxLedgeDrop = 2f;

        /// <summary>NavGridQuery.SnapMaxCellRadius.</summary>
        private const int SnapMaxCellRadius = NavGridQuery.SnapMaxCellRadius;

        public float Dt;

        public bool HasHash;
        [ReadOnly] public NativeParallelMultiHashMap<int, Entity> HashMap;
        public float HashCellSize;

        public bool HasGrid;
        public int GridWidth, GridHeight;
        public float GridCellSize;
        public float3 GridOrigin;
        [ReadOnly] public NativeArray<byte> Cost;
        [ReadOnly] public NativeArray<byte> CostFlags;
        public int CostWidth, CostHeight, CostLayerCount;

        public NavSurface Surface;
        public NavTerrainMask Mask;

        [ReadOnly] public ComponentLookup<LocalTransform> Xform;
        [ReadOnly] public ComponentLookup<LayerTraversalState> Traversal;
        [ReadOnly] public ComponentLookup<SmoothedDirection> Smoothed;
        [ReadOnly] public ComponentLookup<StuckState> Stuck;
        [ReadOnly] public ComponentLookup<FormationSpeedOverride> SpeedOverride;
        [ReadOnly] public ComponentLookup<MoveSpeed> MoveSpeedL;
        [ReadOnly] public ComponentLookup<ArcherTag> Archer;
        [ReadOnly] public ComponentLookup<ArcherState> ArcherStateL;
        [ReadOnly] public ComponentLookup<Target> TargetL;
        [ReadOnly] public ComponentLookup<Health> HealthL;
        [ReadOnly] public ComponentLookup<UserMoveOrder> UserMove;
        [ReadOnly] public ComponentLookup<AttackMoveTag> AttackMove;
        [ReadOnly] public ComponentLookup<FormationMemberState> Member;
        [ReadOnly] public ComponentLookup<FormationSlotMemory> SlotMemory;
        [ReadOnly] public ComponentLookup<DeathAnimationState> Dying;
        [ReadOnly] public ComponentLookup<GuardPoint> Guard;
        [ReadOnly] public ComponentLookup<NavLayerIndex> Layer;
        [ReadOnly] public ComponentLookup<FlyingTag> Flying;
        [ReadOnly] public ComponentLookup<SteeringDesiredDir> SteerDir;
        [ReadOnly] public ComponentLookup<FlowDesiredDir> FlowDir;
        [ReadOnly] public ComponentLookup<FactionTag> Faction;
        [ReadOnly] public ComponentLookup<BuildingTag> Building;
        [ReadOnly] public ComponentLookup<EmplacedEngineTag> Emplaced;
        [ReadOnly] public ComponentLookup<BuildingSize> Sizes;
        [ReadOnly] public ComponentLookup<Radius> Radii;
        [ReadOnly] public EntityStorageInfoLookup Exists;
        public UnitSpeedModifiers.Lookups SpeedMods;

        public EntityCommandBuffer.ParallelWriter Ecb;

        [NativeDisableParallelForRestriction] public NativeArray<LocalTransform> OutXf;
        [NativeDisableParallelForRestriction] public NativeArray<Entity> OutEntity;
        [NativeDisableParallelForRestriction] public NativeArray<byte> OutFlag;

        public void Execute([EntityIndexInQuery] int k, Entity entity,
            in LocalTransform xf, ref DesiredDestination dd)
        {
            OutEntity[k] = entity;
            // OutFlag[k] stays 0 (zeroed) unless a transform is produced.

            if (dd.Has == 0) return;

            // Buildings should never move — and neither should an
            // emplaced engine, whose Y is the wall deck it stands on and
            // must never be re-snapped to the ground under the wall.
            if (Building.HasComponent(entity) || Emplaced.HasComponent(entity))
            {
                dd.Has = 0;
                return;
            }

            // task-112 M5 -- LayerTransitionSystem owns the unit's
            // position while a climb / gate traversal is in flight.
            if (Traversal.HasComponent(entity)) return;

            // Lazy-enable ECS scratch (deferred via ECB). Pre-added
            // DISABLED at spawn (TransientState UnitSet6).
            if (!Active(ref Smoothed, entity))
                Set(k, entity, new SmoothedDirection { Value = float3.zero });
            if (!Active(ref Stuck, entity))
                Set(k, entity, new StuckState { Counter = 0, LastAttempt = 0 });

            // Resolve effective speed: FormationSpeedOverride > MoveSpeed > default.
            float speed = DefaultMoveSpeed;
            if (Active(ref SpeedOverride, entity))
            {
                var fso = SpeedOverride[entity];
                if (fso.Value > 0) speed = fso.Value;
            }
            else if (MoveSpeedL.HasComponent(entity))
            {
                var ms = MoveSpeedL[entity];
                if (ms.Value > 0) speed = ms.Value;
            }

            // Spell slows, veil / Suppression drag, Fortify, haste — one
            // shared definition, so FormationGroupSystem can size a group's
            // speed from exactly what this applies.
            speed *= UnitSpeedModifiers.Multiplier(ref SpeedMods, entity);
            if (speed <= 0f) return;

            // Archers in firing range: ARRIVE, don't just halt (clearing Has
            // makes the halt a real arrival RangedCombatSystem can fire from).
            // The metric matches RangedCombatSystem exactly (XZ distance,
            // height-scaled max range, measured to the target's edge). An
            // EXPLICIT player move order outranks the firing band.
            if (Archer.HasComponent(entity)
                && TargetL.HasComponent(entity)
                && !Active(ref UserMove, entity))
            {
                var tgt = TargetL[entity];
                if (tgt.Value != Entity.Null && Exists.Exists(tgt.Value)
                    && Xform.HasComponent(tgt.Value)
                    && HealthL.HasComponent(tgt.Value)
                    && HealthL[tgt.Value].Value > 0)
                {
                    float3 myP = xf.Position;
                    float3 tgtPos = Xform[tgt.Value].Position;
                    // minRange 0 is the DEFAULT and a valid value — only
                    // siege keeps a dead zone; must agree with RangedCombatSystem.
                    float maxRange = 25f, minRange = 0f;
                    if (ArcherStateL.HasComponent(entity))
                    {
                        var ast = ArcherStateL[entity];
                        if (ast.MaxRange > 0) maxRange = ast.MaxRange;
                        minRange = math.max(0f, ast.MinRange);
                    }
                    maxRange *= TheWaningBorder.Systems.Combat.HeightAdvantage
                        .Multiplier(myP.y, tgtPos.y);
                    float edge = TheWaningBorder.Core.TargetGeometry
                        .SurfaceDistXZ(myP, tgtPos, tgt.Value, in Sizes, in Radii);
                    // Halt only inside the FIRING band. Below min range the
                    // unit must stay mobile or RangedCombat's retreat order
                    // would be arrived-out right here every frame.
                    if (edge <= maxRange && edge >= minRange)
                    {
                        dd.Has = 0;
                        return;
                    }
                }
            }

            float3 pos = xf.Position;
            float3 goal = dd.Position;

            float3 to = goal - pos;
            to.y = 0f;
            float distSqr = math.lengthsq(to);
            float dist = math.sqrt(distSqr);

            // A unit with a live Target is CHASING: neither the crowd-arrival
            // rule nor the arrival brake applies.
            bool chasing = TargetL.HasComponent(entity)
                && TargetL[entity].Value != Entity.Null;

            // Arrived — inside the ordinary stop window, or as close as the
            // crowd physically permits (StopDistance < the separation ring).
            // A FORMATION MEMBER IS NOT A CROWD (distinct slot each), and gets
            // the tighter window.
            bool inFormation = Active(ref Member, entity);
            float stop = inFormation ? FormationStopDistance : StopDistance;

            bool arrived = distSqr <= (stop * stop);
            if (!arrived && !chasing && HasHash && !inFormation
                && distSqr <= CrowdStopDistance * CrowdStopDistance)
                arrived = GoalTakenByCloserNeighbour(goal, pos, entity, dist);

            if (arrived)
            {
                dd.Has = 0;
                // A finished PLAYER MOVE makes where the unit stopped its
                // guard point (docs/Design/Stances.md §2). Not for a chase.
                if (!chasing && Active(ref UserMove, entity) && Active(ref Guard, entity))
                    Ecb.SetComponent(k, entity, new GuardPoint { Position = pos, Has = 1 });
                Clear(ref UserMove, k, entity);
                Clear(ref AttackMove, k, entity);
                // Only for a unit travelling ALONE — the group owns a member's
                // FormationSpeedOverride lifecycle.
                if (!Active(ref Member, entity) && Active(ref SpeedOverride, entity))
                    Ecb.SetComponentEnabled<FormationSpeedOverride>(k, entity, false);
                return;
            }

            float3 dir = to / math.max(1e-5f, dist);

            // Which nav layer is this unit on (0 = Ground, 1 = Rampart)?
            byte unitLayer = 0;
            if (Layer.HasComponent(entity))
                unitLayer = Layer[entity].Layer;
            bool onRampart = unitLayer == NavLayerIndex.LayerRampart;
            // A FLYING unit (docs/Design/Age_1_Runai.md §6.4) ignores
            // pathfinding: straight at the goal, over walls and cliffs.
            bool flying = Flying.HasComponent(entity);

            // === PATHFINDING DIRECTION (M4 preference chain) ===
            //   1. SteeringDesiredDir (M2) -- flow + local avoidance
            //   2. FlowDesiredDir    (M1) -- raw flow direction
            //   3. direct-line       -- straight at the goal cell
            // Rampart units skip flow/steering (ground-layer fields).
            if (!onRampart && !flying && Active(ref SteerDir, entity))
            {
                var sdd = SteerDir[entity];
                if (sdd.HasValue != 0 && math.lengthsq(sdd.Value) > 1e-8f)
                    dir = math.normalize(new float3(sdd.Value.x, 0f, sdd.Value.z));
            }
            else if (!onRampart && !flying && Active(ref FlowDir, entity))
            {
                var fdd = FlowDir[entity];
                if (fdd.HasValue != 0 && math.lengthsq(fdd.Value) > 1e-8f)
                    dir = math.normalize(new float3(fdd.Value.x, 0f, fdd.Value.z));
            }

            // === Smoothing ===
            float3 smoothedDir = dir;
            if (Active(ref Smoothed, entity))
            {
                var sd = Smoothed[entity];
                if (math.lengthsq(sd.Value) > 1e-8f)
                    smoothedDir = math.normalizesafe(math.lerp(sd.Value, dir, math.saturate(SmoothRate * Dt)));
                Ecb.SetComponent(k, entity, new SmoothedDirection { Value = smoothedDir });
            }

            var t = xf;

            // === Cosmetic rotation ===
            if (math.lengthsq(smoothedDir) > 1e-8f)
            {
                float3 fwd = math.normalize(new float3(smoothedDir.x, 0f, smoothedDir.z));
                quaternion targetRot = quaternion.RotateY(math.atan2(fwd.x, fwd.z));
                float maxTurn = TurnSpeed * Dt;
                SmoothSlerp(in t.Rotation, in targetRot, maxTurn, out var smoothed);
                t.Rotation = smoothed;
            }

            // === Step === (arrival braking over the last ArriveSlowRadius m)
            float arriveScale = (!chasing && dist < ArriveSlowRadius)
                ? math.max(MinArriveSpeedScale, dist / ArriveSlowRadius)
                : 1f;
            float step = math.min(speed * arriveScale * Dt, dist);
            float3 nextPos = pos + smoothedDir * step;

            // === PASSABILITY (cost field, terrain mask, bridge deck, ledge) ===
            byte navFaction = Faction.HasComponent(entity)
                ? (byte)Faction[entity].Value
                : (byte)0xFF; // no faction -> never matches an owner
            bool blocked = !flying
                && StepBlocked(pos, nextPos, t.Position.y, unitLayer, navFaction, onRampart);

            // === WALL SLIDE (2026-10-25) ===
            // A blocked step used to be NO step: the unit stood still, then
            // jittered left-right sidesteps, and a formation member (whose
            // stuck counter is reset rather than its order dropped) did that
            // for good. A cliff edge at any angle is a staircase of cells, so
            // every concave corner of it caught units marching along or into
            // it. Slide instead: take the free axis of the blocked step, the
            // larger component first, under the very same rules as the step.
            if (blocked)
            {
                float3 d = nextPos - pos;
                float3 sx = new float3(pos.x + d.x, nextPos.y, pos.z);
                float3 sz = new float3(pos.x, nextPos.y, pos.z + d.z);
                bool xFirst = math.abs(d.x) >= math.abs(d.z);
                float3 a = xFirst ? sx : sz, b = xFirst ? sz : sx;
                float minSlide = step * MinSlideFraction;
                if (math.distancesq(a, pos) >= minSlide * minSlide
                    && !StepBlocked(pos, a, t.Position.y, unitLayer, navFaction, onRampart))
                { nextPos = a; blocked = false; }
                else if (math.distancesq(b, pos) >= minSlide * minSlide
                    && !StepBlocked(pos, b, t.Position.y, unitLayer, navFaction, onRampart))
                { nextPos = b; blocked = false; }
            }

            // === STUCK DETECTION ===
            if (blocked)
            {
                if (Active(ref Stuck, entity))
                {
                    var stuck = Stuck[entity];
                    stuck.Counter = (byte)math.min(stuck.Counter + 1, 255);

                    if (stuck.Counter > 30)
                    {
                        // Tier 3: snap to the nearest passable cell when the
                        // current one is impassable. Ground-only.
                        bool escaped = false;
                        int2 here = WorldToCellInt2(pos);
                        if (!onRampart && here.x != int.MinValue && !IsCellPassable(here))
                        {
                            if (SnapToWalkable(pos, out var escapePos))
                            {
                                escapePos.y = Surface.SurfaceHeight(escapePos.x, escapePos.z, pos.y);
                                // Ledge guard applies to escapes too.
                                if (pos.y - escapePos.y <= MaxLedgeDrop)
                                {
                                    t.Position = escapePos;
                                    Ecb.SetComponent(k, entity, new StuckState { Counter = 0, LastAttempt = 0 });
                                    escaped = true;
                                }
                            }
                        }

                        if (!escaped && Active(ref Member, entity))
                        {
                            // A FORMATION MEMBER DOES NOT ABANDON ITS ORDER
                            // HERE — the group owns its progress (spot
                            // steering + tether fuse).
                            Ecb.SetComponent(k, entity, new StuckState { Counter = 0, LastAttempt = 0 });
                        }
                        else if (!escaped)
                        {
                            dd.Has = 0;
                            Clear(ref UserMove, k, entity);
                            Clear(ref AttackMove, k, entity);
                            if (Active(ref SpeedOverride, entity))
                                Ecb.SetComponentEnabled<FormationSpeedOverride>(k, entity, false);
                            Ecb.SetComponent(k, entity, new StuckState { Counter = 0, LastAttempt = 0 });

                            // Tell the leash the order is abandoned, or
                            // TargetingSystem's return-to-guard re-issues this
                            // exact destination next tick.
                            if (Active(ref Guard, entity))
                            {
                                var gp = Guard[entity];
                                if (gp.Has != 0)
                                    Set(k, entity, new GuardSuppressed { Point = gp.Position });
                            }
                        }
                    }
                    else if (stuck.Counter > 5)
                    {
                        // Tier 2: try perpendicular direction.
                        byte attempt = (byte)(stuck.LastAttempt == 1 ? 2 : 1);
                        float3 perp = attempt == 1
                            ? new float3(-smoothedDir.z, 0f, smoothedDir.x)
                            : new float3(smoothedDir.z, 0f, -smoothedDir.x);
                        perp = math.normalizesafe(perp);

                        float3 perpPos = pos + perp * step;
                        bool perpBlocked = false;
                        int2 perpCell = WorldToCellInt2(perpPos);
                        if (perpCell.x != int.MinValue && !IsCellPassable(perpCell, unitLayer))
                            perpBlocked = true;

                        if (!perpBlocked)
                        {
                            perpPos.y = onRampart
                                ? Surface.RampartSurfaceY(perpPos.x, perpPos.z, t.Position.y)
                                : Surface.SurfaceHeight(perpPos.x, perpPos.z, t.Position.y);
                            // Ledge guard applies to sidesteps too.
                            if (t.Position.y - perpPos.y > MaxLedgeDrop)
                                perpBlocked = true;
                        }

                        if (!perpBlocked)
                            t.Position = perpPos;

                        stuck.LastAttempt = attempt;
                        Ecb.SetComponent(k, entity, stuck);
                    }
                    else
                    {
                        Ecb.SetComponent(k, entity, stuck);
                    }
                }

                OutXf[k] = t;
                OutFlag[k] = 1;
                return;
            }

            // Not blocked: reset stuck counter (skipped when already zero).
            if (Active(ref Stuck, entity))
            {
                var cur = Stuck[entity];
                if (cur.Counter != 0 || cur.LastAttempt != 0)
                    Ecb.SetComponent(k, entity, new StuckState { Counter = 0, LastAttempt = 0 });
            }

            // === Layer-aware height snap (task-112 M5) ===
            // Ground -> walking surface (ground, or a bridge deck within
            // step-up reach). Rampart -> the deck / wall-walk height.
            if (onRampart)
                nextPos.y = Surface.RampartSurfaceY(nextPos.x, nextPos.z, t.Position.y);
            else
                nextPos.y = Surface.SurfaceHeight(nextPos.x, nextPos.z, t.Position.y);

            t.Position = nextPos;
            OutXf[k] = t;
            OutFlag[k] = 1;
        }

        /// <summary>Shortest slide worth taking, as a share of the full step:
        /// below it the free axis is a grazing sliver, not a way along the
        /// wall.</summary>
        private const float MinSlideFraction = 0.2f;

        /// <summary>
        /// Can a unit step from <paramref name="pos"/> to <paramref name="next"/>?
        /// The cost field (faction-aware: a gate admits its owner; enforced
        /// only from a passable cell, so a unit spawned inside a footprint can
        /// walk out), then on the ground the terrain mask the bake uses, the
        /// bridge-deck step-up rule and the ledge guard. ONE test for the step
        /// and the wall slide, so a slide can never take a step the step would
        /// refuse.
        /// </summary>
        private bool StepBlocked(float3 pos, float3 next, float curY, byte unitLayer,
            byte navFaction, bool onRampart)
        {
            int2 nextCell = WorldToCellInt2(next);
            if (nextCell.x != int.MinValue && !IsCellPassableFor(nextCell, unitLayer, navFaction))
            {
                int2 currentCell = WorldToCellInt2(pos);
                if (currentCell.x != int.MinValue
                    && IsCellPassableFor(currentCell, unitLayer, navFaction))
                    return true;
            }

            // === TERRAIN CHECK (ground only — the rampart deck is flat) ===
            // Backstop against cost-grid rounding at cell edges, against the
            // SAME PassabilityGrid mask the pathfinding bake uses. Gated on
            // the mask being READY: an unbuilt mask reads all-walkable.
            if (onRampart || Mask.Ready == 0) return false;
            var cell = Mask.WorldToCell(next);
            if (Mask.GetCell(cell) == TheWaningBorder.World.Terrain.PassabilityGrid.TerrainBlocked)
                return true;
            if (Mask.IsBridgeDeckOnly(cell))
            {
                // Deck-only cell: admit the step only when the deck is within
                // step-up reach (same MountStepLimit rule as the height snap,
                // so admission and snap never disagree).
                bool onDeck = Surface.BridgeCount > 0
                    && Surface.TryGetDeckHeight(next.x, next.z, out float deckY)
                    && deckY - curY <= TheWaningBorder.World.Terrain.BridgeSurface.MountStepLimit;
                if (!onDeck) return true;
            }

            // === LEDGE GUARD === units leave a bridge via its ramps.
            float nextSurf = Surface.SurfaceHeight(next.x, next.z, curY);
            return curY - nextSurf > MaxLedgeDrop;
        }

        // ── Crowded arrival ─────────────────────────────────────────────

        /// <summary>
        /// True when another unit already holds the ordered point and is
        /// wedged between us and it. Existential over the 3x3 spatial-hash
        /// ring around the goal, so the result does not depend on bucket
        /// iteration order. Reads PRE-TICK positions (the job never writes
        /// LocalTransform).
        /// </summary>
        private bool GoalTakenByCloserNeighbour(float3 goal, float3 pos, Entity self, float myDist)
        {
            uint myKey = FormationKinKey(self);

            NavSpatialHash.WorldToCell(in goal, HashCellSize, out int cx, out int cz);
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int key = NavSpatialHash.PackKey(cx + dx, cz + dz);
                    if (!HashMap.TryGetFirstValue(key, out Entity other, out var it)) continue;
                    do
                    {
                        if (other == self) continue;
                        if (!Xform.HasComponent(other)) continue;
                        // A formation-mate does not block us (steering exempts
                        // same-order units from pushing each other).
                        if (myKey != 0 && FormationKinKey(other) == myKey) continue;
                        // A corpse mid-death-animation is about to stop
                        // occupying the point; don't settle behind it.
                        if (Active(ref Dying, other)) continue;
                        // A unit still walking a PLAYER move is passing
                        // through, not holding the point: its order clears
                        // the moment it arrives, so one that has really
                        // settled on our goal still blocks us. Without this
                        // a file spread along a wall-walk (a cell per man,
                        // SelectionOrders.DeckSlots) stopped short wherever
                        // a man bound further along walked over another's
                        // slot (2026-10-20).
                        //
                        // ON THE WALL-WALK ONLY (2026-10-25). On the ground
                        // every AI move is a UserMoveOrder too, so a dozen
                        // men sent to ONE point never counted each other:
                        // the crowd shoved the nearest off the exact spot,
                        // nobody arrived, and they jostled there for good —
                        // the AI then saw them as busy and its musters timed
                        // out. On the ground a mover holding the point
                        // blocks us again, as before.
                        if (Active(ref UserMove, other) && Layer.HasComponent(other)
                            && Layer[other].Layer == NavLayerIndex.LayerRampart) continue;

                        float3 op = Xform[other].Position;

                        float gx = op.x - goal.x, gz = op.z - goal.z;
                        float otherToGoal = math.sqrt(gx * gx + gz * gz);

                        // The blocker must actually be SITTING ON the goal.
                        if (otherToGoal >= GoalBlockedRadius) continue;
                        if (otherToGoal >= myDist) continue; // not ahead of us

                        float sx = op.x - pos.x, sz = op.z - pos.z;
                        float otherToMe = math.sqrt(sx * sx + sz * sz);
                        if (otherToMe <= SeparationClearance) return true;
                    } while (HashMap.TryGetNextValue(out other, ref it));
                }
            }
            return false;
        }

        /// <summary>The unit's remembered formation-order key
        /// (FormationSlotMemory.GroupKey), or 0 when it has none or is chasing
        /// a target. Mirrors SteeringSystem's push-exemption key exactly.</summary>
        private uint FormationKinKey(Entity e)
        {
            if (!Active(ref SlotMemory, e)) return 0;
            if (TargetL.HasComponent(e) && TargetL[e].Value != Entity.Null)
                return 0;
            return SlotMemory[e].GroupKey;
        }

        // ── NavGridQuery twins (same rules, same index math) ────────────

        /// <summary>NavGridQuery.WorldToCellInt2: int.MinValue cells while
        /// the grid is not bootstrapped.</summary>
        private int2 WorldToCellInt2(float3 world)
        {
            if (!HasGrid) return new int2(int.MinValue, int.MinValue);
            int cx = (int)math.floor((world.x - GridOrigin.x) / GridCellSize);
            int cz = (int)math.floor((world.z - GridOrigin.z) / GridCellSize);
            return new int2(cx, cz);
        }

        /// <summary>NavGridQuery.IsCellPassable(int2) — layer 0.</summary>
        private bool IsCellPassable(int2 cell)
        {
            if (!HasGrid) return false;
            if (cell.x < 0 || cell.x >= GridWidth || cell.y < 0 || cell.y >= GridHeight) return false;
            return Cost[cell.y * GridWidth + cell.x] != NavCostField.CostImpassable;
        }

        /// <summary>NavGridQuery.IsCellPassable(int2, byte).</summary>
        private bool IsCellPassable(int2 cell, byte layer)
        {
            if (!HasGrid) return false;
            if (layer >= CostLayerCount) return false;
            if (cell.x < 0 || cell.x >= GridWidth || cell.y < 0 || cell.y >= GridHeight) return false;
            int layerArea = CostWidth * CostHeight;
            int idx = layer * layerArea + cell.y * CostWidth + cell.x;
            return Cost[idx] != NavCostField.CostImpassable;
        }

        /// <summary>NavGridQuery.IsCellPassableFor: a gate (CostConditional)
        /// admits only its owner, and only player slots can own one.</summary>
        private bool IsCellPassableFor(int2 cell, byte layer, byte faction)
        {
            if (!HasGrid) return false;
            if (layer >= CostLayerCount) return false;
            if (cell.x < 0 || cell.x >= GridWidth || cell.y < 0 || cell.y >= GridHeight) return false;

            int layerArea = CostWidth * CostHeight;
            int idx = layer * layerArea + cell.y * CostWidth + cell.x;

            byte c = Cost[idx];
            if (c == NavCostField.CostImpassable) return false;
            if (c != NavCostField.CostConditional) return true;

            if (faction > NavCostField.FlagOwnerMask) return false; // not a player slot
            byte flags = CostFlags[idx];
            return (flags & NavCostField.FlagOwnerMask) == faction;
        }

        /// <summary>NavGridQuery.SnapToWalkable: the start cell if walkable,
        /// else expanding Chebyshev rings visited row-major (z, then x), so
        /// every peer picks the same winner. Keeps the caller's Y.</summary>
        private bool SnapToWalkable(float3 world, out float3 snapped)
        {
            snapped = world;
            if (!HasGrid) return false;
            int cx = (int)math.floor((world.x - GridOrigin.x) / GridCellSize);
            int cz = (int)math.floor((world.z - GridOrigin.z) / GridCellSize);
            if (TryCellCentre(cx, cz, world.y, out snapped)) return true;

            for (int r = 1; r <= SnapMaxCellRadius; r++)
            {
                int z0 = cz - r, z1 = cz + r, x0 = cx - r, x1 = cx + r;
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (x > x0 && x < x1 && z > z0 && z < z1) continue;
                        if (TryCellCentre(x, z, world.y, out snapped)) return true;
                    }
                }
            }
            snapped = world;
            return false;
        }

        private bool TryCellCentre(int cx, int cz, float worldY, out float3 snapped)
        {
            if (cx < 0 || cx >= GridWidth || cz < 0 || cz >= GridHeight
                || Cost[cz * GridWidth + cx] == NavCostField.CostImpassable)
            {
                snapped = float3.zero;
                return false;
            }
            snapped = new float3(
                GridOrigin.x + (cx + 0.5f) * GridCellSize,
                worldY,
                GridOrigin.z + (cz + 0.5f) * GridCellSize);
            return true;
        }

        // ── TransientState twins (lookup read + ParallelWriter write) ───

        private static bool Active<T>(ref ComponentLookup<T> lookup, Entity e)
            where T : unmanaged, IComponentData, IEnableableComponent
            => lookup.HasComponent(e) && lookup.IsComponentEnabled(e);

        private void Clear<T>(ref ComponentLookup<T> lookup, int k, Entity e)
            where T : unmanaged, IComponentData, IEnableableComponent
        {
            if (lookup.HasComponent(e))
                Ecb.SetComponentEnabled<T>(k, e, false);
        }

        /// <summary>TransientState.Set(ecb, ...): Add (set-value on a
        /// pre-added unit) + enable.</summary>
        private void Set<T>(int k, Entity e, T value)
            where T : unmanaged, IComponentData, IEnableableComponent
        {
            Ecb.AddComponent(k, e, value);
            Ecb.SetComponentEnabled<T>(k, e, true);
        }

        /// <summary>
        /// Slerp from current to target rotation, clamped to maxAngle radians.
        /// </summary>
        private static void SmoothSlerp(in quaternion from, in quaternion to, float maxAngle, out quaternion result)
        {
            float4 toVal = to.value;
            float dot = math.dot(from.value, toVal);
            if (dot < 0f) { toVal = -toVal; dot = -dot; }
            quaternion toFixed = new quaternion(toVal);

            if (dot > 0.9999f) { result = toFixed; return; }

            float angle = math.acos(math.clamp(dot, -1f, 1f)) * 2f;
            if (angle <= maxAngle) { result = toFixed; return; }

            float tParam = maxAngle / angle;
            result = math.slerp(from, toFixed, tParam);
        }
    }

    /// <summary>Second half of the integrator: write each produced transform
    /// to its own entity. Every slot names a distinct entity, so the parallel
    /// writes never alias.</summary>
    [BurstCompile(FloatMode = FloatMode.Deterministic, FloatPrecision = FloatPrecision.High)]
    internal struct ApplyIntegratedTransformsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> Entities;
        [ReadOnly] public NativeArray<LocalTransform> Transforms;
        [ReadOnly] public NativeArray<byte> Written;
        [NativeDisableParallelForRestriction] public ComponentLookup<LocalTransform> Xform;

        public void Execute(int i)
        {
            if (Written[i] == 0) return;
            Xform[Entities[i]] = Transforms[i];
        }
    }
}
