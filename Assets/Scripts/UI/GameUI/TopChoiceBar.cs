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

        private RectTransform _bar;
        private ChoiceButton[] _specials;

        // ── Authored menus (BindAuthoredMenus) ─────────────────────────────
        private sealed class AuthoredSpecialButton
        {
            public string Id;
            public Button Button;
            public Cost Cost;
            public string Name;
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

        private void Awake()
        {
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
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            // Root is a 100x100 hub with the 380px radial buttons centered on
            // it — drop the hub far enough that the cluster clears the edge.
            rt.anchoredPosition = new Vector2(0f, -230f);

            // The shared name/cost caption (Label_FantasyMenus_Body instance).
            var labelNode = FindDeep(menu.transform, "Label_Button_Name");
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
            }
        }

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

            // Hover: surface name + cost in the shared caption.
            var relay = button.gameObject.AddComponent<UiClickRelay>();
            relay.OnEnter = () =>
            {
                if (_authoredSpecialLabel == null) return;
                var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                string cost = "";
                if (world != null && world.IsCreated && !entry.Cost.IsZero)
                    cost = "   " + UIHelpers.FormatCostRich(entry.Cost,
                        EntityActionExtractor.GetFactionResourcesAsCostPublic(
                            world.EntityManager, GameSettings.LocalPlayerFaction));
                _authoredSpecialLabel.text = entry.Name + cost;
            };
            relay.OnExit = () =>
            {
                if (_authoredSpecialLabel != null) _authoredSpecialLabel.text = "Choose one";
            };

            _authoredSpecialButtons.Add(entry);
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

            var instructions = FindDeep(menu.transform, "Label_Instructions");
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
                var panel = FindDeep(menu.transform, node);
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
            var label = GameUIKit.Text(cancel, "label", "Cancel (Esc)", 30f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
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

            var rt = (RectTransform)pill.transform;
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -24f);

            _authoredCultureButtonLabel = pill.GetComponentInChildren<TMP_Text>(true);
            if (_authoredCultureButtonLabel != null
                && !string.IsNullOrWhiteSpace(_authoredCultureButtonLabel.text))
                _authoredCultureButtonText = _authoredCultureButtonLabel.text;

            _authoredCultureButtonControl.onClick.AddListener(() =>
            {
                if (_cultureReady) OpenCultureMenu();
            });
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
                        ? "Choose your culture — this advances your faction to Era 2.   Cost: "
                        : "Not enough resources to advance.   Cost: ")
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
            text.text = "NOT AVAILABLE YET";
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

        private void Update()
        {
            if (AuthoredCultureOpen && UnityEngine.Input.GetKeyDown(KeyCode.Escape))
                CloseAuthoredCulture();

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
                    b.Label.text = building.name;
                    b.Label.color = canAfford && !placing ? GameUIKit.Gold : GameUIKit.TextDim;
                    b.Sub.text = cost.IsZero ? "Choose one"
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

            if (em.HasComponent<AgeUpState>(hall))
            {
                var s = em.GetComponentData<AgeUpState>(hall);
                float pct = s.Duration > 0f
                    ? Mathf.Clamp01((s.Duration - s.Remaining) / s.Duration) : 0f;
                if (_authoredCultureButtonLabel != null)
                    _authoredCultureButtonLabel.text = $"Advancing {(int)(pct * 100f)}%";
                _authoredCultureButtonControl.interactable = false;
                _cultureReady = false;
                return;
            }

            bool hasChoice = BuildingFactory.GetCompletedFactionChoiceBuilding(em, faction) != null;
            bool canAfford = FactionEconomy.CanAfford(em, faction, CultureConfig.AgeUpCost);
            _cultureReady = hasChoice && canAfford;

            if (_authoredCultureButtonLabel != null
                && _authoredCultureButtonLabel.text != _authoredCultureButtonText)
                _authoredCultureButtonLabel.text = _authoredCultureButtonText;
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
                PlayerNotificationSystem.NotifyError(
                    $"{CultureConfig.GetName(culture)} is coming soon");
                return;
            }

            Entity hall = FindLocalHall(em, faction, out byte current);
            if (hall == Entity.Null || current != Cultures.None) return;
            if (em.HasComponent<AgeUpState>(hall)) return;

            if (!FactionEconomy.Spend(em, faction, CultureConfig.AgeUpCost))
            {
                PlayerNotificationSystem.NotifyError("Not enough resources to advance");
                return;
            }

            CommandRouter.IssueAgeUp(em, hall, culture);
            CloseAuthoredCulture();
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return t;
            return null;
        }

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
