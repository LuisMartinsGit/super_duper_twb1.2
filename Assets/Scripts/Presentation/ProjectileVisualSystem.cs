// File: Assets/Scripts/Presentation/ProjectileVisualSystem.cs
// Spawns and syncs visual GameObjects for arrow and laser projectile entities.
// Separate from PresentationSpawnSystem because projectiles:
// - Fly through the air (no terrain height snapping)
// - Are short-lived (~0.8s)
// - Don't need colliders or selection support

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;

namespace TheWaningBorder.Presentation
{
    public class ProjectileVisualSystem : MonoBehaviour
    {
        private Unity.Entities.World _world;
        private EntityManager _em;
        private EntityQuery _projectileQuery;

        // Track spawned visuals
        private readonly Dictionary<Entity, GameObject> _visuals = new();
        private readonly List<Entity> _toRemove = new();

        // Prefab templates (procedural)
        private GameObject _arrowTemplate;
        private GameObject _laserTemplate;

        void Awake()
        {
            _arrowTemplate = CreateArrowTemplate();
            _laserTemplate = CreateLaserTemplate();
        }

        void Start()
        {
            _world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
            {
                _em = _world.EntityManager;
                _projectileQuery = _em.CreateEntityQuery(
                    ComponentType.ReadOnly<ArrowProjectile>(),
                    ComponentType.ReadOnly<LocalTransform>()
                );
            }
        }

        void LateUpdate()
        {
            if (_world == null || !_world.IsCreated) return;

            CleanupDestroyed();
            SpawnMissing();
            SyncTransforms();
        }

        private void CleanupDestroyed()
        {
            _toRemove.Clear();

            foreach (var kvp in _visuals)
            {
                if (!_em.Exists(kvp.Key))
                    _toRemove.Add(kvp.Key);
            }

            foreach (var entity in _toRemove)
            {
                if (_visuals.TryGetValue(entity, out var go))
                {
                    if (go != null) Destroy(go);
                }
                _visuals.Remove(entity);
            }
        }

        private void SpawnMissing()
        {
            if (_projectileQuery == null) return;

            var entities = _projectileQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            var transforms = _projectileQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (_visuals.ContainsKey(entities[i])) continue;

                // Choose template based on projectile type
                bool isLaser = _em.HasComponent<LaserProjectileTag>(entities[i]);
                var template = isLaser ? _laserTemplate : _arrowTemplate;

                var go = Instantiate(template);
                go.SetActive(true);
                go.name = isLaser ? $"Laser_{entities[i].Index}" : $"Arrow_{entities[i].Index}";
                go.transform.position = (Vector3)transforms[i].Position;
                go.transform.rotation = transforms[i].Rotation;

                _visuals[entities[i]] = go;
            }

            entities.Dispose();
            transforms.Dispose();
        }

        private void SyncTransforms()
        {
            if (_projectileQuery == null) return;

            var entities = _projectileQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            var transforms = _projectileQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (_visuals.TryGetValue(entities[i], out var go) && go != null)
                {
                    // Direct position sync — NO terrain height snapping
                    go.transform.position = (Vector3)transforms[i].Position;
                    go.transform.rotation = transforms[i].Rotation;
                }
            }

            entities.Dispose();
            transforms.Dispose();
        }

        /// <summary>
        /// Creates a simple procedural arrow visual: a thin elongated cylinder (shaft)
        /// with a small cone-like tip using a scaled sphere.
        /// </summary>
        private GameObject CreateArrowTemplate()
        {
            var root = new GameObject("ArrowTemplate");
            root.SetActive(false);
            DontDestroyOnLoad(root);

            // Arrow shaft (thin cylinder along Z axis)
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shaft.name = "Shaft";
            shaft.transform.SetParent(root.transform);
            // Cylinder is Y-aligned by default; rotate to Z-aligned
            shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            shaft.transform.localPosition = new Vector3(0f, 0f, -0.2f);
            shaft.transform.localScale = new Vector3(0.04f, 0.4f, 0.04f);

            // Remove collider (not needed for visual)
            var shaftCol = shaft.GetComponent<Collider>();
            if (shaftCol != null) Destroy(shaftCol);

            // Arrow tip (small sphere at front)
            var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Tip";
            tip.transform.SetParent(root.transform);
            tip.transform.localPosition = new Vector3(0f, 0f, 0.25f);
            tip.transform.localScale = new Vector3(0.08f, 0.08f, 0.12f);

            var tipCol = tip.GetComponent<Collider>();
            if (tipCol != null) Destroy(tipCol);

            // Apply dark brown material to shaft
            var shaftRenderer = shaft.GetComponent<Renderer>();
            if (shaftRenderer != null)
            {
                shaftRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                shaftRenderer.material.color = new Color(0.35f, 0.22f, 0.1f); // dark wood
            }

            // Apply dark grey/iron material to tip
            var tipRenderer = tip.GetComponent<Renderer>();
            if (tipRenderer != null)
            {
                tipRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                tipRenderer.material.color = new Color(0.3f, 0.3f, 0.32f); // iron
            }

            return root;
        }

        /// <summary>
        /// Creates a laser beam visual: a thin glowing cylinder elongated along the Z axis
        /// with a bright purple/violet emission glow.
        /// </summary>
        private GameObject CreateLaserTemplate()
        {
            var root = new GameObject("LaserTemplate");
            root.SetActive(false);
            DontDestroyOnLoad(root);

            // Laser beam core (thick elongated cylinder along Z axis)
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            beam.name = "BeamCore";
            beam.transform.SetParent(root.transform);
            beam.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            beam.transform.localPosition = Vector3.zero;
            beam.transform.localScale = new Vector3(0.14f, 0.7f, 0.14f);

            var beamCol = beam.GetComponent<Collider>();
            if (beamCol != null) Destroy(beamCol);

            // Laser glow (larger transparent cylinder halo)
            var glow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            glow.name = "BeamGlow";
            glow.transform.SetParent(root.transform);
            glow.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            glow.transform.localPosition = Vector3.zero;
            glow.transform.localScale = new Vector3(0.30f, 0.7f, 0.30f);

            var glowCol = glow.GetComponent<Collider>();
            if (glowCol != null) Destroy(glowCol);

            // Purple laser colors (bright, high-energy)
            var coreColor = new Color(0.85f, 0.35f, 1.0f);
            var emissionColor = new Color(0.70f, 0.25f, 0.90f);
            var glowColor = new Color(0.55f, 0.15f, 0.75f, 0.5f);

            // Core material: bright purple with strong emission
            var beamRenderer = beam.GetComponent<Renderer>();
            if (beamRenderer != null)
            {
                var mat = new Material(
                    Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = coreColor;
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emissionColor * 5f);
                }
                beamRenderer.material = mat;
            }

            // Glow material: semi-transparent purple halo
            var glowRenderer = glow.GetComponent<Renderer>();
            if (glowRenderer != null)
            {
                var mat = new Material(
                    Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = glowColor;

                // Enable transparency
                if (mat.HasProperty("_Surface"))
                {
                    mat.SetFloat("_Surface", 1f); // Transparent
                    mat.SetFloat("_Blend", 0f);
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    mat.SetInt("_ZWrite", 0);
                    mat.renderQueue = 3000;
                }

                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emissionColor * 2f);
                }

                glowRenderer.material = mat;
            }

            // Tip glow point (bright sphere at front)
            var tipGlow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tipGlow.name = "TipGlow";
            tipGlow.transform.SetParent(root.transform);
            tipGlow.transform.localPosition = new Vector3(0f, 0f, 0.45f);
            tipGlow.transform.localScale = Vector3.one * 0.18f;

            var tipCol = tipGlow.GetComponent<Collider>();
            if (tipCol != null) Destroy(tipCol);

            var tipRenderer = tipGlow.GetComponent<Renderer>();
            if (tipRenderer != null)
            {
                var mat = new Material(
                    Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = new Color(0.95f, 0.70f, 1.0f); // Bright white-lavender
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", new Color(0.90f, 0.50f, 1.0f) * 6f);
                }
                tipRenderer.material = mat;
            }

            return root;
        }

        void OnDestroy()
        {
            // Clean up all visuals
            foreach (var kvp in _visuals)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _visuals.Clear();

            if (_arrowTemplate != null) Destroy(_arrowTemplate);
            if (_laserTemplate != null) Destroy(_laserTemplate);
        }
    }
}
