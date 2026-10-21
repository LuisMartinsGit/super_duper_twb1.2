// AlanthorArtProbe.cs
// Test bed for the one-SO-one-model building art (docs/Design/Art_Direction.md
// § 13, 2026-10-20), from inside play mode:
//
//   Launch      — HollowTable, edition A, both seats AI and already Alanthor
//                 in Age 1 (fog off, the editor observing): every starting
//                 building spawns through the SO path.
//   Raise Lv2/3 — Blue's buildings of every Alanthor ladder to that faction
//                 level (FactionBuildingLevels.ApplyToAll): the swap path.
//   Showcase    — every Alanthor model (and the edition A base forms) laid out
//                 in a grid south of Blue's capital, fitted to its footprint,
//                 as plain GameObjects — the art review in the game's light.
//   Lay Walls   — a stone run and a palisade run beside Blue's capital, each
//                 with a gatehouse, a wall tower and both engine mounts: every
//                 WallModuleArt slot of the level in play (Raise re-levels them).
//   Camera      — to Blue's capital / the showcase.

using System.Collections.Generic;
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
    public static class AlanthorArtProbe
    {
        static readonly string[] Ladders = {
            "Fortress", "Barracks", "Hut", "GatherersHut", "Mine", "ArcheryRange", "Alanthor_RoyalStable",
            "Alanthor_SiegeYard", "Alanthor_Tower", "Alanthor_TradingOutpost", "VaultOfAlmierra" };
        static readonly List<GameObject> _showcase = new List<GameObject>();
        static float3 _showcaseAt;

        [MenuItem("Waning Border/Debug/Alanthor Art Probe: Launch (Age 1, Alanthor AI vs Alanthor AI)")]
        public static void Launch()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[AlanthorArtProbe] enter play mode first"); return; }
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
                slot.AIDifficulty = LobbyAIDifficulty.Hard;
                slot.TeamIndex = Alliances.NoTeam;
                slot.StartIndex = PlayerSlot.AutoStart;
            }
            GameSettings.SelectedMapScene = "HollowTable";
            GameSettings.SpawnSeed = 20261020;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = true;
            GameSettings.IsObserver = true;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            GameSettings.StartAge = SkirmishStartAge.Age2;
            GameSettings.PerFactionStartCulture = false;
            GameSettings.StartCulture = Cultures.Alanthor;
            LobbyConfig.ApplyColorSelections();
            Debug.Log("[AlanthorArtProbe] launching HollowTable, edition A, Age 1 Alanthor start");
            LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        [MenuItem("Waning Border/Debug/Alanthor Art Probe: Raise Blue to Lv2")]
        public static void Raise2() => Raise(2);
        [MenuItem("Waning Border/Debug/Alanthor Art Probe: Raise Blue to Lv3")]
        public static void Raise3() => Raise(3);

        static void Raise(byte level)
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            foreach (var id in Ladders)
                TheWaningBorder.Systems.Buildings.FactionBuildingLevels.ApplyToAll(w.EntityManager, Faction.Blue, id, level);
            TheWaningBorder.Entities.AlanthorWall.PromoteFactionWalls(w.EntityManager, Faction.Blue, level);
            MountWhereAllowed(w.EntityManager);
            Debug.Log($"[AlanthorArtProbe] Blue raised to L{level}");
        }

        [MenuItem("Waning Border/Debug/Alanthor Art Probe: Lay Walls")]
        public static void LayWalls()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null || !TryCapital(out var cap)) { Debug.LogWarning("[AlanthorArtProbe] no Blue capital"); return; }
            var em = w.EntityManager;
            _wallsAt = cap + new float3(0, 0, 34f);
            LayRun(em, cap + new float3(-21f, 0, 30f), palisade: false);
            LayRun(em, cap + new float3(-21f, 0, 44f), palisade: true);
            Debug.Log("[AlanthorArtProbe] walls laid");
        }

        static float3 _wallsAt;
        static Entity _ballistaCell, _trebuchetCell;

        static void MountWhereAllowed(EntityManager em)
        {
            foreach (var (cell, treb) in new[] { (_ballistaCell, false), (_trebuchetCell, true) })
                if (cell != Entity.Null && em.Exists(cell) && !em.HasComponent<EmplacementTag>(cell)
                    && TheWaningBorder.Entities.AlanthorWall.CanConvertToEmplacement(em, cell, treb))
                    TheWaningBorder.Entities.AlanthorWall.MountEmplacement(em, cell, treb);
        }

        [MenuItem("Waning Border/Debug/Alanthor Art Probe: Camera to Walls")]
        public static void CameraWalls()
        {
            HideOverlays();
            TheWaningBorder.CameraRig.CameraController.FocusOn(_wallsAt, instant: true);
        }

        /// <summary>The observer still gets the culture picker; it covers the shot.</summary>
        static void HideOverlays()
        {
            var g = GameObject.Find("GameUI_CultureSelectionMenu");
            if (g != null) g.SetActive(false);
        }

        static void LayRun(EntityManager em, float3 a, bool palisade)
        {
            float3 b = a + new float3(42f, 0, 0);
            a.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(a.x, a.z);
            b.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(b.x, b.z);
            var h0 = TheWaningBorder.Entities.AlanthorWall.CreateHubOfKind(em, a, Faction.Blue, palisade);
            var h1 = TheWaningBorder.Entities.AlanthorWall.CreateHubOfKind(em, b, Faction.Blue, palisade);
            var seg = TheWaningBorder.Entities.AlanthorWall.CreateSegment(em, h0, h1, Faction.Blue);
            var buf = em.GetBuffer<WallInstanceRef>(seg);
            var cells = new List<Entity>();
            for (int i = 0; i < buf.Length; i++) cells.Add(buf[i].Instance);
            int n = cells.Count;
            if (n < 12) { Debug.LogWarning($"[AlanthorArtProbe] run too short ({n} cells)"); return; }
            // Only what the rules allow (a palisade takes no tower or engine;
            // engines wait for their wall level — MountWhereAllowed re-tries
            // after every Raise). The gate swallows its neighbours, so first.
            if (!palisade)
            {
                em.AddComponentData(cells[2], new WallUpgradeState { UpgradeType = 1, Duration = 0.1f, Remaining = 0.1f });
                _ballistaCell = cells[n - 5];
                _trebuchetCell = cells[n - 3];
                MountWhereAllowed(em);
            }
            var focus = cells[n / 2 - 1];
            using var members = TheWaningBorder.Entities.AlanthorWall.PickGateRegionInstances(em, seg, focus, Allocator.Temp);
            var list = new List<Entity>();
            for (int i = 0; i < members.Length; i++) list.Add(members[i]);
            TheWaningBorder.Entities.AlanthorWall.MakeGate(em, focus, list);
        }

        [MenuItem("Waning Border/Debug/Alanthor Art Probe: Camera to Blue Capital")]
        public static void CameraBlue()
        {
            HideOverlays();
            if (TryCapital(out var p)) TheWaningBorder.CameraRig.CameraController.FocusOn(p, instant: true);
        }

        [MenuItem("Waning Border/Debug/Alanthor Art Probe: Camera to Showcase")]
        public static void CameraShowcase() =>
            TheWaningBorder.CameraRig.CameraController.FocusOn(_showcaseAt, instant: true);

        [MenuItem("Waning Border/Debug/Alanthor Art Probe: Showcase")]
        public static void Showcase()
        {
            foreach (var g in _showcase) if (g != null) Object.Destroy(g);
            _showcase.Clear();
            if (!TryCapital(out var cap)) { Debug.LogWarning("[AlanthorArtProbe] no Blue capital"); return; }
            var items = new List<(GameObject prefab, int2 cells)>();
            foreach (var b in AssetDatabase.FindAssets("t:BuildingDefSO", new[] { "Assets/GameData/TechTree" }))
            {
                var so = AssetDatabase.LoadAssetAtPath<BuildingDefSO>(AssetDatabase.GUIDToAssetPath(b));
                if (so == null) continue;
                bool alanthor = so.id.StartsWith("Alanthor_") || so.id.StartsWith("Sect_") || so.id.StartsWith("Renewal_")
                                || AssetDatabase.GetAssetPath(so).Contains("/Age0/");
                if (!alanthor) continue;
                if (so.prefab != null) items.Add((so.prefab, new int2(so.footprintCells.x, so.footprintCells.y)));
                foreach (var g in AssetDatabase.FindAssets("t:BuildingLevelDefSO", new[] { "Assets/GameData/TechTree/Civs/Alanthor" }))
                {
                    var lv = AssetDatabase.LoadAssetAtPath<BuildingLevelDefSO>(AssetDatabase.GUIDToAssetPath(g));
                    if (lv != null && lv.buildingId == so.id && lv.prefab != null)
                        items.Add((lv.prefab, new int2(so.footprintCells.x, so.footprintCells.y)));
                }
            }
            const int Cols = 8; const float Pitch = 14f;
            _showcaseAt = cap + new float3(0, 0, -40f);
            for (int i = 0; i < items.Count; i++)
            {
                var (prefab, cells) = items[i];
                float x = cap.x + (i % Cols - Cols / 2f) * Pitch;
                float z = cap.z - 30f - (i / Cols) * Pitch;
                var go = (GameObject)Object.Instantiate(prefab);
                go.name = "Showcase_" + prefab.name;
                float fit = PresentationSpawnSystem.ComputeFootprintFitForSize(go, cells.x * 2f, cells.y * 2f, out Vector3 off);
                go.transform.localScale *= fit;
                float y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(x, z);
                go.transform.SetPositionAndRotation(new Vector3(x, y, z) - off * fit, Quaternion.Euler(0, 180, 0));
                _showcase.Add(go);
            }
            Debug.Log($"[AlanthorArtProbe] showcase: {items.Count} models");
        }

        [MenuItem("Waning Border/Debug/Alanthor Art Probe: Dump Building Views")]
        public static void Dump()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            var em = w.EntityManager;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            q.Dispose();
            var sb = new System.Text.StringBuilder("[AlanthorArtProbe] building views:\n");
            foreach (var e in ents)
            {
                var f = em.GetComponentData<FactionTag>(e).Value;
                if (f == Faction.Border) continue;
                string id = TheWaningBorder.Entities.BuildingIds.Of(e, em) ?? "?";
                int lv = em.HasComponent<BuildingUpgradeState>(e) ? em.GetComponentData<BuildingUpgradeState>(e).Level : -1;
                TheWaningBorder.Rendering.EntityViewManager.Instance.TryGetView(e, out var go);
                var src = go != null ? go.GetComponent<PresentationSpawnSystem.BuildingVisualSource>() : null;
                PresentationSpawnSystem.TryResolveBuildingVisual(em, e, out var want);
                sb.AppendLine($"  {f} {id} L{lv} uc={em.HasComponent<UnderConstruction>(e)} view={(go != null ? go.name : "-")} " +
                              $"src={(src != null && src.Prefab != null ? src.Prefab.name : "-")} want={(want != null ? want.name : "-")}");
            }
            Debug.Log(sb.ToString());
            System.IO.File.WriteAllText(@"C:/Users/LUISMA~1/AppData/Local/Temp/claude/d--Users-overw-Documents-The-Waning-Border-1-2/b3673707-dade-41ab-958f-635ae48459e8/scratchpad/shots/views.txt", sb.ToString());
        }

        /// <summary>
        /// The placement ghost against the finished building: for each of
        /// Blue's finished buildings, the model its view was built from next
        /// to the model a ghost of the same id shows; then the ghost of every
        /// catalog building. Written to scratchpad/shots/previews.txt.
        /// </summary>
        [MenuItem("Waning Border/Debug/Alanthor Art Probe: Compare Placement Previews")]
        public static void ComparePreviews()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            var em = w.EntityManager;
            string Ghost(string id)
            {
                var g = PresentationSpawnSystem.CreateFinishedBuildingPreview(em, id, Faction.Blue, out _);
                if (g == null) return "-";
                string n = g.name.Replace("(Clone)", "");
                Object.DestroyImmediate(g);
                return n;
            }
            var sb = new System.Text.StringBuilder("[AlanthorArtProbe] previews vs finished:\n");
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            q.Dispose();
            var seen = new HashSet<string>();
            foreach (var e in ents)
            {
                if (em.GetComponentData<FactionTag>(e).Value != Faction.Blue) continue;
                if (em.HasComponent<UnderConstruction>(e)) continue;
                string id = TheWaningBorder.Entities.BuildingIds.Of(e, em) ?? "?";
                TheWaningBorder.Rendering.EntityViewManager.Instance.TryGetView(e, out var go);
                var src = go != null ? go.GetComponent<PresentationSpawnSystem.BuildingVisualSource>() : null;
                string have = src != null && src.Prefab != null ? src.Prefab.name : "-";
                string ghost = Ghost(id);
                string line = $"  {(ghost == have ? "OK  " : "DIFF")} {id}: finished={have} ghost={ghost}";
                if (seen.Add(line)) sb.AppendLine(line);
            }
            var pq = em.CreateEntityQuery(ComponentType.ReadOnly<PlannedBuilding>(), ComponentType.ReadOnly<FactionTag>());
            using var plans = pq.ToEntityArray(Allocator.Temp);
            pq.Dispose();
            foreach (var e in plans)
            {
                if (em.GetComponentData<FactionTag>(e).Value != Faction.Blue) continue;
                string id = em.GetComponentData<PlannedBuilding>(e).BuildingId.ToString();
                TheWaningBorder.Rendering.EntityViewManager.Instance.TryGetView(e, out var go);
                var src = go != null ? go.GetComponent<PresentationSpawnSystem.BuildingVisualSource>() : null;
                string have = src != null && src.Prefab != null ? src.Prefab.name : "-";
                string ghost = Ghost(id);
                string line = $"  {(ghost == have ? "OK  " : "DIFF")} PLAN {id}: plan={have} ghost={ghost}";
                if (seen.Add(line)) sb.AppendLine(line);
            }
            foreach (bool pal in new[] { false, true })
            {
                var hub = PresentationSpawnSystem.CreateWallHubPreview(em, Faction.Blue, pal);
                string n = hub != null && hub.transform.childCount > 0 ? hub.transform.GetChild(0).name + " <- " +
                    TheWaningBorder.Rendering.WallModuleArt.TierPrefab(TheWaningBorder.Rendering.WallModuleArt.WallPart.Hub,
                        TheWaningBorder.Entities.WallTiers.ArtLevelFor(em, Faction.Blue, pal),
                        CultureConfig.GetCompletedCulture(em, Faction.Blue))?.name : "-";
                if (hub != null) Object.DestroyImmediate(hub);
                sb.AppendLine($"  hub ghost ({(pal ? "palisade" : "stone")}): {n}");
            }
            sb.AppendLine("ghost of every catalog building:");
            foreach (var b in TechCatalog.GetAllBuildings())
                sb.AppendLine($"  {b.id}: {Ghost(b.id)}");
            Debug.Log(sb.ToString());
            System.IO.File.WriteAllText(@"C:/Users/LUISMA~1/AppData/Local/Temp/claude/d--Users-overw-Documents-The-Waning-Border-1-2/b3673707-dade-41ab-958f-635ae48459e8/scratchpad/shots/previews.txt", sb.ToString());
        }

        static bool TryCapital(out float3 pos)
        {
            pos = default;
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return false;
            var em = w.EntityManager;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<HallTag>(), ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            q.Dispose();
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == Faction.Blue) { pos = xfs[i].Position; return true; }
            return false;
        }
    }
}
