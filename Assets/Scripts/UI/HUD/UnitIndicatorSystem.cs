// UnitIndicatorSystem.cs
// Shows per-unit direction arrows and state-colored circles
// Location: Assets/Scripts/UI/HUD/UnitIndicatorSystem.cs

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Input;
using TheWaningBorder.Presentation;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Attaches a direction arrow and an ownership disc to every unit with a visual.
    /// - Direction arrow: thin cone pointing along the unit's forward axis.
    /// - Ownership disc: small disc on top of the unit, colored with its OWNER's
    ///   faction color (<see cref="FactionColors.Get"/>) — so the dot answers
    ///   "whose unit is this?" at a glance. It previously encoded transient unit
    ///   state (idle/moving/attacking), which duplicated information already
    ///   readable from the unit's animation and gave no ownership cue at all.
    /// - Healing cross: the green cross still overlays the disc while a Litharch
    ///   is actively healing; the disc stays visible underneath it.
    /// </summary>
    [DefaultExecutionOrder(920)] // After PresentationSpawnSystem (default)
    public class UnitIndicatorSystem : MonoBehaviour
    {
        [Header("Direction Arrow")]
        [SerializeField] private float arrowLength = 0.8f;
        [SerializeField] private float arrowWidth = 0.15f;
        [SerializeField] private float arrowYOffset = 0.06f;
        [SerializeField] private Color arrowColor = new Color(1f, 1f, 1f, 0.7f);

        [Header("Ownership Disc")]
        [SerializeField] private float circleRadius = 0.15f;
        [SerializeField] private float circleYAboveUnit = 1.6f;
        [SerializeField] private float circleThickness = 0.02f;

        /// <summary>Opacity of the ownership disc. The faction palette is fully
        /// opaque, so alpha is applied here rather than baked into the palette.</summary>
        [SerializeField, Range(0f, 1f)] private float circleAlpha = 0.85f;

        /// <summary>Owner color for entities with no FactionTag (neutral/creature).</summary>
        private static readonly Color UnownedColor = new Color(0.7f, 0.7f, 0.7f, 0.8f);

        private static readonly Color HealingColor = new Color(0.1f, 0.9f, 0.2f, 0.9f);

        private EntityWorld _world;
        private EntityManager _em;
        private Material _baseMat;

        // Cached queries — CreateEntityQuery per frame leaks into the world's query registry.
        private static readonly ComponentType[] VisibleUnitQueryTypes =
        {
            ComponentType.ReadOnly<PresentationId>(),
            ComponentType.ReadOnly<LocalTransform>(),
            ComponentType.ReadOnly<UnitTag>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _visibleUnitQuery;

        private struct Indicators
        {
            public GameObject Arrow;
            public GameObject Circle;
            public MeshRenderer CircleRenderer;
            public GameObject CrossH;       // Horizontal bar of the cross
            public GameObject CrossV;       // Vertical bar of the cross
            public MeshRenderer CrossHRenderer;
            public MeshRenderer CrossVRenderer;
        }

        private readonly Dictionary<Entity, Indicators> _indicators = new();
        private readonly List<Entity> _toRemove = new();

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world != null && _world.IsCreated)
                _em = _world.EntityManager;

            _baseMat = CreateBaseMaterial();
        }

        void LateUpdate()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
                _em = _world.EntityManager;
            }
            if (EntityViewManager.Instance == null) return;

            CleanupDestroyed();
            SpawnIndicators();
            UpdateIndicators();
        }

        void OnDestroy()
        {
            foreach (var kv in _indicators)
            {
                if (kv.Value.Arrow != null) Destroy(kv.Value.Arrow);
                if (kv.Value.Circle != null) Destroy(kv.Value.Circle);
                if (kv.Value.CrossH != null) Destroy(kv.Value.CrossH);
                if (kv.Value.CrossV != null) Destroy(kv.Value.CrossV);
            }
            _indicators.Clear();
            if (_baseMat != null) Destroy(_baseMat);
        }

        // ═══════════════════════════════════════════════════════════════
        // CLEANUP
        // ═══════════════════════════════════════════════════════════════

        private void CleanupDestroyed()
        {
            _toRemove.Clear();
            foreach (var kv in _indicators)
            {
                if (!_em.Exists(kv.Key) || kv.Value.Arrow == null)
                    _toRemove.Add(kv.Key);
            }
            foreach (var e in _toRemove)
            {
                if (_indicators.TryGetValue(e, out var ind))
                {
                    if (ind.Arrow != null) Destroy(ind.Arrow);
                    if (ind.Circle != null) Destroy(ind.Circle);
                    if (ind.CrossH != null) Destroy(ind.CrossH);
                    if (ind.CrossV != null) Destroy(ind.CrossV);
                }
                _indicators.Remove(e);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SPAWNING
        // ═══════════════════════════════════════════════════════════════

        private void SpawnIndicators()
        {
            if (EntityViewManager.Instance == null) return;

            // Iterate tracked entities from EntityViewManager isn't possible (no public enumeration),
            // so use the PresentationId query to find all visible units
            var query = _visibleUnitQuery.Get(_em, VisibleUnitQueryTypes);

            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (_indicators.ContainsKey(entity)) continue;

                // Only create if the entity has a visual GameObject
                if (!EntityViewManager.Instance.TryGetView(entity, out _)) continue;

                var ind = new Indicators
                {
                    Arrow = CreateArrow(),
                    Circle = CreateCircle(out var circleRenderer),
                    CircleRenderer = circleRenderer,
                    CrossH = CreateCrossBar(out var crossHR),
                    CrossV = CreateCrossBar(out var crossVR),
                    CrossHRenderer = crossHR,
                    CrossVRenderer = crossVR
                };
                // Rotate vertical bar 90° around Y to form the cross
                ind.CrossV.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                // Cross starts hidden
                ind.CrossH.SetActive(false);
                ind.CrossV.SetActive(false);
                _indicators[entity] = ind;
            }

            entities.Dispose();
        }

        // ═══════════════════════════════════════════════════════════════
        // UPDATE
        // ═══════════════════════════════════════════════════════════════

        private void UpdateIndicators()
        {
            foreach (var kv in _indicators)
            {
                var entity = kv.Key;
                var ind = kv.Value;

                if (!_em.Exists(entity)) continue;
                if (!_em.HasComponent<LocalTransform>(entity)) continue;

                var xf = _em.GetComponentData<LocalTransform>(entity);
                float3 pos = xf.Position;
                float terrainY = TerrainUtility.GetHeight(pos.x, pos.z);

                // ── Direction Arrow ──
                if (ind.Arrow != null)
                {
                    // Position on ground in front of unit
                    float3 forward = math.mul(xf.Rotation, new float3(0, 0, 1));
                    forward.y = 0;
                    forward = math.normalizesafe(forward, new float3(0, 0, 1));

                    Vector3 arrowPos = new Vector3(pos.x, terrainY + arrowYOffset, pos.z);
                    arrowPos += (Vector3)(forward * arrowLength * 0.5f);

                    ind.Arrow.transform.position = arrowPos;
                    // Arrow points along forward direction (quad is on XZ plane, default faces Y up)
                    float angle = math.degrees(math.atan2(forward.x, forward.z));
                    ind.Arrow.transform.rotation = Quaternion.Euler(90f, angle, 0f);
                }

                // ── Ownership disc ──
                // Always visible, always the owner's faction color.
                Vector3 indicatorPos = new Vector3(pos.x, terrainY + circleYAboveUnit, pos.z);

                if (ind.Circle != null)
                {
                    ind.Circle.SetActive(true);
                    ind.Circle.transform.position = indicatorPos;
                    SetMaterialColor(ind.CircleRenderer.sharedMaterial, OwnerColor(entity));
                }

                // ── Healing cross ──
                // Overlaid ABOVE the ownership disc rather than replacing it, so
                // a unit being healed still shows who owns it.
                bool isHealing = IsHealing(entity);
                Vector3 crossPos = indicatorPos + Vector3.up * (circleRadius * 1.6f);

                if (ind.CrossH != null)
                {
                    ind.CrossH.SetActive(isHealing);
                    if (isHealing)
                    {
                        ind.CrossH.transform.position = crossPos;
                        SetMaterialColor(ind.CrossHRenderer.sharedMaterial, HealingColor);
                    }
                }
                if (ind.CrossV != null)
                {
                    ind.CrossV.SetActive(isHealing);
                    if (isHealing)
                    {
                        ind.CrossV.transform.position = crossPos;
                        SetMaterialColor(ind.CrossVRenderer.sharedMaterial, HealingColor);
                    }
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // OWNERSHIP / STATE
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// The disc color: the owning faction's palette color, at the disc's
        /// configured opacity. Same source as the minimap blips and health bars,
        /// so a unit's dot always matches its player color everywhere else.
        /// </summary>
        private Color OwnerColor(Entity entity)
        {
            if (!_em.HasComponent<FactionTag>(entity)) return UnownedColor;

            var c = FactionColors.Get(_em.GetComponentData<FactionTag>(entity).Value);
            c.a = circleAlpha;
            return c;
        }

        /// <summary>Litharch actively channeling a heal on a live target.</summary>
        private bool IsHealing(Entity entity)
        {
            if (!_em.HasComponent<LitharchState>(entity)) return false;

            var ls = _em.GetComponentData<LitharchState>(entity);
            return ls.IsHealing != 0 && ls.HealTarget != Entity.Null && _em.Exists(ls.HealTarget);
        }

        // ═══════════════════════════════════════════════════════════════
        // FACTORY HELPERS
        // ═══════════════════════════════════════════════════════════════

        private GameObject CreateArrow()
        {
            // Thin quad on the ground pointing forward
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "DirectionArrow";
            go.transform.localScale = new Vector3(arrowWidth, arrowLength, 1f);

            // Remove collider
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var mr = go.GetComponent<MeshRenderer>();
            var mat = new Material(_baseMat);
            SetMaterialColor(mat, arrowColor);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return go;
        }

        private GameObject CreateCircle(out MeshRenderer renderer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "OwnershipDisc";
            go.transform.localScale = new Vector3(circleRadius * 2f, circleThickness, circleRadius * 2f);

            // Remove collider
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            renderer = go.GetComponent<MeshRenderer>();
            var mat = new Material(_baseMat);
            SetMaterialColor(mat, UnownedColor);
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return go;
        }

        /// <summary>
        /// Creates one bar of the healing cross (a thin flat quad).
        /// Two bars at 90° form the cross shape.
        /// </summary>
        private GameObject CreateCrossBar(out MeshRenderer renderer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "CrossBar";
            // Thin flat bar: wide on X, thin on Y, narrow on Z (one bar)
            // The horizontal bar: scale (0.3, 0.04, 0.1)
            // The vertical bar will be rotated 90°
            go.transform.localScale = new Vector3(circleRadius * 2.5f, circleThickness, circleRadius * 0.7f);

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            renderer = go.GetComponent<MeshRenderer>();
            var mat = new Material(_baseMat);
            SetMaterialColor(mat, HealingColor);
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            return go;
        }

        private Material CreateBaseMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1); // Transparent
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0);
            mat.renderQueue = 3100;
            return mat;
        }

        private static void SetMaterialColor(Material mat, Color color)
        {
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        }
    }
}
