// File: Assets/Scripts/UI/HUD/MovementLineDisplay.cs
// Shows a line from selected moving units to their destinations, with a destination marker

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Input;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Core.Commands.Types;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    [DefaultExecutionOrder(910)]
    public class MovementLineDisplay : MonoBehaviour
    {
        [Header("Display")]
        // Command-specific colors
        [SerializeField] private Color attackLineColor = new Color(1f, 0.2f, 0.2f, 0.35f);
        [SerializeField] private Color attackMarkerColor = new Color(1f, 0.2f, 0.2f, 0.6f);
        [SerializeField] private Color supportLineColor = new Color(0.3f, 1f, 0.3f, 0.35f);
        [SerializeField] private Color supportMarkerColor = new Color(0.3f, 1f, 0.3f, 0.6f);
        [SerializeField] private Color moveLineColor = new Color(1f, 0.82f, 0.2f, 0.35f);
        [SerializeField] private Color moveMarkerColor = new Color(1f, 0.82f, 0.2f, 0.6f);
        // Saturated yellow for queued waypoints (Shift+rclick chain). More
        // opaque than moveLineColor so the chain reads against terrain.
        [SerializeField] private Color queueLineColor = new Color(1f, 0.95f, 0.2f, 0.85f);
        [SerializeField] private Color queueMarkerColor = new Color(1f, 0.95f, 0.2f, 0.95f);
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
                bool hasActiveDest = dest.Has != 0;
                bool hasQueuedCommands = _em.HasBuffer<QueuedCommand>(entity)
                                       && _em.GetBuffer<QueuedCommand>(entity).Length > 0;
                // Skip entities that have neither an active destination nor any
                // queued waypoints — nothing to draw.
                if (!hasActiveDest && !hasQueuedCommands) continue;

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

                // Determine command type for color
                Color lColor, mColor;
                if (_em.HasComponent<AttackCommand>(entity))
                {
                    lColor = attackLineColor;
                    mColor = attackMarkerColor;
                }
                else if (_em.HasComponent<GatherCommand>(entity)
                      || _em.HasComponent<BuildOrder>(entity)
                      || _em.HasComponent<HealCommand>(entity))
                {
                    lColor = supportLineColor;
                    mColor = supportMarkerColor;
                }
                else
                {
                    lColor = moveLineColor;
                    mColor = moveMarkerColor;
                }

                // For battalion leaders, also check member commands
                if (_em.HasComponent<BattalionLeader>(entity) && _em.HasComponent<Target>(entity))
                {
                    var leaderTgt = _em.GetComponentData<Target>(entity);
                    if (leaderTgt.Value != Entity.Null && _em.Exists(leaderTgt.Value))
                    {
                        lColor = attackLineColor;
                        mColor = attackMarkerColor;
                    }
                }

                Vector3 unitWorld = new Vector3(pos.x, 0f, pos.z);

                // Tracks the endpoint each subsequent segment starts from. As
                // we draw the active line, then each queued waypoint, this
                // walks W0 → W1 → W2 → … so we connect them as a chain.
                Vector3 chainEnd = unitWorld;

                if (hasActiveDest)
                {
                    Vector3 destWorld = new Vector3(dest.Position.x, 0f, dest.Position.z);
                    DrawSegment(unitWorld, destWorld, lColor);
                    PlaceMarker(dest.Position, mColor);
                    chainEnd = destWorld;
                }

                // ── Queued-waypoint chain (Shift+rclick) ──
                // Draws a yellow line from the previous waypoint endpoint to
                // each queued waypoint, plus a marker at every queued point.
                if (hasQueuedCommands)
                {
                    var buffer = _em.GetBuffer<QueuedCommand>(entity);
                    for (int q = 0; q < buffer.Length; q++)
                    {
                        var cmd = buffer[q];
                        Vector3 wp = new Vector3(cmd.TargetPosition.x, 0f, cmd.TargetPosition.z);
                        DrawSegment(chainEnd, wp, queueLineColor);
                        PlaceMarker(cmd.TargetPosition, queueMarkerColor);
                        chainEnd = wp;
                    }
                }
            }
        }

        // Draws a terrain-hugging segmented line between two ground-projected
        // points, pulled from the line pool. Y is sampled per segment so the
        // line follows hills.
        private void DrawSegment(Vector3 fromXZ, Vector3 toXZ, Color color)
        {
            var lr = GetOrCreateLine();
            ApplyLineColor(lr, color);
            lr.gameObject.SetActive(true);
            lr.positionCount = lineSegments + 1;
            for (int s = 0; s <= lineSegments; s++)
            {
                float t = (float)s / lineSegments;
                float x = Mathf.Lerp(fromXZ.x, toXZ.x, t);
                float z = Mathf.Lerp(fromXZ.z, toXZ.z, t);
                float y = TerrainUtility.GetHeight(x, z) + lineYOffset;
                lr.SetPosition(s, new Vector3(x, y, z));
            }
            _activeLines.Add(lr);
        }

        // Places a destination decal marker at the given world XZ point,
        // pulled from the marker pool.
        private void PlaceMarker(float3 worldPos, Color color)
        {
            float terrainY = TerrainUtility.GetHeight(worldPos.x, worldPos.z);
            var marker = GetOrCreateMarker(color);
            marker.SetActive(true);
            marker.transform.position = new Vector3(worldPos.x, terrainY + 5f, worldPos.z);
            _activeMarkers.Add(marker);
        }

        private void ApplyLineColor(LineRenderer lr, Color color)
        {
            lr.startColor = color;
            lr.endColor = color;
            var mat = lr.material;
            if (mat != null)
            {
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
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
            line.material = mat;
            line.startWidth = lineWidth;
            line.endWidth = lineWidth;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            return line;
        }

        private void ApplyMarkerColor(GameObject marker, Color color)
        {
            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null)
            {
                if (renderer.material.HasProperty("_BaseColor")) renderer.material.SetColor("_BaseColor", color);
                if (renderer.material.HasProperty("_Color")) renderer.material.SetColor("_Color", color);
            }
        }

        private GameObject GetOrCreateMarker(Color color)
        {
            if (_markerPool.Count > 0)
            {
                var m = _markerPool[_markerPool.Count - 1];
                _markerPool.RemoveAt(_markerPool.Count - 1);
                ApplyMarkerColor(m, color);
                return m;
            }

            // Sphere primitive (no decals)
            var fallback = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fallback.name = "MoveMarker";
            fallback.transform.SetParent(transform);
            fallback.transform.localScale = Vector3.one * markerSize;

            var renderer = fallback.GetComponent<Renderer>();
            var fallbackMat = new Material(_lineMat);
            if (fallbackMat.HasProperty("_BaseColor")) fallbackMat.SetColor("_BaseColor", color);
            if (fallbackMat.HasProperty("_Color")) fallbackMat.SetColor("_Color", color);
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
