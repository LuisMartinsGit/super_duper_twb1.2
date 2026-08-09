// File: Assets/Scripts/Bootstrap/BlightPocketBootstrap.cs
// Spawns the Age 0 blight pockets (§2.5b): near-spawn patches of established
// veil haze, each anchored by a destructible Sporeling. Authored via
// BlightPocketMarker; deterministic fallback places one pocket per Hall at a
// seeded bearing. The pockets are the early game's curse content — wells sit
// far from spawns, so without them Age 0 would have nothing to secure
// against and no veilstone source (all veilstone precipitates from the
// curse).
//
// Registration: pockets land in a BlightPocket buffer on a singleton entity;
// BlightPocketSystem seeds the haze discs once the VeilField exists, starves
// suppressed sporelings, and fires each pocket's collapse exactly once.

using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Entities;
using TheWaningBorder.World.MapMarkers;
using TheWaningBorder.World.Terrain;
using static TheWaningBorder.Core.Config.VeilCrustConstants;

namespace TheWaningBorder.Bootstrap
{
    public static class BlightPocketBootstrap
    {
        public static void SpawnBlightPockets()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            // The registry singleton is ALWAYS created — it is both the pocket
            // store and the "curse stack active" signal the mining-corruption
            // roll checks (VeilstoneMiningSystem registers corrupted nodes
            // here at runtime).
            var registry = em.CreateEntity();
            em.AddBuffer<BlightPocket>(registry);
            em.AddBuffer<PendingCorruption>(registry); // corruption telegraphs

            // rev.3: no procedural near-spawn pockets (playtest: the starting
            // army deleted them in seconds). The Age 0 curse arrives through
            // MINING CORRUPTION instead; only authored markers still place
            // start-of-match pockets.
            if (MapMarkerRegistry.HasBlightMarkers)
            {
                var markers = MapMarkerRegistry.BlightPockets;
                for (int i = 0; i < markers.Count; i++)
                {
                    var m = markers[i];
                    if (m == null) continue;
                    var p = m.WorldPosition;
                    AddPocket(em, registry, new float2(p.x, p.z), m.Radius);
                }
                TWBLog.Log($"[BlightPocketBootstrap] {markers.Count} authored pockets.");
            }
            else
            {
                TWBLog.Log("[BlightPocketBootstrap] registry ready — pockets arrive via mining corruption.");
            }
        }

        /// <summary>Spawn the anchor sporeling and register the pocket.
        /// Returns a fresh buffer handle — the sporeling spawn is a structural
        /// change that invalidates the previous one.</summary>
        private static DynamicBuffer<BlightPocket> AddPocket(
            EntityManager em, Entity registry, float2 center, float radius)
        {
            float y = TerrainUtility.GetHeight(center.x, center.y);
            var sporeling = Sporeling.Create(em, new float3(center.x, y, center.y));
            var buffer = em.GetBuffer<BlightPocket>(registry);
            buffer.Add(new BlightPocket
            {
                Sporeling = sporeling,
                Center = center,
                Radius = radius,
                Seeded = 0,
                Collapsed = 0,
            });
            return buffer;
        }
    }
}
