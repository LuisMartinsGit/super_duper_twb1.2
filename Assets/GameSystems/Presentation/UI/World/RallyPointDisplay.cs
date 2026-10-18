// Shows the rally point marker and line of EVERY selected building that has one

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using TheWaningBorder.Input;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.World
{
    [DefaultExecutionOrder(910)]
    public class RallyPointDisplay : MonoBehaviour
    {
        [SerializeField] private Color markerColor = new Color(0.2f, 0.6f, 1f, 0.8f);
        [SerializeField] private Color lineColor = new Color(0.2f, 0.6f, 1f, 0.4f);

        [SerializeField] private Color resourceMarkerColor = new Color(0.25f, 0.9f, 0.3f, 0.85f);
        [SerializeField] private Color resourceLineColor = new Color(0.25f, 0.9f, 0.3f, 0.45f);

        [SerializeField] private float ResourceRallyLift = 2.2f;

        [SerializeField] private float markerSize = 0.6f;
        [SerializeField] private float lineWidth = 0.08f;

        private EntityWorld _world;
        private EntityManager _em;
        private Material _markerMat;

        // One marker + line per selected building with a rally (2026-10-16:
        // it used to draw only the FIRST, so selecting several barracks showed
        // one rally while the right-click set all of them). Pooled, grown on
        // demand, never shrunk; unused entries are hidden each frame.
        private sealed class Rally
        {
            public GameObject Marker;
            public LineRenderer Line;
            public Material MarkerMat, LineMat;
        }
        private readonly List<Rally> _pool = new List<Rally>();

        void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            _markerMat = new Material(shader);
            if (_markerMat.HasProperty("_Surface")) _markerMat.SetFloat("_Surface", 1);
            _markerMat.renderQueue = 3000;
        }

        void LateUpdate()
        {
            int used = 0;
            try
            {
                if (_world == null || !_world.IsCreated)
                {
                    _world = EntityWorld.DefaultGameObjectInjectionWorld;
                    if (_world == null || !_world.IsCreated) return;
                }
                _em = _world.EntityManager;

                var selection = SelectionSystem.CurrentSelection;
                if (selection == null || selection.Count == 0) return;

                foreach (var entity in selection)
                {
                    if (!_em.Exists(entity)) continue;
                    if (!_em.HasComponent<BuildingTag>(entity)) continue;
                    if (!_em.HasComponent<RallyPoint>(entity)) continue;

                    var rally = _em.GetComponentData<RallyPoint>(entity);
                    if (rally.Has == 0) continue;

                    Draw(Get(used++), entity, rally);
                }
            }
            finally
            {
                for (int i = used; i < _pool.Count; i++)
                {
                    if (_pool[i].Marker.activeSelf) _pool[i].Marker.SetActive(false);
                    if (_pool[i].Line.gameObject.activeSelf) _pool[i].Line.gameObject.SetActive(false);
                }
            }
        }

        private void Draw(Rally r, Entity building, RallyPoint rally)
        {
            // GREEN = a gather rally: trainees mine this node on spawn
            // rather than just walking to a spot. Distinct enough from the
            // default blue to read at a glance which of the two a building
            // is set to. rally.Position is already the node's cell centre
            // (CommandRouter snaps it), so the marker sits dead centre on
            // the cell and the guide line points at it.
            bool gatherRally = ResourceNodeQuery.IsGatherable(_em, rally.TargetEntity);
            Tint(r, gatherRally ? resourceMarkerColor : markerColor,
                    gatherRally ? resourceLineColor : lineColor);

            var buildingPos = _em.GetComponentData<LocalTransform>(building).Position;
            // A gather rally sits at the node's cell centre — which is
            // INSIDE the node's mesh. Float the marker above it, or the
            // colour change the player is meant to read is buried in the
            // ore. Ground rallies keep hugging the terrain.
            float rallyLift = gatherRally ? ResourceRallyLift : 0.2f;
            float rallyY = TerrainUtility.GetHeight(rally.Position.x, rally.Position.z) + rallyLift;
            float buildingY = TerrainUtility.GetHeight(buildingPos.x, buildingPos.z) + 0.5f;

            Vector3 rallyWorld = new Vector3(rally.Position.x, rallyY, rally.Position.z);
            Vector3 buildWorld = new Vector3(buildingPos.x, buildingY, buildingPos.z);

            r.Marker.SetActive(true);
            r.Marker.transform.position = rallyWorld;
            r.Marker.transform.Rotate(Vector3.up, 90f * Time.deltaTime);

            // Terrain-hugging line from the building to its rally point.
            r.Line.gameObject.SetActive(true);
            const int segments = 10;
            r.Line.positionCount = segments + 1;
            for (int s = 0; s <= segments; s++)
            {
                float t = (float)s / segments;
                float x = Mathf.Lerp(buildWorld.x, rallyWorld.x, t);
                float z = Mathf.Lerp(buildWorld.z, rallyWorld.z, t);
                float y = TerrainUtility.GetHeight(x, z) + 0.3f;
                r.Line.SetPosition(s, new Vector3(x, y, z));
            }
        }

        private Rally Get(int index)
        {
            while (_pool.Count <= index) _pool.Add(Create(_pool.Count));
            return _pool[index];
        }

        /// <summary>Re-tint the existing material instances. Cheap: setting a
        /// colour that is already set is a no-op upload, and this avoids
        /// allocating a Material per frame.</summary>
        private static void Tint(Rally r, Color marker, Color line)
        {
            SetColor(r.MarkerMat, marker);
            SetColor(r.LineMat, line);
            r.Line.startColor = line;
            r.Line.endColor = line;
        }

        private static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        private Rally Create(int index)
        {
            var r = new Rally();

            r.Marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            r.Marker.name = "RallyPointMarker_" + index;
            r.Marker.transform.SetParent(transform);
            r.Marker.transform.localScale = new Vector3(markerSize, markerSize * 2f, markerSize);
            var renderer = r.Marker.GetComponent<Renderer>();
            r.MarkerMat = new Material(_markerMat);
            SetColor(r.MarkerMat, markerColor);
            renderer.material = r.MarkerMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            // Remove collider so it doesn't interfere with raycasts
            var col = r.Marker.GetComponent<Collider>();
            if (col != null) Destroy(col);
            r.Marker.SetActive(false);

            var lineGO = new GameObject("RallyPointLine_" + index);
            lineGO.transform.SetParent(transform);
            r.Line = lineGO.AddComponent<LineRenderer>();
            r.LineMat = new Material(_markerMat);
            SetColor(r.LineMat, lineColor);
            r.Line.material = r.LineMat;
            r.Line.startColor = lineColor;
            r.Line.endColor = lineColor;
            r.Line.startWidth = lineWidth;
            r.Line.endWidth = lineWidth;
            r.Line.useWorldSpace = true;
            r.Line.positionCount = 2;
            r.Line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.Line.receiveShadows = false;
            lineGO.SetActive(false);

            return r;
        }

        void OnDestroy()
        {
            foreach (var r in _pool)
            {
                if (r.Marker != null) Destroy(r.Marker);
                if (r.Line != null) Destroy(r.Line.gameObject);
                if (r.MarkerMat != null) Destroy(r.MarkerMat);
                if (r.LineMat != null) Destroy(r.LineMat);
            }
            _pool.Clear();
            if (_markerMat != null) Destroy(_markerMat);
        }
    }
}
