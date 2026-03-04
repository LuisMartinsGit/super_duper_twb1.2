// File: Assets/Scripts/UI/HUD/MovementLineDisplay.cs
// Shows a line from selected moving units to their destinations, with a destination marker

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
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
                if (!_em.HasComponent<DesiredDestination>(entity)) continue;
                if (!_em.HasComponent<LocalTransform>(entity)) continue;

                var dest = _em.GetComponentData<DesiredDestination>(entity);
                if (dest.Has == 0) continue;

                var pos = _em.GetComponentData<LocalTransform>(entity).Position;
                float unitY = TerrainUtility.GetHeight(pos.x, pos.z) + 0.15f;
                float destY = TerrainUtility.GetHeight(dest.Position.x, dest.Position.z) + 0.15f;

                Vector3 unitWorld = new Vector3(pos.x, unitY, pos.z);
                Vector3 destWorld = new Vector3(dest.Position.x, destY, dest.Position.z);

                // Draw line
                var lr = GetOrCreateLine();
                lr.gameObject.SetActive(true);
                lr.SetPosition(0, unitWorld);
                lr.SetPosition(1, destWorld);
                _activeLines.Add(lr);

                // Draw destination marker
                var marker = GetOrCreateMarker();
                marker.SetActive(true);
                marker.transform.position = destWorld + Vector3.up * 0.1f;
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

            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "MoveMarker";
            marker.transform.SetParent(transform);
            marker.transform.localScale = Vector3.one * markerSize;

            var renderer = marker.GetComponent<Renderer>();
            var mat = new Material(_lineMat);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", markerColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", markerColor);
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var col = marker.GetComponent<Collider>();
            if (col != null) Destroy(col);

            return marker;
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
