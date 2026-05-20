// HudBridge — game-state ↔ web-HUD glue.
//
// Polls the relevant ECS/Mono state on a fixed cadence and pushes JSON snapshots
// to the web HUD. Receives JS-originated clicks via HudMessage method.
//
// Topics pushed to JS (window.unityHUD.recv):
//   resources   { population:{current,max}, religion:int, supplies:{value,cap,rate}, ... }
//   objectives  [{ iconKey, name, current, total }]
//   menu        { open:bool, title, subtitle }
//   selection   null | { kind:'single', name, hp, hpMax, ... } | { kind:'multi', units }
//
// Topics received from JS (HudMessage payload.PayloadJson is JSON):
//   menu:open / menu:close / menu:item            { key }
//   sidebar:action                                { sect, variant }
//   selection:upgrade                             { id }
//   hud:ready                                     null  — flushes initial state
//
// Bindings that exist today: resources, objectives, menu (incl. ESC handling),
// selection (stub — name+count only). Minimap rendering is owned by the
// legacy MinimapRenderer on a higher-sortingOrder canvas — no bridge work
// for it. Sidebar (sects) currently shows mock data — search "SECTS-BINDING-TODO".

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Panels;
// NOTE: `TheWaningBorder.World` (terrain namespace) shadows `Unity.Entities.World`
// inside this file — a file-scope `using World = Unity.Entities.World;` alias
// loses to the namespace lookup because sibling namespaces of the enclosing
// `TheWaningBorder` are found before file-scope aliases. We fully qualify
// `Unity.Entities.World.DefaultGameObjectInjectionWorld` instead.

namespace TheWaningBorder.UI.Web
{
    public sealed class HudBridge : MonoBehaviour
    {
        public static HudBridge Instance { get; private set; }

        [Tooltip("Push cadence in Hz for cheap topics (resources/objectives/menu/selection). " +
                 "30 keeps HUD reads inside one CEF frame at 60Hz render rate so the visible " +
                 "lag between game state and HUD stays under ~50ms total.")]
        public float pushHz = 30f;

        HudWebController _ctrl;
        float _accumCheap;
        bool _jsMethodRegistered;
        readonly Dictionary<string, string> _lastJson = new();
        readonly StringBuilder _sb = new(2048);

        // Cached query — re-resolved once per world. The legacy MinimapRenderer
        // owns minimap rendering now, so unit/building queries are gone.
        EntityQuery _qNodes;
        bool _queriesBuilt;

        // Last menu state — pushed when it flips.
        bool _lastMenuOpen;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            // Cached queries are owned by us — release them so the world's
            // query cache doesn't leak entries between matches.
            if (_queriesBuilt)
            {
                try { if (_qNodes != default) _qNodes.Dispose(); } catch { }
                _queriesBuilt = false;
            }
        }

        // No Start() — controller spawn order isn't guaranteed. We bind to the
        // controller and register the JS callback the first time Update sees
        // the browser as connected.

        void EnsureJsMethodRegistered()
        {
            if (_jsMethodRegistered) return;
            if (_ctrl == null || _ctrl.Client == null) return;
            if (!_ctrl.Client.IsConnected) return;
            _ctrl.Client.RegisterJsMethod<HudMessageDto>("HudMessage", OnHudMessage);
            _jsMethodRegistered = true;
        }

        public sealed class HudMessageDto
        {
            public string Topic { get; set; }
            public string PayloadJson { get; set; }
        }

        void OnHudMessage(HudMessageDto m)
        {
            if (m == null || string.IsNullOrEmpty(m.Topic)) return;
            // Most actions are dead-simple; route by topic. JSON parsing is done
            // only when the payload carries meaningful data (defensive parse).
            switch (m.Topic)
            {
                case "hud:ready":
                    // Force a re-push of every cached topic on the next tick.
                    _lastJson.Clear();
                    _lastMenuOpen = !_lastMenuOpen; // force menu push too
                    break;

                case "menu:open":
                    UI.HUD.InGameMenuPanel.Open();
                    break;
                case "menu:close":
                    UI.HUD.InGameMenuPanel.Close();
                    break;
                case "menu:item":
                    HandleMenuItem(m.PayloadJson);
                    break;

                case "sidebar:action":
                    // SECTS-BINDING-TODO — route to sect adoption/levelup/cast handlers
                    Debug.Log($"[HudBridge] sidebar:action {m.PayloadJson} (binding TODO)");
                    break;

                case "selection:upgrade":
                    HandleSelectionUpgrade();
                    break;

                case "actions:invoke":
                    HandleActionInvoke(m.PayloadJson);
                    break;

                case "hud:capture":
                    // Pointer entered/left an interactive HUD region. Sets a
                    // flag the game-world input systems consult so a click on
                    // an HTML button doesn't ALSO process as a click on the
                    // ground (or a unit). See HudWebController.IsPointerOverWebHud.
                    HudWebController.IsPointerOverWebHud =
                        QuickField(m.PayloadJson, "capture") == "true";
                    break;

                case "culture:choose":
                    HandleCultureChoose(m.PayloadJson);
                    break;
            }
        }

        // Builder action cells in the web HUD send their building's id as `key`
        // (matching BuildCommandPanel.TriggerBuildingPlacement). When the panel
        // is showing the builder layout (`selectionKind == "builder"`) we route
        // straight to the legacy placement handler — same code path the IMGUI
        // build hotkeys used, so cost validation, ghost preview, and shift+click
        // re-entry all keep working unchanged.
        void HandleActionInvoke(string payloadJson)
        {
            var key = QuickField(payloadJson, "key");
            if (string.IsNullOrEmpty(key)) return;

            var selectionKind = QuickField(payloadJson, "selectionKind");
            if (selectionKind == "builder")
            {
                UI.Panels.BuilderCommandPanel.TriggerBuildingPlacement(key);
                return;
            }

            // Train commands — hall/barracks/shrine. Routes through the same
            // CommandRouter.IssueTrain the legacy IMGUI panel used, so the
            // lockstep queue and TrainQueueItem buffer flow stay intact.
            // `key` is the unit-def ID (e.g. "Builder", "Swordsman").
            //
            // Previously this skipped the IMGUI path's cost check / population /
            // queue-full guards entirely — orders were queued without spending
            // resources, so supplies (and iron/crystal) never decremented. The
            // IMGUI EntityActionPanel does this same dance; mirror it here.
            if (selectionKind == "hall" || selectionKind == "barracks" ||
                selectionKind == "archery" || selectionKind == "shrine")
            {
                var sel = Input.SelectionSystem.CurrentSelection;
                if (sel == null || sel.Count == 0)
                {
                    Debug.Log("[HudBridge] actions:invoke train: nothing selected");
                    return;
                }
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) return;
                var em = world.EntityManager;

                // Apply to every selected building that matches the type of
                // the first one — clicking "Archer" with 3 Archery Ranges
                // selected should queue an archer at each. Each iteration
                // pays its own cost, so the loop stops cleanly when supplies
                // / population run out.
                var first = sel[0];
                if (!em.Exists(first)) return;
                int dispatched = 0, lastFailure = 0;
                for (int idx = 0; idx < sel.Count; idx++)
                {
                    var trainBuilding = sel[idx];
                    if (!em.Exists(trainBuilding)) continue;
                    // Same-type filter — match on whichever Tag the first
                    // building carries (HallTag / BarracksTag / ArcheryRangeTag /
                    // ShrineTag / TempleOfRidanTag).
                    if (!SameTrainingType(em, first, trainBuilding)) continue;

                    Faction faction = GameSettings.LocalPlayerFaction;
                    if (em.HasComponent<FactionTag>(trainBuilding))
                        faction = em.GetComponentData<FactionTag>(trainBuilding).Value;

                    // Level gate — same authoritative check IssueTrain
                    // does. Mirrored here so we don't spend resources on
                    // a unit the building can't actually train; the JS
                    // side normally hides under-level buttons but a
                    // hotkey or stale state could still trigger one.
                    if (!Core.Commands.CommandRouter.CanTrainAtBuilding(em, trainBuilding, key, out _, out _))
                    { lastFailure = 4; continue; }

                    if (Core.Commands.CommandRouter.IsProductionQueueFull(em, trainBuilding))
                    { lastFailure = 1; continue; }

                    int popCost = TheWaningBorder.Economy.PopulationHelper.GetUnitPopulationCost(key);
                    if (!TheWaningBorder.Economy.PopulationHelper.HasPopulationCapacity(faction, popCost))
                    { lastFailure = 2; continue; }

                    var baseCost = LookupUnitCost(key);
                    var trainCost = TheWaningBorder.Economy.WarSectCostHelper
                        .MilitaryDiscount(em, faction, key, baseCost);

                    if (!TheWaningBorder.Economy.FactionEconomy.Spend(em, faction, trainCost))
                    { lastFailure = 3; continue; }

                    Core.Commands.CommandRouter.IssueTrain(em, trainBuilding, key);
                    dispatched++;
                }

                // Surface the most likely "why nothing happened" message when
                // no building accepted the order.
                if (dispatched == 0)
                {
                    switch (lastFailure)
                    {
                        case 1: UI.HUD.PlayerNotificationSystem.Notify("Production queue full"); break;
                        case 2: UI.HUD.PlayerNotificationSystem.Notify("Population cap reached"); break;
                        case 3: UI.HUD.PlayerNotificationSystem.NotifyError("Not enough resources"); break;
                        case 4: UI.HUD.PlayerNotificationSystem.Notify("Trainer building level too low"); break;
                    }
                }
                return;
            }

            // Military / multi: immediate-fire commands. Targeted ones
            // (patrol, attack-move) need a follow-up ground click, which the
            // web HUD can't drive directly — they're left as world-input
            // commands the player issues via right-click instead.
            if (selectionKind == "military" || selectionKind == "multi")
            {
                var sel = Input.SelectionSystem.CurrentSelection;
                if (sel == null || sel.Count == 0) return;
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (world == null || !world.IsCreated) return;
                var em = world.EntityManager;
                Faction localFaction = GameSettings.LocalPlayerFaction;

                switch (key)
                {
                    case "stop":
                        for (int i = 0; i < sel.Count; i++)
                        {
                            var e = sel[i];
                            if (!em.Exists(e)) continue;
                            if (em.HasComponent<FactionTag>(e) &&
                                em.GetComponentData<FactionTag>(e).Value != localFaction) continue;
                            Core.Commands.CommandRouter.IssueStop(em, e);
                        }
                        return;

                    case "hold":
                        for (int i = 0; i < sel.Count; i++)
                        {
                            var e = sel[i];
                            if (!em.Exists(e)) continue;
                            if (em.HasComponent<FactionTag>(e) &&
                                em.GetComponentData<FactionTag>(e).Value != localFaction) continue;
                            Core.Commands.CommandRouter.IssueHoldPosition(em, e);
                        }
                        return;

                    case "patrol":
                    case "attack":
                    case "formation":
                    case "retreat":
                    case "special":
                    case "stance":
                        // These commands need a world-space target click or
                        // sect-specific routing the HudBridge doesn't own.
                        // Surface a hint so the player knows the button isn't
                        // broken — it just needs a follow-up action.
                        UI.HUD.PlayerNotificationSystem.Notify($"'{key}' must be issued via right-click on the world.");
                        return;
                }
            }

            // Anything else: not wired yet — log for triage.
            Debug.Log($"[HudBridge] actions:invoke {key} (kind={selectionKind}, binding TODO)");
        }

        void HandleMenuItem(string payloadJson)
        {
            // Quick-and-dirty parse: payload is {"key":"resume|settings|save|load|surrender"}
            var key = QuickField(payloadJson, "key");
            switch (key)
            {
                case "resume":
                    UI.HUD.InGameMenuPanel.Close();
                    break;

                case "surrender":
                    // Route through VictoryConditionSystem so elimination tracking
                    // / post-game stats fire the same way the IMGUI menu does.
                    UI.HUD.InGameMenuPanel.Close();
                    if (UI.HUD.VictoryConditionSystem.Instance != null)
                    {
                        UI.HUD.VictoryConditionSystem.Instance.Surrender();
                    }
                    else if (UI.HUD.GameStatsTracker.Instance != null)
                    {
                        UI.HUD.GameStatsTracker.Instance.EndGame();
                        var statsUI = UI.HUD.PostGameStatsUI.Instance;
                        if (statsUI == null)
                        {
                            var go = new GameObject("PostGameStatsUI");
                            statsUI = go.AddComponent<UI.HUD.PostGameStatsUI>();
                        }
                        statsUI.Show();
                    }
                    break;

                case "quit":
                    // Quit to main menu — full teardown (timeScale, ECS world,
                    // RuntimeManagers) then SceneManager.LoadScene("MainMenu").
                    UI.HUD.InGameMenuPanel.QuitToMainMenu();
                    break;

                // Save / Load: hooks exist but the save system itself isn't
                // implemented yet (tracked separately). The JS menu marks
                // these items disabled so the user can't click here, but
                // if a hotkey routes through, surface a notification rather
                // than the silent-close that confused players before.
                case "save":
                case "load":
                    UI.HUD.PlayerNotificationSystem.Notify("Save / Load coming soon");
                    UI.HUD.InGameMenuPanel.Close();
                    break;

                // Settings — full settings UI is a follow-up. Same
                // treatment: notify and close instead of silent-close.
                case "settings":
                    UI.HUD.PlayerNotificationSystem.Notify("Settings menu coming soon");
                    UI.HUD.InGameMenuPanel.Close();
                    break;

                default:
                    Debug.Log($"[HudBridge] menu item '{key}' clicked (no handler)");
                    UI.HUD.InGameMenuPanel.Close();
                    break;
            }
        }

        // Trigger a building upgrade on the currently-selected building.
        // Mirrors the IMGUI EntityInfoPanel upgrade button: routes through
        // UpgradeBuildingCommandHelper.Execute, which handles cost spend +
        // queue + level bump on completion.
        void HandleSelectionUpgrade()
        {
            var sel = Input.SelectionSystem.CurrentSelection;
            if (sel == null || sel.Count == 0) return;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var e = sel[0];
            if (!em.Exists(e)) return;

            var result = TheWaningBorder.Core.Commands.Types
                .UpgradeBuildingCommandHelper.Execute(em, e);
            // UpgradeBuildingResult lives in the global namespace (alongside
            // BuildingUpgradeable), not inside the Commands.Types namespace.
            if (result != UpgradeBuildingResult.Ok)
            {
                Debug.Log($"[HudBridge] selection:upgrade failed: {result}");
            }
        }

        // Compact cost label for the selection panel's upgrade row.
        // Skips zero fields so "150s 50i" reads cleanly instead of
        // "150s 50i 0c 0v 0g". Pure scalar — safe to drop into JSX
        // as a React text node (avoids React error #31).
        static string FormatCostShort(int supplies, int iron, int crystal, int veilsteel, int glow)
        {
            var parts = new System.Text.StringBuilder(24);
            if (supplies  > 0) { if (parts.Length > 0) parts.Append(' '); parts.Append(supplies).Append('s'); }
            if (iron      > 0) { if (parts.Length > 0) parts.Append(' '); parts.Append(iron).Append('i'); }
            if (crystal   > 0) { if (parts.Length > 0) parts.Append(' '); parts.Append(crystal).Append('c'); }
            if (veilsteel > 0) { if (parts.Length > 0) parts.Append(' '); parts.Append(veilsteel).Append('v'); }
            if (glow      > 0) { if (parts.Length > 0) parts.Append(' '); parts.Append(glow).Append('g'); }
            return parts.Length == 0 ? "free" : parts.ToString();
        }

        // Look up a unit's base training cost from TechTreeDB. Returns a
        // zero Cost if the unit isn't registered — Spend will then succeed
        // trivially (matches the IMGUI fallback behaviour).
        // TechTreeDB lives at the global namespace (its UnitDef/BuildingDef
        // helpers live in TheWaningBorder.Data, but the singleton itself
        // is global to match the legacy bootstrap layout).
        static TheWaningBorder.Core.Cost LookupUnitCost(string unitId)
        {
            var db = TechTreeDB.Instance;
            if (db == null) return default;
            if (!db.TryGetUnit(unitId, out var unit) || unit.cost == null) return default;
            return new TheWaningBorder.Core.Cost
            {
                Supplies  = unit.cost.Supplies,
                Iron      = unit.cost.Iron,
                Crystal   = unit.cost.Crystal,
                Veilsteel = unit.cost.Veilsteel,
                Glow      = unit.cost.Glow,
            };
        }

        void Update()
        {
            if (_ctrl == null) _ctrl = HudWebController.Instance;
            if (_ctrl == null || !_ctrl.IsReady) return;

            EnsureJsMethodRegistered();
            EnsureQueriesBuilt();

            _accumCheap += Time.unscaledDeltaTime;
            float cheapPeriod = 1f / Mathf.Max(0.5f, pushHz);
            if (_accumCheap < cheapPeriod) return;
            _accumCheap = 0f;

            PushMenu();
            PushResources();
            PushObjectives();
            PushSelection();
            PushCosts();   // cheap: bails out once TechTreeDB-sourced costs are sent
            PushCultureChoice();
            PushBuilderState();
            PushSectsVisibility();
            PushSects();
        }

        // ─── Sects sidebar visibility ─────────────────────────────────────
        // The React sects rail (HudFrontend/src/components/Sidebar.jsx) is
        // hidden until the local faction owns a completed Temple of Ridan,
        // mirroring the IMGUI ReligionHUD's gate. We push a tiny topic so the
        // React side just toggles render — no game state needs to cross.
        void PushSectsVisibility()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            bool visible = false;
            var faction = GameSettings.LocalPlayerFaction;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using (var arr = q.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(arr[i]).Value != faction) continue;
                    if (em.HasComponent<UnderConstruction>(arr[i])) continue;
                    // Health-based double-check: a freshly created temple lands
                    // on Health.Value = 1 until BuildingConstructionSystem ticks
                    // it up. Require ≥ 80% so the sidebar appears once the
                    // temple is functionally complete, not the frame it spawns.
                    if (em.HasComponent<Health>(arr[i]))
                    {
                        var hp = em.GetComponentData<Health>(arr[i]);
                        if (hp.Max <= 0 || hp.Value * 5 < hp.Max * 4) continue;
                    }
                    visible = true;
                    break;
                }
            }

            string json = visible ? "{\"visible\":true}" : "{\"visible\":false}";
            PushIfChanged("sectsVisible", json);
        }

        // ─── Sects rail content ───────────────────────────────────────────
        // Pushes a 6-entry array of sect rows for the React rail. One entry
        // per chapel slot on the local faction's temple. Empty / building /
        // adopted slots are tagged with a `state` field so the React component
        // can render the right look (empty placeholder, progress label, or a
        // full row with active/passive/level buttons).
        //
        // Adopted entries also carry the sect's display name, active + passive
        // descriptions, and current lever levels so the rail mirrors what the
        // IMGUI ReligionHUD shows. Icon mapping per sect mirrors the lore.
        void PushSects()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            // Resolve the local faction's completed Temple of Ridan. Same gate
            // as PushSectsVisibility — without a temple, we still push a clean
            // 6-empty-slot array so the rail (if somehow visible) looks sane.
            var faction = GameSettings.LocalPlayerFaction;
            Entity temple = Entity.Null;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<TempleOfRidanTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using (var arr = q.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(arr[i]).Value != faction) continue;
                    if (em.HasComponent<UnderConstruction>(arr[i])) continue;
                    temple = arr[i];
                    break;
                }
            }

            // Pull adoption state for level numbers on adopted rows.
            SectAdoptionState adoption = default;
            bool haveAdoption = false;
            if (FactionEconomy.TryGetBank(em, faction, out var bank)
                && em.HasComponent<SectAdoptionState>(bank))
            {
                adoption = em.GetComponentData<SectAdoptionState>(bank);
                haveAdoption = true;
            }

            _sb.Clear();
            _sb.Append('[');

            for (int i = 0; i < SectConfig.MaxAdoptedSects; i++)
            {
                if (i > 0) _sb.Append(',');

                byte state = 0;
                string sectId = null;
                int progress = 0;
                if (temple != Entity.Null && em.HasBuffer<TempleChapelSlot>(temple))
                {
                    var slots = em.GetBuffer<TempleChapelSlot>(temple);
                    if (i < slots.Length)
                    {
                        var s = slots[i];
                        state = s.State;
                        sectId = s.SectId.ToString();
                        progress = s.BuildTime > 0 ? (int)(100f * s.BuildProgress / s.BuildTime) : 0;
                    }
                }

                if (state == 0 || string.IsNullOrEmpty(sectId))
                {
                    _sb.Append("{\"key\":\"empty_").Append(i).Append("\",\"state\":\"empty\"}");
                    continue;
                }

                string shortName = SectInfo.ShortName(sectId);

                if (state == 1)
                {
                    _sb.Append("{\"key\":\"").Append(EscapeJson(sectId)).Append('_').Append(i)
                       .Append("\",\"state\":\"building\",\"name\":\"")
                       .Append(EscapeJson(shortName)).Append("\",\"progress\":").Append(progress).Append('}');
                    continue;
                }

                // state == 2: adopted. Levels from PerSectState, descriptions
                // from SectInfo, icon from a per-sect mapping.
                byte passiveLv = 1, buildingLv = 1, unitLv = 1, activeLv = 1;
                if (haveAdoption)
                {
                    var per = adoption.Get(sectId);
                    passiveLv  = per.PassiveLevel;
                    buildingLv = per.BuildingLevel;
                    unitLv     = per.UnitLevel;
                    activeLv   = per.ActivePowerLevel;
                }
                int totalLv = passiveLv + buildingLv + unitLv + activeLv; // 4..12
                string activeIcon = SectIconKey(sectId);

                _sb.Append("{\"key\":\"").Append(EscapeJson(sectId))
                   .Append("\",\"state\":\"adopted\",\"name\":\"").Append(EscapeJson(shortName))
                   .Append("\",\"level\":").Append(totalLv)
                   .Append(",\"maxLevel\":12")
                   .Append(",\"cost\":\"").Append(SectConfig.UpgradeCost(passiveLv) > 0
                       ? SectConfig.UpgradeCost(passiveLv) + " RP"
                       : "Maxed")
                   .Append("\",\"active\":{\"icon\":\"").Append(activeIcon)
                   .Append("\",\"label\":\"").Append(EscapeJson(ActiveLabelOf(sectId)))
                   .Append("\",\"hint\":\"").Append(EscapeJson(SectInfo.ActivePowerDescription(sectId)))
                   .Append("\"},\"passive\":{\"label\":\"").Append(EscapeJson(PassiveLabelOf(sectId)))
                   .Append("\",\"hint\":\"").Append(EscapeJson(SectInfo.PassiveDescription(sectId)))
                   .Append("\"}}");
            }

            _sb.Append(']');
            PushIfChanged("sects", _sb.ToString());
        }

        // Per-sect icon mapping — uses the HexIcon kinds defined in Sidebar.jsx
        // (castle, sword, rune, star, banner, scroll, eye). Picked thematically.
        static string SectIconKey(string sectId) => sectId switch
        {
            SectConfig.Antiquity   => "scroll",
            SectConfig.Renewal     => "castle",
            SectConfig.Fortitude   => "castle",
            SectConfig.Reclamation => "castle",
            SectConfig.Silence     => "eye",
            SectConfig.Justice     => "sword",
            SectConfig.Veneration  => "star",
            SectConfig.Witness     => "eye",
            SectConfig.War         => "sword",
            SectConfig.Ash         => "star",
            SectConfig.Ruin        => "banner",
            SectConfig.Wrath       => "banner",
            _                      => "rune",
        };

        // Short one-or-two-word names that fit the hex button kicker line.
        static string ActiveLabelOf(string sectId) => sectId switch
        {
            SectConfig.Antiquity   => "Reveal",
            SectConfig.Renewal     => "Heal",
            SectConfig.Fortitude   => "Bulwark",
            SectConfig.Reclamation => "Reclaim",
            SectConfig.Silence     => "Whisper-Wind",
            SectConfig.Justice     => "Sentence",
            SectConfig.Veneration  => "Litany",
            SectConfig.Witness     => "Gaze",
            SectConfig.War         => "Bloodfury",
            SectConfig.Ash         => "Burning Ground",
            SectConfig.Ruin        => "Profane Strike",
            SectConfig.Wrath       => "Spawn Pyre",
            _                      => "Active",
        };

        static string PassiveLabelOf(string sectId) => sectId switch
        {
            SectConfig.Antiquity   => "Tally of the Lost",
            SectConfig.Renewal     => "Hands That Mend",
            SectConfig.Fortitude   => "Veiled Stone",
            SectConfig.Reclamation => "Curse-Hardened",
            SectConfig.Silence     => "Steadfast Vigil",
            SectConfig.Justice     => "Marked for Sentence",
            SectConfig.Veneration  => "Fervor",
            SectConfig.Witness     => "All-Seeing",
            SectConfig.War         => "Forged in Battle",
            SectConfig.Ash         => "Pyre's Promise",
            SectConfig.Ruin        => "Profane Hands",
            SectConfig.Wrath       => "Spite of the Forsaken",
            _                      => "Passive",
        };

        // Minimal JSON string escape — handles the characters that actually
        // appear in our sect copy (backslash, quote, newline). Everything else
        // in our generated descriptions is plain ASCII.
        static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { '\\', '"', '\n', '\r', '\t' }) < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:   sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // ─── Builder stage ───────────────────────────────────────────────
        // Reports which catalog the builder action panel should show:
        //   start    → no special (Shrine/Vault/Keep) started yet — full 7-button menu with all specials
        //   placing  → a special has been started (or completed) — basics only, specials gone
        //   era2     → culture chosen and age-up complete — advanced post-age-up set
        //
        // Same Hall query the culture picker uses; we look at FactionProgress.Culture
        // to detect age-up completion and BuildingFactory.GetFactionChoiceBuilding
        // to detect "any special started" (returns non-null even when the
        // choice is still under construction).
        void PushBuilderState()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (_qHall == default)
                _qHall = em.CreateEntityQuery(
                    ComponentType.ReadOnly<HallTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<FactionProgress>());

            var faction = GameSettings.LocalPlayerFaction;
            byte currentCulture = Cultures.None;
            using (var halls = _qHall.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < halls.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(halls[i]).Value != faction) continue;
                    currentCulture = em.GetComponentData<FactionProgress>(halls[i]).Culture;
                    break;
                }
            }

            string stage;
            if (currentCulture != Cultures.None)
            {
                stage = "era2";
            }
            else
            {
                // GetFactionChoiceBuilding returns the in-progress OR completed
                // choice building id; null means none started yet.
                bool anySpecial = TheWaningBorder.Entities.BuildingFactory
                    .GetFactionChoiceBuilding(em, faction) != null;
                stage = anySpecial ? "placing" : "start";
            }

            PushIfChanged("builderState", "{\"stage\":\"" + stage + "\"}");
        }

        // ─── Culture choice ──────────────────────────────────────────────
        // Drives the "Choose Culture" button + modal at the top of the web HUD.
        // Replaces the old "click Hall → Advance to Era 2 button" flow:
        // the player now opens a dedicated picker once the prerequisites
        // (completed Shrine/Vault/Keep + age-up cost) are met.
        //
        // State pushed:
        //   { active, available, canAfford, lacking:{supplies,iron,...},
        //     cost:{supplies,iron,crystal}, inProgress, progress, remaining,
        //     duration, ageUpCulture, currentCulture }
        EntityQuery _qHall;
        void PushCultureChoice()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            // Cache the Hall query — same chunk shape used elsewhere.
            if (_qHall == default)
                _qHall = em.CreateEntityQuery(
                    ComponentType.ReadOnly<HallTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<FactionProgress>());

            var faction = GameSettings.LocalPlayerFaction;
            Entity hall = Entity.Null;
            byte currentCulture = Cultures.None;
            using (var halls = _qHall.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < halls.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(halls[i]).Value != faction) continue;
                    hall = halls[i];
                    currentCulture = em.GetComponentData<FactionProgress>(halls[i]).Culture;
                    break;
                }
            }

            // No Hall yet, or culture already chosen → button hides entirely.
            if (hall == Entity.Null || currentCulture != Cultures.None)
            {
                PushIfChanged("cultureChoice", "{\"active\":false}");
                return;
            }

            // Age-up in progress on this Hall → drive the progress bar.
            bool inProgress = em.HasComponent<AgeUpState>(hall);
            float progress = 0f, remaining = 0f, duration = 0f;
            byte ageUpCulture = Cultures.None;
            if (inProgress)
            {
                var s = em.GetComponentData<AgeUpState>(hall);
                duration = s.Duration;
                remaining = s.Remaining;
                ageUpCulture = s.Culture;
                progress = duration > 0f ? Mathf.Clamp01((duration - remaining) / duration) : 0f;
            }

            string choiceBuilding = TheWaningBorder.Entities.BuildingFactory
                .GetCompletedFactionChoiceBuilding(em, faction);
            bool available = choiceBuilding != null;

            var cost = CultureConfig.AgeUpCost;
            bool canAfford = TheWaningBorder.Economy.FactionEconomy.CanAfford(em, faction, cost);

            // Per-resource shortage flags (mirrors Actions.jsx affordability).
            bool lackSupplies = false, lackIron = false, lackCrystal = false;
            if (!canAfford && FactionResourcesHelper.TryGetFactionResources(faction, out var r))
            {
                if (cost.Supplies > r.Supplies) lackSupplies = true;
                if (cost.Iron     > r.Iron)     lackIron     = true;
                if (cost.Crystal  > r.Crystal)  lackCrystal  = true;
            }

            _sb.Clear();
            _sb.Append('{');
            _sb.Append("\"active\":true");
            _sb.Append(",\"available\":").Append(available ? "true" : "false");
            _sb.Append(",\"canAfford\":").Append(canAfford ? "true" : "false");
            _sb.Append(",\"inProgress\":").Append(inProgress ? "true" : "false");
            _sb.Append(",\"progress\":").Append(progress.ToString("F3", CultureInfo.InvariantCulture));
            _sb.Append(",\"remaining\":").Append(((int)remaining));
            _sb.Append(",\"duration\":").Append(((int)duration));
            _sb.Append(",\"ageUpCulture\":\"").Append(NameOfCulture(ageUpCulture)).Append('"');
            _sb.Append(",\"cost\":{")
                .Append("\"supplies\":").Append(cost.Supplies)
                .Append(",\"iron\":").Append(cost.Iron)
                .Append(",\"crystal\":").Append(cost.Crystal)
                .Append('}');
            _sb.Append(",\"lacking\":{")
                .Append("\"supplies\":").Append(lackSupplies ? "true" : "false")
                .Append(",\"iron\":").Append(lackIron ? "true" : "false")
                .Append(",\"crystal\":").Append(lackCrystal ? "true" : "false")
                .Append('}');
            _sb.Append('}');
            PushIfChanged("cultureChoice", _sb.ToString());
        }

        static string NameOfCulture(byte c) => c switch
        {
            Cultures.Runai    => "Runai",
            Cultures.Alanthor => "Alanthor",
            Cultures.Feraldis => "Feraldis",
            _ => "None",
        };

        static byte CultureFromName(string name) => name switch
        {
            "Runai"    => Cultures.Runai,
            "Alanthor" => Cultures.Alanthor,
            "Feraldis" => Cultures.Feraldis,
            _          => Cultures.None,
        };

        // Player picked a culture in the web HUD modal. Same routing the
        // legacy popup used: prime CultureChoicePopup's static context
        // (hall + faction) then commit. CommitAgeUpStatic spends the
        // resources and stamps AgeUpState on the Hall.
        void HandleCultureChoose(string payloadJson)
        {
            var name = QuickField(payloadJson, "culture");
            byte culture = CultureFromName(name);
            if (culture == Cultures.None) return;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var faction = GameSettings.LocalPlayerFaction;
            Entity hall = Entity.Null;
            using (var halls = _qHall.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < halls.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(halls[i]).Value != faction) continue;
                    hall = halls[i];
                    break;
                }
            }
            if (hall == Entity.Null) return;

            UI.Panels.CultureChoicePopup.Show(hall, faction);
            UI.Panels.CultureChoicePopup.CommitAgeUpStatic(culture);
        }

        // ─── Costs ────────────────────────────────────────────────────────
        // Real per-id cost lookup pushed once to JS so the action panel can
        // display + gate buttons against actual cost data — not the placeholder
        // amounts hardcoded in Actions.jsx catalogs. Shape:
        //   { "Hut": {supplies:50}, "Barracks": {supplies:150,iron:70}, ... }
        // Costs don't change at runtime so we push exactly once per session.
        bool _costsPushed;
        void PushCosts()
        {
            if (_costsPushed) return;
            var db = TechTreeDB.Instance;
            if (db == null) return;

            _sb.Clear();
            _sb.Append('{');
            bool first = true;
            foreach (var b in db.GetAllBuildings())
            {
                if (b == null || b.id == null) continue;
                if (!AppendCostEntry(b.id, b.cost, ref first)) continue;
            }
            foreach (var u in db.GetAllUnits())
            {
                if (u == null || u.id == null) continue;
                AppendCostEntry(u.id, u.cost, ref first);
            }
            _sb.Append('}');
            PushIfChanged("costs", _sb.ToString());
            _costsPushed = true;
        }

        bool AppendCostEntry(string id, TheWaningBorder.Data.CostBlock c, ref bool first)
        {
            if (c == null) return false;
            if (c.Supplies <= 0 && c.Iron <= 0 && c.Crystal <= 0 && c.Veilsteel <= 0 && c.Glow <= 0)
                return false;  // skip "free" entries — Hall has cost=0 in JSON
            if (!first) _sb.Append(',');
            first = false;
            _sb.Append('"').Append(JsonEscape(id)).Append("\":{");
            bool fieldFirst = true;
            AppendField("supplies",  c.Supplies,  ref fieldFirst);
            AppendField("iron",      c.Iron,      ref fieldFirst);
            AppendField("crystal",   c.Crystal,   ref fieldFirst);
            AppendField("veilsteel", c.Veilsteel, ref fieldFirst);
            AppendField("glow",      c.Glow,      ref fieldFirst);
            _sb.Append('}');
            return true;
        }

        void AppendField(string key, int value, ref bool first)
        {
            if (value <= 0) return;
            if (!first) _sb.Append(',');
            first = false;
            _sb.Append('"').Append(key).Append("\":").Append(value);
        }

        void EnsureQueriesBuilt()
        {
            if (_queriesBuilt) return;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            _qNodes = em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalMainNodeTag>(),
                ComponentType.ReadOnly<CrystalNodeState>());
            _queriesBuilt = true;
        }

        // ─── Resources ────────────────────────────────────────────────────
        //
        // Per-minute income rate is a gross 1-minute sliding window: every 5s
        // we snapshot the current totals, diff against the previous snapshot,
        // store only the POSITIVE deltas in a 12-slot ring buffer (12 × 5s =
        // 60s), and emit the buffer's sum as `rate`. This captures all income
        // sources uniformly — trickle, tick, plunder, trade, mining, walls —
        // because we measure the totals themselves rather than instrumenting
        // each source. Spending (negative deltas) is ignored so the readout
        // is gross income, not net cashflow.

        const int RateBuckets = 12;       // 12 × 5s = 60s window
        const float RateSampleSeconds = 5f;
        int[,] _rateRing = new int[7, RateBuckets]; // 7 resources × 12 buckets
        int[]  _ratePrev = new int[7];     // last sampled totals (for delta)
        int    _rateHead;                  // next bucket index to overwrite
        float  _rateAccum;                 // time since last sample
        bool   _rateSeeded;                // skip first delta (no baseline)

        // Resource indices into _rateRing / _ratePrev. Keep in sync with the
        // JSON field order below.
        const int RPop = 0, RRel = 1, RSup = 2, RIro = 3, RVst = 4, RVsl = 5, RGlw = 6;

        void PushResources()
        {
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld?.EntityManager;
            if (em == null) return;

            var faction = GameSettings.LocalPlayerFaction;
            if (!FactionResourcesHelper.TryGetFactionResources(faction, out var r))
                return;
            PopulationHelper.TryGetFactionPopulation(faction, out int popCur, out int popMax);
            int religion = FactionReligionPointsHelper.GetBalance(em.Value, faction);

            // Advance the rate sampler. PushResources runs at pushHz (4Hz =
            // 250ms), so RateSampleSeconds=5 means we record a new bucket
            // every ~20 ticks. The ring resets when the world is recreated
            // (returning to menu + new game) via _rateSeeded.
            _rateAccum += 1f / Mathf.Max(0.5f, pushHz);
            if (_rateAccum >= RateSampleSeconds)
            {
                _rateAccum = 0f;
                SampleRateBucket(popCur, religion, r);
            }

            int[] rates = new int[7];
            for (int i = 0; i < 7; i++)
                for (int b = 0; b < RateBuckets; b++)
                    rates[i] += _rateRing[i, b];

            _sb.Clear();
            _sb.Append('{');
            _sb.Append("\"population\":{\"value\":").Append(popCur)
                .Append(",\"cap\":").Append(popMax).Append(",\"rate\":").Append(rates[RPop]).Append("},");
            // Religion ("Religion Points") only exists after a culture is
            // picked (Age 0 has no sects, so the bar would always read 0
            // with no income source). The JSX panel reads `hidden` and skips
            // the row entirely instead of showing a dead 0/999 counter.
            bool religionHidden = !TryGetFactionCulture(em.Value, faction, out byte cultureForRel)
                                  || cultureForRel == Cultures.None;
            _sb.Append("\"religion\":{\"value\":").Append(religion)
                .Append(",\"cap\":0,\"rate\":").Append(rates[RRel])
                .Append(",\"hidden\":").Append(religionHidden ? "true" : "false")
                .Append("},");
            _sb.Append("\"supplies\":{\"value\":").Append(r.Supplies)
                .Append(",\"cap\":0,\"rate\":").Append(rates[RSup]).Append("},");
            _sb.Append("\"iron\":{\"value\":").Append(r.Iron)
                .Append(",\"cap\":0,\"rate\":").Append(rates[RIro]).Append("},");
            _sb.Append("\"veilstone\":{\"value\":").Append(r.Crystal)
                .Append(",\"cap\":0,\"rate\":").Append(rates[RVst]).Append("},");
            _sb.Append("\"veilsteel\":{\"value\":").Append(r.Veilsteel)
                .Append(",\"cap\":0,\"rate\":").Append(rates[RVsl]).Append("},");
            _sb.Append("\"glow\":{\"value\":").Append(r.Glow)
                .Append(",\"cap\":0,\"rate\":").Append(rates[RGlw]).Append('}');
            _sb.Append('}');
            PushIfChanged("resources", _sb.ToString());
        }

        void SampleRateBucket(int pop, int rel, FactionResources r)
        {
            int[] now = { pop, rel, r.Supplies, r.Iron, r.Crystal, r.Veilsteel, r.Glow };
            if (!_rateSeeded)
            {
                // First sample — no previous snapshot, can't compute delta.
                // Just seed the baseline and bail.
                for (int i = 0; i < 7; i++) _ratePrev[i] = now[i];
                _rateSeeded = true;
                return;
            }
            for (int i = 0; i < 7; i++)
            {
                int delta = now[i] - _ratePrev[i];
                _ratePrev[i] = now[i];
                // Positive-only: spending shows as a negative delta but we
                // want gross income, not net cashflow.
                _rateRing[i, _rateHead] = delta > 0 ? delta : 0;
            }
            _rateHead = (_rateHead + 1) % RateBuckets;
        }

        // ─── Objectives ───────────────────────────────────────────────────
        void PushObjectives()
        {
            if (!_queriesBuilt) return;
            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld?.EntityManager;
            if (em == null) return;

            int totalNodes = 0, cleansedOrConverted = 0;
            using var entities = _qNodes.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                totalNodes++;
                var s = em.Value.GetComponentData<CrystalNodeState>(entities[i]);
                if (s.State != NodeState.Active) cleansedOrConverted++;
            }

            _sb.Clear();
            _sb.Append('[');
            _sb.Append("{\"iconKey\":\"curse\",\"name\":\"Purify or convert nodes\",")
                .Append("\"current\":").Append(cleansedOrConverted)
                .Append(",\"total\":").Append(totalNodes).Append('}');
            // Enemy-players objective stays as a stub — wire to real elimination tracker.
            _sb.Append(",{\"iconKey\":\"enemy\",\"name\":\"Defeat enemy players\",")
                .Append("\"current\":0,\"total\":3}");
            _sb.Append(']');
            PushIfChanged("objectives", _sb.ToString());
        }

        // ─── Menu ──────────────────────────────────────────────────────────
        void PushMenu()
        {
            bool open = UI.HUD.InGameMenuPanel.IsOpen;
            if (open == _lastMenuOpen) return;
            _lastMenuOpen = open;
            string json = open
                ? "{\"open\":true,\"title\":\"Armistice\",\"subtitle\":\"\"}"
                : "{\"open\":false}";
            PushIfChanged("menu", json);
        }

        // ─── Selection ────────────────────────────────────────────────────
        //
        // Single-selection: full payload — name, class, portrait, HP, atk/def/spd
        // pulled from EntityInfoExtractor (same source the legacy IMGUI panel
        // used, so naming/portrait/stats stay consistent).
        //
        // Multi-selection: grouped by unit display name. e.g. 3 Swordsmen + 2
        // Archers → `[{name:"Swordsman",count:3,hp:0.92}, {name:"Archer",count:2,hp:1.00}]`
        // where `hp` is the group's aggregate HP fraction (sum-current / sum-max).

        // Reusable group accumulator — avoids per-tick allocations of a Dictionary
        // and KeyValuePairs while still grouping by display name. Cleared in-place.
        readonly Dictionary<string, SelGroup> _selGroups = new();
        struct SelGroup
        {
            public int Count;
            public float HpSum;
            public float HpMaxSum;
            public bool IsBuilding;
            public Entity Representative;
        }
        // Filtered selection scratch list — survives across PushSelection calls
        // to avoid GC churn at 4 Hz.
        readonly System.Collections.Generic.List<Entity> _filteredSel = new();

        void PushSelection()
        {
            var sel = Input.SelectionSystem.CurrentSelection;
            if (sel == null || sel.Count == 0)
            {
                PushIfChanged("selection", "null");
                return;
            }

            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld?.EntityManager;
            if (em == null) return;
            var emm = em.Value;

            // Filter out battalion leaders — they carry an invisible dummy HP
            // component, so the player must see the members instead. The
            // SelectionSystem keeps the leader bundled with its members for
            // command-dispatch purposes, but the HUD always summarises a
            // battalion via the members themselves.
            _filteredSel.Clear();
            for (int i = 0; i < sel.Count; i++)
            {
                var e = sel[i];
                if (!emm.Exists(e)) continue;
                if (emm.HasComponent<BattalionLeader>(e)) continue;
                _filteredSel.Add(e);
            }
            if (_filteredSel.Count == 0)
            {
                PushIfChanged("selection", "null");
                return;
            }

            // Bucket by display name so 15 archers collapse into one group.
            // This is the same pass used by both "single" (1 group) and
            // "multi" (2+ groups) emit paths.
            _selGroups.Clear();
            for (int i = 0; i < _filteredSel.Count; i++)
            {
                var e = _filteredSel[i];
                if (!emm.Exists(e)) continue;
                var info = EntityInfoExtractor.GetDisplayInfo(e, emm);
                string key = info.Name ?? "Unit";
                bool isBld = emm.HasComponent<BuildingTag>(e);
                if (!_selGroups.TryGetValue(key, out var g))
                {
                    g.Representative = e;
                }
                g.Count++;
                g.HpSum += info.CurrentHealth ?? 0;
                g.HpMaxSum += Mathf.Max(1, info.MaxHealth ?? 0);
                g.IsBuilding = isBld;
                _selGroups[key] = g;
            }

            // Same-type collapse: 1 group of N entities is presented as a
            // SINGLE selection with `count` set so the Actions panel routes
            // to the type-specific layout (archers → Archer actions, etc.)
            // and applies orders to every entity in the group.
            if (_selGroups.Count == 1)
            {
                var only = _selGroups.GetEnumerator();
                only.MoveNext();
                EmitSingle(emm, only.Current.Key, only.Current.Value);
                return;
            }

            // True multi (2+ types): render the mixed-detachment card.
            _sb.Clear();
            _sb.Append("{\"kind\":\"multi\",\"units\":[");
            bool first = true;
            foreach (var kv in _selGroups)
            {
                if (!first) _sb.Append(',');
                first = false;
                var g = kv.Value;
                float hpFrac = g.HpMaxSum > 0f ? g.HpSum / g.HpMaxSum : 1f;
                _sb.Append("{\"key\":\"").Append(JsonEscape(kv.Key))
                    .Append("\",\"name\":\"").Append(JsonEscape(kv.Key))
                    .Append("\",\"portrait\":\"").Append(PortraitFor(kv.Key, g.IsBuilding))
                    .Append("\",\"count\":").Append(g.Count)
                    .Append(",\"hp\":").Append(hpFrac.ToString("F2", CultureInfo.InvariantCulture))
                    .Append('}');
            }
            _sb.Append("]}");
            PushIfChanged("selection", _sb.ToString());
        }

        // Emit one "single" selection payload representing a same-type group.
        // For solo entities Count==1 and the count field is omitted (the JSX
        // shows it only when > 1). For groups, the representative entity
        // sources the upgrade / training / class data so the action panel
        // picks the right layout.
        void EmitSingle(EntityManager emm, string displayName, SelGroup g)
        {
            var e = g.Representative;
            var info = EntityInfoExtractor.GetDisplayInfo(e, emm);
            bool isBuilding = emm.HasComponent<BuildingTag>(e);
            Faction fac = emm.HasComponent<FactionTag>(e)
                ? emm.GetComponentData<FactionTag>(e).Value
                : Faction.Blue;
            string tone = fac == GameSettings.LocalPlayerFaction ? "own" : "enemy";

            // For groups, use the aggregate HP fraction instead of one
            // entity's HP — that's the count-aware health the player wants.
            int hpCur, hpMax;
            if (g.Count > 1)
            {
                hpCur = (int)g.HpSum;
                hpMax = (int)Mathf.Max(1f, g.HpMaxSum);
            }
            else
            {
                hpCur = info.CurrentHealth ?? 0;
                hpMax = Mathf.Max(1, info.MaxHealth ?? 0);
            }

            _sb.Clear();
            _sb.Append("{\"kind\":\"single\",\"id\":").Append(e.Index)
                .Append(",\"name\":\"").Append(JsonEscape(displayName))
                .Append("\",\"klass\":\"").Append(JsonEscape(isBuilding ? "Structure" : (info.Type ?? "Combatant")))
                .Append("\",\"portrait\":\"").Append(PortraitFor(displayName, isBuilding))
                .Append("\",\"portraitTone\":\"").Append(tone).Append('"')
                .Append(",\"hp\":").Append(hpCur)
                .Append(",\"hpMax\":").Append(hpMax)
                .Append(",\"sh\":0,\"shMax\":0");
            if (g.Count > 1) _sb.Append(",\"count\":").Append(g.Count);

            if (info.HasCombatStats)
            {
                _sb.Append(",\"atk\":{\"value\":").Append(info.Attack ?? 0).Append(",\"kind\":\"Damage\"}")
                    .Append(",\"def\":{\"value\":").Append(info.Defense ?? 0).Append(",\"kind\":\"Armor\"}")
                    .Append(",\"spd\":{\"value\":").Append((info.Speed ?? 0f).ToString("F1", CultureInfo.InvariantCulture)).Append(",\"kind\":\"Move\"}");
            }
            else
            {
                _sb.Append(",\"atk\":{\"value\":0,\"kind\":\"—\"}")
                    .Append(",\"def\":{\"value\":0,\"kind\":\"—\"}")
                    .Append(",\"spd\":{\"value\":0,\"kind\":\"—\"}");
            }

            // Progress bar — buildings that are currently training a unit OR
            // upgrading. The same payload feeds both the Selection panel's
            // progress strip and the floating-bar progress under the
            // health bar. progress.ratio is 0..1, progress.label is "Building
            // Archer" / "Upgrading (lvl 2)" etc.
            bool emittedProgress = false;
            if (isBuilding)
            {
                if (emm.HasComponent<BuildingUpgrading>(e))
                {
                    var up = emm.GetComponentData<BuildingUpgrading>(e);
                    if (up.Total > 0f)
                    {
                        float r = Mathf.Clamp01(up.Progress / up.Total);
                        _sb.Append(",\"progress\":{\"ratio\":").Append(r.ToString("F3", CultureInfo.InvariantCulture))
                            .Append(",\"label\":\"Upgrading (lvl ").Append(up.TargetLevel).Append(")\"")
                            .Append(",\"kind\":\"upgrade\"}");
                        emittedProgress = true;
                    }
                }
                else if (emm.HasComponent<TrainingState>(e))
                {
                    var ts = emm.GetComponentData<TrainingState>(e);
                    if (ts.Busy == 1 && ts.Total > 0f)
                    {
                        float r = Mathf.Clamp01((ts.Total - ts.Remaining) / ts.Total);
                        // Peek at the front of the train queue for the unit name.
                        string trainName = "Unit";
                        if (emm.HasBuffer<TrainQueueItem>(e))
                        {
                            var buf = emm.GetBuffer<TrainQueueItem>(e);
                            if (buf.Length > 0) trainName = buf[0].UnitId.ToString();
                        }
                        _sb.Append(",\"progress\":{\"ratio\":").Append(r.ToString("F3", CultureInfo.InvariantCulture))
                            .Append(",\"label\":\"Training ").Append(JsonEscape(trainName)).Append("\"")
                            .Append(",\"kind\":\"training\"}");
                        emittedProgress = true;
                    }
                }
            }
            if (!emittedProgress) _sb.Append(",\"progress\":null");

            // Building upgrade hint — surfaces an "Ascend" button. Hidden
            // while an upgrade is already in flight (BuildingUpgrading) — the
            // progress bar above tells the same story.
            bool canUpgrade = false;
            int upgSupplies = 0, upgIron = 0, upgCrystal = 0, upgVeilsteel = 0, upgGlow = 0;
            int upgNextLevel = 0;
            if (isBuilding && fac == GameSettings.LocalPlayerFaction
                && emm.HasComponent<BuildingUpgradeable>(e)
                && !emm.HasComponent<BuildingUpgrading>(e)
                && !emm.HasComponent<UnderConstruction>(e))
            {
                if (TheWaningBorder.Core.Commands.Types.UpgradeBuildingCommandHelper
                        .TryGetNextCost(emm, e, out var upgCost, out byte nextLv))
                {
                    canUpgrade = true;
                    upgSupplies  = upgCost.Supplies;
                    upgIron      = upgCost.Iron;
                    upgCrystal   = upgCost.Crystal;
                    upgVeilsteel = upgCost.Veilsteel;
                    upgGlow      = upgCost.Glow;
                    upgNextLevel = nextLv;
                }
            }
            _sb.Append(",\"canUpgrade\":").Append(canUpgrade ? "true" : "false");
            if (canUpgrade)
            {
                _sb.Append(",\"upgradeNextLevel\":").Append(upgNextLevel);
                _sb.Append(",\"upgradeCost\":\"")
                    .Append(JsonEscape(FormatCostShort(upgSupplies, upgIron, upgCrystal, upgVeilsteel, upgGlow)))
                    .Append('"');
                _sb.Append(",\"upgradeCostBreakdown\":{")
                    .Append("\"supplies\":").Append(upgSupplies)
                    .Append(",\"iron\":").Append(upgIron)
                    .Append(",\"crystal\":").Append(upgCrystal)
                    .Append(",\"veilsteel\":").Append(upgVeilsteel)
                    .Append(",\"glow\":").Append(upgGlow)
                    .Append('}');
            }

            // Training roster — only for own buildings. Single source of
            // truth: EntityExtractors.GetTrainingActions applies the same
            // culture + minBuildingLevel + cost filters the IMGUI panel
            // uses. The JS frontend renders these directly instead of its
            // own static TRAIN_* lists, so newly-unlocked units (e.g.
            // Crossbowman at Practice Range L2) appear the moment the
            // upgrade lands.
            if (isBuilding && fac == GameSettings.LocalPlayerFaction)
            {
                var actions = TheWaningBorder.UI.EntityActionExtractor.GetTrainingActions(e, emm);
                _sb.Append(",\"actions\":[");
                for (int i = 0; i < actions.Count; i++)
                {
                    var a = actions[i];
                    if (i > 0) _sb.Append(',');
                    _sb.Append("{\"key\":\"").Append(JsonEscape(a.Id))
                        .Append("\",\"label\":\"").Append(JsonEscape(a.Label))
                        .Append("\",\"tooltip\":\"").Append(JsonEscape(a.Tooltip ?? string.Empty))
                        .Append("\",\"enabled\":").Append(a.Enabled ? "true" : "false")
                        .Append(",\"canAfford\":").Append(a.CanAfford ? "true" : "false")
                        .Append(",\"cost\":{")
                        .Append("\"supplies\":").Append(a.Cost.Supplies)
                        .Append(",\"iron\":").Append(a.Cost.Iron)
                        .Append(",\"crystal\":").Append(a.Cost.Crystal)
                        .Append(",\"veilsteel\":").Append(a.Cost.Veilsteel)
                        .Append(",\"glow\":").Append(a.Cost.Glow)
                        .Append("}}");
                }
                _sb.Append(']');
            }

            _sb.Append('}');
            PushIfChanged("selection", _sb.ToString());
        }

        // True when both entities share the same trainer tag (HallTag,
        // BarracksTag, ArcheryRangeTag, ShrineTag, TempleOfRidanTag, etc.).
        // Drives "apply training to every selected building of this type"
        // dispatch — clicking "Archer" with 3 Archery Ranges selected fires
        // the train order on each range, but ignores any non-Archery
        // building that happened to be in the selection.
        static bool SameTrainingType(EntityManager em, Entity a, Entity b)
        {
            if (em.HasComponent<HallTag>(a))         return em.HasComponent<HallTag>(b);
            if (em.HasComponent<BarracksTag>(a))     return em.HasComponent<BarracksTag>(b);
            if (em.HasComponent<ArcheryRangeTag>(a)) return em.HasComponent<ArcheryRangeTag>(b);
            if (em.HasComponent<ShrineTag>(a))       return em.HasComponent<ShrineTag>(b);
            if (em.HasComponent<TempleOfRidanTag>(a))return em.HasComponent<TempleOfRidanTag>(b);
            return a.Index == b.Index;
        }

        // Look up the faction's culture via its Hall's FactionProgress. Returns
        // false if no Hall exists yet (very early bootstrap). The HudBridge uses
        // this to hide the Religion resource row before age-up, since no sects
        // exist and the bar would always read 0.
        bool TryGetFactionCulture(EntityManager em, Faction faction, out byte culture)
        {
            culture = Cultures.None;
            // _qHall is lazy-initialised by other push methods (PushBuilderState,
            // PushAgeUpProgress, PushSelection's culture sub-query). PushResources
            // can fire before any of those during the first few frames, so
            // ensure the query exists before we touch it — without this guard
            // .ToEntityArray hits a default-struct NRE.
            if (_qHall == default)
                _qHall = em.CreateEntityQuery(
                    ComponentType.ReadOnly<HallTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<FactionProgress>());

            using var halls = _qHall.ToEntityArray(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < halls.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(halls[i]).Value != faction) continue;
                culture = em.GetComponentData<FactionProgress>(halls[i]).Culture;
                return true;
            }
            return false;
        }

        // Mapping from display name → portrait kind known to Selection.jsx
        // (knight / archer / mason / forge / behemoth). Buildings always use
        // the forge glyph; everything else falls back to "knight".
        static string PortraitFor(string name, bool isBuilding)
        {
            if (isBuilding) return "forge";
            if (string.IsNullOrEmpty(name)) return "knight";
            string n = name.ToLowerInvariant();
            if (n.Contains("archer") || n.Contains("ranger") || n.Contains("veilstinger")) return "archer";
            if (n.Contains("builder") || n.Contains("miner") || n.Contains("mason")) return "mason";
            if (n.Contains("siege") || n.Contains("behemoth") || n.Contains("godsplinter")) return "behemoth";
            return "knight";
        }

        // Minimap rendering is owned by the legacy MinimapRenderer now; no
        // C#→JS pushes needed for unit / building / viewport state.

        // ─── Push helpers ─────────────────────────────────────────────────
        void PushIfChanged(string topic, string payloadJson)
        {
            if (_lastJson.TryGetValue(topic, out var prev) && prev == payloadJson) return;
            _lastJson[topic] = payloadJson;
            _ctrl.Push(topic, payloadJson);
        }

        static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // Escape every char JSON spec requires (backslash, quote, the
            // C0 controls). Unit tooltips contain '\n' between lines and
            // any unescaped control char makes the JS-evaluated payload
            // a syntax error — UWB's ExecuteJs just dumps our payload
            // straight into a JS string literal, so \n in the source
            // becomes a real newline and breaks the parser.
            var sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"':  sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // Tiny, defensive JSON field extractors. The JS side only ever sends
        // single-level objects with primitive values for these topics — full
        // JSON parsing is overkill.
        static string QuickField(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int i = json.IndexOf("\"" + key + "\"");
            if (i < 0) return null;
            i = json.IndexOf(':', i);
            if (i < 0) return null;
            i++; while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) return null;
            if (json[i] == '"')
            {
                int end = json.IndexOf('"', i + 1);
                return end > i ? json.Substring(i + 1, end - i - 1) : null;
            }
            int e = i;
            while (e < json.Length && json[e] != ',' && json[e] != '}') e++;
            return json.Substring(i, e - i).Trim();
        }

        static float QuickFloat(string json, string key, float fallback)
        {
            var s = QuickField(json, key);
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }
    }
}
