// File: Assets/Scripts/Economy/WallEnclosureIncomeSystem.cs
// Detects closed wall enclosures for Alanthor factions and grants
// supplies income proportional to the enclosed polygon area.
//
// Algorithm:
// 1. Gather all completed wall hubs per Alanthor faction.
// 2. Build an adjacency graph from WallHubLink buffers.
// 3. Find closed loops by walking the chain (start → walk → return to start).
// 4. Compute polygon area with the Shoelace formula on the XZ plane.
// 5. Create/destroy lightweight income entities so ResourceTickSystem picks them up.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Economy
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct WallEnclosureIncomeSystem : ISystem
    {
        private const float UpdateInterval = 5f;
        private const float IncomePerSquareUnit = 0.5f; // Supplies per square-unit per tick
        private const float TickInterval = 10f;          // Income tick interval (seconds)

        private double _lastUpdateTime;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<WallHubTag>();
            _lastUpdateTime = 0;
        }

        public void OnUpdate(ref SystemState state)
        {
            var currentTime = SystemAPI.Time.ElapsedTime;
            if (currentTime - _lastUpdateTime < UpdateInterval)
                return;
            _lastUpdateTime = currentTime;

            var em = state.EntityManager;

            // ═══════════════════════════════════════════════════════════
            // Step 1: Destroy all existing enclosure income entities (clean slate)
            // ═══════════════════════════════════════════════════════════
            var enclosureQuery = em.CreateEntityQuery(typeof(WallEnclosureIncomeTag));
            var oldEnclosures = enclosureQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < oldEnclosures.Length; i++)
                em.DestroyEntity(oldEnclosures[i]);
            oldEnclosures.Dispose();

            // ═══════════════════════════════════════════════════════════
            // Step 2: Gather all completed wall hubs
            // ═══════════════════════════════════════════════════════════
            var hubQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<WallHubTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());

            // Exclude hubs still under construction
            hubQuery = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<WallHubTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<FactionTag>()
                },
                None = new[]
                {
                    ComponentType.ReadOnly<UnderConstruction>()
                }
            });

            var hubEntities = hubQuery.ToEntityArray(Allocator.Temp);
            var hubTransforms = hubQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var hubFactions = hubQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            if (hubEntities.Length == 0)
            {
                hubEntities.Dispose();
                hubTransforms.Dispose();
                hubFactions.Dispose();
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // Step 3: Determine which factions have Alanthor culture
            // ═══════════════════════════════════════════════════════════
            var hallQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());

            var hallEntities = hallQuery.ToEntityArray(Allocator.Temp);
            var alanthorFactions = new NativeHashSet<byte>(8, Allocator.Temp);

            for (int i = 0; i < hallEntities.Length; i++)
            {
                var fp = em.GetComponentData<FactionProgress>(hallEntities[i]);
                if (fp.Culture == Cultures.Alanthor)
                {
                    var fac = em.GetComponentData<FactionTag>(hallEntities[i]).Value;
                    alanthorFactions.Add((byte)fac);
                }
            }
            hallEntities.Dispose();

            if (alanthorFactions.Count == 0)
            {
                hubEntities.Dispose();
                hubTransforms.Dispose();
                hubFactions.Dispose();
                alanthorFactions.Dispose();
                return;
            }

            // ═══════════════════════════════════════════════════════════
            // Step 4: For each Alanthor faction, build adjacency and find cycles
            // ═══════════════════════════════════════════════════════════

            // Build entity→index map
            var entityToIndex = new NativeHashMap<Entity, int>(hubEntities.Length, Allocator.Temp);
            for (int i = 0; i < hubEntities.Length; i++)
                entityToIndex.TryAdd(hubEntities[i], i);

            // Process each Alanthor faction separately
            for (byte fi = 0; fi < 8; fi++)
            {
                if (!alanthorFactions.Contains(fi)) continue;
                var faction = (Faction)fi;

                // Collect hubs for this faction
                var factionHubs = new NativeList<int>(Allocator.Temp);
                for (int i = 0; i < hubEntities.Length; i++)
                {
                    if (hubFactions[i].Value == faction)
                        factionHubs.Add(i);
                }

                if (factionHubs.Length < 3)
                {
                    factionHubs.Dispose();
                    continue; // Need at least 3 hubs to form a polygon
                }

                // Build adjacency list from WallHubLink buffers
                var adjacency = new NativeParallelMultiHashMap<int, int>(factionHubs.Length * 4, Allocator.Temp);

                for (int fi2 = 0; fi2 < factionHubs.Length; fi2++)
                {
                    int idx = factionHubs[fi2];
                    var hubEntity = hubEntities[idx];

                    if (!em.HasBuffer<WallHubLink>(hubEntity)) continue;

                    var links = em.GetBuffer<WallHubLink>(hubEntity);
                    for (int li = 0; li < links.Length; li++)
                    {
                        var connectedHub = links[li].ConnectedHub;
                        // Only include if the connected hub also exists and belongs to same faction
                        if (entityToIndex.TryGetValue(connectedHub, out int connIdx))
                        {
                            if (hubFactions[connIdx].Value == faction)
                            {
                                adjacency.Add(idx, connIdx);
                            }
                        }
                    }
                }

                // Find cycles using chain-walking
                var visited = new NativeHashSet<int>(factionHubs.Length, Allocator.Temp);
                var usedInCycle = new NativeHashSet<int>(factionHubs.Length, Allocator.Temp);

                for (int fi2 = 0; fi2 < factionHubs.Length; fi2++)
                {
                    int startIdx = factionHubs[fi2];
                    if (usedInCycle.Contains(startIdx)) continue;

                    // Count connections for this hub
                    int connCount = 0;
                    if (adjacency.TryGetFirstValue(startIdx, out _, out var it))
                    {
                        connCount++;
                        while (adjacency.TryGetNextValue(out _, ref it)) connCount++;
                    }
                    if (connCount < 2) continue; // Need at least 2 connections to be part of a cycle

                    // Walk chain from this hub, try to find a cycle
                    var cycle = TryFindCycle(startIdx, adjacency, factionHubs.Length);
                    if (cycle.IsCreated && cycle.Length >= 3)
                    {
                        // Compute polygon area using Shoelace formula on XZ plane
                        float area = ComputePolygonArea(cycle, hubEntities, hubTransforms);

                        if (area > 1f) // Minimum area threshold
                        {
                            // Create an income entity for this enclosure
                            float perTick = area * IncomePerSquareUnit;

                            var incomeEntity = em.CreateEntity(
                                typeof(WallEnclosureIncomeTag),
                                typeof(FactionTag),
                                typeof(SuppliesIncome));

                            em.SetComponentData(incomeEntity, new WallEnclosureIncomeTag { FactionIndex = fi });
                            em.SetComponentData(incomeEntity, new FactionTag { Value = faction });
                            em.SetComponentData(incomeEntity, new SuppliesIncome
                            {
                                PerTick = perTick,
                                Interval = TickInterval,
                                Elapsed = 0f
                            });

                            // Mark all hubs in this cycle so we don't recount them
                            for (int ci = 0; ci < cycle.Length; ci++)
                                usedInCycle.Add(cycle[ci]);
                        }
                    }
                    if (cycle.IsCreated) cycle.Dispose();
                }

                visited.Dispose();
                usedInCycle.Dispose();
                adjacency.Dispose();
                factionHubs.Dispose();
            }

            entityToIndex.Dispose();
            alanthorFactions.Dispose();
            hubEntities.Dispose();
            hubTransforms.Dispose();
            hubFactions.Dispose();
        }

        /// <summary>
        /// Walk the hub graph from startIdx, trying to find a simple cycle.
        /// Uses chain walking: always pick the neighbor that isn't the previous node.
        /// Returns the list of hub indices forming the cycle, or empty if none found.
        /// </summary>
        private static NativeList<int> TryFindCycle(
            int startIdx,
            NativeParallelMultiHashMap<int, int> adjacency,
            int maxNodes)
        {
            var path = new NativeList<int>(maxNodes, Allocator.Temp);
            var inPath = new NativeHashSet<int>(maxNodes, Allocator.Temp);

            path.Add(startIdx);
            inPath.Add(startIdx);

            int current = startIdx;
            int previous = -1;
            int maxSteps = maxNodes + 1; // Safety limit

            for (int step = 0; step < maxSteps; step++)
            {
                // Find a neighbor that isn't the previous node
                int next = -1;
                bool foundStart = false;

                if (adjacency.TryGetFirstValue(current, out int neighbor, out var it))
                {
                    do
                    {
                        if (neighbor == previous) continue;

                        // If we've returned to start and walked at least 3 nodes, cycle found!
                        if (neighbor == startIdx && path.Length >= 3)
                        {
                            foundStart = true;
                            break;
                        }

                        // If neighbor not in path, walk to it
                        if (!inPath.Contains(neighbor))
                        {
                            next = neighbor;
                            break;
                        }
                    } while (adjacency.TryGetNextValue(out neighbor, ref it));
                }

                if (foundStart)
                {
                    // Cycle complete
                    inPath.Dispose();
                    return path;
                }

                if (next == -1)
                {
                    // Dead end — no cycle from this start
                    inPath.Dispose();
                    path.Dispose();
                    return default;
                }

                path.Add(next);
                inPath.Add(next);
                previous = current;
                current = next;
            }

            // Didn't find a cycle within maxSteps
            inPath.Dispose();
            path.Dispose();
            return default;
        }

        /// <summary>
        /// Compute the area of a polygon defined by hub indices using the Shoelace formula.
        /// Works on the XZ plane (ignoring Y).
        /// </summary>
        private static float ComputePolygonArea(
            NativeList<int> cycle,
            NativeArray<Entity> hubEntities,
            NativeArray<LocalTransform> hubTransforms)
        {
            if (cycle.Length < 3) return 0f;

            float sum = 0f;
            int n = cycle.Length;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var posI = hubTransforms[cycle[i]].Position;
                var posJ = hubTransforms[cycle[j]].Position;

                sum += posI.x * posJ.z - posJ.x * posI.z;
            }

            return math.abs(sum) * 0.5f;
        }
    }
}
