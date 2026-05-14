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
                    Debug.Log($"[HudBridge] selection:upgrade {m.PayloadJson} (binding TODO)");
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
            // lockstep queue, cost check, and TrainQueueItem buffer flow stay
            // intact. `key` is the unit-def ID (e.g. "Builder", "Swordsman").
            if (selectionKind == "hall" || selectionKind == "barracks" || selectionKind == "shrine")
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
                // Use the first selected entity — train queues belong to a
                // single building, so a multi-select scenario shouldn't reach
                // these layouts (deriveSelectionKey gates on kind == "single").
                Core.Commands.CommandRouter.IssueTrain(em, sel[0], key);
                return;
            }

            // Vault / research / military command routing — not wired yet.
            Debug.Log($"[HudBridge] actions:invoke {key} (kind={selectionKind}, binding TODO)");
        }

        void HandleMenuItem(string payloadJson)
        {
            // Quick-and-dirty parse: payload is {"key":"resume|settings|save|load|surrender"}
            var key = QuickField(payloadJson, "key");
            switch (key)
            {
                case "resume": UI.HUD.InGameMenuPanel.Close(); break;
                // Settings / Save / Load / Surrender — wire to the existing
                // pause-menu handlers when those are exposed. For now, just log.
                default:
                    Debug.Log($"[HudBridge] menu item '{key}' clicked (no handler yet)");
                    UI.HUD.InGameMenuPanel.Close();
                    break;
            }
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
            _sb.Append("\"religion\":{\"value\":").Append(religion)
                .Append(",\"cap\":0,\"rate\":").Append(rates[RRel]).Append("},");
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
        struct SelGroup { public int Count; public float HpSum; public float HpMaxSum; public bool IsBuilding; }

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

            if (sel.Count == 1)
            {
                var e = sel[0];
                if (!emm.Exists(e)) { PushIfChanged("selection", "null"); return; }

                var info = EntityInfoExtractor.GetDisplayInfo(e, emm);
                bool isBuilding = emm.HasComponent<BuildingTag>(e);
                Faction fac = emm.HasComponent<FactionTag>(e)
                    ? emm.GetComponentData<FactionTag>(e).Value
                    : Faction.Blue;
                string tone = fac == GameSettings.LocalPlayerFaction ? "own" : "enemy";
                int hpCur = info.CurrentHealth ?? 0;
                int hpMax = Mathf.Max(1, info.MaxHealth ?? 0);

                _sb.Clear();
                _sb.Append("{\"kind\":\"single\",\"id\":").Append(e.Index)
                    .Append(",\"name\":\"").Append(JsonEscape(info.Name ?? "Unit"))
                    .Append("\",\"klass\":\"").Append(JsonEscape(isBuilding ? "Structure" : (info.Type ?? "Combatant")))
                    .Append("\",\"portrait\":\"").Append(PortraitFor(info.Name, isBuilding))
                    .Append("\",\"portraitTone\":\"").Append(tone).Append('"')
                    .Append(",\"hp\":").Append(hpCur)
                    .Append(",\"hpMax\":").Append(hpMax)
                    .Append(",\"sh\":0,\"shMax\":0");
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
                _sb.Append(",\"canUpgrade\":false}");
                PushIfChanged("selection", _sb.ToString());
                return;
            }

            // Multi: bucket by name.
            _selGroups.Clear();
            for (int i = 0; i < sel.Count; i++)
            {
                var e = sel[i];
                if (!emm.Exists(e)) continue;
                var info = EntityInfoExtractor.GetDisplayInfo(e, emm);
                string key = info.Name ?? "Unit";
                bool isBld = emm.HasComponent<BuildingTag>(e);
                _selGroups.TryGetValue(key, out var g);
                g.Count++;
                g.HpSum += info.CurrentHealth ?? 0;
                g.HpMaxSum += Mathf.Max(1, info.MaxHealth ?? 0);
                g.IsBuilding = isBld;
                _selGroups[key] = g;
            }

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
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
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
