// HeadlessBattlePerf.cs
// What a 500-unit battle costs in a BUILD, and the Militia's age-up on the way
// there (2026-11-25). A harness, not a game mode. Run WITH graphics:
//
//   "The Waning Border.exe" -twbBattlePerf <folder> -twbNoLauncher \
//       -screen-width 2558 -screen-height 1232 -screen-fullscreen 0
//
// Starts a two-player match in Age 0, photographs twelve Militia before, during
// and after they become Spearmen, then measures 250 against 250 that cannot die.
// Writes battle.txt and the pictures, and quits (0 = done, 3 = never started).
// The Editor's version, with the parts switched off one by one, is BattlePerfProbe.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using TheWaningBorder.CameraRig;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Rendering;
using TheWaningBorder.Systems.Buildings;

namespace TheWaningBorder.Bootstrap
{
    internal sealed class HeadlessBattlePerf : MonoBehaviour
    {
        static readonly (string id, int count)[] Army =
        {
            ("Spearman", 60), ("Alanthor_Swordsman", 50), ("Alanthor_Archer", 50), ("Alanthor_Crossbowman", 20), ("Alanthor_Longbowman", 15),
            ("Alanthor_Outrider", 20), ("Alanthor_Cataphract", 15), ("Sect_Sallyguard", 10), ("Sect_ScarGuard", 10),
        };

        string _dir;
        readonly StringBuilder _out = new();
        readonly List<Entity> _units = new();
        Vector3 _at;
        int _errors;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            var args = Environment.GetCommandLineArgs();
            string dir = CommandLine.Str(args, "-twbBattlePerf");
            if (string.IsNullOrEmpty(dir)) return;
            var go = new GameObject("HeadlessBattlePerf");
            DontDestroyOnLoad(go);
            go.AddComponent<HeadlessBattlePerf>().Begin(dir, CommandLine.Str(args, "-twbMap"));
        }

        void Begin(string dir, string map)
        {
            _dir = Path.GetFullPath(dir);
            Directory.CreateDirectory(_dir);
            Application.logMessageReceived += (msg, trace, type) => { if (type == LogType.Exception || type == LogType.Error) { if (_errors++ < 10) _out.AppendLine(type + ": " + msg); } };
            HeadlessSkirmish.SetupAllAi(2, 20261125);
            LobbyConfig.SetupSinglePlayer(2);      // slot 0 stays the human: no AI builds there
            LobbyConfig.ApplyColorSelections();
            GameSettings.IsObserver = false;
            GameSettings.StartAge = SkirmishStartAge.Age0;
            GameSettings.StartCulture = Cultures.Alanthor;
            GameSettings.SelectedMapScene = string.IsNullOrEmpty(map) ? "YielLymwerra" : map;
            TheWaningBorder.Core.Replay.ReplayRecorder.Suppressed = true;
            SceneManager.LoadScene(GameSettings.SelectedMapScene);
            StartCoroutine(Run());
        }

        static readonly ComponentType[] QT_Capitals =
        {
            ComponentType.ReadOnly<FortressTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>(),
        };

        static bool TryCapital(EntityManager em, out float3 at)
        {
            at = default;
            using var q = em.CreateEntityQuery(QT_Capitals);   // one-off harness, disposed
            using var ents = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
                if (em.GetComponentData<FactionTag>(e).Value == GameSettings.LocalPlayerFaction) { at = em.GetComponentData<LocalTransform>(e).Position; return true; }
            return false;
        }

        IEnumerator Run()
        {
            float deadline = Time.realtimeSinceStartup + 120f;
            float3 home = default;
            while (true)
            {
                var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (w != null && w.IsCreated && TryCapital(w.EntityManager, out home)) break;
                if (Time.realtimeSinceStartup > deadline) { Application.Quit(3); yield break; }
                yield return null;
            }
            yield return new WaitForSecondsRealtime(8f);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var me = GameSettings.LocalPlayerFaction;
            _out.AppendLine($"{Application.version} map {GameSettings.SelectedMapScene} screen {Screen.width}x{Screen.height} quality {QualitySettings.names[QualitySettings.GetQualityLevel()]} gpu {SystemInfo.graphicsDeviceName}");
            foreach (var c in FindObjectsByType<CameraController>(FindObjectsSortMode.None)) c.enabled = false;

            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-twbCavalryCheck") >= 0) { yield return Cavalry(em, me, home); yield break; }
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-twbPerfSweep") >= 0) { yield return Sweeps(em, me, home); yield break; }

            // ── Age-up ────────────────────────────────────────────────
            _at = new Vector3(home.x, home.y, home.z - 16f);
            for (int i = 0; i < 12; i++) Spawn(em, "Spearman", _at + new Vector3((i % 6 - 2.5f) * 1.5f, 0f, i / 6 * 1.6f), me);
            CameraAt(6f, 5f);
            yield return new WaitForSecondsRealtime(3f);
            yield return Shot("ageup_0_before.png");
            Kits(em, "before");
            FactionColors.SetFactionCulture(me, Cultures.Alanthor);
            yield return new WaitForSecondsRealtime(1.3f);
            yield return Shot("ageup_1_during.png");
            yield return new WaitForSecondsRealtime(4f);
            yield return Shot("ageup_2_after.png");
            Kits(em, "after");
            FactionBuildingLevels.ApplyToAll(em, me, "Barracks", 3);
            yield return new WaitForSecondsRealtime(5f);
            yield return Shot("ageup_3_level3.png");
            Kits(em, "level 3");
            foreach (var e in _units) if (em.Exists(e)) em.DestroyEntity(e);
            _units.Clear();

            // ── The battle ────────────────────────────────────────────
            _at = new Vector3(home.x, home.y, home.z) - Vector3.forward * 45f * math.sign(home.z == 0f ? 1f : home.z);
            CameraAt(30f, 38f);
            yield return Measure("no added units");
            SpawnArmies(em, me);
            yield return Measure("battle 250 v 250, all on screen");
            yield return Shot("battle.png");
            yield return Measure("battle, 10 s later");
            CameraAt(13f, 15f);
            yield return Measure("battle, close camera");
            yield return Shot("battle_close.png");
            CameraAt(30f, 38f);
            foreach (var e in _units)
                if (EntityViewManager.Instance.TryGetView(e, out var go) && go != null)
                {
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
                    foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                }
            yield return Measure("battle, unit views hidden (simulation + UI)");
            int alive = _units.Count(e => em.Exists(e));
            _out.AppendLine($"alive {alive}  errors {_errors}");
            File.WriteAllText(Path.Combine(_dir, "battle.txt"), _out + "DONE\n");
            Application.Quit(0);
        }

        void SpawnArmies(EntityManager em, Faction me)
        {
            var enemy = Enum.GetValues(typeof(Faction)).Cast<Faction>().First(f => f != me && Alliances.AreHostile(me, f));
            FactionColors.SetFactionCulture(enemy, Cultures.Alanthor);
            foreach (var f in new[] { me, enemy })
                foreach (var id in new[] { "Barracks", "ArcheryRange", "Alanthor_RoyalStable" })
                    FactionBuildingLevels.ApplyToAll(em, f, id, 3);
            foreach (int side in new[] { -1, 1 })
            {
                int i = 0;
                foreach (var (id, count) in Army)
                    for (int k = 0; k < count; k++, i++)
                        Spawn(em, id, _at + new Vector3((i % 25 - 12.5f) * 1.3f, 0f, side * (4f + i / 25 * 1.3f)), side < 0 ? me : enemy);
            }
            _out.AppendLine($"spawned {_units.Count}, {me} against {enemy}");
        }

        // ── The sweep: the empty map, then the battle, with one part off at a time ──

        IEnumerator Sweeps(EntityManager em, Faction me, float3 home)
        {
            _at = new Vector3(home.x, home.y, home.z) - Vector3.forward * 45f * math.sign(home.z == 0f ? 1f : home.z);
            FactionColors.SetFactionCulture(me, Cultures.Alanthor);
            CameraAt(30f, 38f);
            Debug.LogError("[BattlePerf] a test error during the match: the log console must stay shut");
            yield return null; yield return null;
            var overlay = FindObjectsByType<TheWaningBorder.Core.Diagnostics.DebugLogOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            var vis = typeof(TheWaningBorder.Core.Diagnostics.DebugLogOverlay).GetField("_visible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            _out.AppendLine("log console after an in-match error: " + (overlay == null ? "no overlay" : ((bool)vis.GetValue(overlay) ? "OPEN (wrong)" : "shut (right)")));
            _errors--;
            yield return Sweep("EMPTY");
            SpawnArmies(em, me);
            yield return new WaitForSecondsRealtime(8f);
            yield return Sweep("BATTLE");
            _out.AppendLine($"errors {_errors}");
            File.WriteAllText(Path.Combine(_dir, "battle.txt"), _out + "DONE\n");
            Application.Quit(0);
        }

        IEnumerator Sweep(string what)
        {
            _out.AppendLine("== " + what);
            var rp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            var cam = Camera.main.GetUniversalAdditionalCameraData();
            var terrain = Terrain.activeTerrain;
            int msaa = rp.msaaSampleCount; float scale = rp.renderScale;
            yield return Measure("all on", true);
            yield return Off<Canvas>("HUD off", x => x.enabled = false, x => x.enabled = true);
            Time.timeScale = 0f; yield return Measure("time frozen", true); Time.timeScale = 1f;
            terrain.drawTreesAndFoliage = false; yield return Measure("trees and grass off", true); terrain.drawTreesAndFoliage = true;
            terrain.drawHeightmap = false; yield return Measure("ground off", true); terrain.drawHeightmap = true;
            float px = terrain.heightmapPixelError; terrain.heightmapPixelError = 30f; yield return Measure("ground coarser", true); terrain.heightmapPixelError = px;
            yield return Ground(terrain);
            rp.msaaSampleCount = 1; yield return Measure("MSAA off", true);
            rp.msaaSampleCount = 2; yield return Measure("MSAA 2x", true); rp.msaaSampleCount = msaa;
            rp.renderScale = 0.7f; yield return Measure("70 % resolution", true); rp.renderScale = scale;
            yield return Off<Light>("shadows off", x => x.shadows = LightShadows.None, x => x.shadows = LightShadows.Soft, x => x.shadows != LightShadows.None);
            yield return Off<Light>("point lights off", x => x.enabled = false, x => x.enabled = true, x => x.type != LightType.Directional);
            cam.renderPostProcessing = false; yield return Measure("post processing off", true); cam.renderPostProcessing = true;
            foreach (var feature in Resources.FindObjectsOfTypeAll<ScriptableRendererFeature>().Where(x => x.isActive).ToArray())
            {
                feature.SetActive(false); yield return Measure("feature " + feature.name + " off", true); feature.SetActive(true);
            }
            yield return Off<ParticleSystemRenderer>("particles off", x => x.enabled = false, x => x.enabled = true);
            yield return Off<LineRenderer>("lines off", x => x.enabled = false, x => x.enabled = true);
            yield return Off<SkinnedMeshRenderer>("skinned meshes off", x => x.enabled = false, x => x.enabled = true);
            yield return Off<MeshRenderer>("plain meshes off", x => x.enabled = false, x => x.enabled = true);
            yield return Off<MeshRenderer>("see-through meshes off", x => x.enabled = false, x => x.enabled = true, x => x.sharedMaterial != null && x.sharedMaterial.renderQueue >= 2900);
            yield return Off<Animator>("animators off", x => x.enabled = false, x => x.enabled = true);
            if (what == "BATTLE")
            {
                yield return Off<Renderer>("unit shadows off", x => x.shadowCastingMode = ShadowCastingMode.Off, x => x.shadowCastingMode = ShadowCastingMode.On, x => x.shadowCastingMode == ShadowCastingMode.On && x.GetComponentInParent<UnitAgeKit>() != null);
                yield return Off<Renderer>("unit views not drawn", x => x.enabled = false, x => x.enabled = true, x => x.GetComponentInParent<UnitAgeKit>() != null);
            }
            rp.msaaSampleCount = 1; rp.renderScale = 0.5f; yield return Measure("MSAA off and half resolution", true); rp.msaaSampleCount = msaa; rp.renderScale = scale;
            yield return Measure("all on, again", true);
        }

        // The ground shader's parts, one at a time (globals TerrainLook and the overlay owners set).
        IEnumerator Ground(Terrain terrain)
        {
            var look = terrain.GetComponent<TheWaningBorder.World.Terrain.TerrainLook>();
            _out.AppendLine($"  ground: layers {terrain.terrainData.terrainLayers.Length}, look {(look != null ? $"antiTile {look.antiTile} macro {look.macroVariation} cliffs {look.cliffProjection}" : "none")}, basemap distance {terrain.basemapDistance}, pixel error {terrain.heightmapPixelError}");
            foreach (var (name, id) in new[] { ("anti-tiling off", "_TWBAntiTile"), ("colour drift off", "_TWBMacroVariation"), ("cliff projection off", "_TWBCliffProjection"), ("overlays off", "_TWB_OverlaysEnabled"), ("roads off", "_TWB_RoadEnabled") })
            {
                float was = Shader.GetGlobalFloat(id);
                Shader.SetGlobalFloat(id, 0f);
                yield return Measure("ground: " + name + " (was " + was + ")", true);
                Shader.SetGlobalFloat(id, was);
            }
            float[] keep = { Shader.GetGlobalFloat("_TWBAntiTile"), Shader.GetGlobalFloat("_TWBMacroVariation"), Shader.GetGlobalFloat("_TWBCliffProjection"), Shader.GetGlobalFloat("_TWB_OverlaysEnabled"), Shader.GetGlobalFloat("_TWB_RoadEnabled") };
            foreach (var id in new[] { "_TWBAntiTile", "_TWBMacroVariation", "_TWBCliffProjection", "_TWB_OverlaysEnabled", "_TWB_RoadEnabled" }) Shader.SetGlobalFloat(id, 0f);
            yield return Measure("ground: all five off", true);
            string[] ids = { "_TWBAntiTile", "_TWBMacroVariation", "_TWBCliffProjection", "_TWB_OverlaysEnabled", "_TWB_RoadEnabled" };
            for (int i = 0; i < ids.Length; i++) Shader.SetGlobalFloat(ids[i], keep[i]);
            float bd = terrain.basemapDistance;
            foreach (float d in new[] { 60f, 30f }) { terrain.basemapDistance = d; yield return Measure($"ground: baked far texture from {d} m (was {bd})", true); }
            terrain.basemapDistance = bd;
            var sun = FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional && l.shadows != LightShadows.None);
            if (sun != null) { var sh = sun.shadows; sun.shadows = LightShadows.Hard; yield return Measure("hard shadows instead of soft", true); sun.shadows = sh; }
            var rp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            float dist = rp.shadowDistance;
            rp.shadowDistance = 70f; yield return Measure($"shadow distance 70 m (was {dist})", true); rp.shadowDistance = dist;
            int cas = rp.shadowCascadeCount;
            rp.shadowCascadeCount = 1; yield return Measure($"one shadow cascade (was {cas})", true); rp.shadowCascadeCount = cas;
        }

        IEnumerator Off<T>(string name, Action<T> off, Action<T> on, Func<T, bool> which = null) where T : Component
        {
            var set = FindObjectsByType<T>(FindObjectsSortMode.None).Where(x => ((x as Behaviour)?.enabled ?? (x as Renderer)?.enabled ?? true) && (which == null || which(x))).ToArray();
            foreach (var x in set) off(x);
            yield return Measure(name + " (" + set.Length + ")", true);
            foreach (var x in set) if (x != null) on(x);
        }

        // Mounted units at a canter and a gallop, followed close: the horse must stay
        // on its unit, not creep ahead and snap back with each loop of the clip.
        IEnumerator Cavalry(EntityManager em, Faction me, float3 home)
        {
            FactionColors.SetFactionCulture(me, Cultures.Alanthor);
            var from = new Vector3(home.x + 14f, home.y, home.z - 24f);
            for (int i = 0; i < 3; i++) Spawn(em, "Alanthor_Cataphract", from + new Vector3(i * 2.5f, 0f, 0f), me);
            for (int i = 0; i < 2; i++) Spawn(em, "Alanthor_Outrider", from + new Vector3(i * 2.5f, 0f, -4f), me);
            yield return new WaitForSecondsRealtime(2f);
            foreach (var e in _units) TheWaningBorder.Core.Commands.CommandRouter.IssueMove(em, e, (float3)(from + new Vector3(-40f, 0f, 0f)));
            var shots = new List<(int frame, string file)>();
            for (int k = 0; k < 24; k++)
            {
                var lead = _units[0];
                if (em.Exists(lead))
                {
                    var p = (Vector3)em.GetComponentData<LocalTransform>(lead).Position;
                    _at = p + new Vector3(-0.5f, 0f, -2f);
                    var cam = Camera.main.transform;
                    cam.position = p + new Vector3(0f, 4.5f, -8f);
                    cam.LookAt(p + Vector3.up * 1.2f);
                }
                if (k >= 6 && k % 3 == 0) ScreenCapture.CaptureScreenshot(Path.Combine(_dir, $"cavalry_{k:00}.png"));
                yield return new WaitForSecondsRealtime(0.17f);
            }
            File.WriteAllText(Path.Combine(_dir, "cavalry.txt"), "ok " + _errors + " errors " + _out);
            Application.Quit(0);
        }

        void Spawn(EntityManager em, string id, Vector3 p, Faction f)
        {
            p.y = Ground(p);
            var e = UnitFactory.Create(em, id, (float3)p, f);
            if (e == Entity.Null) { _out.AppendLine("no unit " + id); return; }
            if (em.HasComponent<Health>(e)) { var h = em.GetComponentData<Health>(e); h.Max = h.Value = 1000000; em.SetComponentData(e, h); }
            _units.Add(e);
        }

        void Kits(EntityManager em, string when)
        {
            int views = 0, first = 0, aged = 0, anim = 0, bodies = 0;
            foreach (var e in _units)
            {
                if (!em.Exists(e) || !EntityViewManager.Instance.TryGetView(e, out var go) || go == null) continue;
                views++;
                var kit = go.GetComponent<UnitAgeKit>();
                if (kit.firstAge.Any(x => x != null && x.activeInHierarchy && !kit.aged.Contains(x))) first++;
                if (kit.aged.Any(x => x != null && x.activeInHierarchy && !kit.firstAge.Contains(x))) aged++;
                var a = go.GetComponentInChildren<Animator>();
                if (a != null && a.isActiveAndEnabled && a.GetCurrentAnimatorClipInfoCount(0) > 0) anim++;
                bodies += go.GetComponentsInChildren<SkinnedMeshRenderer>().Count(r => r.enabled);
            }
            _out.AppendLine($"kits {when}: {views} views, first-age shown on {first}, aged on {aged}, animating {anim}, skinned meshes on {bodies}");
        }

        static readonly FrameTiming[] _ft = new FrameTiming[1];
        readonly Dictionary<string, ProfilerRecorder> _rec = new();

        IEnumerator Measure(string name, bool quick = false)
        {
            yield return new WaitForSecondsRealtime(quick ? 1.5f : 4f);
            foreach (var n in new[] { "Draw Calls Count", "SetPass Calls Count", "Triangles Count", "Shadow Casters Count" })
                if (!_rec.ContainsKey(n)) _rec[n] = ProfilerRecorder.StartNew(ProfilerCategory.Render, n, 1);
            double cpu = 0, main = 0, wait = 0, render = 0, gpu = 0; int ft = 0;
            var f = new List<float>();
            float until = Time.realtimeSinceStartup + (quick ? 3.5f : 6f);
            while (Time.realtimeSinceStartup < until) { yield return null; f.Add(Time.unscaledDeltaTime * 1000f); FrameTimingManager.CaptureFrameTimings(); if (FrameTimingManager.GetLatestTimings(1, _ft) > 0) { var t = _ft[0]; main += t.cpuMainThreadFrameTime; wait += t.cpuMainThreadPresentWaitTime; render += t.cpuRenderThreadFrameTime; gpu += t.gpuFrameTime; ft++; } }
            f.Sort();
            float avg = f.Average();
            _out.AppendLine($"{name}: {avg:0.0} ms ({1000f / avg:0} fps)  median {f[f.Count / 2]:0.0}  p95 {f[(int)(f.Count * 0.95f)]:0.0} | main {main / math.max(1, ft):0.0} wait {wait / math.max(1, ft):0.0} render {render / math.max(1, ft):0.0} gpu {gpu / math.max(1, ft):0.0} | {string.Join(" ", _rec.Select(x => x.Key.Split(' ')[0] + " " + x.Value.LastValue))}");
            File.WriteAllText(Path.Combine(_dir, "battle.txt"), _out.ToString());
        }

        IEnumerator Shot(string file)
        {
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, file));
            yield return new WaitForSecondsRealtime(0.4f);
        }

        void CameraAt(float back, float up)
        {
            var cam = Camera.main.transform;
            var at = new Vector3(_at.x, Ground(_at), _at.z);
            cam.position = at - Vector3.forward * back + Vector3.up * up;
            cam.LookAt(at);
        }

        static float Ground(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(p) + t.transform.position.y : p.y;
        }
    }
}
