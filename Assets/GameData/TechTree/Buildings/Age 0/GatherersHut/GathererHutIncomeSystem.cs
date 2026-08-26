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
        // Faction-rotated recalculation (2026-08-16 perf sweep): every 0.5s
        // tick re-evaluates ONE of four faction buckets, so each hut still
        // refreshes every 2s but the ~90k-150k cell iterations of a 4-AI
        // late game no longer land in a single frame (avg 8.8ms spikes).
        private const float UpdateInterval = 0.5f; // One faction bucket per tick
        private int _rotationPhase;

        /// <summary>
        /// Yield multiplier on ground inside the OWNER's influence border.
        /// +50% (user call 2026-08-15); was a flat doubling.
        /// </summary>
        private const float InfluenceYieldMult = 1.5f;

        /// <summary>
        /// Dominant-channel strength at which a cell counts as another player's
        /// territory. Matches the 0.5 border threshold the influence border,
        /// the terrain painter and the own-influence bonus all use, so the
        /// ground a player sees as theirs is exactly the ground that stops
        /// feeding their neighbour.
        /// </summary>
        public const float EnemyOwnershipThreshold = 0.5f;

        // SimCadence, not `ElapsedTime - _lastUpdateTime` — see SimCadence.cs.
        // _lastUpdateTime was zeroed in OnCreate, i.e. when the WORLD was
        // built, and then compared against a clock the lockstep rate manager
        // restarts from zero at install. Whatever the pre-match window put in
        // it survived into the match, so the two peers paid income on
        // different ticks. bank is checksummed.
        private SimCadence.Periodic _cadence;
        private int _nextBuildOrder;

        /// <summary>Match epoch this system last re-phased on. _rotationPhase
        /// is the other half of the bug: it advances once per fire, so the
        /// pre-match window left it at a machine-dependent value and the two
        /// peers paid a DIFFERENT FACTION on the same tick. Fixing the cadence
        /// alone would not have caught that.</summary>
        private int _epoch;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<GathererHutTag>();
            _nextBuildOrder = 0;
        }

        public void OnUpdate(ref SystemState state)
        {
            if (_epoch != SimCadence.Epoch)
            {
                _epoch = SimCadence.Epoch;
                _rotationPhase = 0;
                _nextBuildOrder = 0;
            }

            if (!_cadence.Due(SystemAPI.Time.DeltaTime, UpdateInterval)) return;
            _rotationPhase = (_rotationPhase + 1) & 3;

            double perfT0 = UnityEngine.Time.realtimeSinceStartupAsDouble;
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

            // Flatten the enclosure polygons ONCE. The per-cell loop used to
            // call em.HasBuffer + em.GetBuffer per enclosure per cell — an ECS
            // random-access lookup roughly 125k x enclosures times per pass.
            var enclosureVerts = new NativeList<float2>(64, Allocator.Temp);
            var enclosureRanges = new NativeList<int2>(8, Allocator.Temp);
            for (int e = 0; e < enclosureEntities.Length; e++)
            {
                if (!em.HasBuffer<WallEnclosureVertex>(enclosureEntities[e])) continue;
                var verts = em.GetBuffer<WallEnclosureVertex>(enclosureEntities[e]);
                if (verts.Length < 3) continue;
                int start = enclosureVerts.Length;
                for (int v = 0; v < verts.Length; v++)
                    enclosureVerts.Add(verts[v].Position);
                enclosureRanges.Add(new int2(start, verts.Length));
            }

            // =========================================================
            // Calculate income for each GathererHut using grid sampling
            // =========================================================
            var grid = PassabilityGrid.Instance;
            float totalArea = math.PI * GatherRadius * GatherRadius;

            // Cursed ground yields nothing — sampled per cell below. Same
            // "is this ground cursed" test the exposure DOT and the AI's node
            // pickers use, so the hut agrees with the rest of the game about
            // where the curse is.
            bool hasVeil = SystemAPI.TryGetSingleton<VeilField>(out var veilField)
                && veilField.Initialised == 1 && veilField.Saturation.IsCreated;

            for (int h = 0; h < hutEntities.Length; h++)
            {
                // This tick's faction bucket only (see UpdateInterval note).
                if (((int)hutFactions[h].Value & 3) != _rotationPhase) continue;

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
                        enclosureVerts, enclosureRanges, in veilField, hasVeil);
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

                // Keep the raw coverage readable after the fact — by the time
                // it reaches SuppliesIncome it has been compounded with the
                // guild bonus and the influence doubling and cannot be
                // recovered. See GathererHutYield.
                if (em.HasComponent<GathererHutYield>(hutEntities[h]))
                    em.SetComponentData(hutEntities[h], new GathererHutYield { Ratio = ratio });
                else
                    em.AddComponentData(hutEntities[h], new GathererHutYield { Ratio = ratio });

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

                // Influence bonus: a hut standing inside its owner's influence
                // border (own channel >= 0.5) yields +50%. Was a flat doubling
                // (design 2026-07-06); reduced 2026-08-15 on request.
                bool insideInfluence = PlayerInfluenceMap.ChannelStrengthWorld(
                    (int)hutFactions[h].Value,
                    hutTransforms[h].Position.x,
                    hutTransforms[h].Position.z) >= EnemyOwnershipThreshold;
                if (insideInfluence)
                    effectivePerTick *= InfluenceYieldMult;

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

                    // Fully developed huts are the late-game mine (design
                    // 2026-08-11, Age_1_Alanthor.md § Survey track): map iron
                    // deposits are finite and run dry mid-game, so the survey
                    // drips scale with the Guild level — L2 x1.5, L3 x2 —
                    // and a maxed hut ring with IronSurveying3 carries the
                    // whole iron economy from there.
                    float levelMult = guildLevel switch
                    {
                        2 => 1.5f,
                        3 => 2f,
                        _ => 1f,
                    };
                    ironPerMin      = (int)math.round(ironPerMin * levelMult);
                    veilstonePerMin = (int)math.round(veilstonePerMin * levelMult);

                    // Veilsteel gate (2026-08-04, "how are players getting
                    // veilsteel before age 1??"): the design allows exactly
                    // two sources — the Crucible and a FULLY UPGRADED
                    // Gatherer's Hut. Even with VeilsteelSurvey, a lesser hut
                    // drips nothing (hut levels only exist post-culture, so
                    // this also zeroes it for all of Age 0).
                    if (guildLevel < TheWaningBorder.Core.Settings.BuildingUpgradeConfig.MaxLevel)
                        veilsteelPerMin = 0;

                    // Same +50% as the supplies rule above — the two must move
                    // together or the survey drips quietly outscale the thing
                    // they are meant to complement.
                    if (insideInfluence)
                    {
                        ironPerMin      = (int)math.round(ironPerMin * InfluenceYieldMult);
                        veilstonePerMin = (int)math.round(veilstonePerMin * InfluenceYieldMult);
                        veilsteelPerMin = (int)math.round(veilsteelPerMin * InfluenceYieldMult);
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
            enclosureVerts.Dispose();
            enclosureRanges.Dispose();

            double perfMs = (UnityEngine.Time.realtimeSinceStartupAsDouble - perfT0) * 1000.0;
            if (perfMs >= TheWaningBorder.Core.Diagnostics.PerfSpikeLog.DefaultThresholdMs)
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                    "HutIncome", perfMs, $"huts {hutCount}");
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
            NativeList<float2> enclosureVerts,
            NativeList<int2> enclosureRanges,
            in VeilField veilField,
            bool hasVeil)
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
                    // XZ only — CellToWorld would sample the terrain height
                    // here, once per cell, for a value this loop never reads.
                    float2 cellPos = grid.CellToWorldXZ(cell);

                    // Check if cell is within the gather circle
                    float dx = cellPos.x - hutPos2D.x;
                    float dz = cellPos.y - hutPos2D.y;
                    if (dx * dx + dz * dz > radiusSq)
                        continue;

                    // (totalCells is the full-circle denominator computed
                    // above — we no longer count loop iterations here.)

                    // --- Exclusion 1: Terrain-blocked or building-blocked (Bug C) ---
                    // This is also what makes NoWalk-painted ground yield
                    // nothing: PassabilityGrid bakes the hand-painted "NoWalk"
                    // terrain layer into TerrainBlocked, so it is already
                    // outside Passable here.
                    byte cellValue = grid.GetCell(cell);
                    if (cellValue != PassabilityGrid.Passable)
                    {
                        continue;
                    }

                    // --- Exclusion 5: CURSED GROUND ---
                    // The curse does not feed anyone. Same saturation test the
                    // exposure DOT and the AI's node pickers use.
                    if (hasVeil && veilField.SaturationAt(
                            new float3(cellPos.x, 0f, cellPos.y)) >= VeilField.CrustThreshold)
                        continue;

                    // --- Exclusion 6: GROUND OWNED BY ANOTHER PLAYER ---
                    // Territory that a hostile faction's influence dominates
                    // feeds them, not us. Allied ground still counts (a shared
                    // border must not starve both partners) — Alliances.AreHostile
                    // is the only valid hostility test, docs/Design/Teams.md.
                    // The curse's own channel is skipped here: cursed ground is
                    // handled above and must not be double-counted as "someone
                    // else's territory".
                    if (PlayerInfluenceMap.Sample(cellPos.x, cellPos.y,
                            out int ownerChannel, out float ownerStrength)
                        && ownerStrength >= EnemyOwnershipThreshold
                        && ownerChannel != PlayerInfluenceMap.CurseChannel
                        && ownerChannel != (int)hutFaction
                        && Alliances.AreHostile(hutFaction, (Faction)ownerChannel))
                        continue;

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
                    // (flattened once per pass — see enclosureVerts above)
                    for (int e = 0; e < enclosureRanges.Length; e++)
                    {
                        var range = enclosureRanges[e];
                        if (PointInPolygon(cellPos, enclosureVerts, range.x, range.y))
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
        /// Ray-casting point-in-polygon over a FLATTENED vertex list: the
        /// polygon occupies <paramref name="count"/> entries starting at
        /// <paramref name="start"/>. Lets the per-cell scan test enclosures
        /// without re-fetching a DynamicBuffer from the EntityManager on every
        /// cell.
        /// </summary>
        public static bool PointInPolygon(float2 point, NativeList<float2> vertices,
            int start, int count)
        {
            bool inside = false;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                float2 vi = vertices[start + i];
                float2 vj = vertices[start + j];

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
