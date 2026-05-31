// GameplayUIController — production mount for the in-match HUD.
//
// AUTO-MOUNT (Phase 2b+): the static AutoMount() method below runs after
// every scene load. If the new scene contains a UnifiedUIManager (the
// canonical "this is a gameplay scene" signal), the controller spawns
// itself on a fresh GameObject and loads its assets from Assets/UI/Resources/.
// Menu / lobby / loading scenes don't have UnifiedUIManager and are skipped.
//
// MANUAL MOUNT (Phase 0 demo style): drop the component on a GameObject,
// assign hudUxml + panelSettings in the Inspector, hit Play. The Inspector
// values, when set, override the Resources auto-load.
//
// Regions wired so far:
//   Phase 2a — ResourcesRegion   (live)
//   Phase 2b — ObjectivesRegion  (live), Menu button → InGameMenuPanel.Toggle
//   Phase 3a — SelectionRegion   (live)
// IMGUI suspended in turn — see SuspendedImguiTypeNames.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using TheWaningBorder.UI.Common;     // UnifiedUIManager
using TheWaningBorder.UI.HUD;
using TheWaningBorder.UI.Regions;

namespace TheWaningBorder.UI
{
    [DisallowMultipleComponent]
    public sealed class GameplayUIController : MonoBehaviour
    {
        // Asset names inside Assets/UI/Resources/ — used by the Resources.Load
        // fallback path when the Inspector slots are empty.
        private const string ResourceHudUxmlName       = "GameplayHUD";
        private const string ResourcePanelSettingsName = "HudPanelSettings";

        [Header("Optional — leave empty to auto-load from Assets/UI/Resources/")]
        [Tooltip("Inspector override for Assets/UI/Resources/GameplayHUD.uxml.")]
        [SerializeField] private VisualTreeAsset hudUxml;

        [Tooltip("Inspector override for Assets/UI/Resources/HudPanelSettings.asset.")]
        [SerializeField] private PanelSettings panelSettings;

        [Header("Tuning")]
        [Tooltip("How often (seconds) to repoll ECS for live data.")]
        [SerializeField] private float refreshInterval = 0.25f;

        private UIDocument _document;
        private ResourcesRegion _resources;
        private ObjectivesRegion _objectives;
        private SelectionRegion _selection;
        private ActionPanelRegion _actions;
        private CulturePopupRegion _culturePopup;
        private VisualElement _menuButton;
        private float _timer;
        private bool _suspendedSiblings;  // set after the first-frame suspend pass

        // ─── Pointer-over-HUD tracking ────────────────────────────────────
        // The existing input system asks `EntityInfoPanel.IsPointerOver()` and
        // `EntityActionPanel.IsPointerOver()` to decide whether a click should
        // hit the world or the UI. With those IMGUI panels suspended their
        // checks always return false → clicks fall through to the world. We
        // count pointer-enter/leave on each blocking jade panel and expose the
        // result as a static; the suspended IMGUI panels delegate to it.
        public static bool IsPointerOverHUD { get; private set; }
        private int _hoverCount;

        // ─── Auto-mount ───────────────────────────────────────────────────
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoMount()
        {
            // Only spawn in scenes that already host the IMGUI HUD manager.
            // That's the canonical "this is a real match" signal — its presence
            // means UnitSelection, FactionResources, etc. are all in play.
            var uum = FindFirstObjectByType<UnifiedUIManager>();
            if (uum == null) return;

            // Don't double-mount on scene reloads.
            if (FindFirstObjectByType<GameplayUIController>() != null) return;

            var go = new GameObject("[Gameplay UI]");
            go.AddComponent<GameplayUIController>();
        }

        // Names of IMGUI HUD components this controller has now replaced.
        // The suspend / restore loop disables their MonoBehaviours while we're
        // running so the old gold-trim panels don't double-render on top of
        // the new jade ones. Resolved by full type name so this file doesn't
        // take a hard compile dependency on the IMGUI assemblies — they can
        // be removed entirely in Phase 6 without touching this list.
        // Aggressively suspended at user request: the old IMGUI chrome must
        // not double-render with the new jade panels. Some surfaces below have
        // NOT been migrated yet (action buttons, religion HUD, minimap, modals)
        // — disabling them temporarily removes ways to issue those commands
        // from the UI. Live functionality returns when each is migrated.
        private static readonly string[] SuspendedImguiTypeNames =
        {
            // ── Migrated to UI Toolkit ─────────────────────────────────
            "TheWaningBorder.UI.HUD.ResourceHUD",                // Phase 2a
            "TheWaningBorder.UI.HUD.VictoryProgressHUD",         // Phase 2b
            "TheWaningBorder.UI.Panels.EntityInfoPanel",         // Phase 3a

            // ── Hidden until their migration lands ─────────────────────
            // Loses IMGUI-only functionality; surfaces marked below need
            // re-enabling or migrating before shipping.
            "TheWaningBorder.UI.Panels.EntityActionPanel",       // Phase 3b — command bar
            "TheWaningBorder.UI.Panels.TechTreePanel",           // Phase 3d — research modal
            "TheWaningBorder.UI.Panels.CultureChoicePopup",      // Phase 3e — culture pick
            "TheWaningBorder.UI.HUD.ReligionHUD",                // Phase 3 follow-up — sect HUD
            "TheWaningBorder.UI.HUD.SpellPanel",                 // not yet planned
            "TheWaningBorder.UI.HUD.ActiveAbilityBar",           // not yet planned
            "TheWaningBorder.UI.HUD.EndGameButton",              // surfaced via pause menu
            "TheWaningBorder.UI.HUD.PostGameStatsUI",            // shows on match end
            "TheWaningBorder.UI.HUD.CrystalDebugPanel",          // debug only
            "TheWaningBorder.World.Minimap.MinimapRenderer",     // Phase 2c — uGUI RawImage minimap
        };

        private readonly List<MonoBehaviour> _suspendedImgui = new List<MonoBehaviour>();

        private void OnEnable()
        {
            ResolveAssetsIfNeeded();

            if (hudUxml == null)
            {
                Debug.LogError("[GameplayUIController] hudUxml is not assigned and " +
                               "Assets/UI/Resources/GameplayHUD.uxml was not found.");
                return;
            }
            if (panelSettings == null)
            {
                Debug.LogError("[GameplayUIController] panelSettings is not assigned and " +
                               "Assets/UI/Resources/HudPanelSettings.asset was not found. " +
                               "Run Tools > Waning Border > UI > Create Phase 0 PanelSettings if missing.");
                return;
            }

            _document = GetComponent<UIDocument>();
            if (_document == null) _document = gameObject.AddComponent<UIDocument>();
            _document.panelSettings = panelSettings;
            _document.visualTreeAsset = hudUxml;
            _document.sortingOrder = 10;

            var root = _document.rootVisualElement;
            _resources    = new ResourcesRegion(root);
            _objectives   = new ObjectivesRegion(root);
            _selection    = new SelectionRegion(root);
            _actions      = new ActionPanelRegion(root);
            _culturePopup = new CulturePopupRegion(root);

            WireMenuButton(root);
            WirePointerTracking(root);
            // SuspendImguiCounterparts() is deferred to the first Update so
            // that sibling MonoBehaviours added later in GameBootstrap's
            // AddComponent sequence (e.g. MinimapRenderer) exist by the time
            // we look for them.
            _suspendedSiblings = false;
        }

        private void OnDisable()
        {
            RestoreImguiCounterparts();
            UnwireMenuButton();
            _resources = null;
            _objectives = null;
            _selection = null;
            _actions = null;
            _culturePopup = null;
            _suspendedSiblings = false;
            _hoverCount = 0;
            IsPointerOverHUD = false;
        }

        private void Update()
        {
            if (!_suspendedSiblings)
            {
                SuspendImguiCounterparts();
                _suspendedSiblings = true;
            }

            _timer += Time.unscaledDeltaTime;
            if (_timer < refreshInterval) return;
            _timer = 0f;
            _resources?.Refresh();
            _objectives?.Refresh();
            _selection?.Refresh();
            _actions?.Refresh();
            _culturePopup?.Refresh();
        }

        // ─── Asset resolution ─────────────────────────────────────────────
        private void ResolveAssetsIfNeeded()
        {
            if (hudUxml == null)
            {
                hudUxml = Resources.Load<VisualTreeAsset>(ResourceHudUxmlName);
            }
            if (panelSettings == null)
            {
                panelSettings = Resources.Load<PanelSettings>(ResourcePanelSettingsName);
            }
        }

        // ─── Menu button ──────────────────────────────────────────────────
        private void WireMenuButton(VisualElement root)
        {
            _menuButton = root.Q<VisualElement>("menu-btn");
            if (_menuButton == null) return;
            _menuButton.RegisterCallback<ClickEvent>(OnMenuButtonClicked);
        }

        private void UnwireMenuButton()
        {
            if (_menuButton == null) return;
            _menuButton.UnregisterCallback<ClickEvent>(OnMenuButtonClicked);
            _menuButton = null;
        }

        private static void OnMenuButtonClicked(ClickEvent _)
        {
            // The IMGUI InGameMenuPanel.Toggle is the canonical pause-menu
            // entry point — keybinds, surrender, quit-to-menu all live there.
            // The full-screen pause menu's own UI Toolkit migration is Phase 4.
            InGameMenuPanel.Toggle();
        }

        // ─── Pointer-over tracking ────────────────────────────────────────
        // Names match the UXML elements that should block world clicks.
        private static readonly string[] BlockingPanelNames =
        {
            "menu-btn",
            "objectives",
            "resources",
            "selection-panel",
            "actions-panel",
            "minimap",
            "culture-popup",  // full-screen modal — backdrop also blocks
        };

        private void WirePointerTracking(VisualElement root)
        {
            foreach (var name in BlockingPanelNames)
            {
                var panel = root.Q<VisualElement>(name);
                if (panel == null) continue;
                panel.RegisterCallback<PointerEnterEvent>(OnPanelEnter);
                panel.RegisterCallback<PointerLeaveEvent>(OnPanelLeave);
            }
        }

        private void OnPanelEnter(PointerEnterEvent _)
        {
            _hoverCount++;
            IsPointerOverHUD = _hoverCount > 0;
        }

        private void OnPanelLeave(PointerLeaveEvent _)
        {
            _hoverCount--;
            if (_hoverCount < 0) _hoverCount = 0;
            IsPointerOverHUD = _hoverCount > 0;
        }

        // ─── IMGUI suspend / restore ──────────────────────────────────────
        private GameObject _minimapCanvas;

        private void SuspendImguiCounterparts()
        {
            foreach (var fullName in SuspendedImguiTypeNames)
                SuspendByTypeName(fullName);

            // MinimapRenderer is uGUI: disabling the MonoBehaviour stops the
            // Update loop but leaves the Canvas + RawImage GameObjects visible.
            // Hide the canvas it owns by name lookup. Restored in OnDisable.
            _minimapCanvas = GameObject.Find("MinimapCanvas");
            if (_minimapCanvas != null && _minimapCanvas.activeSelf)
                _minimapCanvas.SetActive(false);
        }

        private void RestoreImguiCounterparts()
        {
            foreach (var c in _suspendedImgui)
            {
                if (c != null) c.enabled = true;
            }
            _suspendedImgui.Clear();

            if (_minimapCanvas != null)
            {
                _minimapCanvas.SetActive(true);
                _minimapCanvas = null;
            }
        }

        private void SuspendByTypeName(string fullName)
        {
            var type = FindTypeByFullName(fullName);
            if (type == null) return;
            var found = FindObjectsByType(type, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var obj in found)
            {
                if (obj is MonoBehaviour mb && mb.enabled)
                {
                    mb.enabled = false;
                    _suspendedImgui.Add(mb);
                }
            }
        }

        private static System.Type FindTypeByFullName(string fullName)
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName, throwOnError: false);
                if (t != null) return t;
            }
            return null;
        }
    }
}
