// Shows a line from selected moving units to their destinations, with a destination marker.
// A formation group draws ONE line for the whole army (see DrawSelection).

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Input;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Core.Commands.Types;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.World
{
    [DefaultExecutionOrder(910)]
    public class MovementLineDisplay : MonoBehaviour
    {
        // Command-specific colors
        [SerializeField] private Color attackLineColor = new Color(1f, 0.2f, 0.2f, 0.35f);
        [SerializeField] private Color attackMarkerColor = new Color(1f, 0.2f, 0.2f, 0.6f);
        [SerializeField] private Color supportLineColor = new Color(0.3f, 1f, 0.3f, 0.35f);
        [SerializeField] private Color supportMarkerColor = new Color(0.3f, 1f, 0.3f, 0.6f);
        [SerializeField] private Color moveLineColor = new Color(1f, 0.82f, 0.2f, 0.35f);
        [SerializeField] private Color moveMarkerColor = new Color(1f, 0.82f, 0.2f, 0.6f);
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

        // Slot reuse + change detection (2026-09-25). Every line and marker
        // used to be returned to the pool and re-claimed every frame, and
        // every segment re-sampled the terrain 11 times and rewrote all its
        // points and its material colour — for a selection that had not
        // moved. Slots are now claimed in order and a segment is rebuilt only
        // when an endpoint moved more than RebuildDistance or its colour
        // changed.
        private const float RebuildDistanceSq = 0.15f * 0.15f;
        private int _lineCursor;
        private int _markerCursor;
        private struct LineState { public Vector3 From, To; public Color Color; }
        private struct MarkerState { public Vector3 At; public Color Color; }
        private readonly Dictionary<LineRenderer, LineState> _lineState = new();
        private readonly Dictionary<GameObject, MarkerState> _markerState = new();

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

            _lineCursor = 0;
            _markerCursor = 0;

            var selection = SelectionSystem.CurrentSelection;
            if (selection != null && selection.Count > 0)
                DrawSelection(selection);

            // Release the slots this frame did not claim.
            for (int i = _activeLines.Count - 1; i >= _lineCursor; i--)
            {
                var lr = _activeLines[i];
                _activeLines.RemoveAt(i);
                if (lr == null) continue;
                lr.gameObject.SetActive(false);
                _lineState.Remove(lr);
                _linePool.Add(lr);
            }
            for (int i = _activeMarkers.Count - 1; i >= _markerCursor; i--)
            {
                var m = _activeMarkers[i];
                _activeMarkers.RemoveAt(i);
                if (m == null) continue;
                m.SetActive(false);
                _markerState.Remove(m);
                _markerPool.Add(m);
            }
        }

        // ONE LINE PER FORMATION (2026-10-15). A group order used to draw a
        // line per unit — twenty parallel yellow strands for one army. A unit
        // marching in a formation group (FormationMemberState -> a live
        // FormationGroup) now contributes nothing of its own: the group draws
        // one line from its virtual leader (the centre of the lattice) to the
        // group destination, plus the queued chain. Grouping is read off the
        // SIM's membership, not the selection, so it stays right after a
        // reselect, a partial selection or a member dropping out (an outlier,
        // a worker, a unit out of rank fighting) — those draw their own line.
        // Groups already drawn this frame; cleared, never reallocated.
        private readonly List<Entity> _groupsDrawn = new();

        private void DrawSelection(List<Entity> selection)
        {
            _groupsDrawn.Clear();
            foreach (var entity in selection)
            {
                if (!_em.Exists(entity)) continue;
                if (!_em.HasComponent<UnitTag>(entity)) continue;
                if (!_em.HasComponent<DesiredDestination>(entity)) continue;
                if (!_em.HasComponent<LocalTransform>(entity)) continue;

                bool attacking = _em.HasComponent<AttackCommand>(entity)
                              && _em.IsComponentEnabled<AttackCommand>(entity);
                if (!attacking && TryGetFormationGroup(entity, out var group))
                {
                    if (!_groupsDrawn.Contains(group))
                    {
                        _groupsDrawn.Add(group);
                        DrawGroup(group, entity);
                    }
                    continue;
                }

                var dest = _em.GetComponentData<DesiredDestination>(entity);
                bool hasActiveDest = dest.Has != 0;
                bool hasQueuedCommands = _em.HasBuffer<QueuedCommand>(entity)
                                       && _em.GetBuffer<QueuedCommand>(entity).Length > 0;
                // Skip entities that have neither an active destination nor any
                // queued waypoints — nothing to draw.
                if (!hasActiveDest && !hasQueuedCommands) continue;

                float3 pos = _em.GetComponentData<LocalTransform>(entity).Position;
                PickColors(entity, out var lColor, out var mColor);

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

                if (hasQueuedCommands) DrawQueuedChain(entity, chainEnd);
            }
        }

        /// <summary>The live formation group a unit marches in, if any.</summary>
        private bool TryGetFormationGroup(Entity unit, out Entity group)
        {
            group = Entity.Null;
            if (!TransientState.Active<FormationMemberState>(_em, unit)) return false;
            group = _em.GetComponentData<FormationMemberState>(unit).Group;
            return group != Entity.Null && _em.Exists(group)
                && _em.HasComponent<FormationGroup>(group);
        }

        /// <summary>
        /// The group's one line: virtual leader → destination while it
        /// marches, then just the destination marker while the ranks settle
        /// (the leader is already there), then the queued chain of the
        /// representative member — a grouped Shift-click queues the same
        /// point on every member, so any one of them carries the route.
        /// </summary>
        private void DrawGroup(Entity group, Entity representative)
        {
            var g = _em.GetComponentData<FormationGroup>(group);
            PickColors(representative, out var lColor, out var mColor);

            Vector3 destWorld = new Vector3(g.Destination.x, 0f, g.Destination.z);
            if (g.State == FormationGroup.StateMoving)
                DrawSegment(new Vector3(g.LeaderPos.x, 0f, g.LeaderPos.z), destWorld, lColor);
            PlaceMarker(g.Destination, mColor);

            if (_em.HasBuffer<QueuedCommand>(representative)
                && _em.GetBuffer<QueuedCommand>(representative).Length > 0)
                DrawQueuedChain(representative, destWorld);
        }

        // Line + marker colour for a unit's live order.
        private void PickColors(Entity entity, out Color lColor, out Color mColor)
        {
            if (_em.HasComponent<AttackCommand>(entity)
                && _em.IsComponentEnabled<AttackCommand>(entity))
            {
                lColor = attackLineColor;
                mColor = attackMarkerColor;
            }
            else if (TransientState.Active<BuildOrder>(_em, entity)
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
        }

        // ── Queued chain (Shift+right-click, chained AI orders) ──
        // Each queued step is drawn with the SAME lines that mark the
        // live order (2026-09-29): red for an attack or attack-move,
        // green for build / repair / heal, the move colour for moves
        // and patrols — chained from the previous step's end. A
        // targeted step follows its target's current position.
        private void DrawQueuedChain(Entity entity, Vector3 chainEnd)
        {
            var buffer = _em.GetBuffer<QueuedCommand>(entity);
            for (int q = 0; q < buffer.Length; q++)
            {
                var cmd = buffer[q];
                float3 at = cmd.TargetPosition;
                if (cmd.TargetEntity != Entity.Null && _em.Exists(cmd.TargetEntity)
                    && _em.HasComponent<LocalTransform>(cmd.TargetEntity))
                    at = _em.GetComponentData<LocalTransform>(cmd.TargetEntity).Position;

                Color ql, qm;
                switch (cmd.Type)
                {
                    case QueuedCommandType.Attack:
                    case QueuedCommandType.AttackMove:
                        ql = attackLineColor; qm = attackMarkerColor; break;
                    case QueuedCommandType.Build:
                    case QueuedCommandType.Repair:
                    case QueuedCommandType.Heal:
                        ql = supportLineColor; qm = supportMarkerColor; break;
                    default:
                        ql = moveLineColor; qm = moveMarkerColor; break;
                }

                Vector3 wp = new Vector3(at.x, 0f, at.z);
                DrawSegment(chainEnd, wp, ql);
                PlaceMarker(at, qm);
                chainEnd = wp;
            }
        }

        // Draws a terrain-hugging segmented line between two ground-projected
        // points, pulled from the line pool. Y is sampled per segment so the
        // line follows hills.
        private void DrawSegment(Vector3 fromXZ, Vector3 toXZ, Color color)
        {
            LineRenderer lr;
            if (_lineCursor < _activeLines.Count && _activeLines[_lineCursor] != null)
            {
                lr = _activeLines[_lineCursor];
                if (_lineState.TryGetValue(lr, out var st)
                    && st.Color == color
                    && (st.From - fromXZ).sqrMagnitude < RebuildDistanceSq
                    && (st.To - toXZ).sqrMagnitude < RebuildDistanceSq)
                {
                    _lineCursor++;
                    return;   // unchanged: keep what is drawn
                }
            }
            else
            {
                lr = GetOrCreateLine();
                if (_lineCursor < _activeLines.Count) _activeLines[_lineCursor] = lr;
                else _activeLines.Add(lr);
            }
            _lineCursor++;
            _lineState[lr] = new LineState { From = fromXZ, To = toXZ, Color = color };

            ApplyLineColor(lr, color);
            if (!lr.gameObject.activeSelf) lr.gameObject.SetActive(true);
            lr.positionCount = lineSegments + 1;
            for (int s = 0; s <= lineSegments; s++)
            {
                float t = (float)s / lineSegments;
                float x = Mathf.Lerp(fromXZ.x, toXZ.x, t);
                float z = Mathf.Lerp(fromXZ.z, toXZ.z, t);
                float y = TerrainUtility.GetHeight(x, z) + lineYOffset;
                lr.SetPosition(s, new Vector3(x, y, z));
            }
        }

        // Places a destination decal marker at the given world XZ point,
        // pulled from the marker pool.
        private void PlaceMarker(float3 worldPos, Color color)
        {
            Vector3 at = new Vector3(worldPos.x, 0f, worldPos.z);
            GameObject marker;
            if (_markerCursor < _activeMarkers.Count && _activeMarkers[_markerCursor] != null)
            {
                marker = _activeMarkers[_markerCursor];
                if (_markerState.TryGetValue(marker, out var st) && st.Color == color
                    && (st.At - at).sqrMagnitude < RebuildDistanceSq)
                {
                    _markerCursor++;
                    return;
                }
                if (!_markerState.TryGetValue(marker, out var prev) || prev.Color != color)
                    ApplyMarkerColor(marker, color);
            }
            else
            {
                marker = GetOrCreateMarker(color);
                if (_markerCursor < _activeMarkers.Count) _activeMarkers[_markerCursor] = marker;
                else _activeMarkers.Add(marker);
            }
            _markerCursor++;
            _markerState[marker] = new MarkerState { At = at, Color = color };

            float terrainY = TerrainUtility.GetHeight(worldPos.x, worldPos.z);
            if (!marker.activeSelf) marker.SetActive(true);
            marker.transform.position = new Vector3(worldPos.x, terrainY + 5f, worldPos.z);
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
