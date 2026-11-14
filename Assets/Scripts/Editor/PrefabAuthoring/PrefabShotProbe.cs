// Offscreen perspective shots of any prefab, for checking authored art without
// a match (2026-11-10). Reads jobs from the file named by the TWB_SHOTS
// environment variable or, when unset, Temp/prefab-shots.txt; one job a line:
//   <asset path>|<out png>|<yaw>|<pitch>[|<width>|<height>[|<r,g,b player colour>]]
// The camera orbits the model's bounds centre: yaw 0 looks along +Z, pitch is
// degrees above the horizon. A player colour runs BuildingFactionColorMarker
// the way the presentation does, so tinted roofs and cloth show as in a match.
//   Waning Border > Art > Render Prefab Shots

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class PrefabShotProbe
    {
        public static string JobFile => Environment.GetEnvironmentVariable("TWB_SHOTS") is { Length: > 0 } s ? s : "Temp/prefab-shots.txt";

        [MenuItem("Waning Border/Art/Render Prefab Shots")]
        public static void RenderAll()
        {
            var log = new StringBuilder();
            string file = JobFile;
            if (!File.Exists(file)) { Debug.LogError($"[PrefabShotProbe] no job file at {file}"); return; }
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                bool warm = false;
                foreach (var raw in File.ReadAllLines(file))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    var p = line.Split('|');
                    if (p.Length < 4) { log.AppendLine($"BAD {line}"); continue; }
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p[0]);
                    if (prefab == null) { log.AppendLine($"MISSING {p[0]}"); continue; }
                    float yaw = F(p[2]), pitch = F(p[3]);
                    int w = p.Length > 5 ? int.Parse(p[4]) : 1280, h = p.Length > 5 ? int.Parse(p[5]) : 900;
                    Color? tint = null;
                    if (p.Length > 6 && p[6].Length > 0)
                    {
                        var c = p[6].Split(',');
                        tint = new Color(F(c[0]), F(c[1]), F(c[2]), 1f);
                    }
                    try
                    {
                        if (!warm) { Shot(prefab, null, yaw, pitch, 64, 64, tint, log); warm = true; }
                        Shot(prefab, p[1], yaw, pitch, w, h, tint, log);
                    }
                    catch (Exception e) { log.AppendLine($"FAIL {p[0]}: {e.Message}"); }
                }
            }
            finally { ShaderUtil.allowAsyncCompilation = async; }
            File.WriteAllText(file + ".log", log.ToString());
            Debug.Log("[PrefabShotProbe]\n" + log);
        }

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        static void Shot(GameObject prefab, string outPng, float yaw, float pitch, int w, int h, Color? tint, StringBuilder log)
        {
            var created = new List<GameObject>();
            var stage = new Vector3(0f, -4500f, 0f);
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.position = stage;
                go.transform.rotation = Quaternion.identity;
                created.Add(go);
                if (tint.HasValue) TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, tint.Value);

                bool any = false; Bounds b = default;
                foreach (var r in go.GetComponentsInChildren<Renderer>(false))
                {
                    if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!any) { log.AppendLine($"EMPTY {prefab.name}"); return; }

                // A ground slab so the model reads against something.
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.hideFlags = HideFlags.HideAndDontSave;
                created.Add(ground);
                float span = Mathf.Max(b.size.x, b.size.z) * 3f + 4f;
                ground.transform.position = new Vector3(b.center.x, b.min.y - 0.26f, b.center.z);
                ground.transform.localScale = new Vector3(span, 0.5f, span);
                var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
                gm.SetColor("_BaseColor", new Color(0.36f, 0.4f, 0.3f));
                ground.GetComponent<Renderer>().sharedMaterial = gm;

                var camGo = new GameObject("ShotCam") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 30f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.66f, 0.78f, 1f);
                cam.allowHDR = false; cam.allowMSAA = true;
                var rot = Quaternion.Euler(pitch, yaw, 0f);
                var fwd = rot * Vector3.forward;
                float radius = b.extents.magnitude;
                float dist = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.92f;
                camGo.transform.rotation = rot;
                camGo.transform.position = b.center - fwd * dist;
                cam.nearClipPlane = 0.3f; cam.farClipPlane = dist + radius * 4f;

                var sunGo = new GameObject("ShotSun") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.5f; sun.color = new Color(1f, 0.95f, 0.86f);
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(46f, yaw - 40f, 0f);
                var fillGo = new GameObject("ShotFill") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(fillGo);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.78f, 0.84f, 1f);
                fillGo.transform.rotation = Quaternion.Euler(30f, yaw + 140f, 0f);

                bool fog = RenderSettings.fog;
                var ambMode = RenderSettings.ambientMode; var ambCol = RenderSettings.ambientLight;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.46f, 0.5f, 0.58f);
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol;
                cam.targetTexture = null;
                if (outPng != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(outPng));
                    File.WriteAllBytes(outPng, tex.EncodeToPNG());
                    log.AppendLine($"OK {prefab.name} yaw {yaw} pitch {pitch}: bounds min {b.min - stage} max {b.max - stage} -> {outPng}");
                }
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(gm);
            }
            finally { foreach (var c in created) if (c != null) UnityEngine.Object.DestroyImmediate(c); }
        }
    }
}
