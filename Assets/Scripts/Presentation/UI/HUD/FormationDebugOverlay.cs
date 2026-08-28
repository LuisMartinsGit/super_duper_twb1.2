// FormationDebugOverlay.cs
// F2: draws what the formation system is actually thinking.
//
// The virtual leader is a POINT MASS THAT DOES NOT EXIST — no entity, no
// renderer, nothing on screen. Every member steers to a spot laid out around
// it, so when a group misbehaves the thing steering it is the one thing you
// cannot see. The bug where half a retreating group walked toward the enemy
// (leader seeded on the centroid, so the spots of everyone in front sat behind
// them) was invisible for exactly that reason: on screen it was units walking
// the wrong way for no reason.
//
// What is drawn, per active FormationGroup:
//   * a GIMBAL at the leader — three orthogonal rings, so its position reads
//     from any camera angle, plus a nose line along Facing
//   * the leader's destination, and the leg still to travel
//   * every member's SPOT, and the line from the unit to it
//
// The line from a unit to its spot is the whole diagnostic: the direction it
// points IS the order that member is being given this tick. A line pointing
// back into the enemy is the bug, visible at a glance.
//
// Debug-only, off by default, and it draws nothing at all while hidden.

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.World.Terrain;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    [DefaultExecutionOrder(911)] // just after MovementLineDisplay (910)
    public class FormationDebugOverlay : MonoBehaviour
    {
        // F1 is the log console (DebugLogOverlay); F2 is this.
        private const KeyCode ToggleKey = KeyCode.F2;

        /// <summary>Off by default — this is a diagnostic, not a HUD.</summary>
        public static bool Visible;

        private static readonly Color LeaderColor      = new Color(1.0f, 0.85f, 0.10f, 0.95f);
        private static readonly Color LeaderNoseColor  = new Color(1.0f, 0.55f, 0.05f, 0.95f);
        private static readonly Color DestinationColor = new Color(0.35f, 0.85f, 1.0f, 0.85f);
        private static readonly Color SpotColor        = new Color(0.35f, 1.0f, 0.45f, 0.75f);
        /// <summary>A member running the +40% catch-up reads hot, so "who is
        /// holding this formation up" is answerable without opening a log.</summary>
        private static readonly Color CatchUpColor     = new Color(1.0f, 0.35f, 0.30f, 0.95f);

        private const float LineWidth   = 0.05f;
        private const float YOffset     = 0.35f;
        private const float LeaderRing  = 1.6f;
        private const float SpotRing    = 0.45f;
        private const int   RingPoints  = 28;

        private EntityWorld _world;
        private EntityManager _em;
        private Material _mat;

        private static readonly ComponentType[] GroupQueryTypes =
        {
            ComponentType.ReadOnly<FormationGroup>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _groupQuery;

        private readonly List<LineRenderer> _active = new();
        private readonly List<LineRenderer> _pool = new();

        private void Awake()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;

            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            _mat = new Material(shader);
            if (_mat.HasProperty("_Surface")) _mat.SetFloat("_Surface", 1);
            _mat.renderQueue = 3000;
        }

        private void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
        }

        private void LateUpdate()
        {
            // UnityEngine.Input, never bare Input: `Input` resolves to the
            // TheWaningBorder.Input NAMESPACE inside this assembly.
            if (UnityEngine.Input.GetKeyDown(ToggleKey)) Visible = !Visible;

            Recycle();
            if (!Visible) return;

            if (_world == null || !_world.IsCreated)
            {
                _world = EntityWorld.DefaultGameObjectInjectionWorld;
                if (_world == null || !_world.IsCreated) return;
            }
            _em = _world.EntityManager;

            var q = _groupQuery.Get(_em, GroupQueryTypes);
            using var groups = q.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int gi = 0; gi < groups.Length; gi++)
            {
                var ge = groups[gi];
                if (!_em.Exists(ge)) continue;
                var g = _em.GetComponentData<FormationGroup>(ge);

                Vector3 leader = Ground(g.LeaderPos.x, g.LeaderPos.z);
                Vector3 facing = new Vector3(g.Facing.x, 0f, g.Facing.z);
                if (facing.sqrMagnitude < 1e-6f) facing = Vector3.forward;
                facing.Normalize();

                DrawGimbal(leader, facing, LeaderRing, LeaderColor);

                // Nose: which way the layout is oriented. Without it the rings
                // tell you where the leader is but not which way it faces, and
                // the slot offsets are expressed in exactly that frame.
                DrawLine(new[] { leader, leader + facing * (LeaderRing * 1.8f) },
                         LeaderNoseColor);

                // The leg still to travel.
                Vector3 dest = Ground(g.Destination.x, g.Destination.z);
                DrawLine(new[] { leader, dest }, DestinationColor);
                DrawRing(dest, Vector3.up, LeaderRing * 0.7f, DestinationColor);

                if (!_em.HasBuffer<FormationMember>(ge)) continue;
                var members = _em.GetBuffer<FormationMember>(ge);

                // Slots are leader-local: x along right, y along facing.
                Vector3 right = Vector3.Cross(Vector3.up, facing);

                for (int i = 0; i < members.Length; i++)
                {
                    var m = members[i];
                    if (m.Unit == Entity.Null || !_em.Exists(m.Unit)) continue;
                    if (!_em.HasComponent<LocalTransform>(m.Unit)) continue;

                    Vector3 spotFlat = leader + right * m.Slot.x + facing * m.Slot.y;
                    Vector3 spot = Ground(spotFlat.x, spotFlat.z);

                    var p = _em.GetComponentData<LocalTransform>(m.Unit).Position;
                    Vector3 unit = Ground(p.x, p.z);

                    Color c = m.CatchingUp != 0 ? CatchUpColor : SpotColor;
                    DrawRing(spot, Vector3.up, SpotRing, c);
                    DrawLine(new[] { unit, spot }, c);
                }
            }
        }

        // ─── drawing ──────────────────────────────────────────────────────

        /// <summary>Three orthogonal rings about a point. A single flat ring
        /// disappears edge-on the moment the camera drops toward the horizon,
        /// which is exactly when a formation problem is being watched.</summary>
        private void DrawGimbal(Vector3 centre, Vector3 facing, float radius, Color c)
        {
            Vector3 right = Vector3.Cross(Vector3.up, facing);
            DrawRing(centre, Vector3.up, radius, c);
            DrawRing(centre, facing, radius * 0.8f, c);
            DrawRing(centre, right, radius * 0.8f, c);
        }

        private void DrawRing(Vector3 centre, Vector3 axis, float radius, Color c)
        {
            axis = axis.sqrMagnitude < 1e-6f ? Vector3.up : axis.normalized;
            Vector3 a = Vector3.Cross(axis, Vector3.up);
            if (a.sqrMagnitude < 1e-4f) a = Vector3.Cross(axis, Vector3.right);
            a.Normalize();
            Vector3 b = Vector3.Cross(axis, a);

            var pts = new Vector3[RingPoints + 1];
            for (int i = 0; i <= RingPoints; i++)
            {
                float t = i / (float)RingPoints * Mathf.PI * 2f;
                pts[i] = centre + (a * Mathf.Cos(t) + b * Mathf.Sin(t)) * radius;
            }
            DrawLine(pts, c);
        }

        private void DrawLine(Vector3[] points, Color c)
        {
            var lr = Rent();
            lr.positionCount = points.Length;
            lr.SetPositions(points);
            lr.startColor = lr.endColor = c;
            lr.startWidth = lr.endWidth = LineWidth;
        }

        private Vector3 Ground(float x, float z)
            => new Vector3(x, TerrainUtility.GetHeight(x, z) + YOffset, z);

        // ─── pooling ──────────────────────────────────────────────────────

        private void Recycle()
        {
            for (int i = 0; i < _active.Count; i++)
            {
                _active[i].gameObject.SetActive(false);
                _pool.Add(_active[i]);
            }
            _active.Clear();
        }

        private LineRenderer Rent()
        {
            LineRenderer lr;
            if (_pool.Count > 0)
            {
                lr = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
                lr.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject("FormationDebugLine");
                go.transform.SetParent(transform, false);
                lr = go.AddComponent<LineRenderer>();
                lr.material = _mat;
                lr.useWorldSpace = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 0;
                lr.alignment = LineAlignment.View;
            }
            _active.Add(lr);
            return lr;
        }
    }
}
