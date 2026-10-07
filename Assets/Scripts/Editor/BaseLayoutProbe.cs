// BaseLayoutProbe.cs
// Debug menu for the AI's drawn base layout (docs/Design/Game_AI.md § 6g):
// a Mirror Marches skirmish with maximum starting resources against one AI,
// a camera jump onto that AI's capital, and a dump of every building it owns
// as an offset from the capital in 2 m build cells (the same frame as the
// layout grid in AIBaseTemplate.asset: x east, rows SOUTH).

using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.UI.Menus;

namespace TheWaningBorder.EditorTools
{
    public static class BaseLayoutProbe
    {
        [MenuItem("Waning Border/Debug/Launch Base Layout Test (Mirror Marches, max resources)")]
        public static void Launch()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[BaseLayoutProbe] enter play mode (any scene) first");
                return;
            }
            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = 2;
            LobbyConfig.SetupSinglePlayer(2);
            for (int i = 0; i < 2; i++)
            {
                var ai = LobbyConfig.Slots[i];
                ai.Type = SlotType.AI;                    // slot 0 too: two AI towns to compare
                ai.AIDifficulty = LobbyAIDifficulty.Hard;
            }
            GameSettings.SelectedMapScene = "MirrorMarches";
            GameSettings.SpawnSeed = 20261006;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = false;          // no curse: watch the base, not a war
            GameSettings.MaxStartingResources = true;
            GameSettings.IsObserver = true;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            LobbyConfig.ApplyColorSelections();
            Debug.Log("[BaseLayoutProbe] launching Mirror Marches, max resources, two Hard AIs, curse off");
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        static bool TryCapital(EntityManager em, Faction f, out Vector3 pos)
        {
            pos = default;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            q.Dispose();
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == f) { pos = xfs[i].Position; return true; }
            return false;
        }

        [MenuItem("Waning Border/Debug/Base Layout: Camera to Red Capital")]
        public static void CameraToRed() => CameraTo(Faction.Red);

        [MenuItem("Waning Border/Debug/Base Layout: Camera to Blue Capital")]
        public static void CameraToBlue() => CameraTo(Faction.Blue);

        static void CameraTo(Faction f)
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null || !TryCapital(w.EntityManager, f, out var p))
            {
                Debug.LogWarning($"[BaseLayoutProbe] no {f} capital");
                return;
            }
            TheWaningBorder.CameraRig.CameraController.FocusOn(p, instant: true);
        }

        [MenuItem("Waning Border/Debug/Base Layout: Dump Red Buildings")]
        public static void DumpRed()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            var em = w.EntityManager;
            if (!TryCapital(em, Faction.Red, out var c)) { Debug.LogWarning("[BaseLayoutProbe] no Red capital"); return; }
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            q.Dispose();
            var sb = new StringBuilder("[BaseLayoutProbe] Red buildings, offset from the capital in cells (x east, z south):\n");
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != Faction.Red) continue;
                var p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                string name = em.HasComponent<DisplayName>(ents[i])
                    ? em.GetComponentData<DisplayName>(ents[i]).Value.ToString() : "?";
                string size = em.HasComponent<BuildingSize>(ents[i])
                    ? $"{em.GetComponentData<BuildingSize>(ents[i]).Width}x{em.GetComponentData<BuildingSize>(ents[i]).Height}m" : "";
                sb.AppendLine($"  {name,-24} {size,-7} dx {(p.x - c.x) / BuildGrid.CellSize,6:F1}  dz {-(p.z - c.z) / BuildGrid.CellSize,6:F1}");
                n++;
            }
            sb.Append($"  ({n} buildings)");
            Debug.Log(sb.ToString());
        }
    }
}
