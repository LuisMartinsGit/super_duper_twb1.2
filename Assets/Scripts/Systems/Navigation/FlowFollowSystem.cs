// FlowFollowSystem.cs
// task-112 M1+M3 -- writes FlowDesiredDir on every unit with a
// DesiredDestination by sampling the per-tile flow cache slab keyed by
// (currentTile, nextPortalId, profile). M1 used the now-deleted
// NavFlowFieldM1 whole-map field; M3 replaced that with NavFlowCache
// slabs populated by FlowSegmentSystem.
//
// Runs after FlowSegmentSystem (so the slab the unit needs is in the
// cache this tick) and before MovementSystem (so the surgical
// [UpdateBefore] hook reads the dir we wrote this tick).
//
// Determinism notes:
//   * Per-unit job reads only the cache slab + the unit's own components.
//   * Cache slab look-up uses NativeHashMap.TryGetValue -- O(1)
//     deterministic.
//   * No SystemAPI.Time reads.
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
    /// Per-tick sampler. Looks up the per-tile flow slab in
    /// <see cref="NavFlowCache"/> for each unit holding a
    /// <see cref="NavPathResult"/> + <see cref="NavPathPortal"/> buffer,
    /// and writes the sampled direction into <see cref="FlowDesiredDir"/>.
    /// Units without an active path get <c>HasValue = 0</c>.
    ///
    /// task-112 M4: UpdateBefore migrated from MovementSystem (deleted)
    /// to UnitIntegratorSystem.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FlowSegmentSystem))]
    [UpdateBefore(typeof(UnitIntegratorSystem))]
    public partial struct FlowFollowSystem : ISystem
    {
        private EntityQuery _needsComponentQuery;
        private EntityQuery _hasComponentQuery;
        private ComponentLookup<NavPathResult> _resultLookup;
        private BufferLookup<NavPathPortal> _portalBufferLookup;
        private ComponentLookup<DesiredDestination> _destLookup;
        private ComponentLookup<FactionTag> _factionLookup;

        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<NavFlowCache>();
            state.RequireForUpdate<NavGridSingleton>();
            state.RequireForUpdate<PortalGraphSingleton>();
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

            _resultLookup = state.GetComponentLookup<NavPathResult>(isReadOnly: true);
            _portalBufferLookup = state.GetBufferLookup<NavPathPortal>(isReadOnly: true);
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
            var cache = SystemAPI.GetSingleton<NavFlowCache>();
            var graphSingleton = SystemAPI.GetSingleton<PortalGraphSingleton>();
            if (graphSingleton.Built == 0) return;
            var table = SystemAPI.GetSingleton<DirectionTableSingleton>();

            _resultLookup.Update(ref state);
            _portalBufferLookup.Update(ref state);
            _destLookup.Update(ref state);
            _factionLookup.Update(ref state);
            var costField = SystemAPI.GetSingleton<NavCostField>();

            var job = new SampleFlowFromCacheJob
            {
                SlotIndex = cache.SlotIndex,
                Slots = cache.Slots,
                DirPool = cache.DirPool,
                TileArea = cache.TileArea,
                TileSize = PortalGraphSingleton.TileSize,
                TilesX = graphSingleton.Graph.Value.TilesX,
                GridWidth = grid.Width,
                GridHeight = grid.Height,
                GridOrigin = grid.Origin,
                CellSize = grid.CellSize,
                Table = table.Table,
                ResultLookup = _resultLookup,
                PortalBufferLookup = _portalBufferLookup,
                DestLookup = _destLookup,
                FactionLookup = _factionLookup,
                Cost = costField.Cost,
                Flags = costField.Flags,
            };
            state.Dependency = job.ScheduleParallel(_hasComponentQuery, state.Dependency);
        }
    }

    /// <summary>
    /// Per-unit Burst job: convert unit position -> (tileIndex, tile-local
    /// cell), look up the cache slab keyed by (tileIndex, nextPortalId, 0),
    /// read the dir byte, expand via the direction-table blob.
    /// </summary>
    [BurstCompile]
    internal partial struct SampleFlowFromCacheJob : IJobEntity
    {
        [ReadOnly] public NativeHashMap<NavFlowCacheKey, int> SlotIndex;
        [ReadOnly] public NativeArray<NavFlowCacheSlot> Slots;
        [ReadOnly] public NativeArray<byte> DirPool;
        public int TileArea;
        public int TileSize;
        public int TilesX;
        public int GridWidth;
        public int GridHeight;
        public float3 GridOrigin;
        public float CellSize;
        [ReadOnly] public BlobAssetReference<DirectionTableBlob> Table;
        [ReadOnly] public ComponentLookup<NavPathResult> ResultLookup;
        [ReadOnly] public BufferLookup<NavPathPortal> PortalBufferLookup;
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

            // Unit's own faction -- used by LOS / angle-sweep to decide
            // whether conditional gate cells (Cost == 254) are passable
            // for this unit. Defaults to sentinel 0xFF when the unit
            // carries no FactionTag (which is fine because no gate cell
            // encodes 0xFF as its owner).
            byte selfFactionIdx = FactionLookup.HasComponent(self)
                ? (byte)FactionLookup[self].Value
                : (byte)0xFF;

            // ── Source 1: LOS-to-goal (S5 spec: LOS pass for smooth
            // gradients). If we can see the goal directly, use a true
            // bearing instead of consulting portal-relative cached flow.
            // This handles the entire flat-ground case (Phase 1 / 2 / 3).
            if (HasLineOfSight(xf.Position, desired.Position, selfFactionIdx))
            {
                float inv = math.rsqrt(distSq);
                dst.Value = toGoal * inv;
                dst.HasValue = 1;
                return;
            }

            // ── Source 1b: angle-sweep around the goal bearing. LOS to
            // goal is blocked (a wall or building sits between unit and
            // destination), but the unit doesn't need to walk all the
            // way to the wall before re-routing. Try bearings offset
            // by +/-15, +/-30, ... +/-75 degrees from the direct goal
            // bearing and pick the smallest-angle offset whose 10-cell
            // probe is clear. Unit immediately starts curving around the
            // obstacle as soon as it can see the wall.
            //
            // Determinism: integer-stepped Bresenham per probe; angle
            // table is a const; iteration order is fixed.
            {
                float invGoal = math.rsqrt(distSq);
                float gx = toGoal.x * invGoal;
                float gz = toGoal.z * invGoal;

                // Probe angles in pairs (right then left at each magnitude)
                // so right-side detours win ties -- consistent with the
                // SteeringSystem obstacle-avoidance right-side preference.
                //
                // Probe distance: as far as the actual goal, up to a 60-
                // cell cap (~60 m). The previous 10-cell probe was too
                // short for the real game -- units only "saw" a building
                // when they were within 10 cells of it, by which time
                // the path was already committed and the unit drifted
                // into the obstacle. With probe-to-goal, a building 50
                // cells away on the direct line gets DETECTED by the
                // 15-degree probes too (the ray still passes through
                // it), forcing a wider detour angle.
                float goalDist = math.sqrt(distSq);
                float maxProbeDist = math.min(60f * CellSize, goalDist);
                int ProbeDistanceCells = math.max(10, (int)(maxProbeDist / CellSize));
                // Cosine / sine for 15/30/45/60/75 degrees.
                // 15° = (0.96593, 0.25882)
                // 30° = (0.86603, 0.50000)
                // 45° = (0.70711, 0.70711)
                // 60° = (0.50000, 0.86603)
                // 75° = (0.25882, 0.96593)
                for (int i = 0; i < 10; i++)
                {
                    float cos, sin;
                    switch (i)
                    {
                        case 0: cos = 0.96593f; sin = -0.25882f; break;  //  15 right
                        case 1: cos = 0.96593f; sin =  0.25882f; break;  //  15 left
                        case 2: cos = 0.86603f; sin = -0.50000f; break;  //  30 right
                        case 3: cos = 0.86603f; sin =  0.50000f; break;  //  30 left
                        case 4: cos = 0.70711f; sin = -0.70711f; break;  //  45 right
                        case 5: cos = 0.70711f; sin =  0.70711f; break;  //  45 left
                        case 6: cos = 0.50000f; sin = -0.86603f; break;  //  60 right
                        case 7: cos = 0.50000f; sin =  0.86603f; break;  //  60 left
                        case 8: cos = 0.25882f; sin = -0.96593f; break;  //  75 right
                        default: cos = 0.25882f; sin = 0.96593f; break;  //  75 left
                    }
                    // 2D rotation of (gx, gz) by angle. For "right" (sin<0):
                    //   new = (gx*cos + gz*sin, -gx*sin + gz*cos)
                    float bx = gx * cos + gz * sin;
                    float bz = -gx * sin + gz * cos;

                    float3 probeTarget = new float3(
                        xf.Position.x + bx * (CellSize * ProbeDistanceCells),
                        xf.Position.y,
                        xf.Position.z + bz * (CellSize * ProbeDistanceCells));

                    if (HasLineOfSight(xf.Position, probeTarget, selfFactionIdx))
                    {
                        dst.Value = new float3(bx, 0f, bz);
                        dst.HasValue = 1;
                        return;
                    }
                }
                // All angle probes blocked -- fall through to the per-tile
                // cache, then to the unconditional direct-to-goal fallback.
            }

            // ── Source 2: per-tile cached flow slab from FlowSegmentSystem.
            // Only consulted if we have a successful path and an
            // unconsumed portal in the buffer; otherwise skip straight
            // to the direct-to-goal fallback below.
            if (ResultLookup.HasComponent(self))
            {
                var result = ResultLookup[self];
                if (result.Status == NavPathRequest.StatusSuccess
                    && PortalBufferLookup.HasBuffer(self))
                {
                    var buf = PortalBufferLookup[self];
                    int nextIdx = result.CurrentPortalIndex + 1;
                    if (nextIdx < buf.Length)
                    {
                        int nextPortalId = buf[nextIdx].PortalId;

                        float dx = xf.Position.x - GridOrigin.x;
                        float dz = xf.Position.z - GridOrigin.z;
                        int cx = (int)math.floor(dx / CellSize);
                        int cz = (int)math.floor(dz / CellSize);
                        if (cx >= 0 && cx < GridWidth && cz >= 0 && cz < GridHeight)
                        {
                            int tileX = cx / TileSize;
                            int tileZ = cz / TileSize;
                            int tileIndex = tileZ * TilesX + tileX;

                            var key = new NavFlowCacheKey
                            {
                                TileIndex = tileIndex,
                                ExitPortalId = nextPortalId,
                                ProfileHash = 0,
                            };

                            if (SlotIndex.TryGetValue(key, out int slot))
                            {
                                var slotMeta = Slots[slot];
                                if (slotMeta.Valid != 0)
                                {
                                    int localX = cx - tileX * TileSize;
                                    int localZ = cz - tileZ * TileSize;
                                    int localIdx = localZ * TileSize + localX;
                                    byte d = DirPool[slotMeta.DirOffset + localIdx];
                                    if (d != NavFlowConstants.NoDirection)
                                    {
                                        ref var dirs = ref Table.Value.Dirs;
                                        float2 v = dirs[d];
                                        dst.Value = new float3(v.x, 0f, v.y);
                                        dst.HasValue = 1;
                                        return;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // ── Source 3: direct-to-goal fallback (unconditional). Fires
            // whenever LOS is blocked AND the cache can't help -- including
            // the first few ticks after a click while the M6 scheduler is
            // still releasing the NavPathRequest for this unit. Without
            // this fallback, front-of-formation units that hadn't yet been
            // assigned a NavPathResult would stop and wait for the back
            // of the formation to catch up (separation push), producing
            // the "front units wait for back units" stutter the M4 user
            // reported. Pointing directly at the goal maintains forward
            // motion; the steering layer's obstacle-avoidance handles
            // wall-collision sidestep.
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
        // selfFactionIdx is the unit's own faction enum index (Blue=0..
        // White=7), or 0xFF for "no faction" (always treats conditionals
        // as blocked).
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
                if (x == x1 && z == z1) return true;
                int e2 = err * 2;
                if (e2 > -dz) { err -= dz; x += sx; }
                if (e2 <  dx) { err += dx; z += sz; }
            }
            return false;
        }
    }
}
