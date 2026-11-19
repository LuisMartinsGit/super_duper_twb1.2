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
// The whole menu (panel, side tab, hotkeys) is LOCKED IN AGE 0 (2026-11-18):
// while the player's faction has no culture the side tab stands dimmed with
// an "unlocks in Age 1" tooltip, and neither the tab nor R opens anything.
// From Age 1 it appears once the player has FINISHED a Temple of Ridan, and
// stays once a sect is adopted.
//
// THE SPLIT (2026-11-19): this menu TRACKS progress; powers are CAST from the
// always-visible column on the left edge (ReligionPanelBinder.CastBar.cs).
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
        // THE POWER RESERVES (2026-11-15, Religion_Menu.md § 0): a font at the
        // top fills as points are earned and pours into one VESSEL per sect;
        // each vessel's three powers sit beside it at the height that unlocks
        // them, and light up when the liquid reaches them.
        private const float Width = 860f;
        private const float Pad = 30f;
        private const float Cell = 76f;
        private const float TubeW = 44f;
        private const float TubeH = 520f;
        private const float DropH = 44f;
        private const float ColPitch = (Width - 2f * Pad) / 6f;
        private const float RowsTop = 276f;          // top of the vessels
        private const float ColHeight = 816f;        // drop + vessel + emblem + two buttons
        private const float RowHeight = 0f;          // (the old row pitch; the picker still names it)
        private const float TopMargin = 150f;        // below the score lines, clear of the minimap
        private const float RefreshInterval = 0.25f;
        private const float SlideSpeed = 4200f;

        private static readonly Color Cyan = new Color(0.37f, 0.84f, 0.90f, 1f);
        private static readonly Color CyanFill = new Color(0.37f, 0.84f, 0.90f, 0.16f);
        private static readonly Color GoldBadge = new Color(0.96f, 0.77f, 0.32f, 1f);
        private static readonly Color RedBadge = new Color(1f, 0.62f, 0.55f, 1f);
        private static readonly Color DimCell = new Color(0.07f, 0.10f, 0.16f, 0.95f);
        private static readonly Color BuyCell = new Color(0.09f, 0.13f, 0.23f, 0.95f);
        private static readonly Color Danger = new Color(0.45f, 0.10f, 0.08f, 0.85f);
        // Faith is gold: the liquid and whatever it has lit.
        private static readonly Color Faith = new Color(0.914f, 0.706f, 0.298f, 1f);
        private static readonly Color FaithBright = new Color(1f, 0.96f, 0.82f, 1f);
        private static readonly Color FaithInk = new Color(0.11f, 0.08f, 0f, 1f);
        private static readonly Color Pipe = new Color(0.54f, 0.45f, 0.21f, 1f);
        private static readonly Color PipeDry = new Color(0.11f, 0.28f, 0.31f, 1f);
        private static readonly Color VesselBg = new Color(0.03f, 0.13f, 0.16f, 1f);
        private static readonly Color LockedCell = new Color(0.04f, 0.16f, 0.20f, 1f);
        private static readonly Color LockedEdge = new Color(0.14f, 0.31f, 0.35f, 1f);
        private static readonly Color LockedInk = new Color(0.44f, 0.59f, 0.61f, 1f);
        private static readonly Color NextCell = new Color(0.06f, 0.22f, 0.27f, 1f);
        private static readonly Color NextInk = new Color(0.85f, 0.70f, 0.35f, 1f);
        private static readonly Color Pale = new Color(0.93f, 0.96f, 0.95f, 1f);
        private static readonly Color Teal = new Color(0.12f, 0.48f, 0.49f, 1f);
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
        private Image _tabBg;
        private Color _tabColor;
        /// <summary>The faction is still in Age 0: the tab shows, locked.</summary>
        private bool _ageLocked;
        /// <summary>Age 1 or later, but no standing Temple and no sect
        /// adopted: the tab stands locked too (developer, 2026-11-18).</summary>
        private bool _templeLocked;
        /// <summary>Slide the menu open the first time it unlocks after Age 0.</summary>
        private bool _openOnUnlock;

        private TMP_Text _rp, _fontText, _noSects;
        private Image _fontFill;
        private Button _tithe;
        private RectTransform _footer;
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

        /// <summary>One vessel: the pipe that feeds it, the tube, its three
        /// powers, the sect's emblem and its building and hero buttons.</summary>
        private sealed class Row
        {
            public RectTransform Root;
            public Image Drop, Drip, TubeBg, TubeFill;
            public readonly Image[] TubeEdges = new Image[3];
            public Button EmptyButton;
            public RectTransform Filled;
            public Image SectImage;
            public TMP_Text SectText;
            public TMP_Text Mark2, Mark3;
            public Image Mark2Line, Mark3Line;
            public Power[] Powers = new Power[3];
            public Button BuildingChip, HeroChip;
            public Image BuildingImage, HeroImage, BuildingIcon, HeroIcon;
            public EdgeFrame BuildingEdge, HeroEdge;
            public TMP_Text BuildingText, HeroText;
            public bool Feeding;
        }

        private sealed class Power
        {
            public Button Button;
            public Image Background;
            public EdgeFrame Edge;
            public Image Icon;
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

            // Hung from the TOP of the right edge, under the score lines, so
            // that however tall it grows it never reaches the minimap below.
            _root.anchorMin = _root.anchorMax = new Vector2(1f, 1f);
            _root.pivot = new Vector2(1f, 1f);
            _root.anchoredPosition = new Vector2(-30f, -TopMargin);
            _root.sizeDelta = new Vector2(Width, 400f);

            _body = GameUIKit.Rect(_root, "Body");
            GameUIKit.Stretch(_body);
            GameUIKit.PanelChrome(_body);

            BuildHeader();
            BuildRows();
            BuildPopups();
            BuildSlideTab();
            BuildTree();
            BuildCastBar();
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
            UpdateCastBar();
            AnimateDrips();
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

        /// <summary>A button's border: four thin bars. (The Outline effect
        /// repeats the whole quad, so on a see-through button it reads as a
        /// solid pale fill.)</summary>
        private sealed class EdgeFrame
        {
            public readonly Image[] Bars = new Image[4];
            public Color effectColor { set { for (int i = 0; i < 4; i++) Bars[i].color = value; } }
        }

        private static EdgeFrame Edge(Image img, Color color, float width = 3f)
        {
            var f = new EdgeFrame();
            for (int i = 0; i < 4; i++)
            {
                var bar = GameUIKit.Image(img.transform, "Edge" + i, color);
                var rt = bar.rectTransform;
                bool vertical = i < 2;
                rt.anchorMin = i == 1 ? new Vector2(1f, 0f) : i == 3 ? new Vector2(0f, 1f) : Vector2.zero;
                rt.anchorMax = i == 0 ? new Vector2(0f, 1f) : i == 2 ? new Vector2(1f, 0f) : Vector2.one;
                rt.pivot = new Vector2(i == 1 ? 1f : i == 0 ? 0f : 0.5f, i == 3 ? 1f : i == 2 ? 0f : 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = vertical ? new Vector2(width, 0f) : new Vector2(0f, width);
                f.Bars[i] = bar;
            }
            return f;
        }

        private void BuildHeader()
        {
            var title = GameUIKit.Text(_body, "Title", Loc.T("RELIGION"), 34f, GameUIKit.Gold);
            Place(title.rectTransform, Pad, 22f, 300f, 56f);

            var temple = MakeButton(_body, "TempleButton", out var tbg, GoToTemple);
            tbg.color = new Color(0f, 0f, 0f, 0.2f);
            Edge(tbg, LockedEdge);
            Place((RectTransform)temple.transform, Width - Pad - Cell - 12f - Cell, 14f, Cell, Cell);
            IconImage(temple.transform, "temple", 44f, GameUIKit.Gold);
            UITooltip.Bind(temple.gameObject, Loc.T("Go to the Temple of Ridan"));

            var hide = MakeButton(_body, "HideButton", out var hideBg, () => SetShown(false));
            hideBg.color = new Color(0f, 0f, 0f, 0.2f);
            Edge(hideBg, LockedEdge);
            Place((RectTransform)hide.transform, Width - Pad - Cell, 14f, Cell, Cell);
            IconImage(hide.transform, "hide", 44f, GameUIKit.TextMain);
            UITooltip.Bind(hide.gameObject, Loc.T("Hide the religion menu (R)"));

            // ── The font: fills toward the next point ──
            float fontW = Width - 2f * Pad - 220f;
            var font = GameUIKit.Image(_body, "Font", VesselBg, raycast: true);
            Place(font.rectTransform, Pad, 104f, fontW, 96f);
            _fontFill = GameUIKit.Image(font.transform, "Fill", Faith);
            _fontFill.rectTransform.anchorMin = Vector2.zero;
            _fontFill.rectTransform.anchorMax = new Vector2(1f, 0f);
            _fontFill.rectTransform.offsetMin = _fontFill.rectTransform.offsetMax = Vector2.zero;
            var fontTop = GameUIKit.Image(_fontFill.transform, "Surface", FaithBright);
            fontTop.rectTransform.anchorMin = new Vector2(0f, 1f); fontTop.rectTransform.anchorMax = Vector2.one;
            fontTop.rectTransform.pivot = new Vector2(0.5f, 1f);
            fontTop.rectTransform.offsetMin = new Vector2(0f, -6f); fontTop.rectTransform.offsetMax = Vector2.zero;
            for (int e = 0; e < 3; e++)
            {
                var edge = GameUIKit.Image(font.transform, "Edge" + e, Pipe);
                var rt = edge.rectTransform;
                if (e == 0) { rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 0.5f); rt.sizeDelta = new Vector2(4f, 0f); }
                else if (e == 1) { rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(1f, 0.5f); rt.sizeDelta = new Vector2(4f, 0f); }
                else { rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f); rt.sizeDelta = new Vector2(0f, 4f); }
                rt.anchoredPosition = Vector2.zero;
            }
            var plate = GameUIKit.Image(font.transform, "Plate", new Color(0f, 0f, 0f, 0.55f));
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = plate.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            plate.rectTransform.anchoredPosition = Vector2.zero;
            plate.rectTransform.sizeDelta = new Vector2(290f, 42f);
            _fontText = Label(plate.transform, "Text", "", 24f, Color.white, TextAlignmentOptions.Center);
            _fontText.fontStyle = FontStyles.Bold;
            UITooltip.Bind(font.gameObject, () => Loc.T("The font fills as you earn: curse kills, sect units' damage, monuments and Sanctums. Each time it is full it pours one point, split equally between the sects still filling."));
            var fontCaption = GameUIKit.Text(_body, "FontCaption", Loc.T("Each full font pours one point into your sects"),
                20f, GameUIKit.TextDim, TextAlignmentOptions.Center, wrap: false);
            Place(fontCaption.rectTransform, Pad, 204f, fontW, 30f);

            // ── The balance: what there is to SPEND (units, heroes, the Temple) ──
            float bx = Pad + fontW + 16f;
            var star = GameUIKit.Image(_body, "Star", Faith, raycast: true);
            Place(star.rectTransform, bx, 120f, 44f, 44f);
            SetIcon(star, "star");
            UITooltip.Bind(star.gameObject, () => Loc.T("Religion points to spend: sect units, heroes, and trades at the Temple. Spending never lowers a reserve."));
            _rp = GameUIKit.Text(_body, "RP", "", 48f, Faith, TextAlignmentOptions.Left, wrap: false);
            Place(_rp.rectTransform, bx + 50f, 108f, 76f, 64f);
            _rp.fontStyle = FontStyles.Bold;
            _tithe = MakeButton(_body, "Tithe", out var titheBg, () => Confirm(Loc.T("Tithe"),
                TitheLine(), ReligionPurchaseKind.Tithe, null, (RectTransform)_tithe.transform, finalWarning: false));
            titheBg.color = new Color(0f, 0f, 0f, 0.2f);
            Edge(titheBg, Pale);
            Place((RectTransform)_tithe.transform, Width - Pad - Cell, 104f, Cell, Cell);
            IconImage(_tithe.transform, "tithe", 46f, Pale);
            UITooltip.Bind(_tithe.gameObject, () => TitheLine() + "\n" + Loc.T("The price rises each time. Needs a standing Temple."));
            var toSpend = GameUIKit.Text(_body, "ToSpend", Loc.T("to spend"), 20f, GameUIKit.TextDim, TextAlignmentOptions.Left, wrap: false);
            Place(toSpend.rectTransform, bx, 172f, 130f, 28f);

            // ── The pipes: one stem down from the font, one manifold across ──
            var stem = GameUIKit.Image(_body, "Stem", Pipe);
            Place(stem.rectTransform, Pad + fontW * 0.5f - 5f, 200f, 10f, 66f);
            // (drawn under the caption: it is a 10-unit line)
            stem.transform.SetSiblingIndex(fontCaption.transform.GetSiblingIndex());
            var manifold = GameUIKit.Image(_body, "Manifold", Pipe);
            Place(manifold.rectTransform, Pad + TubeW * 0.5f - 5f, RowsTop - 10f, 5f * ColPitch + 10f, 10f);

            // ── Nothing adopted yet ──
            _noSects = GameUIKit.Text(_body, "NoSects",
                Loc.T("Adopt a sect and the font starts filling its reserve.\nIts powers light up as the reserve rises."),
                24f, GameUIKit.TextDim, TextAlignmentOptions.Center);
            Place(_noSects.rectTransform, Pad, RowsTop + DropH + TubeH + 30f, Width - 2f * Pad, 80f);

            // ── Footer: the set bonus ──
            _footer = GameUIKit.Rect(_body, "Footer");
            var rule = GameUIKit.Image(_footer, "Rule", LockedEdge);
            Place(rule.rectTransform, 0f, 0f, Width - 2f * Pad, 2f);
            // Only the sets this edition can complete (Alpha_Build.md §4).
            int ringSlot = 0, rings = 0;
            for (int i = 0; i < 4; i++) if (SetPassives.InEdition((SectSet)(i + 1))) rings++;
            float ringW = (Width - 2f * Pad - (rings - 1) * 12f) / Mathf.Max(1, rings);
            for (int i = 0; i < 4; i++)
            {
                var set = (SectSet)(i + 1);
                if (!SetPassives.InEdition(set)) continue;
                _setRing[i] = GameUIKit.Image(_footer, "Set" + i, new Color(0f, 0f, 0f, 0f), raycast: true);
                Place(_setRing[i].rectTransform, ringSlot++ * (ringW + 12f), 10f, ringW, 56f);
                _setText[i] = Label(_setRing[i].transform, "Text", "", 22f, GameUIKit.TextMain, TextAlignmentOptions.Center);
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
            var rebuild = MakeButton(_banner, "Rebuild", out var rbg, RebuildTemple);
            rbg.color = GoldBadge;
            var rrt = (RectTransform)rebuild.transform;
            rrt.anchorMin = rrt.anchorMax = new Vector2(1f, 0.5f); rrt.pivot = new Vector2(1f, 0.5f);
            rrt.anchoredPosition = new Vector2(-12f, 0f); rrt.sizeDelta = new Vector2(150f, 60f);
            Label(rebuild.transform, "Text", Loc.T("Rebuild"), 24f, new Color(0.1f, 0.08f, 0.02f), TextAlignmentOptions.Center);
            _banner.gameObject.SetActive(false);
        }

        /// <summary>
        /// The banner's Rebuild: a fallen Temple is built again only on its
        /// rubble, from the button over it (decision 69) — so the camera goes
        /// there and the player is told how. With no rubble (none ever
        /// finished), the ordinary placement.
        /// </summary>
        private void RebuildTemple()
        {
            var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (w != null && w.IsCreated
                && TheWaningBorder.Entities.KeystoneRubbles.TryFind(w.EntityManager, _faction,
                       TheWaningBorder.Entities.KeystoneRubbles.TempleId, out float3 at, out _))
            {
                TheWaningBorder.CameraRig.CameraController.FocusOn(at);
                PlayerNotificationSystem.Notify(Loc.T("Select Workers and use the button over the Temple's rubble to rebuild it."));
                return;
            }
            WorkerCommandPanel.TriggerBuildingPlacement("TempleOfRidan");
        }

        /// <summary>Top of a power button inside its column: the height on the
        /// vessel where <paramref name="fraction"/> of the reserve is reached.</summary>
        private static float PowerY(float fraction)
            => DropH + TubeH * (1f - Mathf.Clamp01(fraction)) - Cell * 0.5f;

        private void BuildRows()
        {
            float px = TubeW + 8f;   // powers and buttons stand to the right of the tube
            for (int i = 0; i < SectConfig.MaxAdoptedSects; i++)
            {
                int rowIndex = i;
                var row = new Row { Root = GameUIKit.Rect(_body, "Vessel" + i) };
                Place(row.Root, Pad + i * ColPitch, RowsTop, ColPitch, ColHeight);

                // The pipe down from the manifold, and the drip that runs in it.
                row.Drop = GameUIKit.Image(row.Root, "Drop", PipeDry);
                Place(row.Drop.rectTransform, TubeW * 0.5f - 5f, 0f, 10f, DropH);
                row.Drip = GameUIKit.Image(row.Drop.transform, "Drip", FaithBright);
                Place(row.Drip.rectTransform, 0f, 0f, 10f, 16f);

                // The vessel.
                row.TubeBg = GameUIKit.Image(row.Root, "Tube", VesselBg, raycast: true);
                Place(row.TubeBg.rectTransform, 0f, DropH, TubeW, TubeH);
                UITooltip.Bind(row.TubeBg.gameObject, () => VesselTooltip(rowIndex));
                row.TubeFill = GameUIKit.Image(row.TubeBg.transform, "Fill", Faith);
                row.TubeFill.rectTransform.anchorMin = Vector2.zero;
                row.TubeFill.rectTransform.anchorMax = new Vector2(1f, 0f);
                row.TubeFill.rectTransform.offsetMin = row.TubeFill.rectTransform.offsetMax = Vector2.zero;
                var surface = GameUIKit.Image(row.TubeFill.transform, "Surface", FaithBright);
                surface.rectTransform.anchorMin = new Vector2(0f, 1f); surface.rectTransform.anchorMax = Vector2.one;
                surface.rectTransform.pivot = new Vector2(0.5f, 1f);
                surface.rectTransform.offsetMin = new Vector2(0f, -6f); surface.rectTransform.offsetMax = Vector2.zero;
                for (int e = 0; e < 3; e++)
                {
                    var edge = GameUIKit.Image(row.TubeBg.transform, "Edge" + e, Pipe);
                    var rt = edge.rectTransform;
                    if (e == 0) { rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 0.5f); rt.sizeDelta = new Vector2(4f, 0f); }
                    else if (e == 1) { rt.anchorMin = new Vector2(1f, 0f); rt.anchorMax = Vector2.one; rt.pivot = new Vector2(1f, 0.5f); rt.sizeDelta = new Vector2(4f, 0f); }
                    else { rt.anchorMin = Vector2.zero; rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f); rt.sizeDelta = new Vector2(0f, 4f); }
                    rt.anchoredPosition = Vector2.zero;
                    row.TubeEdges[e] = edge;
                }

                // An empty vessel: the first one carries the adopt button.
                row.EmptyButton = MakeButton(row.Root, "Adopt", out var ebg, () => OpenPicker(rowIndex));
                ebg.color = new Color(0f, 0f, 0f, 0.2f);
                Edge(ebg, Pale);
                Place((RectTransform)row.EmptyButton.transform, px, DropH + TubeH - Cell, Cell, Cell);
                IconImage(row.EmptyButton.transform, "plus", 44f, Pale);
                UITooltip.Bind(row.EmptyButton.gameObject, Loc.T("Adopt a sect. Free; a sect cannot be given up."));

                row.Filled = GameUIKit.Rect(row.Root, "Filled");
                Place(row.Filled, 0f, 0f, ColPitch, ColHeight);

                // The level marks on the vessel.
                row.Mark2Line = GameUIKit.Image(row.Filled, "Mark2Line", LockedEdge);
                row.Mark2 = GameUIKit.Text(row.Filled, "Mark2", "II", 22f, LockedInk, TextAlignmentOptions.Left, wrap: false);
                row.Mark3Line = GameUIKit.Image(row.Filled, "Mark3Line", LockedEdge);
                row.Mark3 = GameUIKit.Text(row.Filled, "Mark3", "III", 22f, LockedInk, TextAlignmentOptions.Left, wrap: false);
                row.Mark2.fontStyle = row.Mark3.fontStyle = FontStyles.Bold;

                for (int p = 0; p < 3; p++)
                {
                    int slot = p + 1;
                    var pw = new Power();
                    pw.Button = MakeButton(row.Filled, "Power" + slot, out pw.Background, () => ClickPower(rowIndex, slot));
                    Place((RectTransform)pw.Button.transform, px, PowerY(p == 0 ? 0.1f : p == 1 ? 0.3f : 0.7f), Cell, Cell);
                    pw.Edge = Edge(pw.Background, LockedEdge);
                    pw.Icon = IconImage(pw.Button.transform, null, 48f, LockedInk);
                    pw.Icon.rectTransform.anchoredPosition = new Vector2(0f, 6f);
                    pw.Label = Label(pw.Button.transform, "Label", "", 16f, GameUIKit.TextMain, TextAlignmentOptions.Center);
                    pw.Label.textWrappingMode = TextWrappingModes.Normal;
                    pw.Sweep = GameUIKit.Image(pw.Button.transform, "Sweep", new Color(0f, 0f, 0f, 0.6f));
                    GameUIKit.Stretch(pw.Sweep.rectTransform);
                    pw.Sweep.type = Image.Type.Filled;
                    pw.Sweep.fillMethod = Image.FillMethod.Radial360;
                    pw.Sweep.fillOrigin = (int)Image.Origin360.Top;
                    pw.Sweep.fillClockwise = false;
                    pw.Sweep.fillAmount = 0f;
                    pw.Seconds = Label(pw.Button.transform, "Secs", "", 26f, Color.white, TextAlignmentOptions.Center);
                    pw.Key = GameUIKit.Text(pw.Button.transform, "Key", "", 15f, FaithInk, TextAlignmentOptions.Center, wrap: false);
                    Place(pw.Key.rectTransform, 0f, Cell - 20f, Cell, 18f);
                    pw.Key.fontStyle = FontStyles.Bold;
                    pw.Badge = GameUIKit.Text(pw.Button.transform, "Badge", "", 17f, NextInk, TextAlignmentOptions.Center, wrap: false);
                    Place(pw.Badge.rectTransform, 0f, Cell - 22f, Cell, 20f);
                    pw.NoCast = GameUIKit.Image(pw.Button.transform, "NoCast", new Color(1f, 0.35f, 0.35f, 0.9f));
                    pw.NoCast.rectTransform.anchorMin = pw.NoCast.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                    pw.NoCast.rectTransform.sizeDelta = new Vector2(6f, Cell * 1.2f);
                    pw.NoCast.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
                    UITooltip.Bind(pw.Button.gameObject, () => PowerTooltip(rowIndex, slot));
                    row.Powers[p] = pw;
                }

                // Under the vessel: the sect, its building, its hero.
                float below = DropH + TubeH + 10f;
                float cx = (ColPitch - Cell) * 0.5f;
                var sect = MakeButton(row.Filled, "Sect", out var sbg, () => FocusSectBuilding(rowIndex));
                sbg.color = new Color(0f, 0f, 0f, 0f);
                Place((RectTransform)sect.transform, 0f, below, ColPitch - 6f, 76f);
                row.SectImage = GameUIKit.Image(sect.transform, "Emblem", Faith);
                Place(row.SectImage.rectTransform, (ColPitch - 6f - 44f) * 0.5f, 0f, 44f, 44f);
                row.SectText = GameUIKit.Text(sect.transform, "Text", "", 19f, GameUIKit.TextMain, TextAlignmentOptions.Center, wrap: false);
                Place(row.SectText.rectTransform, -6f, 46f, ColPitch + 6f, 28f);
                row.SectText.overflowMode = TextOverflowModes.Ellipsis;
                UITooltip.Bind(sect.gameObject, () => SectTooltip(rowIndex));

                row.BuildingChip = MakeButton(row.Filled, "Building", out row.BuildingImage, () => ClickBuilding(rowIndex));
                Place((RectTransform)row.BuildingChip.transform, cx, below + 84f, Cell, Cell);
                row.BuildingEdge = Edge(row.BuildingImage, Pale);
                row.BuildingIcon = IconImage(row.BuildingChip.transform, "building", 44f, Pale);
                row.BuildingText = Label(row.BuildingChip.transform, "Text", "", 24f, Color.white, TextAlignmentOptions.Center);
                row.BuildingText.fontStyle = FontStyles.Bold;
                UITooltip.Bind(row.BuildingChip.gameObject, () => BuildingTooltip(rowIndex));

                row.HeroChip = MakeButton(row.Filled, "Hero", out row.HeroImage, () => ClickHero(rowIndex));
                Place((RectTransform)row.HeroChip.transform, cx, below + 84f + Cell + 10f, Cell, Cell);
                row.HeroEdge = Edge(row.HeroImage, Pale);
                row.HeroIcon = IconImage(row.HeroChip.transform, "hero", 44f, Pale);
                row.HeroText = Label(row.HeroChip.transform, "Text", "", 26f, GoldBadge, TextAlignmentOptions.Center);
                row.HeroText.fontStyle = FontStyles.Bold;
                UITooltip.Bind(row.HeroChip.gameObject, () => HeroTooltip(rowIndex));

                _rows.Add(row);
            }
        }

        private void Layout(int unused)
        {
            float y = RowsTop + ColHeight + 12f;
            bool banner = _banner.gameObject.activeSelf;
            if (banner) { Place(_banner, Pad, y, Width - 2f * Pad, 92f); y += 104f; }
            Place(_footer, Pad, y, Width - 2f * Pad, 70f);
            _root.sizeDelta = new Vector2(Width, y + 70f + 16f);
        }

        /// <summary>The drip in each feeding pipe (unscaled time: it runs while paused menus are up).</summary>
        private void AnimateDrips()
        {
            if (!_unlocked || _slide >= 1f) return;
            float t = Mathf.Repeat(Time.unscaledTime, 1.6f) / 1.6f;
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                if (!row.Feeding) continue;
                row.Drip.rectTransform.anchoredPosition = new Vector2(0f, -(t * (DropH + 16f) - 16f));
            }
        }

        // ── Slide tab (R) ──────────────────────────────────────────────────

        private void BuildSlideTab()
        {
            // Locked in Age 0: the tab answers with its tooltip only.
            var tab = MakeButton(_root, "Tab", out var bg, () => { if (_unlocked) SetShown(!_shown); });
            bg.color = new Color(0.02f, 0.21f, 0.24f, 0.96f);
            _tabBg = bg;
            _tabColor = bg.color;
            _tab = (RectTransform)tab.transform;
            _tab.anchorMin = _tab.anchorMax = new Vector2(0f, 0.5f);
            _tab.pivot = new Vector2(1f, 0.5f);
            _tab.anchoredPosition = Vector2.zero;
            _tab.sizeDelta = new Vector2(64f, 220f);
            _tabText = Label(tab.transform, "Text", "", 26f, GoldBadge, TextAlignmentOptions.Center);
            _tabText.textWrappingMode = TextWrappingModes.Normal;
            UITooltip.Bind(tab.gameObject, () => _ageLocked
                ? Loc.T("Religion (R)") + "\n" + Loc.T("Unlocks in Age 1.")
                : _templeLocked
                    ? Loc.T("Religion (R)") + "\n" + Loc.T("Build a Temple of Ridan to unlock it.")
                    : Loc.T("Religion (R)"));
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
            _root.anchoredPosition = new Vector2(-30f + _slide * (Width + 30f), -TopMargin);
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
            // LOCKED UNTIL AGE 1 (2026-11-18, Religion_Menu.md § Unlocking):
            // in Age 0 the menu cannot open at all — the tab stands dimmed at
            // the screen edge, saying when it unlocks; a Temple built in Age 0
            // still pays its points, they wait for the menu.
            _ageLocked = live && CultureConfig.GetCompletedCulture(_em, _faction) == Cultures.None;
            _unlocked = live && !_ageLocked && (_templeStanding || AnyAdopted());
            _templeLocked = live && !_ageLocked && !_unlocked;
            if (!_unlocked)
            {
                _body.gameObject.SetActive(false);
                if (_treeOpen) SetTreeOpen(false);
                ClosePopups();
                if (_ageLocked || _templeLocked)
                {
                    // Slid away, only the locked tab showing; it slides open
                    // by itself the first time the menu unlocks.
                    _shown = false;
                    _openOnUnlock = true;
                    _slide = 1f;
                    _root.anchoredPosition = new Vector2(-30f + Width + 30f, -TopMargin);
                    _tabBg.color = new Color(_tabColor.r * 0.45f, _tabColor.g * 0.45f, _tabColor.b * 0.45f, _tabColor.a);
                    _tabText.text = "<color=#6E7A93>R\n<size=70%>"
                        + (_ageLocked ? Loc.T("Age 1") : Loc.T("Temple")) + "</size></color>";
                    _tab.gameObject.SetActive(true);
                }
                else _tab.gameObject.SetActive(false);
                HideCastBar();
                return;
            }
            if (_openOnUnlock) { _openOnUnlock = false; _shown = true; }
            _tabBg.color = _tabColor;
            _body.gameObject.SetActive(true);
            _tab.gameObject.SetActive(true);

            _rpBalance = FactionReligionPointsHelper.GetBalance(_em, _faction);

            // Header: the balance to spend, and the font toward the next point.
            _rp.text = _rpBalance.ToString();
            var (have, need) = FactionReligionPointsHelper.PtsProgress(_em, _faction);
            float toPoint = need > 0 ? Mathf.Clamp01((float)have / need) : 0f;
            _fontFill.rectTransform.anchorMax = new Vector2(1f, toPoint);
            _fontFill.gameObject.SetActive(toPoint > 0.005f);
            _fontText.text = string.Format(Loc.T("Next point {0}%"), Mathf.FloorToInt(toPoint * 100f));
            _tithe.interactable = ReligionPurchases.CanBuy(_em, _faction, ReligionPurchaseKind.Tithe, null, out _);
            _tabText.text = "‹\n" + _rpBalance + "\n<size=70%><color=#A8A38F>R</color></size>";

            // Sets
            var counts = default(int3);
            if (_hasState) counts = SetPassives.ClusterCounts(_state);
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
                    ? "<color=#F5C451><b>" + name + "</b></color>   " + progress
                    : reachable ? name + "   <b>" + progress + "</b>"
                                : "<color=#6E7A93><s>" + name + "</s>   " + progress + "</color>";
                _setRing[i].color = done ? new Color(0.96f, 0.77f, 0.32f, 0.25f) : new Color(0f, 0f, 0f, 0f);
            }

            // Temple state: unlocked without a standing Temple = it was lost.
            _banner.gameObject.SetActive(!_templeStanding);

            // Vessels: adopted sects in fixed sect order; the first empty one
            // carries the adopt button, the rest stand dry.
            int r = 0;
            for (int i = 0; i < SectConfig.SectCount && _hasState; i++)
                if (_state.Get(i).IsAdopted && r < _rowSect.Length) _rowSect[r++] = SectConfig.IdAt(i);
            int adoptedRows = r;
            for (; r < _rowSect.Length; r++) _rowSect[r] = null;

            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].Root.gameObject.SetActive(true);
                if (_rowSect[i] == null) RefreshEmptyRow(_rows[i], i == adoptedRows);
                else RefreshRow(_rows[i], i, _rowSect[i]);
            }
            _noSects.gameObject.SetActive(adoptedRows == 0);
            Layout(0);
            RefreshPopups();
            RefreshTree();
            RefreshCastBar();
        }

        private bool AnyAdopted() => _hasState && _state.AdoptedCount() > 0;

        private static SectSet SetOf(int3 counts) => SetPassives.SetOf(counts);

        private void RefreshEmptyRow(Row row, bool first)
        {
            row.EmptyButton.gameObject.SetActive(first);
            row.EmptyButton.interactable = _templeStanding;
            row.Filled.gameObject.SetActive(false);
            row.Feeding = false;
            row.Drip.enabled = false;
            row.Drop.color = PipeDry;
            row.TubeFill.gameObject.SetActive(false);
            for (int e = 0; e < 3; e++) row.TubeEdges[e].color = LockedEdge;
        }

        private void RefreshRow(Row row, int rowIndex, string sectId)
        {
            row.EmptyButton.gameObject.SetActive(false);
            row.Filled.gameObject.SetActive(true);
            int idx = SectConfig.IndexOf(sectId);
            var sect = _state.Get(idx);

            // The reserve.
            float total = SectProgression.Threshold(SectProgression.Steps);
            float frac = total > 0f ? Mathf.Clamp01(sect.Progress / total) : 0f;
            bool finished = SectProgression.IsFinished(sect);
            row.Feeding = !finished;
            row.Drip.enabled = !finished;
            row.Drop.color = finished ? PipeDry : Pipe;
            row.TubeFill.rectTransform.anchorMax = new Vector2(1f, frac);
            row.TubeFill.gameObject.SetActive(frac > 0.002f);
            for (int e = 0; e < 3; e++) row.TubeEdges[e].color = finished ? FaithBright : Pipe;

            // Where each step sits on the vessel (the step sizes are data).
            float At(int step) => total > 0f ? SectProgression.Threshold(step) / total : 0f;
            float px = TubeW + 8f;
            Place((RectTransform)row.Powers[0].Button.transform, px, PowerY(At(1)), Cell, Cell);
            Place((RectTransform)row.Powers[1].Button.transform, px, PowerY(At(2)), Cell, Cell);
            Place((RectTransform)row.Powers[2].Button.transform, px, PowerY(At(4)), Cell, Cell);
            int level = sect.PowerLevel < 1 ? 1 : sect.PowerLevel;
            PlaceMark(row.Mark2Line, row.Mark2, At(3), level >= 2);
            PlaceMark(row.Mark3Line, row.Mark3, At(5), level >= 3);

            if (!SetIcon(row.SectImage, SectEmblemKey(sectId))) row.SectImage.enabled = false;
            row.SectText.text = SectInfo.ShortName(sectId);

            for (int p = 0; p < 3; p++) RefreshPower(row.Powers[p], rowIndex, sectId, p + 1, sect);
            RefreshBuildingChip(row, sectId);
            RefreshHeroChip(row, sectId);
        }

        private static string SectEmblemKey(string sectId)
        {
            const string p = "Sect_";
            return sectId != null && sectId.StartsWith(p) ? sectId.Substring(p.Length) : sectId;
        }

        private static void PlaceMark(Image line, TMP_Text text, float fraction, bool lit)
        {
            float y = DropH + TubeH * (1f - Mathf.Clamp01(fraction));
            Place(line.rectTransform, 0f, y - 1.5f, TubeW + 4f, 3f);
            Place(text.rectTransform, TubeW + 10f, y - 6f, 60f, 28f);
            line.color = lit ? Color.white : LockedEdge;
            text.color = lit ? Faith : LockedInk;
        }

        private void RefreshPower(Power pw, int rowIndex, string sectId, int slot, PerSectState sect)
        {
            var spec = SectActivePowerHelper.CurrentSpec(_em, _faction, sectId, slot, out _);
            // A pictogram where one is drawn; the name in text otherwise.
            bool drawn = SetIcon(pw.Icon, spec.Name);
            pw.Label.text = drawn ? "" : (spec.Name != null ? Loc.T(spec.Name) : "—");
            bool owned = sect.IsSlotUnlocked(slot);
            pw.NoCast.gameObject.SetActive(owned && !_templeStanding);
            if (owned)
            {
                // Lit by the reserve: this is its cast button.
                pw.Background.color = Faith;
                pw.Edge.effectColor = FaithBright;
                pw.Icon.color = FaithInk;
                pw.Label.color = FaithInk;
                pw.Badge.text = "";
                // Cast from the column on the left (CastBar); this menu tracks.
                pw.Key.text = "";
            }
            else
            {
                // Locked: it unlocks by itself. The power that comes NEXT
                // shows how far along it is (decision 63).
                float toNext = SectProgression.StepProgress(sect, out int nextStep);
                int nextSlot = nextStep == 1 ? 1 : nextStep == 2 ? 2 : nextStep == 4 ? 3 : 0;
                bool next = nextSlot == slot;
                pw.Background.color = next ? NextCell : LockedCell;
                pw.Edge.effectColor = next ? Faith : LockedEdge;
                pw.Icon.color = next ? NextInk : LockedInk;
                pw.Label.color = next ? GameUIKit.TextMain : GameUIKit.TextDim;
                pw.Badge.text = next ? Mathf.FloorToInt(toNext * 100f) + "%" : "";
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
            // Pale outline = something to click and place; teal = it stands.
            Color fill = new Color(0f, 0f, 0f, 0.2f), ink = Pale, edge = Pale;
            string text = "";
            switch (st)
            {
                case SiteState.Standing: fill = Teal; edge = Teal; ink = Color.white; break;
                case SiteState.Building: text = Mathf.RoundToInt(progress * 100f) + "%"; ink = new Color(1f, 1f, 1f, 0.25f); break;
                case SiteState.Planned: text = "…"; ink = new Color(1f, 1f, 1f, 0.25f); break;
                case SiteState.Destroyed: fill = Danger; edge = RedBadge; text = "!"; ink = new Color(1f, 1f, 1f, 0.25f); break;
            }
            row.BuildingImage.color = fill;
            row.BuildingEdge.effectColor = edge;
            row.BuildingIcon.color = ink;
            row.BuildingText.text = text;
            row.BuildingChip.interactable = !needsTemple;
        }

        /// <summary>The sect building's state, its unit count and its research, in one line (tooltip).</summary>
        private string BuildingCaption(string sectId)
        {
            var st = BuildingState(sectId, out _, out float progress);
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
            return (stateWord != null ? stateWord + " · " : "") + unitName + " " + limitText
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
            bool open = _state.Get(idx).PowerLevel >= 2;   // the hero comes at level II
            Color fill = new Color(0f, 0f, 0f, 0.2f), ink = Pale, edge = Pale;
            string text = "";
            if (live != Entity.Null)
            {
                fill = new Color(0.16f, 0.14f, 0.06f, 0.95f); edge = Faith;
                ink = new Color(1f, 1f, 1f, 0.2f);
                text = level.ToString();
            }
            else if (SectHeroes.HasLiveOrQueued(_em, _faction, hero))
            {
                ink = new Color(1f, 1f, 1f, 0.25f);
                text = "…";
            }
            else if (recruitedOnce) { fill = Danger; edge = RedBadge; }
            else if (!open) { fill = LockedCell; edge = LockedEdge; ink = LockedInk; }
            row.HeroImage.color = fill;
            row.HeroEdge.effectColor = edge;
            row.HeroIcon.color = ink;
            row.HeroText.text = text;
        }

        private string VesselTooltip(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null || !_hasState) return Loc.T("An empty reserve. Adopt a sect to start filling it.");
            var sect = _state.Get(SectConfig.IndexOf(sectId));
            if (SectProgression.IsFinished(sect))
                return string.Format(Loc.T("{0}: the reserve is full. Every power is lit, at level III."), SectInfo.ShortName(sectId));
            float toNext = SectProgression.StepProgress(sect, out int step);
            string what = step == 3 ? Loc.T("level II") : step == 5 ? Loc.T("level III") : Loc.T("the next power");
            return string.Format(Loc.T("{0}: {1}% of the way to {2}. It fills by itself as the font pours."),
                SectInfo.ShortName(sectId), Mathf.FloorToInt(toNext * 100f), what);
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
                // A locked power cannot be bought: it arrives as points come in.
                SimSignals.Notify(Loc.T("This power unlocks by itself as you earn religion points"));
                return;
            }
            // This menu TRACKS (2026-11-19): a lit power is cast from the
            // column on the left, so point at it there.
            PointAtCastButton(rowIndex, slot);
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
                    // CheckTargetInSight: an enemy the caster cannot see is
                    // refused, exactly as Fire refuses it (Spells.md §6.1).
                    (em, target) => SectActivePowerHelper.CheckTargetInSight(em, faction, kind, targetKind, target, out var why)
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

            // Fortify / Plenty raise a building: placed like one
            // (ReligionPanelBinder.Placement.cs, Build_Grid.md).
            if (TryBeginRaisedPlacement(sectId, slot, spec, faction)) return;

            float radius = spec.Radius > 0f ? spec.Radius : SectRadii.Small;
            var aimKind = spec.Kind;
            GroundTargeting.Begin(radius, new Color(0.96f, 0.77f, 0.32f, 0.85f), target =>
            {
                var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (w == null || !w.IsCreated) return;
                CommandRouter.IssueSectPower(w.EntityManager, faction, sectId, slot, target);
            },
            // The centre must be in the caster's sight (Spells.md §6.1): the
            // ring reads the simulation's own test, so red here is exactly
            // what Fire would refuse.
            point =>
            {
                var w = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                if (w == null || !w.IsCreated) return null;
                return SectActivePowerHelper.AimInSight(w.EntityManager, faction, aimKind, point)
                    ? null
                    : TheWaningBorder.Abilities.CastTargetRules.Describe(
                        TheWaningBorder.Abilities.CastTargetRefusal.NotInSight,
                        TheWaningBorder.Abilities.CastTargetKind.None);
            });
        }

        private void ClickLevel(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null || !TryWorld()) return;
            if (SectQuery.PowerLevelOf(_em, _faction, sectId) >= 3) return;
            SimSignals.Notify(Loc.T("A sect's level rises by itself as you earn religion points"));
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
            if (GameSettings.IsSpectating) return;
            if (UnityEngine.EventSystems.EventSystem.current != null
                && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null
                && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null)
                return;
            if (!_unlocked)
            {
                // R in Age 0 says why nothing opens.
                if ((_ageLocked || _templeLocked) && TheWaningBorder.Input.KeyBindings.Down(TheWaningBorder.Input.KeyAction.ReligionMenu))
                    PlayerNotificationSystem.Notify(_ageLocked
                        ? Loc.T("Religion unlocks in Age 1.")
                        : Loc.T("Build a Temple of Ridan to unlock religion."));
                return;
            }
            if (TheWaningBorder.Input.KeyBindings.Down(TheWaningBorder.Input.KeyAction.ReligionMenu)) SetShown(!_shown);
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
            string s = string.Format(Loc.T("Sect level {0}. Every unlocked power of the sect is cast at this level."), SectInfo.Roman(level));
            if (SetPassives.ActiveSet(_em, _faction) == SectSet.Concord)
                s += "\n" + Loc.T("Concord: cast one level higher, up to III.");
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
            sb.Append("<color=#A8A38F>").Append(Loc.T("Reach")).Append(": ").Append(SectRadii.Label(spec))
              .Append("   ").Append(Loc.T("Cooldown")).Append(": ")
              .Append(Mathf.RoundToInt(SectActivePowerHelper.EffectiveCooldown(_em, _faction, sectId, spec.Cooldown))).Append("s   ")
              .Append(Loc.T("Cast")).Append(": ").Append(SectActivePowerHelper.WindupFor(spec.Kind).ToString("0.#")).Append("s</color>");
            if (!SectActivePowerHelper.IsSlotUnlocked(_em, _faction, sectId, slot))
                sb.Append("\n<color=#F5C451>").Append(Loc.T("Locked. Lights up by itself when this reserve reaches it.")).Append("</color>");
            else
                sb.Append("\n<color=#8FE8F2>").Append(HotkeyLabel(rowIndex, slot)).Append("</color>  <color=#A8A38F>")
                  .Append(Loc.T("cast from the column on the left")).Append("</color>");
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
            if (TryWorld()) s += "\n\n<color=#A8A38F>" + BuildingCaption(sectId) + "</color>";
            if (!_templeStanding) s += "\n<color=#FF9E8C>" + Loc.T("Needs a standing Temple to build or rebuild.") + "</color>";
            return s;
        }

        private string HeroTooltip(int rowIndex)
        {
            string sectId = _rowSect[rowIndex];
            if (sectId == null) return "";
            string text = SectInfo.HeroText(sectId) ?? "";
            int rp = TryWorld() ? SectHeroes.RpDue(_em, _faction, sectId) : 0;
            if (_hasState && _state.Get(SectConfig.IndexOf(sectId)).PowerLevel < 2)
                text += "\n<color=#F5C451>" + Loc.T("The hero comes when this reserve reaches level II.") + "</color>";
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
