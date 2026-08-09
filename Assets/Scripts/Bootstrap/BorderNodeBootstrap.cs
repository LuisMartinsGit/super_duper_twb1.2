// File: Assets/Scripts/Bootstrap/BorderNodeBootstrap.cs
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
    /// Spawns Veilstone Main Nodes at game start from BorderNodeMarker components
    /// placed in the hand-authored map scene. Each node acts as a The Border
    /// hive: it spreads border ground and controls veilstone faction AI behavior.
    /// Call after terrain and player spawns are initialized.
    /// </summary>
    public static class BorderNodeBootstrap
    {
        /// <summary>
        /// Spawn veilstone main nodes from scene markers.
        /// Returns the number of nodes spawned.
        /// </summary>
        public static int SpawnBorderNodes()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[BorderNodeBootstrap] no ECS world — aborting");
                return 0;
            }

            var em = world.EntityManager;

            if (!MapMarkerRegistry.HasBorderMarkers)
            {
                Debug.LogWarning("[BorderNodeBootstrap] no BorderNodeMarker in the scene — " +
                                 "no border main nodes will spawn. Place markers in the map if " +
                                 "the border should be active.");
                return 0;
            }

            int spawned = SpawnBorderNodesFromMarkers(em);
            EnsureBorderFactionState(em, spawned);
            return spawned;
        }

        /// <summary>
        /// One BorderMainNode per BorderNodeMarker in the scene, snapped to
        /// current terrain height. No gates — the designer is responsible for
        /// placement.
        /// </summary>
        private static int SpawnBorderNodesFromMarkers(EntityManager em)
        {
            var markers = MapMarkerRegistry.BorderNodes;
            int spawned = 0;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;

                var p = m.WorldPosition;
                float y = TerrainUtility.GetHeight(p.x, p.z);
                float3 pos = new float3(p.x, y, p.z);

                BorderMainNode.Create(em, pos);
                spawned++;
                TWBLog.Log($"[BorderNodeBootstrap] (marker) placed node {spawned} at " +
                          $"({pos.x:F0},{pos.z:F0})");
            }
            TWBLog.Log($"[BorderNodeBootstrap] DONE (marker-driven) — nodesSpawned={spawned}");
            return spawned;
        }

        /// <summary>
        /// Initialise the per-match border-faction singletons (resource bank,
        /// wave state, extinction state, victory state).
        /// </summary>
        private static void EnsureBorderFactionState(EntityManager em, int nodesSpawned)
        {
            // Initialize Faction.Border veilstone bank if it doesn't exist
            if (!FactionEconomy.TryGetBank(em, Faction.Border, out _))
            {
                var bankEntity = em.CreateEntity(typeof(FactionTag), typeof(FactionResources));
                em.SetComponentData(bankEntity, new FactionTag { Value = Faction.Border });
                em.SetComponentData(bankEntity, new FactionResources { Veilstone = 100 * nodesSpawned });
            }

            // Initialize attack wave state singleton so BorderAISystem can send waves
            var waveQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BorderWaveState>());
            if (waveQuery.IsEmpty)
            {
                var waveEntity = em.CreateEntity(typeof(BorderWaveState));
                em.SetComponentData(waveEntity, new BorderWaveState
                {
                    WaveTimer = 180f,         // first wave window opens at 3 min
                    WaveInterval = 210f,      // ~3-4 min between waves (re-rolled each fire)
                    WaveNumber = 0,
                    WaveThreshold = 12,       // first wave needs 12 idle units; grows per wave
                });
            }

            // Initialize extinction state singleton so BorderExtinctionSystem
            // gets to run. Without this, RequireForUpdate<BorderExtinctionState>
            // permanently parks the system and the border can't recover after
            // the player wipes its initial nodes.
            var extQuery = em.CreateEntityQuery(ComponentType.ReadOnly<BorderExtinctionState>());
            if (extQuery.IsEmpty)
            {
                var extEntity = em.CreateEntity(typeof(BorderExtinctionState));
                em.SetComponentData(extEntity, new BorderExtinctionState
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
                    LastDestroyerFaction = Faction.Border,
                    LastDestroyerCulture = Cultures.None,
                    VictoryFired = 0,
                });
            }

            // Starter near-patch is spawned by VeilstoneOutcroppingBootstrap (which always
            // runs, with or without the border). This file used to spawn its own
            // 5×320=1600-veilstone starter patch, doubling up with VeilstoneOutcroppingBootstrap
            // when BorderEnabled. Removed — single source of truth.
        }
    }
}
