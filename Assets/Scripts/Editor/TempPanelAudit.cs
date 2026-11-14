// Throwaway (2026-11-02): walks the local faction's finished buildings one
// type at a time, selecting each and framing the camera on it, so the actions
// panel can be captured for every building type at the faction's current
// level. Run after "Alanthor Art Probe: Raise Blue to Lv3".

using System.Collections.Generic;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Data;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class TempPanelAudit
    {
        static readonly List<(string id, Entity e)> _list = new();
        static int _index = -1;

        /// <summary>Broken Oath, Blue HUMAN (so the actions panel shows) already
        /// Alanthor in Age 1, one Easy AI far east; fog off.</summary>
        [MenuItem("Waning Border/Debug/Panel Audit: Launch (human Blue, Age 1 Alanthor vs 1 AI)")]
        public static void Launch()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[PanelAudit] enter play mode first"); return; }
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
            GameSettings.SelectedMapScene = "BrokenOath";
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
            Debug.Log("[PanelAudit] launching BrokenOath, human Blue Alanthor Age 1 vs 1 Easy AI");
            TheWaningBorder.UI.Menus.LoadingScreen.Show(GameSettings.SelectedMapScene);
        }

        [MenuItem("Waning Border/Debug/Panel Audit: Select Local Worker")]
        public static void SelectWorker()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            var em = w.EntityManager;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<WorkerTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != GameSettings.LocalPlayerFaction) continue;
                TheWaningBorder.Input.SelectionSystem.ClearSelection();
                TheWaningBorder.Input.SelectionSystem.AddToSelection(ents[i]);
                TheWaningBorder.CameraRig.CameraController.FocusOn(em.GetComponentData<LocalTransform>(ents[i]).Position, instant: true);
                Debug.Log("[PanelAudit] selected worker " + ents[i].Index);
                break;
            }
            q.Dispose();
        }

        /// <summary>Grants resources, then issues the player's level-up on ONE
        /// finished building of every type the local faction owns — the real
        /// command path (UpgradeBuildingCommandHelper.Execute), so the queue,
        /// CompleteUpgrade and FactionBuildingLevels all run as in a match.</summary>
        [MenuItem("Waning Border/Debug/Panel Audit: Upgrade Every Local Type Once (command path)")]
        public static void UpgradeAllOnce()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            var em = w.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;
            TheWaningBorder.Economy.FactionEconomy.Add(em, faction,
                new TheWaningBorder.Core.Cost { Supplies = 5000, Iron = 5000, Veilstone = 3000 });
            var seen = new HashSet<string>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingUpgradeable>(), ComponentType.ReadOnly<FactionTag>(),
                                         ComponentType.Exclude<UnderConstruction>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                string id = TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.ResolveBuildingId(em, ents[i]);
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                var r = TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.Execute(em, ents[i]);
                Debug.Log($"[PanelAudit] upgrade {id} ({ents[i].Index}) -> {r}; faction level now {TheWaningBorder.Systems.Buildings.FactionBuildingLevels.Get(em, faction, id)}");
            }
            q.Dispose();
        }

        /// <summary>Spawns one FINISHED building of every Alanthor-buildable
        /// type the local faction does not own yet, in a row south of its
        /// capital, so the sweep covers the whole roster.</summary>
        [MenuItem("Waning Border/Debug/Panel Audit: Spawn Every Alanthor Building")]
        public static void SpawnAll()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            var em = w.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;
            Unity.Mathematics.float3 cap = default; bool found = false;
            var hq = em.CreateEntityQuery(ComponentType.ReadOnly<HallTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<LocalTransform>());
            using (var ents = hq.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < ents.Length && !found; i++)
                    if (em.GetComponentData<FactionTag>(ents[i]).Value == faction) { cap = em.GetComponentData<LocalTransform>(ents[i]).Position; found = true; }
            hq.Dispose();
            if (!found) { Debug.LogWarning("[PanelAudit] no capital"); return; }
            string[] ids = { "ArcheryRange", "GatherersHut", "Mine", "Alanthor_Tower", "Alanthor_SiegeYard", "Alanthor_RoyalStable", "Alanthor_TradingOutpost", "VeilstoneMine", "Palisade", "Alanthor_Wall" };
            int n = 0;
            foreach (var id in ids)
            {
                var pos = cap + new Unity.Mathematics.float3(-40f + 14f * n, 0f, -46f);
                pos = TheWaningBorder.World.Terrain.TerrainUtility.SnapToTerrain(pos);
                var e = TheWaningBorder.Entities.BuildingFactory.Create(em, id, pos, faction);
                Debug.Log($"[PanelAudit] spawn {id} -> {(e == Entity.Null ? "null" : e.Index.ToString())}");
                n++;
            }
        }

        // -- The sweep (2026-11-06): every building type and state, what each panel shows --
        static readonly List<(string label, Entity e, System.Action setup)> _sweep = new();
        static int _sweepAt;
        static double _sweepNext;
        static readonly System.Text.StringBuilder _out = new();

        [MenuItem("Waning Border/Debug/Panel Audit: Sweep All (writes Temp/panel_audit.txt)")]
        public static void Sweep()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            _list.Clear(); Collect(em);
            _sweep.Clear(); _out.Clear();
            foreach (var (id, e) in _list)
            {
                _sweep.Add((id, e, null));
                if (em.HasBuffer<WallGarrisonSlot>(e))
                {
                    var ent = e;
                    _sweep.Add((id + " MANNED", e, () =>
                    {
                        var m = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
                        var q = m.CreateEntityQuery(ComponentType.ReadOnly<WorkerTag>());
                        using var w = q.ToEntityArray(Allocator.Temp);
                        var slots = m.GetBuffer<WallGarrisonSlot>(ent);
                        if (slots.Length > 0 && w.Length > 0) { var s = slots[0]; s.Occupant = w[0]; slots[0] = s; }
                        q.Dispose();
                    }));
                }
            }
            // Units, one of each kind the local faction has.
            var seen = new HashSet<int>();
            var uq = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<PresentationId>());
            using (var us = uq.ToEntityArray(Allocator.Temp))
                for (int i = 0; i < us.Length; i++)
                    if (em.GetComponentData<FactionTag>(us[i]).Value == GameSettings.LocalPlayerFaction
                        && seen.Add(em.GetComponentData<PresentationId>(us[i]).Id))
                        _sweep.Add(("unit pid " + em.GetComponentData<PresentationId>(us[i]).Id, us[i], null));
            uq.Dispose();
            _sweepAt = 0; _sweepNext = 0;
            EditorApplication.update -= SweepTick;
            EditorApplication.update += SweepTick;
        }

        static bool _dumpDue;
        static void SweepTick()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= SweepTick; return; }
            if (EditorApplication.timeSinceStartup < _sweepNext) return;
            _sweepNext = EditorApplication.timeSinceStartup + 0.8;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            if (_dumpDue)
            {
                _dumpDue = false;
                var (label, ent, _) = _sweep[_sweepAt];
                _out.AppendLine("=== " + label);
                if (em.Exists(ent))
                {
                    var info = TheWaningBorder.UI.Data.EntityActionExtractor.GetActionInfo(ent, em);
                    _out.Append("  type ").Append(info.Type).Append(" actions:");
                    if (info.Actions != null) foreach (var a in info.Actions) _out.Append(' ').Append(a.Id).Append(a.Enabled ? "" : "(off)");
                    _out.AppendLine();
                }
                foreach (var t in Object.FindObjectsByType<TMPro.TMP_Text>(FindObjectsSortMode.None))
                {
                    if (!t.isActiveAndEnabled || string.IsNullOrWhiteSpace(t.text)) continue;
                    var p = t.transform.parent; bool inPanel = false; float alpha = 1f;
                    for (; p != null; p = p.parent)
                    {
                        if (p.name.Contains("Action") || p.name.Contains("Spell")) inPanel = true;
                        var g = p.GetComponent<CanvasGroup>(); if (g != null) alpha *= g.alpha;
                    }
                    if (inPanel && alpha > 0.01f) _out.Append("    ").Append(t.text.Replace("\n", " / ")).Append("   <").Append(t.transform.parent.name).AppendLine(">");
                }
                _sweepAt++;
                if (_sweepAt >= _sweep.Count)
                {
                    System.IO.File.WriteAllText("Temp/panel_audit.txt", _out.ToString());
                    Debug.Log("[PanelAudit] sweep done: " + _sweep.Count);
                    EditorApplication.update -= SweepTick;
                }
                return;
            }
            var entry = _sweep[_sweepAt];
            if (!em.Exists(entry.e)) { _sweepAt++; if (_sweepAt >= _sweep.Count) EditorApplication.update -= SweepTick; return; }
            entry.setup?.Invoke();
            TheWaningBorder.Input.SelectionSystem.ClearSelection();
            TheWaningBorder.Input.SelectionSystem.AddToSelection(entry.e);
            _dumpDue = true;
        }

        /// <summary>Presses HOST on the open Multiplayer menu.</summary>
        [MenuItem("Waning Border/Debug/Panel Audit: Press Host")]
        public static void PressHost()
        {
            var panel = Object.FindFirstObjectByType<TheWaningBorder.UI.Menus.Panels.MultiplayerPanel>();
            if (panel == null) { Debug.LogWarning("[PanelAudit] no MultiplayerPanel"); return; }
            panel.HostButton.onClick.Invoke();
            Debug.Log("[PanelAudit] pressed host");
        }

        [MenuItem("Waning Border/Debug/Panel Audit: Reset")]
        public static void Reset() { _list.Clear(); _index = -1; }

        [MenuItem("Waning Border/Debug/Panel Audit: Select Next Local Building")]
        public static void Next()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning("[PanelAudit] enter play mode first"); return; }
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w == null) return;
            var em = w.EntityManager;
            if (_list.Count == 0) Collect(em);
            if (_list.Count == 0) { Debug.LogWarning("[PanelAudit] no finished buildings of the local faction"); return; }
            _index = (_index + 1) % _list.Count;
            var (id, e) = _list[_index];
            if (!em.Exists(e)) { _list.RemoveAt(_index); _index--; Next(); return; }
            TheWaningBorder.Input.SelectionSystem.ClearSelection();
            TheWaningBorder.Input.SelectionSystem.AddToSelection(e);
            var p = em.GetComponentData<LocalTransform>(e).Position;
            TheWaningBorder.CameraRig.CameraController.FocusOn(p, instant: true);
            Debug.Log($"[PanelAudit] {_index + 1}/{_list.Count}: {id} ({e.Index}) level {TheWaningBorder.Systems.Buildings.FactionBuildingLevels.Get(em, GameSettings.LocalPlayerFaction, TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper.ResolveBuildingId(em, e))}");
        }

        [MenuItem("Waning Border/Debug/Panel Audit: Worker + Hover Supply Node")]
        public static void HoverSupply() => HoverNode(ComponentType.ReadOnly<SupplyNodeTag>(), "GatherersHut");
        [MenuItem("Waning Border/Debug/Panel Audit: Worker + Hover Iron Node")]
        public static void HoverIron() => HoverNode(ComponentType.ReadOnly<IronMineTag>(), "Mine");
        [MenuItem("Waning Border/Debug/Panel Audit: Worker + Hover Veilstone Node")]
        public static void HoverVeilstone() => HoverNode(ComponentType.ReadOnly<VeilstoneOutcroppingTag>(), "VeilstoneMine");
        [MenuItem("Waning Border/Debug/Panel Audit: Worker, No Hover")]
        public static void HoverNone()
        {
            TheWaningBorder.UI.Ingame.ActionsPanelPrefabBinder.HoverOverride = new Unity.Mathematics.float3(-99999f, 0f, -99999f);
            SelectWorker(out _);
        }

        [MenuItem("Waning Border/Debug/Panel Audit: Dump Node Button")]
        public static void DumpNodeButton()
        {
            var b = UnityEngine.Object.FindFirstObjectByType<TheWaningBorder.UI.Ingame.ActionsPanelPrefabBinder>();
            if (b == null) { Debug.Log("[PanelAudit] no binder"); return; }
            var f = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object G(string n) => typeof(TheWaningBorder.UI.Ingame.ActionsPanelPrefabBinder).GetField(n, f)?.GetValue(b);
            var rect = G("_nodeRect") as RectTransform;
            var acts = G("_builderActions") as System.Collections.Generic.List<TheWaningBorder.UI.ActionButton>;
            string ids = acts == null ? "null" : string.Join(",", acts.ConvertAll(a => a.Id));
            Debug.Log($"[PanelAudit] node: has={G("_nodeHas")} id={G("_nodeId")} pos={G("_nodePos")} usable={G("_nodeUsable")} dirty={G("_nodeDirty")} done={G("_nodeDone")} " +
                $"rect={(rect == null ? "null" : rect.gameObject.activeInHierarchy + " parent=" + rect.parent.name + " pos=" + rect.position + " size=" + rect.rect.size + " lossy=" + rect.lossyScale)} " +
                $"placing={TheWaningBorder.Core.PresentationState.PlacingBuilding} override={TheWaningBorder.UI.Ingame.ActionsPanelPrefabBinder.HoverOverride} actions={ids}");
            if (rect != null)
            {
                var sb = new System.Text.StringBuilder("[PanelAudit] tree:");
                foreach (var tr in rect.GetComponentsInChildren<Transform>(true))
                {
                    int depth = 0; for (var q = tr; q != rect; q = q.parent) depth++;
                    var img = tr.GetComponent<UnityEngine.UI.Image>();
                    var r2 = tr as RectTransform;
                    sb.Append('|').Append(new string(' ', depth * 2)).Append(tr.name)
                      .Append(tr.gameObject.activeSelf ? "" : " [off]")
                      .Append(r2 != null ? " " + r2.rect.size : "")
                      .Append(img != null ? " IMG sprite=" + (img.sprite != null ? img.sprite.name : "null") + " en=" + img.enabled + " col=" + img.color : "");
                }
                Debug.Log(sb.ToString());
            }
            if (G("_nodeId") is string id && G("_nodePos") is Unity.Mathematics.float3 pos)
                Debug.Log("[PanelAudit] refusal=" + TheWaningBorder.UI.Ingame.WorkerCommandPanel.EvaluateNow(id, pos));
        }

        static bool SelectWorker(out Unity.Mathematics.float3 at)
        {
            at = default;
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (!EditorApplication.isPlaying || w == null) return false;
            var em = w.EntityManager;
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<CanBuild>(), ComponentType.ReadOnly<FactionTag>(),
                                         ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            Entity worker = Entity.Null;
            for (int i = 0; i < ents.Length && worker == Entity.Null; i++)
                if (em.GetComponentData<FactionTag>(ents[i]).Value == GameSettings.LocalPlayerFaction) worker = ents[i];
            q.Dispose();
            if (worker == Entity.Null) { Debug.LogWarning("[PanelAudit] no local worker"); return false; }
            TheWaningBorder.Input.SelectionSystem.ClearSelection();
            TheWaningBorder.Input.SelectionSystem.AddToSelection(worker);
            at = em.GetComponentData<LocalTransform>(worker).Position;
            return true;
        }

        static void HoverNode(ComponentType tag, string buildingId)
        {
            if (!SelectWorker(out var from)) return;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var q = em.CreateEntityQuery(tag, ComponentType.ReadOnly<LocalTransform>());
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            q.Dispose();
            float best = float.MaxValue; Unity.Mathematics.float3 node = default; bool found = false;
            for (int i = 0; i < xfs.Length; i++)
            {
                var p = xfs[i].Position;
                if (!TheWaningBorder.World.Regions.TerritoryOwnership.TrySnapToNode(em, buildingId, p, out _)) continue;
                float d = Unity.Mathematics.math.distancesq(p.xz, from.xz);
                if (d < best) { best = d; node = p; found = true; }
            }
            if (!found) { Debug.LogWarning("[PanelAudit] no free node for " + buildingId); return; }
            TheWaningBorder.UI.Ingame.ActionsPanelPrefabBinder.HoverOverride = node;
            var rig = UnityEngine.Object.FindFirstObjectByType<TheWaningBorder.CameraRig.CameraController>();
            if (rig != null) rig.enabled = false;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 n = new Vector3(node.x, node.y, node.z);
                cam.transform.position = n + new Vector3(0f, 34f, -26f);
                cam.transform.LookAt(n);
            }
            Debug.Log($"[PanelAudit] hovering {buildingId} node at {node}");
        }

        static void Collect(EntityManager em)
        {
            var faction = GameSettings.LocalPlayerFaction;
            var seen = new HashSet<string>();
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>(),
                                         ComponentType.ReadOnly<LocalTransform>(), ComponentType.Exclude<UnderConstruction>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                string id = TheWaningBorder.Entities.BuildingIds.Of(ents[i], em);
                if (string.IsNullOrEmpty(id) || !seen.Add(id)) continue;
                _list.Add((id, ents[i]));
            }
            q.Dispose();
            _list.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        }
    }
}
