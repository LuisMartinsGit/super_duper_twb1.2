// LegionaryProbe.cs — the offscreen camera LegionarySpearmanAuthor uses for
// its clip sheets (Temp/legionary/*.png).

using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    /// <summary>Offscreen shots of whatever is in the open scene around a bounds.</summary>
    public static class LegionaryShot
    {
        public static Bounds BoundsOf(GameObject go)
        {
            Bounds b = new Bounds(go.transform.position, Vector3.zero); bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        /// <summary>yaw 180 = camera in front (+Z side) looking back at -Z.</summary>
        public static void Shoot(Bounds b, float yaw, float pitch, int w, int h, string outPng, bool ortho = false, bool transparent = false)
        {
            bool fog = RenderSettings.fog;
            var ambMode = RenderSettings.ambientMode; var ambCol = RenderSettings.ambientLight;
            var sunGo = new GameObject("ProbeSun"); var fillGo = new GameObject("ProbeFill"); var camGo = new GameObject("ProbeCam");
            try
            {
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.5f; sun.color = new Color(1f, 0.95f, 0.86f);
                sunGo.transform.rotation = Quaternion.Euler(45f, yaw + 35f, 0f);
                var fill = fillGo.AddComponent<Light>();
                fill.type = LightType.Directional; fill.intensity = 0.5f; fill.color = new Color(0.78f, 0.84f, 1f);
                fillGo.transform.rotation = Quaternion.Euler(20f, yaw - 60f, 0f);
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.58f);

                var cam = camGo.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                // A button icon keeps only the subject: the button's own
                // background shows round it.
                cam.backgroundColor = transparent ? new Color(0f, 0f, 0f, 0f) : new Color(0.36f, 0.4f, 0.44f);
                if (transparent) cam.allowHDR = false;
                cam.fieldOfView = 20f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 500f;
                var rot = Quaternion.Euler(pitch, yaw, 0f);
                float aspect = (float)w / h;
                float radius = b.extents.magnitude;
                float half = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float halfH = Mathf.Atan(Mathf.Tan(half) * aspect);
                float dist = Mathf.Max(b.extents.y / Mathf.Tan(half), Mathf.Max(b.extents.x, b.extents.z) / Mathf.Tan(halfH)) * 1.15f + Mathf.Max(b.extents.x, b.extents.z);
                if (radius < 0f) dist = 1f;
                if (ortho)
                {
                    // Parallel projection: every figure in a row is seen from the same angle.
                    cam.orthographic = true;
                    float wide = Mathf.Abs(Vector3.Dot(rot * Vector3.right, new Vector3(b.extents.x, 0f, 0f)))
                               + Mathf.Abs(Vector3.Dot(rot * Vector3.right, new Vector3(0f, 0f, b.extents.z)));
                    cam.orthographicSize = Mathf.Max(b.extents.y * 1.1f, wide / aspect * 1.08f);
                    dist = 60f;
                }
                cam.transform.rotation = rot;
                cam.transform.position = b.center - rot * Vector3.forward * dist;

                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, transparent ? TextureFormat.RGBA32 : TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                Directory.CreateDirectory(Path.GetDirectoryName(outPng));
                File.WriteAllBytes(outPng, tex.EncodeToPNG());
                cam.targetTexture = null;
                Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt);
            }
            finally
            {
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol;
                Object.DestroyImmediate(sunGo); Object.DestroyImmediate(fillGo); Object.DestroyImmediate(camGo);
            }
        }
    }
}

namespace TheWaningBorder.EditorTools
{
    /// <summary>Play mode: photographs the spawned Spearman views (Temp/legionary/game_spearmen.png) and logs their animators.</summary>
    public static class SpearmanViewProbe
    {
        [UnityEditor.MenuItem("Waning Border/Art/Units/Probe: shoot spawned Spearmen")]
        public static void Shoot()
        {
            var sb = new System.Text.StringBuilder();
            UnityEngine.Bounds b = default; bool first = true; int n = 0;
            foreach (var go in UnityEngine.Object.FindObjectsByType<UnityEngine.GameObject>(UnityEngine.FindObjectsSortMode.None))
            {
                if (go.transform.parent != null || !go.name.StartsWith("Entity_") || !go.name.EndsWith("_368")) continue;
                n++;
                var bb = LegionaryShot.BoundsOf(go);
                if (first) { b = bb; first = false; }
                else if ((bb.center - b.center).magnitude < 9f) b.Encapsulate(bb);
                var an = go.GetComponentInChildren<UnityEngine.Animator>();
                if (n <= 4) sb.AppendLine($"{go.name} height={bb.size.y:0.00} rig={(an ? an.gameObject.name : "none")} ctrl={(an && an.runtimeAnimatorController ? an.runtimeAnimatorController.name : "none")} idle={(an ? an.GetCurrentAnimatorStateInfo(0).IsName("Idle") : false)}");
            }
            sb.AppendLine("spearmen: " + n);
            if (n > 0) { b.Expand(1.2f); LegionaryShot.Shoot(b, 200f, 35f, 1600, 1000, "Temp/legionary/game_spearmen.png"); }
            System.IO.File.WriteAllText("Temp/legionary/game_spearmen.txt", sb.ToString());
        }
    }
}

namespace TheWaningBorder.EditorTools
{
    /// <summary>Lists the atlas colours the Spearman's first-age body uses, by where on the body they sit.</summary>
    public static class LevyAtlasProbe
    {
        [UnityEditor.MenuItem("Waning Border/Art/Units/Probe: levy atlas colours")]
        public static void Run()
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.GameObject>("Assets/GameData/TechTree/Age0/Buildings/Barracks/Units/Spearman/Spearman.prefab");
            var levy = prefab.transform.Find("Levy");
            var sb = new System.Text.StringBuilder();
            foreach (var smr in levy.GetComponentsInChildren<UnityEngine.SkinnedMeshRenderer>(true))
            {
                var mat = smr.sharedMaterial; UnityEngine.Texture tex = null; string used = "";
                foreach (var p in mat.GetTexturePropertyNames())
                    if (mat.GetTexture(p) != null && tex == null) { tex = mat.GetTexture(p); used = p; }
                sb.AppendLine($"{smr.name} mat={mat.name} shader={mat.shader.name} tex={(tex ? tex.name : "-")} prop={used} {(tex ? tex.width : 0)}x{(tex ? tex.height : 0)}");
                foreach (var p in mat.GetTexturePropertyNames()) sb.AppendLine("  prop " + p + " = " + (mat.GetTexture(p) ? mat.GetTexture(p).name : "-"));
                if (tex == null) continue;
                var rt = UnityEngine.RenderTexture.GetTemporary(tex.width, tex.height, 0, UnityEngine.RenderTextureFormat.ARGB32, UnityEngine.RenderTextureReadWrite.sRGB);
                UnityEngine.Graphics.Blit(tex, rt);
                var prev = UnityEngine.RenderTexture.active; UnityEngine.RenderTexture.active = rt;
                var copy = new UnityEngine.Texture2D(tex.width, tex.height, UnityEngine.TextureFormat.RGBA32, false, false);
                copy.ReadPixels(new UnityEngine.Rect(0, 0, tex.width, tex.height), 0, 0, false);
                UnityEngine.RenderTexture.active = prev; UnityEngine.RenderTexture.ReleaseTemporary(rt);
                var mesh = smr.sharedMesh; var uv = mesh.uv; var v = mesh.vertices;
                var groups = new System.Collections.Generic.Dictionary<int, (int n, float y, float ymin, float ymax, float ax)>();
                for (int i = 0; i < v.Length; i++)
                {
                    UnityEngine.Color32 c = copy.GetPixel((int)(UnityEngine.Mathf.Repeat(uv[i].x, 1f) * tex.width), (int)(UnityEngine.Mathf.Repeat(uv[i].y, 1f) * tex.height));
                    int key = (c.r / 8 << 16) | (c.g / 8 << 8) | (c.b / 8);
                    groups.TryGetValue(key, out var g);
                    if (g.n == 0) { g.ymin = 99f; g.ymax = -99f; }
                    g.n++; g.y += v[i].y; g.ymin = UnityEngine.Mathf.Min(g.ymin, v[i].y); g.ymax = UnityEngine.Mathf.Max(g.ymax, v[i].y); g.ax += UnityEngine.Mathf.Abs(v[i].x);
                    groups[key] = g;
                }
                foreach (var kv in groups)
                    if (kv.Value.n >= 12)
                        sb.AppendLine($"  rgb~({(kv.Key >> 16) * 8},{((kv.Key >> 8) & 255) * 8},{(kv.Key & 255) * 8}) verts={kv.Value.n} y={kv.Value.y / kv.Value.n:0.00} [{kv.Value.ymin:0.00}..{kv.Value.ymax:0.00}] |x|={kv.Value.ax / kv.Value.n:0.00}");
                UnityEngine.Object.DestroyImmediate(copy);
            }
            System.IO.File.WriteAllText("Temp/legionary/levy_atlas.txt", sb.ToString());
        }
    }
}
