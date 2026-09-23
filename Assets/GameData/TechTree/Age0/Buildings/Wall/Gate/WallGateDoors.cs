// WallGateDoors.cs
// The gate's moving part (docs/Design/Age_1_Alanthor.md § The gate is one
// structure, three modules wide): the leaves swing inward when the gate opens
// and shut again when it closes. Nothing teleports — the leaves are always
// drawn, and the swing is what tells the player whether their gate is open,
// closed or sealed.
//
// It drives PIVOTS, not the leaves themselves. A door mesh exported from
// Blender has its origin wherever the artist left it — usually the object's
// centre — and rotating that spins the leaf like a turnstile. So each leaf is
// re-parented under a pivot placed on its own hinge EDGE, measured from its
// renderer bounds, and the pivot is what turns.
//
// Which edge, and which way it swings, come from the leaf's POSITION, not its
// name: a leaf sitting on the -Z side of the gate hinges on its -Z end, one on
// the +Z side hinges on its +Z end, and both swing toward -X (the friendly
// face) so an opening gate never sweeps its doors through the besiegers.
// Naming only has to say "this is a door".
//
// Reads WallGateState.IsOpen, which GateStateSystem owns (proximity, or the
// player's seal). Presentation only: never writes the sim.

using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public sealed class WallGateDoors : MonoBehaviour
    {
        /// <summary>The gate entity whose open state drives the leaves.</summary>
        public Entity Gate;

        /// <summary>Seconds for a full swing.</summary>
        public const float SwingSeconds = 1.2f;
        /// <summary>How far open a leaf swings, degrees.</summary>
        public const float OpenAngle = 88f;

        /// <summary>A leaf: the pivot that turns, and which way it turns.</summary>
        private readonly List<(Transform Pivot, float Sign)> _leaves
            = new List<(Transform, float)>();

        const float PollSeconds = 0.2f;
        float _nextPoll;
        float _open;            // 0 shut .. 1 open
        float _target;
        EntityManager _em;
        bool _haveEm;

        // ── Binding ───────────────────────────────────────────────────────

        /// <summary>
        /// Find the door leaves in authored gate art and hinge them. A child
        /// whose name says "door" is a leaf; everything else is the gatehouse.
        /// Returns how many were found, so the caller can tell a gate with no
        /// moving parts from one that failed to bind.
        /// </summary>
        public int BindDoors(Transform root)
        {
            if (root == null) return 0;

            var leaves = new List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                if (t.name.IndexOf("door", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                // A door's own children ride with it; only the top of each
                // leaf gets a pivot.
                bool nested = false;
                foreach (var already in leaves)
                    if (t.IsChildOf(already)) { nested = true; break; }
                if (!nested) leaves.Add(t);
            }
            if (leaves.Count == 0) return 0;

            // The gate's centre line along the wall, to tell one side from the
            // other. Measured from the leaves themselves so it does not depend
            // on where the model's origin happens to sit.
            float centreZ = 0f;
            foreach (var leaf in leaves) centreZ += root.InverseTransformPoint(LeafCentre(leaf)).z;
            centreZ /= leaves.Count;

            foreach (var leaf in leaves)
            {
                float localZ = root.InverseTransformPoint(LeafCentre(leaf)).z;
                // A leaf exactly on the centre line (a single-leaf gate) hinges
                // on its -Z end by convention.
                float side = localZ < centreZ ? -1f : (localZ > centreZ ? 1f : -1f);
                var pivot = MakePivot(leaf, root, side);
                if (pivot != null) _leaves.Add((pivot, side));
            }
            return _leaves.Count;
        }

        /// <summary>Hand a leaf that is already hinged — the procedural
        /// gatehouse builds its own pivots.</summary>
        public void AddLeaf(Transform pivot, float sign)
        {
            if (pivot != null) _leaves.Add((pivot, sign));
        }

        /// <summary>
        /// Put a pivot on the leaf's hinge edge — its extreme along the wall
        /// run on <paramref name="side"/> — and re-parent the leaf under it.
        /// The leaf does not move: only its centre of rotation changes.
        /// </summary>
        static Transform MakePivot(Transform leaf, Transform root, float side)
        {
            if (!TryLeafBounds(leaf, out var bounds)) return null;

            // The hinge edge in the ROOT's frame, then back to world.
            var localCentre = root.InverseTransformPoint(bounds.center);
            var localExtent = root.InverseTransformVector(bounds.extents);
            float hingeZ = localCentre.z + side * Mathf.Abs(localExtent.z);
            var hingeWorld = root.TransformPoint(new Vector3(localCentre.x, localCentre.y, hingeZ));

            var pivot = new GameObject(leaf.name + "_Hinge").transform;
            pivot.SetParent(leaf.parent, worldPositionStays: false);
            pivot.position = hingeWorld;
            pivot.rotation = root.rotation;
            pivot.localScale = Vector3.one;
            leaf.SetParent(pivot, worldPositionStays: true);
            return pivot;
        }

        static Vector3 LeafCentre(Transform leaf)
            => TryLeafBounds(leaf, out var b) ? b.center : leaf.position;

        static bool TryLeafBounds(Transform leaf, out Bounds bounds)
        {
            bounds = default;
            var renderers = leaf.GetComponentsInChildren<Renderer>(true);
            bool any = false;
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        // ── Swing ─────────────────────────────────────────────────────────

        void LateUpdate()
        {
            if (_leaves.Count == 0) return;

            if (Time.time >= _nextPoll)
            {
                _nextPoll = Time.time + PollSeconds;
                _target = ReadOpenState() ? 1f : 0f;
            }

            if (!Mathf.Approximately(_open, _target))
            {
                _open = Mathf.MoveTowards(_open, _target, Time.deltaTime / SwingSeconds);
                Apply();
            }
        }

        bool ReadOpenState()
        {
            if (!_haveEm)
            {
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) return false;
                _em = world.EntityManager;
                _haveEm = true;
            }
            if (Gate == Entity.Null || !_em.Exists(Gate)) return false;
            // A sealed gate is shut whatever the proximity poll last wrote.
            if (_em.HasComponent<WallGateLock>(Gate)
                && _em.GetComponentData<WallGateLock>(Gate).Sealed != 0) return false;
            return _em.HasComponent<WallGateState>(Gate)
                && _em.GetComponentData<WallGateState>(Gate).IsOpen != 0;
        }

        void Apply()
        {
            float a = _open * OpenAngle;
            for (int i = 0; i < _leaves.Count; i++)
            {
                var (pivot, sign) = _leaves[i];
                if (pivot == null) continue;
                // Sign follows the side the leaf sits on, so both leaves swing
                // toward -X whichever end they hinge from.
                pivot.localRotation = Quaternion.Euler(0f, sign * a, 0f);
            }
        }

        /// <summary>Snap to the current state without a swing — used right
        /// after the visual is built, so a standing open gate is not drawn
        /// shut for a second.</summary>
        public void SnapToState()
        {
            _target = ReadOpenState() ? 1f : 0f;
            _open = _target;
            Apply();
        }
    }
}
