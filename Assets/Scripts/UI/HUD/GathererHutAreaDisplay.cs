// File: Assets/Scripts/UI/HUD/GathererHutAreaDisplay.cs
// Displays the 15-unit resource gathering radius around GathererHuts
// Shows when: placing a GathererHut, or when one is selected
// During placement, shows expected income percentage tooltip.

using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Economy;
using TheWaningBorder.Input;
using TheWaningBorder.UI.Panels;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    [DefaultExecutionOrder(905)]
    public class GathererHutAreaDisplay : MonoBehaviour
    {
        private const float GatherRadius = GathererHutIncomeSystem.GatherRadius;
        private const int CircleSegments = 64;
        private const float BasePerTick = 15f;

        [Header("Display")]
        [SerializeField] private Color circleColor = new Color(0.2f, 0.8f, 0.3f, 0.6f);
        [SerializeField] private Color overlapColor = new Color(0.8f, 0.2f, 0.2f, 0.4f);
        [SerializeField] private float lineWidth = 0.15f;

        private EntityWorld _world;
        private EntityManager _em;

        // Pool of circle renderers
        private readonly List<LineRenderer> _activeCircles = new();
        private readonly List<LineRenderer> _pool = new();

        // Placement preview circle
        private LineRenderer _placementCircle;

        private Material _circleMat;

        // Placement tooltip state
        private bool _showTooltip;
        private float _placementRatio;
        private GUIStyle _tooltipStyle;

        // Cached PlacementPreview GameObject. GameObject.Find() was previously
        // called every LateUpdate while placing — Find() walks the entire
        // active scene each call. Cache once per placement, invalidate when
        // the cached ref dies or placement ends. (task-062 Q-29)
        private GameObject _cachedPreview;

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;

            // URP-compatible unlit material
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            _circleMat = new Material(shader);
            if (_circleMat.HasProperty("_Surface")) _circleMat.SetFloat("_Surface", 1); // Transparent
            if (_circleMat.HasProperty("_Blend")) _circleMat.SetFloat("_Blend", 0);
            _circleMat.renderQueue = 3000;
        }

        void LateUpdate()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
            }
            _em = _world.EntityManager;

            // Return all active circles to pool
            foreach (var lr in _activeCircles)
            {
                lr.gameObject.SetActive(false);
                _pool.Add(lr);
            }
            _activeCircles.Clear();

            _showTooltip = false;

            // ============================================================
            // Show circles for selected GathererHuts
            // ============================================================
            var selection = SelectionSystem.CurrentSelection;
            if (selection != null)
            {
                foreach (var entity in selection)
                {
                    if (!_em.Exists(entity)) continue;
                    if (!_em.HasComponent<GathererHutTag>(entity)) continue;
                    if (!_em.HasComponent<LocalTransform>(entity)) continue;

                    var pos = _em.GetComponentData<LocalTransform>(entity).Position;
                    var circle = GetOrCreateCircle();
                    SetCircle(circle, new Vector3(pos.x, 0, pos.z), circleColor);
                    _activeCircles.Add(circle);
                }
            }

            // ============================================================
            // Show circle during GathererHut placement preview
            // ============================================================
            if (BuilderCommandPanel.IsPlacingBuilding && IsPlacingGathererHutType)
            {
                // Cache lookup — see _cachedPreview field comment.
                if (_cachedPreview == null)
                    _cachedPreview = GameObject.Find("PlacementPreview");
                if (_cachedPreview != null)
                {
                    var previewPos = _cachedPreview.transform.position;
                    if (_placementCircle == null)
                        _placementCircle = CreateCircleRenderer();
                    _placementCircle.gameObject.SetActive(true);
                    SetCircle(_placementCircle, new Vector3(previewPos.x, 0, previewPos.z), circleColor);

                    // Calculate expected income ratio at this position
                    _placementRatio = CalculatePlacementRatio(new float2(previewPos.x, previewPos.z));
                    _showTooltip = true;
                }

                ShowAllExistingHutCircles();
            }
            else
            {
                // Drop the cache when placement ends so the next placement
                // re-resolves cleanly. (task-062 Q-29)
                _cachedPreview = null;
                if (_placementCircle != null)
                    _placementCircle.gameObject.SetActive(false);
            }
        }

        void OnGUI()
        {
            if (!_showTooltip) return;

            if (_tooltipStyle == null)
            {
                _tooltipStyle = new GUIStyle(GUI.skin.box);
                _tooltipStyle.fontSize = 14;
                _tooltipStyle.alignment = TextAnchor.MiddleCenter;
                _tooltipStyle.normal.textColor = Color.white;
                _tooltipStyle.fontStyle = FontStyle.Bold;
                _tooltipStyle.padding = new RectOffset(8, 8, 4, 4);
            }

            int pct = Mathf.RoundToInt(_placementRatio * 100f);
            float perTick = BasePerTick * _placementRatio;
            string text = $"Income: {pct}%  ({perTick:F1}/tick)";

            // Color based on ratio: green → yellow → red
            if (pct >= 80) _tooltipStyle.normal.textColor = new Color(0.4f, 1f, 0.4f);
            else if (pct >= 40) _tooltipStyle.normal.textColor = new Color(1f, 1f, 0.3f);
            else _tooltipStyle.normal.textColor = new Color(1f, 0.4f, 0.4f);

            var content = new GUIContent(text);
            var size = _tooltipStyle.CalcSize(content);

            // Position above cursor
            float x = Event.current.mousePosition.x - size.x * 0.5f;
            float y = Event.current.mousePosition.y - size.y - 20f;
            x = Mathf.Clamp(x, 0, Screen.width - size.x);
            y = Mathf.Max(y, 0);

            GUI.Box(new Rect(x, y, size.x, size.y), content, _tooltipStyle);
        }

        /// <summary>
        /// Calculate what income ratio a new farm would get at this position.
        /// Uses grid-sampling: iterates PassabilityGrid cells within GatherRadius
        /// and excludes cells that are terrain/building-blocked, inside existing
        /// same-faction or enemy hut circles, or inside wall enclosure polygons.
        /// Since this is a new farm, all existing farms have priority.
        /// </summary>
        private float CalculatePlacementRatio(float2 placementPos)
        {
            var grid = PassabilityGrid.Instance;
            if (grid == null)
                return CalculatePlacementRatioFallback(placementPos);

            float radiusSq = GatherRadius * GatherRadius;

            // Determine cell scan bounds
            int2 minCell = grid.WorldToCell(new float3(placementPos.x - GatherRadius, 0f, placementPos.y - GatherRadius));
            int2 maxCell = grid.WorldToCell(new float3(placementPos.x + GatherRadius, 0f, placementPos.y + GatherRadius));
            minCell = math.max(minCell, int2.zero);
            maxCell = math.min(maxCell, new int2(grid.Width - 1, grid.Height - 1));

            // Snapshot existing GathererHuts
            var hutQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());

            var hutTransforms = hutQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var hutFactions = hutQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Snapshot wall enclosure polygons
            var enclosureQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<WallEnclosureIncomeTag>());
            var enclosureEntities = enclosureQuery.ToEntityArray(Allocator.Temp);

            int totalCells = 0;
            int freeCells = 0;

            for (int cy = minCell.y; cy <= maxCell.y; cy++)
            {
                for (int cx = minCell.x; cx <= maxCell.x; cx++)
                {
                    var cell = new int2(cx, cy);
                    float3 cellWorld = grid.CellToWorld(cell);
                    float2 cellPos = new float2(cellWorld.x, cellWorld.z);

                    // Check if cell is within the gather circle
                    float dx = cellPos.x - placementPos.x;
                    float dz = cellPos.y - placementPos.y;
                    if (dx * dx + dz * dz > radiusSq)
                        continue;

                    totalCells++;

                    // Exclusion 1: Terrain-blocked or building-blocked
                    byte cellValue = grid.GetCell(cell);
                    if (cellValue != PassabilityGrid.Passable)
                        continue;

                    // Exclusion 2: Inside any same-faction GathererHut circle
                    // (all existing are "older" since this is a new placement)
                    bool excluded = false;
                    for (int i = 0; i < hutTransforms.Length; i++)
                    {
                        if (hutFactions[i].Value != GameSettings.LocalPlayerFaction) continue;

                        var hPos = new float2(hutTransforms[i].Position.x, hutTransforms[i].Position.z);
                        float hdx = cellPos.x - hPos.x;
                        float hdz = cellPos.y - hPos.y;
                        if (hdx * hdx + hdz * hdz <= radiusSq)
                        {
                            excluded = true;
                            break;
                        }
                    }
                    if (excluded) continue;

                    // Exclusion 3: Inside any enemy GathererHut circle
                    for (int i = 0; i < hutTransforms.Length; i++)
                    {
                        if (hutFactions[i].Value == GameSettings.LocalPlayerFaction) continue;

                        var hPos = new float2(hutTransforms[i].Position.x, hutTransforms[i].Position.z);
                        float hdx = cellPos.x - hPos.x;
                        float hdz = cellPos.y - hPos.y;
                        if (hdx * hdx + hdz * hdz <= radiusSq)
                        {
                            excluded = true;
                            break;
                        }
                    }
                    if (excluded) continue;

                    // Exclusion 4: Inside any wall enclosure polygon
                    for (int e = 0; e < enclosureEntities.Length; e++)
                    {
                        if (!_em.HasBuffer<WallEnclosureVertex>(enclosureEntities[e]))
                            continue;

                        var vertices = _em.GetBuffer<WallEnclosureVertex>(enclosureEntities[e]);
                        if (vertices.Length < 3) continue;

                        if (GathererHutIncomeSystem.PointInPolygon(cellPos, vertices))
                        {
                            excluded = true;
                            break;
                        }
                    }
                    if (excluded) continue;

                    freeCells++;
                }
            }

            hutTransforms.Dispose();
            hutFactions.Dispose();
            enclosureEntities.Dispose();

            if (totalCells == 0) return 0f;
            return (float)freeCells / totalCells;
        }

        /// <summary>
        /// Fallback geometric calculation when PassabilityGrid is not available.
        /// </summary>
        private float CalculatePlacementRatioFallback(float2 placementPos)
        {
            float totalArea = math.PI * GatherRadius * GatherRadius;
            float occupiedArea = 0f;

            var hutQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());

            var hutTransforms = hutQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var hutFactions = hutQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Subtract overlap with ALL existing GathererHut circles (same-faction
            // and enemy). Since this is a new farm, all existing farms have priority
            // for same-faction, and enemy huts always exclude.
            for (int i = 0; i < hutTransforms.Length; i++)
            {
                var hPos = new float2(hutTransforms[i].Position.x, hutTransforms[i].Position.z);
                float dist = math.distance(placementPos, hPos);

                if (dist < GatherRadius * 2f)
                {
                    occupiedArea += CircleCircleIntersection(GatherRadius, GatherRadius, dist);
                }
            }

            hutTransforms.Dispose();
            hutFactions.Dispose();

            float freeArea = math.max(0f, totalArea - occupiedArea);
            return freeArea / totalArea;
        }

        private void ShowAllExistingHutCircles()
        {
            var query = _em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>());

            var entities = query.ToEntityArray(Allocator.Temp);
            var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != GameSettings.LocalPlayerFaction) continue;

                var pos = transforms[i].Position;
                var circle = GetOrCreateCircle();
                var dimColor = circleColor;
                dimColor.a *= 0.5f;
                SetCircle(circle, new Vector3(pos.x, 0, pos.z), dimColor);
                _activeCircles.Add(circle);
            }

            entities.Dispose();
            transforms.Dispose();
            factions.Dispose();
        }

        /// <summary>
        /// Set by BuilderCommandPanel when starting GathererHut placement.
        /// </summary>
        public static bool IsPlacingGathererHutType;

        private LineRenderer GetOrCreateCircle()
        {
            if (_pool.Count > 0)
            {
                var lr = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
                lr.gameObject.SetActive(true);
                return lr;
            }
            return CreateCircleRenderer();
        }

        private LineRenderer CreateCircleRenderer()
        {
            var go = new GameObject("GathererHutCircle");
            go.transform.SetParent(transform);
            var lr = go.AddComponent<LineRenderer>();

            lr.material = new Material(_circleMat);
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = CircleSegments;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            return lr;
        }

        private void SetCircle(LineRenderer lr, Vector3 center, Color color)
        {
            if (lr.material.HasProperty("_BaseColor"))
                lr.material.SetColor("_BaseColor", color);
            if (lr.material.HasProperty("_Color"))
                lr.material.SetColor("_Color", color);

            lr.startColor = color;
            lr.endColor = color;

            var positions = new Vector3[CircleSegments];
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = (float)i / CircleSegments * Mathf.PI * 2f;
                float x = center.x + Mathf.Cos(angle) * GatherRadius;
                float z = center.z + Mathf.Sin(angle) * GatherRadius;
                float y = TerrainUtility.GetHeight(x, z) + 0.15f;
                positions[i] = new Vector3(x, y, z);
            }
            lr.SetPositions(positions);
        }

        private static float CircleCircleIntersection(float r1, float r2, float d)
        {
            if (d >= r1 + r2) return 0f;

            if (d + math.min(r1, r2) <= math.max(r1, r2))
                return math.PI * math.min(r1, r2) * math.min(r1, r2);

            float r1sq = r1 * r1;
            float r2sq = r2 * r2;
            float dsq = d * d;

            float a1 = r1sq * math.acos((dsq + r1sq - r2sq) / (2f * d * r1));
            float a2 = r2sq * math.acos((dsq + r2sq - r1sq) / (2f * d * r2));

            float trianglePart = 0.5f * math.sqrt(
                (-d + r1 + r2) * (d + r1 - r2) * (d - r1 + r2) * (d + r1 + r2));

            return a1 + a2 - trianglePart;
        }

        void OnDestroy()
        {
            foreach (var lr in _activeCircles)
                if (lr != null) Destroy(lr.gameObject);
            foreach (var lr in _pool)
                if (lr != null) Destroy(lr.gameObject);
            if (_placementCircle != null) Destroy(_placementCircle.gameObject);
            if (_circleMat != null) Destroy(_circleMat);
        }
    }
}
