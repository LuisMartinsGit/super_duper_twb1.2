// DayNightCycle.cs
// Rotates a directional light to simulate a full day-night cycle every
// cycleDuration minutes. Adjusts light color, intensity, and ambient light
// to match the time of day. Configures shadows for RTS camera distance.
// Includes drifting cloud shadow projector using Perlin noise.
//
// Attach to any GameObject or let GameBootstrap create it.

using UnityEngine;
using UnityEngine.Rendering;

namespace TheWaningBorder.World
{
    public class DayNightCycle : MonoBehaviour
    {
        [Header("Cycle")]
        [Tooltip("Full day-night cycle duration in minutes")]
        public float cycleDuration = 15f;

        [Tooltip("Starting time of day (0=midnight, 0.25=sunrise, 0.5=noon, 0.75=sunset)")]
        public float startTime = 0.3f;

        [Header("Sun")]
        [Tooltip("Sun rotation axis latitude (angle from horizon at noon)")]
        public float sunLatitude = 45f;

        [Header("Shadows")]
        [Tooltip("Shadow draw distance in world units")]
        public float shadowDistance = 300f;

        [Header("Cloud Shadows")]
        [Tooltip("Enable moving cloud shadow layer on terrain")]
        public bool cloudShadows = true;
        [Tooltip("Cloud shadow darkness (0=invisible, 1=black)")]
        public float cloudOpacity = 0.25f;
        [Tooltip("Cloud drift speed in world units per second")]
        public float cloudSpeed = 3f;
        [Tooltip("Cloud noise scale (lower = larger clouds)")]
        public float cloudScale = 0.008f;
        [Tooltip("World size of the cloud shadow projector")]
        public float cloudProjectorSize = 300f;

        // ── Runtime ──
        private Light _sun;
        private float _timeOfDay;

        // Color gradient stops for the sun
        private static readonly Color SunriseColor = new(1.0f, 0.55f, 0.25f);
        private static readonly Color NoonColor    = new(1.0f, 0.97f, 0.90f);
        private static readonly Color SunsetColor  = new(1.0f, 0.45f, 0.20f);
        private static readonly Color NightColor   = new(0.15f, 0.18f, 0.35f);

        // Ambient light colors
        private static readonly Color AmbientDay     = new(0.45f, 0.50f, 0.55f);
        private static readonly Color AmbientSunrise = new(0.30f, 0.25f, 0.25f);
        private static readonly Color AmbientNight   = new(0.05f, 0.06f, 0.12f);

        // Cloud projector state
        private GameObject _cloudProjector;
        private Material _cloudMaterial;
        private float _cloudOffsetX;
        private float _cloudOffsetZ;

        // Cached references (Camera.main was previously called every Update,
        // which scans all cameras tagged MainCamera each call). (task-062 Q-28)
        private Camera _mainCamera;

        void Awake()
        {
            _timeOfDay = startTime;
            CreateOrFindSun();
            ConfigureShadows();
            _mainCamera = Camera.main;
        }

        void Update()
        {
            float cycleSeconds = cycleDuration * 60f;
            _timeOfDay += Time.deltaTime / cycleSeconds;
            if (_timeOfDay >= 1f) _timeOfDay -= 1f;

            UpdateSunTransform();
            UpdateSunLight();
            UpdateAmbient();

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
            _sun.intensity = 1.2f;
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

        private void UpdateSunTransform()
        {
            float sunAngle = _timeOfDay * 360f - 90f;
            _sun.transform.rotation = Quaternion.Euler(sunAngle, 170f, 0f);
        }

        private void UpdateSunLight()
        {
            float t = _timeOfDay;
            Color sunColor;
            float intensity;

            if (t < 0.2f)
            {
                float f = t / 0.2f;
                sunColor = Color.Lerp(NightColor, SunriseColor, f * f);
                intensity = Mathf.Lerp(0.05f, 0.4f, f);
            }
            else if (t < 0.3f)
            {
                float f = (t - 0.2f) / 0.1f;
                sunColor = Color.Lerp(SunriseColor, NoonColor, f);
                intensity = Mathf.Lerp(0.4f, 1.2f, f);
            }
            else if (t < 0.7f)
            {
                sunColor = NoonColor;
                intensity = 1.2f;
            }
            else if (t < 0.8f)
            {
                float f = (t - 0.7f) / 0.1f;
                sunColor = Color.Lerp(NoonColor, SunsetColor, f);
                intensity = Mathf.Lerp(1.2f, 0.4f, f);
            }
            else
            {
                float f = (t - 0.8f) / 0.2f;
                sunColor = Color.Lerp(SunsetColor, NightColor, f * f);
                intensity = Mathf.Lerp(0.4f, 0.05f, f);
            }

            _sun.color = sunColor;
            _sun.intensity = intensity;
        }

        private void UpdateAmbient()
        {
            float t = _timeOfDay;
            Color ambient;

            if (t < 0.2f || t > 0.85f)
                ambient = AmbientNight;
            else if (t < 0.35f)
            {
                float f = (t - 0.2f) / 0.15f;
                ambient = Color.Lerp(AmbientNight, AmbientSunrise, f);
            }
            else if (t < 0.45f)
            {
                float f = (t - 0.35f) / 0.1f;
                ambient = Color.Lerp(AmbientSunrise, AmbientDay, f);
            }
            else if (t < 0.65f)
                ambient = AmbientDay;
            else if (t < 0.75f)
            {
                float f = (t - 0.65f) / 0.1f;
                ambient = Color.Lerp(AmbientDay, AmbientSunrise, f);
            }
            else
            {
                float f = (t - 0.75f) / 0.1f;
                ambient = Color.Lerp(AmbientSunrise, AmbientNight, f);
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = ambient;
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

                // Fade clouds at night (no cloud shadows in darkness)
                float dayFactor = Mathf.Clamp01((_sun.intensity - 0.2f) / 0.8f);
                _cloudMaterial.SetFloat("_Opacity", cloudOpacity * dayFactor);
            }

            // Re-resolve if cached camera was destroyed mid-session (e.g. scene
            // change). Cheap fallback that costs nothing in the common case.
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

            var mesh = new Mesh();
            float half = cloudProjectorSize;
            mesh.vertices = new Vector3[]
            {
                new(-half, 0, -half), new(half, 0, -half),
                new(half, 0, half), new(-half, 0, half)
            };
            mesh.uv = new Vector2[]
            {
                new(0, 0), new(1, 0), new(1, 1), new(0, 1)
            };
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mf.mesh = mesh;

            // Generate 3-octave Perlin noise cloud texture
            int res = 512;
            var cloudTex = new Texture2D(res, res, TextureFormat.RGBA32, true);
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
                    cloudTex.SetPixel(x, y, new Color(0f, 0f, 0f, shadow));
                }
            }
            cloudTex.Apply();
            cloudTex.wrapMode = TextureWrapMode.Repeat;
            cloudTex.filterMode = FilterMode.Bilinear;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Transparent");
            _cloudMaterial = new Material(shader);
            _cloudMaterial.mainTexture = cloudTex;
            _cloudMaterial.color = new Color(0f, 0f, 0f, cloudOpacity);

            // Multiply blend — darkens terrain where cloud texture is opaque
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
        }

        /// <summary>Current time of day (0=midnight, 0.5=noon).</summary>
        public float TimeOfDay => _timeOfDay;

        /// <summary>Set time of day immediately (0-1).</summary>
        public void SetTime(float t) => _timeOfDay = Mathf.Repeat(t, 1f);
    }
}
