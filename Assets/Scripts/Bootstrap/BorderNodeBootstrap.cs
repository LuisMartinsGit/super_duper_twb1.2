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
    /// Spawns Veilstone Main Nodes at game start — ONE PER MAP CORNER
    /// (design 2026-08-11: the old centre-of-map marker well is gone; the
    /// curse presses in from the four corners instead, so every base reads
    /// its nearest corner as "its" well). BorderNodeMarker presence in the
    /// scene stays the designer's on/off lever for the Border faction, but
    /// marker POSITIONS are no longer used.
    /// Call after terrain and player spawns are initialized.
    /// </summary>
    public static class BorderNodeBootstrap
    {
        /// <summary>Corner inset as a fraction of each map axis — nodes sit
        /// 12% in from the playable-bounds corner before terrain fitting.</summary>
        private const float CornerInsetFraction = 0.12f;

        /// <summary>
        /// Spawn the four corner wells. Returns the number of nodes spawned.
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
                                 "no border main nodes will spawn. Place at least one marker " +
                                 "(anywhere) if the border should be active; node POSITIONS " +
                                 "are corner-derived, not marker-derived (2026-08-11).");
                return 0;
            }

            int spawned = SpawnCornerNodes(em);
            EnsureBorderFactionState(em, spawned);
            return spawned;
        }

        /// <summary>
        /// One BorderMainNode per map corner, inset from the playable bounds
        /// and walked inward along the corner diagonal until the ground is
        /// actually passable (a corner buried in mountain or water slides
        /// toward the map centre until it fits).
        /// </summary>
        private static int SpawnCornerNodes(EntityManager em)
        {
            TerrainUtility.GetPlayableBounds(out var mn, out var mx);
            float2 center = (mn + mx) * 0.5f;
            float insetX = (mx.x - mn.x) * CornerInsetFraction;
            float insetZ = (mx.y - mn.y) * CornerInsetFraction;

            var corners = new float2[4]
            {
                new float2(mn.x + insetX, mn.y + insetZ),
                new float2(mx.x - insetX, mn.y + insetZ),
                new float2(mn.x + insetX, mx.y - insetZ),
                new float2(mx.x - insetX, mx.y - insetZ),
            };

            int spawned = 0;
            for (int i = 0; i < corners.Length; i++)
            {
                float2 xz = FitToPassableGround(corners[i], center);
                float3 pos = new float3(xz.x, TerrainUtility.GetHeight(xz.x, xz.y), xz.y);
                BorderMainNode.Create(em, pos);
                spawned++;
                TWBLog.Log($"[BorderNodeBootstrap] corner well {spawned}/4 at " +
                          $"({pos.x:F0},{pos.z:F0})");
            }
            TWBLog.Log($"[BorderNodeBootstrap] DONE (corner-driven) — nodesSpawned={spawned}");
            return spawned;
        }

        /// <summary>Walk from the corner candidate toward the map centre in
        /// 4 m steps until the node footprint stands on passable ground.
        /// Falls back to the raw candidate if nothing fits within 120 m —
        /// deterministic either way.</summary>
        private static float2 FitToPassableGround(float2 candidate, float2 center)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null) return candidate;

            float2 dir = center - candidate;
            float len = math.length(dir);
            if (len < 1f) return candidate;
            dir /= len;

            for (float step = 0f; step <= 120f; step += 4f)
            {
                float2 p = candidate + dir * step;
                if (grid.IsPassableForRadius(
                        new float3(p.x, 0f, p.y), BorderConstants.MainNodeRadius))
                    return p;
            }
            return candidate;
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
