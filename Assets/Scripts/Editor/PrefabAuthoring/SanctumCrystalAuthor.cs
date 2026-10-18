// SanctumCrystalAuthor.cs
// Authors the Sanctum landmark (Art_Direction.md §6.3): a cluster of tall
// faceted white-gold crystal spires rising from a weathered two-tier stone
// plinth, ringed by leaning standing stones (one fallen), a broken rune inlay
// glowing faintly in the plinth, a shard hovering above the spires, drifting
// gold motes and one soft point light.
//
// Everything is generated here — low-poly flat-shaded meshes in the Synty
// spirit, but owned by the repo (Assets/Synty is git-ignored, so a prefab
// built from it would not survive a fresh clone). Idempotent: meshes are
// refilled in place, materials updated in place, the prefab saved over itself,
// so every GUID the config holds stays put.
//
//   Waning Border > Art > Author Sanctum Crystal
//   Waning Border > Art > Render Sanctum Crystal Preview  → Temp/sanctum-crystal-preview/*.png

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Rendering;

namespace TheWaningBorder.EditorTools.PrefabAuthoring
{
    public static class SanctumCrystalAuthor
    {
        const string Dir = "Assets/GameData/TechTree/ResourceNodes/SanctumCrystal/";
        const string ArtDir = Dir + "Art/";
        const string PrefabPath = Dir + "SanctumCrystal.prefab";
        const string MeshesPath = ArtDir + "SanctumCrystal_Meshes.asset";
        const string MoteTexPath = ArtDir + "SanctumCrystal_Mote.png";
        const string FacetTexPath = ArtDir + "SanctumCrystal_Facets.png";
        const string ConfigPath = Dir + "SanctumCrystalVisual.asset";
        const string PreviewDir = "Temp/sanctum-crystal-preview";
        const string BuildingLitShader = "TheWaningBorder/BuildingLit";
        /// <summary>The crystal group's scale over the plinth's (main spire ≈ 7.9 m).</summary>
        const float CrystalScale = 1.35f;

        // Palette (Art_Direction.md §3.3 worn stone, §6.3 Sanctum gold).
        static readonly Color CrystalAlbedo = new Color(0.96f, 0.90f, 0.76f);
        static readonly Color StoneLight = new Color(0.50f, 0.50f, 0.50f);   // worn stone, neutral grey
        static readonly Color StoneDark = new Color(0.34f, 0.35f, 0.37f);
        static readonly Color MoteColour = new Color(1f, 0.90f, 0.62f, 0.9f);
        static readonly Color LightColour = new Color(1f, 0.88f, 0.62f);

        [MenuItem("Waning Border/Art/Author Sanctum Crystal")]
        public static void Author()
        {
            Directory.CreateDirectory(ArtDir);
            var rng = new System.Random(0x5A9C7);

            // ── Meshes ──────────────────────────────────────────────────
            var meshes = new MeshStore(MeshesPath);
            var lowerTier = meshes.Get("Plinth_Lower", m => Tier(m, 12, 2.7f, 2.45f, -1.2f, 0.30f, 0.05f, rng));
            var upperTier = meshes.Get("Plinth_Upper", m => Tier(m, 10, 1.75f, 1.6f, 0.10f, 0.62f, 0.035f, rng));
            var rune = meshes.Get("Plinth_Rune", m => RuneRing(m, 1.22f, 1.38f, 0.632f, 9, 0.55f));
            var spires = new Mesh[6];
            for (int i = 0; i < spires.Length; i++)
            {
                int ii = i;
                spires[i] = meshes.Get("Spire_" + (char)('A' + i), m => Spire(m, ii % 2 == 0 ? 6 : 5, rng));
            }
            var shard = meshes.Get("Shard", m => Bipyramid(m, 6, 0.32f, 0.55f, 0.75f, rng));
            var stones = new Mesh[4];
            for (int i = 0; i < stones.Length; i++)
                stones[i] = meshes.Get("Stone_" + (char)('A' + i), m => StandingStone(m, rng));
            meshes.Save();

            // ── Materials ───────────────────────────────────────────────
            var lit = Shader.Find(BuildingLitShader);
            if (lit == null) throw new Exception("shader missing: " + BuildingLitShader);
            var crystalMat = LitMat(lit, "SanctumCrystal_Crystal", CrystalAlbedo, 0.85f, EmissiveLadder.Sanctum);
            crystalMat.SetTexture("_EmissionMap", FacetRamp());
            EditorUtility.SetDirty(crystalMat);
            var runeMat = LitMat(lit, "SanctumCrystal_Rune", CrystalAlbedo, 0.6f, EmissiveLadder.SanctumRune);
            var stoneMat = LitMat(lit, "SanctumCrystal_Stone", StoneLight, 0.12f, Color.black);
            var stoneDarkMat = LitMat(lit, "SanctumCrystal_StoneDark", StoneDark, 0.08f, Color.black);
            var moteMat = MoteMaterial();

            // ── Prefab ──────────────────────────────────────────────────
            var root = new GameObject("SanctumCrystal");
            try
            {
                var plinth = Group(root, "Plinth");
                Part(plinth, "LowerTier", lowerTier, stoneDarkMat, Vector3.zero, Quaternion.identity, Vector3.one);
                Part(plinth, "UpperTier", upperTier, stoneMat, Vector3.zero, Quaternion.Euler(0f, 9f, 0f), Vector3.one);
                Part(plinth, "RuneInlay", rune, runeMat, Vector3.zero, Quaternion.identity, Vector3.one);

                var ring = Group(root, "Stones");
                int stoneCount = 7;
                for (int i = 0; i < stoneCount; i++)
                {
                    float a = (i / (float)stoneCount) * 360f + (float)(rng.NextDouble() * 14.0 - 7.0);
                    var mesh = stones[i % stones.Length];
                    var mat = i % 3 == 0 ? stoneDarkMat : stoneMat;
                    var dir = Quaternion.Euler(0f, a, 0f);
                    if (i == 4)
                    {
                        // The fallen one: lying on the plinth's edge, half off it.
                        var pos = dir * new Vector3(0f, 0.34f, 1.95f);
                        var rot = dir * Quaternion.Euler(95f, 18f, 0f);
                        Part(ring, "FallenStone", mesh, mat, pos, rot, new Vector3(1f, 0.8f, 1f));
                        continue;
                    }
                    float lean = (float)rng.NextDouble() * 9f - 6f;   // mostly inward, a few outward
                    float h = 0.85f + (float)rng.NextDouble() * 0.45f;
                    var p = dir * new Vector3(0f, 0.15f, 2.36f);
                    var r = dir * Quaternion.Euler(lean, (float)(rng.NextDouble() * 30.0 - 15.0), 0f);
                    Part(ring, "Stone" + i, mesh, mat, p, r, new Vector3(1f, h, 1f));
                }

                // Crystals: one tall spire, five companions leaning outward,
                // small buds where the upper tier meets the lower.
                var crystals = Group(root, "Crystals");
                // Taller than the ring it stands in: the spires are the
                // landmark, the plinth stays inside the 4 m node footprint.
                crystals.localScale = Vector3.one * CrystalScale;
                Part(crystals, "Spire_Main", spires[0], crystalMat, new Vector3(0f, 0.45f, 0f),
                     Quaternion.Euler(2f, 12f, -3f), new Vector3(1f, 1f, 1f));
                for (int i = 0; i < 5; i++)
                {
                    float a = i * 72f + 20f + (float)(rng.NextDouble() * 18.0 - 9.0);
                    float rad = 0.55f + (float)rng.NextDouble() * 0.3f;
                    float tilt = 12f + (float)rng.NextDouble() * 16f;
                    float s = 0.42f + (float)rng.NextDouble() * 0.26f;
                    var dir = Quaternion.Euler(0f, a, 0f);
                    Part(crystals, "Spire_" + i, spires[1 + i % 5], crystalMat,
                         dir * new Vector3(0f, 0.5f, rad), dir * Quaternion.Euler(tilt, (float)(rng.NextDouble() * 60.0), 0f),
                         new Vector3(0.8f + s * 0.4f, s, 0.8f + s * 0.4f));
                }
                for (int i = 0; i < 6; i++)
                {
                    float a = i * 60f + 35f + (float)(rng.NextDouble() * 20.0 - 10.0);
                    var dir = Quaternion.Euler(0f, a, 0f);
                    float s = 0.12f + (float)rng.NextDouble() * 0.08f;
                    Part(crystals, "Bud_" + i, spires[(i + 2) % spires.Length], crystalMat,
                         dir * new Vector3(0f, 0.24f, 1.8f / CrystalScale), dir * Quaternion.Euler(30f + (float)rng.NextDouble() * 20f, 0f, 0f),
                         new Vector3(0.55f, s, 0.55f));
                }
                Part(crystals, "FloatingShard", shard, crystalMat, new Vector3(0f, 7.15f, 0f),
                     Quaternion.Euler(0f, 0f, 0f), Vector3.one);

                var motes = new GameObject("Motes");
                motes.transform.SetParent(root.transform, false);
                motes.transform.localPosition = new Vector3(0f, 0.7f, 0f);
                motes.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                SetupMotes(motes.AddComponent<ParticleSystem>(), moteMat);

                var lightGo = new GameObject("SanctumLight");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.transform.localPosition = new Vector3(0f, 4.6f, 0f);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = LightColour;
                light.intensity = 2.4f;
                light.range = 10f;
                light.shadows = LightShadows.None;

                root.AddComponent<SanctumCrystalVisual>();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }

            // ── Config ──────────────────────────────────────────────────
            var cfg = AssetDatabase.LoadAssetAtPath<SanctumCrystalVisualConfig>(ConfigPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<SanctumCrystalVisualConfig>();
                cfg.scale = 1f;
                cfg.shardSpinDegreesPerSecond = 14f;
                cfg.shardBobMetres = 0.22f;
                cfg.cycleSeconds = 6f;
                cfg.lightPulseShare = 0.18f;
                AssetDatabase.CreateAsset(cfg, ConfigPath);
            }
            cfg.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            EditorUtility.SetDirty(cfg);
            AssetDatabase.SaveAssets();
            Debug.Log("[SanctumCrystalAuthor] authored " + PrefabPath + " (+ meshes, 5 materials, config). " +
                      "Run Waning Border > Component Config > Rebuild Catalog if the config is new.");
        }

        // ═════════════════════════════════════════════════════════════════
        // Preview
        // ═════════════════════════════════════════════════════════════════

        [MenuItem("Waning Border/Art/Render Sanctum Crystal Preview")]
        public static void RenderPreview()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) { Debug.LogError("[SanctumCrystalAuthor] no prefab — author it first"); return; }
            string outDir = Path.GetFullPath(PreviewDir);
            Directory.CreateDirectory(outDir);
            Render(prefab, outDir, "Sanctum_rts", 37.5f, 47f, 1f);
            Render(prefab, outDir, "Sanctum_close", 16f, 22f, 3.2f);
            Render(prefab, outDir, "Sanctum_far", 80f, 70f, 1f);
            Debug.Log("[SanctumCrystalAuthor] previews in " + outDir);
        }

        static void Render(GameObject prefab, string outDir, string name, float dist, float tilt, float focusY)
        {
            var created = new List<UnityEngine.Object>();
            var stage = new Vector3(0f, -4000f, 0f);
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                created.Add(go);
                go.transform.position = stage;

                // A worker-sized peg for scale, and a footprint outline.
                var peg = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                created.Add(peg);
                peg.transform.position = stage + new Vector3(3.6f, 0.9f, -1.2f);
                peg.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);

                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                created.Add(ground);
                ground.transform.position = stage + new Vector3(0f, -0.02f, 0f);
                ground.transform.localScale = new Vector3(24f, 1f, 24f);
                var gm = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                created.Add(gm);
                gm.SetColor("_BaseColor", new Color(0.30f, 0.40f, 0.28f));
                gm.SetFloat("_Smoothness", 0.05f);
                ground.GetComponent<Renderer>().sharedMaterial = gm;
                peg.GetComponent<Renderer>().sharedMaterial = gm;

                var camGo = new GameObject("SanctumPreviewCam");
                created.Add(camGo);
                var cam = camGo.AddComponent<Camera>();
                cam.fieldOfView = 40f;
                cam.nearClipPlane = 0.3f; cam.farClipPlane = 600f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.10f, 0.12f, 0.18f, 1f);
                var focus = stage + new Vector3(0f, focusY, 0f);
                var back = new Vector3(0f, Mathf.Sin(tilt * Mathf.Deg2Rad), -Mathf.Cos(tilt * Mathf.Deg2Rad));
                camGo.transform.position = focus + back * dist;
                camGo.transform.rotation = Quaternion.LookRotation(-back, Vector3.up);

                var sunGo = new GameObject("SanctumPreviewSun");
                created.Add(sunGo);
                var sun = sunGo.AddComponent<Light>();
                sun.type = LightType.Directional; sun.intensity = 1.2f; sun.color = new Color(1f, 0.86f, 0.74f);
                sun.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(28f, -40f, 0f);

                bool fog = RenderSettings.fog;
                var ambMode = RenderSettings.ambientMode; var ambCol = RenderSettings.ambientLight;
                RenderSettings.fog = false;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.36f, 0.40f, 0.52f);
                var rt = new RenderTexture(1600, 1000, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                RenderSettings.fog = fog; RenderSettings.ambientMode = ambMode; RenderSettings.ambientLight = ambCol;
                cam.targetTexture = null;
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
            finally { foreach (var c in created) if (c != null) UnityEngine.Object.DestroyImmediate(c); }
        }

        // ═════════════════════════════════════════════════════════════════
        // Scene pieces
        // ═════════════════════════════════════════════════════════════════

        static Transform Group(GameObject root, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root.transform, false);
            return g.transform;
        }

        static void Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot;
            g.transform.localScale = scale;
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            // The glowing parts cast no shadow: light does not shadow itself.
            if (mat.name.Contains("Crystal") || mat.name.Contains("Rune"))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void SetupMotes(ParticleSystem ps, Material mat)
        {
            var main = ps.main;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.17f);
            main.startColor = MoteColour;
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.01f;
            main.prewarm = true;
            var em = ps.emission;
            em.rateOverTime = 8f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 1.7f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.4f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        // ═════════════════════════════════════════════════════════════════
        // Materials
        // ═════════════════════════════════════════════════════════════════

        static Material LitMat(Shader shader, string name, Color albedo, float smoothness, Color emission)
        {
            string path = ArtDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader) { name = name }; AssetDatabase.CreateAsset(m, path); }
            else if (m.shader != shader) m.shader = shader;
            m.SetColor("_BaseColor", albedo);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            m.SetColor("_EmissionColor", emission);
            if (emission.maxColorComponent > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>A 64 x 1 grey ramp, 0.3 → 1: the per-facet glow share.</summary>
        static Texture2D FacetRamp()
        {
            if (!File.Exists(FacetTexPath))
            {
                const int n = 64;
                var t = new Texture2D(n, 1, TextureFormat.RGBA32, false);
                for (int x = 0; x < n; x++)
                {
                    float g = Mathf.Lerp(0.3f, 1f, x / (n - 1f));
                    t.SetPixel(x, 0, new Color(g, g, g, 1f));
                }
                t.Apply();
                File.WriteAllBytes(FacetTexPath, t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(FacetTexPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(FacetTexPath);
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.filterMode = FilterMode.Point;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(FacetTexPath);
        }

        static Material MoteMaterial()
        {
            // A soft round dot, written once.
            if (!File.Exists(MoteTexPath))
            {
                const int n = 64;
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        a = a * a * (3f - 2f * a);
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                t.Apply();
                File.WriteAllBytes(MoteTexPath, t.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(MoteTexPath);
                var imp = (TextureImporter)AssetImporter.GetAtPath(MoteTexPath);
                imp.alphaIsTransparency = true;
                imp.mipmapEnabled = false;
                imp.wrapMode = TextureWrapMode.Clamp;
                imp.SaveAndReimport();
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(MoteTexPath);
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) throw new Exception("shader missing: URP Particles/Unlit");
            string path = ArtDir + "SanctumCrystal_Mote.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(shader) { name = "SanctumCrystal_Mote" }; AssetDatabase.CreateAsset(mat, path); }
            else if (mat.shader != shader) mat.shader = shader;
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", new Color(1f, 0.92f, 0.70f, 1f) * 1.6f);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);   // additive
            mat.SetFloat("_ColorMode", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ═════════════════════════════════════════════════════════════════
        // Meshes (flat-shaded: every triangle owns its vertices)
        // ═════════════════════════════════════════════════════════════════

        sealed class MeshStore
        {
            readonly string _path;
            readonly Dictionary<string, Mesh> _existing = new Dictionary<string, Mesh>();
            readonly List<Mesh> _fresh = new List<Mesh>();
            Mesh _main;

            public MeshStore(string path)
            {
                _path = path;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (o is Mesh m) _existing[m.name] = m;
                _main = AssetDatabase.LoadMainAssetAtPath(path) as Mesh;
            }

            public Mesh Get(string name, Action<Mesh> fill)
            {
                if (!_existing.TryGetValue(name, out var m))
                {
                    m = new Mesh { name = name };
                    _fresh.Add(m);
                }
                m.Clear();
                fill(m);
                m.RecalculateBounds();
                EditorUtility.SetDirty(m);
                return m;
            }

            public void Save()
            {
                foreach (var m in _fresh)
                {
                    if (_main == null) { AssetDatabase.CreateAsset(m, _path); _main = m; }
                    else AssetDatabase.AddObjectToAsset(m, _main);
                }
                AssetDatabase.SaveAssets();
            }
        }

        sealed class Tris
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public void Add(Vector3 a, Vector3 b, Vector3 c) { V.Add(a); V.Add(b); V.Add(c); }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Add(a, b, c); Add(a, c, d); }
            /// <summary>Facets: each triangle gets one random u, so a ramp
            /// texture lights the facets unevenly — a crystal, not a lamp.</summary>
            public System.Random Facets;
            public void Into(Mesh m)
            {
                var idx = new int[V.Count];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
                var uv = new Vector2[V.Count];
                if (Facets != null)
                    for (int i = 0; i + 2 < uv.Length; i += 3)
                    {
                        var u = new Vector2((float)Facets.NextDouble(), 0.5f);
                        uv[i] = uv[i + 1] = uv[i + 2] = u;
                    }
                m.SetVertices(V);
                m.SetTriangles(idx, 0);
                m.SetUVs(0, uv);
                m.RecalculateNormals();
            }
        }

        static float J(System.Random r, float amp) => (float)(r.NextDouble() * 2.0 - 1.0) * amp;

        /// <summary>A weathered n-sided slab: bottom ring, a top ring a little
        /// narrower (chamfer) with uneven heights, a top cap.</summary>
        static void Tier(Mesh m, int sides, float rBottom, float rTop, float yBottom, float yTop, float jitter, System.Random rng)
        {
            var t = new Tris();
            var bot = new Vector3[sides];
            var top = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) / sides * Mathf.PI * 2f + J(rng, 0.06f);
                float rb = rBottom * (1f + J(rng, 0.03f));
                float rt = rTop * (1f + J(rng, 0.04f));
                bot[i] = new Vector3(Mathf.Cos(a) * rb, yBottom, Mathf.Sin(a) * rb);
                top[i] = new Vector3(Mathf.Cos(a) * rt, yTop + J(rng, jitter), Mathf.Sin(a) * rt);
            }
            var centre = new Vector3(0f, yTop + jitter * 0.5f, 0f);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                t.Quad(bot[i], top[i], top[j], bot[j]);
                t.Add(centre, top[j], top[i]);
            }
            t.Into(m);
        }

        /// <summary>A flat broken annulus — the rune inlay, `segments` arcs with gaps.</summary>
        static void RuneRing(Mesh m, float rIn, float rOut, float y, int segments, float fill)
        {
            var t = new Tris();
            const int steps = 4;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s / (float)segments * Mathf.PI * 2f;
                float span = fill / segments * Mathf.PI * 2f;
                for (int k = 0; k < steps; k++)
                {
                    float x0 = a0 + span * k / steps, x1 = a0 + span * (k + 1) / steps;
                    var i0 = new Vector3(Mathf.Cos(x0) * rIn, y, Mathf.Sin(x0) * rIn);
                    var o0 = new Vector3(Mathf.Cos(x0) * rOut, y, Mathf.Sin(x0) * rOut);
                    var i1 = new Vector3(Mathf.Cos(x1) * rIn, y, Mathf.Sin(x1) * rIn);
                    var o1 = new Vector3(Mathf.Cos(x1) * rOut, y, Mathf.Sin(x1) * rOut);
                    t.Quad(i0, i1, o1, o0);
                }
                // A short radial tick at each arc's start — reads as a glyph.
                float ta = a0 - 0.03f, tb = a0 + 0.03f;
                t.Quad(new Vector3(Mathf.Cos(tb) * (rOut + 0.02f), y, Mathf.Sin(tb) * (rOut + 0.02f)),
                       new Vector3(Mathf.Cos(tb) * (rOut + 0.16f), y, Mathf.Sin(tb) * (rOut + 0.16f)),
                       new Vector3(Mathf.Cos(ta) * (rOut + 0.16f), y, Mathf.Sin(ta) * (rOut + 0.16f)),
                       new Vector3(Mathf.Cos(ta) * (rOut + 0.02f), y, Mathf.Sin(ta) * (rOut + 0.02f)));
            }
            t.Into(m);
        }

        /// <summary>A faceted spire, base at y 0, about 5.6 m tall at scale 1:
        /// a slightly flared foot, a long shaft, a facetted point off-centre.</summary>
        static void Spire(Mesh m, int sides, System.Random rng)
        {
            var t = new Tris { Facets = rng };
            float r = 0.52f, shaft = 4.1f + J(rng, 0.3f), tip = 1.3f + J(rng, 0.2f);
            var foot = new Vector3[sides];
            var shoulder = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f + J(rng, 0.08f);
                float rf = r * (1.08f + J(rng, 0.06f));
                float rs = r * (0.86f + J(rng, 0.08f));
                foot[i] = new Vector3(Mathf.Cos(a) * rf, -0.3f, Mathf.Sin(a) * rf);
                shoulder[i] = new Vector3(Mathf.Cos(a) * rs, shaft + J(rng, 0.35f), Mathf.Sin(a) * rs);
            }
            var apex = new Vector3(J(rng, 0.08f), shaft + tip, J(rng, 0.08f));
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                t.Quad(foot[i], shoulder[i], shoulder[j], foot[j]);
                t.Add(shoulder[i], apex, shoulder[j]);
            }
            t.Into(m);
        }

        /// <summary>A double point — the floating shard.</summary>
        static void Bipyramid(Mesh m, int sides, float r, float down, float up, System.Random rng)
        {
            var t = new Tris { Facets = rng };
            var ring = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f;
                ring[i] = new Vector3(Mathf.Cos(a) * r, J(rng, 0.04f), Mathf.Sin(a) * r);
            }
            var top = new Vector3(0f, up, 0f);
            var bot = new Vector3(0f, -down, 0f);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                t.Add(ring[i], top, ring[j]);
                t.Add(ring[j], bot, ring[i]);
            }
            t.Into(m);
        }

        /// <summary>A rough standing stone about 1.9 m tall: a tapered, uneven
        /// slab with a blunt ridge for a top, its foot sunk below ground.</summary>
        static void StandingStone(Mesh m, System.Random rng)
        {
            var t = new Tris();
            float w = 0.40f + J(rng, 0.05f), d = 0.26f + J(rng, 0.04f), h = 1.9f + J(rng, 0.25f);
            Vector3 P(float x, float y, float z) => new Vector3(x + J(rng, 0.04f), y, z + J(rng, 0.03f));
            var b0 = P(-w, -0.5f, -d); var b1 = P(w, -0.5f, -d); var b2 = P(w, -0.5f, d); var b3 = P(-w, -0.5f, d);
            var m0 = P(-w * 0.95f, h * 0.55f, -d * 0.95f); var m1 = P(w * 1.02f, h * 0.5f, -d); var m2 = P(w * 0.97f, h * 0.58f, d * 0.9f); var m3 = P(-w, h * 0.52f, d);
            float tw = 0.7f + J(rng, 0.08f);
            var u0 = P(-w * tw, h * (0.92f + J(rng, 0.04f)), -d * 0.75f); var u1 = P(w * tw * 0.85f, h * (0.86f + J(rng, 0.05f)), -d * 0.7f);
            var u2 = P(w * tw * 0.85f, h * (0.88f + J(rng, 0.05f)), d * 0.7f); var u3 = P(-w * tw, h * (0.94f + J(rng, 0.04f)), d * 0.75f);
            var ridgeA = new Vector3(-w * 0.3f, h, 0f); var ridgeB = new Vector3(w * 0.25f, h * 0.96f, 0f);
            Vector3[] lo = { b0, b1, b2, b3 }, mid = { m0, m1, m2, m3 }, hi = { u0, u1, u2, u3 };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                t.Quad(lo[i], mid[i], mid[j], lo[j]);
                t.Quad(mid[i], hi[i], hi[j], mid[j]);
            }
            // Ridge top: two long faces and two end triangles.
            t.Quad(u0, ridgeA, ridgeB, u1);
            t.Quad(u2, ridgeB, ridgeA, u3);
            t.Add(u1, ridgeB, u2);
            t.Add(u3, ridgeA, u0);
            t.Into(m);
        }
    }
}
