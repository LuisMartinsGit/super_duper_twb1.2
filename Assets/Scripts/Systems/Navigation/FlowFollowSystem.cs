// FlowFollowSystem.cs
// Writes FlowDesiredDir on every unit with a DesiredDestination.
//
// Pathfinding redesign (directive 2026-07-05): the per-leg slab sampling
// (portal path + cursor + per-tile cache) and the reactive angle sweep are
// GONE. A unit's direction now comes from exactly three sources, in order:
//
//   1. LOS-to-goal — straight bearing when the goal is directly visible
//      over the cost grid (smooth, unquantized motion on open ground).
//   2. The whole-map GOAL FLOW FIELD for (goalCell, faction), produced by
//      GoalFlowFieldSystem. Correct by construction — it is a global
//      integration from the goal, so concave obstacles ("U" shapes), long
//      walls, and multi-gap routes are all handled with one array read.
//      A NoDirection cell means the goal is PROVABLY unreachable from
//      here: the unit holds position instead of grinding into the blocker.
//   3. Direct-to-goal — only while the field is still integrating (the
//      producer budgets integrations per tick, so this covers a tick or
//      two after a fresh order).
//
// Formations need nothing special here: each unit's formation slot is its
// own DesiredDestination, hence its own field key; slots sharing a cell
// share a cached field. Player, AI, and Border commands all converge on
// DesiredDestination upstream, so all three drive the same machinery.
//
// Determinism notes:
//   * Per-unit job reads only shared immutable-in-frame data + the unit's
//     own components. Hash lookups are O(1) TryGetValue.
//   * No SystemAPI.Time reads, no randomness.
//
// Location: Assets/Scripts/Systems/Navigation/FlowFollowSystem.cs

using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    /// <summary>
    /// Per-tick sampler: LOS bearing, else whole-map goal-field direction,
    /// else direct bearing while the field integrates. Writes
    /// <see cref="FlowDesiredDir"/>; explicit unreachable = no direction
    /// (unit holds).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(GoalFlowFieldSystem))]
    [UpdateBefore(typeof(UnitIntegratorSystem))]
    public partial struct FlowFollowSystem : ISystem
    {
        private EntityQuery _needsComponentQuery;
        private EntityQuery _hasComponentQuery;
        private ComponentLookup<DesiredDestination> _destLookup;
        private ComponentLookup<FactionTag> _factionLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GoalFlowFieldCache>();
            state.RequireForUpdate<NavGridSingleton>();
            state.RequireForUpdate<DirectionTableSingleton>();
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

            _needsComponentQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, DesiredDestination>()
                .WithNone<FlowDesiredDir>()
                .Build();

            _hasComponentQuery = SystemAPI.QueryBuilder()
                .WithAll<UnitTag, LocalTransform, FlowDesiredDir>()
                .Build();

            _destLookup = state.GetComponentLookup<DesiredDestination>(isReadOnly: true);
            _factionLookup = state.GetComponentLookup<FactionTag>(isReadOnly: true);
        }

        public void OnUpdate(ref SystemState state)
        {
            // Lazy-add FlowDesiredDir to any unit that has a destination
            // but no flow component yet. ECB defers the structural change
            // until end of sim group.
            var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
            if (!_needsComponentQuery.IsEmpty)
            {
                var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
                using var newEntities = _needsComponentQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < newEntities.Length; i++)
                    ecb.AddComponent(newEntities[i], new FlowDesiredDir { HasValue = 0, Value = float3.zero });
            }

            if (_hasComponentQuery.IsEmpty) return;

            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var goalCache = SystemAPI.GetSingleton<GoalFlowFieldCache>();
            var table = SystemAPI.GetSingleton<DirectionTableSingleton>();
            var costField = SystemAPI.GetSingleton<NavCostField>();

            _destLookup.Update(ref state);
            _factionLookup.Update(ref state);

            var job = new SampleGoalFlowJob
            {
                GoalSlotIndex = goalCache.SlotIndex,
                GoalSlots = goalCache.Slots,
                GoalDirPool = goalCache.DirPool,
                GridWidth = grid.Width,
                GridHeight = grid.Height,
                GridOrigin = grid.Origin,
                CellSize = grid.CellSize,
                Quant = GoalFlowQuant.CellsPerBucket(grid.CellSize),
                Table = table.Table,
                DestLookup = _destLookup,
                FactionLookup = _factionLookup,
                Cost = costField.Cost,
                Flags = costField.Flags,
            };
            state.Dependency = job.ScheduleParallel(_hasComponentQuery, state.Dependency);
        }
    }

    /// <summary>
    /// Per-unit Burst job: LOS bearing, else goal-field direction byte
    /// expanded via the direction-table blob, else direct bearing while
    /// the field is pending. NoDirection in a valid field = hold position.
    /// </summary>
    [BurstCompile]
    internal partial struct SampleGoalFlowJob : IJobEntity
    {
        [ReadOnly] public NativeHashMap<GoalFlowKey, int> GoalSlotIndex;
        [ReadOnly] public NativeArray<GoalFlowSlot> GoalSlots;
        [ReadOnly] public NativeArray<byte> GoalDirPool;
        public int GridWidth;
        public int GridHeight;
        public float3 GridOrigin;
        public float CellSize;
        public int Quant;   // goal-bucket size in cells (GoalFlowQuant)
        [ReadOnly] public BlobAssetReference<DirectionTableBlob> Table;
        [ReadOnly] public ComponentLookup<DesiredDestination> DestLookup;
        [ReadOnly] public ComponentLookup<FactionTag> FactionLookup;
        [ReadOnly] public NativeArray<byte> Cost;
        [ReadOnly] public NativeArray<byte> Flags;

        public void Execute(Entity self, in LocalTransform xf, ref FlowDesiredDir dst)
        {
            dst.HasValue = 0;
            dst.Value = float3.zero;

            // No destination = no flow. Idle units don't get a direction.
            if (!DestLookup.HasComponent(self)) return;
            var desired = DestLookup[self];
            if (desired.Has == 0) return;

            float3 toGoal = new float3(
                desired.Position.x - xf.Position.x,
                0f,
                desired.Position.z - xf.Position.z);
            float distSq = toGoal.x * toGoal.x + toGoal.z * toGoal.z;
            if (distSq <= 1e-6f) return;

            // Unit's own faction -- used by LOS to decide whether
            // conditional gate cells (Cost == 254) are passable for this
            // unit, and as half of the goal-field cache key. Sentinel 0xFF
            // when the unit carries no FactionTag (no gate encodes 0xFF).
            byte selfFactionIdx = 0xFF;
            if (FactionLookup.HasComponent(self))
            {
                int f = (int)FactionLookup[self].Value;
                if (f >= 0 && f <= 7) selfFactionIdx = (byte)f;
            }

            // ── Source 1: LOS-to-goal. If we can see the goal directly,
            // use a true bearing instead of the quantized field — smooth
            // motion on open ground, exact arrival lines.
            if (HasLineOfSight(xf.Position, desired.Position, selfFactionIdx))
            {
                float inv = math.rsqrt(distSq);
                dst.Value = toGoal * inv;
                dst.HasValue = 1;
                return;
            }

            // ── Source 2: whole-map goal flow field. One array read gives
            // the globally-correct direction around any blocker shape.
            //
            // Variant cascade (bridges): the GROUND variant treats bridge
            // deck-only cells as impassable — pure ground routing, so units
            // beside a cliff go AROUND it instead of being funneled through
            // a bridge they can't physically enter at ground level. Only
            // when the ground variant cannot reach the unit's cell (sealed
            // ring — the bridge IS the route — or the unit stands on the
            // deck itself) does the BRIDGE variant take over. Goals placed
            // on a deck skip the ground variant entirely.
            int gx = (int)math.floor((desired.Position.x - GridOrigin.x) / CellSize);
            int gz = (int)math.floor((desired.Position.z - GridOrigin.z) / CellSize);
            gx = math.clamp(gx, 0, GridWidth - 1);
            gz = math.clamp(gz, 0, GridHeight - 1);

            int ucx = (int)math.floor((xf.Position.x - GridOrigin.x) / CellSize);
            int ucz = (int)math.floor((xf.Position.z - GridOrigin.z) / CellSize);
            bool unitCellValid = ucx >= 0 && ucx < GridWidth && ucz >= 0 && ucz < GridHeight;

            bool goalOnDeck = Cost[gz * GridWidth + gx] == NavCostField.CostBridgeDeckOnly;
            bool anyFieldSeen = false;

            for (byte variant = 0; variant <= 1 && unitCellValid; variant++)
            {
                if (variant == GoalFlowKey.VariantGround && goalOnDeck) continue;

                // Quantized bucket key — MUST match GoalFlowFieldSystem's
                // producer-side key math exactly.
                var key = new GoalFlowKey
                {
                    GoalCell = new int2(gx / Quant, gz / Quant),
                    FactionIdx = selfFactionIdx,
                    Variant = variant,
                };

                if (!GoalSlotIndex.TryGetValue(key, out int slot)) continue;
                var meta = GoalSlots[slot];
                // Only a fully integrated slot (Valid == 1) may be sampled.
                // Valid == 2 is a mid-batch claim (content not written yet) —
                // treat like a pending field and fall through to the direct
                // bearing this tick.
                if (meta.Valid != 1) continue;
                anyFieldSeen = true;

                byte d = GoalDirPool[meta.DirOffset + ucz * GridWidth + ucx];
                if (d != NavFlowConstants.NoDirection)
                {
                    ref var dirs = ref Table.Value.Dirs;
                    float2 v = dirs[d];
                    dst.Value = new float3(v.x, 0f, v.y);
                    dst.HasValue = 1;
                    return;
                }
                // NoDirection: unreachable in THIS variant (or standing at
                // the goal) — try the next variant.
            }

            if (anyFieldSeen)
            {
                // Every available variant gave an explicit NoDirection. That
                // legitimately means "at the goal / provably unreachable" —
                // but ONLY when the unit stands on ground the field could
                // integrate. A unit whose own cell was stamped impassable
                // out from under it (a building footprint landing on the
                // spawn point, fresh crust) reads NoDirection in EVERY field
                // and would hold forever (the 2-minute frozen-worker in the
                // trace). Walk it out on the direct bearing instead — one or
                // two steps put it back on integrated ground.
                int hereIdx = ucz * GridWidth + ucx;
                byte cHere = Cost[hereIdx];
                bool hereOpen = cHere != NavCostField.CostImpassable
                    && (cHere != NavCostField.CostConditional
                        || (byte)(Flags[hereIdx] & NavCostField.FlagOwnerMask) == selfFactionIdx);
                if (hereOpen) return; // genuine hold (at goal / unreachable)
            }

            // ── Source 3: direct-to-goal while the field is pending. The
            // producer integrates a budgeted number of fields per tick, so
            // this window is a tick or two after a fresh order — keeping
            // formation fronts moving instead of stuttering while the back
            // of the group waits.
            {
                float invLen = math.rsqrt(distSq);
                dst.Value = toGoal * invLen;
                dst.HasValue = 1;
            }
        }

        // Integer-stepped Bresenham over the cost grid from "from" to "to".
        // Returns true iff every traversed cell is walkable for the unit's
        // own faction:
        //   * Cost == 255 (CostImpassable, wall) -> always blocks
        //   * Cost == 254 (CostConditional, gate) -> blocks unless the
        //     unit's faction matches the gate owner encoded in the low
        //     3 bits of the cell's Flags byte
        //   * Cost <  254 -> walkable
        //
        // Integer math throughout -- deterministic across machines.
        private bool HasLineOfSight(float3 from, float3 to, byte selfFactionIdx)
        {
            int x0 = (int)math.floor((from.x - GridOrigin.x) / CellSize);
            int z0 = (int)math.floor((from.z - GridOrigin.z) / CellSize);
            int x1 = (int)math.floor((to.x - GridOrigin.x) / CellSize);
            int z1 = (int)math.floor((to.z - GridOrigin.z) / CellSize);

            if (x0 < 0 || x0 >= GridWidth || z0 < 0 || z0 >= GridHeight) return false;
            if (x1 < 0 || x1 >= GridWidth || z1 < 0 || z1 >= GridHeight) return false;

            int dx = math.abs(x1 - x0);
            int dz = math.abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;
            int x = x0;
            int z = z0;

            int maxSteps = GridWidth + GridHeight;
            for (int step = 0; step < maxSteps; step++)
            {
                int idx = z * GridWidth + x;
                byte c = Cost[idx];
                if (c == NavCostField.CostImpassable) return false;
                if (c == NavCostField.CostConditional)
                {
                    byte ownerIdx = (byte)(Flags[idx] & NavCostField.FlagOwnerMask);
                    if (ownerIdx != selfFactionIdx) return false;
                    // Else: owner match -- fall through, cell is walkable.
                }
                // Bridge deck-only cells break the straight-line shortcut:
                // whether they're actually crossable depends on the unit's
                // surface (deck vs ground), which LOS can't know — let the
                // flow field (which prices them) decide the route instead.
                if (c == NavCostField.CostBridgeDeckOnly) return false;
                if (x == x1 && z == z1) return true;
                int e2 = err * 2;
                if (e2 > -dz) { err -= dz; x += sx; }
                if (e2 <  dx) { err += dx; z += sz; }
            }
            return false;
        }
    }
}
