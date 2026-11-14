// BiomeShotProbe.cs
// Photographs the OPEN map scene (edit mode) so a terrain look can be judged
// without launching a match: one high overview and one view at about the
// in-game camera's height over a player start.
//   Waning Border > Debug > Biome Probe: Shoot Open Scene  -> Temp/biome-shots/

using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class BiomeShotProbe
    {
        [MenuItem("Waning Border/Debug/Biome Probe: Shoot Open Scene")]
        public static void Shoot()
        {
            var terrain = Object.FindFirstObjectByType<Terrain>();
            if (terrain == null) { Debug.LogWarning("[BiomeShot] no Terrain in the open scene"); return; }
            string scene = EditorSceneManager.GetActiveScene().name;
            Directory.CreateDirectory("Temp/biome-shots");
            var look = terrain.GetComponent<TheWaningBorder.World.Terrain.TerrainLook>();
            if (look != null) look.Apply();

            var size = terrain.terrainData.size;
            var origin = terrain.transform.position;
            Vector3 centre = origin + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            centre.y = terrain.SampleHeight(centre) + origin.y;

            Vector3 home = centre;
            var start = Object.FindFirstObjectByType<TheWaningBorder.World.MapMarkers.PlayerStartMarker>();
            if (start != null) { home = start.transform.position; home.y = terrain.SampleHeight(home) + origin.y; }

            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var camGo = new GameObject("BiomeShotCam") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.45f, 0.55f, 0.66f);
                cam.fieldOfView = 40f;
                cam.farClipPlane = 4000f;
                float span = Mathf.Min(size.x, size.z);
                Render(cam, centre, 0.42f * span, 58f, 30f, $"Temp/biome-shots/{scene}_overview.png", true);
                Render(cam, home, 46f, 52f, 40f, $"Temp/biome-shots/{scene}_home.png", false);
                Render(cam, Vector3.Lerp(home, centre, 0.55f), 46f, 52f, 200f, $"Temp/biome-shots/{scene}_field.png", false);
            }
            finally
            {
                Object.DestroyImmediate(camGo);
                ShaderUtil.allowAsyncCompilation = async;
            }
            Debug.Log("[BiomeShot] " + scene + " -> Temp/biome-shots/");
        }

        static void Render(Camera cam, Vector3 target, float height, float pitch, float yaw, string png, bool warm)
        {
            var rot = Quaternion.Euler(pitch, yaw, 0f);
            float dist = height / Mathf.Sin(pitch * Mathf.Deg2Rad);
            cam.transform.rotation = rot;
            cam.transform.position = target - rot * Vector3.forward * dist;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            if (warm) cam.Render();
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(png, tex.EncodeToJPG(88));
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            rt.Release(); Object.DestroyImmediate(rt);
        }
    }
}
