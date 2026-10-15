// FeraldisProbe.cs
// Test bed for the Feraldis redesign (docs/Design/Age_1_Feraldis.md):
//
//   Launch Feraldis Probe — from inside play mode, a 1-v-1 skirmish on
//   HollowTable that STARTS in Age 1 with every faction already Feraldis
//   (StartAgePromoter), edition B, fog off, curse on — both sides AI, the
//   editor watching as an observer. Then, every ten seconds,
//   one line per faction to the Console: resources, slaves / slave cap, slaves
//   crewing, the Forge's bays, totems and their tiers, and the Feraldis units
//   alive by id. Read the result from Editor.log after "[FeraldisProbe]".

using System.Collections.Generic;
using System.Text;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Core.Multiplayer;
using TheWaningBorder.Economy;
using TheWaningBorder.Feraldis;
using TheWaningBorder.UI.Menus;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace TheWaningBorder.EditorTools
{
    public static class FeraldisProbe
    {
        private static double _next;
        private static bool _running;

        [MenuItem("Waning Border/Debug/Launch Feraldis Probe (Age 1, Feraldis AI vs Feraldis AI)")]
        public static void Launch()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[FeraldisProbe] enter play mode (any scene) first");
                return;
            }
            GameEdition.SetEditorPick("B");

            GameSettings.IsMultiplayer = false;
            GameSettings.NetworkRole = NetworkRole.None;
            GameSettings.Mode = GameMode.FreeForAll;
            GameSettings.TotalPlayers = 2;
            LobbyConfig.SetupSinglePlayer(2);
            // Both slots are AI and the editor only watches: a human slot would
            // sit idle and turn the test into one AI against an empty base.
            for (int i = 0; i < 2; i++)
            {
                var slot = LobbyConfig.Slots[i];
                slot.Type = SlotType.AI;
                slot.AIDifficulty = LobbyAIDifficulty.Hard;
                slot.TeamIndex = Alliances.NoTeam;
                slot.StartIndex = PlayerSlot.AutoStart;
            }
            GameSettings.SelectedMapScene = "HollowTable";
            GameSettings.SpawnSeed = 20261012;
            GameSettings.FogOfWarEnabled = false;
            GameSettings.BorderEnabled = true;
            GameSettings.IsObserver = true;
            GameSettings.LocalPlayerFaction = Faction.Blue;
            GameSettings.TutorialActive = false;
            GameSettings.StartAge = SkirmishStartAge.Age2;
            GameSettings.PerFactionStartCulture = false;
            GameSettings.StartCulture = Cultures.Feraldis;
            LobbyConfig.ApplyColorSelections();

            Debug.Log("[FeraldisProbe] launching Feraldis probe on HollowTable (edition B, Age 1 Feraldis start)");
            LoadingScreen.Show(GameSettings.SelectedMapScene);

            if (!_running)
            {
                _running = true;
                _next = EditorApplication.timeSinceStartup + 15;
                EditorApplication.update += Tick;
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; _running = false; return; }
            if (EditorApplication.timeSinceStartup < _next) return;
            _next = EditorApplication.timeSinceStartup + 10;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var perFaction = new SortedDictionary<int, StringBuilder>();
            StringBuilder Line(Faction f)
            {
                if (!perFaction.TryGetValue((int)f, out var sb)) perFaction[(int)f] = sb = new StringBuilder();
                return sb;
            }

            // units by id
            var counts = new SortedDictionary<string, int>();
            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<UnitTypeId>(), ComponentType.ReadOnly<FactionTag>()))
            using (var ids = q.ToComponentDataArray<UnitTypeId>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < ids.Length; i++)
                {
                    string k = (int)facs[i].Value + ":" + ids[i].Value.ToString();
                    counts.TryGetValue(k, out int n); counts[k] = n + 1;
                }

            for (int f = 0; f < 2; f++)
            {
                var fac = (Faction)f;
                var sb = Line(fac);
                if (FactionEconomy.TryGetResources(em, fac, out var r))
                    sb.Append($"S{r.Supplies} I{r.Iron} V{r.Veilstone} St{r.Veilsteel} RP{FactionReligionPointsHelper.GetBalance(em, fac)} ");
                sb.Append($"slaves {FeraldisEconomy.SlaveCount(em, fac)}/{FeraldisEconomy.SlaveCap(em, fac)} ");
            }

            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<StaffAssignment>(), ComponentType.ReadOnly<FactionTag>()))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                var n = new int[8];
                for (int i = 0; i < facs.Length; i++) n[(int)facs[i].Value & 7]++;
                for (int f = 0; f < 2; f++) Line((Faction)f).Append($"crewing {n[f]} ");
            }

            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<GreatForgeTag>(), ComponentType.ReadOnly<FactionTag>()))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    var sb = Line(em.GetComponentData<FactionTag>(e).Value);
                    sb.Append("forge[");
                    if (em.HasBuffer<ForgeBay>(e))
                        foreach (var b in em.GetBuffer<ForgeBay>(e)) sb.Append($"{b.Recipe}L{b.Level} ");
                    if (em.HasComponent<ExtractorCrew>(e)) sb.Append($"crew {em.GetComponentData<ExtractorCrew>(e).Count}");
                    sb.Append("] ");
                }

            using (var q = em.CreateEntityQuery(ComponentType.ReadOnly<TotemLifetime>(), ComponentType.ReadOnly<FactionTag>()))
            using (var ents = q.ToEntityArray(Allocator.Temp))
                foreach (var e in ents)
                {
                    var t = em.GetComponentData<TotemLifetime>(e);
                    Line(em.GetComponentData<FactionTag>(e).Value).Append($"totem T{t.Tier}({t.Lifetime:0}) ");
                }

            foreach (var kv in counts)
            {
                int colon = kv.Key.IndexOf(':');
                int f = int.Parse(kv.Key.Substring(0, colon));
                if (f > 1) continue;
                Line((Faction)f).Append($"{kv.Key.Substring(colon + 1).Replace("Feraldis_", "")}x{kv.Value} ");
            }

            foreach (var kv in perFaction)
                if (kv.Key <= 1) Debug.Log($"[FeraldisProbe] t={Time.timeSinceLevelLoad:0}s {(Faction)kv.Key}: {kv.Value}");
        }
    }
}
