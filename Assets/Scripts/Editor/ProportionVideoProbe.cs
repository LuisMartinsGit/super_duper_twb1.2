// ProportionVideoProbe.cs
// The proportion-test buildings (ProportionTestAuthor: Garrison_PT / Fortress_PT)
// in a real match, beside real units, on video (2026-11-11). From play mode:
//
//   Launch  — Broken Oath, edition A, a HUMAN Blue seat already Alanthor in
//             Age 1 (so nothing orders Blue's units about) and one Easy AI on
//             the far plateau; fog off. Before the map loads, the Alanthor
//             Garrison and Fortress level SOs are pointed at the PT prefabs IN
//             MEMORY ONLY — the game then draws them through its own path
//             (footprint fit, player-colour roofs, construction ground,
//             selection). Nothing is marked dirty or saved; Restore (run by
//             itself when play mode ends, and at the next editor load if the
//             session died) re-imports the six SOs from disk.
//   Arrange — (runs itself once the capital exists) raises the capital and a
//             new Barracks to the level in play, stands the OLD Garrison and
//             Fortress of that level beside them as plain models at their
//             in-game fit, and spawns ~60 units: blocks by the doors, columns
//             walking the road in front of all four buildings.
//   Record  — hides the UI and the log overlay and records a scripted camera
//             pass with ProportionVideoRecorder (JPG frames; encode outside).
//
// Output folder: TWB_OUT when set, else Temp/proportion-video.

using System.Collections.Generic;
using System.IO;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Data;
using TheWaningBorder.UI.Menus;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    [InitializeOnLoad]
    public static class ProportionVideoProbe
    {
        const string Ala = "Assets/GameData/TechTree/Civs/Alanthor/Buildings/";
        const string MapScene = "BrokenOath";
        const byte Level = 3;
        static readonly (string ladder, string soFormat, string ptFormat, string oldFormat, float foot)[] Ladders =
        {
            ("Barracks", Ala + "Garrison/Garrison_Lvl{0}.asset", Ala + "Garrison/ProportionTest/Garrison_PT_Lvl{0}.prefab", Ala + "Garrison/Garrison_3 {0}.prefab", 8f),
            ("Fortress", Ala + "Fortress/Fortress_Lvl{0}.asset", Ala + "Fortress/ProportionTest/Fortress_PT3_Lvl{0}.prefab", Ala + "Fortress/Fortress {0}.prefab", 10f),
        };
        static string OutDir => System.Environment.GetEnvironmentVariable("TWB_OUT") is { Length: > 0 } s ? s : "Temp/proportion-video";
        const string OverrideFlag = "Temp/proportion-video-override.flag";

        // Screen axes of the default camera (yaw 45): right and away.
        static readonly float3 R = new float3(0.7071f, 0f, -0.7071f);
        static readonly float3 F = new float3(0.7071f, 0f, 0.7071f);
        // Where things stand, from the capital (metres along R / F).
        // The capital itself is the unit-kit PT3 Fortress (2026-11-11, third take);
        // beside it, as plain models at their in-game fit: the mixed-scale PT2
        // Fortress, the hand-authored one, and a PT3 copy on the slope to the
        // west (the foundation skirt).
        static readonly float2 PtFortressAt = new float2(30f, -6f);
        static readonly float2 OldFortressAt = new float2(48f, -8f);
        static readonly float2 SlopeCopyAt = new float2(-24f, -5f);
        const string PtFortressFormat = Ala + "Fortress/ProportionTest/Fortress_PT2_Lvl{0}.prefab";

        static readonly List<GameObject> _props = new List<GameObject>();
        static readonly List<(Entity unit, float3 a, float3 b, bool toB)> _walkers = new List<(Entity, float3, float3, bool)>();
        static float3 _cap;
        static Entity _barracks;

        static ProportionVideoProbe()
        {
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
            // A session that died with the override in memory: put the SOs back.
            if (File.Exists(OverrideFlag) && !EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.delayCall += Restore;
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode && File.Exists(OverrideFlag)) Restore();
        }

        // ── the in-memory override ──────────────────────────────────────
        static void Override()
        {
            File.WriteAllText(OverrideFlag, "the Garrison / Fortress level SOs point at the PT prefabs in memory");
            foreach (var l in Ladders)
                for (int lv = 1; lv <= 3; lv++)
                {
                    var pt = AssetDatabase.LoadAssetAtPath<GameObject>(string.Format(l.ptFormat, lv));
                    if (pt == null) { Debug.LogError("[ProportionVideo] missing " + string.Format(l.ptFormat, lv)); continue; }
                    if (!TechCatalog.TryGetBuildingLevel(Cultures.Alanthor, l.ladder, lv, out var def)) { Debug.LogError($"[ProportionVideo] no {l.ladder} L{lv} level"); continue; }
                    def.prefab = pt;      // memory only: never SetDirty, never saved
                }
            Debug.Log("[ProportionVideo] level SOs point at the PT prefabs (memory only)");
        }

        /// <summary>Puts each level SO's prefab back to what its .asset file
        /// on disk names (the guid is read from the YAML, so this does not
        /// depend on what is in memory; a re-import does not reload an object
        /// Unity believes unchanged). Nothing is marked dirty.</summary>
        [MenuItem("Waning Border/Debug/Proportion Video: Restore Level SOs (from disk)")]
        public static void Restore()
        {
            var sb = new System.Text.StringBuilder("[ProportionVideo] level SOs restored from disk:");
            foreach (var l in Ladders)
                for (int lv = 1; lv <= 3; lv++)
                {
                    string path = string.Format(l.soFormat, lv);
                    var so = AssetDatabase.LoadAssetAtPath<BuildingLevelDefSO>(path);
                    var m = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(path), @"
  prefab: \{fileID: -?\d+, guid: ([0-9a-f]{32})");
                    var prefab = m.Success ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(m.Groups[1].Value)) : null;
                    if (so == null || prefab == null) { Debug.LogError("[ProportionVideo] cannot restore " + path); continue; }
                    so.prefab = prefab;
                    sb.Append($" {l.ladder}{lv}={prefab.name}");
                }
            if (File.Exists(OverrideFlag)) File.Delete(OverrideFlag);
            Debug.Log(sb.ToString());
        }

        // ── launch ──────────────────────────────────────────────────────
        [MenuItem("Waning Border/Debug/Proportion Video: Launch (human Blue Alanthor Age 1, PT buildings)")]
        public static void Launch()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[ProportionVideo] enter play mode first"); return; }
            Override();
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
            LobbyConfig.Slots[1].StartIndex = 3;
            GameSettings.SelectedMapScene = MapScene;
            GameSettings.SpawnSeed = 20261020;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = true;
            GameSettings.IsObserver = false;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            GameSettings.StartAge = SkirmishStartAge.Age2;
            GameSettings.PerFactionStartCulture = false;
            GameSettings.StartCulture = Cultures.Alanthor;
            LobbyConfig.ApplyColorSelections();
            Debug.Log("[ProportionVideo] launching " + MapScene);
            LoadingScreen.Show(GameSettings.SelectedMapScene);
            _deadline = Time.realtimeSinceStartup + 240f; _settle = 0;
            EditorApplication.update -= ArrangePoll;
            EditorApplication.update += ArrangePoll;
        }

        static float _deadline; static int _settle;

        static void ArrangePoll()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= ArrangePoll; return; }
            if (Time.realtimeSinceStartup > _deadline) { EditorApplication.update -= ArrangePoll; Debug.LogWarning("[ProportionVideo] gave up waiting for the capital"); return; }
            if (!TryCapital(out _)) { _settle = 0; return; }
            if (++_settle < 60) return;
            EditorApplication.update -= ArrangePoll;
            Arrange();
        }

        static float3 At(float2 rf) => Ground(_cap + R * rf.x + F * rf.y);
        static float3 Ground(float3 p) { p.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(p.x, p.z); return p; }

        [MenuItem("Waning Border/Debug/Proportion Video: Arrange (buildings + units)")]
        public static void Arrange()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null || !TryCapital(out _cap)) { Debug.LogWarning("[ProportionVideo] no world / capital"); return; }
            var em = w.EntityManager;
            Directory.CreateDirectory(OutDir);
            DumpSurroundings(em);

            // The new buildings, through the game's own path.
            foreach (var l in Ladders)
                TheWaningBorder.Systems.Buildings.FactionBuildingLevels.ApplyToAll(em, Faction.Blue, l.ladder, Level);

            // The comparison models, plain, at their in-game fit and half turn.
            foreach (var g in _props) if (g != null) Object.Destroy(g);
            _props.Clear();
            Color blue = FactionColors.ColorPool[FactionColors.GetColorIndex((int)Faction.Blue)];
            var half = Quaternion.Euler(0f, 180f, 0f);
            foreach (var (path, at) in new[] { (string.Format(PtFortressFormat, Level), PtFortressAt), (string.Format(Ladders[1].oldFormat, Level), OldFortressAt), (string.Format(Ladders[1].ptFormat, Level), SlopeCopyAt) })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) { Debug.LogWarning("[ProportionVideo] no prefab " + path); continue; }
                var go = Object.Instantiate(prefab);
                go.name = "ProportionVideo_" + prefab.name;
                go.transform.localScale = Vector3.one;
                float fit = PresentationSpawnSystem.ComputeFootprintFitForSize(go, 10f, 10f, out Vector3 off);
                go.transform.localScale = Vector3.one * fit;
                go.transform.rotation = half;
                go.transform.position = (Vector3)At(at) - half * (off * fit);
                TheWaningBorder.Rendering.BuildingFactionColorMarker.Apply(go, blue);
                _props.Add(go);
            }

            SpawnUnits(em);
            EditorApplication.update -= WalkPoll;
            EditorApplication.update += WalkPoll;
            TheWaningBorder.CameraRig.CameraController.FocusOn(At(new float2(20f, -6f)), instant: true);
            Debug.Log($"[ProportionVideo] arranged at L{Level}: capital {_cap}, {_units} units, {_walkers.Count} walking");
        }

        static int _units;

        static void SpawnUnits(EntityManager em)
        {
            if (_units > 0) return;
            float3 fort = _cap, pt = At(PtFortressAt), old = At(OldFortressAt), slope = At(SlopeCopyAt);
            // At the gates (the fronts face south, -Z): a pair at each door, blocks behind them.
            Block(em, "Spearman", fort + new float3(0f, 0f, -5.9f), 2, 1, 1.7f);
            Block(em, "Spearman", fort + new float3(0f, 0f, -8.5f), 4, 3, 1.3f);
            Block(em, "Alanthor_Archer", fort + new float3(-5.5f, 0f, -8f), 3, 2, 1.3f);
            Block(em, "Alanthor_Swordsman", pt + new float3(0.6f, 0f, -5.8f), 2, 1, 1.6f);
            Block(em, "Alanthor_Swordsman", pt + new float3(0.6f, 0f, -8.5f), 3, 2, 1.3f);
            Block(em, "Spearman", old + new float3(0f, 0f, -6.2f), 3, 2, 1.3f);
            Block(em, "Spearman", slope + new float3(0f, 0f, -6.2f), 2, 2, 1.3f);
            Block(em, "Worker", fort + new float3(8.5f, 0f, -4f), 2, 1, 1.5f);
            // Columns marching past the gates, both ways.
            Column(em, "Spearman", 8, new float2(-30f, -13f), new float2(56f, -17f), 1.6f);
            Column(em, "Alanthor_Outrider", 4, new float2(58f, -21f), new float2(-30f, -19f), 2.6f);
            Column(em, "Alanthor_Crossbowman", 4, new float2(16f, -9f), new float2(40f, -14f), 1.5f);
        }

        static void Block(EntityManager em, string id, float3 centre, int cols, int rows, float pitch)
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var p = Ground(centre + new float3((c - (cols - 1) * 0.5f) * pitch, 0f, -r * pitch));
                    if (TheWaningBorder.Entities.UnitFactory.Create(em, id, p, Faction.Blue) != Entity.Null) _units++;
                }
        }

        /// <summary>n units in file from a toward b (R / F metres from the
        /// capital); WalkPoll turns each round at its end.</summary>
        static void Column(EntityManager em, string id, int n, float2 a, float2 b, float gap)
        {
            float3 pa = At(a), pb = At(b);
            float3 dir = math.normalize(pb - pa);
            for (int i = 0; i < n; i++)
            {
                var start = Ground(pa - dir * (i * gap));
                var u = TheWaningBorder.Entities.UnitFactory.Create(em, id, start, Faction.Blue);
                if (u == Entity.Null) continue;
                _units++;
                _walkers.Add((u, Ground(pa - dir * (i * gap)), Ground(pb - dir * (i * gap)), true));
            }
        }

        static double _nextWalk;
        static bool _ordered;

        /// <summary>Keeps the columns marching end to end.</summary>
        static void WalkPoll()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= WalkPoll; _walkers.Clear(); _units = 0; _ordered = false; _barracks = Entity.Null; return; }
            if (EditorApplication.timeSinceStartup < _nextWalk) return;
            _nextWalk = EditorApplication.timeSinceStartup + 0.5;
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            var em = w.EntityManager;
            for (int i = 0; i < _walkers.Count; i++)
            {
                var (u, a, b, toB) = _walkers[i];
                if (!em.Exists(u) || !em.HasComponent<LocalTransform>(u)) continue;
                float3 goal = toB ? b : a;
                float3 p = em.GetComponentData<LocalTransform>(u).Position;
                bool arrived = math.distance(p.xz, goal.xz) < 2.5f;
                if (arrived) { toB = !toB; goal = toB ? b : a; _walkers[i] = (u, a, b, toB); }
                if (arrived || !_ordered)
                    TheWaningBorder.Core.Commands.CommandRouter.IssueMove(em, u, goal, TheWaningBorder.Core.Commands.CommandSource.AI);
            }
            _ordered = true;
        }

        // ── the take ────────────────────────────────────────────────────
        [MenuItem("Waning Border/Debug/Proportion Video: Hide UI")]
        public static void HideUi()
        {
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) c.enabled = false;
            foreach (var o in Object.FindObjectsByType<TheWaningBorder.Core.Diagnostics.DebugLogOverlay>(FindObjectsInactive.Include, FindObjectsSortMode.None)) o.enabled = false;
            // The world-space "+75" income floaters.
            // The COMPONENT, not its GameObject: it sits on the managers object
            // beside PresentationSpawnSystem, and switching that off stops
            // every view in the match (2026-11-12).
            foreach (var o in Object.FindObjectsByType<TheWaningBorder.UI.Ingame.FloatingIncomeDisplay>(FindObjectsInactive.Include, FindObjectsSortMode.None)) o.enabled = false;
        }

        [MenuItem("Waning Border/Debug/Proportion Video: Record")]
        public static void Record()
        {
            if (!EditorApplication.isPlaying || ProportionVideoRecorder.Running) return;
            HideUi();
            ProportionVideoRecorder.Key K(float t, float r, float f, float zoom) => new ProportionVideoRecorder.Key { T = t, Focus = At(new float2(r, f)), Zoom = zoom };
            var keys = new[]
            {
                K(0f, 10f, -6f, 62f),       // the town: PT3 on the slope, the PT3 capital, PT2
                K(3.5f, -20f, -8f, 34f),    // the PT3 copy on the slope
                K(8f, 3f, -5f, 34f),        // the PT3 capital
                K(11f, 3.6f, -5.0f, 17f),   // down to its gate
                K(12f, 3.6f, -4.8f, 16f),
            };
            if (!ProportionVideoRecorder.Begin(keys, Path.Combine(OutDir, "frames"))) Debug.LogError("[ProportionVideo] no camera rig to record with");
        }

        [MenuItem("Waning Border/Debug/Proportion Video: Camera Wide")]
        public static void CameraWide()
        {
            HideUi();
            TheWaningBorder.CameraRig.CameraController.FocusOn(At(new float2(22f, 6f)), instant: true);
        }

        // ── helpers ─────────────────────────────────────────────────────
        static void DumpSurroundings(EntityManager em)
        {
            var sb = new System.Text.StringBuilder($"capital {_cap}\n(R, F) metres from the capital, of everything with a footprint within 90 m:\n");
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<BuildingSize>());
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    float3 d = em.GetComponentData<LocalTransform>(e).Position - _cap;
                    if (math.length(d.xz) > 90f) continue;
                    var size = em.GetComponentData<BuildingSize>(e);
                    string id = TheWaningBorder.Entities.BuildingIds.Of(e, em) ?? "?";
                    sb.AppendLine($"  ({math.dot(d, R):F0}, {math.dot(d, F):F0})  {id}  {size.Width}x{size.Height}  y {d.y:F1}");
                }
            q.Dispose();
            sb.AppendLine("terrain height (relative to the capital), R -80..100 step 10 across, F 60..-70 step 10 down:");
            for (float f = 60f; f >= -70f; f -= 10f)
            {
                sb.Append($"  F {f,4}:");
                for (float r = -80f; r <= 100f; r += 10f) sb.Append($" {At(new float2(r, f)).y - _cap.y,5:F1}");
                sb.AppendLine();
            }
            File.WriteAllText(Path.Combine(OutDir, "surroundings.txt"), sb.ToString());
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
