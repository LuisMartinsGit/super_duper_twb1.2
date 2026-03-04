// File: Assets/Scripts/Economy/GathererHutIncomeSystem.cs
// Calculates area-based income for GathererHuts (BFME2-style farms)
// Uses first-come-first-served priority: older farms keep full yield,
// newer farms only earn from unclaimed area.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

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
            float totalArea = math.PI * GatherRadius * GatherRadius;

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
            // Snapshot all buildings (obstacles) — excluding under-construction
            // =========================================================
            var buildingQuery = SystemAPI.QueryBuilder()
                .WithAll<BuildingTag, LocalTransform, Radius>()
                .WithNone<UnderConstruction>()
                .Build();

            var bldgEntities = buildingQuery.ToEntityArray(Allocator.Temp);
            var bldgTransforms = buildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var bldgRadii = buildingQuery.ToComponentDataArray<Radius>(Allocator.Temp);

            // =========================================================
            // Calculate income for each GathererHut
            // =========================================================
            for (int h = 0; h < hutEntities.Length; h++)
            {
                var hutPos = hutTransforms[h].Position;
                float occupiedArea = 0f;

                // --- Subtract building footprints within circle ---
                // Skip other GathererHuts here; they are handled in the farm overlap loop.
                for (int b = 0; b < bldgEntities.Length; b++)
                {
                    if (bldgEntities[b] == hutEntities[h]) continue;
                    if (em.HasComponent<GathererHutTag>(bldgEntities[b])) continue;

                    var bldgPos = bldgTransforms[b].Position;
                    float dist = math.distance(
                        new float2(hutPos.x, hutPos.z),
                        new float2(bldgPos.x, bldgPos.z));

                    float bldgRadius = bldgRadii[b].Value;

                    if (dist < GatherRadius + bldgRadius)
                    {
                        if (dist + bldgRadius <= GatherRadius)
                        {
                            occupiedArea += math.PI * bldgRadius * bldgRadius;
                        }
                        else
                        {
                            occupiedArea += CircleCircleIntersection(GatherRadius, bldgRadius, dist);
                        }
                    }
                }

                // --- Subtract overlap with OLDER same-faction GathererHut circles ---
                // Only farms built BEFORE this one reduce its area (first-come-first-served).
                var hutFaction = hutFactions[h].Value;
                int myOrder = hutBuildOrders[h].Value;
                for (int other = 0; other < hutEntities.Length; other++)
                {
                    if (other == h) continue;
                    if (hutFactions[other].Value != hutFaction) continue;
                    if (hutBuildOrders[other].Value >= myOrder) continue;

                    var otherPos = hutTransforms[other].Position;
                    float dist = math.distance(
                        new float2(hutPos.x, hutPos.z),
                        new float2(otherPos.x, otherPos.z));

                    if (dist < GatherRadius * 2f)
                    {
                        float overlap = CircleCircleIntersection(GatherRadius, GatherRadius, dist);
                        occupiedArea += overlap;
                    }
                }

                // --- Clamp and calculate ratio ---
                float freeArea = math.max(0f, totalArea - occupiedArea);
                float ratio = freeArea / totalArea;
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
            bldgEntities.Dispose();
            bldgTransforms.Dispose();
            bldgRadii.Dispose();
        }

        /// <summary>
        /// Calculate the intersection area of two circles with radii r1, r2
        /// separated by distance d.
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
