// IconThumbnailExperiment.cs
// EXPERIMENT (2026-10-11): render a building prefab into a transparent,
// cropped thumbnail for the builder UI. Run in batch mode (WITH graphics):
//   Unity.exe -quit -batchmode -projectPath <p>
//     -executeMethod TheWaningBorder.EditorTools.IconThumbnailExperiment.RenderHouse
//
// Transparency without trusting the pipeline's alpha: URP post-processing can
// write alpha = 1 everywhere, so the model is rendered twice, over black and
// over white, and the alpha of every pixel is recovered from the difference
// (a pixel the model covers is the same colour on both; an empty one is
// black on one and white on the other). Then cropped to the model's pixels
// plus a margin, squared, and scaled to the icon size.

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TheWaningBorder.EditorTools
{
    public static class IconThumbnailExperiment
    {
        const string PrefabPath = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/House/House_1.prefab";
        const string OutDir = "Assets/GameData/Art/Icons/Buildings";
        const string OutPath = OutDir + "/House.png";
        /// <summary>The player colour the builder's own icon is drawn in.</summary>
        const int IconColorIndex = 0;   // Blue
        const int RenderSize = 1024;
        const int IconSize = 256;
        const float Margin = 0.06f;

        [MenuItem("Waning Border/Experiments/Render House Icon")]
        public static void RenderHouse()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError("[IconExperiment] prefab missing: " + PrefabPath); return; }
            var model = (GameObject)Object.Instantiate(prefab);
            model.transform.position = Vector3.zero;

            // Frame the renderers' bounds.
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Debug.LogError("[IconExperiment] no renderers"); return; }
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            // Key + fill light.
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = new Color(1f, 0.96f, 0.88f);
            sun.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            var fill = new GameObject("Fill").AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;
            fill.color = new Color(0.75f, 0.82f, 1f);
            fill.transform.rotation = Quaternion.Euler(30f, 150f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);

            // The RTS three-quarter view, orthographic so the icon has no
            // perspective distortion.
            var cam = new GameObject("IconCam").AddComponent<Camera>();
            cam.orthographic = true;
            // Yaw 140: the FRONT of the house (door side). -40 showed its back.
            cam.transform.rotation = Quaternion.Euler(35f, 140f, 0f);
            float radius = b.extents.magnitude;
            cam.transform.position = b.center - cam.transform.forward * (radius * 4f);
            cam.orthographicSize = radius * 1.05f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = radius * 10f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;

            // One image per player colour, coloured exactly as the game does
            // it (BuildingFactionColorMarker: the roof's marker pixels take the
            // player hue, the rest keeps its authored textures).
            Directory.CreateDirectory(OutDir);
            for (int ci = 0; ci < FactionColors.ColorCount; ci++)
            {
                TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(model, FactionColors.ColorPool[ci]);
                string path = OutDir + "/House_" + FactionColors.ColorNames[ci] + ".png";
                RenderIcon(cam, path);
                if (ci == IconColorIndex) RenderIcon(cam, OutPath);
            }
        }

        static void RenderIcon(Camera cam, string path)
        {
            var black = Grab(cam, Color.black);
            var white = Grab(cam, Color.white);

            // Alpha from the two backgrounds; colour un-premultiplied from black.
            int n = RenderSize;
            var outPx = new Color[n * n];
            var bp = black.GetPixels();
            var wp = white.GetPixels();
            int minX = n, minY = n, maxX = -1, maxY = -1;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                float diff = ((wp[i].r - bp[i].r) + (wp[i].g - bp[i].g) + (wp[i].b - bp[i].b)) / 3f;
                float a = Mathf.Clamp01(1f - diff);
                var c = a > 0.004f ? new Color(bp[i].r / a, bp[i].g / a, bp[i].b / a, a) : Color.clear;
                outPx[i] = c;
                if (a > 0.05f)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) { Debug.LogError("[IconExperiment] nothing rendered"); return; }

            // Square crop around the model with a margin.
            int w = maxX - minX + 1, h = maxY - minY + 1;
            int side = Mathf.CeilToInt(Mathf.Max(w, h) * (1f + 2f * Margin));
            int cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
            var crop = new Texture2D(side, side, TextureFormat.RGBA32, false);
            var cropPx = new Color[side * side];
            for (int y = 0; y < side; y++)
            for (int x = 0; x < side; x++)
            {
                int sx = cx - side / 2 + x, sy = cy - side / 2 + y;
                cropPx[y * side + x] = sx >= 0 && sy >= 0 && sx < n && sy < n ? outPx[sy * n + sx] : Color.clear;
            }
            crop.SetPixels(cropPx);
            crop.Apply();

            // Downscale on the GPU (bilinear) to the icon size.
            var rt = RenderTexture.GetTemporary(IconSize, IconSize, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(crop, rt);
            var icon = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            icon.ReadPixels(new Rect(0, 0, IconSize, IconSize), 0, 0);
            icon.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            File.WriteAllBytes(path, icon.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();

            Debug.Log($"[IconExperiment] wrote {path} guid={AssetDatabase.AssetPathToGUID(path)} " +
                      $"(model {w}x{h} px of {n})");
        }

        static Texture2D Grab(Camera cam, Color background)
        {
            int n = RenderSize;
            cam.backgroundColor = background;
            var rt = new RenderTexture(n, n, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 8;
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, n, n), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            rt.Release();
            return tex;
        }
    }
}
