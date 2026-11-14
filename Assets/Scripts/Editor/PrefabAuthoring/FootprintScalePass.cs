// FootprintScalePass.cs
// Measures building prefabs against their build-grid footprint and scales
// them AS A WHOLE - the prefab ROOT scale, never a piece - so they stand
// inside it (2026-11-13). Buildings are drawn at their authored root scale
// (PresentationSpawnSystem.FootprintFit: no runtime fit), so the root scale
// is the only place a size correction can live.
//
// Jobs come from tools/art/footprint-scale.txt, one a line:
//   <group>|<prefab asset path>|<target metres>|<mode>[|<tile metres>]
// (tile metres: the footprint itself, when the target is smaller than it to
// leave room around the model; the neighbour shots draw it. Default = target.)
// mode  model    the whole model's XZ extent (eaves included) lands on the target
//       marker   the FootprintBounds group's extent lands on the target
//       corners  the *_corner markers' span lands on the target
// Every prefab of one group takes ONE factor - the one its widest member
// needs - so levels authored in one frame keep their common parts in place.
// Re-runnable: spans are always measured with the root at scale 1.
//   Waning Border > Art > Scale > Footprint Jobs: Measure
//   Waning Border > Art > Scale > Footprint Jobs: Apply Root Scale
//   Waning Border > Art > Scale > Footprint Jobs: Render Neighbours
//     three copies of each prefab on adjoining tiles (one beside, one behind),
//     from above and at an angle, to Temp/footprint-neighbours/
// Report: Temp/footprint-scale-report.txt

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class FootprintScalePass
    {
        const string JobFile = "tools/art/footprint-scale.txt";
        const string Report = "Temp/footprint-scale-report.txt";

        struct Job { public string group, path, mode; public float target, tile; }

        [MenuItem("Waning Border/Art/Scale/Footprint Jobs: Measure")]
        public static void MeasureOnly() => Run(false);

        [MenuItem("Waning Border/Art/Scale/Footprint Jobs: Apply Root Scale")]
        public static void Apply() => Run(true);

        static List<Job> ReadJobs(StringBuilder sb)
        {
            var jobs = new List<Job>();
            if (!File.Exists(JobFile)) { Debug.LogError("[FootprintScale] no job file at " + JobFile); return jobs; }
            foreach (var raw in File.ReadAllLines(JobFile))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var p = line.Split('|');
                if (p.Length < 4) { sb.AppendLine("BAD " + line); continue; }
                float target = float.Parse(p[2], CultureInfo.InvariantCulture);
                jobs.Add(new Job { group = p[0], path = p[1], target = target, mode = p[3],
                                   tile = p.Length > 4 ? float.Parse(p[4], CultureInfo.InvariantCulture) : target });
            }
            return jobs;
        }

        static void Run(bool apply)
        {
            var sb = new StringBuilder();
            var jobs = ReadJobs(sb);
            if (jobs.Count == 0) return;

            // Pass 1: spans at root scale 1, and the factor each group needs.
            var factor = new Dictionary<string, float>();
            foreach (var j in jobs)
            {
                var root = PrefabUtility.LoadPrefabContents(j.path);
                try
                {
                    float was = root.transform.localScale.x;
                    root.transform.localScale = Vector3.one;
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Bounds model = Measure(root, out var extremes);
                    bool hasMarker = MarkerBounds(root, out Bounds marker);
                    bool hasCorners = Corners(root, out Vector3 cMin, out Vector3 cMax);
                    sb.AppendLine($"[{j.group}] {Path.GetFileName(j.path)}  root scale now {was:F4}, target {j.target} m by {j.mode}");
                    sb.AppendLine($"    model   {model.size.x:F2} x {model.size.z:F2} m, {model.size.y:F2} high; x {model.min.x:F2}..{model.max.x:F2}  z {model.min.z:F2}..{model.max.z:F2}  y {model.min.y:F2}..{model.max.y:F2}; centre {model.center.x:F2}, {model.center.z:F2}");
                    sb.AppendLine($"    extremes -x {extremes[0]}  +x {extremes[1]}  -z {extremes[2]}  +z {extremes[3]}");
                    if (hasMarker) sb.AppendLine($"    marker  {marker.size.x:F2} x {marker.size.z:F2} m; x {marker.min.x:F2}..{marker.max.x:F2}  z {marker.min.z:F2}..{marker.max.z:F2}  top y {marker.max.y:F2}");
                    if (hasCorners) sb.AppendLine($"    corners {cMax.x - cMin.x:F2} x {cMax.z - cMin.z:F2} m; x {cMin.x:F2}..{cMax.x:F2}  z {cMin.z:F2}..{cMax.z:F2}  y {cMin.y:F2}..{cMax.y:F2}");

                    float span;
                    switch (j.mode)
                    {
                        case "marker": span = hasMarker ? Mathf.Max(marker.size.x, marker.size.z) : 0f; break;
                        case "corners": span = hasCorners ? Mathf.Max(cMax.x - cMin.x, cMax.z - cMin.z) : 0f; break;
                        default:
                            // About the point the game centres the model on: the root
                            // for a corner prefab (no pivot offset), else its own middle.
                            span = hasCorners
                                ? 2f * Mathf.Max(Mathf.Max(-model.min.x, model.max.x), Mathf.Max(-model.min.z, model.max.z))
                                : Mathf.Max(model.size.x, model.size.z);
                            break;
                    }
                    if (span <= 0.001f) { sb.AppendLine("    NO SPAN for mode " + j.mode + " - skipped"); continue; }
                    float f = j.target / span;
                    sb.AppendLine($"    span {span:F2} m -> needs {f:F4}");
                    if (!factor.TryGetValue(j.group, out float g) || f < g) factor[j.group] = f;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            sb.AppendLine();
            foreach (var kv in factor) sb.AppendLine($"GROUP {kv.Key}: factor {kv.Value:F4}");

            // Pass 2: the root scale.
            if (apply)
            {
                foreach (var j in jobs)
                {
                    if (!factor.TryGetValue(j.group, out float f)) continue;
                    var root = PrefabUtility.LoadPrefabContents(j.path);
                    try
                    {
                        root.transform.localScale = Vector3.one * f;
                        PrefabUtility.SaveAsPrefabAsset(root, j.path);
                        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                        var b = Measure(root, out _);
                        sb.AppendLine($"APPLIED {Path.GetFileName(j.path)}: root scale {f:F4} -> {b.size.x:F2} x {b.size.z:F2} m, {b.max.y:F2} high");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                AssetDatabase.SaveAssets();
            }
            Directory.CreateDirectory("Temp");
            File.WriteAllText(Report, sb.ToString());
            Debug.Log("[FootprintScale]\n" + sb);
        }

        [MenuItem("Waning Border/Art/Scale/Footprint Jobs: Render Neighbours")]
        public static void RenderNeighbours()
        {
            var sb = new StringBuilder();
            var jobs = ReadJobs(sb);
            const string outDir = "Temp/footprint-neighbours";
            Directory.CreateDirectory(outDir);
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            bool fog = RenderSettings.fog;
            var ambMode = RenderSettings.ambientMode; var ambCol = RenderSettings.ambientLight;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.46f, 0.5f, 0.58f);
            bool warm = false;
            try
            {
                foreach (var j in jobs)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(j.path);
                    if (prefab == null) { sb.AppendLine("MISSING " + j.path); continue; }
                    var created = new List<GameObject>();
                    try
                    {
                        var stage = new Vector3(0f, -4500f, 0f);
                        var colours = new[] { new Color(0.16f, 0.42f, 0.95f), new Color(0.85f, 0.16f, 0.14f), new Color(0.2f, 0.7f, 0.25f) };
                        var cells = new[] { new Vector3(0, 0, 0), new Vector3(-1, 0, 0), new Vector3(0, 0, -1) };
                        Bounds all = default; bool any = false;
                        var tm = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
                        tm.SetColor("_BaseColor", new Color(0.30f, 0.34f, 0.42f));
                        var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
                        gm.SetColor("_BaseColor", new Color(0.56f, 0.5f, 0.38f));
                        for (int i = 0; i < cells.Length; i++)
                        {
                            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                            go.hideFlags = HideFlags.HideAndDontSave; created.Add(go);
                            go.transform.SetPositionAndRotation(stage, Quaternion.identity);
                            TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, colours[i]);
                            // Where the game stands it: a corner prefab on its root and its
                            // corners' height; else centred on its marker (or model), on its floor.
                            var b = Measure(go, out _);
                            Vector3 centre; float groundY;
                            if (Corners(go, out Vector3 cMin, out Vector3 cMax)) { centre = go.transform.position; groundY = cMin.y; }
                            else if (MarkerBounds(go, out Bounds mk)) { centre = mk.center; groundY = mk.min.y; }
                            else { centre = b.center; groundY = b.min.y; }
                            var tileCentre = stage + cells[i] * j.tile;
                            var shift = new Vector3(tileCentre.x - centre.x, stage.y - groundY, tileCentre.z - centre.z);
                            go.transform.position += shift; b.center += shift;
                            var above = b; above.SetMinMax(new Vector3(b.min.x, stage.y, b.min.z), b.max);
                            if (!any) { all = above; any = true; } else all.Encapsulate(above);
                            if (i == 0) sb.AppendLine($"{prefab.name}: {b.size.x:F2} x {b.size.z:F2} m on a {j.tile} m tile, {b.max.y - stage.y:F2} m above ground; clear to the tile edge: x {j.tile * 0.5f - Mathf.Max(b.max.x - tileCentre.x, tileCentre.x - b.min.x):F2} m, z {j.tile * 0.5f - Mathf.Max(b.max.z - tileCentre.z, tileCentre.z - b.min.z):F2} m");

                            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            tile.hideFlags = HideFlags.HideAndDontSave; created.Add(tile);
                            tile.transform.position = new Vector3(tileCentre.x, stage.y + 0.01f, tileCentre.z);
                            tile.transform.localScale = new Vector3(j.tile - 0.08f, 0.02f, j.tile - 0.08f);
                            tile.GetComponent<Renderer>().sharedMaterial = tm;
                            all.Encapsulate(new Bounds(tileCentre, new Vector3(j.tile, 0.1f, j.tile)));
                        }
                        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        ground.hideFlags = HideFlags.HideAndDontSave; created.Add(ground);
                        ground.transform.position = new Vector3(all.center.x, stage.y - 0.25f, all.center.z);
                        ground.transform.localScale = new Vector3(all.size.x * 4f, 0.5f, all.size.z * 4f);
                        ground.GetComponent<Renderer>().sharedMaterial = gm;

                        var sunGo = new GameObject("ShotSun") { hideFlags = HideFlags.HideAndDontSave }; created.Add(sunGo);
                        var sun = sunGo.AddComponent<Light>();
                        sun.type = LightType.Directional; sun.intensity = 1.5f; sun.color = new Color(1f, 0.95f, 0.86f);
                        sun.shadows = LightShadows.Soft;
                        sunGo.transform.rotation = Quaternion.Euler(46f, 140f, 0f);
                        var fillGo = new GameObject("ShotFill") { hideFlags = HideFlags.HideAndDontSave }; created.Add(fillGo);
                        var fill = fillGo.AddComponent<Light>();
                        fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.78f, 0.84f, 1f);
                        fillGo.transform.rotation = Quaternion.Euler(30f, 320f, 0f);

                        var camGo = new GameObject("ShotCam") { hideFlags = HideFlags.HideAndDontSave }; created.Add(camGo);
                        var cam = camGo.AddComponent<Camera>();
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = new Color(0.55f, 0.66f, 0.78f, 1f);
                        cam.allowHDR = false; cam.allowMSAA = true;
                        string stem = outDir + "/" + prefab.name.Replace(' ', '_');
                        if (!warm) { Shoot(cam, all, 180f, 40f, 64, 64, null); warm = true; }
                        Shoot(cam, all, 180f, 89f, 1500, 1500, stem + "_top.png");
                        Shoot(cam, all, 215f, 38f, 1800, 1200, stem + "_iso.png");
                        Shoot(cam, all, 180f, 12f, 1800, 900, stem + "_front.png");
                        Object.DestroyImmediate(tm); Object.DestroyImmediate(gm);
                    }
                    catch (System.Exception e) { sb.AppendLine("FAIL " + j.path + ": " + e); }
                    finally { foreach (var g in created) if (g != null) Object.DestroyImmediate(g); }
                }
            }
            finally
            {
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol;
                ShaderUtil.allowAsyncCompilation = async;
                File.WriteAllText(outDir + "/log.txt", sb.ToString());
                Debug.Log("[FootprintNeighbours] " + sb);
            }
        }

        static void Shoot(Camera cam, Bounds b, float yaw, float pitch, int w, int h, string outPng)
        {
            cam.fieldOfView = 22f;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            float radius = b.extents.magnitude;
            float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.9f;
            cam.transform.rotation = rot;
            cam.transform.position = b.center - (rot * Vector3.forward) * dist;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = dist + radius * 4f;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            if (outPng != null)
            {
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(outPng, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            cam.targetTexture = null;
            rt.Release(); Object.DestroyImmediate(rt);
        }

        static Bounds Measure(GameObject go, out string[] extremes)
        {
            extremes = new[] { "-", "-", "-", "-" };
            bool any = false; Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var rb = r.bounds;
                if (!any) { b = rb; any = true; for (int i = 0; i < 4; i++) extremes[i] = r.name; continue; }
                if (rb.min.x < b.min.x) extremes[0] = r.name;
                if (rb.max.x > b.max.x) extremes[1] = r.name;
                if (rb.min.z < b.min.z) extremes[2] = r.name;
                if (rb.max.z > b.max.z) extremes[3] = r.name;
                b.Encapsulate(rb);
            }
            return b;
        }

        static bool MarkerBounds(GameObject go, out Bounds b)
        {
            b = default;
            var marker = go.GetComponentInChildren<TheWaningBorder.Rendering.FootprintBounds>(false);
            if (marker == null) return false;
            bool any = false;
            foreach (var r in marker.GetComponentsInChildren<Renderer>(false))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }

        static bool Corners(GameObject go, out Vector3 min, out Vector3 max)
        {
            min = max = default; int n = 0;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.EndsWith("_corner", System.StringComparison.OrdinalIgnoreCase)) continue;
                var p = t.position;
                if (n++ == 0) min = max = p; else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            }
            return n > 0;
        }
    }
}
