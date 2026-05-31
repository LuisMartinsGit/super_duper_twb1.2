// DayNightCycle.cs
// (Day/night cycle removed — the game now stays in a single atmospheric
//  preset: dark-blue volcanic, well-lit. This MonoBehaviour kept its
//  name so GameBootstrap and any inspector references still resolve.)
//
// Responsibilities now:
//   - Configure a single directional sun light with a cool blueish tone
//     and enough intensity that the play area reads clearly.
//   - Set ambient + fog (volumetric fake) for a dark moody backdrop.
//   - Set up post-processing tint, vignette, and bloom on the global
//     URP volume so the screen has a deep blue-volcanic mood.
//
// Cloud-shadow projector retained because it adds depth, but is fixed
// (no day-fade, no cloud-shadow opacity ramp).

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace TheWaningBorder.World
{
    public class DayNightCycle : MonoBehaviour
    {
        // Defaults follow the "Alanthor post-processing + lighting pass" recipe
        // (magic-hour sun, neutral ambient, cool grey fog, mild post). The old
        // "blue-volcanic" tuning compounded sun tint × bloom tint × colour
        // filter × white balance × dark vignette × negative exposure into a
        // near-black, desaturated image. Reset to recipe; faction colours
        // stay vibrant because no global hue rotation is applied — only the
        // ShadowsMidtonesHighlights split-tone shapes colour.

        [Header("Sun (Step 2: magic-hour rake)")]
        [Tooltip("Sun pitch — angle from horizon. Recipe: 50° for long shadows + dramatic contour.")]
        public float sunPitch = 50f;
        [Tooltip("Sun heading (compass) in degrees. Recipe: -30° matches the post-process article.")]
        public float sunHeading = -30f;
        [Tooltip("Sun colour. Recipe: warm off-white #FFF4E0.")]
        public Color sunColor = new(1.0f, 0.957f, 0.878f);
        [Tooltip("Sun intensity. Recipe: 1.2-1.5. Pushed to top of range because this scene has no baked GI to fill shadows.")]
        [Range(0f, 3f)] public float sunIntensity = 1.5f;

        [Header("Ambient (Trilight gradient — no bake needed)")]
        // Why Trilight not Skybox: AmbientMode.Skybox samples the skybox into
        // SH coefficients AT BAKE TIME. Game.unity has m_LightingDataAsset set
        // to the empty default → no baked SH → ambient probe is near-zero →
        // every surface that isn't directly sun-lit renders pure black. Trilight
        // uses the three explicit colours below at runtime with no bake.
        [Tooltip("Sky colour — fills upward-facing surfaces. Bright warm-neutral.")]
        public Color ambientSkyColor = new(0.70f, 0.75f, 0.80f);
        [Tooltip("Equator colour — fills horizontal/side-facing surfaces.")]
        public Color ambientEquatorColor = new(0.50f, 0.50f, 0.50f);
        [Tooltip("Ground colour — fills downward-facing surfaces. Warm earth tone.")]
        public Color ambientGroundColor = new(0.30f, 0.28f, 0.22f);

        [Header("Fog (Step 3: atmospheric depth)")]
        [Tooltip("Fog colour. Recipe Alanthor cool grey-blue #B8C5D6, or warm sandy #E8D8B8 for sunlit.")]
        public Color fogColor = new(0.722f, 0.773f, 0.839f);
        [Tooltip("Exponential-squared fog density. Recipe: start at 0.005, tune until distant terrain fades.")]
        [Range(0f, 0.05f)] public float fogDensity = 0.005f;

        [Header("Post-Processing (Step 1: URP global volume)")]
        [Tooltip("Vignette intensity. Recipe: 0.25 — dropped to 0.18 here so corners don't read as darkness on this scene.")]
        [Range(0f, 1f)] public float vignetteIntensity = 0.18f;
        [Tooltip("Vignette colour. Recipe: near-black.")]
        public Color vignetteColor = new(0f, 0f, 0f);
        [Tooltip("Vignette smoothness. Recipe: 0.4.")]
        [Range(0.01f, 1f)] public float vignetteSmoothness = 0.4f;
        [Tooltip("Bloom intensity. Recipe: 0.4-0.8 — makes crystals + lit windows glow.")]
        [Range(0f, 5f)] public float bloomIntensity = 0.6f;
        [Tooltip("Bloom threshold. Recipe: 1.1 — only true HDR-bright pixels bloom (not faction colours).")]
        [Range(0f, 2f)] public float bloomThreshold = 1.1f;
        [Tooltip("Post-exposure. Recipe: 0 (no global darkening).")]
        [Range(-3f, 3f)] public float postExposure = 0f;
        [Tooltip("Saturation. Recipe: +10. Keeps faction colours vibrant.")]
        [Range(-100f, 100f)] public float saturation = 10f;
        [Tooltip("Contrast. Recipe: +15.")]
        [Range(-100f, 100f)] public float contrast = 15f;

        [Header("Shadows / Midtones / Highlights (Step 1: cinematic split-tone)")]
        [Tooltip("Cool tint applied to shadow luminance. Recipe: slightly blue-ish.")]
        public Color smhShadowsTint = new(0.92f, 0.96f, 1.05f);
        [Tooltip("Warm tint applied to highlight luminance. Recipe: slightly orange-ish.")]
        public Color smhHighlightsTint = new(1.05f, 1.00f, 0.92f);

        [Header("Film Grain (Step 1)")]
        [Tooltip("Film grain intensity. Recipe: 0.15 — subtle texture, hides aliasing.")]
        [Range(0f, 1f)] public float filmGrainIntensity = 0.15f;
        [Tooltip("Film grain response curve. Recipe: 0.8.")]
        [Range(0f, 1f)] public float filmGrainResponse = 0.8f;

        [Header("Shadows")]
        [Tooltip("Shadow draw distance in world units")]
        public float shadowDistance = 300f;

        [Header("Cloud Shadows")]
        [Tooltip("Enable static cloud shadow projector for depth")]
        public bool cloudShadows = true;
        [Range(0f, 1f)] public float cloudOpacity = 0.30f;
        public float cloudSpeed = 2f;
        public float cloudScale = 0.008f;
        public float cloudProjectorSize = 300f;

        // ── Runtime ──
        private Light _sun;
        private Volume _volume;
        private GameObject _cloudProjector;
        private Material _cloudMaterial;
        private Mesh _cloudMesh;
        private Texture2D _cloudTexture;
        private float _cloudOffsetX;
        private float _cloudOffsetZ;
        private Camera _mainCamera;

        // Cached override component refs — populated once in
        // EnsurePostProcessingVolume, then re-pushed every frame by
        // ApplyPostProcessingValues so inspector knobs are live-tunable
        // during Play mode instead of being frozen at Awake-time values.
        // No WhiteBalance / no global colour filter / no bloom tint — the
        // SMH split-tone is the only thing shaping colour, so faction
        // colours don't get crushed by stacked hue rotations.
        private Vignette _vignetteOverride;
        private Bloom _bloomOverride;
        private ColorAdjustments _colorOverride;
        private ShadowsMidtonesHighlights _smhOverride;
        private FilmGrain _grainOverride;

        void Awake()
        {
            CreateOrFindSun();
            ConfigureShadows();
            ApplyStaticAtmosphere();
            EnsurePostProcessingVolume();
            ApplyPostProcessingValues();
            _mainCamera = Camera.main;
            EnableCameraPostProcessing(_mainCamera);
        }

        void Update()
        {
            // Push current inspector values into the cached overrides so
            // tuning at Play time takes effect. Cheap — a handful of float
            // assignments per frame.
            ApplyPostProcessingValues();

            // Camera.main can become non-null on a later frame (lobby →
            // game transitions, scene reloads). Re-acquire and enable PP
            // when we first see it.
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera != null) EnableCameraPostProcessing(_mainCamera);
            }

            // No cycle — just drift the cloud texture for life.
            if (cloudShadows)
                UpdateCloudShadows();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            // When the user types into an inspector field, push values
            // immediately instead of waiting for the next Update tick.
            // Null-guarded for edit-mode (volume not built yet).
            if (_volume != null) ApplyPostProcessingValues();
        }
#endif

        private void CreateOrFindSun()
        {
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in lights)
            {
                if (l.type == LightType.Directional)
                {
                    _sun = l;
                    break;
                }
            }

            if (_sun == null)
            {
                var sunGO = new GameObject("Sun_DirectionalLight");
                _sun = sunGO.AddComponent<Light>();
                _sun.type = LightType.Directional;
            }

            _sun.shadows = LightShadows.Soft;
            _sun.shadowStrength = 0.8f;  // Recipe Step 2
            _sun.shadowNormalBias = 0.4f;
            _sun.shadowBias = 0.05f;
        }

        private void ConfigureShadows()
        {
            QualitySettings.shadowDistance = shadowDistance;

            var rpAsset = GraphicsSettings.currentRenderPipeline;
            if (rpAsset != null)
            {
                var sdField = rpAsset.GetType().GetProperty("shadowDistance",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (sdField != null && sdField.CanWrite)
                    sdField.SetValue(rpAsset, shadowDistance);

                var cascadeField = rpAsset.GetType().GetProperty("shadowCascadeCount",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (cascadeField != null && cascadeField.CanWrite)
                    cascadeField.SetValue(rpAsset, 4);
            }
        }

        /// <summary>Recipe Step 2 + 3: sun, Trilight ambient gradient, fog.</summary>
        private void ApplyStaticAtmosphere()
        {
            _sun.transform.rotation = Quaternion.Euler(sunPitch, sunHeading, 0f);
            _sun.color = sunColor;
            _sun.intensity = sunIntensity;

            // Trilight ambient — explicit sky / equator / ground colours.
            // Recipe-suggested alternative to Skybox source; chosen here because
            // Game.unity has no baked lighting data, so Skybox SH would be ~0.
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSkyColor;
            RenderSettings.ambientEquatorColor = ambientEquatorColor;
            RenderSettings.ambientGroundColor = ambientGroundColor;
            // Belt-and-suspenders: refresh the runtime ambient probe so the
            // change propagates to renderers that cache it.
            DynamicGI.UpdateEnvironment();

            // Fog Step 3.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
        }

        /// <summary>
        /// Build (or reuse) a global URP Volume and register every override
        /// component the scene uses. Values are NOT written here — call
        /// ApplyPostProcessingValues() to push current inspector fields into
        /// the overrides. This split lets the inspector knobs stay live at
        /// Play time without rebuilding the profile each frame.
        /// </summary>
        private void EnsurePostProcessingVolume()
        {
            _volume = GetComponent<Volume>();
            if (_volume == null)
                _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            _volume.weight = 1f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "TWB_StaticAtmosphereProfile";

            _vignetteOverride = profile.Add<Vignette>(true);
            _bloomOverride    = profile.Add<Bloom>(true);
            _colorOverride    = profile.Add<ColorAdjustments>(true);
            _smhOverride      = profile.Add<ShadowsMidtonesHighlights>(true);
            _grainOverride    = profile.Add<FilmGrain>(true);

            // Tonemapping never changes from the inspector — set once here.
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            // NOTE: WhiteBalance is deliberately not registered. Combined
            // with SMH split-tone and a tinted sun colour it produced a
            // triple-cool image that crushed faction colours.

            _volume.sharedProfile = profile;
        }

        /// <summary>
        /// Push current inspector field values into the cached override
        /// components. Cheap — only float / Vector4 assignments. Called from
        /// Update so Play-mode inspector tweaks take effect immediately.
        /// </summary>
        private void ApplyPostProcessingValues()
        {
            if (_vignetteOverride != null)
            {
                _vignetteOverride.intensity.Override(vignetteIntensity);
                _vignetteOverride.color.Override(vignetteColor);
                _vignetteOverride.smoothness.Override(vignetteSmoothness);
                _vignetteOverride.rounded.Override(false);
            }

            if (_bloomOverride != null)
            {
                _bloomOverride.intensity.Override(bloomIntensity);
                _bloomOverride.threshold.Override(bloomThreshold);
                _bloomOverride.scatter.Override(0.7f);
                // Bloom tint left at white. A tinted bloom on top of SMH
                // and the sun colour stacks into a global hue shift.
                _bloomOverride.tint.Override(Color.white);
            }

            if (_colorOverride != null)
            {
                _colorOverride.postExposure.Override(postExposure);
                _colorOverride.saturation.Override(saturation);
                _colorOverride.contrast.Override(contrast);
                // colorFilter left neutral. Any tint here multiplies every
                // pixel — fastest way to crush faction reds/greens/blues.
                _colorOverride.colorFilter.Override(Color.white);
            }

            if (_smhOverride != null)
            {
                _smhOverride.shadows.Override(new Vector4(smhShadowsTint.r, smhShadowsTint.g, smhShadowsTint.b, 0f));
                _smhOverride.highlights.Override(new Vector4(smhHighlightsTint.r, smhHighlightsTint.g, smhHighlightsTint.b, 0f));
            }

            if (_grainOverride != null)
            {
                _grainOverride.intensity.Override(filmGrainIntensity);
                _grainOverride.response.Override(filmGrainResponse);
            }
        }

        /// <summary>
        /// URP cameras default renderPostProcessing=false; without flipping
        /// this flag the global Volume is built but the camera silently
        /// ignores it. The camera is created at runtime in CameraController
        /// without ever touching this flag, so we enable it here from the
        /// canonical post-process owner.
        /// </summary>
        private void EnableCameraPostProcessing(Camera cam)
        {
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
        }

        private void UpdateCloudShadows()
        {
            _cloudOffsetX += cloudSpeed * Time.deltaTime;
            _cloudOffsetZ += cloudSpeed * 0.3f * Time.deltaTime;

            if (_cloudProjector == null)
                CreateCloudProjector();

            if (_cloudMaterial != null)
            {
                _cloudMaterial.SetFloat("_OffsetX", _cloudOffsetX);
                _cloudMaterial.SetFloat("_OffsetZ", _cloudOffsetZ);
                _cloudMaterial.SetFloat("_Opacity", cloudOpacity);
            }

            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera != null)
            {
                var camPos = _mainCamera.transform.position;
                _cloudProjector.transform.position = new Vector3(camPos.x, 200f, camPos.z);
            }
        }

        private void CreateCloudProjector()
        {
            _cloudProjector = new GameObject("CloudShadowProjector");
            _cloudProjector.transform.SetParent(transform);

            var mf = _cloudProjector.AddComponent<MeshFilter>();
            var mr = _cloudProjector.AddComponent<MeshRenderer>();

            _cloudMesh = new Mesh();
            float half = cloudProjectorSize;
            _cloudMesh.vertices = new Vector3[]
            {
                new(-half, 0, -half), new(half, 0, -half),
                new(half, 0, half), new(-half, 0, half)
            };
            _cloudMesh.uv = new Vector2[]
            {
                new(0, 0), new(1, 0), new(1, 1), new(0, 1)
            };
            _cloudMesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            _cloudMesh.RecalculateNormals();
            mf.mesh = _cloudMesh;

            int res = 512;
            _cloudTexture = new Texture2D(res, res, TextureFormat.RGBA32, true);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float u = (float)x / res;
                    float v = (float)y / res;

                    float n = Mathf.PerlinNoise(u * 8f + 50f, v * 8f + 50f) * 0.5f
                            + Mathf.PerlinNoise(u * 16f + 100f, v * 16f + 100f) * 0.3f
                            + Mathf.PerlinNoise(u * 32f + 200f, v * 32f + 200f) * 0.2f;

                    float shadow = Mathf.SmoothStep(0f, 1f, (n - 0.4f) * 3f);
                    _cloudTexture.SetPixel(x, y, new Color(0f, 0f, 0f, shadow));
                }
            }
            _cloudTexture.Apply();
            _cloudTexture.wrapMode = TextureWrapMode.Repeat;
            _cloudTexture.filterMode = FilterMode.Bilinear;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Transparent");
            _cloudMaterial = new Material(shader);
            _cloudMaterial.mainTexture = _cloudTexture;
            _cloudMaterial.color = new Color(0f, 0f, 0f, cloudOpacity);

            _cloudMaterial.SetFloat("_Surface", 1);
            _cloudMaterial.SetFloat("_Blend", 0);
            _cloudMaterial.SetOverrideTag("RenderType", "Transparent");
            _cloudMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.DstColor);
            _cloudMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            _cloudMaterial.SetInt("_ZWrite", 0);
            _cloudMaterial.renderQueue = 3000;
            _cloudMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            mr.material = _cloudMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _cloudProjector.transform.position = new Vector3(0, 200f, 0);
            _cloudProjector.transform.rotation = Quaternion.Euler(0, 0, 0);
        }

        void OnDestroy()
        {
            if (_cloudProjector != null) Destroy(_cloudProjector);
            if (_cloudMesh != null) Destroy(_cloudMesh);
            if (_cloudTexture != null) Destroy(_cloudTexture);
            if (_cloudMaterial != null) Destroy(_cloudMaterial);
            if (_volume != null && _volume.sharedProfile != null) Destroy(_volume.sharedProfile);
        }

        // Legacy API surface kept as no-ops so any caller that still touches
        // these doesn't break compilation. They're meaningless now.
        public float TimeOfDay => 0.5f;
        public void SetTime(float t) { /* no-op */ }
    }
}
