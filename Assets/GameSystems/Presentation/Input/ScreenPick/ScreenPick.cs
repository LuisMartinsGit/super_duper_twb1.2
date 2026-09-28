using UnityEngine;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.Input
{
    /// <summary>
    /// Screen point -> entity under the cursor.
    ///
    /// One implementation. RTSInputManager and SelectionSystem each carried
    /// their own copy of this walk, identical but for variable names.
    /// </summary>
    public static class ScreenPick
    {
        static ScreenPickConfig _cfg;
        static ScreenPickConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<ScreenPickConfig>());

        /// <summary>
        /// The nearest entity under the mouse, or Entity.Null.
        ///
        /// RaycastAll + nearest-valid rather than Raycast: a stray collider that
        /// does NOT resolve to an entity (loot piles, decor, dead visuals) must
        /// not swallow a click aimed at the unit behind it. Per hit the whole
        /// hierarchy is walked upward, because a prefab may carry its collider
        /// on a deep child.
        /// </summary>
        public static Entity EntityUnderMouse(LayerMask mask, EntityManager em)
        {
            var cam = TheWaningBorder.Core.PresentationState.GameplayCamera;
            if (cam == null) return Entity.Null;

            var cfg = Cfg;
            if (cfg == null) return Entity.Null;   // the error is already logged by Require

            var hits = Physics.RaycastAll(
                cam.ScreenPointToRay(UnityEngine.Input.mousePosition), cfg.rayLength, mask);

            // Nearest entity overall, and nearest BUILDING, tracked apart.
            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            Entity bestBuilding = Entity.Null;
            float bestBuildingDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                float d = hits[i].distance;
                if (d >= bestDist && d >= bestBuildingDist) continue;

                for (var t = hits[i].collider.transform; t != null; t = t.parent)
                {
                    var link = t.GetComponent<EntityReference>();
                    if (link != null && em.Exists(link.Entity))
                    {
                        if (d < bestDist) { best = link.Entity; bestDist = d; }
                        if (d < bestBuildingDist && em.HasComponent<BuildingTag>(link.Entity))
                        {
                            bestBuilding = link.Entity;
                            bestBuildingDist = d;
                        }
                        break;
                    }
                }
            }

            // A RESOURCE NODE YIELDS TO THE BUILDING STANDING ON IT
            // (2026-09-26). An extractor (Mine, Veilstone Mine, Smelter)
            // sits on its node, and the node keeps its 2 m
            // cell box (PresentationSpawnSystem.FitCellBoxCollider). The
            // building's fitted box is floored at 2 m tall but centred on the
            // art, so on a low model the node's box pokes out of the
            // building's top and the ray met the NODE first: right-clicking
            // an enemy Mine picked the iron deposit and became a move order.
            // A node only wins when no building face is within the yield of
            // it along the ray.
            if (best != Entity.Null && bestBuilding != Entity.Null && best != bestBuilding
                && ResourceNodeQuery.IsGatherable(em, best)
                && bestBuildingDist - bestDist <= cfg.resourceNodeYield)
                return bestBuilding;

            return best;
        }
    }
}
