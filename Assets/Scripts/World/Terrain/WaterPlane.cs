// WaterPlane.cs
// Animated water plane with procedural waves
// Location: Assets/Scripts/World/Terrain/WaterPlane.cs

using UnityEngine;

namespace TheWaningBorder.World.Terrain
{
    /// <summary>
    /// Creates an animated water plane at sea level.
    /// AoE4-style: flow-based animation, depth coloring, foam.
    /// </summary>
    public class WaterPlane : MonoBehaviour
    {
        [Header("Water Colors (AoE4 Style)")]
        public Color shallowColor = new Color(0.30f, 0.60f, 0.70f, 0.6f);
        public Color deepColor = new Color(0.08f, 0.22f, 0.35f, 0.95f);
        public Color foamColor = new Color(0.95f, 0.98f, 1f, 0.9f);
        public float waterLevel = 20f;

        [Header("Flow Animation")]
        public float flowSpeed = 0.06f;
        public float flowStrength = 0.25f;

        [Header("Surface Detail")]
        public float rippleScale = 0.05f;
        public float rippleSpeed = 0.4f;
        public float bumpiness = 0.35f;

        [Header("Foam")]
        public float foamScale = 0.07f;
        public float foamThreshold = 0.55f;
        public float foamIntensity = 1.2f;

        [Header("Specular")]
        public float specularPower = 64f;
        public float specularIntensity = 0.35f;

        private MeshRenderer _renderer;
        private Material _material;
        private Mesh _mesh;

        /// <summary>
        /// Singleton for easy access.
        /// </summary>
        public static WaterPlane Instance { get; private set; }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (_material != null)
                Destroy(_material);
            if (_mesh != null)
                Destroy(_mesh);
        }

        /// <summary>
        /// Initialize water plane with given parameters.
        /// </summary>
        public void Initialize(Vector2 worldMin, Vector2 worldMax, float seaLevelHeight)
        {
            waterLevel = seaLevelHeight;

            // Create water mesh
            CreateWaterMesh(worldMin, worldMax);

            // Create and apply material
            CreateWaterMaterial();

            Debug.Log($"[WaterPlane] Created at height {waterLevel}");
        }

        void CreateWaterMesh(Vector2 worldMin, Vector2 worldMax)
        {
            float width = worldMax.x - worldMin.x;
            float height = worldMax.y - worldMin.y;
            float centerX = (worldMin.x + worldMax.x) * 0.5f;
            float centerZ = (worldMin.y + worldMax.y) * 0.5f;

            // Higher subdivisions for smoother wave deformation
            int subdivisions = 128;
            int vertCount = (subdivisions + 1) * (subdivisions + 1);
            int triCount = subdivisions * subdivisions * 6;

            Vector3[] vertices = new Vector3[vertCount];
            Vector2[] uvs = new Vector2[vertCount];
            int[] triangles = new int[triCount];

            float stepX = width / subdivisions;
            float stepZ = height / subdivisions;

            // Generate vertices
            for (int z = 0; z <= subdivisions; z++)
            {
                for (int x = 0; x <= subdivisions; x++)
                {
                    int i = z * (subdivisions + 1) + x;
                    vertices[i] = new Vector3(
                        worldMin.x + x * stepX,
                        waterLevel,
                        worldMin.y + z * stepZ
                    );
                    uvs[i] = new Vector2((float)x / subdivisions, (float)z / subdivisions);
                }
            }

            // Generate triangles
            int t = 0;
            for (int z = 0; z < subdivisions; z++)
            {
                for (int x = 0; x < subdivisions; x++)
                {
                    int i = z * (subdivisions + 1) + x;
                    triangles[t++] = i;
                    triangles[t++] = i + subdivisions + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + subdivisions + 1;
                    triangles[t++] = i + subdivisions + 2;
                }
            }

            _mesh = new Mesh
            {
                name = "WaterMesh",
                vertices = vertices,
                uv = uvs,
                triangles = triangles
            };
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            // Add mesh filter and renderer
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.mesh = _mesh;

            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }

        void CreateWaterMaterial()
        {
            // Try to find the custom water shader
            var shader = Shader.Find("Custom/AnimatedWater");
            
            if (shader == null)
            {
                // Fallback to Standard shader
                Debug.LogWarning("[WaterPlane] Custom/AnimatedWater shader not found. Using Standard shader fallback.");
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                shader = Shader.Find("Diffuse");
            }

            _material = new Material(shader);

            // Set initial properties
            UpdateMaterialProperties();

            _renderer.material = _material;
        }

        void UpdateMaterialProperties()
        {
            if (_material == null) return;

            // Check if using custom AoE4-style shader
            if (_material.HasProperty("_ShallowColor"))
            {
                // Colors
                _material.SetColor("_ShallowColor", shallowColor);
                _material.SetColor("_DeepColor", deepColor);
                _material.SetColor("_FoamColor", foamColor);
                
                // Flow animation
                _material.SetFloat("_FlowSpeed", flowSpeed);
                _material.SetFloat("_FlowStrength", flowStrength);
                
                // Surface detail
                _material.SetFloat("_RippleScale", rippleScale);
                _material.SetFloat("_RippleSpeed", rippleSpeed);
                _material.SetFloat("_Bumpiness", bumpiness);
                
                // Foam
                _material.SetFloat("_FoamScale", foamScale);
                _material.SetFloat("_FoamThreshold", foamThreshold);
                _material.SetFloat("_FoamIntensity", foamIntensity);
                
                // Specular
                _material.SetFloat("_SpecularPower", specularPower);
                _material.SetFloat("_SpecularIntensity", specularIntensity);
                
                _material.SetFloat("_WaterLevel", waterLevel);
            }
            else
            {
                // Fallback to Standard shader properties
                _material.SetColor("_Color", shallowColor);
                _material.SetFloat("_Mode", 3); // Transparent mode
                _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _material.SetInt("_ZWrite", 0);
                _material.DisableKeyword("_ALPHATEST_ON");
                _material.EnableKeyword("_ALPHABLEND_ON");
                _material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _material.renderQueue = 3000;
                _material.SetFloat("_Glossiness", 0.9f);
                _material.SetFloat("_Metallic", 0.1f);
            }
        }

        void Update()
        {
            // Animate water using vertex displacement when using fallback shader
            if (_material != null && _mesh != null && !_material.HasProperty("_FlowSpeed"))
            {
                AnimateWaterMesh();
            }
        }

        void AnimateWaterMesh()
        {
            var vertices = _mesh.vertices;
            float time = Time.time;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                
                // Large waves (using flowSpeed and flowStrength)
                float wave1 = Mathf.Sin(v.x * rippleScale + time * flowSpeed) *
                              Mathf.Cos(v.z * rippleScale * 0.8f + time * flowSpeed * 0.7f);
                
                // Secondary waves
                float wave2 = Mathf.Sin(v.x * rippleScale * 2.3f - time * flowSpeed * 1.3f) *
                              Mathf.Cos(v.z * rippleScale * 1.7f + time * flowSpeed * 0.9f) * 0.5f;

                vertices[i].y = waterLevel + (wave1 + wave2) * flowStrength;
            }

            _mesh.vertices = vertices;
            _mesh.RecalculateNormals();
        }

        /// <summary>
        /// Check if a world position is underwater.
        /// </summary>
        public bool IsUnderwater(Vector3 worldPos)
        {
            return worldPos.y < waterLevel;
        }

        /// <summary>
        /// Get the water height at a world position (including waves).
        /// </summary>
        public float GetWaterHeightAt(Vector3 worldPos)
        {
            float time = Time.time;
            
            float wave1 = Mathf.Sin(worldPos.x * rippleScale + time * flowSpeed) *
                          Mathf.Cos(worldPos.z * rippleScale * 0.8f + time * flowSpeed * 0.7f);
            
            float wave2 = Mathf.Sin(worldPos.x * rippleScale * 2.3f - time * flowSpeed * 1.3f) *
                          Mathf.Cos(worldPos.z * rippleScale * 1.7f + time * flowSpeed * 0.9f) * 0.5f;

            return waterLevel + (wave1 + wave2) * flowStrength;
        }
    }
}