// The Ancient Empire stucco's painted stripes and the player colour (2026-11-12).
//
// Developer: "The new Synty pack has a texture Stucco_White_Pattern_01 with
// light blue paint stripes on the walls. I need these to use the player colour
// material like in the UK_Plaster_Wall_25_Slit prefabs ... the SAME player
// colour material."
//
// The stripes are PIXELS of the wall texture, and a material is per submesh:
// nothing can wear UK_Band_Playercolor until the stripe is geometry of its
// own. This pass makes it so. For every renderer slot that holds the stucco
// pattern material, in every prefab of the scope:
//
//   1. the slot's triangles are cut along the stripes' edges in UV space (the
//      stripes run the full width of the texture, so the cuts are lines of
//      constant V; the rows are read from the PNG itself, at the stripes'
//      widest, so no painted blue is left outside the band);
//   2. the pieces inside a stripe become a NEW submesh, appended last, bound
//      to UK_Band_Playercolor; the rest stay in the slot, on the Synty stucco;
//   3. the cut mesh is saved under Art/UnitKit/StripeCut/ (one per source
//      mesh + slot, shared by every prefab) and the prefab takes it as a
//      normal override on the nested Synty instance.
//
// A wall piece usually holds the stucco in TWO slots (outside and inside
// face): every such slot is cut, and all their stripe pieces share the one
// band submesh.
//
// Re-runnable: a renderer whose stucco slots hold no stripe any more is left
// alone, and a mesh cut earlier is cut further in place. Run it again
// after using the stucco on a new building. Report in
// Temp/stucco-stripe-pass.txt.

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class StuccoStripeBandPass
    {
        const string StuccoMatPath = "Assets/Synty/PolygonAncientEmpire/Materials/BuildingAlts/Stucco_White_Pattern_01.mat";
        const string StuccoPngPath = "Assets/Synty/PolygonAncientEmpire/Textures/BuildingAlts/Stucco_White_Pattern_01.png";
        const string BandMatPath   = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/UnitKit/UK_Band_Playercolor.mat";
        const string OutFolder     = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Art/UnitKit/StripeCut";
        const string MaterialTag   = "Stucco_White_Pattern_01";   // the Synty material, or a copy of it
        const string CutSuffix     = "_StripeCut";

        static readonly string[] Scope =
        {
            "Assets/GameData/TechTree/Age0",
            "Assets/GameData/TechTree/Civs/Alanthor",
            "Assets/GameData/TechTree/Sects",
        };

        [MenuItem("Waning Border/Art/Walls/Stucco stripes -> UK_Band_Playercolor (Age 0 + Alanthor + Sects)")]
        public static void Run()
        {
            var stucco = AssetDatabase.LoadAssetAtPath<Material>(StuccoMatPath);
            var band = AssetDatabase.LoadAssetAtPath<Material>(BandMatPath);
            if (stucco == null || band == null)
            {
                Debug.LogError($"[StuccoStripeBandPass] missing material: stucco={(stucco != null)} band={(band != null)}");
                return;
            }
            var stripes = ReadStripeRows(StuccoPngPath);
            if (stripes.Count == 0)
            {
                Debug.LogError("[StuccoStripeBandPass] no stripe rows found in " + StuccoPngPath);
                return;
            }
            if (!AssetDatabase.IsValidFolder(OutFolder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(OutFolder).Replace('\\', '/'), Path.GetFileName(OutFolder));

            var report = new StringBuilder();
            report.AppendLine("stripes (v from, v to):");
            foreach (var s in stripes) report.AppendLine($"  {s.x:F4} .. {s.y:F4}");

            // Meshes from the first version of this pass carried the slot in
            // their file name; same asset, same guid, new name.
            foreach (var guid in AssetDatabase.FindAssets("t:Mesh", new[] { OutFolder }))
            {
                string old = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(old);
                string renamed = System.Text.RegularExpressions.Regex.Replace(file, @"_s\d+" + CutSuffix + "$", CutSuffix);
                if (renamed != file) AssetDatabase.RenameAsset(old, renamed);
            }

            var cutCache = new Dictionary<string, Mesh>();
            int prefabs = 0, slots = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", Scope))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith(OutFolder)) continue;
                // Cheap filter: the prefab file itself names the material.
                bool uses = false;
                foreach (var dep in AssetDatabase.GetDependencies(path, false))
                    if (dep.EndsWith(".mat") && Path.GetFileNameWithoutExtension(dep).Contains(MaterialTag)) { uses = true; break; }
                if (!uses) continue;

                var root = PrefabUtility.LoadPrefabContents(path);
                int changed = 0;
                try
                {
                    foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mf = mr.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        var src = mf.sharedMesh;
                        var mats = mr.sharedMaterials;
                        var cutSlots = new List<int>();
                        for (int i = 0; i < mats.Length && i < src.subMeshCount; i++)
                            if (mats[i] != null && mats[i].name.Contains(MaterialTag)) cutSlots.Add(i);
                        if (cutSlots.Count == 0) continue;
                        // A mesh this pass cut before keeps its band as the last submesh.
                        bool wasCut = src.name.EndsWith(CutSuffix);
                        int bandSlot = wasCut && mats.Length >= src.subMeshCount && mats[src.subMeshCount - 1] == band
                            ? src.subMeshCount - 1 : -1;

                        string key = AssetDatabase.GetAssetPath(src) + "|" + src.name + "|" + string.Join(",", cutSlots);
                        if (!cutCache.TryGetValue(key, out var cut))
                        {
                            cut = Cut(src, cutSlots, bandSlot, stripes, out int bandTris, out int plainTris);
                            if (cut != null)
                            {
                                string baseName = wasCut ? src.name.Substring(0, src.name.Length - CutSuffix.Length) : src.name;
                                cut.name = baseName + CutSuffix;
                                string assetPath = $"{OutFolder}/{baseName}{CutSuffix}.asset";
                                var old = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
                                if (old != null) { EditorUtility.CopySerialized(cut, old); EditorUtility.SetDirty(old); cut = old; }
                                else AssetDatabase.CreateAsset(cut, assetPath);
                                report.AppendLine($"mesh {baseName} slots {string.Join(",", cutSlots)}: {plainTris} plain + {bandTris} band triangles -> {assetPath}");
                            }
                            cutCache[key] = cut;
                        }

                        var next = new List<Material>(mats);
                        bool touched = false;
                        foreach (int slot in cutSlots)
                            if (next[slot] != stucco) { next[slot] = stucco; touched = true; }   // the plain part: the Synty stucco itself
                        if (cut != null)
                        {
                            // Exactly one material per submesh, the band last.
                            int want = cut.subMeshCount - 1;
                            while (next.Count < want) next.Add(null);
                            if (next.Count > want) next.RemoveRange(want, next.Count - want);
                            next.Add(band);
                            mf.sharedMesh = cut;
                            touched = true;
                        }
                        if (!touched) continue;
                        mr.sharedMaterials = next.ToArray();
                        changed++;
                    }
                    if (changed > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }

                if (changed > 0)
                {
                    prefabs++;
                    slots += changed;
                    report.AppendLine($"prefab {path}: {changed} renderers");
                }
            }
            AssetDatabase.SaveAssets();
            report.AppendLine($"TOTAL {prefabs} prefabs, {slots} renderers");
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/stucco-stripe-pass.txt", report.ToString());
            Debug.Log($"[StuccoStripeBandPass] {prefabs} prefabs, {slots} renderers. Report: Temp/stucco-stripe-pass.txt");
        }

        // ── The stripes, read off the texture ─────────────────────────────

        /// <summary>Each stripe as (v from, v to), at its widest: every row
        /// that holds any bluish pixel, grown by one row.</summary>
        static List<Vector2> ReadStripeRows(string pngPath)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var result = new List<Vector2>();
            try
            {
                tex.LoadImage(File.ReadAllBytes(pngPath));
                int w = tex.width, h = tex.height;
                var px = tex.GetPixels32();
                int from = -1;
                for (int y = 0; y <= h; y++)   // y = 0 is the BOTTOM row, as v = 0
                {
                    bool blue = false;
                    if (y < h)
                        for (int x = 0; x < w; x++)
                        {
                            var c = px[y * w + x];
                            if (c.b - c.r > 26) { blue = true; break; }
                        }
                    if (blue && from < 0) from = y;
                    if (!blue && from >= 0)
                    {
                        result.Add(new Vector2((from - 1f) / h, (y + 1f) / h));
                        from = -1;
                    }
                }
            }
            finally { Object.DestroyImmediate(tex); }
            return result;
        }

        // ── Cutting a submesh along lines of constant V ───────────────────

        struct Vtx
        {
            public Vector3 P, N;
            public Vector4 T;
            public Vector2 U0, U1;
            public Color C;
            public static Vtx Lerp(in Vtx a, in Vtx b, float t) => new Vtx
            {
                P = Vector3.Lerp(a.P, b.P, t),
                N = Vector3.Lerp(a.N, b.N, t).normalized,
                T = Vector4.Lerp(a.T, b.T, t),
                U0 = Vector2.Lerp(a.U0, b.U0, t),
                U1 = Vector2.Lerp(a.U1, b.U1, t),
                C = Color.Lerp(a.C, b.C, t),
            };
        }

        /// <summary>
        /// <paramref name="src"/> with the stripe faces of <paramref name="slots"/>
        /// moved to the band submesh (<paramref name="bandSlot"/>, or a new
        /// last one when it is -1). Null when no face of those slots lies in a
        /// stripe — nothing to do.
        /// </summary>
        static Mesh Cut(Mesh src, List<int> slots, int bandSlot, List<Vector2> stripes, out int bandTris, out int plainTris)
        {
            bandTris = plainTris = 0;
            var pos = src.vertices;
            var nrm = src.normals;
            var tan = src.tangents;
            var uv0 = src.uv;
            var uv1 = src.uv2;
            var col = src.colors;
            if (uv0 == null || uv0.Length != pos.Length) return null;
            bool hasN = nrm != null && nrm.Length == pos.Length;
            bool hasT = tan != null && tan.Length == pos.Length;
            bool hasU1 = uv1 != null && uv1.Length == pos.Length;
            bool hasC = col != null && col.Length == pos.Length;

            Vtx At(int i) => new Vtx
            {
                P = pos[i],
                N = hasN ? nrm[i] : Vector3.up,
                T = hasT ? tan[i] : new Vector4(1, 0, 0, 1),
                U0 = uv0[i],
                U1 = hasU1 ? uv1[i] : Vector2.zero,
                C = hasC ? col[i] : Color.white,
            };

            // The new vertex stream: the original vertices first (every other
            // submesh keeps its indices), cut pieces appended.
            var outV = new List<Vtx>(pos.Length + 256);
            for (int i = 0; i < pos.Length; i++) outV.Add(At(i));
            var plainBySlot = new Dictionary<int, List<int>>();
            var band = new List<int>();
            if (bandSlot >= 0) band.AddRange(src.GetTriangles(bandSlot));
            int bandBefore = band.Count;

            List<Vtx> poly;
            var pieces = new List<List<Vtx>>();
            var next = new List<List<Vtx>>();
            foreach (int slot in slots)
            {
            var plain = plainBySlot[slot] = new List<int>();
            var tris = src.GetTriangles(slot);
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                float vmin = Mathf.Min(uv0[a].y, Mathf.Min(uv0[b].y, uv0[c].y));
                float vmax = Mathf.Max(uv0[a].y, Mathf.Max(uv0[b].y, uv0[c].y));

                // Every stripe edge (in every repeat of the texture) that
                // crosses this triangle.
                pieces.Clear();
                poly = new List<Vtx> { At(a), At(b), At(c) };
                pieces.Add(poly);
                bool cutAny = false;
                for (int k = Mathf.FloorToInt(vmin); k <= Mathf.FloorToInt(vmax); k++)
                    foreach (var s in stripes)
                        for (int e = 0; e < 2; e++)
                        {
                            float line = k + (e == 0 ? s.x : s.y);
                            if (line <= vmin + 1e-5f || line >= vmax - 1e-5f) continue;
                            cutAny = true;
                            next.Clear();
                            foreach (var p in pieces)
                            {
                                var below = Clip(p, line, keepBelow: true);
                                var above = Clip(p, line, keepBelow: false);
                                if (below.Count >= 3) next.Add(below);
                                if (above.Count >= 3) next.Add(above);
                            }
                            pieces.Clear();
                            pieces.AddRange(next);
                        }

                if (!cutAny)
                {
                    // Whole triangle on one side: keep its original vertices.
                    float mid = (uv0[a].y + uv0[b].y + uv0[c].y) / 3f;
                    var list = InStripe(mid, stripes) ? band : plain;
                    list.Add(a); list.Add(b); list.Add(c);
                    continue;
                }
                foreach (var p in pieces)
                {
                    float mid = 0f;
                    foreach (var v in p) mid += v.U0.y;
                    mid /= p.Count;
                    var list = InStripe(mid, stripes) ? band : plain;
                    int first = outV.Count;
                    outV.AddRange(p);
                    for (int i = 1; i + 1 < p.Count; i++)
                    {
                        list.Add(first); list.Add(first + i); list.Add(first + i + 1);
                    }
                }
            }
            plainTris += plain.Count / 3;
            }
            bandTris = band.Count / 3;
            if (band.Count == bandBefore) return null;

            var mesh = new Mesh { name = src.name + CutSuffix };
            if (outV.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            int n = outV.Count;
            var P = new Vector3[n]; var N = new Vector3[n]; var T = new Vector4[n];
            var U0 = new Vector2[n]; var U1 = new Vector2[n]; var C = new Color[n];
            for (int i = 0; i < n; i++)
            {
                P[i] = outV[i].P; N[i] = outV[i].N; T[i] = outV[i].T;
                U0[i] = outV[i].U0; U1[i] = outV[i].U1; C[i] = outV[i].C;
            }
            mesh.vertices = P;
            if (hasN) mesh.normals = N;
            if (hasT) mesh.tangents = T;
            mesh.uv = U0;
            if (hasU1) mesh.uv2 = U1;
            if (hasC) mesh.colors = C;
            int outBand = bandSlot >= 0 ? bandSlot : src.subMeshCount;
            mesh.subMeshCount = outBand + 1;
            for (int s = 0; s < src.subMeshCount; s++)
            {
                if (s == bandSlot) continue;
                mesh.SetTriangles(plainBySlot.TryGetValue(s, out var cutPlain) ? cutPlain.ToArray() : src.GetTriangles(s), s);
            }
            mesh.SetTriangles(band.ToArray(), outBand);
            mesh.RecalculateBounds();
            return mesh;
        }

        static bool InStripe(float v, List<Vector2> stripes)
        {
            float f = v - Mathf.Floor(v);
            foreach (var s in stripes)
                if (f > s.x && f < s.y) return true;
            return false;
        }

        /// <summary>The part of a convex polygon on one side of the line V = <paramref name="line"/>.</summary>
        static List<Vtx> Clip(List<Vtx> poly, float line, bool keepBelow)
        {
            var result = new List<Vtx>(poly.Count + 2);
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i];
                var b = poly[(i + 1) % poly.Count];
                float da = keepBelow ? line - a.U0.y : a.U0.y - line;
                float db = keepBelow ? line - b.U0.y : b.U0.y - line;
                if (da >= 0f) result.Add(a);
                if ((da > 0f && db < 0f) || (da < 0f && db > 0f))
                    result.Add(Vtx.Lerp(a, b, da / (da - db)));
            }
            return result;
        }
    }
}
