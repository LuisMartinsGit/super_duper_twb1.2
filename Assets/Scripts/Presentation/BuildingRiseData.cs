// BuildingRiseData.cs
// Stores per-child original local positions for staggered construction rise.
// Attached to building GameObjects during construction by PresentationSpawnSystem.
// Location: Assets/Scripts/Presentation/BuildingRiseData.cs

using UnityEngine;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// Tracks each child's original local Y so the construction animation
    /// can raise pieces bottom-to-top. Lower pieces appear first.
    /// </summary>
    public class BuildingRiseData : MonoBehaviour
    {
        private Transform[] _children;
        private float[] _originalLocalY;
        private float _maxChildY;
        private bool _initialized;

        // Spread factor: how much of the progress range each piece uses to transition
        private const float TransitionWidth = 0.25f;

        /// <summary>
        /// Snapshot each direct child's local Y position.
        /// Must be called once before ApplyRise.
        /// </summary>
        public void Init()
        {
            if (_initialized) return;
            _initialized = true;

            int count = transform.childCount;
            _children = new Transform[count];
            _originalLocalY = new float[count];
            _maxChildY = 0.01f; // avoid division by zero

            for (int i = 0; i < count; i++)
            {
                _children[i] = transform.GetChild(i);
                _originalLocalY[i] = _children[i].localPosition.y;
                if (_originalLocalY[i] > _maxChildY)
                    _maxChildY = _originalLocalY[i];
            }
        }

        /// <summary>
        /// Position each child based on overall construction progress (0..1).
        /// Lower children rise into place earlier, upper children later.
        /// </summary>
        /// <param name="ratio">Overall construction progress 0..1</param>
        /// <param name="sinkDepth">How far below ground pieces start</param>
        public void ApplyRise(float ratio, float sinkDepth)
        {
            if (!_initialized || _children == null) return;

            for (int i = 0; i < _children.Length; i++)
            {
                if (_children[i] == null) continue;

                // Normalize this child's height to 0..1 range
                float normalizedHeight = _originalLocalY[i] / _maxChildY;

                // Threshold: when this piece starts rising (lower pieces start earlier)
                float threshold = normalizedHeight * (1f - TransitionWidth);

                // Per-child progress: 0 = fully sunk, 1 = in place
                float childProgress = Mathf.Clamp01((ratio - threshold) / TransitionWidth);

                // Ease-out for a satisfying "settling into place" feel
                float eased = 1f - (1f - childProgress) * (1f - childProgress);

                // Apply Y offset: at childProgress=0, piece is sinkDepth below original
                var localPos = _children[i].localPosition;
                localPos.y = _originalLocalY[i] - sinkDepth * (1f - eased);
                _children[i].localPosition = localPos;
            }
        }

        /// <summary>
        /// Reset all children to their original positions (called on construction complete).
        /// </summary>
        public void ResetToOriginal()
        {
            if (!_initialized || _children == null) return;

            for (int i = 0; i < _children.Length; i++)
            {
                if (_children[i] == null) continue;
                var localPos = _children[i].localPosition;
                localPos.y = _originalLocalY[i];
                _children[i].localPosition = localPos;
            }
        }
    }
}
