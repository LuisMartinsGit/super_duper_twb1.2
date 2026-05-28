// File: Assets/Scripts/Bootstrap/CrystalNodeBootstrap.cs
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Entities;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.MapMarkers;

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

        // Forest avoidance — reject candidates whose centre OR any of the 8
        // sampled probe points sits on a forest splat cell. The cursed
        // ground spreads ~15 m around the node and looks visually wrong
        // overlapping Unity Terrain trees (the trees clip through the
        // crystal shards and don't get destroyed by the spread). Picking
        // a node centre that's clear of forest at the spawn-time radius
        // gives the curse room to grow without obvious tree clip.
        // Matches ProceduralTerrain.ForestSplatLayer / ForestSplatThreshold.
        private const int ForestSplatLayerIndex = 3;
        private const float ForestSplatRejectThreshold = 0.20f;
        private const float ForestProbeRadius = 12f;

        /// <summary>
        /// Spawn crystal main nodes.
        /// Returns the number of nodes spawned.
        /// </summary>
        public static int SpawnCrystalNodes()
        {
            Debug.Log($"[CrystalNodeBootstrap] SpawnCrystalNodes ENTRY — " +
                      $"CrystalCurseEnabled={GameSettings.CrystalCurseEnabled} " +
                      $"FlatTestMap={GameSettings.FlatTestMap} " +
                      $"TotalPlayers={GameSettings.TotalPlayers} " +
                      $"MapHalfSize={GameSettings.MapHalfSize} " +
                      $"SpawnSeed={GameSettings.SpawnSeed}");

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[CrystalNodeBootstrap] no ECS world — aborting");
                return 0;
            }

            var em = world.EntityManager;

            // Hand-authored maps: CurseNodeMarkers in the scene replace the
            // procedural placement entirely. Curse-main balance is sensitive
            // to position (forest avoidance, beach pockets, etc.), so manual
            // placement is the recommended path for designed maps.
            if (MapMarkerRegistry.HasCurseMarkers)
            {
                int spawned = SpawnCurseNodesFromMarkers(em);
                EnsureCurseFactionState(em, spawned);
                return spawned;
            }

            var random = new Unity.Mathematics.Random((uint)(GameSettings.SpawnSeed ^ 0xC7A5));

            var playerPositions = GetPlayerPositions(em);
            int half = GameSettings.MapHalfSize;
            float spawnRange = half * 0.7f;

            int nodeCount = playerPositions.Length; // one node per player
            Debug.Log($"[CrystalNodeBootstrap] playerPositions.Length={playerPositions.Length} " +
                      $"→ nodeCount={nodeCount} spawnRange={spawnRange:F0}");
            var nodePosArray = new float3[nodeCount];
            int nodesSpawned = 0;

            for (int n = 0; n < nodeCount; n++)
            {
                float3 nodePos = float3.zero;
                bool found = false;

                // Two-pass placement: pass 0 enforces the connectivity gate
                // (rejects beach pockets and isolated cliff tops); pass 1
                // disables it as a fallback. The gate depends on
                // PassabilityGrid being baked, which on flat test maps
                // and early-bake races reports every cell as not-passable
                // — would otherwise silently fail every attempt.
                for (int strict = 0; strict < 2 && !found; strict++)
                {
                    bool useConnectivityGate = (strict == 0);
                    int rejWater = 0, rejConnectivity = 0, rejPlayer = 0, rejNode = 0, rejForest = 0;
                    int sampledPassableSum = 0;
                    bool gridSeenNotNull = false;

                    for (int attempt = 0; attempt < 30; attempt++)
                    {
                        float x = random.NextFloat(-spawnRange, spawnRange);
                        float z = random.NextFloat(-spawnRange, spawnRange);
                        float y = TerrainUtility.GetHeight(x, z);
                        float3 candidate = new float3(x, y, z);

                        // Check not in water
                        var terrain = ProceduralTerrain.Instance;
                        if (terrain != null && terrain.IsInWater(new Vector3(x, y, z)))
                        { rejWater++; continue; }

                        // Connectivity gate (only on strict pass).
                        var grid = PassabilityGrid.Instance;
                        if (useConnectivityGate && grid != null)
                        {
                            gridSeenNotNull = true;
                            int passable = 0;
                            for (int d = 0; d < 8; d++)
                            {
                                float a = d * (math.PI * 2f / 8f);
                                float3 sample = candidate + new float3(
                                    math.cos(a) * ConnectivityProbeRadius, 0f,
                                    math.sin(a) * ConnectivityProbeRadius);
                                sample.y = TerrainUtility.GetHeight(sample.x, sample.z);
                                if (grid.IsReachableByAllPlayers(sample)) passable++;
                            }
                            sampledPassableSum += passable;
                            if (passable < MinPassableNeighbours) { rejConnectivity++; continue; }
                        }

                        // Forest avoidance — sample L_FOREST splat at the
                        // candidate centre plus 8 ring points. If any reads
                        // above threshold, the curse would clip Unity Terrain
                        // trees on spread — reject. Active on BOTH strict
                        // passes (we never want trees inside curse).
                        if (IsOnOrNearForest(candidate))
                        { rejForest++; continue; }

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
                        if (tooCloseToPlayer) { rejPlayer++; continue; }

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
                        if (tooCloseToNode) { rejNode++; continue; }

                        nodePos = candidate;
                        found = true;
                        break;
                    }

                    if (!found && strict == 0)
                    {
                        float avgPassable = gridSeenNotNull && rejConnectivity > 0
                            ? sampledPassableSum / (float)rejConnectivity : -1f;
                        Debug.LogWarning(
                            $"[CrystalNodeBootstrap] node {n + 1}/{nodeCount} strict pass failed — " +
                            $"water={rejWater} connectivity={rejConnectivity} forest={rejForest} " +
                            $"player-too-close={rejPlayer} node-too-close={rejNode} " +
                            $"avgPassableSamples={avgPassable:F1}/8. " +
                            "Retrying without connectivity gate.");
                    }
                    else if (!found && strict == 1)
                    {
                        Debug.LogError(
                            $"[CrystalNodeBootstrap] node {n + 1}/{nodeCount} fallback pass also failed — " +
                            $"water={rejWater} forest={rejForest} " +
                            $"player-too-close={rejPlayer} node-too-close={rejNode}.");
                    }
                }

                if (!found) continue;

                // Create the crystal main node
                CrystalMainNode.Create(em, nodePos);
                nodePosArray[nodesSpawned] = nodePos;
                nodesSpawned++;
                Debug.Log($"[CrystalNodeBootstrap] placed node {nodesSpawned} at " +
                          $"({nodePos.x:F0},{nodePos.z:F0})");
            }

            Debug.Log($"[CrystalNodeBootstrap] DONE — nodesSpawned={nodesSpawned}");

            EnsureCurseFactionState(em, nodesSpawned);
            return nodesSpawned;
        }

        /// <summary>
        /// Marker-driven path: one CrystalMainNode per CurseNodeMarker in
        /// the scene, snapped to current terrain height. No gates — the
        /// designer is responsible for placement.
        /// </summary>
        private static int SpawnCurseNodesFromMarkers(EntityManager em)
        {
            var markers = MapMarkerRegistry.CurseNodes;
            int spawned = 0;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;

                var p = m.WorldPosition;
                float y = TerrainUtility.GetHeight(p.x, p.z);
                float3 pos = new float3(p.x, y, p.z);

                CrystalMainNode.Create(em, pos);
                spawned++;
                Debug.Log($"[CrystalNodeBootstrap] (marker) placed node {spawned} at " +
                          $"({pos.x:F0},{pos.z:F0})");
            }
            Debug.Log($"[CrystalNodeBootstrap] DONE (marker-driven) — nodesSpawned={spawned}");
            return spawned;
        }

        /// <summary>
        /// Samples the active terrain's L_FOREST splat weight at the
        /// candidate position plus 8 ring points within
        /// <see cref="ForestProbeRadius"/>. Returns true if ANY sample
        /// exceeds <see cref="ForestSplatRejectThreshold"/> — meaning the
        /// curse-spread area would clip Unity Terrain trees. Falls back
        /// to false if no active terrain / no L_FOREST layer (legacy maps
        /// without the new ProceduralSplat layer set).
        /// </summary>
        private static bool IsOnOrNearForest(float3 candidate)
        {
            var terrain = UnityEngine.Terrain.activeTerrain;
            if (terrain == null || terrain.terrainData == null) return false;
            var td = terrain.terrainData;
            if (td.alphamapLayers <= ForestSplatLayerIndex) return false;

            var tpos = terrain.transform.position;
            int alphaRes = td.alphamapResolution;
            float invSizeX = 1f / td.size.x;
            float invSizeZ = 1f / td.size.z;

            // Sample at centre + 8 ring points so we catch forest just
            // outside the candidate centre too (the curse will spread to
            // ForestProbeRadius within seconds of spawning).
            for (int i = 0; i < 9; i++)
            {
                float sx, sz;
                if (i == 0) { sx = candidate.x; sz = candidate.z; }
                else
                {
                    float a = (i - 1) * (math.PI * 2f / 8f);
                    sx = candidate.x + math.cos(a) * ForestProbeRadius;
                    sz = candidate.z + math.sin(a) * ForestProbeRadius;
                }

                int ax = (int)((sx - tpos.x) * invSizeX * alphaRes);
                int az = (int)((sz - tpos.z) * invSizeZ * alphaRes);
                if (ax < 0 || az < 0 || ax >= alphaRes || az >= alphaRes) continue;

                // One-cell read; allocates a small 1×1×layers array but
                // this only fires during bootstrap, not per frame.
                var splat = td.GetAlphamaps(ax, az, 1, 1);
                if (splat[0, 0, ForestSplatLayerIndex] >= ForestSplatRejectThreshold)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Initialise the per-match curse-faction singletons (resource bank,
        /// wave state, extinction state, victory state). Extracted from the
        /// noise-driven path so the image-driven path can call it too.
        /// </summary>
        private static void EnsureCurseFactionState(EntityManager em, int nodesSpawned)
        {
            // Initialize Faction.Curse crystal bank if it doesn't exist
            if (!FactionEconomy.TryGetBank(em, Faction.Curse, out _))
            {
                var bankEntity = em.CreateEntity(typeof(FactionTag), typeof(FactionResources));
                em.SetComponentData(bankEntity, new FactionTag { Value = Faction.Curse });
                em.SetComponentData(bankEntity, new FactionResources { Crystal = 100 * nodesSpawned });
            }

            // Initialize attack wave state singleton so CrystalAISystem can send waves
            var waveQuery = em.CreateEntityQuery(ComponentType.ReadOnly<CrystalWaveState>());
            if (waveQuery.IsEmpty)
            {
                var waveEntity = em.CreateEntity(typeof(CrystalWaveState));
                em.SetComponentData(waveEntity, new CrystalWaveState
                {
                    WaveTimer = 180f,         // first wave window opens at 3 min
                    WaveInterval = 210f,      // ~3-4 min between waves (re-rolled each fire)
                    WaveNumber = 0,
                    WaveThreshold = 12,       // first wave needs 12 idle units; grows per wave
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

            // Initialize node victory singleton so NodeVictorySystem can run.
            // Tracks per-culture hold timers and Feraldis last-destroyer
            // attribution for the dual node-victory paths (spec §8).
            var victoryQuery = em.CreateEntityQuery(ComponentType.ReadOnly<NodeVictoryState>());
            if (victoryQuery.IsEmpty)
            {
                var victoryEntity = em.CreateEntity(typeof(NodeVictoryState));
                em.SetComponentData(victoryEntity, new NodeVictoryState
                {
                    AlanthorHoldTimer = 0f,
                    RunaiHoldTimer = 0f,
                    LastDestroyerFaction = Faction.Curse,
                    LastDestroyerCulture = Cultures.None,
                    VictoryFired = 0,
                });
            }

            // Starter near-patch is spawned by CrystalPatchBootstrap (which always
            // runs, with or without the curse). This file used to spawn its own
            // 5×320=1600-crystal starter patch, doubling up with CrystalPatchBootstrap
            // when CrystalCurseEnabled. Removed — single source of truth.
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
