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
        [Header("Sun (fixed, no day/night cycle)")]
        [Tooltip("Sun pitch — angle from horizon. ~25° gives a low, dramatic 'eternal evening' rake.")]
        public float sunPitch = 25f;
        [Tooltip("Sun heading (compass) in degrees.")]
        public float sunHeading = 145f;
        [Tooltip("Sun colour. Default is a cool steel-blue with a faint warm tint, like a moonlit volcanic ridge.")]
        public Color sunColor = new(0.55f, 0.65f, 0.85f);
        [Tooltip("Sun intensity. ~0.85 keeps the scene 'dark' but readable.")]
        public float sunIntensity = 0.85f;

        [Header("Ambient (fixed)")]
        [Tooltip("Flat ambient — fills shadows so units stay readable in a dark scene.")]
        public Color ambientColor = new(0.10f, 0.13f, 0.20f);

        [Header("Fog / Volumetric Mood")]
        [Tooltip("Fog colour — deep blue-black for the volcanic-ash mood.")]
        public Color fogColor = new(0.05f, 0.07f, 0.12f);
        [Tooltip("Exponential-squared fog density. Tiny values: 0.002 = subtle, 0.008 = thick.")]
        public float fogDensity = 0.0035f;

        [Header("Post-Processing (URP global volume)")]
        [Tooltip("Vignette intensity (0-1). 0.45 darkens screen corners noticeably.")]
        [Range(0f, 1f)] public float vignetteIntensity = 0.45f;
        [Tooltip("Vignette colour.")]
        public Color vignetteColor = new(0.02f, 0.03f, 0.06f);
        [Tooltip("Bloom intensity for crystal/lava/light glow.")]
        [Range(0f, 5f)] public float bloomIntensity = 0.9f;
        [Tooltip("Bloom threshold — only HDR-bright pixels glow.")]
        [Range(0f, 2f)] public float bloomThreshold = 0.95f;
        [Tooltip("Negative post-exposure darkens overall image.")]
        [Range(-3f, 3f)] public float postExposure = -0.6f;
        [Tooltip("Saturation adjustment. Negative drains colour for cinematic mood.")]
        [Range(-100f, 100f)] public float saturation = -15f;
        [Tooltip("Contrast adjustment.")]
        [Range(-100f, 100f)] public float contrast = 12f;
        [Tooltip("White-balance temperature. Negative = cool/blue.")]
        [Range(-100f, 100f)] public float whiteBalanceTemperature = -25f;
        [Tooltip("White-balance tint. Slight magenta to push toward 'volcanic'.")]
        [Range(-100f, 100f)] public float whiteBalanceTint = -8f;

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

        void Awake()
        {
            CreateOrFindSun();
            ConfigureShadows();
            ApplyStaticAtmosphere();
            EnsurePostProcessingVolume();
            _mainCamera = Camera.main;
        }

        void Update()
        {
            // No cycle — just drift the cloud texture for life.
            if (cloudShadows)
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
            _sun.shadowStrength = 0.7f;
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

        /// <summary>One-time atmospheric setup: sun, ambient, fog.</summary>
        private void ApplyStaticAtmosphere()
        {
            // Sun rotation and look.
            _sun.transform.rotation = Quaternion.Euler(sunPitch, sunHeading, 0f);
            _sun.color = sunColor;
            _sun.intensity = sunIntensity;

            // Ambient — fills shadows so the dark scene stays readable.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambientColor;

            // Fog — fakes volumetric depth for the volcanic-haze mood.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
        }

        /// <summary>
        /// Build (or update) a global URP Volume with vignette, bloom, color
        /// adjustments, and white balance for the dark blue-volcanic mood.
        /// </summary>
        private void EnsurePostProcessingVolume()
        {
            // Find or create a global Volume on this GameObject.
            _volume = GetComponent<Volume>();
            if (_volume == null)
                _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 10f;
            _volume.weight = 1f;

            // Always rebuild the profile in case fields changed in the inspector.
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "TWB_StaticAtmosphereProfile";

            // Vignette.
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(vignetteIntensity);
            vignette.color.Override(vignetteColor);
            vignette.smoothness.Override(0.45f);
            vignette.rounded.Override(false);

            // Bloom — makes crystals / projectiles glow against the dark.
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(bloomIntensity);
            bloom.threshold.Override(bloomThreshold);
            bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(0.55f, 0.70f, 1.0f));

            // Color adjustments — exposure / saturation / contrast.
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(postExposure);
            color.saturation.Override(saturation);
            color.contrast.Override(contrast);
            color.colorFilter.Override(new Color(0.85f, 0.92f, 1.05f, 1f)); // faint cool tint

            // White balance — push the whole image cool/blue.
            var wb = profile.Add<WhiteBalance>(true);
            wb.temperature.Override(whiteBalanceTemperature);
            wb.tint.Override(whiteBalanceTint);

            // Tonemapping — ACES pulls highlights without crushing.
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);

            _volume.sharedProfile = profile;
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
