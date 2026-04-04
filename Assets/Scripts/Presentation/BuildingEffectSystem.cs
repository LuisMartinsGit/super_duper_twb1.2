// BuildingEffectSystem.cs
// Handles visual effects for building construction (dust particles)
// and building destruction (inward collapse + dust cloud).
// Location: Assets/Scripts/Presentation/BuildingEffectSystem.cs

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Presentation
{
    /// <summary>
    /// MonoBehaviour system that drives:
    /// 1. Construction dust particles — emitted when construction progress increases
    /// 2. Destruction collapse — children fall inward + dust cloud when BuildingCollapseState present
    /// </summary>
    public class BuildingEffectSystem : MonoBehaviour
    {
        public static BuildingEffectSystem Instance { get; private set; }

        // ── Construction dust tracking ──
        private readonly Dictionary<Entity, float> _lastProgress = new();
        private readonly Dictionary<Entity, ParticleSystem> _constructionDust = new();

        // ── Collapse tracking ──
        private readonly Dictionary<Entity, CollapseData> _collapsingBuildings = new();

        // ── Constants ──
        private const float DustBurstInterval = 0.05f; // Minimum progress change to emit dust

        private struct CollapseData
        {
            public float Duration;
            public float Elapsed;
            public Transform[] Children;
            public Vector3[] OriginalLocalPos;
            public Quaternion[] OriginalLocalRot;
            public Vector3[] OriginalLocalScale;
            public float MaxChildY;
            public ParticleSystem DustCloud;
            public ParticleSystem Debris;
        }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;

            // Cleanup any active particles
            foreach (var ps in _constructionDust.Values)
                if (ps != null) Destroy(ps.gameObject);
            _constructionDust.Clear();

            foreach (var data in _collapsingBuildings.Values)
            {
                if (data.DustCloud != null) Destroy(data.DustCloud.gameObject);
                if (data.Debris != null) Destroy(data.Debris.gameObject);
            }
            _collapsingBuildings.Clear();
        }

        void Update()
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            if (EntityViewManager.Instance == null) return;

            var em = world.EntityManager;
            float dt = Time.deltaTime;

            UpdateConstructionDust(em);
            UpdateCollapseAnimations(em, dt);
        }

        // ═══════════════════════════════════════════════════════════════
        // CONSTRUCTION DUST
        // ═══════════════════════════════════════════════════════════════

        private void UpdateConstructionDust(EntityManager em)
        {
            // Query all entities under construction
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnderConstruction>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<BuildingTag>()
            );

            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var constructions = query.ToComponentDataArray<UnderConstruction>(Unity.Collections.Allocator.Temp);
            using var transforms = query.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

            // Track which entities are still under construction for cleanup
            var activeEntities = new HashSet<Entity>();

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var uc = constructions[i];
                float ratio = uc.Total > 0 ? Mathf.Clamp01(uc.Progress / uc.Total) : 1f;
                activeEntities.Add(entity);

                // Check if progress increased enough to emit dust
                if (_lastProgress.TryGetValue(entity, out float lastRatio))
                {
                    if (ratio - lastRatio >= DustBurstInterval)
                    {
                        // Emit dust burst centered on building
                        if (EntityViewManager.Instance.TryGetView(entity, out var bgo) && bgo != null)
                        {
                            EnsureConstructionDust(entity, bgo);
                            var ps = _constructionDust[entity];
                            if (ps != null)
                            {
                                // Re-center on building bounds each burst (building is rising)
                                var cRenderers = bgo.GetComponentsInChildren<Renderer>();
                                if (cRenderers.Length > 0)
                                {
                                    var cBounds = cRenderers[0].bounds;
                                    for (int r = 1; r < cRenderers.Length; r++)
                                        cBounds.Encapsulate(cRenderers[r].bounds);
                                    ps.transform.position = cBounds.center;
                                }
                                else
                                {
                                    var pos = (Vector3)transforms[i].Position;
                                    pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                                    ps.transform.position = pos;
                                }
                                ps.Emit(Random.Range(15, 30));
                            }
                        }
                        _lastProgress[entity] = ratio;
                    }
                }
                else
                {
                    _lastProgress[entity] = ratio;
                }
            }

            // Cleanup dust for completed buildings
            var toRemove = new List<Entity>();
            foreach (var kvp in _constructionDust)
            {
                if (!activeEntities.Contains(kvp.Key))
                {
                    if (kvp.Value != null) Destroy(kvp.Value.gameObject);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var e in toRemove)
            {
                _constructionDust.Remove(e);
                _lastProgress.Remove(e);
            }
        }

        private void EnsureConstructionDust(Entity entity, GameObject buildingGO)
        {
            if (_constructionDust.ContainsKey(entity)) return;

            // Compute building footprint radius from visual bounds
            float buildingRadius = 2f; // fallback
            var cRenderers = buildingGO.GetComponentsInChildren<Renderer>();
            if (cRenderers.Length > 0)
            {
                var cBounds = cRenderers[0].bounds;
                for (int r = 1; r < cRenderers.Length; r++)
                    cBounds.Encapsulate(cRenderers[r].bounds);
                buildingRadius = Mathf.Max(cBounds.extents.x, cBounds.extents.z);
            }

            var ps = CreateDustParticleSystem("ConstructionDust", 3.0f, 4.0f, 200, buildingRadius);
            ps.transform.position = buildingGO.transform.position;
            _constructionDust[entity] = ps;
        }

        // ═══════════════════════════════════════════════════════════════
        // DESTRUCTION COLLAPSE
        // ═══════════════════════════════════════════════════════════════

        private void UpdateCollapseAnimations(EntityManager em, float dt)
        {
            // Detect new collapsing buildings
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingCollapseState>(),
                ComponentType.ReadOnly<BuildingTag>()
            );

            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var collapses = query.ToComponentDataArray<BuildingCollapseState>(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (_collapsingBuildings.ContainsKey(entity)) continue;

                // Initialize collapse
                if (!EntityViewManager.Instance.TryGetView(entity, out var go) || go == null) continue;

                var data = new CollapseData();
                // Read original timer from the component (it's already been ticking in DeathSystem)
                data.Duration = 2.0f;
                data.Elapsed = 0f;

                int childCount = go.transform.childCount;
                data.Children = new Transform[childCount];
                data.OriginalLocalPos = new Vector3[childCount];
                data.OriginalLocalRot = new Quaternion[childCount];
                data.OriginalLocalScale = new Vector3[childCount];
                data.MaxChildY = 0.01f;

                for (int c = 0; c < childCount; c++)
                {
                    var child = go.transform.GetChild(c);
                    data.Children[c] = child;
                    data.OriginalLocalPos[c] = child.localPosition;
                    data.OriginalLocalRot[c] = child.localRotation;
                    data.OriginalLocalScale[c] = child.localScale;
                    if (child.localPosition.y > data.MaxChildY)
                        data.MaxChildY = child.localPosition.y;
                }

                // Compute building visual bounds for properly centered effects
                var renderers = go.GetComponentsInChildren<Renderer>();
                Bounds buildingBounds;
                if (renderers.Length > 0)
                {
                    buildingBounds = renderers[0].bounds;
                    for (int r = 1; r < renderers.Length; r++)
                        buildingBounds.Encapsulate(renderers[r].bounds);
                }
                else
                {
                    buildingBounds = new Bounds(go.transform.position + Vector3.up * data.MaxChildY * 0.5f,
                        Vector3.one * 3f);
                }

                // Dust cloud centered on the building's visual center, radius covers full footprint
                var dustPos = buildingBounds.center;
                float buildingRadius = Mathf.Max(buildingBounds.extents.x, buildingBounds.extents.z);
                float buildingHeight = buildingBounds.size.y;
                data.DustCloud = CreateDustParticleSystem("CollapseDust", 4.0f, 6.0f, 400, buildingRadius);
                data.DustCloud.transform.position = dustPos;
                data.DustCloud.Emit(100);

                // Spawn rock/debris burst centered on building
                data.Debris = CreateDebrisParticleSystem("CollapseDebris", dustPos, buildingHeight);
                data.Debris.Emit(Random.Range(20, 35));

                _collapsingBuildings[entity] = data;
            }

            // Animate active collapses
            var finished = new List<Entity>();
            var keys = new List<Entity>(_collapsingBuildings.Keys);

            foreach (var entity in keys)
            {
                var data = _collapsingBuildings[entity];
                data.Elapsed += dt;
                _collapsingBuildings[entity] = data;

                float t = Mathf.Clamp01(data.Elapsed / data.Duration);

                // Emit continuous dust during collapse
                if (data.DustCloud != null && t < 0.9f)
                    data.DustCloud.Emit(Random.Range(12, 25));

                // Periodic debris bursts during collapse
                if (data.Debris != null && t < 0.7f && Random.value < 0.3f)
                    data.Debris.Emit(Random.Range(3, 8));

                // Animate children: upper pieces collapse first (reverse of construction)
                for (int c = 0; c < data.Children.Length; c++)
                {
                    if (data.Children[c] == null) continue;

                    float normalizedHeight = data.OriginalLocalPos[c].y / data.MaxChildY;

                    // Upper pieces start collapsing earlier
                    float threshold = (1f - normalizedHeight) * 0.5f;
                    float childT = Mathf.Clamp01((t - threshold) / (1f - threshold));

                    // Ease-in for accelerating fall
                    float eased = childT * childT;

                    // Drop downward
                    var pos = data.OriginalLocalPos[c];
                    pos.y -= eased * data.MaxChildY * 0.8f;

                    // Move inward toward center (XZ)
                    pos.x *= 1f - eased * 0.6f;
                    pos.z *= 1f - eased * 0.6f;

                    // Rotate randomly inward
                    float rotAngle = eased * Random.Range(15f, 45f);
                    var rot = data.OriginalLocalRot[c] * Quaternion.Euler(
                        rotAngle * Mathf.Sign(data.OriginalLocalPos[c].z),
                        0f,
                        -rotAngle * Mathf.Sign(data.OriginalLocalPos[c].x));

                    // Shrink
                    var scale = data.OriginalLocalScale[c] * (1f - eased * 0.5f);

                    data.Children[c].localPosition = pos;
                    data.Children[c].localRotation = rot;
                    data.Children[c].localScale = scale;
                }

                // Entity will be destroyed by DeathSystem when timer expires;
                // PresentationSpawnSystem.CleanupDestroyedEntities handles GO removal.
                if (!em.Exists(entity))
                {
                    if (data.DustCloud != null) Destroy(data.DustCloud.gameObject);
                    if (data.Debris != null) Destroy(data.Debris.gameObject);
                    finished.Add(entity);
                }
            }

            foreach (var e in finished)
                _collapsingBuildings.Remove(e);
        }

        // ═══════════════════════════════════════════════════════════════
        // PARTICLE SYSTEM FACTORY
        // ═══════════════════════════════════════════════════════════════

        // Shared soft-circle texture (generated once, reused by all particle systems)
        private static Texture2D _dustTexture;
        private static Material _dustMaterial;

        /// <summary>
        /// Generate a soft radial gradient texture for cloud-like particles.
        /// Produces a round, feathered circle with varying density — not a hard square.
        /// </summary>
        private static Texture2D GetDustTexture()
        {
            if (_dustTexture != null) return _dustTexture;

            const int res = 64;
            _dustTexture = new Texture2D(res, res, TextureFormat.RGBA32, false);
            _dustTexture.wrapMode = TextureWrapMode.Clamp;
            _dustTexture.filterMode = FilterMode.Bilinear;

            float center = (res - 1) * 0.5f;
            var pixels = new Color[res * res];

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dx = (x - center) / center;
                    float dy = (y - center) / center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Soft falloff: dense center, feathered edges
                    // Uses a smooth cubic falloff for natural cloud density
                    float alpha = Mathf.Clamp01(1f - dist);
                    alpha = alpha * alpha * (3f - 2f * alpha); // smoothstep
                    alpha *= 0.92f; // High peak opacity for thick, visible clouds

                    // Slight noise-like variation from concentric rings
                    float ring = Mathf.Sin(dist * 12f) * 0.05f;
                    alpha = Mathf.Clamp01(alpha + ring);

                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            _dustTexture.SetPixels(pixels);
            _dustTexture.Apply();
            return _dustTexture;
        }

        /// <summary>
        /// Get a shared alpha-blended particle material using the soft circle texture.
        /// </summary>
        private static Material GetDustMaterial()
        {
            if (_dustMaterial != null) return _dustMaterial;

            // Try URP particles shader first, then built-in
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                      ?? Shader.Find("Particles/Standard Unlit")
                      ?? Shader.Find("Standard");

            _dustMaterial = new Material(shader);
            _dustMaterial.mainTexture = GetDustTexture();
            _dustMaterial.color = Color.white; // Tint via particle color, not material

            // Enable alpha blending
            _dustMaterial.SetFloat("_Surface", 1); // URP: Transparent
            _dustMaterial.SetFloat("_Blend", 0);   // URP: Alpha blend
            _dustMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _dustMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _dustMaterial.SetInt("_ZWrite", 0);
            _dustMaterial.renderQueue = 3000; // Transparent queue

            // Enable keyword for built-in particles shader
            _dustMaterial.EnableKeyword("_ALPHABLEND_ON");

            return _dustMaterial;
        }

        /// <summary>
        /// Create a procedural dust cloud particle system.
        /// Particles are round, soft, semi-transparent puffs with varied sizes and rotation.
        /// </summary>
        private ParticleSystem CreateDustParticleSystem(string name, float lifetime, float size, int maxParticles,
            float buildingRadius = 2f)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();

            // Scale particle size relative to building footprint
            float effectiveSize = Mathf.Max(size, buildingRadius * 1.5f);

            // ── Main module ──
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.7f, lifetime * 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(effectiveSize * 0.2f, effectiveSize * 2.0f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.08f; // Gentle upward drift
            main.loop = false;
            main.playOnAwake = false;

            // Varied dust tones: dense, opaque browns and tans
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.75f, 0.65f, 0.50f, 0.85f),  // Light tan, high alpha
                new Color(0.48f, 0.40f, 0.30f, 0.95f)   // Warm brown, near-opaque
            );

            // ── Emission: manual only ──
            var emission = ps.emission;
            emission.enabled = false;

            // ── Shape: hemisphere sized to cover the entire building footprint ──
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = Mathf.Max(buildingRadius * 1.2f, 2f);

            // ── Rotation over lifetime: slow tumble for organic feel ──
            var rotOverLife = ps.rotationOverLifetime;
            rotOverLife.enabled = true;
            rotOverLife.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

            // ── Color over lifetime: fade in briefly then fade out ──
            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] {
                    new GradientColorKey(new Color(0.70f, 0.60f, 0.45f), 0f),
                    new GradientColorKey(new Color(0.58f, 0.50f, 0.40f), 0.3f),
                    new GradientColorKey(new Color(0.45f, 0.40f, 0.35f), 1f)
                },
                new[] {
                    new GradientAlphaKey(0f, 0f),       // Fade in
                    new GradientAlphaKey(0.9f, 0.08f),  // Rapid peak — thick cloud
                    new GradientAlphaKey(0.7f, 0.5f),   // Hold strong
                    new GradientAlphaKey(0f, 1f)         // Fade out
                }
            );
            colorOverLife.color = gradient;

            // ── Size over lifetime: puffs expand as they rise ──
            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.4f),
                    new Keyframe(0.3f, 0.8f),
                    new Keyframe(0.7f, 1.2f),
                    new Keyframe(1f, 1.5f)
                ));

            // ── Noise module: turbulence for natural billowing ──
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = new ParticleSystem.MinMaxCurve(0.3f);
            noise.frequency = 1.5f;
            noise.scrollSpeed = 0.5f;
            noise.damping = true;
            noise.octaveCount = 2;

            // ── Renderer: soft blended billboard with our circle texture ──
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = GetDustMaterial();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.minParticleSize = 0.01f;
            renderer.maxParticleSize = 20f;

            return ps;
        }

        // ═══════════════════════════════════════════════════════════════
        // DEBRIS / ROCK PARTICLE SYSTEM
        // ═══════════════════════════════════════════════════════════════

        private static Material _debrisMaterial;

        /// <summary>
        /// Create a shared opaque material for rock debris particles.
        /// </summary>
        private static Material GetDebrisMaterial()
        {
            if (_debrisMaterial != null) return _debrisMaterial;

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                      ?? Shader.Find("Standard");

            _debrisMaterial = new Material(shader);
            _debrisMaterial.color = new Color(0.45f, 0.40f, 0.35f); // Dark stone
            if (_debrisMaterial.HasProperty("_Smoothness"))
                _debrisMaterial.SetFloat("_Smoothness", 0.15f);

            return _debrisMaterial;
        }

        /// <summary>
        /// Create a debris particle system that flings opaque rock chunks outward.
        /// Uses mesh rendering (small cubes) for solid-looking fragments.
        /// </summary>
        private ParticleSystem CreateDebrisParticleSystem(string name, Vector3 position, float buildingHeight)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            var ps = go.AddComponent<ParticleSystem>();

            // ── Main module ──
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 2.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 10f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.1f, 0.5f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.08f, 0.4f);
            main.startSizeZ = new ParticleSystem.MinMaxCurve(0.1f, 0.45f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1.8f; // Heavy — rocks fall fast
            main.loop = false;
            main.playOnAwake = false;

            // Varied stone colors: greys, browns, tans
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.55f, 0.50f, 0.42f, 1f),  // Light stone
                new Color(0.35f, 0.30f, 0.25f, 1f)   // Dark stone
            );

            // ── Emission: manual only ──
            var emission = ps.emission;
            emission.enabled = false;

            // ── Shape: cone bursting upward and outward ──
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 50f;
            shape.radius = 1.5f;
            shape.position = new Vector3(0f, buildingHeight * 0.5f, 0f);

            // ── Rotation over lifetime: tumble in all axes ──
            var rotOverLife = ps.rotationOverLifetime;
            rotOverLife.enabled = true;
            rotOverLife.separateAxes = true;
            rotOverLife.x = new ParticleSystem.MinMaxCurve(-3f, 3f);
            rotOverLife.y = new ParticleSystem.MinMaxCurve(-3f, 3f);
            rotOverLife.z = new ParticleSystem.MinMaxCurve(-3f, 3f);

            // ── Collision: bounce off ground (approximated by stopping at Y=0) ──
            var collision = ps.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.bounce = 0.2f;
            collision.dampen = 0.4f;
            collision.lifetimeLoss = 0.3f;

            // ── Renderer: use mesh cubes for solid rock look ──
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = CreateDebrisMesh();
            renderer.material = GetDebrisMaterial();

            return ps;
        }

        /// <summary>
        /// Create a simple cube mesh for debris particles.
        /// </summary>
        private static Mesh _debrisMesh;
        private static Mesh CreateDebrisMesh()
        {
            if (_debrisMesh != null) return _debrisMesh;

            // Use a primitive cube mesh
            var tempCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _debrisMesh = tempCube.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tempCube);
            return _debrisMesh;
        }
    }
}
