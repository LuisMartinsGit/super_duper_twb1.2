// TrueScaleLineupShot.cs
// One offscreen picture of the Alanthor Garrison, Royal Stable, Siege Yard
// and Archery Range (their level 1 models, straight from the level SOs'
// prefabs) standing side by side at scale 1, with a Worker, a Spearman and
// an Archer in front for scale. Writes Temp/true-scale/lineup.png, a close
// shot of the three units, and a log with every measured size.
//   Waning Border > Art > Scale > Render True Scale Lineup

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class TrueScaleLineupShot
    {
        const string Root = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/";
        static readonly (string label, string path)[] Models =
        {
            ("Garrison", Root + "Garrison/Garrison_2.prefab"),
            ("RoyalStable", Root + "RoyalStable/Art/alanthor_RoyalStable_lv1.prefab"),
            ("SiegeYard", Root + "SiegeYard/Art/alanthor_SiegeYard_lv1.prefab"),
            ("ArcheryRange", Root + "ArcheryRange/ArcheryRange_3 2.prefab"),
        };
        static readonly (string label, string path)[] Units =
        {
            ("Worker", "Assets/GameData/TechTree/Age0/Buildings/Fortress/Units/Worker/Worker.prefab"),
            ("Spearman", "Assets/GameData/TechTree/Age0/Buildings/Barracks/Units/Spearman/Spearman.prefab"),
            ("Archer", "Assets/GameData/TechTree/Civs/Alanthor/Buildings/ArcheryRange/Units/Archer/Archer.prefab"),
        };
        const string OutDir = "Temp/true-scale";
        // The Royal Stable's footprint: 4 x 4 cells of 2 m (RoyalStable.asset).
        const float TileMetres = 8f;

        [MenuItem("Waning Border/Art/Scale/Render True Scale Lineup")]
        public static void Render()
        {
            var log = new StringBuilder();
            var created = new List<GameObject>();
            var stage = new Vector3(0f, -4500f, 0f);
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            bool fog = RenderSettings.fog;
            var ambMode = RenderSettings.ambientMode; var ambCol = RenderSettings.ambientLight;
            try
            {
                Directory.CreateDirectory(OutDir);
                var blue = new Color(0.16f, 0.42f, 0.95f, 1f);
                var each = new List<Bounds>();
                var tiles = new List<Renderer>();
                Bounds all = default; bool any = false;
                float x = 0f;
                foreach (var (label, path) in Models)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) { log.AppendLine("MISSING " + path); continue; }
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    go.transform.SetPositionAndRotation(stage, Quaternion.identity);   // the prefab's own root scale is kept
                    created.Add(go);
                    TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, blue);
                    // The NSEW corner markers, where the prefab has them: they
                    // are the building's footprint and its GROUND LEVEL
                    // (whatever sits below them is buried).
                    bool hasCorners = Corners(go, out Vector3 cMin, out Vector3 cMax, out float cY);
                    var b = Measure(go);
                    // The tile the building stands on: its corners, else the middle of its model.
                    Vector3 tileCentre = hasCorners ? (cMin + cMax) * 0.5f : b.center;
                    float groundY = hasCorners ? cY : b.min.y;
                    float left = Mathf.Max(b.max.x, tileCentre.x + TileMetres * 0.5f);
                    float right = Mathf.Min(b.min.x, tileCentre.x - TileMetres * 0.5f);
                    float front = tileCentre.z + TileMetres * 0.5f;
                    // The camera looks back from +Z, so +X is on the LEFT of the
                    // picture: each model goes to the -X side of the one before,
                    // 2.5 m apart, the fronts of the TILES on one line, the
                    // ground level on the stage floor.
                    var shift = new Vector3(x - (left - stage.x), stage.y - groundY, -(front - stage.z));
                    go.transform.position += shift;
                    b.center += shift; tileCentre += shift;
                    x -= (left - right) + 2.5f;
                    log.AppendLine($"{label}: {b.size.x:F1} m wide, {b.size.z:F1} m deep, {b.max.y - stage.y:F1} m above ground, {Mathf.Max(0f, stage.y - b.min.y):F1} m buried  (root scale {go.transform.localScale.x:F3}, {(hasCorners ? "floor on its corner markers" : "no corner markers: floor on its lowest point")})");

                    // The tile, drawn on the ground.
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tile.hideFlags = HideFlags.HideAndDontSave; created.Add(tile);
                    tile.transform.position = new Vector3(tileCentre.x, stage.y + 0.01f, tileCentre.z);
                    tile.transform.localScale = new Vector3(TileMetres, 0.02f, TileMetres);
                    tiles.Add(tile.GetComponent<Renderer>());

                    // Only what stands above the ground counts for the framing.
                    var above = b; above.SetMinMax(new Vector3(b.min.x, stage.y, b.min.z), b.max);
                    above.Encapsulate(new Vector3(tileCentre.x - TileMetres * 0.5f, stage.y, tileCentre.z - TileMetres * 0.5f));
                    above.Encapsulate(new Vector3(tileCentre.x + TileMetres * 0.5f, stage.y, tileCentre.z + TileMetres * 0.5f));
                    b = above;
                    each.Add(b);
                    if (!any) { all = b; any = true; } else all.Encapsulate(b);

                }

                // A Worker, a Spearman and an Archer in a row in front of the buildings.
                Bounds units = default; bool anyUnit = false;
                for (int i = 0; i < Units.Length; i++)
                {
                    var up = AssetDatabase.LoadAssetAtPath<GameObject>(Units[i].path);
                    if (up == null) { log.AppendLine("MISSING " + Units[i].path); continue; }
                    var u = (GameObject)PrefabUtility.InstantiatePrefab(up);
                    u.hideFlags = HideFlags.HideAndDontSave;
                    u.transform.position = new Vector3(all.center.x + (1 - i) * 1.6f, stage.y, stage.z + 3f);
                    u.transform.rotation = Quaternion.identity;   // facing +Z, toward the camera
                    created.Add(u);
                    var ub = Measure(u);
                    log.AppendLine($"{Units[i].label}: {ub.size.y:F2} m tall (root scale {u.transform.localScale})");
                    all.Encapsulate(ub);
                    if (!anyUnit) { units = ub; anyUnit = true; } else units.Encapsulate(ub);
                }
                if (!any) { File.WriteAllText(OutDir + "/log.txt", log.ToString()); return; }

                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.hideFlags = HideFlags.HideAndDontSave; created.Add(ground);
                ground.transform.position = new Vector3(all.center.x, stage.y - 0.25f, all.center.z);
                ground.transform.localScale = new Vector3(all.size.x * 2f + 40f, 0.5f, all.size.z * 3f + 60f);
                var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
                gm.SetColor("_BaseColor", new Color(0.56f, 0.5f, 0.38f));
                ground.GetComponent<Renderer>().sharedMaterial = gm;
                var tm = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave };
                tm.SetColor("_BaseColor", new Color(0.30f, 0.34f, 0.42f));
                foreach (var tr in tiles) tr.sharedMaterial = tm;

                var sunGo = new GameObject("ShotSun") { hideFlags = HideFlags.HideAndDontSave }; created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.5f; sun.color = new Color(1f, 0.95f, 0.86f);
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(46f, 140f, 0f);
                var fillGo = new GameObject("ShotFill") { hideFlags = HideFlags.HideAndDontSave }; created.Add(fillGo);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.78f, 0.84f, 1f);
                fillGo.transform.rotation = Quaternion.Euler(30f, 320f, 0f);
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.46f, 0.5f, 0.58f);

                var camGo = new GameObject("ShotCam") { hideFlags = HideFlags.HideAndDontSave }; created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.55f, 0.66f, 0.78f, 1f);
                cam.allowHDR = false; cam.allowMSAA = true;

                // The fronts face +Z, so the camera stands on +Z looking back.
                Shoot(cam, all, 180f, 30f, 22f, 2600, 1100, null);                 // warm-up
                Shoot(cam, all, 180f, 30f, 22f, 2600, 1100, OutDir + "/lineup.png");
                if (anyUnit)
                {
                    // the three units close up, the buildings behind them
                    var close = units; close.Expand(new Vector3(6f, 3f, 0f));
                    Shoot(cam, close, 180f, 14f, 22f, 1800, 1100, OutDir + "/units.png");
                }
                Object.DestroyImmediate(gm); Object.DestroyImmediate(tm);
                log.AppendLine("OK");
            }
            catch (System.Exception e) { log.AppendLine("FAIL " + e); }
            finally
            {
                foreach (var g in created) if (g != null) Object.DestroyImmediate(g);
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol;
                ShaderUtil.allowAsyncCompilation = async;
                File.WriteAllText(OutDir + "/log.txt", log.ToString());
                Debug.Log("[TrueScaleLineup]\n" + log);
            }
        }

        /// <summary>The *_corner markers under a model: their XZ box and mean height.</summary>
        static bool Corners(GameObject go, out Vector3 min, out Vector3 max, out float y)
        {
            min = max = default; y = 0f; int n = 0;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.EndsWith("_corner", System.StringComparison.OrdinalIgnoreCase)) continue;
                var p = t.position;
                if (n == 0) { min = max = p; } else { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
                y += p.y; n++;
            }
            if (n == 0) return false;
            y /= n;
            return true;
        }

        static Bounds Measure(GameObject go)
        {
            bool any = false; Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        static void Shoot(Camera cam, Bounds b, float yaw, float pitch, float fov, int w, int h, string outPng)
        {
            cam.fieldOfView = fov;
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            // fit the wider of the two screen axes
            // Fit the box on both screen axes (the camera looks along Z).
            float pr = pitch * Mathf.Deg2Rad, t = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float halfW = b.extents.x, halfH = b.extents.y * Mathf.Cos(pr) + b.extents.z * Mathf.Sin(pr);
            float dist = Mathf.Max(halfW / (t * w / (float)h), halfH / t) * 1.06f + b.extents.z;
            cam.transform.rotation = rot;
            cam.transform.position = b.center - (rot * Vector3.forward) * dist;
            cam.nearClipPlane = 0.3f; cam.farClipPlane = dist + b.extents.magnitude * 4f;
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
    }
}
