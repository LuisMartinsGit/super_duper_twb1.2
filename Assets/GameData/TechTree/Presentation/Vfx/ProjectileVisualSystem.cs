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

        // Prefab templates (procedural fallback + authored MagicArsenal wrappers).
        private GameObject _arrowTemplate;
        private GameObject _laserTemplate;        // generic laser (e.g. tower beams)
        private GameObject _veilstingerTemplate;  // arcane-purple SMALL missile, arcs in
        private GameObject _godsplinterTemplate;  // arcane-purple MEGA missile, straight beam
        private GameObject _impactTemplate;       // tiny arcane explosion, spawned on Veilstinger hit
        private GameObject _fireballTemplate;     // Synty catapult fire FX, scaled way down (Feraldis Firethrower)

        /// <summary>Scale applied to the Synty catapult shot effect when it is
        /// reused as a Firethrower fireball. The authored effect is a siege
        /// boulder; the Firethrower hurls something a man can throw.</summary>
        private const float FirethrowerFxScale = 0.22f;

        // Tracks which projectile visuals should spawn an impact VFX when they
        // die, and at what visual scale. Set at spawn time so we don't have to
        // ask the ECS world after the entity is already destroyed. Scale 1
        // matches the authored Veilstinger impact size; siege shots (Godsplinter)
        // scale up so the blast visually reads as a wide-radius AOE.
        private readonly Dictionary<Entity, float> _impactScales = new();


        void Awake()
        {
            _arrowTemplate       = CreateArrowTemplate();
            _veilstingerTemplate = LoadAuthoredTemplate("Prefabs/Border/Effects/VeilstingerLaser", "VeilstingerTemplate");
            _godsplinterTemplate = LoadAuthoredTemplate("Prefabs/Border/Effects/GodsplinterLaser", "GodsplinterTemplate");
            _impactTemplate      = LoadAuthoredTemplate("Prefabs/Border/Effects/VeilstingerImpact", "VeilstingerImpactTemplate");
            // Feraldis Firethrower reuses the Synty catapult fire effect.
            _fireballTemplate    = LoadAuthoredTemplate("Prefabs/Effects/FX_CatapultShot", "FirethrowerFxTemplate");
            // Generic laser falls back to the Veilstinger missile if present,
            // else to the procedural cylinder. Anything that still tags itself
            // LaserProjectileTag without a more-specific tag (e.g. building
            // turrets) gets a sensible arcane look rather than the old cylinder.
            _laserTemplate = _veilstingerTemplate != null
                ? _veilstingerTemplate
                : CreateLaserTemplate();
        }

        /// <summary>
        /// Load an authored prefab from Resources and keep an inactive scene
        /// instance as the template. Returns null if the resource isn't found —
        /// callers decide whether to fall back to a procedural template.
        /// </summary>
        private GameObject LoadAuthoredTemplate(string resourcePath, string templateName)
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null) return null;

            var template = Instantiate(prefab);
            template.name = templateName;
            template.SetActive(false);
            DontDestroyOnLoad(template);
            return template;
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
                    // Spawn impact VFX at projectile death — Veilstinger gets a
                    // tiny arcane pop at scale 1; Godsplinter shells reuse the
                    // same prefab scaled up so the explosion visually covers
                    // the full AOE radius (see _impactScales). The prefab plays
                    // its own particle/audio one-shot and self-destroys via its
                    // ParticleSystem lifetimes.
                    if (_impactScales.TryGetValue(entity, out var impactScale)
                        && _impactTemplate != null && go != null)
                    {
                        var impact = Instantiate(_impactTemplate, go.transform.position, Quaternion.identity);
                        impact.SetActive(true);
                        if (impactScale > 0f && !Mathf.Approximately(impactScale, 1f))
                            impact.transform.localScale = Vector3.one * impactScale;
                        // Hand the impact ~3 s to play out then collect it.
                        Destroy(impact, 3f);
                    }

                    if (go != null) Destroy(go);
                }
                _visuals.Remove(entity);
                _impactScales.Remove(entity);
            }
        }

        private void SpawnMissing()
        {
            if (_projectileQuery == null) return;

            var entities = _projectileQuery.ToEntityArray(Unity.Collections.Allocator.Temp);
            var transforms = _projectileQuery.ToComponentDataArray<LocalTransform>(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (_visuals.ContainsKey(entity)) continue;

                // Template priority: specific (Godsplinter / Veilstinger) wins
                // over generic LaserProjectileTag, which wins over the default
                // arrow. Specific tags also opt the visual in for the impact-VFX
                // spawn on death, with a per-projectile scale.
                GameObject template;
                string namePrefix;
                float impactScale = 0f; // 0 = no impact

                if (_em.HasComponent<CatapultShotTag>(entity))
                {
                    // Catapult shots have NO per-entity visual: CatapultVisual
                    // fires the self-contained Synty FX (stone + trail +
                    // impact) from the engine itself, aimed by distance. The
                    // ECS projectile only carries the damage. Remember the
                    // entity so this check doesn't repeat every frame.
                    _visuals[entity] = null;
                    continue;
                }

                if (_em.HasComponent<FirethrowerShotTag>(entity) && _fireballTemplate != null)
                {
                    // Feraldis Firethrower: the Synty catapult fire effect, way
                    // down in scale — a hurled fireball, not a boulder.
                    template = _fireballTemplate;
                    namePrefix = "Fireball";
                }
                else if (_em.HasComponent<GodsplinterProjectileTag>(entity) && _godsplinterTemplate != null)
                {
                    template = _godsplinterTemplate;
                    namePrefix = "GodSplinterShell";
                    // Scale the impact VFX to match the AOEProjectile.Radius so
                    // the blast visually covers the full splash zone. The base
                    // Veilstinger-impact prefab reads as roughly a 1.5 m pop,
                    // so AOE-radius / 1.5 gives a visual that fills the radius.
                    const float VeilstingerImpactBaseSize = 1.5f;
                    float aoeR = _em.HasComponent<AOEProjectile>(entity)
                        ? _em.GetComponentData<AOEProjectile>(entity).Radius
                        : 6f;
                    impactScale = Mathf.Max(1f, aoeR / VeilstingerImpactBaseSize);
                }
                else if (_em.HasComponent<VeilstingerProjectileTag>(entity) && _veilstingerTemplate != null)
                {
                    template = _veilstingerTemplate;
                    namePrefix = "VeilstingerMissile";
                    impactScale = 1f;
                }
                else if (_em.HasComponent<LaserProjectileTag>(entity))
                {
                    template = _laserTemplate;
                    namePrefix = "Laser";
                }
                else
                {
                    template = _arrowTemplate;
                    namePrefix = "Arrow";
                }

                var go = Instantiate(template);
                go.SetActive(true);
                go.name = $"{namePrefix}_{entity.Index}";
                go.transform.position = (Vector3)transforms[i].Position;
                go.transform.rotation = transforms[i].Rotation;

                // Scale up siege projectiles (ballista bolts) for visual distinction —
                // only applies to plain arrows, not to the specialised tags above.
                bool isPlainArrow = template == _arrowTemplate;
                if (isPlainArrow && _em.HasComponent<Projectile>(entity))
                {
                    var proj = _em.GetComponentData<Projectile>(entity);
                    if (proj.DmgType == DamageType.Siege)
                    {
                        go.transform.localScale = Vector3.one * 2.5f;
                        go.name = $"Bolt_{entity.Index}";
                    }
                }

                // Godsplinter shells are massive siege ordnance — scale 2× over
                // the base Veilstinger-derived missile template so they read as
                // distinct, threatening artillery rounds in flight.
                if (template == _godsplinterTemplate)
                {
                    go.transform.localScale = Vector3.one * 2.0f;
                }

                // Firethrower fireballs are the catapult FX at a fraction of
                // its authored size.
                if (template == _fireballTemplate)
                {
                    go.transform.localScale = Vector3.one * FirethrowerFxScale;
                }


                _visuals[entity] = go;
                if (impactScale > 0f) _impactScales[entity] = impactScale;
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
