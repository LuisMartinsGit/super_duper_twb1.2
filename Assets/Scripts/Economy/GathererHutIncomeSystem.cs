// File: Assets/Scripts/Economy/GathererHutIncomeSystem.cs
// Calculates area-based income for GathererHuts (BFME2-style farms)
// Uses first-come-first-served priority: older farms keep full yield,
// newer farms only earn from unclaimed area.
//
// Grid-sampling approach: iterates PassabilityGrid cells within the
// gather radius and excludes cells that are terrain-blocked, inside
// enemy hut circles, inside older same-faction hut circles, or inside
// wall enclosure polygons.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Economy
{
    // No [BurstCompile] — runs every 2s, not perf-critical,
    // and we need reliable structural changes (AddComponent).
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct GathererHutIncomeSystem : ISystem
    {
        /// <summary>Radius of the resource gathering circle around each GathererHut.</summary>
        public const float GatherRadius = 15f;

        private const float BasePerTick = 15f;     // 15 supplies per tick at 100% area
        private const float TickInterval = 10f;    // Tick every 10 seconds
        private const float UpdateInterval = 2f;   // Recalculate every 2 seconds

        private double _lastUpdateTime;
        private int _nextBuildOrder;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GathererHutTag>();
            _lastUpdateTime = 0;
            _nextBuildOrder = 0;
        }

        public void OnUpdate(ref SystemState state)
        {
            var currentTime = SystemAPI.Time.ElapsedTime;
            if (currentTime - _lastUpdateTime < UpdateInterval)
                return;
            _lastUpdateTime = currentTime;

            var em = state.EntityManager;

            // =========================================================
            // Pre-pass: assign build orders to newly completed huts
            // Two-step add+set for reliability outside Burst.
            // =========================================================
            var newHutQuery = SystemAPI.QueryBuilder()
                .WithAll<GathererHutTag>()
                .WithNone<UnderConstruction, FarmBuildOrder>()
                .Build();

            var newHuts = newHutQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < newHuts.Length; i++)
            {
                em.AddComponent<FarmBuildOrder>(newHuts[i]);
                em.SetComponentData(newHuts[i], new FarmBuildOrder { Value = _nextBuildOrder++ });
            }
            newHuts.Dispose();

            // =========================================================
            // Snapshot all completed GathererHuts (all now have FarmBuildOrder)
            // =========================================================
            var hutQuery = SystemAPI.QueryBuilder()
                .WithAll<GathererHutTag, LocalTransform, FactionTag, SuppliesIncome, FarmBuildOrder>()
                .WithNone<UnderConstruction>()
                .Build();

            var hutEntities = hutQuery.ToEntityArray(Allocator.Temp);
            var hutTransforms = hutQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var hutFactions = hutQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            var hutBuildOrders = hutQuery.ToComponentDataArray<FarmBuildOrder>(Allocator.Temp);

            // =========================================================
            // Snapshot wall enclosure polygons for point-in-polygon tests (Bug B)
            // =========================================================
            var enclosureQuery = SystemAPI.QueryBuilder()
                .WithAll<WallEnclosureIncomeTag>()
                .Build();

            var enclosureEntities = enclosureQuery.ToEntityArray(Allocator.Temp);

            // =========================================================
            // Calculate income for each GathererHut using grid sampling
            // =========================================================
            var grid = PassabilityGrid.Instance;
            float totalArea = math.PI * GatherRadius * GatherRadius;

            for (int h = 0; h < hutEntities.Length; h++)
            {
                float ratio;

                if (grid != null)
                {
                    ratio = CalculateRatioGridSampling(
                        em, grid, hutEntities[h], hutTransforms[h].Position,
                        hutFactions[h].Value, hutBuildOrders[h].Value,
                        hutEntities, hutTransforms, hutFactions, hutBuildOrders,
                        enclosureEntities);
                }
                else
                {
                    // Fallback: no PassabilityGrid available (e.g. during bootstrap)
                    ratio = CalculateRatioFallback(
                        em, hutEntities[h], hutTransforms[h].Position,
                        hutFactions[h].Value, hutBuildOrders[h].Value,
                        hutEntities, hutTransforms, hutFactions, hutBuildOrders,
                        totalArea);
                }

                float effectivePerTick = BasePerTick * ratio;

                // --- Update the component (preserve Elapsed timer) ---
                var current = em.GetComponentData<SuppliesIncome>(hutEntities[h]);
                em.SetComponentData(hutEntities[h], new SuppliesIncome
                {
                    PerTick = effectivePerTick,
                    Interval = TickInterval,
                    Elapsed = current.Elapsed
                });
            }

            // Cleanup
            hutEntities.Dispose();
            hutTransforms.Dispose();
            hutFactions.Dispose();
            hutBuildOrders.Dispose();
            enclosureEntities.Dispose();
        }

        /// <summary>
        /// Grid-sampling area calculation. Iterates PassabilityGrid cells within
        /// GatherRadius and checks each cell against all exclusion criteria.
        /// </summary>
        private static float CalculateRatioGridSampling(
            EntityManager em,
            PassabilityGrid grid,
            Entity hutEntity,
            float3 hutPos,
            Faction hutFaction,
            int myOrder,
            NativeArray<Entity> hutEntities,
            NativeArray<LocalTransform> hutTransforms,
            NativeArray<FactionTag> hutFactions,
            NativeArray<FarmBuildOrder> hutBuildOrders,
            NativeArray<Entity> enclosureEntities)
        {
            float radiusSq = GatherRadius * GatherRadius;
            float2 hutPos2D = new float2(hutPos.x, hutPos.z);

            // Determine cell scan bounds
            int2 minCell = grid.WorldToCell(new float3(hutPos.x - GatherRadius, 0f, hutPos.z - GatherRadius));
            int2 maxCell = grid.WorldToCell(new float3(hutPos.x + GatherRadius, 0f, hutPos.z + GatherRadius));
            minCell = math.max(minCell, int2.zero);
            maxCell = math.min(maxCell, new int2(grid.Width - 1, grid.Height - 1));

            int totalCells = 0;
            int freeCells = 0;

            for (int cy = minCell.y; cy <= maxCell.y; cy++)
            {
                for (int cx = minCell.x; cx <= maxCell.x; cx++)
                {
                    var cell = new int2(cx, cy);
                    float3 cellWorld = grid.CellToWorld(cell);
                    float2 cellPos = new float2(cellWorld.x, cellWorld.z);

                    // Check if cell is within the gather circle
                    float dx = cellPos.x - hutPos2D.x;
                    float dz = cellPos.y - hutPos2D.y;
                    if (dx * dx + dz * dz > radiusSq)
                        continue;

                    totalCells++;

                    // --- Exclusion 1: Terrain-blocked or building-blocked (Bug C) ---
                    byte cellValue = grid.GetCell(cell);
                    if (cellValue != PassabilityGrid.Passable)
                    {
                        continue;
                    }

                    // --- Exclusion 2: Inside older same-faction GathererHut circle ---
                    bool excluded = false;
                    for (int other = 0; other < hutEntities.Length; other++)
                    {
                        if (hutEntities[other] == hutEntity) continue;
                        if (hutFactions[other].Value != hutFaction) continue;
                        if (hutBuildOrders[other].Value >= myOrder) continue;

                        var otherPos = new float2(
                            hutTransforms[other].Position.x,
                            hutTransforms[other].Position.z);
                        float odx = cellPos.x - otherPos.x;
                        float odz = cellPos.y - otherPos.y;
                        if (odx * odx + odz * odz <= radiusSq)
                        {
                            excluded = true;
                            break;
                        }
                    }
                    if (excluded) continue;

                    // --- Exclusion 3: Inside ANY enemy GathererHut circle (Bug A) ---
                    for (int other = 0; other < hutEntities.Length; other++)
                    {
                        if (hutEntities[other] == hutEntity) continue;
                        if (hutFactions[other].Value == hutFaction) continue;

                        var otherPos = new float2(
                            hutTransforms[other].Position.x,
                            hutTransforms[other].Position.z);
                        float odx = cellPos.x - otherPos.x;
                        float odz = cellPos.y - otherPos.y;
                        if (odx * odx + odz * odz <= radiusSq)
                        {
                            excluded = true;
                            break;
                        }
                    }
                    if (excluded) continue;

                    // --- Exclusion 4: Inside any wall enclosure polygon (Bug B) ---
                    for (int e = 0; e < enclosureEntities.Length; e++)
                    {
                        if (!em.HasBuffer<WallEnclosureVertex>(enclosureEntities[e]))
                            continue;

                        var vertices = em.GetBuffer<WallEnclosureVertex>(enclosureEntities[e]);
                        if (vertices.Length < 3) continue;

                        if (PointInPolygon(cellPos, vertices))
                        {
                            excluded = true;
                            break;
                        }
                    }
                    if (excluded) continue;

                    freeCells++;
                }
            }

            if (totalCells == 0) return 0f;
            return (float)freeCells / totalCells;
        }

        /// <summary>
        /// Fallback geometric area calculation when PassabilityGrid is not available.
        /// Uses same-faction overlap only (original logic minus enemy hut skip bug).
        /// </summary>
        private static float CalculateRatioFallback(
            EntityManager em,
            Entity hutEntity,
            float3 hutPos,
            Faction hutFaction,
            int myOrder,
            NativeArray<Entity> hutEntities,
            NativeArray<LocalTransform> hutTransforms,
            NativeArray<FactionTag> hutFactions,
            NativeArray<FarmBuildOrder> hutBuildOrders,
            float totalArea)
        {
            float occupiedArea = 0f;

            // Subtract overlap with older same-faction GathererHut circles
            for (int other = 0; other < hutEntities.Length; other++)
            {
                if (hutEntities[other] == hutEntity) continue;
                if (hutFactions[other].Value != hutFaction) continue;
                if (hutBuildOrders[other].Value >= myOrder) continue;

                var otherPos = hutTransforms[other].Position;
                float dist = math.distance(
                    new float2(hutPos.x, hutPos.z),
                    new float2(otherPos.x, otherPos.z));

                if (dist < GatherRadius * 2f)
                {
                    occupiedArea += CircleCircleIntersection(GatherRadius, GatherRadius, dist);
                }
            }

            // Also subtract enemy GathererHut circles (Bug A fix in fallback)
            for (int other = 0; other < hutEntities.Length; other++)
            {
                if (hutEntities[other] == hutEntity) continue;
                if (hutFactions[other].Value == hutFaction) continue;

                var otherPos = hutTransforms[other].Position;
                float dist = math.distance(
                    new float2(hutPos.x, hutPos.z),
                    new float2(otherPos.x, otherPos.z));

                if (dist < GatherRadius * 2f)
                {
                    occupiedArea += CircleCircleIntersection(GatherRadius, GatherRadius, dist);
                }
            }

            float freeArea = math.max(0f, totalArea - occupiedArea);
            return freeArea / totalArea;
        }

        /// <summary>
        /// Ray-casting point-in-polygon test on the XZ plane.
        /// Returns true if the point lies inside the polygon defined by the vertex buffer.
        /// </summary>
        public static bool PointInPolygon(float2 point, DynamicBuffer<WallEnclosureVertex> vertices)
        {
            int n = vertices.Length;
            bool inside = false;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float2 vi = vertices[i].Position;
                float2 vj = vertices[j].Position;

                if (((vi.y > point.y) != (vj.y > point.y)) &&
                    (point.x < (vj.x - vi.x) * (point.y - vi.y) / (vj.y - vi.y) + vi.x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// Calculate the intersection area of two circles with radii r1, r2
        /// separated by distance d. Used by the fallback path.
        /// </summary>
        private static float CircleCircleIntersection(float r1, float r2, float d)
        {
            if (d >= r1 + r2) return 0f;

            if (d + math.min(r1, r2) <= math.max(r1, r2))
                return math.PI * math.min(r1, r2) * math.min(r1, r2);

            float r1sq = r1 * r1;
            float r2sq = r2 * r2;
            float dsq = d * d;

            float a1 = r1sq * math.acos((dsq + r1sq - r2sq) / (2f * d * r1));
            float a2 = r2sq * math.acos((dsq + r2sq - r1sq) / (2f * d * r2));

            float trianglePart = 0.5f * math.sqrt(
                (-d + r1 + r2) * (d + r1 - r2) * (d - r1 + r2) * (d + r1 + r2));

            return a1 + a2 - trianglePart;
        }
    }
}
