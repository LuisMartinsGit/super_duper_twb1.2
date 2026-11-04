// Renders every building's icon for the build menu from its own model
// (2026-11-02): an isometric orthographic shot of the prefab the SO binds,
// framed on its bounds over a transparent background, written to
// Assets/Resources/UI/Icons/Buildings/ where EntityActionExtractor's
// GetBuildingIcon loads it:
//   <buildingId>.png                       the base SO's prefab (the Age 0 form)
//   <buildingId>_<Culture>_L<n>.png        each BuildingLevelDefSO's prefab
// so a player sees the building they would actually get — their culture's
// look at their faction's current level of the type.
//   Waning Border > Art > Render Building Icons
// Re-run after re-authoring a building prefab. Materials are left as authored
// (no faction tint): the icon is the building, not one player's copy of it.

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class BuildingIconRenderer
    {
        const string OutDir = "Assets/Resources/UI/Icons/Buildings";
        const int Size = 256;

        [MenuItem("Waning Border/Art/Render Building Icons")]
        public static void RenderAll()
        {
            Directory.CreateDirectory(OutDir);
            var log = new StringBuilder();
            int ok = 0, skipped = 0;
            var written = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:BuildingDefSO"))
            {
                var so = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Data.BuildingDefSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so == null || string.IsNullOrEmpty(so.id)) continue;
                if (so.prefab == null) { skipped++; log.AppendLine($"skip {so.id}: no prefab"); continue; }
                if (Render(so.prefab, so.id, log)) { ok++; written.Add(so.id); }
            }
            foreach (var guid in AssetDatabase.FindAssets("t:BuildingLevelDefSO"))
            {
                var so = AssetDatabase.LoadAssetAtPath<TheWaningBorder.Data.BuildingLevelDefSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so == null || string.IsNullOrEmpty(so.buildingId)) continue;
                if (so.prefab == null) { skipped++; log.AppendLine($"skip {so.buildingId} {so.culture} L{so.level}: no prefab"); continue; }
                string name = $"{so.buildingId}_{so.culture}_L{so.level}";
                if (Render(so.prefab, name, log)) { ok++; written.Add(name); }
            }

            AssetDatabase.Refresh();
            foreach (var name in written)
            {
                var imp = AssetImporter.GetAtPath($"{OutDir}/{name}.png") as TextureImporter;
                if (imp == null) continue;
                bool dirty = false;
                if (imp.textureType != TextureImporterType.Default) { imp.textureType = TextureImporterType.Default; dirty = true; }
                if (!imp.alphaIsTransparency) { imp.alphaIsTransparency = true; dirty = true; }
                if (imp.mipmapEnabled) { imp.mipmapEnabled = false; dirty = true; }
                if (imp.maxTextureSize != Size) { imp.maxTextureSize = Size; dirty = true; }
                if (imp.textureCompression != TextureImporterCompression.Uncompressed) { imp.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }
                if (dirty) imp.SaveAndReimport();
            }
            log.AppendLine($"-- {ok} icons written, {skipped} skipped -> {OutDir}");
            File.WriteAllText(Path.Combine(OutDir, "..", "building-icons.txt"), log.ToString());
            Debug.Log("[BuildingIconRenderer]\n" + log);
        }

        static bool Render(GameObject prefab, string name, StringBuilder log)
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
                if (!MeshBounds(go, out var b)) { log.AppendLine($"FAIL {name}: no renderers"); return false; }

                // Isometric: the camera looks down the (1, 1, -1)-ish diagonal
                // from the building's south-east, pitched ~35 degrees — the
                // classic three-faces view, with the front (−z) face showing.
                const int W = Size, H = Size;
                var camGo = new GameObject("IconCam") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                float diag = Mathf.Max(b.size.x, b.size.y, b.size.z);
                cam.nearClipPlane = 0.1f; cam.farClipPlane = diag * 12f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.allowHDR = false; cam.allowMSAA = true;
                var dir = new Vector3(1f, 1.15f, -1f).normalized;
                camGo.transform.position = b.center + dir * diag * 4f;
                camGo.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                float ex = 0f, ey = 0f;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var v = camGo.transform.InverseTransformPoint(corner);
                    ex = Mathf.Max(ex, Mathf.Abs(v.x)); ey = Mathf.Max(ey, Mathf.Abs(v.y));
                }
                cam.orthographicSize = Mathf.Max(ey, ex * H / (float)W) * 1.06f;

                var sunGo = new GameObject("IconSun") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.6f; sun.color = new Color(1f, 0.95f, 0.88f);
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(48f, -30f, 0f);
                var fillGo = new GameObject("IconFill") { hideFlags = HideFlags.HideAndDontSave };
                created.Add(fillGo);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = 0.5f; fill.color = new Color(0.78f, 0.84f, 1f);
                fillGo.transform.rotation = Quaternion.Euler(30f, 150f, 0f);

                bool fog = RenderSettings.fog;
                var ambMode = RenderSettings.ambientMode; var ambCol = RenderSettings.ambientLight;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.48f, 0.52f, 0.6f);
                var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol;
                cam.targetTexture = null;

                // Anything the camera did not draw stays clear; what it drew is
                // opaque — the pipeline's alpha is not trusted either way.
                var px = tex.GetPixels32();
                int drawn = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a > 8) { px[i].a = 255; drawn++; } else px[i].a = 0;
                }
                tex.SetPixels32(px); tex.Apply();
                File.WriteAllBytes($"{OutDir}/{name}.png", tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                rt.Release(); Object.DestroyImmediate(rt);
                log.AppendLine($"OK {name}: {b.size.x:F1} x {b.size.y:F1} x {b.size.z:F1} m, {drawn * 100 / px.Length}% covered");
                return true;
            }
            catch (System.Exception e) { log.AppendLine($"FAIL {name}: {e.Message}"); return false; }
            finally { foreach (var c in created) if (c != null) Object.DestroyImmediate(c); }
        }

        /// <summary>Bounds of the model's mesh renderers. A renderer whose
        /// own extent is more than four times the median renderer's (a
        /// ground decal, a stray far-away piece) is left out of the frame so
        /// it cannot shrink the building to a dot; the log names it.</summary>
        static bool MeshBounds(GameObject go, out Bounds b)
        {
            b = default;
            var rs = new List<Renderer>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                rs.Add(r);
            }
            if (rs.Count == 0) return false;
            var extents = new List<float>(rs.Count);
            foreach (var r in rs) extents.Add(Mathf.Max(r.bounds.size.x, r.bounds.size.z));
            extents.Sort();
            float median = extents[extents.Count / 2];
            float limit = rs.Count >= 5 ? median * 4f : float.PositiveInfinity;
            bool any = false;
            foreach (var r in rs)
            {
                float e = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
                if (e > limit && e > 20f)
                {
                    Debug.LogWarning($"[BuildingIconRenderer] {go.name}: left {r.name} ({e:F1} m wide) out of the icon frame");
                    continue;
                }
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any;
        }
    }
}
