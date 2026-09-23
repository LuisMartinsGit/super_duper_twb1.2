// WallArtMesh.cs
// Bakes the authored wall module (WallModuleArt) along a segment's drawn
// curve into ONE mesh — the art-driven twin of WallCurveMesh, and the path
// taken whenever the curtain module's SO has a prefab bound. Same contract, same call
// site, same result shape: one mesh, one sub-mesh per material, spans whose
// sim cell is gone or has become a gate left open.
//
// One copy of the module per cell, turned to the local tangent and dropped
// on the terrain, with the pitch stretched to divide the run exactly so
// consecutive copies butt rather than overlap or gap. A wall of 11 modules
// is still one draw call per material.

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
        static readonly List<Vector3> _srcVerts = new List<Vector3>();
        static readonly List<Vector3> _srcNorms = new List<Vector3>();
        static readonly List<Vector2> _srcUvs = new List<Vector2>();
        static readonly List<int> _srcTris = new List<int>();

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
            // Then stretch a little FURTHER so neighbours interpenetrate: art
            // rarely fills its own bounding box end to end, and whatever is
            // short shows a seam at every joint (AlanthorWall.ModuleOverlap).
            int copies = Mathf.Max(1, Mathf.RoundToInt(span / art.Length));
            float pitch = span / copies;
            float stretch = pitch / art.Length
                            * (1f + TheWaningBorder.Entities.AlanthorWall.ModuleOverlap);

            _verts.Clear(); _norms.Clear(); _uvs.Clear();
            while (_subs.Count < art.Materials.Count) _subs.Add(new List<int>());
            for (int i = 0; i < _subs.Count; i++) _subs[i].Clear();

            var toLocal = worldToLocal ?? Matrix4x4.identity;
            int cellCount = cellArcs != null ? cellArcs.Count : 0;

            for (int c = 0; c < copies; c++)
            {
                float sAt = s0 + pitch * (c + 0.5f);
                if (cellCount > 0 && cellSolid != null && !cellSolid(NearestCell(cellArcs, sAt))) continue;

                float3 p = TheWaningBorder.Entities.AlanthorWall.SampleCurve(curve, cum, sAt, out float3 tan);
                var pos = new Vector3(p.x, TerrainUtility.GetHeight(p.x, p.z), p.z);
                var rot = Quaternion.LookRotation(new Vector3(tan.x, 0f, tan.z), Vector3.up);
                // +Z runs along the wall, so the stretch lands on Z and the
                // construction squash on Y.
                var place = Matrix4x4.TRS(pos, rot, new Vector3(1f, hs, stretch));

                for (int i = 0; i < art.Pieces.Count; i++)
                {
                    var piece = art.Pieces[i];
                    Append(piece, toLocal * place * piece.Local, _subs[piece.MaterialSlot]);
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

        static void Append(in WallModuleArt.Piece piece, Matrix4x4 m, List<int> tris)
        {
            var src = piece.Mesh;
            src.GetVertices(_srcVerts);
            src.GetNormals(_srcNorms);
            src.GetUVs(0, _srcUvs);
            src.GetTriangles(_srcTris, piece.SubMesh);
            if (_srcTris.Count == 0) return;

            int baseIndex = _verts.Count;
            bool haveNormals = _srcNorms.Count == _srcVerts.Count;
            bool haveUvs = _srcUvs.Count == _srcVerts.Count;
            for (int i = 0; i < _srcVerts.Count; i++)
            {
                _verts.Add(m.MultiplyPoint3x4(_srcVerts[i]));
                _norms.Add(haveNormals ? m.MultiplyVector(_srcNorms[i]).normalized : Vector3.up);
                _uvs.Add(haveUvs ? _srcUvs[i] : Vector2.zero);
            }
            for (int i = 0; i < _srcTris.Count; i++) tris.Add(baseIndex + _srcTris[i]);
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
