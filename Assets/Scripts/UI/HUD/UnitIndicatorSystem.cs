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
    /// Attaches a direction arrow and state circle to every unit with a visual.
    /// - Direction arrow: thin cone pointing along the unit's forward axis.
    /// - State circle: small disc on top of the unit that changes color:
    ///     White  = idle
    ///     Blue   = moving
    ///     Yellow = pursuing
    ///     Red    = attacking (in range / dealing damage)
    ///     Magenta = taking damage
    /// </summary>
    [DefaultExecutionOrder(920)] // After PresentationSpawnSystem (default) and SelectionRings (900)
    public class UnitIndicatorSystem : MonoBehaviour
    {
        [Header("Direction Arrow")]
        [SerializeField] private float arrowLength = 0.8f;
        [SerializeField] private float arrowWidth = 0.15f;
        [SerializeField] private float arrowYOffset = 0.06f;
        [SerializeField] private Color arrowColor = new Color(1f, 1f, 1f, 0.7f);

        [Header("State Circle")]
        [SerializeField] private float circleRadius = 0.15f;
        [SerializeField] private float circleYAboveUnit = 1.6f;
        [SerializeField] private float circleThickness = 0.02f;

        // State colors
        private static readonly Color IdleColor = new Color(0.85f, 0.85f, 0.85f, 0.8f);
        private static readonly Color MovingColor = new Color(0.2f, 0.5f, 1f, 0.8f);
        private static readonly Color PursuingColor = new Color(1f, 0.85f, 0.1f, 0.8f);
        private static readonly Color AttackingColor = new Color(1f, 0.15f, 0.15f, 0.8f);
        private static readonly Color TakingDamageColor = new Color(1f, 0.1f, 0.8f, 0.9f);

        private EntityWorld _world;
        private EntityManager _em;
        private Material _baseMat;

        private struct Indicators
        {
            public GameObject Arrow;
            public GameObject Circle;
            public MeshRenderer CircleRenderer;
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
            var query = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PresentationId>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<UnitTag>()
            );

            var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (_indicators.ContainsKey(entity)) continue;

                // Skip battalion leaders (invisible)
                if (_em.HasComponent<BattalionLeader>(entity)) continue;

                // Only create if the entity has a visual GameObject
                if (!EntityViewManager.Instance.TryGetView(entity, out _)) continue;

                var ind = new Indicators
                {
                    Arrow = CreateArrow(),
                    Circle = CreateCircle(out var circleRenderer),
                    CircleRenderer = circleRenderer
                };
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

                // ── State Circle ──
                if (ind.Circle != null)
                {
                    Vector3 circlePos = new Vector3(pos.x, terrainY + circleYAboveUnit, pos.z);
                    ind.Circle.transform.position = circlePos;

                    Color stateColor = DetermineStateColor(entity);
                    SetMaterialColor(ind.CircleRenderer.sharedMaterial, stateColor);
                }
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // STATE DETECTION
        // ═══════════════════════════════════════════════════════════════

        private Color DetermineStateColor(Entity entity)
        {
            // Taking damage (highest priority — flashes briefly since LastAttackerEntity is cleared each frame)
            if (_em.HasComponent<LastAttackerEntity>(entity))
                return TakingDamageColor;

            bool hasTarget = _em.HasComponent<Target>(entity) &&
                             _em.GetComponentData<Target>(entity).Value != Entity.Null;

            bool isMoving = false;
            if (_em.HasComponent<DesiredDestination>(entity))
            {
                isMoving = _em.GetComponentData<DesiredDestination>(entity).Has != 0;
            }
            // Battalion members don't have DesiredDestination — check if leader is moving
            else if (_em.HasComponent<BattalionMemberData>(entity))
            {
                var leader = _em.GetComponentData<BattalionMemberData>(entity).Leader;
                if (_em.Exists(leader) && _em.HasComponent<DesiredDestination>(leader))
                    isMoving = _em.GetComponentData<DesiredDestination>(leader).Has != 0;
            }

            if (hasTarget && !isMoving)
                return AttackingColor;   // In range, fighting
            if (hasTarget && isMoving)
                return PursuingColor;    // Chasing target
            if (isMoving)
                return MovingColor;      // Moving to destination

            return IdleColor;
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
            go.name = "StateCircle";
            go.transform.localScale = new Vector3(circleRadius * 2f, circleThickness, circleRadius * 2f);

            // Remove collider
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            renderer = go.GetComponent<MeshRenderer>();
            var mat = new Material(_baseMat);
            SetMaterialColor(mat, IdleColor);
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
