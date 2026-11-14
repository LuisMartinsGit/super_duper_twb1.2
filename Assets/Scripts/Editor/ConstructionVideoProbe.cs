// ConstructionVideoProbe.cs
// A building's CONSTRUCTION, in a real match, on video (2026-11-12) — made to
// look at the authored construction sequence of Garrison_2
// (BuildingConstructionSequence). From play mode:
//
//   Launch — Broken Oath, edition A, a HUMAN Blue seat already Alanthor in
//            Age 1, one Easy AI far away, fog off. Once the capital stands it
//            places a Barracks (the Alanthor Garrison) through the game's own
//            placement on the flattest free ground near the capital, orders
//            ONE Worker to build it (one worker = the SO's build time), hides
//            the UI, parks the camera on the site and records from the moment
//            ground is broken until three seconds after the building is done.
//
// Frames: JPG, 30 a second, in TWB_OUT when set, else Temp/construction-video
// (frames/, then done.txt). Encode outside.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TheWaningBorder.CameraRig;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.UI.Menus;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class ConstructionVideoProbe
    {
        static string MapScene = "BrokenOath";
        static bool _stillsOnly;
        static int _aiStart = 3;
        const string BuildingId = "Barracks";
        const int Fps = 30;
        const float Zoom = 58f;
        const float TailSeconds = 3f;
        const float MaxSeconds = 120f;
        static bool _diagOnly = false;

        static string OutDir => System.Environment.GetEnvironmentVariable("TWB_OUT") is { Length: > 0 } s ? s : "Temp/construction-video";

        static float _deadline;
        static int _settle;
        static float3 _site;

        /// <summary>Terrain stills only, on Yiel Lymwerra: no building, no video.</summary>
        [MenuItem("Waning Border/Debug/Terrain Stills: Launch (Yiel Lymwerra)")]
        public static void LaunchStillsYiel()
        {
            MapScene = "YielLymwerra"; _stillsOnly = true; _aiStart = 1;
            Launch();
        }

        [MenuItem("Waning Border/Debug/Construction Video: Launch + Record (Garrison, one Worker)")]
        public static void LaunchVideo()
        {
            MapScene = "BrokenOath"; _stillsOnly = false; _aiStart = 3;
            Launch();
        }

        static void Launch()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[ConstructionVideo] enter play mode first"); return; }
            GameEdition.SetEditorPick("A");
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = 2;
            LobbyConfig.SetupSinglePlayer(2);
            LobbyConfig.Slots[0].Type = SlotType.Human;
            LobbyConfig.Slots[0].TeamIndex = Alliances.NoTeam;
            LobbyConfig.Slots[0].StartIndex = 0;
            LobbyConfig.Slots[1].Type = SlotType.AI;
            LobbyConfig.Slots[1].AIDifficulty = LobbyAIDifficulty.Easy;
            LobbyConfig.Slots[1].TeamIndex = Alliances.NoTeam;
            LobbyConfig.Slots[1].StartIndex = _aiStart;
            GameSettings.SelectedMapScene = MapScene;
            GameSettings.SpawnSeed = 20261112;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = true;
            GameSettings.IsObserver = false;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            GameSettings.StartAge = SkirmishStartAge.Age2;
            GameSettings.PerFactionStartCulture = false;
            GameSettings.StartCulture = Cultures.Alanthor;
            LobbyConfig.ApplyColorSelections();
            Debug.Log("[ConstructionVideo] launching " + MapScene);
            LoadingScreen.Show(GameSettings.SelectedMapScene);
            _deadline = Time.realtimeSinceStartup + 240f; _settle = 0;
            EditorApplication.update -= Poll;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= Poll; return; }
            if (Time.realtimeSinceStartup > _deadline)
            {
                EditorApplication.update -= Poll;
                Debug.LogWarning("[ConstructionVideo] gave up: no capital, or no legal site (" + CommandRouter.LastPlacementRefusal + ")");
                Directory.CreateDirectory(OutDir);
                File.WriteAllText(Path.Combine(OutDir, "done.txt"), "FAILED: no site (" + CommandRouter.LastPlacementRefusal + ")");
                return;
            }
            if (!TryCapital(out _)) { _settle = 0; return; }
            // The ground is not Blue's for the first moments of a match: try
            // again every couple of seconds until a site is accepted.
            if (++_settle < 120) return;
            _settle = 0;
            if (_stillsOnly)
            {
                EditorApplication.update -= Poll;
                Directory.CreateDirectory(OutDir);
                ProportionVideoProbe.HideUi();
                var stillRig = Object.FindFirstObjectByType<CameraController>();
                if (stillRig != null) stillRig.StartCoroutine(Record(stillRig));
                return;
            }
            if (PlaceAndRecord()) EditorApplication.update -= Poll;
        }

        static bool PlaceAndRecord()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            TryCapital(out float3 cap);
            Directory.CreateDirectory(OutDir);
            var log = new System.Text.StringBuilder($"capital {cap}\n");

            // Candidate sites round the capital, flattest first.
            int2 cells = TheWaningBorder.Core.Commands.Types.BuildCommandHelper.GetBuildingSize(BuildingId);
            float half = math.max(cells.x, cells.y) * 1f;   // cells are 2 m: half the footprint in metres
            var candidates = new List<(float slope, float3 p)>();
            for (float r = 18f; r <= 46f; r += 4f)
                for (int a = 0; a < 16; a++)
                {
                    float ang = a * math.PI * 2f / 16f;
                    float3 p = cap + new float3(math.cos(ang), 0f, math.sin(ang)) * r;
                    p.x = math.round(p.x * 0.5f) * 2f; p.z = math.round(p.z * 0.5f) * 2f;
                    float lo = float.MaxValue, hi = float.MinValue;
                    for (int ix = -1; ix <= 1; ix++)
                        for (int iz = -1; iz <= 1; iz++)
                        {
                            float h = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x + ix * half, p.z + iz * half);
                            lo = math.min(lo, h); hi = math.max(hi, h);
                        }
                    p.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.z);
                    candidates.Add((hi - lo + r * 0.005f, p));
                }
            candidates.Sort((x, y) => x.slope.CompareTo(y.slope));

            Entity site = Entity.Null;
            foreach (var c in candidates)
            {
                site = CommandRouter.PlaceBuildingDirect(em, BuildingId, c.p, Faction.Blue, Entity.Null, 0f);
                if (site == Entity.Null) continue;
                _site = em.HasComponent<LocalTransform>(site) ? em.GetComponentData<LocalTransform>(site).Position : c.p;
                log.AppendLine($"site {_site} (height spread {c.slope:F2})");
                break;
            }
            if (site == Entity.Null)
            {
                return false;
            }

            // One Worker: the nearest.
            Entity worker = Entity.Null; float best = float.MaxValue;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<WorkerTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>());
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    if (em.GetComponentData<FactionTag>(e).Value != Faction.Blue) continue;
                    float d = math.distance(em.GetComponentData<LocalTransform>(e).Position, _site);
                    if (d < best) { best = d; worker = e; }
                }
            q.Dispose();
            if (worker == Entity.Null)
            {
                Debug.LogError("[ConstructionVideo] Blue has no Worker");
                File.WriteAllText(Path.Combine(OutDir, "done.txt"), "FAILED: no worker");
                return true;
            }
            CommandRouter.IssueBuild(em, worker, site, BuildingId, _site, CommandSource.AI, append: false);
            log.AppendLine($"worker {worker.Index} at {best:F0} m");
            File.WriteAllText(Path.Combine(OutDir, "setup.txt"), log.ToString());

            ProportionVideoProbe.HideUi();
            var rig = Object.FindFirstObjectByType<CameraController>();
            if (rig == null) { Debug.LogError("[ConstructionVideo] no camera rig"); return true; }
            rig.StartCoroutine(Record(rig));
            return true;
        }

        /// <summary>0 = not broken ground yet, 1 = under construction, 2 = built.</summary>
        static int SiteState()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            int state = 0;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<LocalTransform>());
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    if (math.distance(em.GetComponentData<LocalTransform>(e).Position.xz, _site.xz) > 3f) continue;
                    state = em.HasComponent<UnderConstruction>(e) ? 1 : 2;
                    break;
                }
            q.Dispose();
            return state;
        }

        /// <summary>What the site's view looks like right now, to diag.txt.</summary>
        static void Diagnose(int frame)
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var sb = new System.Text.StringBuilder($"frame {frame}: ");
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<FactionTag>());
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    var lt = em.GetComponentData<LocalTransform>(e);
                    if (math.distance(lt.Position.xz, _site.xz) > 1.5f) continue;
                    bool uc = em.HasComponent<UnderConstruction>(e);
                    float ratio = uc ? em.GetComponentData<UnderConstruction>(e).Progress / math.max(1f, em.GetComponentData<UnderConstruction>(e).Total) : 1f;
                    sb.Append($"entity {e.Index} pos {lt.Position} scale {lt.Scale} uc={uc} ratio={ratio:F2} planned={em.HasComponent<PlannedBuilding>(e)} building={em.HasComponent<BuildingTag>(e)} ");
                    if (TheWaningBorder.Rendering.EntityViewManager.Instance != null
                        && TheWaningBorder.Rendering.EntityViewManager.Instance.TryGetView(e, out var go) && go != null)
                    {
                        int on = 0, all = 0; Bounds b = default; bool any = false;
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        {
                            all++;
                            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                            on++;
                            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                        }
                        sb.Append($"view '{go.name}' active={go.activeInHierarchy} pos {go.transform.position} scale {go.transform.localScale} renderers {on}/{all} bounds {b.center} size {b.size} sequence={(go.GetComponent<TheWaningBorder.Rendering.BuildingConstructionSequence>() != null)} children={go.transform.childCount}");
                    }
                    else
                    {
                        sb.Append("NO VIEW; ");
                        sb.Append($"manager={(TheWaningBorder.Rendering.EntityViewManager.Instance != null)} presId={(em.HasComponent<PresentationId>(e) ? em.GetComponentData<PresentationId>(e).Id.ToString() : "none")} ");
                        bool res = PresentationSpawnSystem.TryResolveBuildingVisual(em, e, out var want);
                        sb.Append($"resolve={res} prefab={(want != null ? want.name : "null")} ");
                        int named = 0;
                        foreach (var g in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                            if (g.parent == null && g.name.StartsWith("Entity_" + e.Index + "_")) { named++; sb.Append($"go '{g.name}' active={g.gameObject.activeSelf} at {g.position} "); }
                        var types = em.GetComponentTypes(e, Allocator.Temp);
                        sb.Append("components:");
                        foreach (var t in types) sb.Append(" " + t.GetManagedType().Name);
                        types.Dispose();
                    }
                    sb.Append(" | ");
                }
            q.Dispose();
            File.AppendAllText(Path.Combine(OutDir, "diag.txt"), sb + System.Environment.NewLine);
        }

        static IEnumerator Record(CameraController rig)
        {
            var fPos = typeof(CameraController).GetField("_targetPosition", BindingFlags.NonPublic | BindingFlags.Instance);
            var fZoom = typeof(CameraController).GetField("_targetZoom", BindingFlags.NonPublic | BindingFlags.Instance);
            string frames = Path.Combine(OutDir, "frames");
            Directory.CreateDirectory(frames);
            foreach (var old in Directory.GetFiles(frames, "frame_*.jpg")) File.Delete(old);
            if (File.Exists(Path.Combine(OutDir, "done.txt"))) File.Delete(Path.Combine(OutDir, "done.txt"));

            Time.captureFramerate = Fps;
            var eof = new WaitForEndOfFrame();
            int f = 0, tail = -1, waited = 0;
            bool started = false;
            while (!_stillsOnly && f < MaxSeconds * Fps)
            {
                if (fPos != null) fPos.SetValue(rig, new Vector3(_site.x, 0f, _site.z));
                if (fZoom != null) fZoom.SetValue(rig, Zoom);
                yield return eof;
                int state = SiteState();
                if (!started)
                {
                    // The worker is still walking over.
                    if (state == 0 && ++waited < 90 * Fps) continue;
                    started = true;
                }
                if (state == 2 && tail < 0) tail = Mathf.RoundToInt(TailSeconds * Fps);
                if (_diagOnly && f == 30) { Diagnose(f); break; }
                if (f % 60 == 0 || tail == Mathf.RoundToInt(TailSeconds * Fps) - 1) Diagnose(f);
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(frames, $"frame_{f:D4}.jpg"), shot.EncodeToJPG(92));
                Object.Destroy(shot);
                f++;
                if (tail >= 0 && --tail <= 0) break;
            }
            Debug.Log("[ConstructionVideo] recorded " + f + " frames -> " + frames);

            // ── Stills of the map's terrain ──────────────────────────────
            // One where each terrain layer is purest, plus the steepest and
            // the lowest ground (cliffs / ramps, water), nearest the capital
            // first, and a wide shot of the town.
            string stills = Path.Combine(OutDir, "terrain");
            Directory.CreateDirectory(stills);
            foreach (var old in Directory.GetFiles(stills, "*.png")) File.Delete(old);
            var shots = TerrainSpots();
            TryCapital(out float3 cap);
            shots.Insert(0, ("town_wide", new Vector3(cap.x, 0f, cap.z), 70f));
            foreach (var (name, at, zoom) in shots)
            {
                for (int i = 0; i < 45; i++)
                {
                    if (fPos != null) fPos.SetValue(rig, new Vector3(at.x, 0f, at.z));
                    if (fZoom != null) fZoom.SetValue(rig, zoom);
                    yield return eof;
                }
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                File.WriteAllBytes(Path.Combine(stills, name + ".png"), shot.EncodeToPNG());
                Object.Destroy(shot);
            }

            Time.captureFramerate = 0;
            File.WriteAllText(Path.Combine(OutDir, "done.txt"), f + " frames at " + Fps + " fps, " + shots.Count + " terrain stills");
        }

        static List<(string name, Vector3 at, float zoom)> TerrainSpots()
        {
            var result = new List<(string, Vector3, float)>();
            var terrain = Terrain.activeTerrain;
            if (terrain == null) return result;
            var data = terrain.terrainData;
            Vector3 origin = terrain.transform.position, size = data.size;
            TryCapital(out float3 cap);
            int aw = data.alphamapWidth, ah = data.alphamapHeight, layers = data.alphamapLayers;
            var alpha = data.GetAlphamaps(0, 0, aw, ah);
            const int Step = 8, Win = 6;

            Vector3 World(float u, float v) => new Vector3(origin.x + u * size.x, 0f, origin.z + v * size.z);
            // Keep clear of the map edge, and prefer ground near the capital.
            float Near(Vector3 p) => Vector2.Distance(new Vector2(p.x, p.z), new Vector2(cap.x, cap.z)) / Mathf.Max(size.x, size.z);

            for (int l = 0; l < layers; l++)
            {
                float best = 0f; Vector3 at = default;
                for (int y = Win * 3; y < ah - Win * 3; y += Step)
                    for (int x = Win * 3; x < aw - Win * 3; x += Step)
                    {
                        float sum = 0f; int n = 0;
                        for (int dy = -Win; dy <= Win; dy += 3)
                            for (int dx = -Win; dx <= Win; dx += 3) { sum += alpha[y + dy, x + dx, l]; n++; }
                        var p = World(x / (float)aw, y / (float)ah);
                        float score = sum / n - Near(p) * 0.35f;
                        if (score > best) { best = score; at = p; }
                    }
                if (best > 0.2f) result.Add(("layer_" + data.terrainLayers[l].name, at, 42f));
            }

            float steepFar = 0f; Vector3 steepFarAt = default;
            float steep = 0f, low = float.MaxValue, high = float.MinValue; Vector3 steepAt = default, lowAt = default, highAt = default;
            for (float v = 0.06f; v < 0.94f; v += 0.01f)
                for (float u = 0.06f; u < 0.94f; u += 0.01f)
                {
                    var p = World(u, v);
                    float bias = Near(p);
                    float s = data.GetSteepness(u, v) - bias * 25f;
                    float h = data.GetInterpolatedHeight(u, v);
                    if (s > steep) { steep = s; steepAt = p; }
                    float sf = data.GetSteepness(u, v) + bias * 25f;
                    if (sf > steepFar) { steepFar = sf; steepFarAt = p; }
                    if (h + bias * 4f < low) { low = h + bias * 4f; lowAt = p; }
                    if (h - bias * 4f > high) { high = h - bias * 4f; highAt = p; }
                }
            result.Add(("steepest_slope", steepAt, 48f));
            result.Add(("steepest_slope_far", steepFarAt, 30f));
            result.Add(("lowest_ground", lowAt, 48f));
            result.Add(("highest_ground", highAt, 48f));
            return result;
        }

        // ── Garrison_2: the floor planes ────────────────────────────────
        const string GarrisonPath = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/Garrison/Garrison_2.prefab";

        /// <summary>Deletes every Plane object (the built-in Plane mesh, or a
        /// GameObject named "Plane" / "Plane (n)") from Garrison_2.</summary>
        [MenuItem("Waning Border/Art/Garrison_2: Remove Planes")]
        public static void RemoveGarrisonPlanes()
        {
            var root = PrefabUtility.LoadPrefabContents(GarrisonPath);
            int removed = 0;
            try
            {
                var doomed = new List<GameObject>();
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t == root.transform) continue;
                    var mf = t.GetComponent<MeshFilter>();
                    bool planeMesh = mf != null && mf.sharedMesh != null && mf.sharedMesh.name == "Plane"
                        && AssetDatabase.GetAssetPath(mf.sharedMesh).StartsWith("Library/");
                    bool planeName = t.name == "Plane" || t.name.StartsWith("Plane (");
                    if (planeMesh || planeName) doomed.Add(t.gameObject);
                }
                foreach (var go in doomed)
                    if (go != null) { Object.DestroyImmediate(go); removed++; }
                if (removed > 0) PrefabUtility.SaveAsPrefabAsset(root, GarrisonPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Directory.CreateDirectory("Temp");
            File.WriteAllText("Temp/garrison-planes.txt", removed + " planes removed from " + GarrisonPath);
            Debug.Log($"[ConstructionVideo] {removed} planes removed from {GarrisonPath}");
        }

        static bool TryCapital(out float3 pos)
        {
            pos = default;
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return false;
            var em = w.EntityManager;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<HallTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            q.Dispose();
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == Faction.Blue) { pos = xfs[i].Position; return true; }
            return false;
        }
    }
}
