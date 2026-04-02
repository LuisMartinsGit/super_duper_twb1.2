// File: Assets/Scripts/UI/HUD/MovementLineDisplay.cs
// Shows a line from selected moving units to their destinations, with a destination marker

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Input;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    [DefaultExecutionOrder(910)]
    public class MovementLineDisplay : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private Color lineColor = new Color(0.3f, 1f, 0.3f, 0.35f);
        [SerializeField] private Color markerColor = new Color(0.3f, 1f, 0.3f, 0.6f);
        [SerializeField] private float lineWidth = 0.06f;
        [SerializeField] private float markerSize = 0.3f;
        [SerializeField] private int lineSegments = 10;
        [SerializeField] private float lineYOffset = 0.3f;

        private EntityWorld _world;
        private EntityManager _em;

        private readonly List<LineRenderer> _activeLines = new();
        private readonly List<LineRenderer> _linePool = new();
        private readonly List<GameObject> _activeMarkers = new();
        private readonly List<GameObject> _markerPool = new();

        private Material _lineMat;

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            _lineMat = new Material(shader);
            if (_lineMat.HasProperty("_Surface")) _lineMat.SetFloat("_Surface", 1);
            _lineMat.renderQueue = 3000;
        }

        void LateUpdate()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
            }
            _em = _world.EntityManager;

            // Return all to pool
            foreach (var lr in _activeLines)
            {
                lr.gameObject.SetActive(false);
                _linePool.Add(lr);
            }
            _activeLines.Clear();

            foreach (var m in _activeMarkers)
            {
                m.SetActive(false);
                _markerPool.Add(m);
            }
            _activeMarkers.Clear();

            var selection = SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0) return;

            foreach (var entity in selection)
            {
                if (!_em.Exists(entity)) continue;
                if (!_em.HasComponent<UnitTag>(entity)) continue;
                if (_em.HasComponent<BattalionMemberData>(entity)) continue; // Members follow formation, not destinations
                if (!_em.HasComponent<DesiredDestination>(entity)) continue;
                if (!_em.HasComponent<LocalTransform>(entity)) continue;

                var dest = _em.GetComponentData<DesiredDestination>(entity);
                if (dest.Has == 0) continue;

                // For battalion leaders, use average position of living members
                float3 pos = _em.GetComponentData<LocalTransform>(entity).Position;
                if (_em.HasComponent<BattalionLeader>(entity) && _em.HasBuffer<BattalionMember>(entity))
                {
                    var members = _em.GetBuffer<BattalionMember>(entity);
                    if (members.Length > 0)
                    {
                        float3 sum = float3.zero;
                        int count = 0;
                        for (int m = 0; m < members.Length; m++)
                        {
                            var member = members[m].Value;
                            if (_em.Exists(member) && _em.HasComponent<LocalTransform>(member))
                            {
                                sum += _em.GetComponentData<LocalTransform>(member).Position;
                                count++;
                            }
                        }
                        if (count > 0) pos = sum / count;
                    }
                }

                Vector3 unitWorld = new Vector3(pos.x, 0f, pos.z);
                Vector3 destWorld = new Vector3(dest.Position.x, 0f, dest.Position.z);

                // Draw terrain-hugging multi-segment line
                var lr = GetOrCreateLine();
                lr.gameObject.SetActive(true);
                lr.positionCount = lineSegments + 1;
                for (int s = 0; s <= lineSegments; s++)
                {
                    float t = (float)s / lineSegments;
                    float x = Mathf.Lerp(unitWorld.x, destWorld.x, t);
                    float z = Mathf.Lerp(unitWorld.z, destWorld.z, t);
                    float y = TerrainUtility.GetHeight(x, z) + lineYOffset;
                    lr.SetPosition(s, new Vector3(x, y, z));
                }
                _activeLines.Add(lr);

                // Draw destination marker (decal projected on terrain)
                float destY = TerrainUtility.GetHeight(dest.Position.x, dest.Position.z);
                var marker = GetOrCreateMarker();
                marker.SetActive(true);
                marker.transform.position = new Vector3(dest.Position.x, destY + 5f, dest.Position.z);
                _activeMarkers.Add(marker);
            }
        }

        private LineRenderer GetOrCreateLine()
        {
            if (_linePool.Count > 0)
            {
                var lr = _linePool[_linePool.Count - 1];
                _linePool.RemoveAt(_linePool.Count - 1);
                return lr;
            }

            var go = new GameObject("MoveLine");
            go.transform.SetParent(transform);
            var line = go.AddComponent<LineRenderer>();

            var mat = new Material(_lineMat);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", lineColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", lineColor);
            line.material = mat;
            line.startColor = lineColor;
            line.endColor = lineColor;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            return line;
        }

        private GameObject GetOrCreateMarker()
        {
            if (_markerPool.Count > 0)
            {
                var m = _markerPool[_markerPool.Count - 1];
                _markerPool.RemoveAt(_markerPool.Count - 1);
                return m;
            }

            // Use DecalProjector for terrain-projected marker
            var baseMat = DecalHelper.GetDotMarkerMaterial();
            if (baseMat != null)
            {
                var marker = new GameObject("MoveMarkerDecal");
                marker.transform.SetParent(transform);
                marker.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                var decal = marker.AddComponent<DecalProjector>();
                var mat = new Material(baseMat);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", markerColor);
                else if (mat.HasProperty("Base_Color")) mat.SetColor("Base_Color", markerColor);
                decal.material = mat;
                decal.size = new Vector3(markerSize * 2f, 10f, markerSize * 2f);
                decal.drawDistance = 500f;

                return marker;
            }

            // Fallback: sphere primitive
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fallback.name = "MoveMarker";
            fallback.transform.SetParent(transform);
            fallback.transform.localScale = Vector3.one * markerSize;

            var renderer = fallback.GetComponent<Renderer>();
            var fallbackMat = new Material(_lineMat);
            if (fallbackMat.HasProperty("_BaseColor")) fallbackMat.SetColor("_BaseColor", markerColor);
            if (fallbackMat.HasProperty("_Color")) fallbackMat.SetColor("_Color", markerColor);
            renderer.material = fallbackMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var col = fallback.GetComponent<Collider>();
            if (col != null) Destroy(col);

            return fallback;
        }

        void OnDestroy()
        {
            foreach (var lr in _activeLines)
                if (lr != null) Destroy(lr.gameObject);
            foreach (var lr in _linePool)
                if (lr != null) Destroy(lr.gameObject);
            foreach (var m in _activeMarkers)
                if (m != null) Destroy(m);
            foreach (var m in _markerPool)
                if (m != null) Destroy(m);
            if (_lineMat != null) Destroy(_lineMat);
        }
    }
}
