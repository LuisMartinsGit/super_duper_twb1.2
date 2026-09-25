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

            Entity best = Entity.Null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].distance >= bestDist) continue;

                for (var t = hits[i].collider.transform; t != null; t = t.parent)
                {
                    var link = t.GetComponent<EntityReference>();
                    if (link != null && em.Exists(link.Entity))
                    {
                        best = link.Entity;
                        bestDist = hits[i].distance;
                        break;
                    }
                }
            }
            return best;
        }
    }
}
