// GameUIManager.cs
// Runtime host for the FINAL authored game UI (Assets/GameData/Scenes/Menus/
// GameUI prefabs, wired through Resources/GameUICatalog). Replaces the old
// IMGUI / UI Toolkit HUD stacks entirely (removed 2026-07-17): GameBootstrap
// adds this one component, it instantiates every assigned panel prefab and
// binds live game data to it.
//
// Panels are authored in the GameUI staging scene under a Screen-Space
// Overlay canvas with a 3840x2160 ScaleWithScreenSize scaler. The panel
// PREFABS do not carry that canvas (a stray staging canvas may sit on an
// inner node instead, e.g. the Synty background subtree) — so this manager
// builds ONE host canvas that mirrors the staging setup, parents every
// panel under it with authored rects intact, and strips any canvas found
// inside a panel. Anything left outside a canvas would not render at all.
//
// The resource / stats / selection-header panels are authored inside the
// scene's "BottonLeft" dock (a bottom-left container; the panels carry a
// 2x root scale), so their prefab root rects are relative to that box —
// the manager recreates the dock (BottomLeftDock* mirror constants) and
// spawns them under it. When moving a panel in the staging scene, APPLY
// the root transform override to the prefab or the runtime will keep
// using the stale authored rect.
//
// Wired panels:
// - RESOURCE PANEL: rows Supplies / Iron / Veilstone / Veilsteel (bank) and
//   Housing (FactionPopulation "Current/Max"), each row with an "amount"
//   TMP_Text child, refreshed 4x per second.
// - SELECTION HEADER: "SPR_Screenshot" Image gets the per-entity symbol
//   sprite (catalog entitySymbols), the single TMP label shows the entity
//   name, "Archer x 45" for a same-type group, or "45 units" for a mixed
//   group. Hidden while nothing is selected.
// - UNIT STATS PANEL: HP + attack-cooldown sliders/labels for the focused
//   selected unit; owns its refresh loop (UnitStatsPanelBinder, 10 Hz).
// - UNIT ROSTER: one clickable entry per distinct unit type in a mixed
//   selection; clicking pins the stats panel to that type
//   (UnitRosterPanelBinder + UnitRosterFocus).
// - MINIMAP: authored frame; the "Map" Image receives a translucent
//   elevation image generated from the loaded map's baked terrain, plus
//   the live layers ported from the retired MinimapRenderer — FoW dimming,
//   faction/resource/ritual blips, camera view rectangle, click-to-snap
//   and right-click move orders (MinimapPanelBinder).
// - ACTIONS PANEL: when GameUICatalog.actionsPanel is assigned, the AUTHORED
//   3x5 grid (ActionsPanelPrefabBinder) serves the builder palette and the
//   building train/research rows; the code-built ActionsPanelBinder then only
//   covers the special selections (vault, walls, hut age-up, bazaar wagon,
//   temple upgrade lever). Unassigned -> the code-built actions panel and
//   builder palette (BuilderPanelBinder) render everything as before.
// - TOP CHOICE BAR: special-building choice buttons (Shrine / Vault /
//   Keep, until one is started), plus the authored CultureSelection
//   prefabs — the "SELECT CULTURE" pill and the culture selection menu
//   it opens (TopChoiceBar).
// - SELECTION HEADER UPGRADE BUTTON: bound only when the SelectionHeader
//   prefab AUTHORS a node named "…upgrade…". The building level-up itself is
//   a normal action in whichever actions panel is up (BuildingUpgradeAction);
//   the code-built pill that used to float off the header's right edge is
//   gone.
// - PAUSE MENU: code-built PauseMenuPanel (Esc) — resume / restart / main
//   menu / quit, and the owner of the Escape cascade for the whole HUD.
// - TOOLTIPS: every button in this stack hovers through the shared UITooltip.

using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TheWaningBorder.UI.GameUI
{
    public sealed class GameUIManager : MonoBehaviour
    {
        private const float RefreshInterval = 0.25f;

        // Mirror of the staging scene's canvas (GameUI.unity): overlay,
        // ScaleWithScreenSize, 3840x2160, match width.
        private static readonly Vector2 StagingReferenceResolution = new Vector2(3840f, 2160f);

        // Mirrors of the staging scene's layout containers (GameUI.unity).
        // Panel prefab root rects are authored RELATIVE to these boxes, so
        // the runtime must recreate them exactly. If a container moves or
        // resizes in the scene, update these to match (and run
        // tools/sync_gameui_prefabs.py to push panel overrides into the
        // prefabs).
        // "BottonLeft" dock: resource panel / unit stats / selection header /
        // unit roster (the roster's authored rect places it flush right of
        // the stats panel).
        private static readonly Vector2 BottomLeftDockCenter = new Vector2(750.5383f, 328.28113f);
        private static readonly Vector2 BottomLeftDockSize = new Vector2(1501.1f, 656.561f);

        /// <summary>
        /// SCREEN PIXELS the in-game HUD reserves along the BOTTOM edge of the
        /// screen — the tallest thing docked there is the bottom-left dock, and
        /// it clears the actions panel (440 canvas px) and the minimap too.
        ///
        /// Anything that draws its own overlay must stay above this, the same
        /// way anything pinning top-centre must clear
        /// <see cref="HUD.GameClockHUD.ReservedScreenHeight"/>.
        ///
        /// The conversion is exact rather than approximate: the host canvas is
        /// ScaleWithScreenSize with matchWidthOrHeight = 0, so its scale factor
        /// is precisely Screen.width / referenceResolution.x.
        /// </summary>
        public static float ReservedBottomScreenHeight =>
            BottomLeftDockSize.y * (Screen.width / StagingReferenceResolution.x);

        private RectTransform _hostCanvasRect;
        private RectTransform _bottomLeftDock;
        private GameObject _resourcePanel;
        private TMP_Text _supplies, _iron, _veilstone, _veilsteel, _housing;

        private GameObject _selectionHeader;
        private TMP_Text _selectionLabel;
        private Image _selectionPortrait;

        // "Upgrade to Lv N" button on the selection header (single owned
        // upgradeable building selected). Routes UpgradeBuildingCommandHelper.
        private GameObject _upgradeButton;
        private Image _upgradeButtonBg;
        private TMP_Text _upgradeButtonLabel;
        private Entity _upgradeTarget;
        private string _upgradeTooltip;

        private GameObject _unitDetailsPanel;

        /// <summary>One stat cell of the details grid: [icon] TITLE + either
        /// a value text, a 5-diamond strength scale, or the health bar.</summary>
        private sealed class StatCell
        {
            public GameObject Root;
            public TMP_Text Value;
            public Image Icon;
            public Image[] Diamonds;
        }

        private StatCell _health, _attack, _range, _speed, _atkSpeed,
                         _armor, _armorType, _bonus, _sight;
        private RectTransform _healthBar, _healthFill;
        private Image _healthFillImage;

        private static readonly Color HealthGreen = new Color(0.36f, 0.72f, 0.33f);
        private static readonly Color HealthAmber = new Color(0.86f, 0.68f, 0.25f);
        private static readonly Color HealthRed   = new Color(0.78f, 0.22f, 0.18f);
        private static readonly Color DiamondOn   = new Color(0.909f, 0.835f, 0.627f, 1f);
        private static readonly Color DiamondOff  = new Color(0.909f, 0.835f, 0.627f, 0.16f);

        // 1-5 diamond thresholds, calibrated to the authored TechTree unit
        // ranges (damage 2-40, ranged range 10-40, speed 1.8-7.2).
        private static readonly float[] DamageBins = { 6f, 12f, 18f, 27f };
        private static readonly float[] RangeBins  = { 12f, 16f, 22f, 30f };
        private static readonly float[] SpeedBins  = { 3.5f, 4.8f, 5.6f, 6.4f };
        private readonly Dictionary<string, Sprite> _symbols =
            new Dictionary<string, Sprite>(System.StringComparer.OrdinalIgnoreCase);

        private float _timer;
        private SelectionChangeDetector _selectionDetector;

        // Cached queries — CreateEntityQuery per frame leaks into the world's
        // query registry.
        private static readonly ComponentType[] BankQueryTypes =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<TheWaningBorder.Economy.FactionResources>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _bankQuery;

        private void Start()
        {
            var catalog = Resources.Load<GameUICatalog>("GameUICatalog");
            if (catalog == null)
            {
                TWBLog.Log("[GameUI] Resources/GameUICatalog.asset missing — no game UI will show.");
                return;
            }

            // uGUI interactivity needs an EventSystem; game scenes are built
            // procedurally and have none of their own.
            if (EventSystem.current == null)
            {
                var es = new GameObject("EventSystem",
                    typeof(EventSystem), typeof(StandaloneInputModule));
                es.transform.SetParent(transform, false);
            }

            CreateHostCanvas();

            // Code-built HUD extras (no authored prefabs yet): the formations
            // strip and the spells bar. Both build their own subtrees under
            // the host canvas and self-hide when the selection is irrelevant.
            _hostCanvasRect.gameObject.AddComponent<FormationsPanelBinder>();
            _hostCanvasRect.gameObject.AddComponent<SpellsPanelBinder>();

            // Pause menu (Esc). Spawned on its own full-canvas host and last
            // in sibling order so its scrim covers every other panel; it also
            // owns the Escape cascade for the whole HUD.
            SpawnCodeBuilt<PauseMenuPanel>("GameUI_PauseMenu");

            // End-of-match screen — spawned after the pause menu so its scrim
            // covers it. Hidden until VictoryConditionSystem calls TryShow.
            SpawnCodeBuilt<VictoryPanel>("GameUI_VictoryScreen");

            // Tutorial coach — only for a match launched from the TUTORIAL
            // menu entry. It needs a canvas parent, so it is spawned here
            // rather than alongside the other managers in GameBootstrap.
            if (GameSettings.TutorialActive)
                SpawnCodeBuilt<TutorialDirector>("GameUI_TutorialCoach");

            if (catalog.entitySymbols != null)
            {
                foreach (var entry in catalog.entitySymbols)
                {
                    if (entry != null && !string.IsNullOrEmpty(entry.key) && entry.sprite != null)
                        _symbols[entry.key] = entry.sprite;
                }
            }

            if (catalog.resourcePanel != null)
            {
                _resourcePanel = SpawnPanel(catalog.resourcePanel, "GameUI_ResourcePanel",
                    _bottomLeftDock);

                _supplies  = FindAmountLabel(_resourcePanel.transform, "Supplies");
                _iron      = FindAmountLabel(_resourcePanel.transform, "Iron");
                _veilstone = FindAmountLabel(_resourcePanel.transform, "Veilstone");
                _veilsteel = FindAmountLabel(_resourcePanel.transform, "Veilsteel");
                _housing   = FindAmountLabel(_resourcePanel.transform, "Housing");
                if (_supplies == null)
                    TWBLog.Log("[GameUI] ResourcePanel: no Supplies/amount label found — " +
                        "row names changed?");
            }
            else
            {
                TWBLog.Log("[GameUI] GameUICatalog.resourcePanel is unassigned.");
            }

            if (catalog.unitDetailsPanel != null)
            {
                _unitDetailsPanel = SpawnPanel(catalog.unitDetailsPanel, "GameUI_UnitDetailsPanel");
                _health    = BindCell(_unitDetailsPanel.transform, "Health");
                _attack    = BindCell(_unitDetailsPanel.transform, "Attack");
                _range     = BindCell(_unitDetailsPanel.transform, "Range");
                _speed     = BindCell(_unitDetailsPanel.transform, "Speed");
                _atkSpeed  = BindCell(_unitDetailsPanel.transform, "AttackSpeed");
                _armor     = BindCell(_unitDetailsPanel.transform, "Armor");
                _armorType = BindCell(_unitDetailsPanel.transform, "ArmorType");
                _bonus     = BindCell(_unitDetailsPanel.transform, "Bonus");
                _sight     = BindCell(_unitDetailsPanel.transform, "Sight");

                if (_health != null)
                {
                    foreach (var t in _health.Root.GetComponentsInChildren<Transform>(true))
                    {
                        if (t.name == "bar") _healthBar = (RectTransform)t;
                        else if (t.name == "fill")
                        {
                            _healthFill = (RectTransform)t;
                            _healthFillImage = t.GetComponent<Image>();
                        }
                    }
                }
                _unitDetailsPanel.SetActive(false);
            }

            if (catalog.unitStatsPanel != null)
            {
                // Binds itself to the selection and refreshes at 10 Hz —
                // faster than this manager's 4 Hz loop, as the live attack
                // cooldown needs it.
                var statsPanel = SpawnPanel(catalog.unitStatsPanel, "GameUI_UnitStatsPanel",
                    _bottomLeftDock);
                statsPanel.AddComponent<UnitStatsPanelBinder>();
            }

            if (catalog.unitRosterPanel != null)
            {
                // Authored slot grid showing every selected unit; clicking a
                // slot drives UnitRosterFocus, which the stats panel reads.
                // Owns its own refresh loop.
                var rosterPanel = SpawnPanel(catalog.unitRosterPanel, "GameUI_UnitRosterPanel",
                    _bottomLeftDock);
                rosterPanel.AddComponent<UnitRosterPanelBinder>().Init(_symbols);
            }

            if (catalog.minimapPanel != null)
            {
                // Top-level authored rect (not in the bottom-left dock). The
                // binder fills the "Map" Image with a per-map elevation image
                // once the scene's baked terrain is ready.
                var minimap = SpawnPanel(catalog.minimapPanel, "GameUI_Minimap");
                minimap.AddComponent<MinimapPanelBinder>();
            }

            if (catalog.actionsPanel != null)
            {
                // Authored 3x5 actions grid: builder palette + building
                // train/research rows (ActionsPanelPrefabBinder). The code-
                // built panel stays alive for the selections the authored
                // grid doesn't cover (vault, wall conversions, hut age-up,
                // bazaar wagon, temple upgrade lever) — it hides itself for
                // the grids via ActionsPanelPrefabBinder.Active.
                // Parented into the bottom-left dock like the staging scene
                // so it aligns flush with the resource/stats/roster row.
                var actionsPanel = SpawnPanel(catalog.actionsPanel, "GameUI_ActionsPanel",
                    _bottomLeftDock);
                actionsPanel.AddComponent<ActionsPanelPrefabBinder>().Init(_symbols);
                SpawnCodeBuilt<ActionsPanelBinder>("GameUI_SpecialActionsPanel");
            }
            else
            {
                // Code-built fallbacks (GameUIKit theme) while no authored
                // actions panel is assigned in the catalog.
                SpawnCodeBuilt<ActionsPanelBinder>("GameUI_ActionsPanel");
                SpawnCodeBuilt<BuilderPanelBinder>("GameUI_BuilderPanel");
            }
            // Top-center choice flow: the controller is always code-spawned;
            // it drives the authored menus (special cluster, culture menu,
            // culture button pill). Menus spawn top-level under the host
            // canvas (SpawnPanel strips their staging canvases).
            var topBar = SpawnCodeBuilt<TopChoiceBar>("GameUI_TopChoiceBar");
            GameObject specialMenu = catalog.specialBuildingMenu != null
                ? SpawnPanel(catalog.specialBuildingMenu, "GameUI_SpecialBuildingMenu")
                : null;
            GameObject cultureMenu = catalog.cultureSelectionMenu != null
                ? SpawnPanel(catalog.cultureSelectionMenu, "GameUI_CultureSelectionMenu")
                : null;
            GameObject cultureButton = catalog.cultureSelectionButton != null
                ? SpawnPanel(catalog.cultureSelectionButton, "GameUI_CultureSelectionButton")
                : null;
            topBar.BindAuthoredMenus(specialMenu, cultureMenu, cultureButton);

            if (catalog.objectivesPanel != null)
            {
                var objectives = SpawnPanel(catalog.objectivesPanel, "GameUI_ObjectivesPanel");
                objectives.AddComponent<ObjectivesPanelBinder>();
            }

            if (catalog.religionPanel != null)
            {
                var religion = SpawnPanel(catalog.religionPanel, "GameUI_ReligionPanel");
                religion.AddComponent<ReligionPanelBinder>();
            }

            if (catalog.selectionHeader != null)
            {
                _selectionHeader = SpawnPanel(catalog.selectionHeader, "GameUI_SelectionHeader",
                    _bottomLeftDock);

                _selectionLabel = _selectionHeader.GetComponentInChildren<TMP_Text>(true);
                foreach (var img in _selectionHeader.GetComponentsInChildren<Image>(true))
                {
                    if (string.Equals(img.name, "SPR_Screenshot",
                            System.StringComparison.OrdinalIgnoreCase))
                    {
                        _selectionPortrait = img;
                        img.preserveAspect = true;
                        break;
                    }
                }
                if (_selectionLabel == null)
                    TWBLog.Log("[GameUI] SelectionHeader: no TMP label found.");

                BuildUpgradeButton();
                _selectionHeader.SetActive(false);
            }

            RefreshNow();
        }

        private void CreateHostCanvas()
        {
            var go = new GameObject("GameUI_Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = StagingReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            _hostCanvasRect = (RectTransform)go.transform;

            _bottomLeftDock = MakeDock("GameUI_BottomLeftDock",
                Vector2.zero, BottomLeftDockCenter, BottomLeftDockSize);
        }

        /// <summary>Full-canvas host for a code-built panel component; the
        /// component assembles its own visuals in Awake.</summary>
        private T SpawnCodeBuilt<T>(string name) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_hostCanvasRect, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return go.AddComponent<T>();
        }

        private RectTransform MakeDock(string name, Vector2 anchor, Vector2 center, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_hostCanvasRect, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = center;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>
        /// Instantiate a panel prefab under the host canvas with its authored
        /// RectTransform intact, then remove any canvas the prefab carries —
        /// those are staging-scene artifacts (e.g. the Synty background node
        /// carries a stale World Space canvas which, left alive, renders the
        /// background alone and leaves sibling content outside any canvas,
        /// i.e. invisible).
        /// </summary>
        private GameObject SpawnPanel(GameObject prefab, string name,
            RectTransform parent = null)
        {
            var panel = Instantiate(prefab, parent != null ? parent : _hostCanvasRect, false);
            panel.name = name;

            // Authored prefab labels render through the localization table;
            // binder-driven labels are translated at their own call sites and
            // the component goes dormant on them (see LocAuthoredLabel).
            TheWaningBorder.Core.Localization.LocAuthored.Localize(panel);

            foreach (var stray in panel.GetComponentsInChildren<Canvas>(true))
            {
                // A negative override sort order marks a node authored to
                // render BEHIND its siblings (e.g. the UnitStatsPanel
                // background). Sorting dies with the canvas, so keep the
                // intent via sibling order instead.
                bool renderBehind = stray.overrideSorting && stray.sortingOrder < 0;

                // Dependents first — Canvas cannot be destroyed while a
                // scaler/raycaster still requires it.
                var scaler = stray.GetComponent<CanvasScaler>();
                if (scaler != null) DestroyImmediate(scaler);
                var raycaster = stray.GetComponent<GraphicRaycaster>();
                if (raycaster != null) DestroyImmediate(raycaster);
                var strayTransform = stray.transform;
                DestroyImmediate(stray);
                if (renderBehind) strayTransform.SetAsFirstSibling();
            }

            return panel;
        }

        /// <summary>Stat cell = node named after the stat, containing an
        /// "icon" Image, plus an "amount" TMP and/or a "diamonds" node with
        /// five ordered diamond Images. Cells toggle off for entities that
        /// lack the stat.</summary>
        private static StatCell BindCell(Transform panelRoot, string cellName)
        {
            foreach (var t in panelRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(t.name, cellName, System.StringComparison.OrdinalIgnoreCase))
                    continue;

                var cell = new StatCell { Root = t.gameObject };
                cell.Value = FindAmountLabel(panelRoot, cellName);
                foreach (var child in t.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == "icon")
                        cell.Icon = child.GetComponent<Image>();
                    else if (child.name == "diamonds")
                        cell.Diamonds = child.GetComponentsInChildren<Image>(true);
                }
                return cell;
            }
            return null;
        }

        /// <summary>Map a raw stat to a 1-5 strength level via thresholds.</summary>
        private static int StatLevel(float value, float[] bins)
        {
            int level = 1;
            for (int i = 0; i < bins.Length; i++)
                if (value > bins[i]) level++;
            return level;
        }

        private static void ShowDiamonds(StatCell cell, int level)
        {
            if (cell?.Diamonds == null) return;
            for (int i = 0; i < cell.Diamonds.Length; i++)
                cell.Diamonds[i].color = i < level ? DiamondOn : DiamondOff;
        }

        private static void SetCellActive(StatCell cell, bool active)
        {
            if (cell?.Root != null && cell.Root.activeSelf != active)
                cell.Root.SetActive(active);
        }

        /// <summary>Row node by name anywhere under the panel, then its
        /// "amount" TMP child (falls back to any TMP under the row).</summary>
        private static TMP_Text FindAmountLabel(Transform panelRoot, string rowName)
        {
            foreach (var t in panelRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!string.Equals(t.name, rowName, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var label in t.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (string.Equals(label.transform.name, "amount",
                            System.StringComparison.OrdinalIgnoreCase))
                        return label;
                }
                return t.GetComponentInChildren<TMP_Text>(true);
            }
            return null;
        }

        private void Update()
        {
            // 4 Hz for live data (bank amounts, HP) — but the selection
            // header/details must react the FRAME the selection changes, in
            // lockstep with the stats and roster panels' own detectors.
            _timer += Time.unscaledDeltaTime;
            bool selectionChanged = _selectionDetector.Poll();
            if (_timer < RefreshInterval && !selectionChanged) return;
            _timer = 0f;
            RefreshNow();
        }

        private void RefreshNow()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            RefreshResources(em);
            RefreshSelection(em);
        }

        private void RefreshResources(EntityManager em)
        {
            if (_supplies == null && _iron == null && _housing == null) return;

            // Observer perspective: the bar shows the VIEWED player's bank;
            // with nothing selected there is no perspective to show, so the
            // readouts blank rather than lying with some fixed faction's
            // numbers.
            var view = GameSettings.ViewFaction;
            if (view == null)
            {
                if (_supplies != null)  _supplies.text  = "-";
                if (_iron != null)      _iron.text      = "-";
                if (_veilstone != null) _veilstone.text = "-";
                if (_veilsteel != null) _veilsteel.text = "-";
                if (_housing != null)   _housing.text   = "-";
                return;
            }
            var faction = view.Value;
            var query = _bankQuery.Get(em, BankQueryTypes);

            using var entities = query.ToEntityArray(Allocator.Temp);
            using var tags = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var banks = query.ToComponentDataArray<TheWaningBorder.Economy.FactionResources>(Allocator.Temp);

            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value != faction) continue;
                if (_supplies != null)  _supplies.text  = banks[i].Supplies.ToString();
                if (_iron != null)      _iron.text      = banks[i].Iron.ToString();
                if (_veilstone != null) _veilstone.text = banks[i].Veilstone.ToString();
                if (_veilsteel != null) _veilsteel.text = banks[i].Veilsteel.ToString();

                if (_housing != null
                    && em.HasComponent<TheWaningBorder.Economy.FactionPopulation>(entities[i]))
                {
                    var pop = em.GetComponentData<TheWaningBorder.Economy.FactionPopulation>(entities[i]);
                    _housing.text = pop.Current + "/" + pop.Max;
                }
                return;
            }
        }

        /// <summary>
        /// Binds an upgrade Button AUTHORED into the SelectionHeader prefab
        /// (node name containing "upgrade"), if there is one.
        ///
        /// There is deliberately no code-built fallback any more: the old one
        /// was a pill hanging off the right edge of the header, outside every
        /// panel and without a tooltip. The building level-up now renders as a
        /// normal action inside whichever actions panel is up
        /// (BuildingUpgradeAction), so when the prefab ships no upgrade node
        /// the feature is still there — just in the grid where it belongs.
        /// </summary>
        private void BuildUpgradeButton()
        {
            foreach (var authored in _selectionHeader.GetComponentsInChildren<Button>(true))
            {
                if (!authored.gameObject.name.ToLowerInvariant().Contains("upgrade")) continue;
                _upgradeButton = authored.gameObject;
                _upgradeButtonBg = authored.GetComponent<Image>();
                _upgradeButtonLabel = authored.GetComponentInChildren<TMP_Text>(true);
                authored.onClick.AddListener(ClickUpgrade);
                UITooltip.Bind(_upgradeButton, () => _upgradeTooltip);
                _upgradeButton.SetActive(false);
                return;
            }
        }

        private void ClickUpgrade()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            if (_upgradeTarget == Entity.Null) return;
            BuildingUpgradeAction.Execute(world.EntityManager, _upgradeTarget);
            RefreshNow();
        }

        /// <summary>
        /// Drives the AUTHORED header upgrade button when the prefab has one.
        /// All of the gating lives in BuildingUpgradeAction, shared with the
        /// two action panels, so header and grid can never disagree.
        /// </summary>
        private void RefreshUpgradeButton(EntityManager em, int count, Entity first)
        {
            if (_upgradeButton == null) return;

            _upgradeTarget = Entity.Null;
            var upgrade = count == 1
                ? BuildingUpgradeAction.Describe(em, first)
                : default;

            if (_upgradeButton.activeSelf != upgrade.Show)
                _upgradeButton.SetActive(upgrade.Show);
            if (!upgrade.Show) { _upgradeTooltip = null; return; }

            if (upgrade.Enabled) _upgradeTarget = first;
            _upgradeTooltip = upgrade.Tooltip;

            if (_upgradeButtonLabel != null)
            {
                string label = upgrade.Label.Replace('\n', ' ');
                if (_upgradeButtonLabel.text != label) _upgradeButtonLabel.text = label;
                _upgradeButtonLabel.color = upgrade.Enabled ? GameUIKit.Gold : GameUIKit.TextDim;
            }
            if (_upgradeButtonBg != null)
                _upgradeButtonBg.color = upgrade.Enabled
                    ? GameUIKit.ButtonBg : GameUIKit.ButtonBgLocked;
        }

        private void RefreshSelection(EntityManager em)
        {
            if (_selectionHeader == null) return;

            var selection = TheWaningBorder.Input.SelectionSystem.CurrentSelection;

            int count = 0;
            string firstName = null;
            bool uniform = true;
            Entity firstEntity = Entity.Null;
            if (selection != null)
            {
                for (int i = 0; i < selection.Count; i++)
                {
                    var e = selection[i];
                    if (!em.Exists(e)) continue;
                    if (count == 0) firstEntity = e;
                    count++;
                    if (uniform)
                    {
                        var name = EntityInfoExtractor.GetSelectionDisplayName(e, em);
                        if (firstName == null) firstName = name;
                        else if (name != firstName) uniform = false;
                    }
                }
            }

            RefreshUnitDetails(em, count, uniform, firstEntity);
            RefreshUpgradeButton(em, count, firstEntity);

            if (count == 0)
            {
                if (_selectionHeader.activeSelf) _selectionHeader.SetActive(false);
                return;
            }
            if (!_selectionHeader.activeSelf) _selectionHeader.SetActive(true);

            // symbolKey stays the ENGLISH display name on purpose: the
            // GameUICatalog sprite table is keyed by it, so the icon lookup
            // must happen before translation. Only labelText is localized.
            string labelText;
            string symbolKey;
            if (uniform)
            {
                string shownName = TheWaningBorder.Core.Localization.Loc.T(firstName);
                labelText = count == 1 ? shownName : shownName + " x " + count;
                symbolKey = firstName;
            }
            else
            {
                labelText = string.Format(
                    TheWaningBorder.Core.Localization.Loc.T("{0} units"), count);
                symbolKey = "Mixed";
            }

            if (_selectionLabel != null) _selectionLabel.text = labelText;

            if (_selectionPortrait != null)
            {
                if (!_symbols.TryGetValue(symbolKey, out var sprite))
                    _symbols.TryGetValue("Unit", out sprite);
                if (sprite != null && _selectionPortrait.sprite != sprite)
                    _selectionPortrait.sprite = sprite;
            }
        }

        /// <summary>
        /// Stat chips for the selection. Shown for a single entity or a
        /// same-type group (stats of the first member — identical types share
        /// them); hidden for mixed groups and empty selections. Chips whose
        /// stat the entity lacks (e.g. Attack/Speed on buildings) hide
        /// individually.
        /// </summary>
        private void RefreshUnitDetails(EntityManager em, int count, bool uniform, Entity first)
        {
            if (_unitDetailsPanel == null) return;

            if (count == 0 || !uniform || first == Entity.Null)
            {
                if (_unitDetailsPanel.activeSelf) _unitDetailsPanel.SetActive(false);
                return;
            }
            if (!_unitDetailsPanel.activeSelf) _unitDetailsPanel.SetActive(true);

            var info = EntityInfoExtractor.GetDisplayInfo(first, em);

            // Health: full-width bar with cur/max overlaid, tinted by state.
            bool hasHealth = info.CurrentHealth.HasValue;
            SetCellActive(_health, hasHealth);
            if (hasHealth && _health?.Value != null)
                _health.Value.text = info.MaxHealth.HasValue
                    ? info.CurrentHealth.Value + "/" + info.MaxHealth.Value
                    : info.CurrentHealth.Value.ToString();
            if (hasHealth && _healthFill != null && _healthBar != null
                && info.MaxHealth.HasValue && info.MaxHealth.Value > 0)
            {
                float f = Mathf.Clamp01((float)info.CurrentHealth.Value / info.MaxHealth.Value);
                float inner = Mathf.Max(0f, _healthBar.rect.width - 4f);
                _healthFill.sizeDelta = new Vector2(inner * f, _healthFill.sizeDelta.y);
                if (_healthFillImage != null)
                    _healthFillImage.color = f >= 0.5f
                        ? Color.Lerp(HealthAmber, HealthGreen, (f - 0.5f) * 2f)
                        : Color.Lerp(HealthRed, HealthAmber, f * 2f);
            }

            // Attack: 5-diamond strength, icon follows the damage type.
            bool hasAttack = info.HasCombatStats && info.Attack.HasValue;
            SetCellActive(_attack, hasAttack);
            if (hasAttack)
            {
                ShowDiamonds(_attack, StatLevel(info.Attack.Value, DamageBins));
                if (_attack?.Icon != null && !string.IsNullOrEmpty(info.DamageTypeName)
                    && _symbols.TryGetValue("AttackType_" + info.DamageTypeName, out var typeIcon)
                    && typeIcon != null)
                    _attack.Icon.sprite = typeIcon;
            }

            // Range: 5-diamond strength; omitted entirely for melee.
            bool hasRange = hasAttack && info.RangeMax.HasValue && info.RangeMax.Value > 2f;
            SetCellActive(_range, hasRange);
            if (hasRange)
                ShowDiamonds(_range, StatLevel(info.RangeMax.Value, RangeBins));

            // Speed: 5-diamond strength.
            bool hasSpeed = info.Speed.HasValue && info.Speed.Value > 0f;
            SetCellActive(_speed, hasSpeed);
            if (hasSpeed)
                ShowDiamonds(_speed, StatLevel(info.Speed.Value, SpeedBins));

            // Attack speed as attacks per second.
            bool hasAtkSpeed = hasAttack && info.AttackCooldown.HasValue && info.AttackCooldown.Value > 0f;
            SetCellActive(_atkSpeed, hasAtkSpeed);
            if (hasAtkSpeed && _atkSpeed?.Value != null)
                _atkSpeed.Value.text = (1f / info.AttackCooldown.Value).ToString("0.#") + "/s";

            // Armor: per-type breakdown melee|ranged|siege|magic.
            bool hasArmor = info.Defense.HasValue;
            SetCellActive(_armor, hasArmor);
            if (hasArmor && _armor?.Value != null)
                _armor.Value.text = (info.DefenseMelee ?? 0) + "|" + (info.DefenseRanged ?? 0)
                    + "|" + (info.DefenseSiege ?? 0) + "|" + (info.DefenseMagic ?? 0);

            bool hasArmorType = !string.IsNullOrEmpty(info.ArmorTypeName);
            SetCellActive(_armorType, hasArmorType);
            if (hasArmorType && _armorType?.Value != null)
                _armorType.Value.text = info.ArmorTypeName;

            bool hasBonus = !string.IsNullOrEmpty(info.BonusVsText);
            SetCellActive(_bonus, hasBonus);
            if (hasBonus && _bonus?.Value != null)
                _bonus.Value.text = info.BonusVsText;

            bool hasSight = info.SightRadius.HasValue && info.SightRadius.Value > 0f;
            SetCellActive(_sight, hasSight);
            if (hasSight && _sight?.Value != null)
                _sight.Value.text = info.SightRadius.Value.ToString("0.#");
        }
    }
}
