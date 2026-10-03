// WallArtMesh.cs
// Bakes the authored wall module (WallModuleArt) along a segment's drawn
// curve into ONE mesh — the art-driven twin of WallCurveMesh, and the path
// taken whenever the curtain module's SO has a prefab bound. Same contract, same call
// site, same result shape: one mesh, one sub-mesh per material, spans whose
// sim cell is gone or has become a gate left open.
//
// One copy of the module per pitch, BENT along the curve (2026-10-02): every
// vertex is placed by its own position along the module — its point on the
// curve, offset across by the curve's own sideways direction there, lifted
// from the ground blended between the module's two seams. Consecutive copies
// therefore share their seam EXACTLY. They used to be rigid copies turned to
// the tangent at their centre: on a bend the outside of each joint opened a
// wedge gap and the inside overlapped, and on a straight run the kit's pieces
// (which reach a little past their module) overlapped their neighbours
// coplanar and z-fought. Anything past the module's ends is clamped onto its
// seam for the same reason. A wall of 11 modules is still one draw call per
// material.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Rendering
{
    public static class WallArtMesh
    {
        // Scratch, reused across rebuilds — a wall rebuild happens on
        // construction ticks and breaches, and this used to be the sort of
        // place that quietly allocated a megabyte a second.
        static readonly List<Vector3> _verts = new List<Vector3>();
        static readonly List<Vector3> _norms = new List<Vector3>();
        static readonly List<Vector2> _uvs = new List<Vector2>();
        static readonly List<List<int>> _subs = new List<List<int>>();
        // A module piece's source arrays, read ONCE per (mesh, sub-mesh) —
        // Append used to pull vertices, normals, UVs and triangles out of the
        // source mesh for every piece of every copy of every rebuild. Module
        // meshes are imported assets and never change at runtime.
        sealed class SourcePiece
        {
            public Vector3[] Verts;
            public Vector3[] Norms;   // null when the source has none
            public Vector2[] Uvs;     // null when the source has none
            public int[] Tris;
        }
        static readonly Dictionary<(Mesh, int), SourcePiece> _sources = new Dictionary<(Mesh, int), SourcePiece>();
        static readonly List<Vector3> _srcVerts = new List<Vector3>();
        static readonly List<Vector3> _srcNorms = new List<Vector3>();
        static readonly List<Vector2> _srcUvs = new List<Vector2>();
        static readonly List<int> _srcTris = new List<int>();
        // Cells by arc length, for the per-copy nearest-cell lookup.
        static readonly List<int> _cellOrder = new List<int>();

        static SourcePiece SourceOf(Mesh mesh, int subMesh)
        {
            if (_sources.TryGetValue((mesh, subMesh), out var sp) && sp != null) return sp;
            if (_sources.Count > 512) _sources.Clear();   // art swapped out wholesale
            mesh.GetVertices(_srcVerts);
            mesh.GetNormals(_srcNorms);
            mesh.GetUVs(0, _srcUvs);
            mesh.GetTriangles(_srcTris, subMesh);
            sp = new SourcePiece
            {
                Verts = _srcVerts.ToArray(),
                Norms = _srcNorms.Count == _srcVerts.Count ? _srcNorms.ToArray() : null,
                Uvs = _srcUvs.Count == _srcVerts.Count ? _srcUvs.ToArray() : null,
                Tris = _srcTris.ToArray(),
            };
            _sources[(mesh, subMesh)] = sp;
            return sp;
        }

        /// <summary>
        /// Build the segment's mesh from <paramref name="art"/>.
        /// <paramref name="curve"/> runs hub centre to hub centre and the wall
        /// occupies [inset, total − inset]; <paramref name="cellArcs"/> gives
        /// each sim cell's arc length and <paramref name="cellSolid"/> answers
        /// per cell index, exactly as WallCurveMesh takes them.
        /// <paramref name="heightScale"/> squashes the wall toward the ground
        /// while it is being built.
        /// </summary>
        public static Mesh Build(WallModuleArt art, IReadOnlyList<float3> curve, float inset,
                                 IReadOnlyList<float> cellArcs, System.Func<int, bool> cellSolid,
                                 float heightScale, Mesh reuse, Matrix4x4? worldToLocal)
        {
            var mesh = reuse != null ? reuse : new Mesh { name = "WallArt" };
            mesh.Clear();
            if (art == null || !art.IsValid || curve == null || curve.Count < 2) return mesh;

            float hs = Mathf.Clamp(heightScale, 0.02f, 1f);
            int n = curve.Count;
            var cum = new float[n];
            for (int i = 1; i < n; i++)
                cum[i] = cum[i - 1] + math.distance(new float2(curve[i].x, curve[i].z),
                                                    new float2(curve[i - 1].x, curve[i - 1].z));
            float total = cum[n - 1];
            float s0 = inset, s1 = total - inset;
            if (s1 - s0 < 0.5f) { s0 = math.max(0f, total * 0.5f - 0.75f); s1 = math.min(total, total * 0.5f + 0.75f); }
            float span = s1 - s0;

            // Divide the run into whole modules and stretch the pitch to fit
            // exactly — a fraction of a module at the end would read as a gap.
            int copies = Mathf.Max(1, Mathf.RoundToInt(span / art.Length));
            float pitch = span / copies;
            float halfLen = art.Length * 0.5f;

            _verts.Clear(); _norms.Clear(); _uvs.Clear();
            while (_subs.Count < art.Materials.Count) _subs.Add(new List<int>());
            for (int i = 0; i < _subs.Count; i++) _subs[i].Clear();

            var toLocal = worldToLocal ?? Matrix4x4.identity;
            int cellCount = cellArcs != null ? cellArcs.Count : 0;
            if (cellCount > 0) SortCellsByArc(cellArcs);

            for (int c = 0; c < copies; c++)
            {
                float sA = s0 + pitch * c;
                float sAt = sA + pitch * 0.5f;
                if (cellCount > 0 && cellSolid != null && !cellSolid(NearestCellSorted(cellArcs, sAt))) continue;

                // The module's frame along the curve, sampled finely enough
                // that a bent face reads smooth; the ground blended between
                // the two seams, so neighbours meet at the same height.
                float hA = SeamHeight(curve, cum, sA), hB = SeamHeight(curve, cum, sA + pitch);
                for (int k = 0; k <= BendSteps; k++)
                {
                    float3 p = TheWaningBorder.Entities.AlanthorWall.SampleCurve(
                        curve, cum, sA + pitch * k / (float)BendSteps, out float3 tan);
                    _framePos[k] = new Vector3(p.x, 0f, p.z);
                    var f = new Vector3(tan.x, 0f, tan.z).normalized;
                    _frameFwd[k] = f;
                    _frameRight[k] = new Vector3(f.z, 0f, -f.x);
                }

                for (int i = 0; i < art.Pieces.Count; i++)
                {
                    var piece = art.Pieces[i];
                    AppendBent(piece, piece.Local, halfLen, hA, hB, hs, toLocal, _subs[piece.MaterialSlot]);
                }
            }

            mesh.indexFormat = _verts.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_norms);
            if (_uvs.Count == _verts.Count) mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = art.Materials.Count;
            for (int i = 0; i < art.Materials.Count; i++) mesh.SetTriangles(_subs[i], i);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Frame samples along one module: enough that a 3 m module
        /// on the tightest legal bend (12 m radius) reads as a curve.</summary>
        const int BendSteps = 8;
        static readonly Vector3[] _framePos = new Vector3[BendSteps + 1];
        static readonly Vector3[] _frameFwd = new Vector3[BendSteps + 1];
        static readonly Vector3[] _frameRight = new Vector3[BendSteps + 1];

        static float SeamHeight(IReadOnlyList<float3> curve, float[] cum, float s)
        {
            float3 p = TheWaningBorder.Entities.AlanthorWall.SampleCurve(curve, cum, s, out _);
            return TerrainUtility.GetHeight(p.x, p.z);
        }

        /// <summary>
        /// Append one piece of one module, bent: a vertex at module-space
        /// (x across, y up, z along in [-halfLen, halfLen]) lands at the
        /// curve point for z, x along that point's sideways direction, y over
        /// the ground blended between the seams. z is clamped to the module,
        /// so nothing reaches into the neighbour's span.
        /// </summary>
        static void AppendBent(in WallModuleArt.Piece piece, Matrix4x4 local, float halfLen,
            float hA, float hB, float hs, Matrix4x4 toLocal, List<int> tris)
        {
            if (piece.Mesh == null) return;
            var src = SourceOf(piece.Mesh, piece.SubMesh);
            if (src.Tris.Length == 0) return;

            int baseIndex = _verts.Count;
            var verts = src.Verts; var norms = src.Norms; var uvs = src.Uvs;
            float inv = halfLen > 1e-4f ? 0.5f / halfLen : 0f;
            for (int i = 0; i < verts.Length; i++)
            {
                Vector3 v = local.MultiplyPoint3x4(verts[i]);
                float t = Mathf.Clamp01((Mathf.Clamp(v.z, -halfLen, halfLen) + halfLen) * inv);
                float ft = t * BendSteps;
                int k = Mathf.Min(BendSteps - 1, (int)ft);
                float w = ft - k;
                Vector3 pos = Vector3.Lerp(_framePos[k], _framePos[k + 1], w);
                Vector3 right = Vector3.Lerp(_frameRight[k], _frameRight[k + 1], w).normalized;
                Vector3 fwd = Vector3.Lerp(_frameFwd[k], _frameFwd[k + 1], w).normalized;
                float ground = Mathf.Lerp(hA, hB, t);
                Vector3 world = pos + right * v.x + Vector3.up * (ground + v.y * hs);
                _verts.Add(toLocal.MultiplyPoint3x4(world));

                if (norms != null)
                {
                    Vector3 n = local.MultiplyVector(norms[i]);
                    Vector3 wn = right * n.x + Vector3.up * (n.y / Mathf.Max(hs, 0.02f)) + fwd * n.z;
                    _norms.Add(toLocal.MultiplyVector(wn).normalized);
                }
                else _norms.Add(Vector3.up);
                _uvs.Add(uvs != null ? uvs[i] : Vector2.zero);
            }
            var srcTris = src.Tris;
            for (int i = 0; i < srcTris.Length; i++) tris.Add(baseIndex + srcTris[i]);
        }

        static void Append(in WallModuleArt.Piece piece, Matrix4x4 m, List<int> tris)
        {
            if (piece.Mesh == null) return;
            var src = SourceOf(piece.Mesh, piece.SubMesh);
            if (src.Tris.Length == 0) return;

            int baseIndex = _verts.Count;
            var verts = src.Verts; var norms = src.Norms; var uvs = src.Uvs;
            for (int i = 0; i < verts.Length; i++)
            {
                _verts.Add(m.MultiplyPoint3x4(verts[i]));
                _norms.Add(norms != null ? m.MultiplyVector(norms[i]).normalized : Vector3.up);
                _uvs.Add(uvs != null ? uvs[i] : Vector2.zero);
            }
            var srcTris = src.Tris;
            for (int i = 0; i < srcTris.Length; i++) tris.Add(baseIndex + srcTris[i]);
        }

        static IReadOnlyList<float> _sortArcs;
        static readonly System.Comparison<int> _byArc = (a, b) =>
        {
            int c = _sortArcs[a].CompareTo(_sortArcs[b]);
            return c != 0 ? c : a.CompareTo(b);
        };

        static void SortCellsByArc(IReadOnlyList<float> arcs)
        {
            _cellOrder.Clear();
            for (int i = 0; i < arcs.Count; i++) _cellOrder.Add(i);
            _sortArcs = arcs;
            _cellOrder.Sort(_byArc);
            _sortArcs = null;
        }

        /// <summary><see cref="NearestCell"/> by binary search over the
        /// arc-sorted cells: the nearer of the two cells either side of
        /// <paramref name="s"/>, lowest index on a tie — the same pick as the
        /// linear scan.</summary>
        static int NearestCellSorted(IReadOnlyList<float> arcs, float s)
        {
            int n = _cellOrder.Count;
            int lo = 0, hi = n;                       // first position with arc >= s
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (arcs[_cellOrder[mid]] < s) lo = mid + 1; else hi = mid;
            }
            int best = -1; float bestD = float.MaxValue;
            if (lo < n)
            {
                best = _cellOrder[lo];               // lowest index of its arc value
                bestD = math.abs(arcs[best] - s);
            }
            if (lo > 0)
            {
                // Lowest index among the cells sharing the value just below s.
                int k = lo - 1;
                float v = arcs[_cellOrder[k]];
                while (k > 0 && arcs[_cellOrder[k - 1]] == v) k--;
                int cand = _cellOrder[k];
                float d = math.abs(v - s);
                if (d < bestD || (d == bestD && cand < best)) { best = cand; bestD = d; }
            }
            return best < 0 ? 0 : best;
        }

        /// <summary>Index of the cell whose arc position is nearest <paramref name="s"/>.</summary>
        static int NearestCell(IReadOnlyList<float> arcs, float s)
        {
            int best = 0; float bestD = float.MaxValue;
            for (int i = 0; i < arcs.Count; i++)
            {
                float d = math.abs(arcs[i] - s);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }
    }
}
