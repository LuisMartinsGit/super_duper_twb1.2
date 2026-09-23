// WallCurveMesh.cs
// ONE continuous mesh for a curved wall segment (docs/Design/Age_1_Alanthor.md
// § Drawing walls): the curtain's cross-section swept along the segment's
// drawn curve, terrain-following, with the crown broken by a square wave of
// arc length. Spans whose sim cell is gone — or whose cell became a GATE, a
// structure that draws its own 9 m of masonry — are left open, so a breached
// wall shows its breach and a gateway is not walled over.
//
// The cross-section is per WALL LEVEL (§ The three wall levels): a timber
// palisade, crude stone, or reinforced stone with great shields hung along
// the outer face. Five sub-meshes, one per band, so each keeps its own
// colour through the shared procedural Lit material; a tier that does not
// use a band leaves that sub-mesh empty.

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.Rendering
{
    public static class WallCurveMesh
    {
        public const float Step = 0.25f;   // station spacing, metres
        /// <summary>Sub-mesh count — the widest profile (reinforced) uses all
        /// five; the others leave the shield band empty.</summary>
        public const int Bands = 5;

        /// <summary>One swept band of the cross-section.</summary>
        struct Band
        {
            /// <summary>Half-width across the wall, metres.</summary>
            public float Half;
            /// <summary>Centre offset along the OUTER normal — how a shield
            /// hangs on the outer face without a twin on the inner one.</summary>
            public float Shift;
            public float Y0, Y1;
            /// <summary>Broken into blocks by a square wave of arc length.</summary>
            public bool Broken;
            public float Period, Duty;
            /// <summary>False leaves the sub-mesh empty at this tier.</summary>
            public bool Used;
        }

        static Band Solid(float half, float y0, float y1)
            => new Band { Half = half, Y0 = y0, Y1 = y1, Used = true };

        static Band Blocks(float half, float y0, float y1, float period, float duty, float shift = 0f)
            => new Band { Half = half, Shift = shift, Y0 = y0, Y1 = y1, Broken = true, Period = period, Duty = duty, Used = true };

        static readonly Band Unused = default;

        /// <summary>
        /// Level 1 — the palisade: split logs on a timber sill under a lashed
        /// rail, sharpened tops. Level 2 — crude stone: plinth, masonry body,
        /// coping ledge, merlon crown. Level 3 — reinforced: heavier stone, an
        /// iron band at the coping, and great shields hung on the outer face.
        /// </summary>
        static Band[] Profile(byte tier) => tier switch
        {
            TheWaningBorder.Entities.WallTiers.Reinforced => new[]
            {
                Solid(0.72f, 0f,     0.35f),                 // plinth
                Solid(0.56f, 0.35f,  2.10f),                 // body
                Solid(0.70f, 2.10f,  2.28f),                 // iron band / coping
                Blocks(0.46f, 2.28f, 2.90f, 1.5f, 0.65f),    // merlons
                Blocks(0.24f, 1.00f, 2.05f, 1.6f, 0.50f, shift: 0.50f), // shields
            },
            TheWaningBorder.Entities.WallTiers.Stone => new[]
            {
                Solid(0.65f, 0f,     0.30f),
                Solid(0.50f, 0.30f,  2.00f),
                Solid(0.63f, 2.00f,  2.15f),
                Blocks(0.40f, 2.15f, 2.60f, 1.5f, 0.60f),
                Unused,
            },
            _ => new[]
            {
                Solid(0.58f, 0f,     0.22f),                 // timber sill
                Solid(0.34f, 0.22f,  1.95f),                 // log body
                Solid(0.46f, 1.95f,  2.08f),                 // lashed rail
                Blocks(0.30f, 2.08f, 2.55f, 0.50f, 0.78f),   // sharpened log tops
                Unused,
            },
        };

        /// <summary>
        /// Build the mesh. <paramref name="curve"/> runs hub centre to hub
        /// centre; the wall occupies [inset, total − inset]. <paramref name="cellArcs"/>
        /// gives each cell's arc length along the curve (from its stored
        /// position — AlanthorWall.ArcLengthAlong), every station belongs to
        /// the nearest cell, and <paramref name="cellSolid"/> answers per cell
        /// index: false leaves that cell's stations OPEN (dead, or a gate,
        /// which builds its own masonry). Positions rather than even spans,
        /// because a segment split at a hub keeps its cells where they were
        /// while its span changes under them.
        /// </summary>
        public static Mesh Build(IReadOnlyList<float3> curve, float inset, IReadOnlyList<float> cellArcs,
                                 System.Func<int, bool> cellSolid, float heightScale = 1f, Mesh reuse = null,
                                 Matrix4x4? worldToLocal = null,
                                 byte tier = TheWaningBorder.Entities.WallTiers.Palisade)
        {
            var mesh = reuse != null ? reuse : new Mesh { name = "WallCurve" };
            mesh.Clear();
            float hs = Mathf.Clamp(heightScale, 0.02f, 1f);
            if (curve == null || curve.Count < 2) return mesh;

            var profile = Profile(tier);

            int n = curve.Count;
            var cum = new float[n];
            for (int i = 1; i < n; i++)
                cum[i] = cum[i - 1] + math.distance(new float2(curve[i].x, curve[i].z),
                                                    new float2(curve[i - 1].x, curve[i - 1].z));
            float total = cum[n - 1];
            float s0 = inset, s1 = total - inset;
            if (s1 - s0 < 0.5f) { s0 = math.max(0f, total * 0.5f - 0.75f); s1 = math.min(total, total * 0.5f + 0.75f); }
            float span = s1 - s0;
            int cellCount = cellArcs != null ? cellArcs.Count : 0;

            int stations = math.max(2, (int)math.ceil(span / Step) + 1);
            var pos = new Vector3[stations];
            var right = new Vector3[stations];
            var arc = new float[stations];
            for (int i = 0; i < stations; i++)
            {
                float s = s0 + span * i / (stations - 1);
                arc[i] = s;
                float3 p = TheWaningBorder.Entities.AlanthorWall.SampleCurve(curve, cum, s, out float3 tan);
                pos[i] = new Vector3(p.x, TerrainUtility.GetHeight(p.x, p.z), p.z);
                right[i] = new Vector3(tan.z, 0f, -tan.x);   // across the wall (+ = outer face)
            }

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var subs = new List<int>[Bands];
            for (int b = 0; b < Bands; b++) subs[b] = new List<int>();

            for (int i = 0; i < stations - 1; i++)
            {
                float mid = (arc[i] + arc[i + 1]) * 0.5f;
                if (cellCount > 0 && cellSolid != null && !cellSolid(NearestCell(cellArcs, mid))) continue;

                for (int b = 0; b < Bands && b < profile.Length; b++)
                {
                    var band = profile[b];
                    if (!band.Used) continue;
                    bool here = !band.Broken || Block(arc[i], band);
                    bool next = !band.Broken || Block(arc[i + 1], band);
                    if (band.Broken && !(here && next))
                    {
                        // The face closing a block where the band steps down.
                        if (here != next) EndFace(verts, norms, uvs, subs[b], here ? pos[i] : pos[i + 1],
                                                 here ? right[i] : right[i + 1], band, facingForward: here,
                                                 pos[i + 1] - pos[i], hs);
                        continue;
                    }
                    Extrude(verts, norms, uvs, subs[b], pos[i], right[i], pos[i + 1], right[i + 1], band, arc[i], arc[i + 1], hs);
                }
            }
            // Closed ends of the wall body at both hubs would be inside the hub; skip.

            // The visual's root is placed by PresentationSpawnSystem.SyncTransforms
            // (entity position, yaw + the building offset), so the mesh has to be
            // in THAT frame, not world space — the caller hands us the frame.
            if (worldToLocal.HasValue)
            {
                var m = worldToLocal.Value;
                for (int i = 0; i < verts.Count; i++) verts[i] = m.MultiplyPoint3x4(verts[i]);
                for (int i = 0; i < norms.Count; i++) norms[i] = m.MultiplyVector(norms[i]).normalized;
            }

            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = Bands;
            for (int b = 0; b < Bands; b++) mesh.SetTriangles(subs[b], b);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Index of the cell whose arc position is nearest <paramref name="s"/>.
        /// Cells are few (a module every 3 m); a linear scan per station is fine.</summary>
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

        static bool Block(float s, in Band band) => math.frac(s / band.Period) < band.Duty;

        /// <summary>One band between two stations: outer face, inner face, top.</summary>
        static void Extrude(List<Vector3> v, List<Vector3> nrm, List<Vector2> uv, List<int> tris,
                            Vector3 pA, Vector3 rA, Vector3 pB, Vector3 rB, in Band band, float sA, float sB, float hs)
        {
            float y0 = band.Y0 * hs, y1 = band.Y1 * hs;
            Vector3 cA = pA + rA * band.Shift, cB = pB + rB * band.Shift;
            Vector3 aOutLo = cA + rA * band.Half + Vector3.up * y0, aOutHi = cA + rA * band.Half + Vector3.up * y1;
            Vector3 aInLo  = cA - rA * band.Half + Vector3.up * y0, aInHi  = cA - rA * band.Half + Vector3.up * y1;
            Vector3 bOutLo = cB + rB * band.Half + Vector3.up * y0, bOutHi = cB + rB * band.Half + Vector3.up * y1;
            Vector3 bInLo  = cB - rB * band.Half + Vector3.up * y0, bInHi  = cB - rB * band.Half + Vector3.up * y1;
            Vector3 nOut = ((rA + rB) * 0.5f).normalized, nIn = -nOut;
            float uA = sA / 3f, uB = sB / 3f;
            Quad(v, nrm, uv, tris, aOutLo, bOutLo, bOutHi, aOutHi, nOut, uA, uB, band.Y0, band.Y1);
            Quad(v, nrm, uv, tris, bInLo, aInLo, aInHi, bInHi, nIn, uB, uA, band.Y0, band.Y1);
            Quad(v, nrm, uv, tris, aOutHi, bOutHi, bInHi, aInHi, Vector3.up, uA, uB, 0f, 1f);
        }

        /// <summary>The face closing a block where the band steps down.</summary>
        static void EndFace(List<Vector3> v, List<Vector3> nrm, List<Vector2> uv, List<int> tris,
                            Vector3 p, Vector3 r, in Band band, bool facingForward, Vector3 along, float hs)
        {
            Vector3 n = (facingForward ? along : -along).normalized;
            float y0 = band.Y0 * hs, y1 = band.Y1 * hs;
            Vector3 c = p + r * band.Shift;
            Vector3 outLo = c + r * band.Half + Vector3.up * y0, outHi = c + r * band.Half + Vector3.up * y1;
            Vector3 inLo  = c - r * band.Half + Vector3.up * y0, inHi  = c - r * band.Half + Vector3.up * y1;
            if (facingForward) Quad(v, nrm, uv, tris, inLo, outLo, outHi, inHi, n, 0f, 1f, 0f, 1f);
            else               Quad(v, nrm, uv, tris, outLo, inLo, inHi, outHi, n, 0f, 1f, 0f, 1f);
        }

        static void Quad(List<Vector3> v, List<Vector3> nrm, List<Vector2> uv, List<int> tris,
                         Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n,
                         float u0, float u1, float v0, float v1)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            nrm.Add(n); nrm.Add(n); nrm.Add(n); nrm.Add(n);
            uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u1, v0)); uv.Add(new Vector2(u1, v1)); uv.Add(new Vector2(u0, v1));
            // Winding chosen so the normal we assign is the outward one.
            Vector3 geom = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(geom, n) >= 0f) { tris.Add(i); tris.Add(i + 1); tris.Add(i + 2); tris.Add(i); tris.Add(i + 2); tris.Add(i + 3); }
            else                            { tris.Add(i); tris.Add(i + 2); tris.Add(i + 1); tris.Add(i); tris.Add(i + 3); tris.Add(i + 2); }
        }
    }
}
