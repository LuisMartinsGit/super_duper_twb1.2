// File: Assets/Scripts/UI/HUD/RallyPointDisplay.cs
// Shows rally point marker when a building with a rally point is selected

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
    public class RallyPointDisplay : MonoBehaviour
    {
        [Header("Rally Marker")]
        [SerializeField] private Color markerColor = new Color(0.2f, 0.6f, 1f, 0.8f);
        [SerializeField] private Color lineColor = new Color(0.2f, 0.6f, 1f, 0.4f);

        [Header("Resource Rally")]
        [Tooltip("Marker/line colour when the rally targets a resource node — " +
                 "trainees gather it on spawn instead of just walking there.")]
        [SerializeField] private Color resourceMarkerColor = new Color(0.25f, 0.9f, 0.3f, 0.85f);
        [SerializeField] private Color resourceLineColor = new Color(0.25f, 0.9f, 0.3f, 0.45f);

        [Tooltip("Height the marker floats above a resource node, so it clears " +
                 "the node mesh it is centred on. A node fills one 2 m build cell.")]
        [SerializeField] private float ResourceRallyLift = 2.2f;

        [SerializeField] private float markerSize = 0.6f;
        [SerializeField] private float lineWidth = 0.08f;

        private EntityWorld _world;
        private EntityManager _em;

        // Marker objects
        private GameObject _markerObj;
        private LineRenderer _lineRenderer;
        private Material _markerMat;

        // Live instances, re-tinted per frame from the rally kind.
        private Material _markerInstance;
        private Material _lineInstance;

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            _markerMat = new Material(shader);
            if (_markerMat.HasProperty("_Surface")) _markerMat.SetFloat("_Surface", 1);
            _markerMat.renderQueue = 3000;

            CreateMarker();
            CreateLine();
        }

        void LateUpdate()
        {
            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
            }
            _em = _world.EntityManager;

            _markerObj.SetActive(false);
            _lineRenderer.gameObject.SetActive(false);

            var selection = SelectionSystem.CurrentSelection;
            if (selection == null || selection.Count == 0) return;

            // Find first selected building with an active rally point
            foreach (var entity in selection)
            {
                if (!_em.Exists(entity)) continue;
                if (!_em.HasComponent<BuildingTag>(entity)) continue;
                if (!_em.HasComponent<RallyPoint>(entity)) continue;

                var rally = _em.GetComponentData<RallyPoint>(entity);
                if (rally.Has == 0) continue;

                // GREEN = a gather rally: trainees mine this node on spawn
                // rather than just walking to a spot. Distinct enough from the
                // default blue to read at a glance which of the two a building
                // is set to. rally.Position is already the node's cell centre
                // (CommandRouter snaps it), so the marker sits dead centre on
                // the cell and the guide line points at it.
                bool gatherRally = ResourceNodeQuery.IsGatherable(_em, rally.TargetEntity);
                Tint(gatherRally ? resourceMarkerColor : markerColor,
                     gatherRally ? resourceLineColor : lineColor);

                var buildingPos = _em.GetComponentData<LocalTransform>(entity).Position;
                // A gather rally sits at the node's cell centre — which is
                // INSIDE the node's mesh. Float the marker above it, or the
                // colour change the player is meant to read is buried in the
                // ore. Ground rallies keep hugging the terrain.
                float rallyLift = gatherRally ? ResourceRallyLift : 0.2f;
                float rallyY = TerrainUtility.GetHeight(rally.Position.x, rally.Position.z) + rallyLift;
                float buildingY = TerrainUtility.GetHeight(buildingPos.x, buildingPos.z) + 0.5f;

                Vector3 rallyWorld = new Vector3(rally.Position.x, rallyY, rally.Position.z);
                Vector3 buildWorld = new Vector3(buildingPos.x, buildingY, buildingPos.z);

                // Show marker at rally point
                _markerObj.SetActive(true);
                _markerObj.transform.position = rallyWorld;
                _markerObj.transform.Rotate(Vector3.up, 90f * Time.deltaTime);

                // Show terrain-hugging line from building to rally point
                _lineRenderer.gameObject.SetActive(true);
                const int segments = 10;
                _lineRenderer.positionCount = segments + 1;
                for (int s = 0; s <= segments; s++)
                {
                    float t = (float)s / segments;
                    float x = Mathf.Lerp(buildWorld.x, rallyWorld.x, t);
                    float z = Mathf.Lerp(buildWorld.z, rallyWorld.z, t);
                    float y = TerrainUtility.GetHeight(x, z) + 0.3f;
                    _lineRenderer.SetPosition(s, new Vector3(x, y, z));
                }

                break; // Only show one rally point
            }
        }

        /// <summary>Re-tint the existing material instances. Cheap: setting a
        /// colour that is already set is a no-op upload, and this avoids
        /// allocating a Material per frame.</summary>
        private void Tint(Color marker, Color line)
        {
            if (_markerInstance != null)
            {
                if (_markerInstance.HasProperty("_BaseColor")) _markerInstance.SetColor("_BaseColor", marker);
                if (_markerInstance.HasProperty("_Color")) _markerInstance.SetColor("_Color", marker);
            }
            if (_lineInstance != null)
            {
                if (_lineInstance.HasProperty("_BaseColor")) _lineInstance.SetColor("_BaseColor", line);
                if (_lineInstance.HasProperty("_Color")) _lineInstance.SetColor("_Color", line);
            }
            if (_lineRenderer != null)
            {
                _lineRenderer.startColor = line;
                _lineRenderer.endColor = line;
            }
        }

        private void CreateMarker()
        {
            _markerObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _markerObj.name = "RallyPointMarker";
            _markerObj.transform.SetParent(transform);
            _markerObj.transform.localScale = new Vector3(markerSize, markerSize * 2f, markerSize);

            var renderer = _markerObj.GetComponent<Renderer>();
            var mat = new Material(_markerMat);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", markerColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", markerColor);
            renderer.material = mat;
            _markerInstance = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // Remove collider so it doesn't interfere with raycasts
            var col = _markerObj.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _markerObj.SetActive(false);
        }

        private void CreateLine()
        {
            var lineGO = new GameObject("RallyPointLine");
            lineGO.transform.SetParent(transform);
            _lineRenderer = lineGO.AddComponent<LineRenderer>();

            var mat = new Material(_markerMat);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", lineColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", lineColor);
            _lineRenderer.material = mat;
            _lineInstance = mat;
            _lineRenderer.startColor = lineColor;
            _lineRenderer.endColor = lineColor;
            _lineRenderer.startWidth = lineWidth;
            _lineRenderer.endWidth = lineWidth;
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.positionCount = 2;
            _lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;

            lineGO.SetActive(false);
        }

        void OnDestroy()
        {
            if (_markerObj != null) Destroy(_markerObj);
            if (_lineRenderer != null) Destroy(_lineRenderer.gameObject);
            if (_markerMat != null) Destroy(_markerMat);
        }
    }
}
