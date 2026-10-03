// ArrowSpawnHide.cs
// A pooled arrow is teleported back to the bow when it is re-used, and its
// trail and tip particles still remember where it last flew — the first frame
// draws a streak from the old impact point back to the shooter. This hides
// the trail and every particle renderer on the arrow for that one frame, then
// clears them at the new position and turns them back on. Added to an arrow
// by ArrowTrailTiers.HideForOneFrame on every spawn. Presentation only.

using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class ArrowSpawnHide : MonoBehaviour
    {
        private int _hiddenFrame = -1;
        private TrailRenderer _trail;
        private bool _trailWanted;
        private readonly System.Collections.Generic.List<TrailRenderer> _tipTrails = new();

        /// <summary>Hide now; show again from the next frame.</summary>
        public void Begin(TrailRenderer trail)
        {
            _trail = trail;
            _trailWanted = trail != null && trail.enabled;
            if (_trail != null) { _trail.emitting = false; _trail.Clear(); }
            // The tip effect's own trails (trail glow / trail shadow).
            _tipTrails.Clear();
            foreach (var t in GetComponentsInChildren<TrailRenderer>(true))
            {
                if (t == _trail) continue;
                t.emitting = false;
                t.Clear();
                _tipTrails.Add(t);
            }
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                if (ps.TryGetComponent<ParticleSystemRenderer>(out var r)) r.enabled = false;
            }
            _hiddenFrame = Time.frameCount;
            enabled = true;
        }

        void LateUpdate()
        {
            if (Time.frameCount <= _hiddenFrame) return;
            if (_trail != null && _trailWanted) { _trail.Clear(); _trail.emitting = true; }
            foreach (var t in _tipTrails)
                if (t != null) { t.Clear(); t.emitting = true; }
            foreach (var ps in GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                if (ps.TryGetComponent<ParticleSystemRenderer>(out var r)) r.enabled = true;
                ps.Play(true);
            }
            enabled = false;
        }
    }
}
