// TopChoiceBar.cs
// Top-center controller for the two faction-level choices. Owns the gating
// logic; the visuals are the authored CultureSelection prefabs:
// - SPECIAL BUILDING CHOICE: while the local faction has picked no special
//   yet (culture None, no Shrine of Ahridan / Vault of Almiérra /
//   Fiendstone Keep started), one button per choice building (authored
//   SpecialBuildingChoiceMenu radial cluster, pinned top-center; code-built
//   fallback buttons when unassigned). Clicking enters placement mode; the
//   placement runtime enforces exclusivity and the building self-constructs.
//   Once any special is started the buttons disappear for the rest of the
//   match (design: Age_0.md § Special buildings).
// - CULTURE CHOICE: the authored CultureSelectionButton pill ("SELECT
//   CULTURE"), visible until the culture is chosen. Enabled only when the
//   faction's choice building is COMPLETED and the age-up cost is
//   affordable; while the age-up timer runs it becomes a progress readout.
//   Clicking opens the authored CultureSelectionMenu overlay (Esc or the
//   added Cancel button closes it; coming-soon cultures render locked).
//   Committing spends the cost and routes CommandRouter.IssueAgeUp so the
//   era advance replicates in multiplayer.
//   (The old code-built culture modal + "Advance to Era 2" pill were
//   removed 2026-07-25 in favour of these prefabs.)
// Location: Assets/Scripts/UI/GameUI/TopChoiceBar.cs

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.UI.Panels;

namespace TheWaningBorder.UI.GameUI
{
    public sealed class TopChoiceBar : MonoBehaviour
    {
        private const float RefreshInterval = 0.25f;

        // ── Authored-prefab scaling ────────────────────────────────────────
        // The three prefabs below were authored at a size that swamped the
        // HUD: the radial choice buttons are 380x380 canvas units each (about
        // 190 screen px at 1080p) and the culture pill is 630x100 at a 1.6
        // local scale, i.e. ~1000x160. They are scaled down here rather than
        // in the prefabs so the artist's source assets stay untouched.

        /// <summary>Radial special-building cluster (three 380px buttons).</summary>
        private const float SpecialClusterScale = 0.5f;

        /// <summary>
        /// "SELECT CULTURE" pill. Was 0.25 (the original 4x reduction); doubled
        /// 2026-08-15 on request — the pill was too small to read. The authored
        /// prefab is 630x100 at a 1.6 local scale, so 0.5 lands it around
        /// 500x80 canvas units.
        /// </summary>
        private const float CultureButtonScale = 0.5f;

        /// <summary>
        /// Culture selection modal. HALF, not the quarter the pill gets: this
        /// is a full-screen three-card chooser, and at 0.25 the cards land
        /// around 140 screen px wide with unreadable body text. Drop this to
        /// 0.25f if the smaller modal is wanted anyway.
        /// </summary>
        private const float CultureMenuScale = 0.5f;

        /// <summary>Gap from the top screen edge for the pinned widgets.</summary>
        private const float SpecialClusterTopMargin = 40f;

        /// <summary>Breathing room between the game clock and the pill below it,
        /// in SCREEN PIXELS (converted to canvas units at pin time).</summary>
        private const float CultureButtonClockGapPx = 10f;

        /// <summary>
        /// Top margin for the "SELECT CULTURE" pill, in this canvas's units.
        ///
        /// It cannot be a constant. The pill pins top-centre — the same spot as
        /// the game clock — but the two live on canvases with different scaling:
        /// the clock's is CONSTANT-PIXEL, while this one is ScaleWithScreenSize
        /// against a 3840-wide reference. So the clock's footprint measured in
        /// OUR units grows as the window shrinks (3x at 1280 wide, 1x at 4K).
        /// A fixed margin that cleared the clock at 4K overlapped it at 1080p.
        /// Convert the clock's reserved screen height through our own
        /// scaleFactor instead.
        /// </summary>
        private static float CultureButtonTopMargin(RectTransform pill)
        {
            float scale = 1f;
            var canvas = pill != null ? pill.GetComponentInParent<Canvas>() : null;
            if (canvas != null && canvas.rootCanvas != null && canvas.rootCanvas.scaleFactor > 1e-3f)
                scale = canvas.rootCanvas.scaleFactor;

            return (GameClockHUD.ReservedScreenHeight + CultureButtonClockGapPx) / scale;
        }

        private RectTransform _bar;
        private ChoiceButton[] _specials;

        // ── Authored menus (BindAuthoredMenus) ─────────────────────────────
        private sealed class AuthoredSpecialButton
        {
            public string Id;
            public Button Button;
            public Cost Cost;
            public string Name;
            /// <summary>This button's own caption, kept so the name can be
            /// refreshed once the tech catalog finishes loading.</summary>
            public TMP_Text Label;
        }

        /// <summary>
        /// A TMP_Text that belongs to this button and is not the cluster's
        /// shared caption. Returns null when the button has no label of its own.
        /// </summary>
        private TMP_Text FindOwnLabel(Button button)
        {
            foreach (var t in button.GetComponentsInChildren<TMP_Text>(true))
            {
                if (_authoredSpecialLabel != null && t == _authoredSpecialLabel) continue;
                return t;
            }
            return null;
        }

        // Visibility is plain SetActive on the menu roots (this controller
        // lives on its own GameObject, and the Synty buttons carry particle
        // FX that keep rendering under a zero-alpha CanvasGroup).
        private GameObject _authoredSpecial;
        private bool _specialMenuShowing;
        private readonly List<AuthoredSpecialButton> _authoredSpecialButtons = new();
        private TMP_Text _authoredSpecialLabel;

        private sealed class AuthoredCulturePanel
        {
            public byte Culture;
            public Selectable Selectable;
            public CanvasGroup Group;
            /// <summary>"NOT AVAILABLE YET" overlay, shown only while locked.</summary>
            public GameObject LockedOverlay;
        }

        private GameObject _authoredCulture;
        private readonly List<AuthoredCulturePanel> _authoredCulturePanels = new();
        private TMP_Text _authoredInstructions;

        // Authored "SELECT CULTURE" pill (CultureSelectionButton prefab).
        private GameObject _authoredCultureButton;
        private Button _authoredCultureButtonControl;
        private TMP_Text _authoredCultureButtonLabel;
        private string _authoredCultureButtonText = "Select Culture";
        private bool _cultureReady;

        // PinTopCenter measures RENDERED bounds, so it only works once the
        // widget is active and the layout has resolved. Both stay false until
        // a measurement succeeds, and the retry runs on the refresh tick.
        private bool _specialPinned;
        private bool _culturePinned;

        /// <summary>Screen size the culture pill was last pinned at. Its margin
        /// is derived from the canvas scale factor, which moves with the window
        /// — so a resize has to re-pin or the clearance goes stale.</summary>
        private Vector2Int _culturePinnedScreen;

        private float _timer;

        private sealed class ChoiceButton
        {
            public GameObject Root;
            public Image Bg;
            public TMP_Text Label;
            public TMP_Text Sub;
            public System.Action Click;
            public string Id;
        }

        private static readonly ComponentType[] HallQueryTypes =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionProgress>(),
        };
        private TheWaningBorder.Core.CachedEntityQuery _hallQuery;

        // ── Construction ───────────────────────────────────────────────────

        /// <summary>Set in Awake so PauseMenuPanel's Esc cascade can close the
        /// culture modal without a scene lookup.</summary>
        private static TopChoiceBar _instance;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Awake()
        {
            _instance = this;
            _bar = GameUIKit.Rect(transform, "TopChoiceBar");
            _bar.anchorMin = new Vector2(0.5f, 1f);
            _bar.anchorMax = new Vector2(0.5f, 1f);
            _bar.pivot = new Vector2(0.5f, 1f);
            _bar.anchoredPosition = new Vector2(0f, -16f);

            var h = _bar.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 14f;
            h.childControlWidth = false;
            h.childControlHeight = false;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.UpperCenter;
            var fitter = _bar.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _specials = new ChoiceButton[3];
            for (int i = 0; i < _specials.Length; i++)
                _specials[i] = MakeChoiceButton(_bar, "special" + i, 360f);
        }

        private ChoiceButton MakeChoiceButton(Transform parent, string name, float width)
        {
            var b = new ChoiceButton();
            var rt = GameUIKit.Rect(parent, name);
            rt.sizeDelta = new Vector2(width, 118f);
            GameUIKit.PanelChrome(rt);

            b.Label = GameUIKit.Text(rt, "label", "", 30f, GameUIKit.Gold,
                TextAlignmentOptions.Center, wrap: false);
            b.Label.fontStyle = FontStyles.Bold;
            b.Label.rectTransform.anchorMin = new Vector2(0f, 0.45f);
            b.Label.rectTransform.anchorMax = new Vector2(1f, 1f);
            b.Label.rectTransform.offsetMin = Vector2.zero;
            b.Label.rectTransform.offsetMax = Vector2.zero;

            b.Sub = GameUIKit.Text(rt, "sub", "", 22f, GameUIKit.TextDim,
                TextAlignmentOptions.Center, wrap: false);
            b.Sub.rectTransform.anchorMin = new Vector2(0f, 0f);
            b.Sub.rectTransform.anchorMax = new Vector2(1f, 0.45f);
            b.Sub.rectTransform.offsetMin = new Vector2(4f, 6f);
            b.Sub.rectTransform.offsetMax = new Vector2(-4f, 0f);

            b.Bg = rt.GetComponentInChildren<Image>();  // the chrome bg
            var relay = b.Bg.gameObject.AddComponent<UiClickRelay>();
            relay.OnLeftClick = () => b.Click?.Invoke();

            b.Root = rt.gameObject;
            rt.gameObject.SetActive(false);
            return b;
        }

        // ── Authored menu binding ──────────────────────────────────────────

        /// <summary>
        /// Adopt the authored menu prefabs (already spawned under the host
        /// canvas by GameUIManager; any may be null). The special cluster
        /// is re-anchored TOP-CENTER; the culture menu keeps its authored
        /// full-screen layout (header top-center) and starts hidden; the
        /// culture button pill is re-anchored top-center below the cluster.
        /// </summary>
        public void BindAuthoredMenus(GameObject specialMenu, GameObject cultureMenu,
            GameObject cultureButton = null)
        {
            if (specialMenu != null) BindAuthoredSpecial(specialMenu);
            if (cultureMenu != null) BindAuthoredCulture(cultureMenu);
            if (cultureButton != null) BindAuthoredCultureButton(cultureButton);
            if (_authoredCultureButton == null)
                TWBLog.Log("[GameUI] CultureSelectionButton prefab is not assigned — " +
                    "culture selection cannot be opened from the HUD.");
        }

        private void BindAuthoredSpecial(GameObject menu)
        {
            // The prefab root is authored inactive — binding works on the
            // inactive instance and Refresh activates it when it applies.
            _authoredSpecial = menu;

            // Same ToggleGroup defusal as BindAuthoredCulture: a Synty
            // ToggleGroup with allowSwitchOff=false and no registered active
            // toggles throws in OnEnable every time this menu re-activates.
            foreach (var group in menu.GetComponentsInChildren<ToggleGroup>(true))
                group.allowSwitchOff = true;

            var rt = (RectTransform)menu.transform;
            rt.localScale = new Vector3(SpecialClusterScale, SpecialClusterScale, 1f);
            // Root is a 100x100 hub with the 380px radial buttons centered on
            // it, so the hub's own rect says nothing about where the cluster
            // ends up. Provisional drop; PinTopCenter measures the rendered
            // bounds once the layout resolves (see EnsurePinned).
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -140f);

            // The prefab's shared name/cost caption. It used to read "Choose
            // one" and swap to whichever button the cursor was over — so the
            // cluster showed two names at once (a permanent one on two buttons
            // and a borrowed one on the third), and the caption sits over the
            // leftmost button, which therefore appeared to be labelled "Choose
            // one" and then to rename itself to a DIFFERENT building on hover.
            //
            // Three buttons, three fixed names. The caption is retired; each
            // button carries its own label (see RegisterAuthoredSpecial).
            var labelNode = GameUIKit.FindDeep(menu.transform, "Label_Button_Name");
            if (labelNode != null)
                _authoredSpecialLabel = labelNode.GetComponentInChildren<TMP_Text>(true);

            // Map each authored radial button to a choice-building id by its
            // node name or its authored label text; leftovers pair up with
            // the ids that are still missing, in catalog order.
            var buttons = new List<Button>();
            foreach (var button in menu.GetComponentsInChildren<Button>(true))
                buttons.Add(button);

            var pending = new List<string> { "ShrineOfAhridan", "VaultOfAlmierra", "FiendstoneKeep" };
            foreach (var button in buttons)
            {
                string hint = button.gameObject.name.ToLowerInvariant();
                var text = button.GetComponentInChildren<TMP_Text>(true);
                if (text != null) hint += " " + text.text.ToLowerInvariant();

                string id = null;
                if (hint.Contains("shrine")) id = "ShrineOfAhridan";
                else if (hint.Contains("vault")) id = "VaultOfAlmierra";
                else if (hint.Contains("keep") || hint.Contains("fiendstone")) id = "FiendstoneKeep";
                if (id == null || !pending.Remove(id)) continue;

                RegisterAuthoredSpecial(id, button);
            }
            // Un-hinted buttons take the remaining ids in order.
            foreach (var button in buttons)
            {
                if (pending.Count == 0) break;
                bool taken = _authoredSpecialButtons.Exists(x => x.Button == button);
                if (taken) continue;
                string id = pending[0];
                pending.RemoveAt(0);
                RegisterAuthoredSpecial(id, button);
            }
            if (_authoredSpecialButtons.Count == 0)
            {
                TWBLog.Log("[GameUI] SpecialBuildingChoiceMenu: no Buttons found — " +
                    "falling back to the code-built choice bar.");
                _authoredSpecial = null;
                return;
            }

            // Retire the shared caption — UNLESS a button had no label of its
            // own and adopted it (RegisterAuthoredSpecial claims it in that
            // case, which is what the leftmost button does in the shipped
            // prefab). Hiding it then would leave that button nameless.
            if (_authoredSpecialLabel != null && !_specialLabelAdopted)
                _authoredSpecialLabel.gameObject.SetActive(false);
        }

        /// <summary>True once a button has taken the prefab's shared caption as
        /// its own permanent label, so the cleanup above leaves it alone.</summary>
        private bool _specialLabelAdopted;

        private void RegisterAuthoredSpecial(string id, Button button)
        {
            var entry = new AuthoredSpecialButton { Id = id, Button = button, Name = id };
            if (TechCatalog.IsReady && TechCatalog.TryGetBuilding(id, out var def))
            {
                entry.Name = def.name ?? id;
                if (def.cost != null)
                    entry.Cost = Cost.Of(def.cost.Supplies, def.cost.Iron, def.cost.Veilstone);
            }
            button.onClick.AddListener(() =>
            {
                if (!BuilderCommandPanel.IsPlacingBuilding)
                    BuilderCommandPanel.TriggerBuildingPlacement(entry.Id);
            });

            // This button's OWN permanent label. Prefer a TMP_Text that belongs
            // to the button and is not the cluster's shared caption; if the
            // button has nothing else (the leftmost one in the shipped prefab
            // is captioned by the shared node), adopt the caption as its label
            // so it ends up named rather than blank.
            var own = FindOwnLabel(button);
            if (own == null && _authoredSpecialLabel != null && !_specialLabelAdopted)
            {
                own = _authoredSpecialLabel;
                _specialLabelAdopted = true;
            }
            if (own != null)
            {
                // Display only — entry.Name/Id stay English (the hint match
                // above and the catalog lookups depend on them).
                own.text = Loc.T(entry.Name);
                entry.Label = own;
            }
            else
            {
                // Loud, because the symptom is silent: an unlabelled button in
                // a one-per-match choice. Means the authored button has no
                // TMP_Text of its own and the shared caption was already
                // claimed by an earlier one.
                TWBLog.Log($"[GameUI] SpecialBuildingChoiceMenu: '{button.gameObject.name}' " +
                           $"has no label of its own — '{entry.Name}' will render unnamed.");
            }

            // Hover shows the full description in the standard tooltip beside
            // the cursor. It deliberately does NOT rewrite any button label —
            // a button that renames itself to a different building while you
            // read it is worse than no caption at all.
            UITooltip.Bind(button.gameObject, () => SpecialTooltip(entry));

            _authoredSpecialButtons.Add(entry);
        }

        /// <summary>
        /// One-per-match decision, so the tooltip spells out what it buys:
        /// catalog description, cost against the current bank, and the fact
        /// that picking one retires the other two.
        /// </summary>
        private string SpecialTooltip(AuthoredSpecialButton entry)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(Loc.T(entry.Name)).Append("</b>");

            if (TechCatalog.IsReady && TechCatalog.TryGetBuilding(entry.Id, out var def)
                && !string.IsNullOrEmpty(def.description))
                sb.Append('\n').Append(Loc.T(def.description));

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && !entry.Cost.IsZero)
            {
                var em = world.EntityManager;
                var faction = GameSettings.LocalPlayerFaction;
                sb.Append('\n').Append(Loc.T("Cost: ")).Append(UIHelpers.FormatCostRich(entry.Cost,
                    EntityActionExtractor.GetFactionResourcesAsCostPublic(em, faction)));
                if (!FactionEconomy.CanAfford(em, faction, entry.Cost))
                    sb.Append('\n').Append(Loc.T("<color=#C08040>Not enough resources.</color>"));
            }
            sb.Append('\n').Append(
                Loc.T("<i>One special building per faction — this choice is final.</i>"));
            return sb.ToString();
        }

        private void BindAuthoredCulture(GameObject menu)
        {
            _authoredCulture = menu;

            // The Synty template ships ToggleGroups with allowSwitchOff=false.
            // When the menu re-activates with every toggle forced off (see
            // OpenAuthoredCulture) — or with a group whose only toggles are
            // inactive template leftovers — ToggleGroup.OnEnable's
            // EnsureValidState indexes an empty toggle list and throws
            // ArgumentOutOfRangeException. Selection is committed by our
            // click handlers, not the groups, so switch-off is always fine.
            foreach (var group in menu.GetComponentsInChildren<ToggleGroup>(true))
                group.allowSwitchOff = true;

            // The staging Animator parks the full-stretch root off-screen for
            // its slide-in clip; visibility is ours now, so pin it in place.
            var animator = menu.GetComponent<Animator>();
            if (animator != null) animator.enabled = false;
            var rt = (RectTransform)menu.transform;
            rt.anchoredPosition = Vector2.zero;

            // Shrink the CONTENT (header + culture cards + instructions), not
            // the root: the root is the full-screen click blocker and has to
            // keep covering the map.
            var content = GameUIKit.FindDeep(menu.transform, "Content");
            if (content != null)
                content.localScale =
                    new Vector3(CultureMenuScale, CultureMenuScale, 1f);
            else
                TWBLog.Log("[GameUI] CultureSelectionMenu: no \"Content\" node — " +
                    "the menu renders at its authored (oversized) scale.");

            var instructions = GameUIKit.FindDeep(menu.transform, "Label_Instructions");
            if (instructions != null)
                _authoredInstructions = instructions.GetComponentInChildren<TMP_Text>(true);

            (string node, byte culture)[] panels =
            {
                ("Panel_Alanthor", Cultures.Alanthor),
                ("Panel_Feraldis", Cultures.Feraldis),
                ("Panel_Runai", Cultures.Runai),
            };
            foreach (var (node, culture) in panels)
            {
                var panel = GameUIKit.FindDeep(menu.transform, node);
                if (panel == null)
                {
                    TWBLog.Log($"[GameUI] CultureSelectionMenu: node \"{node}\" not found.");
                    continue;
                }
                var entry = new AuthoredCulturePanel { Culture = culture };
                entry.Group = panel.gameObject.GetComponent<CanvasGroup>();
                if (entry.Group == null) entry.Group = panel.gameObject.AddComponent<CanvasGroup>();
                entry.Selectable = panel.GetComponentInChildren<Selectable>(true);
                entry.LockedOverlay = BuildLockedOverlay(panel);

                byte c = culture;
                if (entry.Selectable is Toggle toggle)
                    toggle.onValueChanged.AddListener(v => { if (v) CommitAgeUp(c); });
                else if (entry.Selectable is Button button)
                    button.onClick.AddListener(() => CommitAgeUp(c));
                else
                {
                    // No Selectable authored — the whole panel becomes the button.
                    panel.gameObject.AddComponent<UiClickRelay>().OnLeftClick = () => CommitAgeUp(c);
                }
                _authoredCulturePanels.Add(entry);
            }
            if (_authoredCulturePanels.Count == 0)
            {
                TWBLog.Log("[GameUI] CultureSelectionMenu: no culture panels bound — " +
                    "falling back to the code-built modal.");
                _authoredCulture = null;
            }
            else
            {
                BuildAuthoredCancel(menu.transform);
            }
        }

        /// <summary>The authored menu ships no close control; add a themed
        /// Cancel button bottom-center so the player can back out.</summary>
        private void BuildAuthoredCancel(Transform menuRoot)
        {
            var cancel = GameUIKit.Rect(menuRoot, "cancel");
            cancel.anchorMin = new Vector2(0.5f, 0f);
            cancel.anchorMax = new Vector2(0.5f, 0f);
            cancel.pivot = new Vector2(0.5f, 0f);
            cancel.anchoredPosition = new Vector2(0f, 60f);
            cancel.sizeDelta = new Vector2(300f, 80f);
            var bg = GameUIKit.Image(cancel, "bg", GameUIKit.ButtonBg, raycast: true);
            GameUIKit.Stretch(bg.rectTransform);
            var label = GameUIKit.Text(cancel, "label", Loc.T("Cancel (Esc)"), 30f,
                GameUIKit.TextMain, TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(label.rectTransform);
            bg.gameObject.AddComponent<UiClickRelay>().OnLeftClick = CloseAuthoredCulture;
        }

        /// <summary>
        /// Adopt the authored "SELECT CULTURE" pill. Re-anchored top-center
        /// (below the special cluster's slot); visibility + interactability
        /// are driven by RefreshCultureButton.
        /// </summary>
        private void BindAuthoredCultureButton(GameObject pill)
        {
            _authoredCultureButtonControl = pill.GetComponentInChildren<Button>(true);
            if (_authoredCultureButtonControl == null)
            {
                TWBLog.Log("[GameUI] CultureSelectionButton: no Button found in the prefab.");
                return;
            }
            _authoredCultureButton = pill;

            // The pill's own rect is a 100x100 hub; the visible button is a
            // nested instance sitting 90 units ABOVE it, so anchoring the hub
            // flush to the top edge left roughly half the pill off-screen.
            // Scale first, then let PinTopCenter place it from its measured
            // bounds (EnsurePinned).
            var rt = (RectTransform)pill.transform;
            rt.localScale = new Vector3(CultureButtonScale, CultureButtonScale, 1f);
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, -60f);

            _authoredCultureButtonLabel = pill.GetComponentInChildren<TMP_Text>(true);
            if (_authoredCultureButtonLabel != null
                && !string.IsNullOrWhiteSpace(_authoredCultureButtonLabel.text))
                _authoredCultureButtonText = _authoredCultureButtonLabel.text;

            _authoredCultureButtonControl.onClick.AddListener(() =>
            {
                if (_cultureReady) OpenCultureMenu();
            });

            UITooltip.Bind(_authoredCultureButtonControl.gameObject, CultureButtonTooltip);
        }

        /// <summary>Why the pill is (or is not) clickable, priced.</summary>
        private string CultureButtonTooltip()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return Loc.T("Choose your culture");
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            string cost = UIHelpers.FormatCostRich(CultureConfig.AgeUpCost,
                EntityActionExtractor.GetFactionResourcesAsCostPublic(em, faction));
            string body = "<b>" + Loc.T("Choose your culture") + "</b>\n"
                + Loc.T("Advances the faction to Era 2 and unlocks its Age 1 buildings, units and upgrades.")
                + "\n" + Loc.T("Cost: ") + cost;

            if (BuildingFactory.GetCompletedFactionChoiceBuilding(em, faction) == null)
                return body + "\n"
                    + Loc.T("<color=#C08040>Finish your special building first.</color>");
            if (!FactionEconomy.CanAfford(em, faction, CultureConfig.AgeUpCost))
                return body + "\n" + Loc.T("<color=#C08040>Not enough resources.</color>");
            return body;
        }

        private void OpenCultureMenu()
        {
            if (_authoredCulture == null)
            {
                TWBLog.Log("[GameUI] CultureSelectionMenu prefab is not assigned — " +
                    "cannot open culture selection.");
                return;
            }
            OpenAuthoredCulture();
        }

        private bool AuthoredCultureOpen =>
            _authoredCulture != null && _authoredCulture.activeSelf;

        private void OpenAuthoredCulture()
        {
            _authoredCulture.transform.SetAsLastSibling();
            foreach (var p in _authoredCulturePanels)
                if (p.Selectable is Toggle toggle) toggle.SetIsOnWithoutNotify(false);
            _authoredCulture.SetActive(true);
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
                RefreshAuthoredCulture(world.EntityManager, GameSettings.LocalPlayerFaction);
        }

        private void CloseAuthoredCulture()
        {
            if (AuthoredCultureOpen) _authoredCulture.SetActive(false);
        }

        private void RefreshAuthoredCulture(EntityManager em, Faction faction)
        {
            bool canAfford = FactionEconomy.CanAfford(em, faction, CultureConfig.AgeUpCost);
            if (_authoredInstructions != null)
                _authoredInstructions.text = (canAfford
                        ? Loc.T("Choose your culture — this advances your faction to Era 2.   Cost: ")
                        : Loc.T("Not enough resources to advance.   Cost: "))
                    + UIHelpers.FormatCostRich(CultureConfig.AgeUpCost,
                        EntityActionExtractor.GetFactionResourcesAsCostPublic(em, faction));

            foreach (var p in _authoredCulturePanels)
            {
                bool locked = CultureConfig.IsComingSoon(p.Culture);
                bool enabled = !locked && canAfford;
                // 0.6 rather than the old 0.45: the overlay caption is a child
                // of this CanvasGroup, so the card's alpha multiplies it and
                // 0.45 left "NOT AVAILABLE YET" too faint to read.
                p.Group.alpha = locked ? 0.6f : 1f;
                p.Group.interactable = enabled;
                if (p.Selectable != null) p.Selectable.interactable = enabled;
                if (p.LockedOverlay != null && p.LockedOverlay.activeSelf != locked)
                    p.LockedOverlay.SetActive(locked);
            }
        }

        /// <summary>
        /// Builds the "NOT AVAILABLE YET" band for a culture card. The
        /// authored CultureSelection prefabs carry no such label, so the bar
        /// adds one per card and just toggles it — the card still greys out
        /// and goes non-interactable through its CanvasGroup, and
        /// <see cref="CommitAgeUp"/> refuses locked cultures regardless.
        /// </summary>
        private static GameObject BuildLockedOverlay(Transform panel)
        {
            var go = new GameObject("Label_NotAvailableYet",
                                    typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(panel, false);
            // Full-width band across the middle of the card.
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(2f, -24f);
            rt.offsetMax = new Vector2(-2f, 24f);
            rt.SetAsLastSibling();

            var backing = go.GetComponent<Image>();
            backing.color = new Color(0f, 0f, 0f, 0.78f);
            backing.raycastTarget = false;

            var textGo = new GameObject("Text", typeof(RectTransform),
                                        typeof(TextMeshProUGUI));
            var trt = (RectTransform)textGo.transform;
            trt.SetParent(rt, false);
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(4f, 2f);
            trt.offsetMax = new Vector2(-4f, -2f);

            var text = textGo.GetComponent<TextMeshProUGUI>();
            text.text = Loc.T("NOT AVAILABLE YET");
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 8f;
            text.fontSizeMax = 22f;
            text.fontStyle = FontStyles.Bold;
            text.color = new Color(1f, 0.88f, 0.62f, 1f);
            text.raycastTarget = false;

            go.SetActive(false);
            return go;
        }

        // ── Refresh ────────────────────────────────────────────────────────

        /// <summary>
        /// Esc hook for PauseMenuPanel, which owns the key. Returns true when
        /// the culture modal was open and has been closed, so the cascade
        /// stops there instead of also opening the pause menu.
        /// </summary>
        public static bool CloseCultureMenu()
        {
            if (_instance == null || !_instance.AuthoredCultureOpen) return false;
            _instance.CloseAuthoredCulture();
            return true;
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < RefreshInterval) return;
            _timer = 0f;
            Refresh();
        }

        private void Refresh()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            bool ok = world != null && world.IsCreated && !GameSettings.IsObserver;
            if (!ok)
            {
                HideAll();
                return;
            }
            var em = world.EntityManager;

            var faction = GameSettings.LocalPlayerFaction;
            Entity hall = FindLocalHall(em, faction, out byte culture);

            // Culture chosen (or no hall yet): the whole bar retires.
            if (hall == Entity.Null || culture != Cultures.None)
            {
                HideAll();
                if (AuthoredCultureOpen) CloseAuthoredCulture();
                return;
            }

            RefreshSpecials(em, faction);
            RefreshCultureButton(em, faction, hall);
            if (AuthoredCultureOpen) RefreshAuthoredCulture(em, faction);
        }

        private void HideAll()
        {
            foreach (var s in _specials)
                if (s.Root.activeSelf) s.Root.SetActive(false);
            if (_authoredCultureButton != null && _authoredCultureButton.activeSelf)
                _authoredCultureButton.SetActive(false);
            if (_authoredSpecial != null && _authoredSpecial.activeSelf)
                _authoredSpecial.SetActive(false);
            _specialMenuShowing = false;
            _cultureReady = false;
        }

        private void RefreshSpecials(EntityManager em, Faction faction)
        {
            // A started (even in-progress) special hides the choice for good.
            bool anyStarted = BuildingFactory.GetFactionChoiceBuilding(em, faction) != null;

            if (_authoredSpecial != null)
            {
                RefreshAuthoredSpecial(em, faction, anyStarted);
                // Code-built buttons stay retired while the authored menu runs.
                foreach (var s in _specials)
                    if (s.Root.activeSelf) s.Root.SetActive(false);
                return;
            }

            int used = 0;
            if (!anyStarted && TechCatalog.IsReady)
            {
                var available = EntityActionExtractor.GetFactionResourcesAsCostPublic(em, faction);
                foreach (var building in TechCatalog.GetAllBuildings())
                {
                    if (!BuildingFactory.IsChoiceBuilding(building.id)) continue;
                    if (used >= _specials.Length) break;

                    var b = _specials[used++];
                    var cost = building.cost != null
                        ? Cost.Of(building.cost.Supplies, building.cost.Iron, building.cost.Veilstone)
                        : default;
                    bool canAfford = FactionEconomy.CanAfford(em, faction, cost);
                    bool placing = BuilderCommandPanel.IsPlacingBuilding;

                    b.Root.SetActive(true);
                    b.Id = building.id;
                    b.Label.text = Loc.T(building.name);
                    b.Label.color = canAfford && !placing ? GameUIKit.Gold : GameUIKit.TextDim;
                    b.Sub.text = cost.IsZero ? Loc.T("Choose one")
                        : UIHelpers.FormatCostRich(cost, available);

                    string id = building.id;
                    b.Click = (canAfford && !placing)
                        ? (System.Action)(() => BuilderCommandPanel.TriggerBuildingPlacement(id))
                        : null;
                }
            }
            for (int i = used; i < _specials.Length; i++)
                if (_specials[i].Root.activeSelf) _specials[i].Root.SetActive(false);
        }

        private void RefreshAuthoredSpecial(EntityManager em, Faction faction, bool anyStarted)
        {
            _specialMenuShowing = !anyStarted;
            if (_authoredSpecial.activeSelf != _specialMenuShowing)
                _authoredSpecial.SetActive(_specialMenuShowing);
            if (anyStarted) return;

            if (!_specialPinned)
                _specialPinned = GameUIKit.PinTopCenter(
                    (RectTransform)_authoredSpecial.transform, SpecialClusterTopMargin);

            bool placing = BuilderCommandPanel.IsPlacingBuilding;
            foreach (var b in _authoredSpecialButtons)
            {
                // Refresh cost from the catalog once it finishes loading
                // (binding can run before TechTree parsing completes).
                if (b.Cost.IsZero && TechCatalog.IsReady
                    && TechCatalog.TryGetBuilding(b.Id, out var def))
                {
                    b.Name = def.name ?? b.Id;
                    if (def.cost != null)
                        b.Cost = Cost.Of(def.cost.Supplies, def.cost.Iron, def.cost.Veilstone);

                    // Push the real name onto the button. Binding can run before
                    // the catalog finishes parsing, in which case the label was
                    // stamped with the raw id ("ShrineOfAhridan") and would have
                    // kept it for the whole match.
                    if (b.Label != null) b.Label.text = Loc.T(b.Name);
                }
                bool canAfford = FactionEconomy.CanAfford(em, faction, b.Cost);
                b.Button.interactable = canAfford && !placing;
            }
        }

        private void RefreshCultureButton(EntityManager em, Faction faction, Entity hall)
        {
            if (_authoredCultureButton == null) return;

            // While the authored special-building cluster occupies the top
            // center, the culture pill stays out of its way — it appears the
            // moment a special is started (progress / choose).
            bool visible = !_specialMenuShowing;
            if (_authoredCultureButton.activeSelf != visible)
                _authoredCultureButton.SetActive(visible);
            if (!visible)
            {
                _cultureReady = false;
                return;
            }

            var screen = new Vector2Int(Screen.width, Screen.height);
            if (_culturePinned && screen != _culturePinnedScreen) _culturePinned = false;

            if (!_culturePinned)
            {
                var pill = (RectTransform)_authoredCultureButton.transform;
                _culturePinned = GameUIKit.PinTopCenter(pill, CultureButtonTopMargin(pill));
                if (_culturePinned) _culturePinnedScreen = screen;
            }

            if (em.HasComponent<AgeUpState>(hall))
            {
                var s = em.GetComponentData<AgeUpState>(hall);
                float pct = s.Duration > 0f
                    ? Mathf.Clamp01((s.Duration - s.Remaining) / s.Duration) : 0f;
                if (_authoredCultureButtonLabel != null)
                    _authoredCultureButtonLabel.text =
                        string.Format(Loc.T("Advancing {0}%"), (int)(pct * 100f));
                _authoredCultureButtonControl.interactable = false;
                _cultureReady = false;
                return;
            }

            bool hasChoice = BuildingFactory.GetCompletedFactionChoiceBuilding(em, faction) != null;
            bool canAfford = FactionEconomy.CanAfford(em, faction, CultureConfig.AgeUpCost);
            _cultureReady = hasChoice && canAfford;

            // Compare against the TRANSLATED pill text — comparing against the
            // stored English would rewrite the label on every refresh.
            string pillText = Loc.T(_authoredCultureButtonText);
            if (_authoredCultureButtonLabel != null
                && _authoredCultureButtonLabel.text != pillText)
                _authoredCultureButtonLabel.text = pillText;
            _authoredCultureButtonControl.interactable = _cultureReady;
        }

        // ── Commit ─────────────────────────────────────────────────────────

        private void CommitAgeUp(byte culture)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            // Defense in depth — the card is disabled for locked cultures.
            if (CultureConfig.IsComingSoon(culture))
            {
                PlayerNotificationSystem.NotifyError(string.Format(
                    Loc.T("{0} is coming soon"), CultureConfig.GetName(culture)));
                return;
            }

            Entity hall = FindLocalHall(em, faction, out byte current);
            if (hall == Entity.Null || current != Cultures.None) return;
            if (em.HasComponent<AgeUpState>(hall)) return;

            // Affordability CHECK only — AgeUpCommandDirect spends on every
            // peer (docs/Multiplayer_LAN_Readiness.md).
            if (!FactionEconomy.CanAfford(em, faction, CultureConfig.AgeUpCost))
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources to advance"));
                return;
            }

            CommandRouter.IssueAgeUp(em, hall, culture);
            CloseAuthoredCulture();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private Entity FindLocalHall(EntityManager em, Faction faction, out byte culture)
        {
            culture = Cultures.None;
            var q = _hallQuery.Get(em, HallQueryTypes);
            using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var prog = q.ToComponentDataArray<FactionProgress>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                culture = prog[i].Culture;
                return ents[i];
            }
            return Entity.Null;
        }
    }
}
