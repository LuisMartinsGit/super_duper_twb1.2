// FormationGroupSystem.cs
// AoE4-style formation travel (GDC 2022 "Pathing in Age of Empires IV",
// slide 32; docs/Design/Navigation_And_Formations.md §2):
//
//   1. The group's VIRTUAL LEADER follows the flow toward the destination
//      (LOS bearing on open ground, whole-map goal-field direction around
//      blockers) at the group speed (slowest member).
//   2. Formation SPOTS ride around the leader (leader-local offsets, laid
//      out perpendicular to the travel direction).
//   3. Each member steers to its moving spot: this system overrides the
//      member's FlowDesiredDir before SteeringSystem blends separation /
//      avoidance on top. A member with NO line of sight to its spot falls
//      back to its own goal flow toward its final slot destination.
//   4. Members behind their spot get the +40% catch-up speed; members in
//      place march at the group speed.
//   5. Combat dissolves membership (a unit that acquires a Target leaves
//      the group and fights at its own speed). Arrival settles members
//      into their spots and the group dissolves.
//
// Runs on the main thread (group counts are tiny — one entity per active
// group order); per-member work is O(members) with O(grid-ray) LOS checks.
//
// Determinism: reads only sim state, fixed iteration order (query chunk
// order + member buffer order), integer Bresenham LOS, no wall-clock and
// no randomness. The goal-field integration it samples runs synchronously
// in GoalFlowFieldSystem earlier in the tick.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace TheWaningBorder.Systems.Navigation
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(FlowFollowSystem))]
    [UpdateBefore(typeof(SteeringSystem))]
    public partial struct FormationGroupSystem : ISystem
    {
        /// <summary>A member this close to its spot counts as "in place"
        /// and simply marches with the leader.</summary>
        private const float InPlaceDistance = 0.6f;
        /// <summary>Leader turn smoothing rate (1/s) — keeps the layout from
        /// whipping around when the flow direction jitters.</summary>
        private const float FacingLerpRate = 4f;

        private EntityQuery _groupQuery;

        public void OnCreate(ref SystemState state)
        {
            _groupQuery = state.GetEntityQuery(ComponentType.ReadWrite<FormationGroup>());
            state.RequireForUpdate(_groupQuery);
            state.RequireForUpdate<NavCostField>();
            state.RequireForUpdate<NavGridSingleton>();
        }

        public void OnUpdate(ref SystemState state)
        {
            // Main-thread management pass; make sure the flow jobs that
            // write FlowDesiredDir this tick are not still in flight.
            state.CompleteDependency();

            var em = state.EntityManager;
            float dt = SystemAPI.Time.DeltaTime;

            var grid = SystemAPI.GetSingleton<NavGridSingleton>();
            var cost = SystemAPI.GetSingleton<NavCostField>();
            if (!cost.Cost.IsCreated) return;

            bool hasGoalCache = SystemAPI.TryGetSingleton<GoalFlowFieldCache>(out var goalCache);
            bool hasDirTable = SystemAPI.TryGetSingleton<DirectionTableSingleton>(out var dirTable);

            using var groups = _groupQuery.ToEntityArray(Allocator.Temp);
            for (int gi = 0; gi < groups.Length; gi++)
            {
                var groupEntity = groups[gi];
                var g = em.GetComponentData<FormationGroup>(groupEntity);

                // ── Prune / detach members (two-phase: decide on a
                // snapshot, THEN apply structural changes — Detach removes
                // components, which would invalidate a live buffer). ──
                var bufferRO = em.GetBuffer<FormationMember>(groupEntity);
                var snapshot = new NativeArray<FormationMember>(bufferRO.Length, Allocator.Temp);
                bufferRO.AsNativeArray().CopyTo(snapshot);

                var keep = new NativeList<FormationMember>(snapshot.Length, Allocator.Temp);
                var toDetach = new NativeList<Entity>(snapshot.Length, Allocator.Temp);
                float slowest = float.MaxValue;

                // Worst member lag against the PRE-advance leader pose. Drives
                // the leader tether below: the leader may only travel as fast
                // as the formation it is leading can actually follow.
                float3 preRight = math.cross(new float3(0f, 1f, 0f), g.Facing);
                float maxLag = 0f;
                Entity worstLaggard = Entity.Null;

                for (int i = 0; i < snapshot.Length; i++)
                {
                    var m = snapshot[i];
                    var u = m.Unit;

                    if (u == Entity.Null || !em.Exists(u)) continue;
                    if (em.HasComponent<DeathAnimationState>(u)) { toDetach.Add(u); continue; }

                    // Re-ordered into another group / individually: the
                    // member state no longer points here — just drop it.
                    if (!em.HasComponent<FormationMemberState>(u)
                        || em.GetComponentData<FormationMemberState>(u).Group != groupEntity)
                        continue;

                    // Combat dissolves the formation (AoE4): the unit
                    // fights individually at its own speed.
                    if (em.HasComponent<Target>(u)
                        && em.GetComponentData<Target>(u).Value != Entity.Null)
                    {
                        toDetach.Add(u);
                        continue;
                    }

                    bool hasDest = em.HasComponent<DesiredDestination>(u)
                        && em.GetComponentData<DesiredDestination>(u).Has != 0;

                    if (!hasDest)
                    {
                        // Order finished (settled into the final slot) or
                        // cancelled by stuck recovery — either way the unit
                        // is done travelling with the group.
                        toDetach.Add(u);
                        continue;
                    }

                    keep.Add(m);
                    if (em.HasComponent<MoveSpeed>(u))
                    {
                        float sp = em.GetComponentData<MoveSpeed>(u).Value;
                        if (sp > 0f && sp < slowest) slowest = sp;
                    }

                    if (em.HasComponent<LocalTransform>(u))
                    {
                        float3 sp0 = g.LeaderPos + preRight * m.Slot.x + g.Facing * m.Slot.y;
                        float3 p0 = em.GetComponentData<LocalTransform>(u).Position;
                        float lx = sp0.x - p0.x, lz = sp0.z - p0.z;
                        float lag = math.sqrt(lx * lx + lz * lz);
                        if (lag > maxLag) { maxLag = lag; worstLaggard = u; }
                    }
                }
                snapshot.Dispose();

                for (int i = 0; i < toDetach.Length; i++)
                    Detach(em, toDetach[i]);
                toDetach.Dispose();

                if (keep.Length == 0)
                {
                    keep.Dispose();
                    em.DestroyEntity(groupEntity);
                    continue;
                }

                var buffer = em.GetBuffer<FormationMember>(groupEntity);
                if (keep.Length != buffer.Length)
                {
                    buffer.Clear();
                    for (int i = 0; i < keep.Length; i++) buffer.Add(keep[i]);
                }

                if (slowest > 0f && slowest != float.MaxValue)
                    g.GroupSpeed = slowest;

                // ── Leader tether ──────────────────────────────────────────
                // The virtual leader pays none of the costs its members pay:
                // no separation, no obstacle slide, no turn-rate clamp, and —
                // crucially — no terrain cost and no BorderDebuff.SpeedPenalty,
                // all of which UnitIntegratorSystem DOES apply to the members.
                // At equal nominal speed the leader therefore always pulls
                // ahead, the members lose line of sight to their spots, fall
                // back to their own goal flow, and the formation stops being a
                // formation. Scale the leader's step by how far the group has
                // actually fallen behind. This also removes the need for a
                // separate "wait while we form up" rule: on tick 1 members can
                // be a whole CohesionRadius from their spots, so the leader
                // starts slow and accelerates as the shape comes together.
                float lagScale = 1f;
                if (maxLag > FormationGroup.LeaderTetherDistance)
                {
                    lagScale = math.saturate(
                        1f - (maxLag - FormationGroup.LeaderTetherDistance)
                             / FormationGroup.LeaderTetherDistance);
                }

                // Only a group that is FAILING to close up counts toward the
                // release fuse — a formation still forming has a large lag that
                // is steadily shrinking, and must not be torn apart for it.
                Entity pendingDrop = Entity.Null;
                if (maxLag < g.BestLag - FormationGroup.TetherProgressEpsilon)
                {
                    g.BestLag = maxLag;
                    g.TetherTicks = 0;
                }
                else if (lagScale <= 0.01f)
                {
                    g.TetherTicks = (byte)math.min(g.TetherTicks + 1, 255);
                    if (g.TetherTicks >= FormationGroup.TetherReleaseTicks
                        && worstLaggard != Entity.Null)
                    {
                        // One wedged member would otherwise freeze the whole
                        // group at a standstill. Drop it; it finishes to its
                        // own slot independently (design §2.4 outlier rule).
                        pendingDrop = worstLaggard;
                        g.TetherTicks = 0;
                        g.BestLag = float.MaxValue;
                        lagScale = 1f;
                    }
                }

                // ── Advance the virtual leader. ──
                if (g.State == FormationGroup.StateMoving)
                {
                    float3 toDest = g.Destination - g.LeaderPos;
                    toDest.y = 0f;
                    float destDistSq = math.lengthsq(toDest);

                    if (destDistSq <= FormationGroup.ArriveDistance * FormationGroup.ArriveDistance)
                    {
                        g.LeaderPos = new float3(g.Destination.x, 0f, g.Destination.z);
                        g.State = FormationGroup.StateArrived;
                    }
                    else
                    {
                        float3 dir = ResolveLeaderDir(in grid, in cost, hasGoalCache, in goalCache,
                            hasDirTable, in dirTable, g.LeaderPos, g.Destination, g.FactionIdx);

                        float destDist = math.sqrt(destDistSq);
                        float stepLen = math.min(g.GroupSpeed * lagScale * dt, destDist);
                        float3 next = g.LeaderPos + dir * stepLen;

                        if (IsLeaderCellPassable(in grid, in cost, next, g.FactionIdx))
                        {
                            g.LeaderPos = next;
                            g.StallTicks = 0;
                            // Smooth the facing toward the actual travel
                            // direction so the layout turns, not snaps.
                            if (math.lengthsq(dir) > 1e-6f)
                            {
                                float t = math.saturate(FacingLerpRate * dt);
                                float3 f = math.normalizesafe(math.lerp(g.Facing, dir, t),
                                    g.Facing);
                                if (math.lengthsq(f) > 1e-6f) g.Facing = f;
                            }
                        }
                        else
                        {
                            // Blocked: hold this tick; the goal field routes
                            // the leader around the blocker on following
                            // ticks. A leader stuck for good releases the
                            // group so members finish on their own flow
                            // instead of hovering around a dead spot layout.
                            g.StallTicks = (byte)math.min(g.StallTicks + 1, 255);
                            if (g.StallTicks >= FormationGroup.StallReleaseTicks)
                                g.State = FormationGroup.StateArrived;
                        }
                    }
                }

                // ── Arrival DISSOLVES the group (design §2.8). ─────────────
                // The leader has reached the destination, so every member's
                // own DesiredDestination — its final slot — already IS the
                // frozen spot. Keeping the group alive past this point kept
                // FormationMemberState / FormationSpeedOverride on units that
                // the system no longer steers, which (a) leaked the group
                // entity whenever a member could not close the last 0.5 m, and
                // (b) held SteeringSystem's formation exemption open during the
                // settle, when the arrival damping is exactly what's wanted.
                if (g.State == FormationGroup.StateArrived)
                {
                    var settling = em.GetBuffer<FormationMember>(groupEntity);
                    var settled = new NativeArray<Entity>(settling.Length, Allocator.Temp);
                    for (int i = 0; i < settling.Length; i++) settled[i] = settling[i].Unit;
                    for (int i = 0; i < settled.Length; i++) Detach(em, settled[i]);
                    settled.Dispose();
                    keep.Dispose();
                    em.DestroyEntity(groupEntity);
                    continue;
                }

                // ── Steer members to their moving spots. ──
                float3 right = math.cross(new float3(0f, 1f, 0f), g.Facing);
                float catchUpSpeed = g.GroupSpeed * FormationGroup.CatchUpMultiplier;

                buffer = em.GetBuffer<FormationMember>(groupEntity);
                for (int i = 0; i < buffer.Length; i++)
                {
                    var u = buffer[i].Unit;

                    if (!em.HasComponent<LocalTransform>(u)
                        || !em.HasComponent<FlowDesiredDir>(u)) continue;

                    float2 s = buffer[i].Slot;
                    float3 spot = g.LeaderPos + right * s.x + g.Facing * s.y;
                    float3 pos = em.GetComponentData<LocalTransform>(u).Position;
                    float3 toSpot = spot - pos;
                    toSpot.y = 0f;
                    float spotDist = math.length(toSpot);

                    // Catch-up hysteresis: engage above the trigger distance,
                    // release only once genuinely back in place. See
                    // FormationMember.CatchingUp for why a single threshold
                    // produced a permanent lag instead of a catch-up.
                    bool catching = buffer[i].CatchingUp != 0;
                    if (spotDist > FormationGroup.CatchUpTriggerDistance) catching = true;
                    else if (spotDist <= InPlaceDistance) catching = false;

                    if (spotDist <= InPlaceDistance)
                    {
                        // In place: march with the leader.
                        em.SetComponentData(u, new FlowDesiredDir
                        {
                            Value = g.Facing,
                            HasValue = 1,
                        });
                    }
                    else if (HasLineOfSight(in grid, in cost, pos, spot, g.FactionIdx))
                    {
                        // Formation steering: seek the moving spot.
                        em.SetComponentData(u, new FlowDesiredDir
                        {
                            Value = toSpot / math.max(1e-5f, spotDist),
                            HasValue = 1,
                        });
                    }
                    else
                    {
                        // No LOS to the spot (blocker between): fall back to
                        // the unit's own goal flow toward its final slot
                        // destination (already in FlowDesiredDir), at catch-up
                        // speed so it can rejoin once it clears the blocker.
                        catching = true;
                    }

                    byte catchByte = (byte)(catching ? 1 : 0);
                    if (buffer[i].CatchingUp != catchByte)
                    {
                        var m = buffer[i];
                        m.CatchingUp = catchByte;
                        buffer[i] = m;
                    }

                    if (em.HasComponent<FormationSpeedOverride>(u))
                    {
                        em.SetComponentData(u, new FormationSpeedOverride
                        {
                            Value = catching ? catchUpSpeed : g.GroupSpeed,
                        });
                    }
                }

                em.SetComponentData(groupEntity, g);
                keep.Dispose();

                // Structural change last: Detach removes components, which
                // would invalidate the member buffer held above.
                if (pendingDrop != Entity.Null) Detach(em, pendingDrop);
            }
        }

        /// <summary>Detach a unit from formation travel: it keeps whatever
        /// order it is executing, at its own speed.</summary>
        private static void Detach(EntityManager em, Entity u)
        {
            if (em.HasComponent<FormationMemberState>(u))
                em.RemoveComponent<FormationMemberState>(u);
            if (em.HasComponent<FormationSpeedOverride>(u))
                em.RemoveComponent<FormationSpeedOverride>(u);
        }

        /// <summary>
        /// Leader direction, mirroring FlowFollowSystem's source order:
        /// LOS bearing → whole-map goal field → direct bearing.
        /// </summary>
        private static float3 ResolveLeaderDir(in NavGridSingleton grid, in NavCostField cost,
            bool hasCache, in GoalFlowFieldCache cache, bool hasTable,
            in DirectionTableSingleton table, float3 from, float3 dest, byte factionIdx)
        {
            float3 direct = dest - from;
            direct.y = 0f;
            float lenSq = math.lengthsq(direct);
            if (lenSq <= 1e-8f) return float3.zero;
            direct *= math.rsqrt(lenSq);

            if (HasLineOfSight(in grid, in cost, from, dest, factionIdx))
                return direct;

            if (hasCache && hasTable && cache.SlotIndex.IsCreated)
            {
                int quant = GoalFlowQuant.CellsPerBucket(grid.CellSize);
                int gx = math.clamp((int)math.floor((dest.x - grid.Origin.x) / grid.CellSize), 0, grid.Width - 1);
                int gz = math.clamp((int)math.floor((dest.z - grid.Origin.z) / grid.CellSize), 0, grid.Height - 1);
                int lx = (int)math.floor((from.x - grid.Origin.x) / grid.CellSize);
                int lz = (int)math.floor((from.z - grid.Origin.z) / grid.CellSize);
                if (lx >= 0 && lx < grid.Width && lz >= 0 && lz < grid.Height)
                {
                    bool goalOnDeck = cost.Cost[gz * grid.Width + gx] == NavCostField.CostBridgeDeckOnly;
                    for (byte variant = 0; variant <= 1; variant++)
                    {
                        if (variant == GoalFlowKey.VariantGround && goalOnDeck) continue;
                        var key = new GoalFlowKey
                        {
                            GoalCell = new int2(gx / quant, gz / quant),
                            FactionIdx = factionIdx,
                            Variant = variant,
                        };
                        if (!cache.SlotIndex.TryGetValue(key, out int slot)) continue;
                        var meta = cache.Slots[slot];
                        if (meta.Valid == 0) continue;
                        byte d = cache.DirPool[meta.DirOffset + lz * grid.Width + lx];
                        if (d == NavFlowConstants.NoDirection) continue;
                        ref var dirs = ref table.Table.Value.Dirs;
                        float2 v = dirs[d];
                        return new float3(v.x, 0f, v.y);
                    }
                }
            }

            return direct;
        }

        /// <summary>Layer-0 passability for the virtual leader — walls block,
        /// gates admit the group's own faction, bridge deck-only strips are
        /// not enterable at ground level.</summary>
        private static bool IsLeaderCellPassable(in NavGridSingleton grid, in NavCostField cost,
            float3 pos, byte factionIdx)
        {
            int x = (int)math.floor((pos.x - grid.Origin.x) / grid.CellSize);
            int z = (int)math.floor((pos.z - grid.Origin.z) / grid.CellSize);
            if (x < 0 || x >= grid.Width || z < 0 || z >= grid.Height) return false;
            int idx = z * grid.Width + x;
            byte c = cost.Cost[idx];
            if (c == NavCostField.CostImpassable) return false;
            if (c == NavCostField.CostBridgeDeckOnly) return false;
            if (c == NavCostField.CostConditional)
                return (byte)(cost.Flags[idx] & NavCostField.FlagOwnerMask) == factionIdx;
            return true;
        }

        // Integer Bresenham over the cost grid — the same walkability rules
        // as FlowFollowSystem.SampleGoalFlowJob.HasLineOfSight (walls block;
        // gates block unless owned; deck-only strips break the shortcut).
        private static bool HasLineOfSight(in NavGridSingleton grid, in NavCostField cost,
            float3 from, float3 to, byte selfFactionIdx)
        {
            int x0 = (int)math.floor((from.x - grid.Origin.x) / grid.CellSize);
            int z0 = (int)math.floor((from.z - grid.Origin.z) / grid.CellSize);
            int x1 = (int)math.floor((to.x - grid.Origin.x) / grid.CellSize);
            int z1 = (int)math.floor((to.z - grid.Origin.z) / grid.CellSize);

            if (x0 < 0 || x0 >= grid.Width || z0 < 0 || z0 >= grid.Height) return false;
            if (x1 < 0 || x1 >= grid.Width || z1 < 0 || z1 >= grid.Height) return false;

            int dx = math.abs(x1 - x0);
            int dz = math.abs(z1 - z0);
            int sx = x0 < x1 ? 1 : -1;
            int sz = z0 < z1 ? 1 : -1;
            int err = dx - dz;
            int x = x0;
            int z = z0;

            int maxSteps = grid.Width + grid.Height;
            for (int step = 0; step < maxSteps; step++)
            {
                int idx = z * grid.Width + x;
                byte c = cost.Cost[idx];
                if (c == NavCostField.CostImpassable) return false;
                if (c == NavCostField.CostConditional)
                {
                    byte ownerIdx = (byte)(cost.Flags[idx] & NavCostField.FlagOwnerMask);
                    // Owner or ally. docs/Design/Teams.md
                    if (!Alliances.AreAlliedBurst(ownerIdx, selfFactionIdx)) return false;
                }
                if (c == NavCostField.CostBridgeDeckOnly) return false;
                if (x == x1 && z == z1) return true;
                int e2 = err * 2;
                if (e2 > -dz) { err -= dz; x += sx; }
                if (e2 < dx) { err += dx; z += sz; }
            }
            return false;
        }
    }
}
