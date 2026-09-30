// BallistaBoltVisual.cs
// The ballista's LARGE ARROW (2026-09-29): one procedural mesh — an
// octagonal ash shaft, a four-faced iron head and three fletching fins — in
// place of the arrow template scaled up 2.5x. Built once per match by
// ProjectileVisualSystem as the pooled template for every BallistaBoltTag
// projectile (mobile Ballista, wall-mounted Emplaced Ballista, a Keep's
// Ballista emplacement, a Watch Tower's L3 bolt).
//
// Modelled along +Z with the head forward: ProjectileSystem orients every
// projectile along its velocity, so the bolt flies point-first.
// Three submeshes (wood / iron / fletching), one material each.

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Rendering
{
    public static class BallistaBoltVisual
    {
        private const float Length = 1.9f;      // tail to tip
        private const float ShaftRadius = 0.045f;
        private const float HeadLength = 0.34f;
        private const float HeadRadius = 0.11f;
        private const float FinLength = 0.42f;
        private const float FinHeight = 0.13f;
        private const int ShaftSides = 8;

        private static readonly Color Wood = new Color(0.46f, 0.33f, 0.20f);
        private static readonly Color Iron = new Color(0.24f, 0.23f, 0.22f);
        private static readonly Color Fletch = new Color(0.78f, 0.74f, 0.66f);

        public static GameObject Build()
        {
            var go = new GameObject("BallistaBolt");
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = BuildMesh();
            mr.sharedMaterials = new[]
            {
                MakeMaterial(Wood, 0.0f, 0.25f),
                MakeMaterial(Iron, 0.6f, 0.45f),
                MakeMaterial(Fletch, 0.0f, 0.1f),
            };
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go;
        }

        private static Material MakeMaterial(Color c, float metallic, float smooth)
        {
            var m = new Material(ProceduralPrimitive.LitShader) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            return m;
        }

        public static Mesh BuildMesh()
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var wood = new List<int>();
            var iron = new List<int>();
            var fletch = new List<int>();

            float tail = -Length * 0.5f;
            float shaftEnd = Length * 0.5f - HeadLength;
            float tip = Length * 0.5f;

            // ── Shaft: an octagonal prism, flat-shaded, capped at the tail ──
            for (int i = 0; i < ShaftSides; i++)
            {
                float a0 = i * Mathf.PI * 2f / ShaftSides;
                float a1 = (i + 1) * Mathf.PI * 2f / ShaftSides;
                var p0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * ShaftRadius;
                var p1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * ShaftRadius;
                Quad(v, n, wood,
                    p0 + Vector3.forward * tail, p1 + Vector3.forward * tail,
                    p1 + Vector3.forward * shaftEnd, p0 + Vector3.forward * shaftEnd);
                Tri(v, n, wood, Vector3.forward * tail, p1 + Vector3.forward * tail, p0 + Vector3.forward * tail);
            }

            // ── Head: a four-faced pyramid with a socket collar ──
            var hc = Vector3.forward * shaftEnd;
            var apex = Vector3.forward * tip;
            var corners = new[]
            {
                hc + new Vector3( HeadRadius, 0f, 0f), hc + new Vector3(0f,  HeadRadius, 0f),
                hc + new Vector3(-HeadRadius, 0f, 0f), hc + new Vector3(0f, -HeadRadius, 0f),
            };
            for (int i = 0; i < 4; i++)
            {
                var c0 = corners[i];
                var c1 = corners[(i + 1) % 4];
                Tri(v, n, iron, c0, c1, apex);   // the blade faces
                Tri(v, n, iron, c1, c0, hc);     // the back of the head
            }
            // socket collar: a short iron band where head meets shaft
            float collar0 = shaftEnd - 0.08f;
            for (int i = 0; i < ShaftSides; i++)
            {
                float a0 = i * Mathf.PI * 2f / ShaftSides;
                float a1 = (i + 1) * Mathf.PI * 2f / ShaftSides;
                float r = ShaftRadius * 1.35f;
                var p0 = new Vector3(Mathf.Cos(a0), Mathf.Sin(a0), 0f) * r;
                var p1 = new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f) * r;
                Quad(v, n, iron,
                    p0 + Vector3.forward * collar0, p1 + Vector3.forward * collar0,
                    p1 + Vector3.forward * shaftEnd, p0 + Vector3.forward * shaftEnd);
            }

            // ── Fletching: three fins at 120 degrees, both faces ──
            for (int k = 0; k < 3; k++)
            {
                float ang = Mathf.PI * 0.5f + k * Mathf.PI * 2f / 3f;
                var dir = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);
                var root0 = dir * ShaftRadius + Vector3.forward * (tail + 0.03f);
                var root1 = dir * ShaftRadius + Vector3.forward * (tail + 0.03f + FinLength);
                var outer0 = dir * (ShaftRadius + FinHeight) + Vector3.forward * (tail + 0.01f);
                var outer1 = dir * (ShaftRadius + FinHeight * 0.55f) + Vector3.forward * (tail + FinLength * 0.8f);
                Quad(v, n, fletch, root0, root1, outer1, outer0);
                Quad(v, n, fletch, root0, outer0, outer1, root1);
            }

            var mesh = new Mesh { name = "BallistaBoltMesh" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(wood, 0);
            mesh.SetTriangles(iron, 1);
            mesh.SetTriangles(fletch, 2);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat-shaded triangle, wound so its face normal points out.</summary>
        private static void Tri(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 a, Vector3 b, Vector3 c)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(normal); n.Add(normal); n.Add(normal);
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        private static void Quad(List<Vector3> v, List<Vector3> n, List<int> t,
                                 Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(v, n, t, a, b, c);
            Tri(v, n, t, a, c, d);
        }
    }
}
