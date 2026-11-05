using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Systems.Combat
{
    /// <summary>
    /// Where a building's shot leaves from (docs/Design/Combat_Pacing.md § Building
    /// volleys): the fire point nearest the target, from the SO that gives the building
    /// its model, so a Fortress shoots from the tower closest to what it aims at.
    /// Simulation data, read the same on every lockstep peer.
    /// </summary>
    public static class BuildingFirePoints
    {
        public static float3 MuzzleFor(EntityManager em, Entity building, Faction faction, float3 target)
        {
            var transform = em.GetComponentData<LocalTransform>(building);
            var points = Of(em, building, faction);
            if (points == null || points.Length == 0)
                return transform.Position + new float3(0f, FallbackHeight(em, building), 0f);

            float3 best = transform.Position;
            float bestSq = float.MaxValue;
            for (int i = 0; i < points.Length; i++)
            {
                float3 world = transform.Position + math.mul(transform.Rotation, (float3)points[i]);
                float sq = math.distancesq(world.xz, target.xz);
                if (sq < bestSq) { bestSq = sq; best = world; }
            }
            // The point is the tower's axis, inside its roof: leave from the gallery
            // edge that faces the target.
            float2 toTarget = math.normalizesafe(target.xz - best.xz);
            float outset = BuildingCombatSystemConfig.I.firePointOutset;
            return best + new float3(toTarget.x, 0f, toTarget.y) * outset;
        }

        private static Vector3[] Of(EntityManager em, Entity building, Faction faction)
        {
            string id = TheWaningBorder.Entities.BuildingIds.Of(building, em);
            if (string.IsNullOrEmpty(id)) return null;
            string ladder = TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.ResolveBuildingId(em, building);
            byte culture = CultureConfig.GetCompletedCulture(em, faction);
            string key = string.IsNullOrEmpty(ladder) ? id : ladder;
            int level = culture != Cultures.None && TechCatalog.HasBuildingLevels(culture, key)
                ? math.max(1, (int)TheWaningBorder.Systems.Buildings.FactionBuildingLevels.Get(em, faction, key))
                : 0;
            return TechCatalog.BuildingFirePoints(id, key, culture, level);
        }

        /// <summary>A wall piece's art has no level SO; anything else with no points
        /// (a curse node) fires from just above its own radius.</summary>
        private static float FallbackHeight(EntityManager em, Entity building)
        {
            if (em.HasComponent<WallTowerTag>(building) || em.HasComponent<WallHubTag>(building))
                return BuildingCombatSystemConfig.I.wallFireHeight;
            return em.HasComponent<Radius>(building) ? em.GetComponentData<Radius>(building).Value + 0.5f : 1.5f;
        }
    }
}
