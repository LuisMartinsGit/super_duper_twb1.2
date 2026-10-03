// WallDrawTool.cs
// Drawing a wall (docs/Design/Age_1_Alanthor.md § Drawing walls): while the
// mouse is held, the path FOLLOWS THE DRAG -- the cursor's track is sampled
// every sampleSpacing metres, smoothed, and resampled; retracing over the
// path erases it back to that point, and hub ghosts sit at the ends -- plus,
// only once the stroke passes maxModulesPerSegment modules, evenly spaced
// ones between. The minimum bend radius is a VALIDITY rule, not a steering
// law: a stroke that bends tighter than it (after the offending windows are
// relaxed to iron out hand jitter), or runs back over itself, shows the
// stretch at fault in red and is refused on release. A stroke closes into a
// loop only when it is long enough to be a legal loop AND its end comes back
// within snap reach of its start; the start shows a close ring while it would.
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

        /// <summary>The cursor's track while dragging, world XZ, one point per
        /// sampleSpacing of cursor travel. The first point is the start.</summary>
        private readonly List<Vector2> _raw = new List<Vector2>();
        /// <summary>Arc length at each <see cref="_raw"/> point.</summary>
        private readonly List<float> _rawArc = new List<float>();
        /// <summary>Where the cursor is now; the path's live end.</summary>
        private Vector2 _cursor;
        /// <summary>The path as laid out: <see cref="_raw"/> plus the live end,
        /// smoothed, resampled at sampleSpacing and bent onto the end snap.
        /// Rebuilt by <see cref="ComputeLayout"/>.</summary>
        private readonly List<Vector2> _pts = new List<Vector2>();
        /// <summary>Hub positions derived from the path (terrain height).</summary>
        private readonly List<float3> _hubs = new List<float3>();
        private readonly List<bool> _hubValid = new List<bool>();

        /// <summary>Why a path is refused, or None.</summary>
        public enum PathProblem { None, TooTight, CrossesItself, Blocked }

        public bool Drawing { get; private set; }
        public IReadOnlyList<float3> Hubs => _hubs;
        public bool AllHubsValid { get { for (int i = 0; i < _hubValid.Count; i++) if (!_hubValid[i]) return false; return true; } }
        /// <summary>The path's own shape rule, from the last layout: None when
        /// it bends no tighter than minBendRadius and does not run back over itself.</summary>
        public PathProblem Problem { get; private set; }
        /// <summary>Hubs legal AND the path's shape legal.</summary>
        public bool Valid => AllHubsValid && Problem == PathProblem.None;
        /// <summary>The last layout's path ends on its own start: releasing
        /// now closes the wall into a loop. Previewed by a distinct ring on
        /// the start.</summary>
        public bool ClosesLoop { get; private set; }
        /// <summary>The shortest stroke that may close into a loop, metres:
        /// the circumference of a circle of minBendRadius. Anything shorter
        /// cannot be a legal loop, so its end near its start is an open end,
        /// never a silent ring.</summary>
        public float MinLoopLength => Cfg == null ? 0f : 2f * Mathf.PI * Cfg.minBendRadius;
        /// <summary>Path-point range of the shape problem (-1 = none), for the preview.</summary>
        private int _badFrom = -1, _badTo = -1;
        private int CurvatureK
            => Mathf.Max(1, Mathf.RoundToInt(Cfg.curvatureWindow / Mathf.Max(0.25f, Cfg.sampleSpacing)));
        /// <summary>Drag samples so far (1 = a press with no drag yet).</summary>
        public int PointCount => _raw.Count;
        /// <summary>Length of the drag so far, metres (the live end included).</summary>
        public float Length => _raw.Count == 0 ? 0f
            : _rawArc[_rawArc.Count - 1] + (EndXZ - _raw[_raw.Count - 1]).magnitude;
        /// <summary>The path's actual end, world XZ -- what end snapping tests
        /// against: the cursor, while it is within a sample of the track's
        /// last point (it always is while drawing, but not once the track
        /// has hit maxPoints and stopped growing).</summary>
        public Vector2 EndXZ
        {
            get
            {
                if (_raw.Count == 0) return Vector2.zero;
                Vector2 last = _raw[_raw.Count - 1];
                if (_raw.Count < 2 || Cfg == null) return last;
                float reach = 1.5f * Mathf.Max(0.25f, Cfg.sampleSpacing);
                return (_cursor - last).sqrMagnitude <= reach * reach ? _cursor : last;
            }
        }

        // ── Preview ──
        private LineRenderer _pathLine;
        private LineRenderer _problemLine;
        private LineRenderer _loopRing;
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
            _raw.Clear(); _rawArc.Clear(); _pts.Clear();
            _raw.Add(start); _rawArc.Add(0f);
            _cursor = start;
            Problem = PathProblem.None;
            ClosesLoop = false;
            _badFrom = _badTo = -1;
            Drawing = true;
        }

        /// <summary>
        /// Follow the cursor. Backtracking first: if the cursor sits on an
        /// earlier part of the track, everything after that point is dropped.
        /// Then the track is extended along the straight line from its last
        /// sample to the cursor, one sample per sampleSpacing -- so the path
        /// never grows by more than the cursor actually moved, and never
        /// turns on its own. How tightly it bends is judged later, in
        /// <see cref="ComputeLayout"/>, as a validity rule.
        /// </summary>
        public void Extend(Vector2 cursor)
        {
            if (!Drawing || Cfg == null || _raw.Count == 0) return;
            float spacing = Mathf.Max(0.25f, Cfg.sampleSpacing);
            _cursor = cursor;

            // Backtrack: the earliest sample the cursor is within reach of,
            // ignoring the tail the cursor is always near while drawing
            // forward. Once the stroke is long enough to be a loop, the
            // first closeGuardRadius of it is ignored too, so bringing the
            // end home to close a loop does not erase the whole stroke.
            float total = _rawArc[_rawArc.Count - 1];
            float keepTail = 3f * Cfg.backtrackRadius + spacing;
            float guard = total >= 3f * Cfg.closeGuardRadius ? Cfg.closeGuardRadius : -1f;
            float br2 = Cfg.backtrackRadius * Cfg.backtrackRadius;
            for (int i = 0; i < _raw.Count; i++)
            {
                if (total - _rawArc[i] < keepTail) break;
                if (_rawArc[i] < guard) continue;
                if ((_raw[i] - cursor).sqrMagnitude <= br2)
                {
                    _raw.RemoveRange(i + 1, _raw.Count - (i + 1));
                    _rawArc.RemoveRange(i + 1, _rawArc.Count - (i + 1));
                    break;
                }
            }

            // Retrace eats the tail: while the cursor is nearer the
            // second-to-last sample than the last, it is moving back along
            // the path, so the last sample goes. Drawing forward never trips
            // it -- only a turn of well over 90 degrees within one sample,
            // which no legal bend comes near. Without it a retrace would
            // first grow a hairpin, which the far backtrack above only
            // reaches ~3 m later.
            while (_raw.Count >= 2
                && (cursor - _raw[_raw.Count - 2]).sqrMagnitude < (cursor - _raw[_raw.Count - 1]).sqrMagnitude)
            {
                _raw.RemoveAt(_raw.Count - 1);
                _rawArc.RemoveAt(_rawArc.Count - 1);
            }

            // Extend along the cursor's own track.
            Vector2 last = _raw[_raw.Count - 1];
            Vector2 d = cursor - last;
            float len = d.magnitude;
            if (len < spacing) return;
            Vector2 step = d / len * spacing;
            while (len >= spacing && _raw.Count < Cfg.maxPoints)
            {
                last += step;
                _rawArc.Add(_rawArc[_rawArc.Count - 1] + spacing);
                _raw.Add(last);
                len -= spacing;
            }
        }

        /// <summary>Stop drawing; the path and hubs stay available until Clear.</summary>
        public void End() => Drawing = false;

        public void Clear()
        {
            _raw.Clear(); _rawArc.Clear(); _pts.Clear();
            _hubs.Clear(); _hubValid.Clear();
            Problem = PathProblem.None;
            ClosesLoop = false;
            _badFrom = _badTo = -1;
            Drawing = false;
            HidePreview();
        }

        // ── Shaping: track -> path ────────────────────────────────────────

        private readonly List<Vector2> _tmpA = new List<Vector2>();
        private readonly List<Vector2> _tmpB = new List<Vector2>();
        private readonly List<float> _arc = new List<float>();

        /// <summary>
        /// Rebuild <see cref="_pts"/> from the track: the live end appended,
        /// a symmetric moving average over smoothingRadius (ends pinned, the
        /// window shrinking toward them so it never drags an end), resampled
        /// at sampleSpacing, and -- when the end snapped -- the last
        /// snapBlendLength metres bent smoothly onto the snap target, instead
        /// of a straight chord swapped in for the last sample.
        /// </summary>
        private void BuildPath(float3? endSnap)
        {
            _pts.Clear();
            if (_raw.Count == 0) return;
            float spacing = Mathf.Max(0.25f, Cfg.sampleSpacing);

            _tmpA.Clear();
            _tmpA.AddRange(_raw);
            Vector2 end = EndXZ;
            if ((end - _raw[_raw.Count - 1]).sqrMagnitude > 0.0625f * spacing * spacing)
                _tmpA.Add(end);
            if (_tmpA.Count == 1) { _pts.Add(_tmpA[0]); return; }

            int w = Mathf.Max(0, Mathf.RoundToInt(Cfg.smoothingRadius / spacing));
            _tmpB.Clear();
            int n = _tmpA.Count;
            for (int i = 0; i < n; i++)
            {
                int k = Mathf.Min(w, Mathf.Min(i, n - 1 - i));
                Vector2 sum = Vector2.zero;
                for (int j = i - k; j <= i + k; j++) sum += _tmpA[j];
                _tmpB.Add(sum / (2 * k + 1));
            }

            Resample(_tmpB, spacing, _pts);

            if (endSnap.HasValue && _pts.Count >= 2)
            {
                Vector2 target = new Vector2(endSnap.Value.x, endSnap.Value.z);
                Vector2 start = _pts[0];
                Vector2 tail = _pts[_pts.Count - 1];
                Vector2 delta = target - tail;
                float total = 0f;
                for (int i = 1; i < _pts.Count; i++) total += (_pts[i] - _pts[i - 1]).magnitude;

                // A smoothstep offset of d over L bends at most 6d/L^2, so
                // the blend has to be this long for the snap's own bend to
                // stay inside the bend limit.
                float required = Mathf.Max(spacing, Mathf.Max(Cfg.snapBlendLength,
                    Mathf.Sqrt(6f * delta.magnitude * Cfg.minBendRadius * 1.25f)));
                float cap = Mathf.Min(Cfg.snapBlendMaxLength, Cfg.snapBlendMaxFraction * total);
                Vector2 chordNow = tail - start, chordNew = target - start;
                bool canRotate = chordNow.sqrMagnitude > 1e-4f && chordNew.sqrMagnitude > 1e-4f
                    && chordNow.magnitude >= 2f * delta.magnitude;

                if (required <= cap || !canRotate)
                {
                    // Local blend. Capped where it fits; where it cannot
                    // (a hook whose end is nearly back at its start, e.g. a
                    // loop closing), the old rule -- as long as the bend
                    // needs, but never the whole stroke.
                    float blend = required <= cap ? required
                                : Mathf.Max(spacing, Mathf.Min(required, 0.9f * total));
                    float fromEnd = 0f;
                    // Never move the start.
                    for (int i = _pts.Count - 1; i >= 1 && fromEnd < blend; i--)
                    {
                        float t = 1f - fromEnd / blend;
                        _pts[i] += delta * (t * t * (3f - 2f * t));
                        fromEnd += (_pts[i] - _pts[i - 1]).magnitude;
                    }
                }
                else
                {
                    // The blend would bend too much of the stroke: rotate and
                    // scale the whole stroke about its start so its end lands
                    // on the target. The drawn shape survives (a straight drag
                    // stays straight, it just points at the hub) and no bend
                    // is added anywhere. canRotate keeps the angle under ~30
                    // degrees. q = chordNew / chordNow as complex numbers.
                    float den = chordNow.sqrMagnitude;
                    float qr = (chordNew.x * chordNow.x + chordNew.y * chordNow.y) / den;
                    float qi = (chordNew.y * chordNow.x - chordNew.x * chordNow.y) / den;
                    for (int i = 1; i < _pts.Count; i++)
                    {
                        Vector2 v = _pts[i] - start;
                        _pts[i] = start + new Vector2(qr * v.x - qi * v.y, qr * v.y + qi * v.x);
                    }
                }
                _pts[_pts.Count - 1] = target;
            }
        }

        /// <summary>The bend test at path point <paramref name="i"/>: the
        /// circle through the samples <paramref name="k"/> either side of it
        /// has a radius under minBendRadius.</summary>
        private bool IsTightAt(int i, int k)
        {
            Vector2 a = _pts[i - k], b = _pts[i], c = _pts[i + k];
            float cross = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
            if (cross < 1e-4f) return false;               // straight
            float r = (b - c).magnitude * (a - c).magnitude * (a - b).magnitude / (2f * cross);
            return r < Cfg.minBendRadius;
        }

        private readonly List<bool> _relaxMask = new List<bool>();

        /// <summary>
        /// One relaxation pass over the windows that fail the bend test: every
        /// interior point within a curvature window of an offender is pulled
        /// toward its neighbours' midpoint (two sweeps). The start and the end
        /// are pinned, so snaps and hubs stay put. Hand jitter irons out in a
        /// few passes; a deliberate corner needs far more than bendRelaxPasses
        /// and is still refused.
        /// </summary>
        private bool RelaxTight(int k)
        {
            int n = _pts.Count;
            _relaxMask.Clear();
            for (int i = 0; i < n; i++) _relaxMask.Add(false);
            bool any = false;
            for (int i = k; i + k < n; i++)
            {
                if (!IsTightAt(i, k)) continue;
                any = true;
                for (int j = Mathf.Max(1, i - k); j <= Mathf.Min(n - 2, i + k); j++) _relaxMask[j] = true;
            }
            if (!any) return false;
            for (int sweep = 0; sweep < 2; sweep++)
            {
                _tmpA.Clear();
                _tmpA.AddRange(_pts);
                for (int i = 1; i < n - 1; i++)
                    if (_relaxMask[i])
                        _pts[i] = 0.5f * _tmpA[i] + 0.25f * (_tmpA[i - 1] + _tmpA[i + 1]);
            }
            return true;
        }

        /// <summary>Resample a polyline at a fixed arc spacing; first and last kept.</summary>
        private static void Resample(List<Vector2> src, float spacing, List<Vector2> dst)
        {
            dst.Clear();
            if (src.Count == 0) return;
            dst.Add(src[0]);
            float carry = 0f;   // distance travelled since the last emitted sample
            for (int i = 1; i < src.Count; i++)
            {
                Vector2 a = src[i - 1], b = src[i];
                float seg = (b - a).magnitude;
                if (seg < 1e-5f) continue;
                float at = spacing - carry;
                while (at <= seg)
                {
                    dst.Add(a + (b - a) * (at / seg));
                    at += spacing;
                }
                carry = seg - (at - spacing);
            }
            Vector2 end = src[src.Count - 1];
            if (dst.Count == 1 || (end - dst[dst.Count - 1]).sqrMagnitude > 0.0625f * spacing * spacing) dst.Add(end);
            else dst[dst.Count - 1] = end;
        }

        /// <summary>
        /// The shape rule, on <see cref="_pts"/> (arc lengths in <see cref="_arc"/>).
        /// TooTight: the circle through three samples curvatureWindow metres
        /// apart has a radius under minBendRadius. CrossesItself: two samples
        /// more than 4 x selfClearance apart ALONG the path are within
        /// selfClearance of each other ON THE GROUND -- a legal curve cannot
        /// do that (at a 12 m radius a 12 m arc still spans 11.5 m), so it
        /// means the stroke doubled back, crossed, or spiralled onto itself.
        /// A loop closing onto its own start is exempt where its ends meet.
        /// </summary>
        private PathProblem Validate(bool closesLoop)
        {
            _badFrom = _badTo = -1;
            int n = _pts.Count;
            if (n < 3) return PathProblem.None;
            int k = CurvatureK;

            // Every offending window, so the preview can mark the whole
            // stretch that bends too tightly.
            for (int i = k; i + k < n; i++)
            {
                if (!IsTightAt(i, k)) continue;
                if (_badFrom < 0) _badFrom = i - k;
                _badTo = i + k;
            }
            if (_badFrom >= 0) return PathProblem.TooTight;

            float clear = Cfg.selfClearance;
            float clear2 = clear * clear;
            float apart = 4f * clear;
            float total = _arc[n - 1];
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (_arc[j] - _arc[i] <= apart) continue;
                    if (closesLoop && _arc[i] < apart && total - _arc[j] < apart) continue;
                    if ((_pts[i] - _pts[j]).sqrMagnitude < clear2)
                    {
                        _badFrom = i; _badTo = j;
                        return PathProblem.CrossesItself;
                    }
                }
            }
            return PathProblem.None;
        }

        /// <summary>
        /// The whole-length collision rule (docs/Design/Build_Grid.md § Walls
        /// on the grid): run <paramref name="blockedAt"/> (a path point and
        /// its heading) over the laid-out path and mark the stretch that runs
        /// into something. Call after <see cref="ComputeLayout"/>; a shape
        /// problem already found wins. The executor applies the same test, so
        /// what shows red here is what would be refused.
        /// </summary>
        public void MarkBlocked(System.Func<float3, float3, bool> blockedAt)
        {
            if (Problem != PathProblem.None || blockedAt == null) return;
            int n = _pts.Count;
            if (n < 2) return;
            int from = -1, to = -1;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = _pts[i > 0 ? i - 1 : i], b = _pts[i < n - 1 ? i + 1 : i];
                var tan = new float3(b.x - a.x, 0f, b.y - a.y);
                var p = new float3(_pts[i].x, TerrainUtility.GetHeight(_pts[i].x, _pts[i].y), _pts[i].y);
                if (!blockedAt(p, tan)) continue;
                if (from < 0) from = i;
                to = i;
            }
            if (from < 0) return;
            Problem = PathProblem.Blocked;
            _badFrom = Mathf.Max(0, from - 1);
            _badTo = Mathf.Min(n - 1, Mathf.Max(to + 1, _badFrom + 1));
        }

        // ── Layout: the curve and its hubs ────────────────────────────────

        /// <summary>The curve as sent to the sim: samples every commandSampleSpacing
        /// metres, first and last always included, terrain height sampled.</summary>
        private readonly List<float3> _points = new List<float3>();
        private readonly List<CommandRouter.WallPathKind> _kinds = new List<CommandRouter.WallPathKind>();
        public IReadOnlyList<float3> Points => _points;
        public IReadOnlyList<CommandRouter.WallPathKind> Kinds => _kinds;
        /// <summary>Hubs the order pays for: new ones and wall cells converted into one.</summary>
        public int NewHubCount { get { int c = 0; foreach (var k in _kinds) if (k == CommandRouter.WallPathKind.NewHub || k == CommandRouter.WallPathKind.CellHub) c++; return c; } }

        /// <summary>
        /// Recompute the path and layout from the drag. Hubs: the start
        /// (<paramref name="startKind"/> — a new hub, the existing hub the
        /// path began on, or the wall cell it began on, which becomes one),
        /// the end (<paramref name="endKind"/> likewise — that is how a loop
        /// closes or a wall branches off another; <paramref name="endSnap"/>
        /// is the hub or cell the end snapped to, and the path's tail is bent
        /// onto it), and — only when the stroke is longer than
        /// maxModulesPerSegment modules — as many EVENLY SPACED hubs between
        /// them as it takes to get every run under the cap. Even spacing is
        /// what keeps the wall symmetrical: a stroke needing one extra hub
        /// gets it exactly at the midpoint. Between hubs the wall is ONE
        /// swept mesh along the samples, so a stroke under the cap is a
        /// single continuous curved wall.
        ///
        /// A stroke whose end snapped back onto its OWN start is a closed
        /// loop and gets at least three runs whatever its length: with one
        /// run the segment would join a hub to itself, and with two the
        /// second would join the same pair of hubs as the first, which the
        /// executor skips as already connected — either way a gap.
        /// </summary>
        public void ComputeLayout(CommandRouter.WallPathKind startKind, CommandRouter.WallPathKind endKind,
            float3? endSnap, System.Func<float3, bool> isLegal)
        {
            _points.Clear(); _kinds.Clear(); _hubs.Clear(); _hubValid.Clear();
            Problem = PathProblem.None;
            ClosesLoop = false;
            _badFrom = _badTo = -1;
            if (_raw.Count == 0 || Cfg == null) return;
            float sample = Mathf.Max(0.5f, Cfg.commandSampleSpacing);

            BuildPath(endSnap);
            bool closesLoop = endSnap.HasValue && _pts.Count >= 2
                && (new Vector2(endSnap.Value.x, endSnap.Value.z) - _pts[0]).sqrMagnitude < 0.25f;
            ClosesLoop = closesLoop;

            // Arc length per path point.
            RebuildArc();
            Problem = Validate(closesLoop);
            // Too tight: first try to iron the offending windows out (hand
            // jitter), and only refuse what is still too tight after that.
            for (int pass = 0; pass < Cfg.bendRelaxPasses && Problem == PathProblem.TooTight; pass++)
            {
                if (!RelaxTight(CurvatureK)) break;
                RebuildArc();
                Problem = Validate(closesLoop);
            }
            float total = _arc[_pts.Count - 1];

            // How many runs the stroke has to be cut into to keep every run
            // at or under the module cap, and therefore where the hubs go.
            // Equal arc intervals — the symmetry is the point: one extra hub
            // lands at the midpoint, two at the thirds.
            float hubEvery = 0f;
            int runs = 1;
            if (Cfg.maxModulesPerSegment > 0)
            {
                float maxRun = Cfg.maxModulesPerSegment * Wall.InstanceSpacing + 2f * Wall.HubInsetMetres;
                runs = Mathf.Max(1, Mathf.CeilToInt(total / maxRun - 0.001f));
            }
            if (closesLoop) runs = Mathf.Max(3, runs);
            if (runs > 1) hubEvery = total / runs;

            // Which path points become samples / hubs.
            var pick = new List<int>();
            var kind = new List<CommandRouter.WallPathKind>();
            float nextSample = 0f, nextHub = hubEvery;
            for (int i = 0; i < _pts.Count; i++)
            {
                bool first = i == 0, last = i == _pts.Count - 1;
                bool hub = false;
                if (!first && !last && hubEvery > 0f && _arc[i] >= nextHub && total - _arc[i] > hubEvery * 0.5f)
                { hub = true; nextHub += hubEvery; }

                if (first || last || hub || _arc[i] >= nextSample)
                {
                    pick.Add(i);
                    kind.Add(first ? startKind
                          : last  ? endKind
                          : hub   ? CommandRouter.WallPathKind.NewHub
                          : CommandRouter.WallPathKind.Point);
                    nextSample = _arc[i] + sample;
                }
            }
            // A single press with no drag: one point, one new hub.
            if (_pts.Count == 1) { pick.Clear(); kind.Clear(); pick.Add(0); kind.Add(startKind); }

            for (int j = 0; j < pick.Count; j++)
            {
                Vector2 xz = _pts[pick[j]];
                var k = kind[j];
                // No snap substitution here: BuildPath already bent the
                // path's tail onto the snap target, so the last point IS it.
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

        private void RebuildArc()
        {
            _arc.Clear();
            _arc.Add(0f);
            for (int i = 1; i < _pts.Count; i++) _arc.Add(_arc[i - 1] + (_pts[i] - _pts[i - 1]).magnitude);
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
                // The path already runs on to the hub or cell the end snapped
                // to (its tail is bent there), so the line is just the path.
                _pathLine.positionCount = _pts.Count;
                for (int i = 0; i < _pts.Count; i++)
                    _pathLine.SetPosition(i, new Vector3(_pts[i].x,
                        TerrainUtility.GetHeight(_pts[i].x, _pts[i].y) + Cfg.groundOffset, _pts[i].y));
                // A shape problem marks only the stretch at fault (drawn over
                // the path); illegal hubs turn the whole path red.
                bool section = Problem != PathProblem.None && _badFrom >= 0 && _badTo > _badFrom
                    && _badTo < _pts.Count;
                Tint(_pathLine, !AllHubsValid || (Problem != PathProblem.None && !section) ? bad : ok);
                if (_problemLine == null) _problemLine = MakeLine("WallDrawProblem", Cfg.pathWidth * 1.6f);
                _problemLine.enabled = section;
                if (section)
                {
                    _problemLine.positionCount = _badTo - _badFrom + 1;
                    for (int i = _badFrom; i <= _badTo; i++)
                        _problemLine.SetPosition(i - _badFrom, new Vector3(_pts[i].x,
                            TerrainUtility.GetHeight(_pts[i].x, _pts[i].y) + Cfg.groundOffset * 1.5f, _pts[i].y));
                    Tint(_problemLine, bad);
                }
            }
            else
            {
                _pathLine.enabled = false;
                if (_problemLine != null) _problemLine.enabled = false;
            }

            // Closing a loop: a distinct, wider ring on the start, so a ring
            // is never made without the player seeing it coming.
            if (_loopRing == null)
            {
                _loopRing = MakeLine("WallDrawLoopClose", Cfg.pathWidth);
                _loopRing.loop = true;
                _loopRing.positionCount = RingSegments;
            }
            _loopRing.enabled = ClosesLoop && _pts.Count >= 2;
            if (_loopRing.enabled)
            {
                float lr = Cfg.hubRingRadius * 1.4f;
                Vector2 s = _pts[0];
                for (int k = 0; k < RingSegments; k++)
                {
                    float a = k / (float)RingSegments * Mathf.PI * 2f;
                    float x = s.x + Mathf.Cos(a) * lr, z = s.y + Mathf.Sin(a) * lr;
                    _loopRing.SetPosition(k, new Vector3(x, TerrainUtility.GetHeight(x, z) + Cfg.groundOffset, z));
                }
                Tint(_loopRing, Cfg.loopCloseColor);
            }

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
            if (_problemLine != null) _problemLine.enabled = false;
            if (_loopRing != null) _loopRing.enabled = false;
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
