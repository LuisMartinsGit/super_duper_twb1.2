// RunaiProbe.cs
// Play-mode probe for the Runai art (docs/Design/Age_1_Runai.md §1.1): an
// AI-vs-AI skirmish in edition C with both factions starting as Runai, so
// the in-game look of every Runai building can be checked (and captured
// through the editor bridge) without clicking through the menus.
//
//   Launch Runai Probe     — enter play mode first, then run this.
//   Runai Probe Focus Next — moves the camera to the next Runai building,
//                            cycling; log line names it.

using System.Collections.Generic;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.UI.Menus;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class RunaiProbe
    {
        private static int _focus;

        [MenuItem("Waning Border/Debug/Launch Runai Probe (Age 2, Runai AI vs Runai AI)")]
        public static void Launch()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[RunaiProbe] enter play mode (any scene) first");
                return;
            }
            GameEdition.SetEditorPick("C");
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = 2;
            LobbyConfig.SetupSinglePlayer(2);
            for (int i = 0; i < 2; i++)
            {
                var slot = LobbyConfig.Slots[i];
                slot.Type = SlotType.AI;
                slot.AIDifficulty = LobbyAIDifficulty.Hard;
                slot.TeamIndex = Alliances.NoTeam;
                slot.StartIndex = PlayerSlot.AutoStart;
                slot.StartCulture = Cultures.Runai;
            }
            GameSettings.SelectedMapScene = "HollowTable";
            GameSettings.SpawnSeed = 20261014;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = true;
            GameSettings.IsObserver = true;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            GameSettings.MaxStartingResources = true;
            GameSettings.StartAge = SkirmishStartAge.Age2;
            GameSettings.PerFactionStartCulture = false;
            GameSettings.StartCulture = Cultures.Runai;
            LobbyConfig.ApplyColorSelections();
            Debug.Log("[RunaiProbe] launching on HollowTable (edition C, Age 2 Runai start, max resources)");
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        // ── Capture all: focus each Blue building in turn and screenshot it ──
        private static List<(Vector3 p, string id)> _shots;
        private static int _shot;
        private static double _at;

        /// <summary>Temp/runai-ingame/<n>_<buildingId>.png for every Blue
        /// building, one per 2 s (the camera settles, the frame renders).</summary>
        [MenuItem("Waning Border/Debug/Runai Probe Capture All")]
        public static void CaptureAll()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (!EditorApplication.isPlaying || world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            _shots = new List<(Vector3, string)>();
            var seen = new HashSet<string>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>(),
                                                ComponentType.ReadOnly<LocalTransform>()))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    if (em.GetComponentData<FactionTag>(e).Value != Faction.Blue) continue;
                    string id = TheWaningBorder.Entities.BuildingIds.Of(e, em) ?? "Unknown";
                    if (!seen.Add(id)) continue;   // one of each kind
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    _shots.Add((new Vector3(p.x, p.y, p.z), id));
                }
            System.IO.Directory.CreateDirectory("Temp/runai-ingame");
            _shot = -1; _at = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Step;
            EditorApplication.update += Step;
            Debug.Log($"[RunaiProbe] capturing {_shots.Count} building kinds");
        }

        private static void Step()
        {
            if (!EditorApplication.isPlaying || _shots == null) { EditorApplication.update -= Step; return; }
            if (EditorApplication.timeSinceStartup < _at) return;
            if (_shot >= 0 && _shot < _shots.Count)
                ScreenCapture.CaptureScreenshot($"Temp/runai-ingame/{_shot:00}_{_shots[_shot].id}.png");
            _shot++;
            if (_shot >= _shots.Count) { EditorApplication.update -= Step; Debug.Log("[RunaiProbe] capture done"); return; }
            TheWaningBorder.CameraRig.CameraController.FocusOn(_shots[_shot].p, instant: true);
            _at = EditorApplication.timeSinceStartup + 2.0;
        }

        [MenuItem("Waning Border/Debug/Runai Probe Focus Next")]
        public static void FocusNext()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (!EditorApplication.isPlaying || world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var list = new List<(Entity e, Vector3 p, string id)>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>(),
                                                ComponentType.ReadOnly<LocalTransform>()))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    if (em.GetComponentData<FactionTag>(e).Value != Faction.Blue) continue;
                    var p = em.GetComponentData<LocalTransform>(e).Position;
                    list.Add((e, new Vector3(p.x, p.y, p.z), TheWaningBorder.Entities.BuildingIds.Of(e, em)));
                }
            if (list.Count == 0) { Debug.Log("[RunaiProbe] no Blue buildings yet"); return; }
            list.Sort((a, b) => a.e.Index.CompareTo(b.e.Index));
            var pick = list[_focus++ % list.Count];
            TheWaningBorder.CameraRig.CameraController.FocusOn(pick.p, instant: true);
            Debug.Log($"[RunaiProbe] focus {(_focus - 1) % list.Count}/{list.Count}: {pick.id} at {pick.p}");
        }
    }
}
