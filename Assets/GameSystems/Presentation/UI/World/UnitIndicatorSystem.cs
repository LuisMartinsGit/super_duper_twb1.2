// UnitIndicatorSystem.cs
// Per-unit world indicators: selection ring, ownership disc, healing cross.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using TheWaningBorder.Rendering;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Input;
using TheWaningBorder.Rendering;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.World
{
    /// <summary>
    /// Per-unit world indicators.
    /// - Ownership disc: small disc on top of the unit, colored with its OWNER's
    ///   faction color (<see cref="FactionColors.Get"/>) — so the dot answers
    ///   "whose unit is this?" at a glance.
    /// - Selection ring: a ground ring under each selected unit, the same
    ///   LineRenderer treatment the Gatherer's Hut gather circle uses.
    /// - Healing cross: the green cross overlays the disc while a Litharch
    ///   is actively healing; the disc stays visible underneath it.
    ///
    /// FOG: every indicator here is a STANDALONE GameObject, not a child of the
    /// unit's presentation view — so FogVisibilitySyncSystem hiding the view
    /// left the indicators floating in the dark, marking out units the player
    /// could not see. They now follow the view's own visibility rather than
    /// re-deriving a fog rule of their own, so the two can never disagree.
    ///
    /// The direction arrow was removed 2026-08-15 (user request): a unit's
    /// facing is already readable from its model and animation.
    /// </summary>
    [DefaultExecutionOrder(920)] // After PresentationSpawnSystem (default)
    public class UnitIndicatorSystem : MonoBehaviour
    {
        /// <summary>Ring radius when the unit carries no Radius component.</summary>
        [SerializeField] private float defaultRingRadius = 0.7f;
        /// <summary>Ring radius as a multiple of the unit's sim Radius, so the
        /// ring reads as "this unit's footprint" rather than a fixed blob.</summary>
        [SerializeField] private float ringRadiusScale = 1.35f;
        [SerializeField, Range(0f, 1f)] private float ringAlpha = 0.9f;

        private const int RingSegments = 24;

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
            public DecalProjector Ring;
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

            // Instrumented (2026-08-16 perf sweep): full entity sweep + up to
            // four GameObjects per tracked unit, per frame, unthrottled.
            double perfT0 = Time.realtimeSinceStartupAsDouble;
            CleanupDestroyed();
            SpawnIndicators();
            UpdateIndicators();
            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("UnitIndicators",
                (Time.realtimeSinceStartupAsDouble - perfT0) * 1000.0);
        }

        void OnDestroy()
        {
            foreach (var kv in _indicators)
            {
                GroundDecals.Return(kv.Value.Ring);
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
                if (!_em.Exists(kv.Key) || kv.Value.Circle == null)
                    _toRemove.Add(kv.Key);
            }
            foreach (var e in _toRemove)
            {
                if (_indicators.TryGetValue(e, out var ind))
                {
                    GroundDecals.Return(ind.Ring);
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
                    Ring = CreateSelectionRing(),
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
            var selection = SelectionSystem.CurrentSelection;

            foreach (var kv in _indicators)
            {
                var entity = kv.Key;
                var ind = kv.Value;

                if (!_em.Exists(entity)) continue;
                if (!_em.HasComponent<LocalTransform>(entity)) continue;

                // FOG GATE. Follow the unit view's own visibility rather than
                // re-deriving a fog rule here: FogVisibilitySyncSystem already
                // owns that decision (own units always shown, enemies only in
                // line of sight), and a second rule would eventually disagree
                // with it. Without this the indicators were standalone objects
                // the fog never touched, so an enemy's ownership disc hung in
                // the dark exactly where the unit was — free intel.
                bool viewVisible = EntityViewManager.Instance != null
                    && EntityViewManager.Instance.TryGetView(entity, out var view)
                    && view != null && view.activeInHierarchy;

                if (!viewVisible)
                {
                    if (ind.Ring != null) ind.Ring.gameObject.SetActive(false);
                    if (ind.Circle != null) ind.Circle.SetActive(false);
                    if (ind.CrossH != null) ind.CrossH.SetActive(false);
                    if (ind.CrossV != null) ind.CrossV.SetActive(false);
                    continue;
                }

                var xf = _em.GetComponentData<LocalTransform>(entity);
                float3 pos = xf.Position;
                float terrainY = TerrainUtility.GetHeight(pos.x, pos.z);

                // ── Selection ring ──
                if (ind.Ring != null)
                {
                    bool selected = selection != null && selection.Contains(entity);
                    ind.Ring.gameObject.SetActive(selected);
                    if (selected)
                        UpdateSelectionRing(ind.Ring, entity, pos);
                }

                // ── Ownership disc ──
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

        /// <summary>
        /// Ground ring under a selected unit, as a projected DECAL.
        ///
        /// It was a LineRenderer laid out at one height, which meant the ring
        /// cut into rising ground and floated over falling ground. A decal is
        /// projected onto the terrain, so it follows the slope exactly and
        /// costs no height sampling at all.
        /// </summary>
        private DecalProjector CreateSelectionRing()
        {
            var ring = GroundDecals.Rent(GroundDecals.Ring(), Color.white);
            ring.gameObject.SetActive(false);
            return ring;
        }

        /// <summary>
        /// Size and colour the ring under a unit.
        ///
        /// No terrain sampling and no per-segment layout: the projector paints
        /// whatever ground is beneath it. The old LineRenderer needed a height
        /// and deliberately used ONE for the whole ring, because sampling per
        /// segment per selected unit per frame is one of this project's known
        /// hot costs. That trade-off is simply gone.
        /// </summary>
        private void UpdateSelectionRing(DecalProjector ring, Entity entity, float3 pos)
        {
            if (ring == null) return;

            float radius = defaultRingRadius;
            if (_em.HasComponent<Radius>(entity))
            {
                float r = _em.GetComponentData<Radius>(entity).Value;
                if (r > 0.01f) radius = r * ringRadiusScale;
            }

            var color = OwnerColor(entity);
            color.a = ringAlpha;

            GroundDecals.SetShape(ring, GroundDecals.Ring(), color);
            GroundDecals.Place(ring, new Vector3(pos.x, pos.y, pos.z), radius * 2f);
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
