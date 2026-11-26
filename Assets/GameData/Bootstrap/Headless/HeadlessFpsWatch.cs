// HeadlessFpsWatch.cs
// The frame rate of a whole match, watched in a windowed player (2026-11-25):
//   "The Waning Border.exe" -twbHeadless -twbPlayers 8 -twbMap Highfold -twbSpeed 1
//       -twbLimit 2700 -twbNoLauncher -twbFpsWatch <folder>
// (no -batchmode / -nographics). The camera stays on the thickest crowd of units.
// Every window writes one row to fps.csv: frame time, where the CPU spent it by
// player-loop phase, the GPU, and what there was to simulate; every minute two
// seconds run with the camera drawing nothing ("blank"), which leaves what the
// simulation and UI cost with no graphics card in the way.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using Unity.Transforms;
using UnityEngine;
using TheWaningBorder.CameraRig;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Diagnostics;

namespace TheWaningBorder.Bootstrap
{
    internal sealed partial class HeadlessFpsWatch : MonoBehaviour
    {
        const float Window = 15f, BlankSeconds = 2f, BlankEvery = 60f, CameraEvery = 5f, ShotEvery = 300f;
        static readonly ComponentType[] QT_Units = { ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<LocalTransform>() };
        static CachedEntityQuery QC_Units;

        string _dir;
        readonly List<float> _f = new();
        readonly double[] _phase = new double[8];
        double _gpu, _main, _render;
        int _ft, _mask, _windows;
        float _windowAt, _blankAt = BlankEvery, _cameraAt, _shotAt = 60f;
        double _simAt;
        bool _blank, _started;
        readonly FrameTiming[] _t = new FrameTiming[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            string dir = CommandLine.Str(Environment.GetCommandLineArgs(), "-twbFpsWatch");
            if (string.IsNullOrEmpty(dir)) return;
            var go = new GameObject("HeadlessFpsWatch");
            DontDestroyOnLoad(go);
            go.AddComponent<HeadlessFpsWatch>().Begin(dir);
        }

        void Begin(string dir)
        {
            _dir = Path.GetFullPath(dir);
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Csv, "sim_s,kind,fps,avg_ms,median,p95,max,main,render,gpu,init,early,fixed,preupd,update,prelate,postlate,tail,units,near,sim_rate\n");
        }

        string Csv => Path.Combine(_dir, "fps.csv");

        void LateUpdate()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            var cam = Camera.main;
            if (w == null || !w.IsCreated || cam == null || PresentationState.LoadingOverlayVisible || SimClock.Now < 5.0) return;
            if (!_started)
            {
                _started = true;
                foreach (var c in FindObjectsByType<CameraController>(FindObjectsSortMode.None)) c.enabled = false;
                _mask = cam.cullingMask;
                foreach (var o in FindObjectsByType<DebugLogOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None)) o.enabled = false;
                Open();
                _blankAt = Time.unscaledTime + BlankEvery;
                _shotAt = Time.unscaledTime + 60f;
                if (Debug.isDebugBuild && Array.IndexOf(Environment.GetCommandLineArgs(), "-twbAblate") >= 0) { _blankAt = float.MaxValue; StartCoroutine(AblateLater()); }
            }
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            if (Debug.isDebugBuild) Markers();
            FrameTimingManager.CaptureFrameTimings();
            float now = Time.unscaledTime;
            if (now >= _windowAt)      // the first half second after a switch is not counted
            {
                _f.Add(Time.unscaledDeltaTime * 1000f);
                for (int i = 0; i < 8; i++) _phase[i] += PlayerLoopPhaseProfiler.LastMs(i);
                if (FrameTimingManager.GetLatestTimings(1, _t) > 0) { _gpu += _t[0].gpuFrameTime; _main += _t[0].cpuMainThreadFrameTime; _render += _t[0].cpuRenderThreadFrameTime; _ft++; }
            }
            if (now >= _windowAt + (_blank ? BlankSeconds : Window)) Close(w.EntityManager, cam, now);
            if (!_blank && now >= _cameraAt) { _cameraAt = now + CameraEvery; Follow(w.EntityManager, cam); }
            if (!_blank && now >= _shotAt) { _shotAt = now + ShotEvery; ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"at_{SimClock.Now:0000}s.png")); }
        }

        void Open()
        {
            _windowAt = Time.unscaledTime + 0.5f;
            _simAt = SimClock.Now;
            _f.Clear();
            Array.Clear(_phase, 0, 8);
            _gpu = _main = _render = 0; _ft = 0;
        }

        void Close(EntityManager em, Camera cam, float now)
        {
            if (_f.Count > 0)
            {
                var s = _f.OrderBy(x => x).ToList();
                float avg = s.Average();
                double n = Math.Max(1, _ft);
                double rate = (SimClock.Now - _simAt) / Math.Max(0.01, now - _windowAt + 0.5);
                File.AppendAllText(Csv, FormattableString.Invariant($"{SimClock.Now:0},{(_blank ? "blank" : "normal")},{1000f / avg:0},{avg:0.0},{s[s.Count / 2]:0.0},{s[(int)(s.Count * 0.95f)]:0.0},{s[s.Count - 1]:0},{_main / n:0.0},{_render / n:0.0},{_gpu / n:0.0},")
                    + string.Join(",", _phase.Select(p => FormattableString.Invariant($"{p / s.Count:0.0}")))
                    + FormattableString.Invariant($",{QC_Units.Get(em, QT_Units).CalculateEntityCount()},{_near},{rate:0.00}\n"));
            }
            bool wasBlank = _blank;
            _blank = !wasBlank && now >= _blankAt;
            if (_blank) { cam.cullingMask = 0; _blankAt = now + BlankEvery; }
            else if (wasBlank) { cam.cullingMask = _mask; }
            Open();
        }

        // Development player only: the engine's and the scripts' own markers, the
        // sixty largest every five minutes (normal frames only), in markers.txt.
        readonly Dictionary<string, ProfilerRecorder> _marks = new();
        readonly Dictionary<string, double> _markSum = new();
        int _markN;
        float _markScan, _markDump;

        void Markers()
        {
            float now = Time.unscaledTime;
            if (now >= _markScan)
            {
                _markScan = now + 60f;
                if (_markDump == 0f) _markDump = now + 150f;
                var handles = new List<ProfilerRecorderHandle>();
                ProfilerRecorderHandle.GetAvailable(handles);
                foreach (var h in handles)
                {
                    var d = ProfilerRecorderHandle.GetDescription(h);
                    if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds || _marks.ContainsKey(d.Name) || !Wanted(d.Name)) continue;
                    var rec = new ProfilerRecorder(h, 1, ProfilerRecorderOptions.SumAllSamplesInFrame | ProfilerRecorderOptions.WrapAroundWhenCapacityReached);
                    rec.Start();
                    _marks[d.Name] = rec;
                }
            }
            if (_blank) return;
            foreach (var m in _marks) { long v = m.Value.LastValue; if (v > 0) { _markSum.TryGetValue(m.Key, out double o); _markSum[m.Key] = o + v * 1e-6; } }
            _markN++;
            if (now < _markDump) return;
            _markDump = now + 150f;
            Census();
            File.AppendAllText(Path.Combine(_dir, "markers.txt"), $"== sim {SimClock.Now:0} frames {_markN} markers {_marks.Count}\n" + string.Join("\n", _markSum.OrderByDescending(x => x.Value).Take(140).Select(x => FormattableString.Invariant($"{x.Value / _markN,8:0.00}  {x.Key}"))) + "\n");
            _markSum.Clear(); _markN = 0;
        }

        // Script callbacks, the player-loop steps, UI, and the engine steps measured so far.
        static bool Wanted(string n) => n.EndsWith("Update()") || n.EndsWith("OnGUI()") || n.StartsWith("Default World ") || n.EndsWith("[Invoke]") || n.Contains("Coroutine") || n.StartsWith("PreLateUpdate.") || n.StartsWith("PostLateUpdate.") || n.StartsWith("Update.")
            || n.StartsWith("Canvas.") || n.StartsWith("UGUI.") || n.StartsWith("UIR.") || n.StartsWith("UIElements") || n.StartsWith("Animator") || n.StartsWith("MeshSkinning") || n.StartsWith("RenderLoop")
            || n.StartsWith("Shadows.") || n.StartsWith("Physics.") || n.StartsWith("ParticleSystem.") || n == "Render" || n == "CullScriptable" || n == "GUI.Repaint" || n.StartsWith("GC.") || n.Contains("Unity.Entities")
            || n.StartsWith("Gfx.") || n.StartsWith("Terrain") || n.StartsWith("Director.") || n.StartsWith("UpdateRendererBoundingVolumes") || n.StartsWith("TransformChange");

        // What is live, by kind: the scripts that run every frame, the particle
        // systems playing, the shadow casters, the animators and how they cull.
        void Census()
        {
            var sb = new System.Text.StringBuilder($"== census at sim {SimClock.Now:0}\n");
            foreach (var g in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled).GroupBy(b => b.GetType().Name).OrderByDescending(g => g.Count()).Take(45))
                sb.Append($"  {g.Count(),6}  {g.Key}\n");
            var ps = FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None).Where(p => p.isPlaying).ToList();
            sb.Append($"  particle systems playing {ps.Count}, particles alive {ps.Sum(p => p.particleCount)}\n");
            foreach (var g in ps.GroupBy(p => p.transform.root.name.Split('(')[0].Trim() + " / " + p.name).OrderByDescending(g => g.Sum(p => p.particleCount)).Take(15))
                sb.Append($"    {g.Count(),5} x {g.Key}: {g.Sum(p => p.particleCount)} particles\n");
            var rs = FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToList();
            sb.Append($"  renderers on {rs.Count}: skinned {rs.Count(r => r is SkinnedMeshRenderer)}, mesh {rs.Count(r => r is MeshRenderer)}, particle {rs.Count(r => r is ParticleSystemRenderer)}, other {rs.Count(r => !(r is SkinnedMeshRenderer || r is MeshRenderer || r is ParticleSystemRenderer))}; casting shadows {rs.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)}, visible now {rs.Count(r => r.isVisible)}\n");
            var an = FindObjectsByType<Animator>(FindObjectsSortMode.None).Where(a => a.isActiveAndEnabled).ToList();
            sb.Append($"  animators on {an.Count}: " + string.Join(", ", an.GroupBy(a => a.cullingMode).Select(g => $"{g.Key} {g.Count()}")) + $"; visible {an.Count(a => a.GetComponentInChildren<Renderer>() is Renderer r && r.isVisible)}\n");
            sb.Append($"  lights on {FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled)}, canvases {FindObjectsByType<Canvas>(FindObjectsSortMode.None).Count(c => c.isActiveAndEnabled)}, game objects {FindObjectsByType<Transform>(FindObjectsSortMode.None).Length}\n");
            sb.Append(Owners());
            File.AppendAllText(Path.Combine(_dir, "census.txt"), sb.ToString());
        }

        IEnumerator AblateLater() { yield return new WaitForSecondsRealtime(20f); Census(); yield return Ablate(); }

        int _near;

        void Follow(EntityManager em, Camera cam)
        {
            using var at = QC_Units.Get(em, QT_Units).ToComponentDataArray<LocalTransform>(Allocator.Temp);
            var cells = new Dictionary<(int, int), (int n, Vector3 sum)>();
            foreach (var t in at)
            {
                var k = (Mathf.FloorToInt(t.Position.x / 30f), Mathf.FloorToInt(t.Position.z / 30f));
                cells.TryGetValue(k, out var c);
                cells[k] = (c.n + 1, c.sum + (Vector3)t.Position);
            }
            if (cells.Count == 0) return;
            var best = cells.Values.OrderByDescending(c => c.n).First();
            _near = best.n;
            var p = best.sum / best.n;
            cam.transform.position = p + new Vector3(0f, 38f, -30f);
            cam.transform.LookAt(p);
        }
    }
}
