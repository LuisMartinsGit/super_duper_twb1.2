// Walkable bridge/overpass surface (directive 2026-07-05: units must cross
// NoWalk terrain over bridge objects).
//
// Attach to the ROOT of a bridge prefab/scene object. On Awake it snapshots
// every child MeshFilter as a "piece" (its mesh treated as an oriented box
// via mesh.bounds — exact for the scaled/rotated cube primitives bridges
// are built from). Two services drive the rest of the stack:
//
//   * TryGetDeckHeight(x, z, out y): exact top-surface height of the
//     highest piece under the vertical line at (x, z). TerrainUtility
//     .GetHeight consults this and returns the deck height when it is
//     above the terrain — units, slope probes and visuals all stand ON
//     the bridge automatically.
//   * OverlapsCell(x, z, halfExtent): true when any piece's deck covers
//     the cell area (5-point sample). PassabilityGrid forces such cells
//     PASSABLE, overriding NoWalk paint / slope / water — the bridge IS
//     the crossing.
//
// Bridges are STATIC scene furniture: piece matrices are cached once at
// Awake (registered before PassabilityGrid's deferred build), and all
// queries are pure cached math — deterministic for lockstep. Moving or
// spawning bridges at runtime is NOT supported (the passability grid and
// cost-field bake would not refresh).

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.World.Terrain
{
    [DisallowMultipleComponent]
    public class BridgeSurface : MonoBehaviour
    {
        private struct Piece
        {
            public Matrix4x4 WorldToLocal;
            public Vector3 BoundsCenter;   // mesh-local bounds
            public Vector3 BoundsExtents;
        }

        private static readonly List<BridgeSurface> _all = new List<BridgeSurface>();

        /// <summary>Fast global gate so TerrainUtility pays one branch when no
        /// bridges exist on the map.</summary>
        public static bool HasAny => _all.Count > 0;

        private readonly List<Piece> _pieces = new List<Piece>();
        private Vector2 _aabbMin;   // world XZ bounds for quick reject
        private Vector2 _aabbMax;

        // Small tolerance so a query exactly on the deck edge still hits.
        private const float EdgeEpsilon = 0.02f;

        /// <summary>
        /// Max height difference a unit can physically step UP onto a deck.
        /// Shared by TerrainUtility.GetSurfaceHeight and
        /// UnitIntegratorSystem's deck admission so the two never disagree.
        /// </summary>
        public const float MountStepLimit = 1.25f;


        void Awake()
        {
            _pieces.Clear();
            var filters = GetComponentsInChildren<MeshFilter>();
            bool first = true;
            foreach (var mf in filters)
            {
                if (mf == null || mf.sharedMesh == null) continue;
                var b = mf.sharedMesh.bounds;
                _pieces.Add(new Piece
                {
                    WorldToLocal = mf.transform.worldToLocalMatrix,
                    BoundsCenter = b.center,
                    BoundsExtents = b.extents,
                });

                // Accumulate the world-space XZ AABB from the 8 box corners.
                var l2w = mf.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3(
                        (c & 1) == 0 ? -1f : 1f,
                        (c & 2) == 0 ? -1f : 1f,
                        (c & 4) == 0 ? -1f : 1f));
                    var w = l2w.MultiplyPoint3x4(corner);
                    if (first)
                    {
                        _aabbMin = new Vector2(w.x, w.z);
                        _aabbMax = _aabbMin;
                        first = false;
                    }
                    else
                    {
                        _aabbMin = Vector2.Min(_aabbMin, new Vector2(w.x, w.z));
                        _aabbMax = Vector2.Max(_aabbMax, new Vector2(w.x, w.z));
                    }
                }
            }

            if (_pieces.Count > 0)
                _all.Add(this);
            else
                Debug.LogWarning($"[BridgeSurface] '{name}' has no MeshFilter pieces — nothing to walk on.");
        }

        void OnDestroy()
        {
            _all.Remove(this);
        }

        // ──────────────────────────────────────────────────────────────────
        // STATIC QUERIES
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// World Y of the highest bridge deck under the vertical line at
        /// (x, z). False when no bridge covers the point.
        /// </summary>
        public static bool TryGetDeckHeight(float x, float z, out float y)
        {
            y = float.MinValue;
            bool hit = false;
            for (int i = 0; i < _all.Count; i++)
            {
                var b = _all[i];
                if (x < b._aabbMin.x || x > b._aabbMax.x
                    || z < b._aabbMin.y || z > b._aabbMax.y) continue;

                if (b.SampleDeck(x, z, out float deckY) && deckY > y)
                {
                    y = deckY;
                    hit = true;
                }
            }
            return hit;
        }

        /// <summary>
        /// True when a bridge deck covers any of a 5-point sample of the cell
        /// square at (x, z) with the given half extent. PassabilityGrid uses
        /// this so a deck narrower than one nav cell still opens the crossing.
        /// </summary>
        public static bool OverlapsCell(float x, float z, float halfExtent)
        {
            if (_all.Count == 0) return false;
            if (TryGetDeckHeight(x, z, out _)) return true;
            float o = halfExtent * 0.7f;
            if (TryGetDeckHeight(x - o, z, out _)) return true;
            if (TryGetDeckHeight(x + o, z, out _)) return true;
            if (TryGetDeckHeight(x, z - o, out _)) return true;
            if (TryGetDeckHeight(x, z + o, out _)) return true;
            return false;
        }

        // ──────────────────────────────────────────────────────────────────
        // PER-BRIDGE SAMPLING
        // ──────────────────────────────────────────────────────────────────

        // Intersect the vertical world line at (x, z) with each piece's TOP
        // face (local plane y = bounds.max.y). The local coordinates of the
        // line are affine in world-Y, so the intersection solves in closed
        // form: local(t) = base + t * yCol, with t = world Y.
        private bool SampleDeck(float x, float z, out float deckY)
        {
            deckY = float.MinValue;
            bool hit = false;

            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                var m = p.WorldToLocal;

                Vector3 basePt = m.MultiplyPoint3x4(new Vector3(x, 0f, z));
                var yCol = new Vector3(m.m01, m.m11, m.m21);

                if (Mathf.Abs(yCol.y) < 1e-6f) continue; // degenerate (vertical deck)

                float topLocalY = p.BoundsCenter.y + p.BoundsExtents.y;
                float t = (topLocalY - basePt.y) / yCol.y;   // world Y of the top face

                float lx = basePt.x + t * yCol.x;
                float lz = basePt.z + t * yCol.z;
                if (Mathf.Abs(lx - p.BoundsCenter.x) > p.BoundsExtents.x + EdgeEpsilon) continue;
                if (Mathf.Abs(lz - p.BoundsCenter.z) > p.BoundsExtents.z + EdgeEpsilon) continue;

                if (t > deckY)
                {
                    deckY = t;
                    hit = true;
                }
            }
            return hit;
        }
    }
}
