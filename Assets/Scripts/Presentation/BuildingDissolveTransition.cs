// BuildingDissolveTransition.cs
// Wave-driven level-up flourish.
//
// Spatial split (no temporal swap): the wave amount is a world-Y threshold.
//   • OLD mesh keeps its lit shading via BuildingLitDissolve(Inverted=0) —
//     pixels BELOW the wave are clipped, so the unrebuilt portion (above)
//     stays visible.
//   • NEW mesh uses BuildingLitDissolve(Inverted=1) — pixels ABOVE the wave
//     are clipped, so the rebuilt portion (below) stays visible.
//   • Together the two halves cover the full silhouette at every moment.
//
// On top of both, we additively overlay the BuildingWaveBand shader on a
// duplicate of each mesh — this is the bright seam between the halves and
// the fading trail behind it. The trail-only glow phase at the end gives the
// edge time to taper out before the old mesh and overlays are torn down.
//
// Location: Assets/Scripts/Presentation/BuildingDissolveTransition.cs

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public class BuildingDissolveTransition : MonoBehaviour
    {
        // Tuning ─────────────────────────────────────────────────────────
        private const float WaveDuration       = 1.5f;  // wave climbs 0 → 1
        private const float GlowFadeDuration   = 0.5f;  // rim glow tapers to 0
        private const float TotalDuration      = WaveDuration + GlowFadeDuration;

        private const float WaveSpanPad        = 0.6f;
        private const float NoiseScale         = 4.5f;
        private const float NoiseStrength      = 0.30f;
        private const float AmountPad          = 0.25f;
        private const float BandWidth          = 0.06f;
        private const float TrailLength        = 0.20f;
        private const float PeakIntensity      = 1.0f;
        // ─────────────────────────────────────────────────────────────────

        public static void Begin(GameObject oldGo, GameObject newGo, float duration, Color edgeColor)
        {
            if (newGo == null) { if (oldGo != null) Object.Destroy(oldGo); return; }

            float originY, span;
            ComputeWaveBounds(newGo, oldGo, out originY, out span);

            var driverGo = new GameObject($"Dissolve_{newGo.name}");
            driverGo.transform.position = newGo.transform.position;
            var driver = driverGo.AddComponent<BuildingDissolveTransition>();
            driver._oldGo     = oldGo;
            driver._newGo     = newGo;
            driver._edgeColor = edgeColor;
            driver._originY   = originY;
            driver._span      = span;
            driver.Init();
        }

        private float _elapsed;
        private GameObject _oldGo;
        private GameObject _newGo;
        private Color _edgeColor;
        private float _originY;
        private float _span;

        private struct RendererBinding
        {
            public Renderer Renderer;
            public Material[] Originals;
            public Material[] Instances;
        }
        private readonly List<RendererBinding> _newBindings = new();
        private readonly List<RendererBinding> _oldBindings = new();

        private readonly List<GameObject> _overlays    = new();
        private readonly List<Material>   _overlayMats = new();

        private static Shader _litDissolveShader;
        private static Shader LitDissolveShader =>
            _litDissolveShader != null ? _litDissolveShader : (_litDissolveShader = Shader.Find("TheWaningBorder/BuildingLitDissolve"));

        private static Shader _bandShader;
        private static Shader BandShader =>
            _bandShader != null ? _bandShader : (_bandShader = Shader.Find("TheWaningBorder/BuildingWaveBand"));

        private void Init()
        {
            if (LitDissolveShader == null || BandShader == null)
            {
                Debug.LogWarning("[BuildingDissolveTransition] Missing dissolve/band shader — falling back to instant swap.");
                if (_oldGo != null) Object.Destroy(_oldGo);
                Object.Destroy(gameObject);
                return;
            }

            if (_oldGo != null)
                foreach (var col in _oldGo.GetComponentsInChildren<Collider>())
                    col.enabled = false;

            BindLitDissolve(_oldGo, inverted: 0f, _oldBindings);
            BindLitDissolve(_newGo, inverted: 1f, _newBindings);

            BuildOverlayOn(_oldGo);
            BuildOverlayOn(_newGo);

            ApplyWave(0f, 1f);
        }

        private void BindLitDissolve(GameObject root, float inverted, List<RendererBinding> bindings)
        {
            if (root == null) return;
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
            foreach (var r in renderers)
            {
                if (r is ParticleSystemRenderer) continue;
                var originals = r.sharedMaterials;
                if (originals == null || originals.Length == 0) continue;

                var instances = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                {
                    var src = originals[i];
                    var mat = new Material(LitDissolveShader);
                    mat.name = $"LitDissolve({(src != null ? src.name : "null")})";

                    // Capture the colour via Unity's MainColor accessor —
                    // resolves to URP-Lit's _BaseColor or Standard's _Color
                    // automatically.
                    Color baseCol = (src != null) ? src.color : Color.white;
                    mat.SetColor("_BaseColor", baseCol);

                    // Surface Inputs > Base Map (the standard URP-Lit albedo
                    // slot). Pull both texture and ST across.
                    if (src != null && src.HasProperty("_BaseMap"))
                    {
                        var baseTex = src.GetTexture("_BaseMap");
                        if (baseTex != null)
                        {
                            mat.SetTexture("_BaseMap", baseTex);
                            mat.SetFloat("_UseBaseMap", 1f);
                            if (src.HasProperty("_BaseMap_ST"))
                                mat.SetVector("_BaseMap_ST",
                                    src.GetVector("_BaseMap_ST"));
                            else
                                mat.SetVector("_BaseMap_ST",
                                    new Vector4(1, 1, 0, 0));
                        }
                    }

                    // Detail Inputs > Base Map (URP-Lit's ×2 detail-overlay
                    // slot). Kept as a fallback so any materials still using
                    // detail-map workflow continue to render correctly.
                    if (src != null && src.HasProperty("_DetailAlbedoMap"))
                    {
                        var detailTex = src.GetTexture("_DetailAlbedoMap");
                        if (detailTex != null)
                        {
                            mat.SetTexture("_DetailAlbedoMap", detailTex);
                            mat.SetFloat("_UseDetailMap", 1f);
                            if (src.HasProperty("_DetailAlbedoMap_ST"))
                                mat.SetVector("_DetailAlbedoMap_ST",
                                    src.GetVector("_DetailAlbedoMap_ST"));
                            else
                                mat.SetVector("_DetailAlbedoMap_ST",
                                    new Vector4(1, 1, 0, 0));
                        }
                    }

                    // Carry metallic / smoothness across so UniversalFragmentPBR
                    // uses the same fresnel / specular-environment response as
                    // the source URP-Lit material, keeping the lit look
                    // seamless across the transition.
                    if (src != null)
                    {
                        if (src.HasProperty("_Metallic"))
                            mat.SetFloat("_Metallic", src.GetFloat("_Metallic"));
                        if (src.HasProperty("_Smoothness"))
                            mat.SetFloat("_Smoothness", src.GetFloat("_Smoothness"));
                        else if (src.HasProperty("_Glossiness"))
                            mat.SetFloat("_Smoothness", src.GetFloat("_Glossiness"));
                    }

                    mat.SetFloat("_Inverted",      inverted);
                    mat.SetFloat("_WaveOriginY",   _originY);
                    mat.SetFloat("_WaveSpan",      _span);
                    mat.SetFloat("_NoiseScale",    NoiseScale);
                    mat.SetFloat("_NoiseStrength", NoiseStrength);
                    mat.SetFloat("_AmountPad",     AmountPad);
                    mat.SetFloat("_DissolveAmount", 0f);

                    instances[i] = mat;
                }

                bool keepOriginals = (root == _newGo);
                bindings.Add(new RendererBinding
                {
                    Renderer  = r,
                    Originals = keepOriginals ? originals : null,
                    Instances = instances,
                });
                r.sharedMaterials = instances;
            }
        }

        private void BuildOverlayOn(GameObject root)
        {
            if (root == null) return;
            var meshFilters = root.GetComponentsInChildren<MeshFilter>(includeInactive: false);
            for (int i = 0; i < meshFilters.Length; i++)
            {
                var mf = meshFilters[i];
                if (mf == null || mf.sharedMesh == null) continue;
                var srcRenderer = mf.GetComponent<MeshRenderer>();
                if (srcRenderer == null) continue;

                var overlayGo = new GameObject($"WaveOverlay_{mf.name}");
                overlayGo.transform.SetParent(mf.transform, worldPositionStays: false);

                var of = overlayGo.AddComponent<MeshFilter>();
                of.sharedMesh = mf.sharedMesh;

                var or = overlayGo.AddComponent<MeshRenderer>();
                or.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
                or.receiveShadows       = false;
                or.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
                or.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                var mat = new Material(BandShader);
                mat.name = $"WaveBand({mf.name})";
                mat.SetColor("_EdgeColor",     _edgeColor);
                mat.SetFloat("_WaveOriginY",   _originY);
                mat.SetFloat("_WaveSpan",      _span);
                mat.SetFloat("_NoiseScale",    NoiseScale);
                mat.SetFloat("_NoiseStrength", NoiseStrength);
                mat.SetFloat("_BandWidth",     BandWidth);
                mat.SetFloat("_TrailLength",   TrailLength);
                mat.SetFloat("_Intensity",     PeakIntensity);
                mat.SetFloat("_DissolveAmount", 0f);

                or.sharedMaterial = mat;
                _overlays.Add(overlayGo);
                _overlayMats.Add(mat);
            }
        }

        private void ApplyWave(float amount, float intensity)
        {
            for (int i = 0; i < _oldBindings.Count; i++)
                foreach (var m in _oldBindings[i].Instances)
                    if (m != null) m.SetFloat("_DissolveAmount", amount);

            for (int i = 0; i < _newBindings.Count; i++)
                foreach (var m in _newBindings[i].Instances)
                    if (m != null) m.SetFloat("_DissolveAmount", amount);

            float bandIntensity = PeakIntensity * intensity;
            for (int i = 0; i < _overlayMats.Count; i++)
            {
                var m = _overlayMats[i];
                if (m == null) continue;
                m.SetFloat("_DissolveAmount", amount);
                m.SetFloat("_Intensity",      bandIntensity);
            }
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;

            float waveT     = Mathf.Clamp01(_elapsed / WaveDuration);
            float waveEased = waveT * waveT * (3f - 2f * waveT);

            float postT     = Mathf.Clamp01((_elapsed - WaveDuration) / GlowFadeDuration);
            float intensity = 1f - (postT * postT);

            ApplyWave(waveEased, intensity);

            if (_elapsed >= TotalDuration) Cleanup();
        }

        private void Cleanup()
        {
            for (int i = 0; i < _newBindings.Count; i++)
            {
                var b = _newBindings[i];
                if (b.Renderer != null && b.Originals != null)
                    b.Renderer.sharedMaterials = b.Originals;
                DestroyMaterials(b.Instances);
            }
            for (int i = 0; i < _oldBindings.Count; i++)
                DestroyMaterials(_oldBindings[i].Instances);

            for (int i = 0; i < _overlays.Count; i++)
                if (_overlays[i] != null) Object.Destroy(_overlays[i]);
            for (int i = 0; i < _overlayMats.Count; i++)
                if (_overlayMats[i] != null) Object.Destroy(_overlayMats[i]);

            _newBindings.Clear();
            _oldBindings.Clear();
            _overlays.Clear();
            _overlayMats.Clear();

            if (_oldGo != null) Object.Destroy(_oldGo);
            Object.Destroy(gameObject);
        }

        private static void DestroyMaterials(Material[] mats)
        {
            if (mats == null) return;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null) Object.Destroy(mats[i]);
        }

        private static void ComputeWaveBounds(GameObject newGo, GameObject oldGo, out float originY, out float span)
        {
            bool seeded = false;
            Bounds b = default;

            void Encapsulate(GameObject root)
            {
                if (root == null) return;
                var rs = root.GetComponentsInChildren<Renderer>(includeInactive: false);
                for (int i = 0; i < rs.Length; i++)
                {
                    if (rs[i] is ParticleSystemRenderer) continue;
                    if (!seeded) { b = rs[i].bounds; seeded = true; }
                    else b.Encapsulate(rs[i].bounds);
                }
            }

            Encapsulate(newGo);
            Encapsulate(oldGo);

            if (!seeded)
            {
                originY = (newGo != null ? newGo.transform.position.y : 0f);
                span    = 4f;
                return;
            }
            originY = b.min.y;
            span    = Mathf.Max(b.size.y + WaveSpanPad, 1f);
        }
    }
}
