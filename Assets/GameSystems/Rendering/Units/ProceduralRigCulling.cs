// ProceduralRigCulling.cs
// One frustum test for every hand-animated procedural unit rig (2026-09-25).

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    /// <summary>
    /// Is a procedural rig worth animating this frame?
    ///
    /// The procedural unit visuals (Spearman, Archer, Swordsman, the siege
    /// engines, the sect units …) each pose six or more pivots in their own
    /// LateUpdate, every frame, whether or not anyone can see them. Late game
    /// that is most of the army standing off-screen, paying transform writes
    /// that the renderer then culls anyway. They now ask this first.
    ///
    /// The gameplay camera's frustum planes are computed ONCE per frame and
    /// shared; the test is a padded bounding sphere, so a unit whose root is
    /// just outside the edge but whose spear or banner pokes in keeps
    /// animating. A culled rig simply holds its last pose — nothing it drives
    /// is read by the simulation or by any other system.
    /// </summary>
    public static class ProceduralRigCulling
    {
        /// <summary>Sphere radius around the rig root: covers the largest
        /// procedural unit (the trebuchet arm) so it never pops at the edge.</summary>
        private const float PadRadius = 6f;

        private static readonly Plane[] _planes = new Plane[6];
        private static int _frame = -1;
        private static bool _haveCamera;

        public static bool Visible(Vector3 position)
        {
            int frame = Time.frameCount;
            if (frame != _frame)
            {
                _frame = frame;
                var cam = TheWaningBorder.Core.PresentationState.GameplayCamera;
                _haveCamera = cam != null;
                if (_haveCamera) GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            }
            if (!_haveCamera) return true;   // no camera to cull against: animate

            for (int i = 0; i < 6; i++)
                if (_planes[i].GetDistanceToPoint(position) < -PadRadius) return false;
            return true;
        }
    }
}
