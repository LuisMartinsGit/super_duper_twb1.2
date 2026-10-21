// TopChoiceBar.cs
// Top-center controller for the two faction-level choices. Owns the gating
// logic; the visuals are authored prefabs:
// - SPECIAL BUILDING CHOICE: while the local faction has picked no special
//   yet (culture None, no Vault of Almiérra / Fiendstone Keep started),
//   one button per choice building (authored
//   SpecialBuildingChoiceMenu radial cluster, pinned top-center; code-built
//   fallback buttons when unassigned). Clicking enters placement mode; the
//   placement runtime enforces exclusivity and the building self-constructs.
//   Once any special is started the buttons disappear for the rest of the
//   match (design: Age_0.md § Special buildings).
// - AGE-UP READOUT: the authored CultureSelectionButton pill, a read-only
//   progress readout once a landmark is started — THE AGE-UP IS THE
//   LANDMARK (Age_0.md § Age-up by landmark), so there is no culture to
//   choose. The CultureSelectionMenu overlay it used to open was deleted
//   2026-10-20 (it was dead, and still popped up for observers).

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
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
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

        // Authored "SELECT CULTURE" pill (CultureSelectionButton prefab).
        private GameObject _authoredCultureButton;
        private Button _authoredCultureButtonControl;
        private TMP_Text _authoredCultureButtonLabel;
        private string _authoredCultureButtonText = "Select Culture";

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
        /// is re-anchored TOP-CENTER; the age-up pill is re-anchored
        /// top-center below the cluster.
        /// </summary>
        public void BindAuthoredMenus(GameObject specialMenu, GameObject cultureButton = null)
        {
            if (specialMenu != null) BindAuthoredSpecial(specialMenu);
            if (cultureButton != null) BindAuthoredCultureButton(cultureButton);
            if (_authoredCultureButton == null)
                TWBLog.Log("[GameUI] CultureSelectionButton prefab is not assigned — " +
                    "the age-up progress has no readout on the HUD.");
        }

        private void BindAuthoredSpecial(GameObject menu)
        {
            // The prefab root is authored inactive — binding works on the
            // inactive instance and Refresh activates it when it applies.
            _authoredSpecial = menu;

            // The ToggleGroup defusal: a Synty
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

            // The prefab's third radial slot stands for Thessara's Crossing,
            // the Runai landmark (Age_0.md § Age-up by landmark) — shown,
            // disabled until it exists.
            var pending = new List<string> { ThessarasCrossingId, "VaultOfAlmierra", "FiendstoneKeep" };
            foreach (var button in buttons)
            {
                string hint = button.gameObject.name.ToLowerInvariant();
                var text = button.GetComponentInChildren<TMP_Text>(true);
                if (text != null) hint += " " + text.text.ToLowerInvariant();

                string id = null;
                if (hint.Contains("shrine") || hint.Contains("thessara")) id = ThessarasCrossingId;
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
            if (id == ThessarasCrossingId) entry.Name = "Thessara's Crossing";
            // The demo edition strips the Keep's def (Editor/EditionContent),
            // so its locked button would otherwise read the bare id.
            if (id == "FiendstoneKeep") entry.Name = "Fiendstone Keep";
            if (TechCatalog.IsReady && TechCatalog.TryGetBuilding(id, out var def))
            {
                entry.Name = def.name ?? id;
                if (def.cost != null)
                    entry.Cost = Cost.Of(def.cost.Supplies, def.cost.Iron, def.cost.Veilstone);
            }
            button.onClick.AddListener(() =>
            {
                if (!WorkerCommandPanel.IsPlacingBuilding && LandmarkAvailable(entry.Id))
                    WorkerCommandPanel.TriggerBuildingPlacement(entry.Id);
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

            byte landmarkCulture = TheWaningBorder.Systems.Work.LandmarkAgeUp.CultureOf(entry.Id);
            if (landmarkCulture != Cultures.None)
                sb.Append('\n').Append(string.Format(
                    Loc.T("Building it ages you up to {0}."), CultureConfig.GetName(landmarkCulture)));
            if (!LandmarkAvailable(entry.Id))
            {
                sb.Append('\n').Append(Loc.T("<color=#C08040>Unavailable in the demo.</color>"));
                return sb.ToString();
            }

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
                Loc.T("<i>One landmark per faction — this choice is final. " +
                      "If it is destroyed before it is finished, everything spent is lost.</i>"));
            return sb.ToString();
        }

        /// <summary>Id of the Runai landmark's button. The building does not
        /// exist yet, so the catalog never knows it and the button stays
        /// disabled.</summary>
        private const string ThessarasCrossingId = "ThessarasCrossing";

        /// <summary>Can this landmark be placed in this build? It must age up
        /// into a culture, and that culture must ship (the demo is Alanthor
        /// only — CultureConfig.IsComingSoon). The executor re-checks.</summary>
        private static bool LandmarkAvailable(string id)
        {
            byte c = TheWaningBorder.Systems.Work.LandmarkAgeUp.CultureOf(id);
            return c != Cultures.None && !CultureConfig.IsComingSoon(c);
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

            _authoredCultureButtonControl.interactable = false;
            UITooltip.Bind(_authoredCultureButtonControl.gameObject, () =>
                "<b>" + Loc.T("Age-up") + "</b>\n"
                + Loc.T("Your landmark's construction is the age-up: when it finishes, the faction advances to Era 2."));
        }

        // ── Refresh ────────────────────────────────────────────────────────

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
            bool ok = world != null && world.IsCreated && !GameSettings.IsSpectating;
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
                return;
            }

            RefreshSpecials(em, faction);
            RefreshCultureButton(em, faction, hall);
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
                    if (!LandmarkAvailable(building.id)) continue;
                    if (used >= _specials.Length) break;

                    var b = _specials[used++];
                    var cost = building.cost != null
                        ? Cost.Of(building.cost.Supplies, building.cost.Iron, building.cost.Veilstone)
                        : default;
                    bool canAfford = FactionEconomy.CanAfford(em, faction, cost);
                    bool placing = WorkerCommandPanel.IsPlacingBuilding;

                    b.Root.SetActive(true);
                    b.Id = building.id;
                    b.Label.text = Loc.T(building.name);
                    b.Label.color = canAfford && !placing ? GameUIKit.Gold : GameUIKit.TextDim;
                    b.Sub.text = cost.IsZero ? Loc.T("Choose one")
                        : UIHelpers.FormatCostRich(cost, available);

                    string id = building.id;
                    b.Click = (canAfford && !placing)
                        ? (System.Action)(() => WorkerCommandPanel.TriggerBuildingPlacement(id))
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

            bool placing = WorkerCommandPanel.IsPlacingBuilding;
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
                    // stamped with the raw id and would have
                    // kept it for the whole match.
                    if (b.Label != null) b.Label.text = Loc.T(b.Name);
                }
                bool canAfford = FactionEconomy.CanAfford(em, faction, b.Cost);
                b.Button.interactable = LandmarkAvailable(b.Id) && canAfford && !placing;
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
            if (!visible) return;

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
                return;
            }

            // THE AGE-UP IS THE LANDMARK (Age_0.md § Age-up by landmark): the
            // pill no longer opens a culture choice. It reports the landmark's
            // construction, which IS the age-up progress.
            float progress = BuildingFactory.GetFactionChoiceBuildingProgress(em, faction);
            if (_authoredCultureButtonLabel != null)
                _authoredCultureButtonLabel.text = progress >= 0f
                    ? string.Format(Loc.T("Advancing {0}%"), (int)(progress * 100f))
                    : Loc.T(_authoredCultureButtonText);
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
