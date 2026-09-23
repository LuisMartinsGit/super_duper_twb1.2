// DayNightCycle.cs
// (Day/night cycle removed — the game stays in ONE static atmospheric
//  preset. This MonoBehaviour kept its name so GameBootstrap and any
//  inspector references still resolve.)
//
// The preset is the dusk look in docs/Design/Art_Direction.md: cool weak
// sun, navy/teal Trilight ambient, blue-violet shadow split-tone, a
// desaturated base, and bloom that only HDR emissives cross. Every number
// lives in DayNightCycle.asset beside this file (DayNightCycleConfig) —
// this class holds no values of its own.
//
// Responsibilities:
//   - Configure the single directional sun and push shadow distance /
//     cascade count over the pipeline asset.
//   - Set Trilight ambient, fog and the camera's void colour.
//   - Build the global URP volume (vignette, bloom, colour adjustments,
//     shadows/midtones/highlights, film grain, ACES) and re-push the
//     config into it every frame, so editing the asset in Play mode is
//     live — and persists, because it is an asset.
//
// The one-tint rule (Art_Direction.md §3.1), learned the hard way: an
// earlier "blue-volcanic" tuning stacked sun tint × bloom tint × colour
// filter × white balance × negative exposure into a near-black image.
// Colour temperature therefore comes from exactly two places — the
// ambient and the SMH shadows tint. Bloom tint and colour filter stay
// white, WhiteBalance is not registered, and postExposure is a knob that
// is expected to stay at 0.
//
// Cloud-shadow projector retained because it adds depth, but is fixed
// (no day-fade, no cloud-shadow opacity ramp).

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.World
{
    public class DayNightCycle : MonoBehaviour
    {
        private DayNightCycleConfig _cfg;
        private DayNightCycleConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<DayNightCycleConfig>());

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
        // Set when no unlit shader survived the build — a runtime flag, NOT a
        // write to the shared asset (that would persist in the editor).
        private bool _cloudShadowsUnavailable;

        // What ApplyStaticAtmosphere last pushed. Sun and RenderSettings
        // writes are cheap, but DynamicGI.UpdateEnvironment is not, so the
        // per-frame path only re-applies when a value actually changed.
        private Color _appliedSky, _appliedEquator, _appliedGround;

        // Cached override component refs — populated once in
        // EnsurePostProcessingVolume, then re-pushed every frame by
        // ApplyPostProcessingValues so the asset is live-tunable during Play
        // mode instead of being frozen at Awake-time values.
        private Vignette _vignetteOverride;
        private Bloom _bloomOverride;
        private ColorAdjustments _colorOverride;
        private ShadowsMidtonesHighlights _smhOverride;
        private Tonemapping _toneOverride;
        private ColorLookup _lutOverride;
        private FilmGrain _grainOverride;

        void Awake()
        {
            CreateOrFindSun();
            ConfigureShadows();
            ApplyStaticAtmosphere(force: true);
            EnsurePostProcessingVolume();
            ApplyPostProcessingValues();
            _mainCamera = Camera.main;
            ConfigureCamera(_mainCamera);
        }

        void Update()
        {
            // Push current asset values into the sun, RenderSettings and the
            // cached overrides so tuning at Play time takes effect. Cheap — a
            // handful of float / colour assignments per frame.
            ApplyStaticAtmosphere(force: false);
            ApplyPostProcessingValues();

            // Camera.main can become non-null on a later frame (lobby →
            // game transitions, scene reloads). Re-acquire and configure it
            // when we first see it.
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera != null) ConfigureCamera(_mainCamera);
            }

            // No cycle — just drift the cloud texture for life.
            if (Cfg.cloudShadows && !_cloudShadowsUnavailable)
                UpdateCloudShadows();
        }

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
            _sun.shadowNormalBias = 0.4f;
            _sun.shadowBias = 0.05f;
        }

        private void ConfigureShadows()
        {
            QualitySettings.shadowDistance = Cfg.shadowDistance;

            var rpAsset = GraphicsSettings.currentRenderPipeline;
            if (rpAsset != null)
            {
                // The asset value is the AUTHORITY — pushed over the pipeline
                // asset via reflection, so tuning the pipeline asset alone does
                // not stick.
                var sdField = rpAsset.GetType().GetProperty("shadowDistance",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (sdField != null && sdField.CanWrite)
                    sdField.SetValue(rpAsset, Cfg.shadowDistance);

                // 4 -> 2 (2026-08-31 GPU pass): four cascades re-render the
                // scene's shadow casters up to four times for a camera that
                // never sees past ~120 m. Two splits cover that range with no
                // visible seam at RTS height.
                var cascadeField = rpAsset.GetType().GetProperty("shadowCascadeCount",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (cascadeField != null && cascadeField.CanWrite)
                    cascadeField.SetValue(rpAsset, Cfg.shadowCascadeCount);
            }
        }

        /// <summary>
        /// Sun, Trilight ambient gradient, fog. Runs every frame; the ambient
        /// probe refresh only when an ambient colour changed.
        /// </summary>
        private void ApplyStaticAtmosphere(bool force)
        {
            _sun.transform.rotation = Quaternion.Euler(Cfg.sunPitch, Cfg.sunHeading, 0f);
            _sun.color = Cfg.sunColor;
            _sun.intensity = Cfg.sunIntensity;
            _sun.shadowStrength = Cfg.shadowStrength;

            // Trilight ambient — explicit sky / equator / ground colours.
            // Why not Skybox: AmbientMode.Skybox samples the skybox into SH
            // coefficients AT BAKE TIME. The map scenes have no baked lighting
            // data → no baked SH → ambient probe near-zero → every surface
            // that isn't directly sun-lit renders pure black. Trilight uses
            // the three explicit colours at runtime with no bake.
            bool ambientChanged = force
                || _appliedSky != Cfg.ambientSkyColor
                || _appliedEquator != Cfg.ambientEquatorColor
                || _appliedGround != Cfg.ambientGroundColor;
            if (ambientChanged)
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = Cfg.ambientSkyColor;
                RenderSettings.ambientEquatorColor = Cfg.ambientEquatorColor;
                RenderSettings.ambientGroundColor = Cfg.ambientGroundColor;
                _appliedSky = Cfg.ambientSkyColor;
                _appliedEquator = Cfg.ambientEquatorColor;
                _appliedGround = Cfg.ambientGroundColor;
                // Belt-and-suspenders: refresh the runtime ambient probe so the
                // change propagates to renderers that cache it.
                DynamicGI.UpdateEnvironment();
            }

            // Fog. Enabled only when a density is actually set — this also
            // OVERRIDES any fog baked into the map scene's RenderSettings, so
            // a legacy scene bake cannot re-fog a match.
            RenderSettings.fog = Cfg.fogDensity > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Cfg.fogColor;
            RenderSettings.fogDensity = Cfg.fogDensity;

            if (_mainCamera != null)
                _mainCamera.backgroundColor = Cfg.voidColor;
        }

        /// <summary>
        /// Build (or reuse) a global URP Volume and register every override
        /// component the scene uses. Values are NOT written here — call
        /// ApplyPostProcessingValues() to push the config into the
        /// overrides. This split lets the asset stay live at Play time
        /// without rebuilding the profile each frame.
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

            // Tonemapping and the colour LUT now come FROM the asset, so the
            // look is tunable in Play mode like every other value here.
            _toneOverride = profile.Add<Tonemapping>(true);
            _lutOverride = profile.Add<ColorLookup>(true);

            // NOTE: WhiteBalance is deliberately not registered (one-tint
            // rule, file header).

            _volume.sharedProfile = profile;
        }

        /// <summary>
        /// Push current config values into the cached override components.
        /// Cheap — only float / Vector4 assignments. Called from Update so
        /// Play-mode asset tweaks take effect immediately.
        /// </summary>
        private void ApplyPostProcessingValues()
        {
            if (_vignetteOverride != null)
            {
                _vignetteOverride.intensity.Override(Cfg.vignetteIntensity);
                _vignetteOverride.color.Override(Cfg.vignetteColor);
                _vignetteOverride.smoothness.Override(Cfg.vignetteSmoothness);
                _vignetteOverride.rounded.Override(false);
            }

            if (_toneOverride != null)
            {
                var mode = Cfg.tonemapping switch
                {
                    2 => TonemappingMode.ACES,
                    1 => TonemappingMode.Neutral,
                    _ => TonemappingMode.None,
                };
                _toneOverride.mode.Override(mode);
            }

            if (_lutOverride != null)
            {
                // An unset texture leaves the override inactive rather than
                // applying an identity lookup nobody authored.
                _lutOverride.active = Cfg.lutTexture != null;
                if (Cfg.lutTexture != null)
                {
                    _lutOverride.texture.Override(Cfg.lutTexture);
                    _lutOverride.contribution.Override(Mathf.Clamp01(Cfg.lutContribution));
                }
            }

            if (_bloomOverride != null)
            {
                _bloomOverride.intensity.Override(Cfg.bloomIntensity);
                _bloomOverride.threshold.Override(Cfg.bloomThreshold);
                _bloomOverride.scatter.Override(Cfg.bloomScatter);
                // Bloom tint stays white (one-tint rule).
                _bloomOverride.tint.Override(Color.white);
            }

            if (_colorOverride != null)
            {
                _colorOverride.postExposure.Override(Cfg.postExposure);
                _colorOverride.saturation.Override(Cfg.saturation);
                _colorOverride.contrast.Override(Cfg.contrast);
                // colorFilter stays neutral (one-tint rule). Any tint here
                // multiplies every pixel — the fastest way to crush faction
                // reds / greens / blues.
                _colorOverride.colorFilter.Override(Color.white);
            }

            if (_smhOverride != null)
            {
                var s = Cfg.smhShadowsTint;
                var h = Cfg.smhHighlightsTint;
                _smhOverride.shadows.Override(new Vector4(s.r, s.g, s.b, 0f));
                _smhOverride.highlights.Override(new Vector4(h.r, h.g, h.b, 0f));
            }

            if (_grainOverride != null)
            {
                _grainOverride.intensity.Override(Cfg.filmGrainIntensity);
                _grainOverride.response.Override(Cfg.filmGrainResponse);
            }
        }

        /// <summary>
        /// URP cameras default renderPostProcessing=false; without flipping
        /// this flag the global Volume is built but the camera silently
        /// ignores it. The camera is created at runtime in CameraController
        /// without ever touching this flag, so we enable it here from the
        /// canonical post-process owner — and give it the void colour, since
        /// that is atmosphere too.
        /// </summary>
        private void ConfigureCamera(Camera cam)
        {
            if (cam == null) return;
            var data = cam.GetUniversalAdditionalCameraData();
            if (data != null) data.renderPostProcessing = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Cfg.voidColor;
        }

        private void UpdateCloudShadows()
        {
            _cloudOffsetX += Cfg.cloudSpeed * Time.deltaTime;
            _cloudOffsetZ += Cfg.cloudSpeed * 0.3f * Time.deltaTime;

            if (_cloudProjector == null)
                CreateCloudProjector();

            if (_cloudMaterial != null)
            {
                _cloudMaterial.SetFloat("_OffsetX", _cloudOffsetX);
                _cloudMaterial.SetFloat("_OffsetZ", _cloudOffsetZ);
                _cloudMaterial.SetFloat("_Opacity", Cfg.cloudOpacity);
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
            float half = Cfg.cloudProjectorSize;
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

            // Both of these can be stripped from a player build (nothing
            // references them from a material asset), and this runs from
            // Update — an unguarded new Material(null) threw EVERY FRAME in
            // the 2026-08-09 build. Give up on cloud shadows instead.
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Transparent")
                      ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError(
                    "[DayNightCycle] No unlit shader available for cloud shadows — add "
                    + "\"Universal Render Pipeline/Unlit\" to Project Settings > Graphics > "
                    + "Always Included Shaders. Disabling cloud shadows.");
                _cloudShadowsUnavailable = true;   // stops UpdateCloudShadows being called again
                return;
            }
            _cloudMaterial = new Material(shader);
            _cloudMaterial.mainTexture = _cloudTexture;
            _cloudMaterial.color = new Color(0f, 0f, 0f, Cfg.cloudOpacity);

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
