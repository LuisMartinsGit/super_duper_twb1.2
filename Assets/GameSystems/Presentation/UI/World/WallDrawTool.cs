// WallDrawTool.cs
// Drawing a wall (docs/Design/Age_1_Alanthor.md § Drawing walls): while the
// mouse is held, the path follows the cursor as a curve that cannot bend
// tighter than the minimum radius (it runs at maximum curvature and catches
// up when the cursor is ahead again), retracing over the path erases it
// back to that point, and hub ghosts sit at the ends — plus, only once the
// stroke passes maxModulesPerSegment modules, evenly spaced ones between.
// On release the caller hands the curve (with its hub points) to
// CommandRouter as one PlaceWallPath order; between hubs the sim lays a
// single swept mesh along it. Pure input + preview: nothing here touches ECS
// except to ask whether a hub position is legal.
//
// Values live in WallDrawTool.asset beside this file.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Settings;
using TheWaningBorder.UI.Common;
using TheWaningBorder.World.Terrain;
using Wall = TheWaningBorder.Entities.AlanthorWall;

namespace TheWaningBorder.UI.World
{
    public sealed class WallDrawTool : MonoBehaviour
    {
        private WallDrawToolConfig _cfg;
        private WallDrawToolConfig Cfg
            => _cfg != null ? _cfg : (_cfg = ComponentConfig.Require<WallDrawToolConfig>());

        /// <summary>The path as drawn, world XZ, one point per sampleSpacing.</summary>
        private readonly List<Vector2> _pts = new List<Vector2>();
        /// <summary>Hub positions derived from the path (grid-snapped, terrain height).</summary>
        private readonly List<float3> _hubs = new List<float3>();
        private readonly List<bool> _hubValid = new List<bool>();

        public bool Drawing { get; private set; }
        public IReadOnlyList<float3> Hubs => _hubs;
        public bool AllHubsValid { get { for (int i = 0; i < _hubValid.Count; i++) if (!_hubValid[i]) return false; return true; } }
        public int PointCount => _pts.Count;

        // ── Preview ──
        private LineRenderer _pathLine;
        private readonly List<LineRenderer> _hubRings = new List<LineRenderer>();
        private Material _lineMat;
        private const int RingSegments = 20;

        void Awake()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                      ?? Shader.Find("Unlit/Color")
                      ?? Shader.Find("Sprites/Default");
            _lineMat = new Material(shader);
            if (_lineMat.HasProperty("_Surface")) _lineMat.SetFloat("_Surface", 1);
            _lineMat.renderQueue = 3000;
            _pathLine = MakeLine("WallDrawPath", Cfg != null ? Cfg.pathWidth : 0.4f);
            _pathLine.enabled = false;
        }

        void OnDestroy()
        {
            if (_lineMat != null) Destroy(_lineMat);
        }

        // ── Drawing ───────────────────────────────────────────────────────

        /// <summary>Start a path at <paramref name="start"/> (world XZ).</summary>
        public void Begin(Vector2 start)
        {
            _pts.Clear();
            _pts.Add(start);
            Drawing = true;
        }

        /// <summary>
        /// Advance the path toward the cursor. Backtracking first: if the
        /// cursor sits on an earlier part of the path, everything after that
        /// point is dropped. Then new points are appended one sample at a
        /// time, each turning no more than the curvature limit allows; when
        /// the cursor is behind the path's heading the path waits.
        /// </summary>
        public void Extend(Vector2 cursor)
        {
            if (!Drawing || Cfg == null) return;
            float spacing = Mathf.Max(0.25f, Cfg.sampleSpacing);

            // Backtrack: the earliest point (excluding the last few) the
            // cursor is within reach of.
            float br2 = Cfg.backtrackRadius * Cfg.backtrackRadius;
            for (int i = 0; i < _pts.Count - 2; i++)
            {
                if ((_pts[i] - cursor).sqrMagnitude <= br2)
                {
                    _pts.RemoveRange(i + 1, _pts.Count - (i + 1));
                    break;
                }
            }

            float maxTurn = spacing / Mathf.Max(1f, Cfg.minBendRadius);   // radians per sample
            int guard = 0;
            while ((cursor - _pts[_pts.Count - 1]).sqrMagnitude >= spacing * spacing && guard++ < 64)
            {
                if (_pts.Count >= Cfg.maxPoints) break;
                Vector2 last = _pts[_pts.Count - 1];
                Vector2 want = (cursor - last).normalized;
                Vector2 dir = want;
                if (_pts.Count >= 2)
                {
                    Vector2 heading = (last - _pts[_pts.Count - 2]).normalized;
                    // The cursor is behind the heading: the path waits rather
                    // than looping back on itself.
                    if (Vector2.Dot(heading, want) < -0.2f) break;
                    float angle = Vector2.SignedAngle(heading, want) * Mathf.Deg2Rad;
                    float clamped = Mathf.Clamp(angle, -maxTurn, maxTurn);
                    float c = Mathf.Cos(clamped), s = Mathf.Sin(clamped);
                    dir = new Vector2(heading.x * c - heading.y * s, heading.x * s + heading.y * c);
                }
                _pts.Add(last + dir * spacing);
            }
        }

        /// <summary>Stop drawing; the path and hubs stay available until Clear.</summary>
        public void End() => Drawing = false;

        public void Clear()
        {
            _pts.Clear(); _hubs.Clear(); _hubValid.Clear(); _endSnap = null;
            Drawing = false;
            HidePreview();
        }

        // ── Layout: the curve and its hubs ────────────────────────────────

        /// <summary>The curve as sent to the sim: samples every commandSampleSpacing
        /// metres, first and last always included, hub points snapped to the
        /// build grid, terrain height sampled.</summary>
        private readonly List<float3> _points = new List<float3>();
        private readonly List<CommandRouter.WallPathKind> _kinds = new List<CommandRouter.WallPathKind>();
        public IReadOnlyList<float3> Points => _points;
        public IReadOnlyList<CommandRouter.WallPathKind> Kinds => _kinds;
        /// <summary>Hubs the order pays for: new ones and wall cells converted into one.</summary>
        public int NewHubCount { get { int c = 0; foreach (var k in _kinds) if (k == CommandRouter.WallPathKind.NewHub || k == CommandRouter.WallPathKind.CellHub) c++; return c; } }
        /// <summary>Where the stroke's end was snapped to (a hub or a wall cell), if it was.</summary>
        private float3? _endSnap;

        /// <summary>
        /// Recompute the layout from the drawn path. Hubs: the start
        /// (<paramref name="startKind"/> — a new hub, the existing hub the
        /// path began on, or the wall cell it began on, which becomes one),
        /// the end (<paramref name="endKind"/> likewise — that is how a loop
        /// closes or a wall branches off another; <paramref name="endSnap"/>
        /// is the hub or cell the end snapped to and replaces the cursor
        /// point), and — only when the stroke is longer than
        /// maxModulesPerSegment modules — as many EVENLY SPACED hubs between
        /// them as it takes to get every run under the cap. Even spacing is
        /// what keeps the wall symmetrical: a stroke needing one extra hub
        /// gets it exactly at the midpoint. Between hubs the wall is ONE
        /// swept mesh along the samples, so a stroke under the cap is a
        /// single continuous curved wall.
        /// </summary>
        public void ComputeLayout(CommandRouter.WallPathKind startKind, CommandRouter.WallPathKind endKind,
            float3? endSnap, System.Func<float3, bool> isLegal)
        {
            _points.Clear(); _kinds.Clear(); _hubs.Clear(); _hubValid.Clear();
            _endSnap = endSnap;
            if (_pts.Count == 0 || Cfg == null) return;
            float sample = Mathf.Max(0.5f, Cfg.commandSampleSpacing);

            // Arc length per drawn point.
            var arc = new float[_pts.Count];
            for (int i = 1; i < _pts.Count; i++) arc[i] = arc[i - 1] + (_pts[i] - _pts[i - 1]).magnitude;
            float total = arc[_pts.Count - 1];

            // How many runs the stroke has to be cut into to keep every run
            // at or under the module cap, and therefore where the hubs go.
            // Equal arc intervals — the symmetry is the point: one extra hub
            // lands at the midpoint, two at the thirds.
            float hubEvery = 0f;
            if (Cfg.maxModulesPerSegment > 0)
            {
                float maxRun = Cfg.maxModulesPerSegment * Wall.InstanceSpacing + 2f * Wall.HubInsetMetres;
                int runs = Mathf.Max(1, Mathf.CeilToInt(total / maxRun - 0.001f));
                if (runs > 1) hubEvery = total / runs;
            }

            // Which drawn points become samples / hubs.
            var pick = new List<int>();
            var kind = new List<CommandRouter.WallPathKind>();
            float nextSample = 0f, nextHub = hubEvery;
            for (int i = 0; i < _pts.Count; i++)
            {
                bool first = i == 0, last = i == _pts.Count - 1;
                bool hub = false;
                if (!first && !last && hubEvery > 0f && arc[i] >= nextHub && total - arc[i] > hubEvery * 0.5f)
                { hub = true; nextHub += hubEvery; }

                if (first || last || hub || arc[i] >= nextSample)
                {
                    pick.Add(i);
                    kind.Add(first ? startKind
                          : last  ? endKind
                          : hub   ? CommandRouter.WallPathKind.NewHub
                          : CommandRouter.WallPathKind.Point);
                    nextSample = arc[i] + sample;
                }
            }
            // A single press with no drag: one point, one new hub.
            if (_pts.Count == 1) { pick.Clear(); kind.Clear(); pick.Add(0); kind.Add(startKind); }

            for (int j = 0; j < pick.Count; j++)
            {
                Vector2 xz = _pts[pick[j]];
                var k = kind[j];
                bool isEnd = j == pick.Count - 1 && pick.Count > 1;
                if (isEnd && endSnap.HasValue) xz = new Vector2(endSnap.Value.x, endSnap.Value.z);
                var p = new float3(xz.x, TerrainUtility.GetHeight(xz.x, xz.y), xz.y);
                // Nothing on a drawn wall is grid-snapped (2026-09-24) -- not
                // the curve and not the hubs on it. The hub is the one building
                // exempt from the build grid precisely so a drawn wall runs
                // where it was drawn; snapping either half kinked the line at
                // every hub the run cap inserted.
                // docs/Design/Build_Grid.md § 5
                _points.Add(p); _kinds.Add(k);
                if (k == CommandRouter.WallPathKind.NewHub)
                {
                    _hubs.Add(p);
                    _hubValid.Add(isLegal == null || isLegal(p));
                }
                else if (k == CommandRouter.WallPathKind.CellHub)
                {
                    // A standing cell: the hub is legal by construction.
                    _hubs.Add(p);
                    _hubValid.Add(true);
                }
            }
        }

        // ── Preview ───────────────────────────────────────────────────────

        public void ShowPreview()
        {
            if (Cfg == null) return;
            var ok = WorldOverlayPalette.Accent;
            var bad = Cfg.invalidColor;

            if (_pts.Count >= 2)
            {
                _pathLine.enabled = true;
                // The line runs on to the hub or cell the end snapped to.
                int extra = _endSnap.HasValue ? 1 : 0;
                _pathLine.positionCount = _pts.Count + extra;
                for (int i = 0; i < _pts.Count; i++)
                    _pathLine.SetPosition(i, new Vector3(_pts[i].x,
                        TerrainUtility.GetHeight(_pts[i].x, _pts[i].y) + Cfg.groundOffset, _pts[i].y));
                if (extra == 1)
                    _pathLine.SetPosition(_pts.Count, new Vector3(_endSnap.Value.x,
                        TerrainUtility.GetHeight(_endSnap.Value.x, _endSnap.Value.z) + Cfg.groundOffset, _endSnap.Value.z));
                Tint(_pathLine, AllHubsValid ? ok : bad);
            }
            else _pathLine.enabled = false;

            while (_hubRings.Count < _hubs.Count)
            {
                var ring = MakeLine("WallDrawHub" + _hubRings.Count, Cfg.pathWidth * 0.75f);
                ring.loop = true;
                ring.positionCount = RingSegments;
                _hubRings.Add(ring);
            }
            float r = Cfg.hubRingRadius;
            for (int h = 0; h < _hubRings.Count; h++)
            {
                var ring = _hubRings[h];
                if (h >= _hubs.Count) { ring.enabled = false; continue; }
                ring.enabled = true;
                var c = _hubs[h];
                for (int k = 0; k < RingSegments; k++)
                {
                    float a = k / (float)RingSegments * Mathf.PI * 2f;
                    float x = c.x + Mathf.Cos(a) * r, z = c.z + Mathf.Sin(a) * r;
                    ring.SetPosition(k, new Vector3(x, TerrainUtility.GetHeight(x, z) + Cfg.groundOffset, z));
                }
                Tint(ring, _hubValid[h] ? ok : bad);
            }
        }

        public void HidePreview()
        {
            if (_pathLine != null) _pathLine.enabled = false;
            foreach (var ring in _hubRings) ring.enabled = false;
        }

        private LineRenderer MakeLine(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.material = new Material(_lineMat);
            lr.startWidth = width; lr.endWidth = width;
            lr.useWorldSpace = true;
            lr.positionCount = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCornerVertices = 4;
            return lr;
        }

        private static void Tint(LineRenderer lr, Color color)
        {
            lr.startColor = color; lr.endColor = color;
            var mat = lr.material;
            if (mat == null) return;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        }
    }
}
