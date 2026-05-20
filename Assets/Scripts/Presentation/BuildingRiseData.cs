// BuildingRiseData.cs
// Stores per-piece original local positions for sequential construction rise.
// Pieces are ordered by ascending world Y at Init time and each gets its own
// non-overlapping slot in the [0..1] progress range — only one piece moves
// at a time, lowest first, top piece last.
// Location: Assets/Scripts/Presentation/BuildingRiseData.cs

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public class BuildingRiseData : MonoBehaviour
    {
        private Transform[] _pieces;
        private float[] _restLocalY;
        private float[] _slotStart;       // when this piece begins its rise (in [0..1])
        private float _slotWidth;         // duration of each piece's slot
        private bool _initialized;
        private bool _underConstructionLastFrame;

        public void Init()
        {
            if (_initialized) return;
            _initialized = true;

            var collected = new List<Transform>();
            for (int i = 0; i < transform.childCount; i++)
                CollectPieces(transform.GetChild(i), collected);

            int n = collected.Count;
            _pieces = new Transform[n];
            _restLocalY = new float[n];
            _slotStart = new float[n];
            if (n == 0) return;

            var worldYs = new float[n];
            for (int i = 0; i < n; i++)
            {
                _pieces[i] = collected[i];
                _restLocalY[i] = collected[i].localPosition.y;
                worldYs[i] = collected[i].position.y;
            }

            // Rank pieces by ascending world Y so the lowest physical piece
            // rises first and the highest finishes last. Each rank gets a
            // non-overlapping slot of width 1/n in the construction progress.
            var rankByPiece = new int[n];
            var indices = new int[n];
            for (int i = 0; i < n; i++) indices[i] = i;
            System.Array.Sort(indices, (a, b) => worldYs[a].CompareTo(worldYs[b]));
            for (int rank = 0; rank < n; rank++)
                rankByPiece[indices[rank]] = rank;

            _slotWidth = 1f / n;
            for (int i = 0; i < n; i++)
                _slotStart[i] = rankByPiece[i] * _slotWidth;
        }

        // Renderer-bearing transforms are pieces; pure grouping transforms
        // (no Renderer, has children) recurse so FBX-wrapped prefabs animate
        // per-mesh instead of as one rigid block.
        private static void CollectPieces(Transform t, List<Transform> output)
        {
            bool hasOwnRenderer = t.GetComponent<Renderer>() != null;
            if (hasOwnRenderer || t.childCount == 0)
            {
                output.Add(t);
                return;
            }
            for (int i = 0; i < t.childCount; i++)
                CollectPieces(t.GetChild(i), output);
        }

        /// <summary>Position each piece based on overall construction progress (0..1). One piece rises at a time.</summary>
        public void ApplyRise(float ratio, float sinkDepth)
        {
            if (!_initialized || _pieces == null || _slotWidth <= 0f) return;

            for (int i = 0; i < _pieces.Length; i++)
            {
                var c = _pieces[i];
                if (c == null) continue;

                float pieceProgress = Mathf.Clamp01((ratio - _slotStart[i]) / _slotWidth);
                float eased = 1f - (1f - pieceProgress) * (1f - pieceProgress);

                var lp = c.localPosition;
                lp.y = _restLocalY[i] - sinkDepth * (1f - eased);
                c.localPosition = lp;
            }
            _underConstructionLastFrame = true;
        }

        /// <summary>
        /// Snap pieces back to rest the first frame after construction ends.
        /// Returns true exactly on that transition frame (so callers can fire
        /// a one-shot completion effect), false otherwise.
        /// </summary>
        public bool NotifyConstructionComplete()
        {
            if (!_underConstructionLastFrame) return false;
            _underConstructionLastFrame = false;
            if (!_initialized || _pieces == null) return true;
            for (int i = 0; i < _pieces.Length; i++)
            {
                if (_pieces[i] == null) continue;
                var lp = _pieces[i].localPosition;
                lp.y = _restLocalY[i];
                _pieces[i].localPosition = lp;
            }
            return true;
        }
    }
}
