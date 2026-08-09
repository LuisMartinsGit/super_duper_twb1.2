// BuildingRiseData.cs
// Stores per-piece original local positions for sequential construction rise.
// Piece ORDER: when the model's parts are labeled 1..N (a number anywhere in
// the transform name — "1", "Part_2", "03_roof"; a labeled GROUP passes its
// number down to its meshes), parts rise in ascending numerical order and
// parts SHARING a number rise TOGETHER in one shared slot (one construction
// step). Pieces without any number (the spawn system parents helpers like
// the faction banner pole/flag onto the visual before this snapshot) form a
// final step after the numbered ones — they must NOT disable numeric mode.
// Only when NO piece has a number does the visual use the legacy order:
// per-piece slots by ascending world Y (lowest first, top last).
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

        /// <summary>
        /// True when the snapshot found at least one movable piece. False for
        /// visuals whose only renderer sits on the ROOT (e.g. single-mesh FBX
        /// prefabs — CollectPieces only walks children, and the root can't be
        /// piece-animated because SyncTransforms owns its position). Callers
        /// must fall back to the rigid root sink in that case; calling
        /// ApplyRise would silently do nothing and the building would start
        /// fully above ground.
        /// </summary>
        public bool HasPieces => _initialized && _pieces != null && _pieces.Length > 0;

        public void Init()
        {
            if (_initialized) return;
            _initialized = true;

            var collected = new List<Transform>();
            var labels = new List<int>();
            for (int i = 0; i < transform.childCount; i++)
                CollectPieces(transform.GetChild(i), collected, labels, NoLabel);

            int n = collected.Count;
            _pieces = new Transform[n];
            _restLocalY = new float[n];
            _slotStart = new float[n];
            if (n == 0) return;

            var worldYs = new float[n];
            bool anyLabeled = false;
            for (int i = 0; i < n; i++)
            {
                _pieces[i] = collected[i];
                _restLocalY[i] = collected[i].localPosition.y;
                worldYs[i] = collected[i].position.y;
                if (labels[i] != NoLabel) anyLabeled = true;
            }

            if (anyLabeled)
            {
                // NUMERIC MODE. One slot per DISTINCT label, ascending — so
                // parts sharing a number rise simultaneously as one step.
                // Unlabeled pieces (banner pole/flag and other helpers the
                // spawn system adds to the visual) become the final step
                // instead of knocking the whole model out of numeric mode.
                for (int i = 0; i < n; i++)
                {
                    if (labels[i] == NoLabel) labels[i] = int.MaxValue;
                }

                var distinct = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    if (!distinct.Contains(labels[i])) distinct.Add(labels[i]);
                }
                distinct.Sort();

                _slotWidth = 1f / distinct.Count;
                for (int i = 0; i < n; i++)
                    _slotStart[i] = distinct.IndexOf(labels[i]) * _slotWidth;
                return;
            }

            // LEGACY MODE (no numbers anywhere): rank pieces by ascending
            // world Y so the lowest physical piece rises first and the
            // highest finishes last, each in its own slot of width 1/n.
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

        private const int NoLabel = int.MinValue;

        // Renderer-bearing transforms are pieces; pure grouping transforms
        // (no Renderer, has children) recurse so FBX-wrapped prefabs animate
        // per-mesh instead of as one rigid block. A grouping transform's
        // numeric label is inherited by every piece beneath it (nearest
        // labeled ancestor wins, the piece's own name wins over both).
        private static void CollectPieces(Transform t, List<Transform> output,
            List<int> labels, int inheritedLabel)
        {
            // Inactive branches are not part of the visible model — the
            // multi-variant prefabs keep their culture Lv1-Lv3 models
            // deactivated behind Lv0 during construction, and those must
            // never join the rise (or flash visible).
            if (!t.gameObject.activeSelf) return;

            int label = TryExtractLabel(t.name, out int value) ? value : inheritedLabel;

            bool hasOwnRenderer = t.GetComponent<Renderer>() != null;
            if (hasOwnRenderer || t.childCount == 0)
            {
                output.Add(t);
                labels.Add(label);
                return;
            }
            for (int i = 0; i < t.childCount; i++)
                CollectPieces(t.GetChild(i), output, labels, label);
        }

        /// <summary>First run of digits in the name ("Part_12" -> 12,
        /// "03_roof" -> 3). False when the name contains no digits.</summary>
        private static bool TryExtractLabel(string name, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(name)) return false;

            int i = 0;
            while (i < name.Length && !char.IsDigit(name[i])) i++;
            if (i >= name.Length) return false;

            long parsed = 0;
            while (i < name.Length && char.IsDigit(name[i]))
            {
                parsed = parsed * 10 + (name[i] - '0');
                if (parsed > int.MaxValue) { parsed = int.MaxValue; break; }
                i++;
            }
            value = (int)parsed;
            return true;
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
