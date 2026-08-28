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
using TheWaningBorder.Entities;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Regions;
using System.Collections.Generic;

namespace TheWaningBorder.Bootstrap
{
    public static class VeilsteelDepositBootstrap
    {
        /// <summary>Presentation id. Forwards to the factory so the id has one
        /// definition; kept here because PresentationSpawnSystem's dispatch
        /// table already names it through this class.</summary>
        public const int VeilsteelDepositPresentationId = VeilsteelDeposit.PresentationID;

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
                // …and then spread the rest across the map: one contested node
                // is a fine objective and a poor economy.
                SeedTerritoryCoverage(em);
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

            SeedTerritoryCoverage(em);
        }

        /// <summary>
        /// VEILSTEEL IS SCARCE BY DESIGN: about one territory in three carries
        /// a deposit, and no territory carries two.
        ///
        /// Scarcity is what makes it worth taking ground for. Before this it
        /// was a SINGLE node on the whole map (the midpoint fallback) or
        /// whatever a map author happened to place, so on most maps veilsteel
        /// was either uncontested or absent — and either way it was not a
        /// reason to expand.
        ///
        /// A TOP-UP, not a replacement: authored markers are honoured first and
        /// only the shortfall is seeded, so a hand-built map keeps its
        /// composition. Deterministic — regions are walked in index order and
        /// every third one that lacks a deposit gets one, so every lockstep peer
        /// seeds the identical map from the identical partition.
        /// </summary>
        private static void SeedTerritoryCoverage(EntityManager em)
        {
            if (!RegionMap.Ready || RegionMap.Count == 0) return;

            int wanted = Mathf.Max(1, Mathf.RoundToInt(RegionMap.Count / 3f));

            // Which regions already carry one — from markers, the fallback, or
            // an earlier call.
            var have = new HashSet<int>();
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilsteelDepositTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                for (int i = 0; i < xfs.Length; i++)
                {
                    int r = RegionMap.RegionAt(xfs[i].Position.x, xfs[i].Position.z);
                    if (r != RegionMap.None) have.Add(r);
                }
            q.Dispose();

            if (have.Count >= wanted) return;

            // Stride by three so the deposits are spread across the map rather
            // than clustered in the low-numbered regions, then sweep for any
            // remainder if the strided pass could not fill the quota.
            for (int pass = 0; pass < 2 && have.Count < wanted; pass++)
            {
                int step = pass == 0 ? 3 : 1;
                for (int r = 0; r < RegionMap.Count && have.Count < wanted; r += step)
                {
                    if (have.Contains(r)) continue;
                    var seed = RegionMap.SeedOf(r);
                    if (!TrySpawnNear(em, seed.x, seed.y)) continue;
                    have.Add(r);
                }
            }
        }

        /// <summary>Place a deposit on standable ground near a region seed.</summary>
        private static bool TrySpawnNear(EntityManager em, float x, float z)
        {
            for (float ring = 0f; ring <= 48f; ring += 8f)
            {
                int samples = ring <= 0.01f ? 1 : 8;
                for (int i = 0; i < samples; i++)
                {
                    float a = i * (Mathf.PI * 2f / samples);
                    float px = x + Mathf.Cos(a) * ring;
                    float pz = z + Mathf.Sin(a) * ring;
                    // Same passability idiom the midpoint fallback uses, so
                    // both paths agree about what counts as standable ground.
                    var grid = PassabilityGrid.Instance;
                    if (grid != null)
                    {
                        var cell = grid.WorldToCell(new float3(px, 0f, pz));
                        if (cell.x < 0 || cell.x >= grid.Width
                            || cell.y < 0 || cell.y >= grid.Height) continue;
                        if (grid.GetCell(cell) != PassabilityGrid.Passable) continue;
                    }
                    float y = TerrainUtility.GetHeight(px, pz);
                    CreateVeilsteelDepositEntity(em, new float3(px, y, pz), DefaultAmount);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Payload when no marker authored one — owned by the factory.</summary>
        private const int DefaultAmount = VeilsteelDeposit.DefaultAmount;
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

        /// <summary>Entity creation lives in the co-located
        /// <see cref="VeilsteelDeposit"/> factory (TechTree convention); the
        /// bootstrap only decides WHERE nodes go.</summary>
        private static Entity CreateVeilsteelDepositEntity(EntityManager em, float3 position, int amount)
            => VeilsteelDeposit.Create(em, position, amount);
    }
}
