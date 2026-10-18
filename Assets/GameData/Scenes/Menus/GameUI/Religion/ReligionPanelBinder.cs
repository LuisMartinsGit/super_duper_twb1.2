// ReligionPanelBinder.cs
// The in-match RELIGION MENU (docs/Design/Religion_Menu.md, 2026-10-15).
//
// Docked to the right edge where the old Religion panel was. The authored
// ReligionPanel prefab only supplies the anchored root; everything inside is
// built here in code, because the menu's shape follows the rules (six sect
// rows, three power cells each, a building and a hero chip, set progress)
// rather than a fixed node list. Partial files:
//
//   ReligionPanelBinder.cs        layout, refresh, rows, casting, buying
//   ReligionPanelBinder.Popups.cs the sect picker and the inline confirm
//   ReligionPanelBinder.Tree.cs   the full-screen religion tree (T)
//
// Every purchase and cast goes through CommandRouter, which re-validates on
// every lockstep peer; the menu only decides what to offer and what to say.
//
// The whole menu (panel, side tab, hotkeys) appears only once the player
// has FINISHED a Temple of Ridan, and stays once a sect is adopted.
//
// Hotkeys: R shows / hides the menu, T opens the tree, F1-F6 cast power A of
// that row, Shift+F1-F6 power B, Ctrl+F1-F6 the wildcard (digits are control
// groups, so the rows use the F keys).

using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.UI.World;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ReligionPanelBinder : MonoBehaviour
    {
        // ── Layout (canvas units: the HUD canvas references 3840x2160) ─────
        private const float Width = 720f;
        private const float Pad = 24f;
        private const float Cell = 84f;
        private const float RowHeight = 124f;
        private const float RefreshInterval = 0.25f;
        private const float SlideSpeed = 4200f;

        private static readonly Color Cyan = new Color(0.37f, 0.84f, 0.90f, 1f);
        private static readonly Color CyanFill = new Color(0.37f, 0.84f, 0.90f, 0.16f);
        private static readonly Color GoldBadge = new Color(0.96f, 0.77f, 0.32f, 1f);
        private static readonly Color RedBadge = new Color(1f, 0.62f, 0.55f, 1f);
        private static readonly Color DimCell = new Color(0.07f, 0.10f, 0.16f, 0.95f);
        private static readonly Color BuyCell = new Color(0.09f, 0.13f, 0.23f, 0.95f);
        private static readonly Color Danger = new Color(0.45f, 0.10f, 0.08f, 0.85f);
        private static readonly Color[] ClusterColor =
        {
            new Color(0.56f, 0.75f, 0.60f, 1f),   // Alanthor
            new Color(0.78f, 0.70f, 0.54f, 1f),   // Runai
            new Color(0.85f, 0.51f, 0.37f, 1f),   // Feraldis
        };

        // ── Cached queries (never CreateEntityQuery on a repeating path) ────
        static readonly ComponentType[] QT_Buildings =
            { ComponentType.ReadOnly<BuildingTag>(), ComponentType.ReadOnly<FactionTag>() };
        static CachedEntityQuery QC_Buildings;
        static readonly ComponentType[] QT_Heroes =
            { ComponentType.ReadOnly<SectHeroTag>(), ComponentType.ReadOnly<FactionTag>(), ComponentType.ReadOnly<UnitTypeId>() };
        static CachedEntityQuery QC_Heroes;
        static readonly ComponentType[] QT_Temples =
            { ComponentType.ReadOnly<TempleOfRidanTag>(), ComponentType.ReadOnly<FactionTag>() };
        static CachedEntityQuery QC_Temples;

        // ── State ──────────────────────────────────────────────────────────
        private RectTransform _root;
        private RectTransform _body;
        private float _timer;
        private bool _shown = true;
        private float _slide;            // 0 = on screen, 1 = slid away
        private RectTransform _tab;
        private TMP_Text _tabText;

        private TMP_Text _rp, _rpProgress, _slotsText;
        private Image _rpBar;
        private Button _tithe;
        private TMP_Text _titheText;
        private readonly Image[] _slotDots = new Image[SectConfig.MaxAdoptedSects];
        private readonly TMP_Text[] _setText = new TMP_Text[4];
        private readonly Image[] _setRing = new Image[4];
        private RectTransform _banner;
        private readonly List<Row> _rows = new List<Row>();

        private EntityManager _em;
        private Faction _faction;
        private bool _templeStanding, _hasTemple;
        private bool _unlocked;   // a Temple has been finished (see Refresh)
        private Entity _temple;
        private int _rpBalance;
        private SectAdoptionState _state;
        private bool _hasState;
        private readonly string[] _rowSect = new string[SectConfig.MaxAdoptedSects];
        private readonly HashSet<string> _hadBuilding = new HashSet<string>();

        private sealed class Row
        {
            public RectTransform Root;
            public Button EmptyButton;
            public RectTransform Filled;
            public Image SectImage;
            public TMP_Text SectText;
            public Button LevelButton;
            public Image[] Bars = new Image[3];
            public TMP_Text LevelBadge;
            public Power[] Powers = new Power[3];
            public Button BuildingChip, HeroChip;
            public Image BuildingImage, HeroImage;
            public TMP_Text BuildingText, HeroText;
            public TMP_Text Caption;
        }

        private sealed class Power
        {
            public Button Button;
            public Image Background;
            public Image Sweep;
            public TMP_Text Label, Badge, Key, Seconds;
            public Image NoCast;
        }

        // ── Unity ──────────────────────────────────────────────────────────

        private void Awake()
        {
            _root = (RectTransform)transform;
            // The authored nodes belong to the old panel; the menu is built below.
            foreach (Transform child in transform) child.gameObject.SetActive(false);

            _root.anchorMin = _root.anchorMax = new Vector2(1f, 0.5f);
            _root.pivot = new Vector2(1f, 0.5f);
            _root.anchoredPosition = new Vector2(-30f, 0f);
            _root.sizeDelta = new Vector2(Width, 400f);

            _body = GameUIKit.Rect(_root, "Body");
            GameUIKit.Stretch(_body);
            GameUIKit.PanelChrome(_body);

            BuildHeader();
            BuildRows();
            BuildPopups();
            BuildSlideTab();
            BuildTree();
            Layout(6);

            // Hidden until the first Refresh finds a finished Temple, so the
            // menu never flashes up at match start.
            _body.gameObject.SetActive(false);
            _tab.gameObject.SetActive(false);
        }

        private void Update()
        {
            HandleHotkeys();
            AnimateSlide();
            UpdateCooldownSweeps();
            _timer += Time.unscaledDeltaTime;
            if (_timer < RefreshInterval) return;
            _timer = 0f;
            Refresh();
        }

        // ── Building the widgets ───────────────────────────────────────────

        private static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        private static TMP_Text Label(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left)
        {
            var t = GameUIKit.Text(parent, name, text, size, color, align, wrap: false);
            GameUIKit.Stretch(t.rectTransform);
            return t;
        }

        private static Button MakeButton(Transform parent, string name, out Image bg, System.Action onClick)
        {
            var rt = GameUIKit.Rect(parent, name);
            bg = rt.gameObject.AddComponent<Image>();
            bg.color = BuyCell;
            bg.raycastTarget = true;
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            if (onClick != null) b.onClick.AddListener(() => onClick());
            return b;
        }

        private void BuildHeader()
        {
            var title = GameUIKit.Text(_body, "Title", Loc.T("RELIGION"), 34f, GameUIKit.Gold);
            Place(title.rectTransform, Pad, 20f, 300f, 56f);

            var tree = MakeButton(_body, "TreeButton", out _, () => SetTreeOpen(true));
            Place((RectTransform)tree.transform, Width - Pad - 56f - 12f - 56f - 12f - 150f, 18f, 150f, 56f);
            Label(tree.transform, "Text", Loc.T("Tree") + "  <color=#A8A38F>T</color>", 26f, GameUIKit.Gold,
                TextAlignmentOptions.Center);
            UITooltip.Bind(tree.gameObject, Loc.T("The religion tree: every sect, what unlocks what. Hotkey T."));

            var temple = MakeButton(_body, "TempleButton", out _, GoToTemple);
            Place((RectTransform)temple.transform, Width - Pad - 56f - 12f - 56f, 18f, 56f, 56f);
            Label(temple.transform, "Text", "⌂", 30f, GameUIKit.Gold, TextAlignmentOptions.Center);
            UITooltip.Bind(temple.gameObject, Loc.T("Go to the Temple of Ridan"));

            var hide = MakeButton(_body, "HideButton", out var hideBg, () => SetShown(false));
            hideBg.color = new Color(0f, 0f, 0f, 0f);
            Place((RectTransform)hide.transform, Width - Pad - 56f, 18f, 56f, 56f);
            Label(hide.transform, "Text", "›", 34f, GameUIKit.TextMain, TextAlignmentOptions.Center);
            UITooltip.Bind(hide.gameObject, Loc.T("Hide the religion menu (R)"));

            // RP line
            _rp = GameUIKit.Text(_body, "RP", "", 40f, GoldBadge);
            Place(_rp.rectTransform, Pad, 94f, 170f, 60f);
            _rpProgress = GameUIKit.Text(_body, "RPProgress", "", 24f, new Color(0.72f, 0.60f, 0.85f));
            Place(_rpProgress.rectTransform, Pad + 180f, 92f, 300f, 34f);
            var barBg = GameUIKit.Image(_body, "RPBarBg", new Color(0.12f, 0.10f, 0.16f, 0.9f));
            Place(barBg.rectTransform, Pad + 180f, 130f, 300f, 12f);
            _rpBar = GameUIKit.Image(barBg.transform, "Fill", new Color(0.72f, 0.60f, 0.85f));
            _rpBar.rectTransform.anchorMin = Vector2.zero;
            _rpBar.rectTransform.anchorMax = new Vector2(0f, 1f);
            _rpBar.rectTransform.pivot = new Vector2(0f, 0.5f);
            _rpBar.rectTransform.offsetMin = _rpBar.rectTransform.offsetMax = Vector2.zero;
            UITooltip.Bind(barBg.gameObject, () => Loc.T("Kill curse creatures to earn Religion Points."));

            _tithe = MakeButton(_body, "Tithe", out _, () => Confirm(Loc.T("Tithe"),
                TitheLine(), ReligionPurchaseKind.Tithe, null, (RectTransform)_tithe.transform, finalWarning: false));
            Place((RectTransform)_tithe.transform, Width - Pad - 170f, 88f, 170f, 76f);
            _titheText = Label(_tithe.transform, "Text", "", 22f, GameUIKit.Gold, TextAlignmentOptions.Center);
            _titheText.textWrappingMode = TextWrappingModes.Normal;
            UITooltip.Bind(_tithe.gameObject, () => Loc.T("Tithe: buy one Religion Point with resources. The price rises each time. Needs a standing Temple."));

            // Slots
            for (int i = 0; i < _slotDots.Length; i++)
            {
                _slotDots[i] = GameUIKit.Image(_body, "Dot" + i, GameUIKit.TextMain);
                Place(_slotDots[i].rectTransform, Pad + i * 30f, 186f, 20f, 20f);
            }
            _slotsText = GameUIKit.Text(_body, "SlotsText", "", 24f, GameUIKit.TextDim);
            Place(_slotsText.rectTransform, Pad + 190f, 176f, 480f, 40f);

            // Set passives
            // Only the sets this edition can complete (Alpha_Build.md §4).
            int ringSlot = 0;
            for (int i = 0; i < 4; i++)
            {
                var set = (SectSet)(i + 1);
                if (!SetPassives.InEdition(set)) continue;
                _setRing[i] = GameUIKit.Image(_body, "Set" + i, DimCell, raycast: true);
                Place(_setRing[i].rectTransform, Pad + ringSlot++ * 170f, 224f, 158f, 74f);
                _setText[i] = Label(_setRing[i].transform, "Text", "", 22f, GameUIKit.TextMain, TextAlignmentOptions.Center);
                _setText[i].textWrappingMode = TextWrappingModes.Normal;
                UITooltip.Bind(_setRing[i].gameObject, () => Loc.T(SetPassives.Name(set)) + "\n" + Loc.T(SetPassives.Description(set)));
            }

            // Temple-down banner (hidden until needed). There is no no-Temple
            // block: the menu does not exist until a Temple is finished.
            _banner = GameUIKit.Rect(_body, "Banner");
            GameUIKit.Image(_banner, "Bg", Danger).rectTransform.anchorMin = Vector2.zero;
            var bbg = _banner.GetChild(0) as RectTransform; GameUIKit.Stretch(bbg);
            var bt = GameUIKit.Text(_banner, "Text", Loc.T("Temple destroyed. Rebuild it to cast sect powers and raise sect buildings."),
                22f, GameUIKit.TextMain);
            bt.rectTransform.anchorMin = Vector2.zero; bt.rectTransform.anchorMax = Vector2.one;
            bt.rectTransform.offsetMin = new Vector2(16f, 6f); bt.rectTransform.offsetMax = new Vector2(-180f, -6f);
            var rebuild = MakeButton(_banner, "Rebuild", out var rbg, () => WorkerCommandPanel.TriggerBuildingPlacement("TempleOfRidan"));
            rbg.color = GoldBadge;
            var rrt = (RectTransform)rebuild.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 0.5f); rrt.pivot = new Vector2(1f, 0.5f);
            rrt.anchoredPosition = new Vector2(-12f, 0f); rrt.sizeDelta = new Vector2(150f, 60f);
            Label(rebuild.transform, "Text", Loc.T("Rebuild"), 24f, new Color(0.1f, 0.08f, 0.02f), TextAlignmentOptions.Center);
            _banner.gameObject.SetActive(false);
        }

        private const float RowsTop = 320f;

        private void BuildRows()
        {
            for (int i = 0; i < SectConfig.MaxAdoptedSects; i++)
            {
                int rowIndex = i;
                var row = new Row { Root = GameUIKit.Rect(_body, "Row" + i) };
                Place(row.Root, Pad, RowsTop + i * RowHeight, Width - 2f * Pad, RowHeight - 10f);

                row.EmptyButton = MakeButton(row.Root, "Adopt", out var ebg, () => OpenPicker(rowIndex));
                ebg.color = new Color(0f, 0f, 0f, 0.25f);
                Place((RectTransform)row.EmptyButton.transform, 0f, 8f, Width - 2f * Pad, 84f);
                Label(row.EmptyButton.transform, "Text", "+  " + Loc.T("Adopt a sect"), 26f, GameUIKit.TextDim,
                    TextAlignmentOptions.Center);

                row.Filled = GameUIKit.Rect(row.Root, "Filled");
                Place(row.Filled, 0f, 0f, Width - 2f * Pad, RowHeight - 10f);

                var sect = MakeButton(row.Filled, "Sect", out row.SectImage, () => FocusSectBuilding(rowIndex));
                Place((RectTransform)sect.transform, 0f, 0f, Cell, Cell);
                row.SectText = Label(sect.transform, "Text", "", 22f, GameUIKit.TextMain, TextAlignmentOptions.Center);
                UITooltip.Bind(sect.gameObject, () => SectTooltip(rowIndex));

                row.LevelButton = MakeButton(row.Filled, "Level", out var lbg, () => ClickLevel(rowIndex));
                lbg.color = new Color(0f, 0f, 0f, 0f);
                Place((RectTransform)row.LevelButton.transform, Cell + 8f, 0f, 44f, Cell);
                for (int b = 0; b < 3; b++)
                {
                    row.Bars[b] = GameUIKit.Image(row.LevelButton.transform, "Bar" + b, GameUIKit.BarGold);
                    Place(row.Bars[b].rectTransform, 14f, 52f - b * 22f, 14f, 18f);
                }
                row.LevelBadge = GameUIKit.Text(row.LevelButton.transform, "Badge", "", 20f, GoldBadge,
                    TextAlignmentOptions.Center);
                Place(row.LevelBadge.rectTransform, 0f, 74f, 44f, 26f);
                UITooltip.Bind(row.LevelButton.gameObject, () => LevelTooltip(rowIndex));

                for (int p = 0; p < 3; p++)
                {
                    int slot = p + 1;
                    var pw = new Power();
                    pw.Button = MakeButton(row.Filled, "Power" + slot, out pw.Background, () => ClickPower(rowIndex, slot));
                    Place((RectTransform)pw.Button.transform, Cell + 60f + p * (Cell + 12f), 0f, Cell, Cell);
                    pw.Label = Label(pw.Button.transform, "Label", "", 18f, GameUIKit.TextMain, TextAlignmentOptions.Center);
                    pw.Label.textWrappingMode = TextWrappingModes.Normal;
                    pw.Sweep = GameUIKit.Image(pw.Button.transform, "Sweep", new Color(0f, 0f, 0f, 0.6f));
                    GameUIKit.Stretch(pw.Sweep.rectTransform);
                    pw.Sweep.type = Image.Type.Filled;
                    pw.Sweep.fillMethod = Image.FillMethod.Radial360;
                    pw.Sweep.fillOrigin = (int)Image.Origin360.Top;
                    pw.Sweep.fillClockwise = false;
                    pw.Sweep.fillAmount = 0f;
                    pw.Seconds = Label(pw.Button.transform, "Secs", "", 26f, Color.white, TextAlignmentOptions.Center);
                    pw.Key = GameUIKit.Text(pw.Button.transform, "Key", "", 16f, Cyan);
                    Place(pw.Key.rectTransform, 4f, Cell - 22f, Cell, 20f);
                    pw.Badge = GameUIKit.Text(pw.Button.transform, "Badge", "", 22f, GoldBadge, TextAlignmentOptions.Right);
                    Place(pw.Badge.rectTransform, 0f, Cell - 26f, Cell - 4f, 26f);
                    pw.NoCast = GameUIKit.Image(pw.Button.transform, "NoCast", new Color(1f, 0.35f, 0.35f, 0.9f));
                    pw.NoCast.rectTransform.anchorMin = pw.NoCast.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    pw.NoCast.rectTransform.sizeDelta = new Vector2(6f, Cell * 1.2f);
                    pw.NoCast.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    UITooltip.Bind(pw.Button.gameObject, () => PowerTooltip(rowIndex, slot));
                    row.Powers[p] = pw;
                }

                float chipX = Cell + 60f + 3f * (Cell + 12f) + 4f;
                row.BuildingChip = MakeButton(row.Filled, "Building", out row.BuildingImage, () => ClickBuilding(rowIndex));
                Place((RectTransform)row.BuildingChip.transform, chipX, 12f, 60f, 60f);
                row.BuildingText = Label(row.BuildingChip.transform, "Text", "", 22f, GameUIKit.TextMain, TextAlignmentOptions.Center);
                UITooltip.Bind(row.BuildingChip.gameObject, () => BuildingTooltip(rowIndex));

                row.HeroChip = MakeButton(row.Filled, "Hero", out row.HeroImage, () => ClickHero(rowIndex));
                Place((RectTransform)row.HeroChip.transform, chipX + 68f, 12f, 60f, 60f);
                row.HeroText = Label(row.HeroChip.transform, "Text", "", 24f, GoldBadge, TextAlignmentOptions.Center);
                UITooltip.Bind(row.HeroChip.gameObject, () => HeroTooltip(rowIndex));

                row.Caption = GameUIKit.Text(row.Filled, "Caption", "", 20f, GameUIKit.TextDim);
                Place(row.Caption.rectTransform, 0f, Cell + 4f, Width - 2f * Pad, 28f);
                row.Caption.overflowMode = TextOverflowModes.Ellipsis;

                _rows.Add(row);
            }
        }

        private void Layout(int rowsShown)
        {
            float h = RowsTop + rowsShown * RowHeight + 20f;
            if (_banner.gameObject.activeSelf) h += 104f;
            _root.sizeDelta = new Vector2(Width, h);
            float bannerShift = _banner.gameObject.activeSelf ? 104f : 0f;
            if (_banner.gameObject.activeSelf) Place(_banner, Pad, RowsTop, Width - 2f * Pad, 92f);
            for (int i = 0; i < _rows.Count; i++)
                Place(_rows[i].Root, Pad, RowsTop + bannerShift + i * RowHeight, Width - 2f * Pad, RowHeight - 10f);
        }

        // ── Slide tab (R) ──────────────────────────────────────────────────

        private void BuildSlideTab()
        {
            var tab = MakeButton(_root, "Tab", out var bg, () => SetShown(!_shown));
            bg.color = new Color(0.02f, 0.21f, 0.24f, 0.96f);
            _tab = (RectTransform)tab.transform;
            _tab.anchorMin = _tab.anchorMax = new Vector2(0f, 0.5f);
            _tab.pivot = new Vector2(1f, 0.5f);
            _tab.anchoredPosition = Vector2.zero;
            _tab.sizeDelta = new Vector2(64f, 220f);
            _tabText = Label(tab.transform, "Text", "", 26f, GoldBadge, TextAlignmentOptions.Center);
            _tabText.textWrappingMode = TextWrappingModes.Normal;
            UITooltip.Bind(tab.gameObject, Loc.T("Religion (R)"));
        }

        private void SetShown(bool shown)
        {
            _shown = shown;
            if (!shown) { ClosePopups(); }
        }

        private void AnimateSlide()
        {
            float target = _shown ? 0f : 1f;
            if (Mathf.Approximately(_slide, target)) return;
            _slide = Mathf.MoveTowards(_slide, target, Time.unscaledDeltaTime * SlideSpeed / Width);
            _root.anchoredPosition = new Vector2(-30f + _slide * (Width + 30f), 0f);
        }

        // ── Refresh ────────────────────────────────────────────────────────

        private bool TryWorld()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            _em = world.EntityManager;
            _faction = GameSettings.LocalPlayerFaction;
            return true;
        }

        private void Refresh()
        {
            bool live = TryWorld() && !GameSettings.IsSpectating;
            _root.gameObject.SetActive(true);
            if (live)
            {
                _hasTemple = TryGetTemple(out _temple);
                _templeStanding = SectQuery.HasStandingTemple(_em, _faction);
                _hasState = SectQuery.TryGetAdoptionState(_em, _faction, out _state);
            }

            // The menu exists only once a Temple is FINISHED (a site under
            // construction does not count) — or once a sect is adopted, so
            // losing the Temple later keeps the menu and shows the Rebuild
            // banner instead of taking the adopted sects' powers away.
            _unlocked = live && (_templeStanding || AnyAdopted());
            if (!_unlocked)
            {
                _body.gameObject.SetActive(false);
                _tab.gameObject.SetActive(false);
                if (_treeOpen) SetTreeOpen(false);
                ClosePopups();
                return;
            }
            _body.gameObject.SetActive(true);
            _tab.gameObject.SetActive(true);

            _rpBalance = FactionReligionPointsHelper.GetBalance(_em, _faction);

            // Header
            _rp.text = _rpBalance + " <size=60%>" + Loc.T("RP") + "</size>";
            var (have, need) = FactionReligionPointsHelper.PtsProgress(_em, _faction);
            bool feraldis = CultureConfig.GetCompletedCulture(_em, _faction) == Cultures.Feraldis;
            _rpProgress.text = string.Format(feraldis ? Loc.T("blood {0}/{1} to next RP") : Loc.T("curse {0}/{1} to next RP"), have, need);
            _rpBar.rectTransform.anchorMax = new Vector2(need > 0 ? Mathf.Clamp01((float)have / need) : 0f, 1f);
            var titheCost = FactionReligionPointsHelper.TitheCost(_em, _faction);
            _titheText.text = Loc.T("Tithe") + "\n<size=80%><color=#A8A38F>" + CostShort(titheCost) + "</color></size>";
            _tithe.interactable = ReligionPurchases.CanBuy(_em, _faction, ReligionPurchaseKind.Tithe, null, out _);
            _tabText.text = "‹\n" + _rpBalance + "\n<size=70%><color=#A8A38F>R</color></size>";

            // Slots and sets
            int used = 0;
            var counts = default(int3);
            if (_hasState)
            {
                counts = SetPassives.ClusterCounts(_state);
                used = counts.x + counts.y + counts.z;
            }
            for (int i = 0; i < _slotDots.Length; i++)
                _slotDots[i].color = i < used ? GameUIKit.TextMain : new Color(0.42f, 0.49f, 0.64f, 0.5f);
            _slotsText.text = string.Format(Loc.T("{0} of {1} sects · choices are final"), used, SectConfig.MaxAdoptedSects);
            var active = SetOf(counts);
            for (int i = 0; i < 4; i++)
            {
                var set = (SectSet)(i + 1);
                if (_setRing[i] == null) continue;
                bool done = active == set;
                bool reachable = SetPassives.StillReachable(counts, set);
                string progress = set == SectSet.Concord
                    ? counts.x + "·" + counts.y + "·" + counts.z
                    : (set == SectSet.Alanthor ? counts.x : set == SectSet.Runai ? counts.y : counts.z) + "/4";
                string name = Loc.T(SetPassives.Name(set));
                _setText[i].text = done
                    ? "<color=#F5C451><b>" + name + "</b></color>\n" + progress
                    : reachable ? name + "\n<b>" + progress + "</b>"
                                : "<color=#6E7A93><s>" + name + "</s>\n" + progress + "</color>";
                _setRing[i].color = done ? new Color(0.96f, 0.77f, 0.32f, 0.25f) : DimCell;
            }

            // Temple state: unlocked without a standing Temple = it was lost.
            _banner.gameObject.SetActive(!_templeStanding);

            // Rows: adopted sects in fixed sect order, then one empty row.
            int r = 0;
            for (int i = 0; i < SectConfig.SectCount && _hasState; i++)
                if (_state.Get(i).IsAdopted && r < _rowSect.Length) _rowSect[r++] = SectConfig.IdAt(i);
            int adoptedRows = r;
            for (; r < _rowSect.Length; r++) _rowSect[r] = null;

            int shown = math.min(SectConfig.MaxAdoptedSects, adoptedRows + (adoptedRows < SectConfig.MaxAdoptedSects ? 1 : 0));
            for (int i = 0; i < _rows.Count; i++)
            {
                bool visible = i < shown;
                _rows[i].Root.gameObject.SetActive(visible);
                if (!visible) continue;
                if (_rowSect[i] == null) RefreshEmptyRow(_rows[i]);
                else RefreshRow(_rows[i], i, _rowSect[i]);
            }
            Layout(shown);
            RefreshPopups();
            RefreshTree();
        }

        private bool AnyAdopted() => _hasState && _state.AdoptedCount() > 0;

        private static SectSet SetOf(int3 counts) => SetPassives.SetOf(counts);

        private void RefreshEmptyRow(Row row)
        {
            row.EmptyButton.gameObject.SetActive(true);
            row.Filled.gameObject.SetActive(false);
            row.EmptyButton.interactable = _templeStanding;
        }

        private void RefreshRow(Row row, int rowIndex, string sectId)
        {
            row.EmptyButton.gameObject.SetActive(false);
            row.Filled.gameObject.SetActive(true);
            int idx = SectConfig.IndexOf(sectId);
            var sect = _state.Get(idx);
            var color = ClusterColor[(int)SectConfig.ClusterOf(sectId)];

            row.SectImage.color = new Color(color.r * 0.25f, color.g * 0.25f, color.b * 0.25f, 0.95f);
            row.SectText.text = "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + Short(SectInfo.ShortName(sectId)) + "</color>";

            int level = sect.PowerLevel < 1 ? 1 : sect.PowerLevel;
            for (int b = 0; b < 3; b++)
                row.Bars[b].color = b < level ? GameUIKit.BarGold : new Color(0.2f, 0.26f, 0.37f, 1f);
            if (level < 3)
            {
                bool can = ReligionPurchases.CanBuy(_em, _faction, ReligionPurchaseKind.ChapelLevel, sectId, out int cost);
                if (cost <= 0) cost = level == 1 ? FactionReligionPointsHelper.Cfg.chapelLevel2Rp : FactionReligionPointsHelper.Cfg.chapelLevel3Rp;
                row.LevelBadge.text = "+" + cost;
                row.LevelBadge.color = can ? GoldBadge : RedBadge;
            }
            else row.LevelBadge.text = "";

            for (int p = 0; p < 3; p++) RefreshPower(row.Powers[p], rowIndex, sectId, p + 1, sect);
            RefreshBuildingChip(row, sectId);
            RefreshHeroChip(row, sectId);
        }

        private void RefreshPower(Power pw, int rowIndex, string sectId, int slot, PerSectState sect)
        {
            var spec = SectActivePowerHelper.CurrentSpec(_em, _faction, sectId, slot, out _);
            pw.Label.text = spec.Name != null ? Loc.T(spec.Name) : "—";
            bool owned = sect.IsSlotUnlocked(slot);
            pw.NoCast.gameObject.SetActive(owned && !_templeStanding);
            if (owned)
            {
                pw.Background.color = CyanFill;
                pw.Label.color = Cyan;
                pw.Badge.text = "";
                pw.Key.text = HotkeyLabel(rowIndex, slot);
            }
            else
            {
                var kind = ReligionPurchases.UnlockKindFor(slot);
                bool can = ReligionPurchases.CanBuy(_em, _faction, kind, sectId, out int cost);
                if (cost <= 0) cost = slot == 2 ? FactionReligionPointsHelper.Cfg.unlockSecondRp : FactionReligionPointsHelper.Cfg.unlockWildcardRp;
                pw.Background.color = can ? BuyCell : DimCell;
                pw.Label.color = can ? GameUIKit.TextMain : GameUIKit.TextDim;
                pw.Badge.text = cost.ToString();
                pw.Badge.color = can ? GoldBadge : RedBadge;
                pw.Key.text = "";
                pw.Sweep.fillAmount = 0f;
                pw.Seconds.text = "";
            }
        }

        private void UpdateCooldownSweeps()
        {
            if (!_hasState || !TryWorld()) return;
            for (int i = 0; i < _rows.Count; i++)
            {
                string sectId = _rowSect[i];
                if (sectId == null || !_rows[i].Root.gameObject.activeSelf) continue;
                var sect = _state.Get(SectConfig.IndexOf(sectId));
                for (int p = 0; p < 3; p++)
                {
                    int slot = p + 1;
                    var pw = _rows[i].Powers[p];
                    if (!sect.IsSlotUnlocked(slot)) continue;
                    float rem = SectActivePowerHelper.CooldownRemaining(_em, _faction, sectId, slot);
                    if (rem > 0f)
                    {
                        var spec = SectActivePowerHelper.CurrentSpec(_em, _faction, sectId, slot, out _);
                        float total = SectActivePowerHelper.EffectiveCooldown(_em, _faction, sectId, spec.Cooldown);
                        pw.Sweep.fillAmount = total > 0f ? Mathf.Clamp01(rem / total) : 0f;
                        pw.Seconds.text = Mathf.CeilToInt(rem) + "s";
                    }
                    else { pw.Sweep.fillAmount = 0f; pw.Seconds.text = ""; }
                }
            }
        }

        private static string HotkeyLabel(int rowIndex, int slot)
            => (slot == 2 ? "⇧" : slot == 3 ? "^" : "") + "F" + (rowIndex + 1);

        private static string Short(string name) => name.Length <= 5 ? name : name.Substring(0, 4) + ".";

        private static string CostShort(Cost c)
        {
            var parts = new List<string>(4);
            if (c.Supplies > 0) parts.Add(c.Supplies + " S");
            if (c.Iron > 0) parts.Add(c.Iron + " I");
            if (c.Veilstone > 0) parts.Add(c.Veilstone + " V");
            if (c.Veilsteel > 0) parts.Add(c.Veilsteel + " Vs");
            return parts.Count == 0 ? "—" : string.Join(" · ", parts);
        }

        private string TitheLine()
            => string.Format(Loc.T("Buy 1 Religion Point for {0}."), CostShort(FactionReligionPointsHelper.TitheCost(_em, _faction)));

        // ── The sect building and hero chips ───────────────────────────────

        private enum SiteState : byte { None, Planned, Building, Standing, Destroyed }

        private SiteState BuildingState(string sectId, out Entity building, out float progress)
        {
            building = Entity.Null;
            progress = 0f;
            string id = SectConfig.BuildingIdFor(sectId);
            if (id == null) return SiteState.None;
            SiteState best = SiteState.None;
            var q = QC_Buildings.Get(_em, QT_Buildings);
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != _faction) continue;
                    if (BuildCosts.IdFromEntity(_em, ents[i]) != id) continue;
                    if (_em.HasComponent<Health>(ents[i]) && _em.GetComponentData<Health>(ents[i]).Value <= 0) continue;
                    if (_em.HasComponent<UnderConstruction>(ents[i]))
                    {
                        var uc = _em.GetComponentData<UnderConstruction>(ents[i]);
                        progress = uc.Total > 0f ? uc.Progress / uc.Total : 0f;
                        building = ents[i];
                        best = SiteState.Building;
                    }
                    else { building = ents[i]; best = SiteState.Standing; break; }
                }
            }
            if (best == SiteState.None && TheWaningBorder.Entities.PlannedBuildings.CountOf(_em, _faction, id) > 0)
                best = SiteState.Planned;
            if (best == SiteState.Standing || best == SiteState.Building) _hadBuilding.Add(sectId);
            else if (best == SiteState.None && _hadBuilding.Contains(sectId)) best = SiteState.Destroyed;
            return best;
        }

        private void RefreshBuildingChip(Row row, string sectId)
        {
            var st = BuildingState(sectId, out _, out float progress);
            bool needsTemple = !_templeStanding && (st == SiteState.None || st == SiteState.Destroyed);
            row.BuildingChip.gameObject.SetActive(SectConfig.BuildingIdFor(sectId) != null);
            switch (st)
            {
                case SiteState.Standing: row.BuildingImage.color = CyanFill; row.BuildingText.text = "⌂"; break;
                case SiteState.Building: row.BuildingImage.color = BuyCell; row.BuildingText.text = Mathf.RoundToInt(progress * 100f) + "%"; break;
                case SiteState.Planned: row.BuildingImage.color = BuyCell; row.BuildingText.text = "…"; break;
                case SiteState.Destroyed: row.BuildingImage.color = Danger; row.BuildingText.text = "!"; break;
                default: row.BuildingImage.color = DimCell; row.BuildingText.text = "+"; break;
            }
            row.BuildingChip.interactable = !needsTemple;

            // Unit count and research for the caption.
            string unit = SectConfig.UnitIdFor(sectId);
            int units = unit != null ? SectHeroes.CountLiveOrQueued(_em, _faction, unit, 999) : 0;
            string unitName = unit != null && TechCatalog.TryGetUnit(unit, out var ud) ? ud.name : "?";
            string bName = SectConfig.BuildingIdFor(sectId) is string bid && TechCatalog.TryGetBuilding(bid, out var bd) ? bd.name : null;
            string research = ResearchIdFor(sectId);
            bool researched = research != null && FactionResearchState.Instance != null
                              && FactionResearchState.Instance.HasResearched(_faction, research);
            string stateWord = st switch
            {
                SiteState.Standing => bName,
                SiteState.Building => bName + " " + Mathf.RoundToInt(progress * 100f) + "%",
                SiteState.Planned => bName + " " + Loc.T("planned"),
                SiteState.Destroyed => "<color=#FF9E8C>" + bName + " " + Loc.T("destroyed") + "</color>",
                _ => bName != null ? bName + " " + Loc.T("not built") : null,
            };
            string limitText = SectHeroes.SectIdForLimitedUnit(unit) != null ? units + "/" + SectConfig.SectUnitLimit : units.ToString();
            row.Caption.text = (stateWord != null ? stateWord + " · " : "") + unitName + " " + limitText
                + (research != null ? " · " + ResearchName(research) + (researched ? " ✓" : " –") : "");
        }

        private void RefreshHeroChip(Row row, string sectId)
        {
            string hero = SectConfig.HeroIdFor(sectId);
            row.HeroChip.gameObject.SetActive(hero != null);
            if (hero == null) return;
            var live = FindHero(hero, out int level);
            int idx = SectConfig.IndexOf(sectId);
            bool recruitedOnce = _state.Get(idx).HeroRecruited != 0;
            if (live != Entity.Null)
            {
                row.HeroImage.color = new Color(0.16f, 0.14f, 0.06f, 0.95f);
                row.HeroText.text = level.ToString();
            }
            else if (SectHeroes.HasLiveOrQueued(_em, _faction, hero))
            {
                row.HeroImage.color = BuyCell;
                row.HeroText.text = "…";
            }
            else
            {
                row.HeroImage.color = recruitedOnce ? Danger : DimCell;
                row.HeroText.text = recruitedOnce ? "✕" : "+";
            }
        }

        private Entity FindHero(string heroId, out int level)
        {
            level = 0;
            var id = new FixedString64Bytes(heroId);
            var q = QC_Heroes.Get(_em, QT_Heroes);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (_em.GetComponentData<FactionTag>(ents[i]).Value != _faction) continue;
                if (_em.GetComponentData<UnitTypeId>(ents[i]).Value != id) continue;
                if (_em.HasComponent<Health>(ents[i]) && _em.GetComponentData<Health>(ents[i]).Value <= 0) continue;
                level = _em.HasComponent<HeroLevel>(ents[i]) ? _em.GetComponentData<HeroLevel>(ents[i]).Value : 1;
                return ents[i];
            }
            return Entity.Null;
        }

        private static string ResearchIdFor(string sectId)
        {
            string b = SectConfig.BuildingIdFor(sectId);
            if (b == null || !TechCatalog.TryGetBuilding(b, out var def) || def.research == null || def.research.Length == 0)
                return null;
            return def.research[0];
        }

        private static string ResearchName(string techId)
            => TechCatalog.TryGetTechnology(techId, out var t) && !string.IsNullOrEmpty(t.name) ? Loc.T(t.name) : techId;

        // ── Clicks ─────────────────────────────────────────────────────────

        private void ClickPower(int rowIndex, int slot)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null || !TryWorld()) return;
            if (!SectActivePowerHelper.IsSlotUnlocked(_em, _faction, sectId, slot))
            {
                var spec0 = SectActivePowerHelper.CurrentSpec(_em, _faction, sectId, slot, out _);
                Confirm(spec0.Name != null ? Loc.T(spec0.Name) : Loc.T("Power"),
                    Loc.T("Unlock this power. Final, cannot be undone."),
                    ReligionPurchases.UnlockKindFor(slot), sectId,
                    (RectTransform)_rows[rowIndex].Powers[slot - 1].Button.transform, finalWarning: true);
                return;
            }
            BeginCast(sectId, slot);
        }

        private void BeginCast(string sectId, int slot)
        {
            if (!TryWorld()) return;
            if (!_templeStanding) { PlayerNotificationSystem.NotifyError(Loc.T("Rebuild the Temple to cast sect powers")); return; }
            if (!SectActivePowerHelper.CanFire(_em, _faction, sectId, slot))
            {
                PlayerNotificationSystem.Notify(Loc.T("Not ready yet"));
                return;
            }
            var spec = SectActivePowerHelper.CurrentSpec(_em, _faction, sectId, slot, out _);
            var faction = _faction;
            if (SectActivePowerHelper.IsUntargeted(spec.Kind))
            {
                float3 at = _hasTemple && _em.Exists(_temple) ? _em.GetComponentData<LocalTransform>(_temple).Position : float3.zero;
                CommandRouter.IssueSectPower(_em, faction, sectId, slot, at);
                return;
            }
            // A SINGLE-TARGET power is cast ON the unit or building itself:
            // the player clicks it, and the ground or the wrong kind is refused
            // before anything is spent. Fire re-runs the same CheckTarget on
            // every peer before the cooldown (docs/Design/Spells.md §6).
            if (spec.Target != TheWaningBorder.Abilities.CastTargetKind.None)
            {
                var kind = spec.Kind;
                var targetKind = spec.Target;
                EntityTargeting.Begin(targetKind,
                    (em, target) => SectActivePowerHelper.CheckTarget(em, faction, kind, targetKind, target, out var why)
                        ? null : TheWaningBorder.Abilities.CastTargetRules.Describe(why, targetKind),
                    target =>
                    {
                        var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                        if (w == null || !w.IsCreated) return;
                        var em = w.EntityManager;
                        if (!em.Exists(target)) return;
                        CommandRouter.IssueSectPower(em, faction, sectId, slot,
                            em.GetComponentData<LocalTransform>(target).Position,
                            CommandSource.LocalPlayer, target);
                    });
                return;
            }

            float radius = spec.Radius > 0f ? spec.Radius : SectRadii.Small;
            GroundTargeting.Begin(radius, new Color(0.96f, 0.77f, 0.32f, 0.85f), target =>
            {
                var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (w == null || !w.IsCreated) return;
                CommandRouter.IssueSectPower(w.EntityManager, faction, sectId, slot, target);
            });
        }

        private void ClickLevel(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null || !TryWorld()) return;
            if (SectQuery.PowerLevelOf(_em, _faction, sectId) >= 3) return;
            Confirm(Loc.T("Raise chapel level"), Loc.T("Every unlocked power of this sect is cast one level higher."),
                ReligionPurchaseKind.ChapelLevel, sectId, (RectTransform)_rows[rowIndex].LevelButton.transform, finalWarning: true);
        }

        private void ClickBuilding(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null || !TryWorld()) return;
            var st = BuildingState(sectId, out var building, out _);
            if (st == SiteState.Standing || st == SiteState.Building)
            {
                Select(building);
                return;
            }
            if (!_templeStanding) { PlayerNotificationSystem.NotifyError(Loc.T("Sect buildings need a standing Temple of Ridan")); return; }
            string id = SectConfig.BuildingIdFor(sectId);
            if (id != null) WorkerCommandPanel.TriggerBuildingPlacement(id);
        }

        private void ClickHero(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            string hero = sectId != null ? SectConfig.HeroIdFor(sectId) : null;
            if (hero == null || !TryWorld()) return;
            var live = FindHero(hero, out _);
            if (live != Entity.Null) { Select(live); return; }
            // Recruited at the sect building: select it so its roster shows.
            var st = BuildingState(sectId, out var building, out _);
            if (st == SiteState.Standing) { Select(building); return; }
            PlayerNotificationSystem.NotifyError(string.Format(Loc.T("Raise the {0} first"),
                TechCatalog.TryGetBuilding(SectConfig.BuildingIdFor(sectId), out var bd) ? bd.name : Loc.T("sect building")));
        }

        private void FocusSectBuilding(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null || !TryWorld()) return;
            if (BuildingState(sectId, out var b, out _) is SiteState s && (s == SiteState.Standing || s == SiteState.Building))
                Select(b);
        }

        private void GoToTemple()
        {
            if (!TryWorld() || !TryGetTemple(out var t)) return;
            Select(t);
        }

        private void Select(Entity e)
        {
            if (e == Entity.Null || !_em.Exists(e)) return;
            TheWaningBorder.Input.SelectionSystem.ClearSelection();
            TheWaningBorder.Input.SelectionSystem.AddToSelection(e);
            if (_em.HasComponent<LocalTransform>(e))
                TheWaningBorder.CameraRig.CameraController.FocusOn(_em.GetComponentData<LocalTransform>(e).Position);
        }

        // ── Hotkeys ────────────────────────────────────────────────────────

        private void HandleHotkeys()
        {
            if (GameSettings.IsSpectating || !_unlocked) return;
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null
                && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null)
                return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.R)) SetShown(!_shown);
            if (UnityEngine.Input.GetKeyDown(KeyCode.T)) SetTreeOpen(!_treeOpen);
            bool shift = UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
            bool ctrl = UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
            for (int i = 0; i < SectConfig.MaxAdoptedSects; i++)
            {
                if (!UnityEngine.Input.GetKeyDown(KeyCode.F1 + i)) continue;
                string sectId = _rowSect[i];
                if (sectId == null) return;
                int slot = ctrl ? 3 : shift ? 2 : 1;
                if (TryWorld() && SectActivePowerHelper.IsSlotUnlocked(_em, _faction, sectId, slot))
                    BeginCast(sectId, slot);
                return;
            }
        }

        // ── Tooltips ───────────────────────────────────────────────────────

        private string SectTooltip(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null) return "";
            return "<b>" + Loc.T("Sect of") + " " + SectInfo.ShortName(sectId) + "</b>\n" + SectInfo.Lore(sectId)
                 + "\n\n" + SectInfo.PassiveDescription(sectId);
        }

        private string LevelTooltip(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null || !TryWorld()) return "";
            int level = SectQuery.PowerLevelOf(_em, _faction, sectId);
            string s = string.Format(Loc.T("Chapel level {0}. Every unlocked power of the sect is cast at this level."), SectInfo.Roman(level));
            if (SetPassives.ActiveSet(_em, _faction) == SectSet.Concord)
                s += "\n" + Loc.T("Concord: cast one level higher, up to III.");
            if (level < 3) s += "\n<color=#F5C451>" + Loc.T("Click to raise it.") + "</color>";
            return s;
        }

        private string PowerTooltip(int rowIndex, int slot)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null || !TryWorld()) return "";
            var spec = SectActivePowerHelper.CurrentSpec(_em, _faction, sectId, slot, out int level);
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>").Append(spec.Name != null ? Loc.T(spec.Name) : "?").Append("</b>  <color=#8FA8C0>")
              .Append(slot == 1 ? "A" : slot == 2 ? "B" : "★").Append(" · ").Append(SectInfo.ShortName(sectId))
              .Append(" · ").Append(Loc.T("Level")).Append(' ').Append(SectInfo.Roman(level)).Append("</color>\n");
            if (spec.Description != null) sb.Append(Loc.T(spec.Description)).Append('\n');
            sb.Append("<color=#A8A38F>").Append(Loc.T("Reach")).Append(": ").Append(SectRadii.Label(spec.Reach))
              .Append("   ").Append(Loc.T("Cooldown")).Append(": ")
              .Append(Mathf.RoundToInt(SectActivePowerHelper.EffectiveCooldown(_em, _faction, sectId, spec.Cooldown))).Append("s   ")
              .Append(Loc.T("Cast")).Append(": ").Append(SectActivePowerHelper.WindupFor(spec.Kind).ToString("0.#")).Append("s</color>");
            if (!SectActivePowerHelper.IsSlotUnlocked(_em, _faction, sectId, slot))
                sb.Append("\n<color=#F5C451>").Append(Loc.T("Click to unlock.")).Append("</color>");
            else
                sb.Append("\n<color=#8FE8F2>").Append(HotkeyLabel(rowIndex, slot)).Append("</color>");
            return sb.ToString();
        }

        private string BuildingTooltip(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null) return "";
            string s = SectInfo.BuildingDescription(sectId);
            string unit = SectInfo.SectUnitText(sectId);
            string research = SectInfo.ResearchText(sectId);
            if (unit != null) s += "\n\n" + unit;
            if (research != null) s += "\n" + research;
            if (!_templeStanding) s += "\n<color=#FF9E8C>" + Loc.T("Needs a standing Temple to build or rebuild.") + "</color>";
            return s;
        }

        private string HeroTooltip(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null) return "";
            string text = SectInfo.HeroText(sectId) ?? "";
            int rp = TryWorld() ? SectHeroes.RpDue(_em, _faction, sectId) : 0;
            return text + "\n<color=#A8A38F>" + (rp > 0
                ? string.Format(Loc.T("Recruit at the sect building: {0} RP + resources. Unique."), rp)
                : Loc.T("Revive at the sect building for its resources. Unique.")) + "</color>";
        }

        // ── Lookups ────────────────────────────────────────────────────────

        private bool TryGetTemple(out Entity temple)
        {
            temple = Entity.Null;
            var q = QC_Temples.Get(_em, QT_Temples);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (_em.GetComponentData<FactionTag>(ents[i]).Value == _faction)
                { temple = ents[i]; return true; }
            return false;
        }
    }
}
