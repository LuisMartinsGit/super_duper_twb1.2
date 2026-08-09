// File: Assets/GameData/TechTree/Buildings/Age 0/GatherersHut/GathererHutIncomeSystem.cs
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
using TheWaningBorder.Influence;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Economy
{
    // No [BurstCompile] — runs every 2s, not perf-critical,
    // and we need reliable structural changes (AddComponent).
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct GathererHutIncomeSystem : ISystem
    {
        /// <summary>Radius of the resource gathering circle around each GathererHut.</summary>
        public const float GatherRadius = 19.5f;

        // Age_0.md: GathererHut emits 60 S/min at 100 % area.
        private const float BasePerTick = 10f;     // 10 supplies per 10-s tick → 60 S/min
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

            var perfSw = System.Diagnostics.Stopwatch.StartNew();
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
                // Feraldis Raider Camps do NOT gather. The hut is the same
                // entity, but at age-up it switched to producing Plunderers
                // and the faction's income is whatever they steal
                // (docs/Design/Age_1_Feraldis.md § Raider Camp). Zero its
                // drips so the two economies never stack.
                if (em.HasComponent<RaiderCampTag>(hutEntities[h]))
                {
                    var zeroed = em.GetComponentData<SuppliesIncome>(hutEntities[h]);
                    em.SetComponentData(hutEntities[h], new SuppliesIncome
                    {
                        PerTick = 0f,
                        Interval = TickInterval,
                        Elapsed = zeroed.Elapsed
                    });
                    SetSecondaryIncome(em, hutEntities[h], 0, 0, 0);
                    continue;
                }

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

                // Alanthor Guild level ladder: flat supplies-per-tick bonus that
                // scales with building level (L1 +5, L2 +10, L3 +20). Added
                // before the influence doubling so it inherits the ×2 bonus.
                byte guildLevel = 0;
                if (em.HasComponent<BuildingUpgradeState>(hutEntities[h]))
                    guildLevel = em.GetComponentData<BuildingUpgradeState>(hutEntities[h]).Level;
                effectivePerTick += guildLevel switch
                {
                    1 => 5f,
                    2 => 10f,
                    3 => 20f,
                    _ => 0f,
                };

                // Influence bonus (design 2026-07-06): ground inside the
                // owner's influence border (own channel ≥ 0.5) produces
                // double the resources.
                bool insideInfluence = PlayerInfluenceMap.ChannelStrengthWorld(
                    (int)hutFactions[h].Value,
                    hutTransforms[h].Position.x,
                    hutTransforms[h].Position.z) >= 0.5f;
                if (insideInfluence)
                    effectivePerTick *= 2f;

                // --- Update the component (preserve Elapsed timer) ---
                var current = em.GetComponentData<SuppliesIncome>(hutEntities[h]);
                em.SetComponentData(hutEntities[h], new SuppliesIncome
                {
                    PerTick = effectivePerTick,
                    Interval = TickInterval,
                    Elapsed = current.Elapsed
                });

                // Alanthor Guild "Survey" research: huts passively generate
                // secondary resources at flat per-minute rates (doubled inside
                // the owner's influence border, matching the supplies rule).
                // The Surveys are the ONLY hut drips — DeepGathering was
                // removed outright (2026-08-04; its compat shim leaked
                // veilsteel from every hut straight from Age 0).
                var research = FactionResearchState.Instance;
                int ironPerMin = 0, veilstonePerMin = 0, veilsteelPerMin = 0;
                if (research != null)
                {
                    var f = hutFactions[h].Value;

                    if (research.HasResearched(f, "IronSurveying3"))      ironPerMin = 42;
                    else if (research.HasResearched(f, "IronSurveying2")) ironPerMin = 24;
                    else if (research.HasResearched(f, "IronSurveying1")) ironPerMin = 12;

                    if (research.HasResearched(f, "VeilstoneSurvey2"))      veilstonePerMin = 18;
                    else if (research.HasResearched(f, "VeilstoneSurvey1")) veilstonePerMin = 6;

                    if (research.HasResearched(f, "VeilsteelSurvey")) veilsteelPerMin = 6;

                    // Veilsteel gate (2026-08-04, "how are players getting
                    // veilsteel before age 1??"): the design allows exactly
                    // two sources — the Crucible and a FULLY UPGRADED
                    // Gatherer's Hut. Even with VeilsteelSurvey, a lesser hut
                    // drips nothing (hut levels only exist post-culture, so
                    // this also zeroes it for all of Age 0).
                    if (guildLevel < TheWaningBorder.Core.Settings.BuildingUpgradeConfig.MaxLevel)
                        veilsteelPerMin = 0;

                    if (insideInfluence)
                    {
                        ironPerMin      *= 2;
                        veilstonePerMin *= 2;
                        veilsteelPerMin *= 2;
                    }
                }
                SetSecondaryIncome(em, hutEntities[h], ironPerMin, veilstonePerMin, veilsteelPerMin);
            }

            // Cleanup
            int hutCount = hutEntities.Length;
            hutEntities.Dispose();
            hutTransforms.Dispose();
            hutFactions.Dispose();
            hutBuildOrders.Dispose();
            enclosureEntities.Dispose();

            perfSw.Stop();
            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                "HutIncome", perfSw.Elapsed.TotalMilliseconds, $"huts {hutCount}");
        }

        /// <summary>Set (or attach) the hut's Survey secondary income
        /// components. Zero rates stay attached but inert —
        /// ResourceTickSystem skips PerMinute &lt;= 0.</summary>
        private static void SetSecondaryIncome(EntityManager em, Entity hut,
            int ironPerMinute, int veilstonePerMinute, int veilsteelPerMinute)
        {
            if (em.HasComponent<IronIncome>(hut))
            {
                var c = em.GetComponentData<IronIncome>(hut);
                c.PerMinute = ironPerMinute;
                em.SetComponentData(hut, c);
            }
            else if (ironPerMinute > 0)
            {
                em.AddComponentData(hut, new IronIncome { PerMinute = ironPerMinute });
            }

            if (em.HasComponent<VeilstoneIncome>(hut))
            {
                var c = em.GetComponentData<VeilstoneIncome>(hut);
                c.PerMinute = veilstonePerMinute;
                em.SetComponentData(hut, c);
            }
            else if (veilstonePerMinute > 0)
            {
                em.AddComponentData(hut, new VeilstoneIncome { PerMinute = veilstonePerMinute });
            }

            if (em.HasComponent<VeilsteelIncome>(hut))
            {
                var c = em.GetComponentData<VeilsteelIncome>(hut);
                c.PerMinute = veilsteelPerMinute;
                em.SetComponentData(hut, c);
            }
            else if (veilsteelPerMinute > 0)
            {
                em.AddComponentData(hut, new VeilsteelIncome { PerMinute = veilsteelPerMinute });
            }
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

            // Only huts whose circle can overlap OURS can ever claim one of
            // our cells — anything further than 2R is irrelevant. 2026-08-05:
            // the per-cell loops below previously scanned EVERY hut on the
            // map for EVERY sampled cell, O(huts^2 x cells) — with 8 players'
            // economies that was tens of millions of distance checks on the
            // main thread every 2 s (the 8-FFA hitch). Typical relevant
            // neighbour count is 0-4.
            var relevantSame = new NativeList<int>(8, Allocator.Temp);
            var relevantEnemy = new NativeList<int>(8, Allocator.Temp);
            float overlapSq = (GatherRadius * 2f) * (GatherRadius * 2f);
            for (int other = 0; other < hutEntities.Length; other++)
            {
                if (hutEntities[other] == hutEntity) continue;
                // A Feraldis Raider Camp harvests NOTHING, so it must not
                // contest gather area either — otherwise it was free area
                // denial: a camp starved a neighbouring hut's yield while
                // producing zero itself (design 2026-08-05 rev.4, "Feraldis
                // huts do not gather from an area around them").
                if (em.HasComponent<RaiderCampTag>(hutEntities[other])) continue;
                var op = hutTransforms[other].Position;
                float odx = op.x - hutPos.x, odz = op.z - hutPos.z;
                if (odx * odx + odz * odz > overlapSq) continue;
                if (hutFactions[other].Value == hutFaction)
                {
                    if (hutBuildOrders[other].Value < myOrder) relevantSame.Add(other);
                }
                else
                    relevantEnemy.Add(other);
            }

            // Determine cell scan bounds — clamped to the grid so we don't
            // read out-of-range indices. The DENOMINATOR (totalCells) is
            // computed below from the full circle area, NOT from the loop's
            // iteration count, so a hut placed near the map edge can't get
            // 100 % income just because its in-bounds cells happen to be all
            // passable (BuildCommand.IsValidBuildPosition rejects out-of-bounds
            // placement now, but this keeps the income calc honest even when
            // pre-existing huts straddle the edge from older saves).
            int2 minCell = grid.WorldToCell(new float3(hutPos.x - GatherRadius, 0f, hutPos.z - GatherRadius));
            int2 maxCell = grid.WorldToCell(new float3(hutPos.x + GatherRadius, 0f, hutPos.z + GatherRadius));
            minCell = math.max(minCell, int2.zero);
            maxCell = math.min(maxCell, new int2(grid.Width - 1, grid.Height - 1));

            // Total cells the gather circle WOULD cover at the configured
            // cell size — denominator used by the final ratio.
            float circleArea = math.PI * radiusSq;
            float cellArea = grid.CellSize * grid.CellSize;
            int totalCells = (int)math.max(1f, math.round(circleArea / cellArea));

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

                    // (totalCells is the full-circle denominator computed
                    // above — we no longer count loop iterations here.)

                    // --- Exclusion 1: Terrain-blocked or building-blocked (Bug C) ---
                    byte cellValue = grid.GetCell(cell);
                    if (cellValue != PassabilityGrid.Passable)
                    {
                        continue;
                    }

                    // --- Exclusion 2: Inside older same-faction GathererHut circle ---
                    // (pre-filtered neighbour list — see relevantSame above)
                    bool excluded = false;
                    for (int r = 0; r < relevantSame.Length; r++)
                    {
                        int other = relevantSame[r];
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
                    // (pre-filtered neighbour list — see relevantEnemy above)
                    for (int r = 0; r < relevantEnemy.Length; r++)
                    {
                        int other = relevantEnemy[r];
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

            relevantSame.Dispose();
            relevantEnemy.Dispose();

            // totalCells is fixed by the circle area so it's never zero; the
            // denominator stays stable even if the hut is at the map edge.
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
