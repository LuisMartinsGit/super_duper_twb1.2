// File: Assets/Scripts/Bootstrap/VeilsteelDepositBootstrap.cs
// Spawns the Veilsteel "Sharp Crystals" map resource from VeilsteelDepositMarker
// components placed in the hand-authored map scene. Unlike iron (patches of
// many small deposits), veilsteel is a SINGLE node per marker holding the
// marker's full amount (design default 1500). Mining behaviour is identical
// to iron — MiningSystem reuses IronDepositState with the veilsteel tag.

using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.Core.Multiplayer;

namespace TheWaningBorder.Bootstrap
{
    public static class VeilsteelDepositBootstrap
    {
        // Presentation ID (must match PresentationSpawnSystem)
        public const int VeilsteelDepositPresentationId = 403;

        // Larger than a single iron deposit — one node carries a whole
        // expansion's worth of veilsteel and should read as a landmark.
        private const float DepositRadius = 2.2f;

        public static void SpawnVeilsteelDeposits()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;

            if (!MapMarkerRegistry.HasVeilsteelMarkers)
            {
                // No authored marker (2026-08-04): the Sharp Crystals node
                // spawns at the midpoint between all players — neutral,
                // contestable ground by construction — marching outwards in
                // rings until a passable spot accepts it.
                SpawnFallbackAtMidpoint(em);
                return;
            }

            var markers = MapMarkerRegistry.VeilsteelDeposits;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                if (m == null) continue;

                var p = m.WorldPosition;
                float y = TerrainUtility.GetHeight(p.x, p.z);
                CreateVeilsteelDepositEntity(em, new float3(p.x, y, p.z), m.Amount);
            }
        }

        /// <summary>Sharp Crystals payload when no marker authored one.</summary>
        private const int DefaultAmount = 1500;
        /// <summary>How far the fallback search marches out from the exact
        /// midpoint before giving up (no node beats a stupid node).</summary>
        private const float FallbackMaxMarch = 90f;

        /// <summary>Spawn the single Sharp Crystals node at the midpoint of
        /// all player Halls, marching outwards in 6 m rings while the spot is
        /// impassable (cliff, water, obstacle, building). Equidistant by
        /// construction, so it stays a fair contest objective on any map.</summary>
        private static void SpawnFallbackAtMidpoint(EntityManager em)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            if (xfs.Length == 0) return; // no players yet — nothing to contest

            float3 mid = float3.zero;
            for (int i = 0; i < xfs.Length; i++) mid += xfs[i].Position;
            mid /= xfs.Length;

            var grid = PassabilityGrid.Instance;
            for (float r = 0f; r <= FallbackMaxMarch; r += 6f)
            {
                int steps = r < 1f ? 1 : (int)math.max(8f, r * 0.8f);
                for (int s = 0; s < steps; s++)
                {
                    float angle = (s / (float)steps) * math.PI * 2f;
                    float x = mid.x + math.cos(angle) * r;
                    float z = mid.z + math.sin(angle) * r;

                    if (grid != null)
                    {
                        var cell = grid.WorldToCell(new float3(x, 0f, z));
                        if (cell.x < 0 || cell.x >= grid.Width
                            || cell.y < 0 || cell.y >= grid.Height) continue;
                        if (grid.GetCell(cell) != PassabilityGrid.Passable) continue;
                    }

                    float y = TerrainUtility.GetHeight(x, z);
                    CreateVeilsteelDepositEntity(em, new float3(x, y, z), DefaultAmount);
                    Debug.Log($"[VeilsteelDeposit] No marker — Sharp Crystals spawned at " +
                              $"player midpoint ({x:0},{z:0}), march {r:0}m.");
                    return;
                }
            }
            Debug.LogWarning("[VeilsteelDeposit] No marker and no passable spot within " +
                             $"{FallbackMaxMarch}m of the player midpoint — no Sharp Crystals node.");
        }

        private static Entity CreateVeilsteelDepositEntity(EntityManager em, float3 position, int amount)
        {
            var entity = em.CreateEntity(
                typeof(VeilsteelDepositTag),
                // ObstacleTag — units route around the node on the passability
                // grid, same treatment as iron deposits.
                typeof(ObstacleTag),
                typeof(IronDepositState),   // shared deposit state — see VeilsteelDepositTag docs
                typeof(LocalTransform),
                typeof(Radius),
                typeof(PresentationId)
            );

            em.SetComponentData(entity, LocalTransform.FromPosition(position));
            em.SetComponentData(entity, new Radius { Value = DepositRadius });
            em.SetComponentData(entity, new PresentationId { Id = VeilsteelDepositPresentationId });
            em.SetComponentData(entity, new IronDepositState
            {
                RemainingIron = amount,
                InitialIron = amount,
                Depleted = 0
            });

            em.AddComponentData(entity, new NetworkedEntity
            {
                NetworkId = NetworkIdGenerator.GetNextId(),
                SpawnTick = 0
            });

            var grid = PassabilityGrid.Instance;
            if (grid != null)
                grid.BlockObstacle(position, DepositRadius);

            return entity;
        }
    }
}
