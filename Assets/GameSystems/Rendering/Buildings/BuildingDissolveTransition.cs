// BuildingDissolveTransition.cs
// Wave-driven level-up transition.
//
// Spatial split (no temporal swap): the wave amount is a world-Y threshold.
//   • OLD mesh wears BuildingLitDissolve(Inverted=0) — pixels BELOW the
//     wave are clipped, so it dissolves bottom-up and its top stays.
//   • NEW mesh wears BuildingLitDissolve(Inverted=1) — pixels ABOVE the
//     wave are clipped, so it is revealed bottom-up as the wave passes and
//     is never seen ahead of it.
//   • Together the two halves cover the full silhouette at every moment.
//
// THE DISSOLVE MATERIAL IS THE SOURCE MATERIAL'S LOOK (2026-10-30).
// Developer: "no lighting or texture change other than the dissolve … must
// not show lightless buildings, lose lighting, lose texture detail". The
// wave material used to carry the albedo alone, so both halves lost their
// normal maps and read as flat, unlit paint for two seconds. BindLitDissolve
// now carries albedo, normal map, emission, occlusion, metallic and
// smoothness across (whatever slot names the source shader uses), and the
// shader shades them through URP's own PBR, so a revealed or dissolving
// half is lit and detailed exactly as the finished building; the originals
// come back on the new view when the wave is done.
//
// On top of both, we additively overlay the BuildingWaveBand shader on a
// duplicate of each mesh — the bright seam at the wave and the fading trail
// behind it. The trail-only glow phase at the end gives the edge time to
// taper out before the old mesh and the overlays are torn down. The old
// view is DEACTIVATED before its materials are destroyed: destroying them
// first left it drawn with no material for one frame (the end flicker).

using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.Rendering
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
            Begin(oldGo, newGo, duration, edgeColor, destroyOldOnComplete: true);
        }

        /// <summary>
        /// destroyOldOnComplete = false runs the same wave but DEACTIVATES the
        /// old visual at the end (original materials restored, colliders
        /// re-enabled) instead of destroying it — used when old and new are
        /// sibling BRANCHES inside one multi-variant prefab
        /// (BuildingVariantVisual) rather than two separate instances.
        /// </summary>
        public static void Begin(GameObject oldGo, GameObject newGo, float duration, Color edgeColor,
            bool destroyOldOnComplete)
        {
            if (newGo == null)
            {
                if (oldGo != null)
                {
                    if (destroyOldOnComplete) Object.Destroy(oldGo);
                    else oldGo.SetActive(false);
                }
                return;
            }

            float originY, span;
            ComputeWaveBounds(newGo, oldGo, out originY, out span);

            var driverGo = new GameObject($"Dissolve_{newGo.name}");
            driverGo.transform.position = newGo.transform.position;
            var driver = driverGo.AddComponent<BuildingDissolveTransition>();
            driver._oldGo      = oldGo;
            driver._newGo      = newGo;
            driver._edgeColor  = edgeColor;
            driver._originY    = originY;
            driver._span       = span;
            driver._destroyOld = destroyOldOnComplete;
            driver.Init();
        }

        private float _elapsed;
        private GameObject _oldGo;
        private GameObject _newGo;
        private Color _edgeColor;
        private float _originY;
        private float _span;
        private bool _destroyOld = true;

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

        // Both shaders live under Assets/Shaders/Resources/ and are loaded by
        // file name. No material or scene references them, so before the move
        // a player build stripped them and Shader.Find returned null — which
        // silently demoted every building transition to the instant-swap
        // fallback in Init(). Resources/ assets ship unconditionally.
        // Shader.Find stays as a fallback so a rename degrades, not breaks.
        private static Shader _litDissolveShader;
        private static Shader LitDissolveShader =>
            _litDissolveShader != null
                ? _litDissolveShader
                : (_litDissolveShader = Resources.Load<Shader>("BuildingLitDissolve")
                                        ?? Shader.Find("TheWaningBorder/BuildingLitDissolve"));

        private static Shader _bandShader;
        private static Shader BandShader =>
            _bandShader != null
                ? _bandShader
                : (_bandShader = Resources.Load<Shader>("BuildingWaveBand")
                                 ?? Shader.Find("TheWaningBorder/BuildingWaveBand"));

        private void Init()
        {
            if (LitDissolveShader == null || BandShader == null)
            {
                Debug.LogWarning("[BuildingDissolveTransition] Missing dissolve/band shader — falling back to instant swap.");
                if (_oldGo != null)
                {
                    if (_destroyOld) Object.Destroy(_oldGo);
                    else _oldGo.SetActive(false);
                }
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

                    // Capture the colour WITHOUT Material.color — its getter
                    // hard-requires a "_Color" property and spams the log for
                    // any shader that lacks it (e.g. TheWaningBorder/Building-
                    // Damage). Resolve URP-Lit's _BaseColor first, then legacy
                    // _Color, else white.
                    Color baseCol = Color.white;
                    if (src != null)
                    {
                        if (src.HasProperty("_BaseColor")) baseCol = src.GetColor("_BaseColor");
                        else if (src.HasProperty("_Color")) baseCol = src.GetColor("_Color");
                    }
                    mat.SetColor("_BaseColor", baseCol);

                    // The albedo, whatever slot the source shader keeps it in:
                    // URP-Lit's _BaseMap, the legacy _MainTex, or Synty's
                    // _Albedo_Map (Generic_Basic shader graph). Only _BaseMap
                    // was read until 2026-10-30, so a building assembled from
                    // raw Synty pieces dissolved as flat white: the texture
                    // never made it onto the wave material.
                    var baseTex = BuildingFactionColorMarker.AlbedoOf(src, out Vector4 baseST);
                    if (baseTex != null)
                    {
                        mat.SetTexture("_BaseMap", baseTex);
                        mat.SetFloat("_UseBaseMap", 1f);
                        mat.SetVector("_BaseMap_ST", baseST);
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

                    // Normal map, emission and occlusion too (2026-10-30): the
                    // relief of tiles and planks, a lit window, a shaded
                    // corner — without them the dissolving building read as
                    // flat, unlit paint.
                    var normalMap = BuildingFactionColorMarker.NormalMapOf(src, out float normalScale);
                    if (normalMap != null)
                    {
                        mat.SetTexture("_BumpMap", normalMap);
                        mat.SetFloat("_BumpScale", normalScale);
                        mat.SetFloat("_UseBumpMap", 1f);
                    }
                    var emissionMap = BuildingFactionColorMarker.EmissionOf(src, out Color emissionColor);
                    mat.SetColor("_EmissionColor", emissionColor);
                    if (emissionMap != null)
                    {
                        mat.SetTexture("_EmissionMap", emissionMap);
                        mat.SetFloat("_UseEmissionMap", 1f);
                    }
                    var occlusionMap = BuildingFactionColorMarker.OcclusionOf(src);
                    if (occlusionMap != null)
                    {
                        mat.SetTexture("_OcclusionMap", occlusionMap);
                        mat.SetFloat("_UseOcclusionMap", 1f);
                    }

                    // Player colour rides on _TWB_FactionHSV, not the albedo.
                    BuildingFactionColorMarker.CopyFactionProperties(src, mat);

                    mat.SetFloat("_Inverted",      inverted);
                    mat.SetFloat("_WaveOriginY",   _originY);
                    mat.SetFloat("_WaveSpan",      _span);
                    mat.SetFloat("_NoiseScale",    NoiseScale);
                    mat.SetFloat("_NoiseStrength", NoiseStrength);
                    mat.SetFloat("_AmountPad",     AmountPad);
                    mat.SetFloat("_DissolveAmount", 0f);

                    instances[i] = mat;
                }

                // The old visual's originals only matter when it survives the
                // transition (deactivate mode) — a destroyed instance never
                // renders again. The new visual's always come back.
                bool keepOriginals = (root == _newGo) || !_destroyOld;
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
            // The old view goes dark BEFORE its wave materials are destroyed.
            // Object.Destroy is deferred to the end of the frame, so destroying
            // the materials first left the old renderers drawn once with no
            // material at all — the previous level flickering back for a frame
            // at the very end of every upgrade.
            if (_oldGo != null) _oldGo.SetActive(false);

            for (int i = 0; i < _newBindings.Count; i++)
            {
                var b = _newBindings[i];
                if (b.Renderer != null && b.Originals != null)
                    b.Renderer.sharedMaterials = b.Originals;
                DestroyMaterials(b.Instances);
            }
            for (int i = 0; i < _oldBindings.Count; i++)
            {
                var b = _oldBindings[i];
                // Deactivate mode: the old branch may be shown again some day
                // — hand its original materials back before hiding it.
                if (!_destroyOld && b.Renderer != null && b.Originals != null)
                    b.Renderer.sharedMaterials = b.Originals;
                DestroyMaterials(b.Instances);
            }

            // Put the owner's colour back on whatever is left standing: the
            // wave handed the captured originals back, and that restore is the
            // last word on the materials, so the recolour is re-asserted or the
            // upgraded building could finish wearing its authored blue. In the
            // multi-variant case old and new are two BRANCHES of one prefab,
            // so a single Reapply from the shared root covers both.
            BuildingFactionColorMarker.Reapply(_newGo);
            if (!_destroyOld && _oldGo != null && !SharesColorRoot(_oldGo, _newGo))
                BuildingFactionColorMarker.Reapply(_oldGo);

            for (int i = 0; i < _overlays.Count; i++)
                if (_overlays[i] != null) Object.Destroy(_overlays[i]);
            for (int i = 0; i < _overlayMats.Count; i++)
                if (_overlayMats[i] != null) Object.Destroy(_overlayMats[i]);

            _newBindings.Clear();
            _oldBindings.Clear();
            _overlays.Clear();
            _overlayMats.Clear();

            if (_oldGo != null)
            {
                if (_destroyOld)
                {
                    Object.Destroy(_oldGo);
                }
                else
                {
                    foreach (var col in _oldGo.GetComponentsInChildren<Collider>())
                        col.enabled = true;
                }
            }
            Object.Destroy(gameObject);
        }

        private static bool SharesColorRoot(GameObject a, GameObject b)
        {
            if (a == null || b == null) return false;
            var sa = a.GetComponentInParent<BuildingFactionColorStamp>(includeInactive: true);
            if (sa == null) return false;
            return sa == b.GetComponentInParent<BuildingFactionColorStamp>(includeInactive: true);
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
