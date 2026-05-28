// MapMarker.cs
// Base class for design-time spawn markers placed in the Game scene.
// Bootstraps (PlayerSpawnSystem, IronDepositBootstrap, CrystalPatchBootstrap,
// CrystalNodeBootstrap) check MapMarkerRegistry on load — if any markers
// of a given type exist they replace the procedural placement for that
// category. Otherwise the procedural path runs unchanged.
//
// The gizmo snaps to current terrain height in the Scene view so the
// marker visually conforms to your MapMagic / Unity Terrain heightmap
// even when authored at Y=0.

using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.World.MapMarkers
{
    public abstract class MapMarker : MonoBehaviour
    {
        [Tooltip("Snap the marker's Y to terrain height in the Scene view. " +
                 "Keeps the gizmo on the surface as you drag the marker around.")]
        public bool SnapToTerrain = true;

        protected abstract Color GizmoColor { get; }

        /// <summary>Radius of the outer wire sphere — should hint at the
        /// real spawn footprint (Hall ring, patch spread, curse radius).</summary>
        protected abstract float GizmoRadius { get; }

        /// <summary>Label drawn above the marker in the Scene view.</summary>
        protected virtual string GizmoLabel => GetType().Name;

        /// <summary>World-space position bootstraps should spawn at.</summary>
        public Vector3 WorldPosition => transform.position;

        protected virtual void OnDrawGizmos()
        {
            if (!Application.isPlaying && SnapToTerrain)
                TrySnapToTerrain();

            var c = GizmoColor;
            Gizmos.color = new Color(c.r, c.g, c.b, 0.35f);
            Gizmos.DrawSphere(transform.position, Mathf.Max(0.4f, GizmoRadius * 0.18f));
            Gizmos.color = c;
            Gizmos.DrawWireSphere(transform.position, GizmoRadius);
        }

        protected virtual void OnDrawGizmosSelected()
        {
#if UNITY_EDITOR
            UnityEditor.Handles.color = GizmoColor;
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * (GizmoRadius + 1.5f),
                GizmoLabel);
            // Draw a brighter inner ring when selected so the active marker
            // pops against the field of unselected markers.
            UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up,
                GizmoRadius * 0.6f);
#endif
        }

        private void TrySnapToTerrain()
        {
            // TerrainUtility.GetHeight is safe in edit mode — it samples
            // Unity Terrain via Terrain.activeTerrain and returns the
            // marker's own Y when no terrain is active, which makes this
            // a no-op outside of game scenes.
            var p = transform.position;
            float y;
            try { y = TerrainUtility.GetHeight(p.x, p.z); }
            catch { return; }
            if (Mathf.Abs(p.y - y) > 0.001f)
            {
                p.y = y;
                transform.position = p;
            }
        }
    }
}
