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
    /// Spawns Veilstone Main Nodes (wells) at game start.
    ///
    /// DEFAULT — ONE PER MAP CORNER (design 2026-08-11: the curse presses in
    /// from the four corners, so every base reads its nearest corner as "its"
    /// well). BorderNodeMarker presence is the designer's on/off lever for the
    /// Border faction and marker positions are ignored.
    ///
    /// PER-MAP OVERRIDE (2026-08-12) — a map may author its own well set by
    /// ticking <see cref="BorderNodeMarker.AuthoredPosition"/>. Then the
    /// ticked markers ARE the well list: one well each, where they stand, and
    /// N is whatever the map placed. Corner wells say nothing about ground
    /// that matters on a duel map with a single contested middle, or a river
    /// map whose crossings are the fight; those maps place their own.
    ///
    /// Everything downstream is already N-agnostic: NodeVictorySystem scores
    /// well domination against the live node count, so N = 1 and N = 4 both
    /// work without a special case.
    ///
    /// Call after terrain and player spawns are initialized.
    /// </summary>
    public static class BorderNodeBootstrap
    {
        /// <summary>Corner inset as a fraction of each map axis — nodes sit
        /// 12% in from the playable-bounds corner before terrain fitting.</summary>
        private const float CornerInsetFraction = 0.12f;

        /// <summary>
        /// Spawn the map's wells. Returns the number of nodes spawned.
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
                                 "if the border should be active; positions are corner-derived " +
                                 "unless a marker ticks AuthoredPosition.");
                return 0;
            }

            int spawned = HasAuthoredWells()
                ? SpawnAuthoredNodes(em)
                : SpawnCornerNodes(em);
            EnsureBorderFactionState(em, spawned);
            return spawned;
        }

        /// <summary>True when at least one marker claims its own position —
        /// which hands the whole well list to the map.</summary>
        private static bool HasAuthoredWells()
        {
            var markers = MapMarkerRegistry.BorderNodes;
            for (int i = 0; i < markers.Count; i++)
                if (markers[i] != null && markers[i].AuthoredPosition) return true;
            return false;
        }

        /// <summary>
        /// One well per AuthoredPosition marker, at the marker's position,
        /// fitted to passable ground the same way corner wells are (a marker
        /// nudged onto a crag or into a river would otherwise spawn a well
        /// nobody can reach — and an unreachable well can never be claimed,
        /// which makes well-domination victory unwinnable for everyone).
        ///
        /// Un-ticked markers are deliberately skipped rather than merged: a
        /// half-authored well set is a silent balance change.
        /// </summary>
        private static int SpawnAuthoredNodes(EntityManager em)
        {
            TerrainUtility.GetPlayableBounds(out var mn, out var mx);
            float2 center = (mn + mx) * 0.5f;

            var markers = MapMarkerRegistry.BorderNodes;
            int spawned = 0;
            int ignored = 0;

            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;
                if (!m.AuthoredPosition) { ignored++; continue; }

                var p = m.WorldPosition;
                float2 xz = FitToPassableGround(new float2(p.x, p.z), center);
                float3 pos = new float3(xz.x, TerrainUtility.GetHeight(xz.x, xz.y), xz.y);
                BorderMainNode.Create(em, pos);
                spawned++;
                TWBLog.Log($"[BorderNodeBootstrap] authored well {spawned} at " +
                          $"({pos.x:F0},{pos.z:F0}) from marker '{m.name}'");
            }

            if (ignored > 0)
                Debug.LogWarning($"[BorderNodeBootstrap] {ignored} BorderNodeMarker(s) without " +
                                 "AuthoredPosition were IGNORED — this map authors its wells, so " +
                                 "every well marker must tick AuthoredPosition.");

            TWBLog.Log($"[BorderNodeBootstrap] DONE (map-authored) — nodesSpawned={spawned}");
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
