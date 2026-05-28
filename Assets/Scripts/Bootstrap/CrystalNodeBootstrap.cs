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
    /// Spawns Crystal Main Nodes at game start from CurseNodeMarker components
    /// placed in the hand-authored map scene. Each node acts as a Crystal Curse
    /// hive: it spreads cursed ground and controls crystal faction AI behavior.
    /// Call after terrain and player spawns are initialized.
    /// </summary>
    public static class CrystalNodeBootstrap
    {
        /// <summary>
        /// Spawn crystal main nodes from scene markers.
        /// Returns the number of nodes spawned.
        /// </summary>
        public static int SpawnCrystalNodes()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                Debug.LogError("[CrystalNodeBootstrap] no ECS world — aborting");
                return 0;
            }

            var em = world.EntityManager;

            if (!MapMarkerRegistry.HasCurseMarkers)
            {
                Debug.LogWarning("[CrystalNodeBootstrap] no CurseNodeMarker in the scene — " +
                                 "no curse main nodes will spawn. Place markers in the map if " +
                                 "the curse should be active.");
                return 0;
            }

            int spawned = SpawnCurseNodesFromMarkers(em);
            EnsureCurseFactionState(em, spawned);
            return spawned;
        }

        /// <summary>
        /// One CrystalMainNode per CurseNodeMarker in the scene, snapped to
        /// current terrain height. No gates — the designer is responsible for
        /// placement.
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
        /// Initialise the per-match curse-faction singletons (resource bank,
        /// wave state, extinction state, victory state).
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
    }
}
