// BattlePerfProbe.cs
// What 500 units cost to draw (2026-11-25). In play mode, with a match running:
//   Waning Border > Debug > Battle Perf: Run
// walks a list of scenes - the empty match, 500 views standing / striking, with
// parts switched off, and a real 250 against 250 that cannot die - and writes the
// average of each to Temp/battle-perf.txt. Old prefabs for the comparison are
// read from Assets/TempOldUnits when that folder exists.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Rendering;
using Object = UnityEngine.Object;

namespace TheWaningBorder.EditorTools
{
    public static class BattlePerfProbe
    {
        const float Settle = 3f, Measure = 6f;
        const string TT = "Assets/GameData/TechTree/";
        static readonly (string path, int count, bool horse)[] Mix =
        {
            (TT + "Age0/Buildings/Barracks/Units/Spearman/Spearman.prefab", 120, false),
            (TT + "Civs/Alanthor/Buildings/Garrison/Units/Swordsman/Swordsman.prefab", 100, false),
            (TT + "Civs/Alanthor/Buildings/ArcheryRange/Units/Archer/Archer.prefab", 100, false),
            (TT + "Civs/Alanthor/Buildings/ArcheryRange/Units/Crossbowman/Crossbowman.prefab", 40, false),
            (TT + "Civs/Alanthor/Buildings/ArcheryRange/Units/Longbowman/Longbowman.prefab", 30, false),
            (TT + "Civs/Alanthor/Buildings/RoyalStable/Units/Outrider/Outrider.prefab", 40, true),
            (TT + "Civs/Alanthor/Buildings/RoyalStable/Units/Cataphract/Cataphract.prefab", 30, true),
            (TT + "Sects/Fortitude/Units/Sallyguard/Sallyguard.prefab", 20, false),
            (TT + "Sects/Renewal/Units/ScarGuard/ScarGuard.prefab", 20, false),
        };
        static readonly (string id, int count)[] Army =
        {
            ("Spearman", 60), ("Alanthor_Swordsman", 50), ("Alanthor_Archer", 50), ("Alanthor_Crossbowman", 20), ("Alanthor_Longbowman", 15),
            ("Alanthor_Outrider", 20), ("Alanthor_Cataphract", 15), ("Sect_Sallyguard", 10), ("Sect_ScarGuard", 10),
        };

        static readonly List<(string name, Action begin)> _steps = new();
        static int _step = -1;
        static double _t0;
        static StringBuilder _out;
        static GameObject _stage;
        static Vector3 _at, _fwd;
        static readonly List<(string name, ProfilerRecorder rec, double scale)> _rec = new();
        static readonly Dictionary<string, double> _sum = new();
        static readonly List<double> _frames = new();
        static int _n;
        static readonly FrameTiming[] _ft = new FrameTiming[1];
        static double _gpu, _main, _render; static int _ftn;

        [MenuItem("Waning Border/Debug/Battle Perf: Run")]
        public static void Run()
        {
            if (!Application.isPlaying) return;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            AlanthorArtProbe.CameraBlue();
            foreach (var o in Object.FindObjectsByType<TheWaningBorder.Core.Diagnostics.DebugLogOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None)) o.enabled = false;
            _out = new StringBuilder();
            _out.AppendLine($"screen {Screen.width}x{Screen.height}  quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}  pipeline {QualitySettings.renderPipeline?.name ?? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline.name}  gpu {SystemInfo.graphicsDeviceName}  api {SystemInfo.graphicsDeviceType}");
            _steps.Clear();
            _steps.Add(("simulation frozen, no added units", () => { Time.timeScale = 0f; Place(); }));
            _steps.Add(("NEW 500 views, idle", () => Views(Mix, false)));
            _steps.Add(("NEW 500 views, all striking", () => Strike(true)));
            _steps.Add(("  ... unit shadows off", () => Shadows(false)));
            _steps.Add(("  ... animators off", () => { Shadows(true); Anims(false); }));
            _steps.Add(("  ... nothing drawn (animators on)", () => { Anims(true); Draw(false); }));
            _steps.Add(("  ... bodies only: one renderer per unit", () => { Draw(true); Bodies(); }));
            _steps.Add(("no added units, again", Clear));
            _steps.Add(("NEW 250 views, all striking", () => { Views(Mix, true); Half(); }));
            if (Directory.Exists("Assets/TempOldUnits")) _steps.Add(("OLD 0.0.96 infantry, 420 views, all striking", () => Views(Old(), true)));
            _steps.Add(("NEW the same 420 infantry, all striking", () => Views(NewSame(), true)));
            _steps.Add(("no added units, third", Clear));
            _steps.Add(("simulation running, no added units", () => Time.timeScale = 1f));
            _steps.Add(("real battle 250 v 250, all on screen", Battle));
            _steps.Add(("real battle, 10 s later", () => { }));
            _steps.Add(("real battle, close camera", Close));
            _steps.Add(("real battle, unit views hidden (simulation + UI)", () => { CameraAt(30f, 38f); HideViews(true); }));
            Recorders();
            _step = -1;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Next();
        }

        static void Recorders()
        {
            foreach (var r in _rec) r.rec.Dispose();
            _rec.Clear();
            void R(string n, ProfilerCategory c, string s, double k) => _rec.Add((n, ProfilerRecorder.StartNew(c, s, 1), k));
            R("draws", ProfilerCategory.Render, "Draw Calls Count", 1);
            R("batches", ProfilerCategory.Render, "Batches Count", 1);
            R("setpass", ProfilerCategory.Render, "SetPass Calls Count", 1);
            R("tris(k)", ProfilerCategory.Render, "Triangles Count", 1e-3);
            R("shadowcasters", ProfilerCategory.Render, "Shadow Casters Count", 1);
            R("skinned", ProfilerCategory.Render, "Visible Skinned Meshes Count", 1);
            R("animators ms", ProfilerCategory.Animation, "Animators.Update", 1e-6);
            R("skinning ms", ProfilerCategory.Render, "MeshSkinning.Update", 1e-6);
            R("culling ms", ProfilerCategory.Render, "CullScriptable", 1e-6);
            R("scripts LateUpdate ms", ProfilerCategory.Internal, "PreLateUpdate.ScriptRunBehaviourLateUpdate", 1e-6);
            R("scripts Update ms", ProfilerCategory.Internal, "Update.ScriptRunBehaviourUpdate", 1e-6);
        }

        static void Next()
        {
            _step++;
            if (_step >= _steps.Count)
            {
                EditorApplication.update -= Tick;
                Clear();
                File.WriteAllText("Temp/battle-perf.txt", _out + "DONE\n");
                return;
            }
            try { _steps[_step].begin(); } catch (Exception e) { _out.AppendLine(_steps[_step].name + " FAILED " + e.Message); }
            _t0 = EditorApplication.timeSinceStartup;
            _sum.Clear(); _frames.Clear(); _n = 0; _gpu = _main = _render = 0; _ftn = 0; _lastFrame = -1;
        }

        static int _lastFrame;

        static void Tick()
        {
            if (!Application.isPlaying) { EditorApplication.update -= Tick; return; }
            if (_steps.Count == 0 || Time.frameCount == _lastFrame) return;
            _lastFrame = Time.frameCount;
            double t = EditorApplication.timeSinceStartup - _t0;
            FrameTimingManager.CaptureFrameTimings();
            if (t < Settle) return;
            _frames.Add(Time.unscaledDeltaTime * 1000.0);
            foreach (var (name, rec, k) in _rec) { _sum.TryGetValue(name, out double s); _sum[name] = s + (rec.Valid ? rec.LastValue * k : 0); }
            if (FrameTimingManager.GetLatestTimings(1, _ft) > 0) { _gpu += _ft[0].gpuFrameTime; _main += _ft[0].cpuMainThreadFrameTime; _render += _ft[0].cpuRenderThreadFrameTime; _ftn++; }
            _n++;
            if (t < Settle + Measure) return;
            _frames.Sort();
            double avg = _frames.Average(), p95 = _frames[(int)(_frames.Count * 0.95)];
            var sb = new StringBuilder($"{_steps[_step].name}\n    frame {avg:0.0} ms ({1000 / avg:0} fps) p95 {p95:0.0}  main {_main / Math.Max(1, _ftn):0.0}  render {_render / Math.Max(1, _ftn):0.0}  gpu {_gpu / Math.Max(1, _ftn):0.0}  frames {_n}\n    ");
            foreach (var (name, _, _) in _rec) sb.Append($"{name} {_sum[name] / _n:0.##}  ");
            _out.AppendLine(sb.ToString());
            File.WriteAllText("Temp/battle-perf.txt", _out.ToString());
            if (_step == 2 || _steps[_step].name.StartsWith("real battle, 10")) ScreenCapture.CaptureScreenshot("Temp/battle-perf-" + _step + ".png");
            Next();
        }

        // ── Scenes ─────────────────────────────────────────────────────

        static void Place()
        {
            var cam = Camera.main.transform;
            _fwd = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            _at = cam.position + cam.forward * (cam.position.y / Mathf.Max(0.2f, -cam.forward.y)) - _fwd * 45f;
            CameraAt(30f, 38f);
        }

        static void CameraAt(float back, float up)
        {
            foreach (var c in Object.FindObjectsByType<TheWaningBorder.CameraRig.CameraController>(FindObjectsSortMode.None)) c.enabled = false;
            var cam = Camera.main.transform;
            float y = Ground(_at);
            cam.position = new Vector3(_at.x, y, _at.z) - _fwd * back + Vector3.up * up;
            cam.LookAt(new Vector3(_at.x, y, _at.z));
        }

        static void Close() => CameraAt(13f, 15f);

        static float Ground(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(p) + t.transform.position.y : p.y;
        }

        static void Clear() { if (_stage != null) Object.DestroyImmediate(_stage); }

        static void Views((string path, int count, bool horse)[] mix, bool strike)
        {
            Clear();
            _stage = new GameObject("PerfViews");
            var right = Vector3.Cross(Vector3.up, _fwd);
            int total = mix.Sum(m => m.count), cols = 25, i = 0;
            var colours = new[] { FactionColors.Get(Faction.Blue), FactionColors.Get(Faction.Red) };
            foreach (var (path, count, horse) in mix)
            {
                var a = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (a == null) { _out.AppendLine("missing " + path); continue; }
                for (int k = 0; k < count; k++, i++)
                {
                    Vector3 p = _at + right * ((i % cols - cols * 0.5f) * 1.5f) + _fwd * ((i / cols - total / cols * 0.5f) * 1.5f);
                    p.y = Ground(p);
                    var go = Object.Instantiate(a, p, Quaternion.LookRotation(i % 2 == 0 ? _fwd : -_fwd), _stage.transform);
                    var kit = go.GetComponent<UnitAgeKit>();
                    if (kit != null) kit.Apply(true, 3, 3, 0, instant: true, quiver: 3);
                    var pc = go.GetComponent<UnitPlayerColor>();
                    if (pc != null) pc.Apply(colours[i % 2]);
                    UnitAnimationSync.ConfigureView(go, go.GetComponentInChildren<Animator>());
                    // As the spawn system leaves them; unscaled, so they play over a frozen match.
                    foreach (var an in go.GetComponentsInChildren<Animator>(true)) { an.fireEvents = false; an.updateMode = AnimatorUpdateMode.UnscaledTime; }
                }
            }
            Strike(strike);
        }

        static void Strike(bool on)
        {
            foreach (var an in _stage.GetComponentsInChildren<Animator>())
                foreach (var p in an.parameters)
                {
                    if (p.name == "IsAttacking") an.SetBool("IsAttacking", on);
                    if (p.name == "AttackSpeed") an.SetFloat("AttackSpeed", 1f);
                }
        }

        static void Shadows(bool on)
        {
            foreach (var r in _stage.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        static void Anims(bool on) { foreach (var a in _stage.GetComponentsInChildren<Animator>(true)) a.enabled = on; }
        static void Draw(bool on) { foreach (var r in _stage.GetComponentsInChildren<Renderer>(true)) r.enabled = on; }
        static void Bodies()
        {
            foreach (Transform u in _stage.transform)
            {
                var keep = u.GetComponentsInChildren<SkinnedMeshRenderer>().OrderByDescending(x => x.sharedMesh.vertexCount).FirstOrDefault();
                foreach (var x in u.GetComponentsInChildren<Renderer>()) x.enabled = x == keep;
            }
        }
        static void Half() { for (int i = _stage.transform.childCount - 1; i >= 0; i -= 2) Object.DestroyImmediate(_stage.transform.GetChild(i).gameObject); }

        static (string, int, bool)[] Old() => new[] { ("Assets/TempOldUnits/Spearman.prefab", 160, false), ("Assets/TempOldUnits/Swordsman.prefab", 140, false), ("Assets/TempOldUnits/Archer.prefab", 120, false) };
        static (string, int, bool)[] NewSame() => new[] { (Mix[0].path, 160, false), (Mix[1].path, 140, false), (Mix[2].path, 120, false) };

        // ── The battle ─────────────────────────────────────────────────

        static readonly List<Entity> _units = new();

        static void Battle()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var enemy = Enum.GetValues(typeof(Faction)).Cast<Faction>().First(f => Alliances.AreHostile(Faction.Blue, f) && f != Faction.Blue);
            foreach (var f in new[] { Faction.Blue, enemy })
                foreach (var id in new[] { "Barracks", "ArcheryRange", "Alanthor_RoyalStable" })
                    try { TheWaningBorder.Systems.Buildings.FactionBuildingLevels.ApplyToAll(em, f, id, 3); } catch { }
            var right = Vector3.Cross(Vector3.up, _fwd);
            _units.Clear();
            foreach (var side in new[] { -1, 1 })
            {
                int i = 0;
                foreach (var (id, count) in Army)
                    for (int k = 0; k < count; k++, i++)
                    {
                        Vector3 p = _at + right * ((i % 25 - 12.5f) * 1.3f) + _fwd * (side * (4f + i / 25 * 1.3f));
                        p.y = Ground(p);
                        var e = TheWaningBorder.Entities.UnitFactory.Create(em, id, (float3)p, side < 0 ? Faction.Blue : enemy);
                        if (e == Entity.Null) continue;
                        // Nobody dies: the count stays 500 while it is measured.
                        if (em.HasComponent<Health>(e)) { var h = em.GetComponentData<Health>(e); h.Max = 1000000; h.Value = 1000000; em.SetComponentData(e, h); }
                        _units.Add(e);
                    }
            }
            _out.AppendLine($"spawned {_units.Count} real units, Blue against {enemy}");
            CameraAt(30f, 38f);
        }

        static void HideViews(bool hide)
        {
            foreach (var e in _units)
                if (EntityViewManager.Instance.TryGetView(e, out var go) && go != null)
                {
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = !hide;
                    foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = !hide;
                }
        }
    }
}
