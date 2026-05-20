// BuildingPrefabPrewarm.cs
// Pulls every building prefab variant into memory and forces a one-frame
// off-screen render so URP shader variants compile before gameplay starts.
// Without this, the first time a level-up swap (or culture-choice respawn)
// instantiates a prefab the engine has never drawn, Unity stalls compiling
// shader variants and the building flashes its fallback colour while we're
// already mid-dissolve. Running this during the loading screen pays the
// cost up front so the dissolve renders cleanly every time.
//
// Location: Assets/Scripts/Bootstrap/BuildingPrefabPrewarm.cs

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TheWaningBorder.UI.Menus;

namespace TheWaningBorder.Bootstrap
{
    public static class BuildingPrefabPrewarm
    {
        // Per-renderer layer used by the off-screen prewarm camera so the
        // prefabs we're warming don't render to the player's main camera.
        // 30 is reserved for this — adjust if it collides with project layers.
        private const int PrewarmLayer = 30;

        // Mirrors the variant grid in BuildingPrefabSwapSystem.BuildCandidatePaths.
        // We over-enumerate cultures × levels × variants and let null-result
        // Resources.Load tell us which combos exist on disk.
        private static readonly string[] CultureCodes  = { "al", "ru", "fe" };
        private static readonly int[]    Levels        = { 1, 2, 3 };
        private static readonly int[]    HouseVariants = { 0, 1, 2 };

        /// <summary>
        /// Run the prewarm. Yields per-prefab so the LoadingScreen can update
        /// its progress bar between assets instead of freezing for the full
        /// duration in one stall.
        /// </summary>
        public static IEnumerator PrewarmAll()
        {
            // We're called from SpawnDelayHelper at the 88 % mark and own
            // the 88 → 99 % slice of the loading bar. SpawnDelayHelper sets
            // the final 100 % after PrewarmAll returns.
            const float ProgressStart = 0.88f;
            const float ProgressEnd   = 0.99f;
            const float ProgressSpan  = ProgressEnd - ProgressStart;

            LoadingScreen.SetStatus("Warming up prefabs…");

            var paths = CollectPaths();
            int total = paths.Count;
            if (total == 0) yield break;

            // Off-screen camera that renders only the prewarm layer.
            var camGo = new GameObject("PrewarmCamera");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            var cam = camGo.AddComponent<Camera>();
            cam.enabled         = false; // we trigger Render() manually
            cam.cullingMask     = 1 << PrewarmLayer;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = 200f;
            cam.fieldOfView     = 60f;
            cam.depth           = -100;

            var rt = new RenderTexture(64, 64, 16, RenderTextureFormat.ARGB32);
            rt.hideFlags = HideFlags.HideAndDontSave;
            cam.targetTexture = rt;

            // Stage the prefabs far below the world so even if a stray
            // renderer ends up on the default layer it can't be seen.
            var stagePos = new Vector3(99999f, -99999f, 99999f);
            cam.transform.position = stagePos + new Vector3(0f, 5f, -10f);
            cam.transform.LookAt(stagePos);

            int loaded = 0;
            GameObject lastInstance = null;
            for (int i = 0; i < paths.Count; i++)
            {
                var prefab = Resources.Load<GameObject>(paths[i]);
                LoadingScreen.SetProgress(ProgressStart + ((float)i / total) * ProgressSpan);

                if (prefab != null)
                {
                    var instance = Object.Instantiate(prefab, stagePos, Quaternion.identity);
                    instance.name = $"Prewarm_{prefab.name}";
                    SetLayerRecursive(instance, PrewarmLayer);

                    // Render this prefab off-screen — the GPU pass triggers
                    // shader-variant compilation for whatever keywords the
                    // material set demands (shadows, lightmaps, fog, etc.).
                    cam.Render();

                    // Hold onto the last instantiated prefab for the dissolve
                    // shader prewarm pass below; destroy the rest.
                    if (i < paths.Count - 1) Object.Destroy(instance);
                    else lastInstance = instance;
                    loaded++;
                }

                // Yield so the loading screen UI can repaint and so we don't
                // jam compilation into a single mega-stall.
                yield return null;
            }

            // Dissolve / band shader prewarm — apply our custom level-up
            // shaders to a held-over prefab and render once, so their
            // variants are compiled now instead of stalling on the first
            // in-game level-up swap (which used to flash blue / pink while
            // URP compiled them inline).
            yield return PrewarmDissolveShaders(lastInstance, cam);

            if (lastInstance != null) Object.Destroy(lastInstance);

            cam.targetTexture = null;
            rt.Release();
            Object.Destroy(rt);
            Object.Destroy(camGo);

            LoadingScreen.SetProgress(ProgressEnd);
            LoadingScreen.SetStatus($"Warmed {loaded} prefab(s)");
            yield return null;
        }

        private static List<string> CollectPaths()
        {
            // ‘Resources/Prefabs/Buildings/…’ paths only — Resources.Load
            // handles the relative root.
            const string root = "Prefabs/Buildings/";
            var paths = new List<string>(64);

            // Base prefabs (no culture / no level)
            paths.Add(root + "Hall");
            paths.Add(root + "Barracks");
            paths.Add(root + "Hut");
            paths.Add(root + "GatherersHut");
            paths.Add(root + "House");

            // Per-culture, per-level swaps used by BuildingPrefabSwapSystem.
            for (int c = 0; c < CultureCodes.Length; c++)
            {
                var code = CultureCodes[c];
                for (int li = 0; li < Levels.Length; li++)
                {
                    int level = Levels[li];
                    paths.Add(root + $"Hall_{code}_{level}");
                    paths.Add(root + $"Barracks_{code}_{level}");
                    paths.Add(root + $"house_{code}_{level}");
                    foreach (var variant in HouseVariants)
                    {
                        if (variant > 0) paths.Add(root + $"house_{code}_{level}_{variant}");
                    }
                }
            }
            return paths;
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }

        // Force compilation of the level-up dissolve and wave-band shaders by
        // applying them to the held-over prefab and rendering once. Without
        // this, the FIRST in-game level-up swap stalls compiling these
        // shaders inline, briefly rendering the building with a fallback
        // material (the blue / pink "shader compiling" placeholder).
        private static IEnumerator PrewarmDissolveShaders(GameObject probe, Camera cam)
        {
            if (probe == null) yield break;

            LoadingScreen.SetStatus("Warming up dissolve effect…");

            var dissolveShader = Shader.Find("TheWaningBorder/BuildingLitDissolve");
            var bandShader     = Shader.Find("TheWaningBorder/BuildingWaveBand");

            // Track every Material instance we allocate so we can destroy
            // them after rendering — Unity won't GC stray Material assets.
            var allocated = new List<Material>(32);

            // ── Pass 1: Lit dissolve material on every Renderer ──────────
            if (dissolveShader != null)
            {
                var renderers = probe.GetComponentsInChildren<Renderer>(includeInactive: false);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var r = renderers[i];
                    if (r is ParticleSystemRenderer) continue;
                    var originals = r.sharedMaterials;
                    if (originals == null || originals.Length == 0) continue;

                    var swap = new Material[originals.Length];
                    for (int j = 0; j < originals.Length; j++)
                    {
                        var m = new Material(dissolveShader);
                        // Default the wave to mid-progress so both clipped
                        // and unclipped fragment branches compile.
                        m.SetFloat("_DissolveAmount", 0.5f);
                        swap[j] = m;
                        allocated.Add(m);
                    }
                    r.sharedMaterials = swap;
                }
                cam.Render();
                yield return null;
            }

            // ── Pass 2: Wave-band overlay on a single MeshFilter ─────────
            // Adds a child renderer using BuildingWaveBand so its variants
            // (additive transparent + ZTest LEqual) are compiled too.
            if (bandShader != null)
            {
                var anyMf = probe.GetComponentInChildren<MeshFilter>(includeInactive: false);
                if (anyMf != null && anyMf.sharedMesh != null)
                {
                    var bandGo = new GameObject("BandPrewarm");
                    bandGo.transform.SetParent(anyMf.transform, worldPositionStays: false);
                    bandGo.layer = PrewarmLayer;

                    var bf = bandGo.AddComponent<MeshFilter>();
                    bf.sharedMesh = anyMf.sharedMesh;

                    var br = bandGo.AddComponent<MeshRenderer>();
                    var bandMat = new Material(bandShader);
                    bandMat.SetFloat("_DissolveAmount", 0.5f);
                    bandMat.SetFloat("_Intensity", 1f);
                    br.sharedMaterial = bandMat;
                    allocated.Add(bandMat);

                    cam.Render();
                    Object.Destroy(bandGo);
                    yield return null;
                }
            }

            // Free the temporary Material instances.
            for (int i = 0; i < allocated.Count; i++)
                if (allocated[i] != null) Object.Destroy(allocated[i]);
        }
    }
}
