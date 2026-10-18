// SanctumCrystalProbe.cs
// See the Sanctum landmark in a real match (Art_Direction.md §6.3).
//
//   Launch Sanctum Probe (Fiendstone Pass) / (Yiel Lymwerra) — enter play mode
//       (any scene) first; AI vs AI, observer, fog off, Age 0.
//   Sanctum Probe Focus Next — moves the camera to the next Sanctum crystal,
//       cycling; the log line names its territory.

using System.Collections.Generic;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.UI.Menus;
using TheWaningBorder.World.Regions;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class SanctumCrystalProbe
    {
        private static int _focus;

        [MenuItem("Waning Border/Debug/Launch Sanctum Probe (Fiendstone Pass)")]
        public static void LaunchFiendstone() => Launch("FiendstonePass");

        [MenuItem("Waning Border/Debug/Launch Sanctum Probe (Yiel Lymwerra)")]
        public static void LaunchYiel() => Launch("YielLymwerra");

        private static void Launch(string scene)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[SanctumProbe] enter play mode (any scene) first");
                return;
            }
            GameEdition.SetEditorPick("A");
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = 2;
            LobbyConfig.SetupSinglePlayer(2);
            for (int i = 0; i < 2; i++)
            {
                var slot = LobbyConfig.Slots[i];
                slot.Type = SlotType.AI;
                slot.AIDifficulty = LobbyAIDifficulty.Normal;
                slot.TeamIndex = Alliances.NoTeam;
                slot.StartIndex = PlayerSlot.AutoStart;
            }
            GameSettings.SelectedMapScene = scene;
            GameSettings.SpawnSeed = 20261018;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = true;
            GameSettings.IsObserver = true;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            GameSettings.StartAge = SkirmishStartAge.Age0;
            GameSettings.PerFactionStartCulture = false;
            LobbyConfig.ApplyColorSelections();
            Debug.Log($"[SanctumProbe] launching on {scene}");
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        /// <summary>Hide every screen-space canvas (popups included) so a
        /// capture shows only the world; run again to bring them back.</summary>
        [MenuItem("Waning Border/Debug/Sanctum Probe Toggle UI")]
        public static void ToggleUi()
        {
            if (!EditorApplication.isPlaying) return;
            _uiHidden = !_uiHidden;
            foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) c.enabled = !_uiHidden;
        }
        private static bool _uiHidden;

        [MenuItem("Waning Border/Debug/Sanctum Probe Focus Next")]
        public static void FocusNext()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (!EditorApplication.isPlaying || world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var list = new List<Vector3>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<SanctumCrystalTag>(),
                                                ComponentType.ReadOnly<LocalTransform>()))
            using (var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp))
                foreach (var xf in xfs) list.Add(xf.Position);
            if (list.Count == 0) { Debug.Log("[SanctumProbe] no Sanctum crystals in this match"); return; }
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
            var p = list[_focus++ % list.Count];
            TheWaningBorder.CameraRig.CameraController.FocusOn(p, instant: true);
            int t = RegionMap.Ready ? RegionMap.RegionAt(p.x, p.z) : -1;
            Debug.Log($"[SanctumProbe] focus {(_focus - 1) % list.Count + 1}/{list.Count}: " +
                      $"{(t >= 0 ? RegionMap.NameOf(t) : "?")} at {p}");
        }
    }
}
