// File: Assets/Scripts/Bootstrap/CrystalNodeBootstrap.cs
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Entities;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Bootstrap
{
    /// <summary>
    /// Spawns Crystal Main Nodes at game start.
    /// Each node acts as a Crystal Curse hive: it spreads cursed ground
    /// and controls crystal faction AI behavior.
    /// Call after terrain and player spawns are initialized.
    /// </summary>
    public static class CrystalNodeBootstrap
    {
        private const float MinDistFromPlayers = 60f;
        private const float MinDistBetweenNodes = 50f;

        // Connectivity probe — see CrystalAISystem.HasOpenNeighbourhood. Reject
        // candidates with too few passable neighbours so the curse main never
        // seeds onto a beach pocket where its spawned units would be stranded
        // between water and a cliff edge.
        private const int MinPassableNeighbours = 6;     // out of 8 sampled
        private const float ConnectivityProbeRadius = 10f;

        /// <summary>
        /// Spawn crystal main nodes.
        /// Returns the number of nodes spawned.
        /// </summary>
        public static int SpawnCrystalNodes()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return 0;
            }

            var em = world.EntityManager;
            var random = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xC7A5));

            var playerPositions = GetPlayerPositions(em);
            int half = GameSettings.MapHalfSize;
            float spawnRange = half * 0.7f;

            // At least one curse node per player, up to 2× the player count
            // (random within that range). With 4 players, the map seeds 4-8
            // initial curse nodes — keeps low-player skirmishes survivable
            // while ramping curse pressure on larger lobbies.
            int playerN = math.max(1, playerPositions.Length);
            int nodeCount = random.NextInt(playerN, playerN * 2 + 1);
            UnityEngine.Debug.Log(
                $"[CrystalNodeBootstrap] players={playerPositions.Length} → nodeCount={nodeCount} " +
                $"(range [{playerN}, {playerN * 2}], spawnRange={spawnRange:F0})");
            var nodePosArray = new float3[nodeCount];
            int nodesSpawned = 0;

            for (int n = 0; n < nodeCount; n++)
            {
                float3 nodePos = float3.zero;
                bool found = false;

                for (int attempt = 0; attempt < 30; attempt++)
                {
                    float x = random.NextFloat(-spawnRange, spawnRange);
                    float z = random.NextFloat(-spawnRange, spawnRange);
                    float y = TerrainUtility.GetHeight(x, z);
                    float3 candidate = new float3(x, y, z);

                    // Check not in water
                    var terrain = ProceduralTerrain.Instance;
                    if (terrain != null && terrain.IsInWater(new Vector3(x, y, z)))
                        continue;

                    // Connectivity gate: reject candidates surrounded by water or
                    // cliff (e.g. small beach pockets). Sample 8 directions around
                    // the candidate and require enough passable neighbours.
                    var grid = PassabilityGrid.Instance;
                    if (grid != null)
                    {
                        int passable = 0;
                        for (int d = 0; d < 8; d++)
                        {
                            float a = d * (math.PI * 2f / 8f);
                            float3 sample = candidate + new float3(
                                math.cos(a) * ConnectivityProbeRadius, 0f,
                                math.sin(a) * ConnectivityProbeRadius);
                            sample.y = TerrainUtility.GetHeight(sample.x, sample.z);
                            if (grid.IsPassable(sample)) passable++;
                        }
                        if (passable < MinPassableNeighbours) continue;
                    }

                    // Check distance from all player positions
                    bool tooCloseToPlayer = false;
                    for (int p = 0; p < playerPositions.Length; p++)
                    {
                        if (math.distance(candidate, playerPositions[p]) < MinDistFromPlayers)
                        {
                            tooCloseToPlayer = true;
                            break;
                        }
                    }
                    if (tooCloseToPlayer) continue;

                    // Check distance from already-placed nodes
                    bool tooCloseToNode = false;
                    for (int prev = 0; prev < nodesSpawned; prev++)
                    {
                        if (math.distance(candidate, nodePosArray[prev]) < MinDistBetweenNodes)
                        {
                            tooCloseToNode = true;
                            break;
                        }
                    }
                    if (tooCloseToNode) continue;

                    nodePos = candidate;
                    found = true;
                    break;
                }

                if (!found)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[CrystalNodeBootstrap] node {n + 1}/{nodeCount}: 30 placement attempts " +
                        "all rejected (water / cliff / too close to player or other node).");
                    continue;
                }

                // Create the crystal main node
                CrystalMainNode.Create(em, nodePos);
                nodePosArray[nodesSpawned] = nodePos;
                nodesSpawned++;
                UnityEngine.Debug.Log(
                    $"[CrystalNodeBootstrap] placed main node {nodesSpawned} at " +
                    $"({nodePos.x:F0}, {nodePos.z:F0})");
            }

            if (nodesSpawned == 0)
            {
                UnityEngine.Debug.LogError(
                    "[CrystalNodeBootstrap] ZERO curse main nodes placed — curse will be inactive. " +
                    "Map may be too small / dense with players, or all candidate spots failed " +
                    "the water+connectivity gate. Check the warnings above.");
            }

            // Initialize Faction.Curse crystal bank if it doesn't exist
            if (!FactionEconomy.TryGetBank(em, Faction.Curse, out _))
            {
                var bankEntity = em.CreateEntity(typeof(FactionTag), typeof(FactionResources));
                em.SetComponentData(bankEntity, new FactionTag { Value = Faction.Curse });
                em.SetComponentData(bankEntity, new FactionResources { Crystal = 100 * nodesSpawned });
            }

            // Initialize attack wave state singleton so CrystalAISystem can send waves.
            // First wave fires at WaveTimer = 30s so the curse becomes visible
            // quickly; subsequent waves are 180s apart per spec (overwritten by
            // CrystalAISystem.WaveInterval each tick).
            var waveQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CrystalWaveState>());
            if (waveQuery.IsEmpty)
            {
                var waveEntity = em.CreateEntity(typeof(CrystalWaveState));
                em.SetComponentData(waveEntity, new CrystalWaveState
                {
                    WaveTimer = 30f,
                    WaveInterval = 180f,
                    WaveNumber = 0
                });
            }

            // Initialize extinction state singleton so CrystalExtinctionSystem
            // gets to run. Without this, RequireForUpdate<CrystalExtinctionState>
            // permanently parks the system and the curse can't recover after
            // the player wipes its initial nodes.
            var extQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CrystalExtinctionState>());
            if (extQuery.IsEmpty)
            {
                var extEntity = em.CreateEntity(typeof(CrystalExtinctionState));
                em.SetComponentData(extEntity, new CrystalExtinctionState
                {
                    IsExtinct = 0,
                    RespawnTimer = 0f,
                    HasEverExisted = 1,
                });
            }

            // Starter near-patch is spawned by CrystalPatchBootstrap (which always
            // runs, with or without the curse). This file used to spawn its own
            // 5×320=1600-crystal starter patch, doubling up with CrystalPatchBootstrap
            // when CrystalCurseEnabled. Removed — single source of truth.
            return nodesSpawned;
        }

        /// <summary>
        /// Get player positions from existing Halls, or estimate from spawn layout.
        /// Get player positions from existing Halls, or estimate from spawn layout.
        /// </summary>
        private static float3[] GetPlayerPositions(EntityManager em)
        {
            var hallQuery = em.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<HallTag>(),
                Unity.Entities.ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()
            );

            using var hallTransforms = hallQuery.ToComponentDataArray<Unity.Transforms.LocalTransform>(
                Unity.Collections.Allocator.Temp);

            if (hallTransforms.Length > 0)
            {
                var positions = new float3[hallTransforms.Length];
                for (int i = 0; i < hallTransforms.Length; i++)
                {
                    positions[i] = hallTransforms[i].Position;
                }
                return positions;
            }

            // Fallback: estimate based on player count and map layout
            int playerCount = GameSettings.TotalPlayers;
            int half = GameSettings.MapHalfSize;
            float spawnRadius = half * 0.7f;
            var fallback = new float3[playerCount];

            for (int i = 0; i < playerCount; i++)
            {
                float angle = (i / (float)playerCount) * math.PI * 2f;
                fallback[i] = new float3(
                    math.cos(angle) * spawnRadius,
                    0f,
                    math.sin(angle) * spawnRadius
                );
            }

            return fallback;
        }
    }
}
