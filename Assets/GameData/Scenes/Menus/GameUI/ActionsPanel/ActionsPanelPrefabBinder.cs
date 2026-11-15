// ActionsPanelPrefabBinder.cs
// Binder for the ARTIST-AUTHORED ActionsPanel prefab (GameData/Scenes/Menus/
// GameUI/ActionsPanel/ActionsPanel.prefab): a fixed 3x5 grid of Synty action
// buttons under the "Actions" node, bound in sibling order (row-major,
// 5 columns). Spawned by GameUIManager from GameUICatalog.actionsPanel. It is
// THE actions panel for every selection (2026-11-10): the code-built
// ActionsPanelBinder that drew the vault, walls, gates, hut age-up and wagon
// on a panel of their own is gone — those render here too
// (ActionsPanelPrefabBinder.Special.cs, .Vault.cs).
//
// One panel, two selection modes (spec 2026-07-24):
// - WORKER: the build palette. Only buildings unlockable at the CURRENT
//   age are visible (era-locked entries are hidden outright, not greyed);
//   visible entries grey out when resources are missing. Special/choice
//   buildings never appear here (they come pre-filtered from
//   EntityActionExtractor.GetBuildingActions).
// - BUILDING: top row (slots 0-4) = trainable units; the two lower rows
//   (slots 5-14) = research. Tiered research occupies ONE fixed slot per
//   chain and advances to the next tier the moment the current tier STARTS
//   researching (queued techs vanish from the extractor's list, so the
//   successor takes the same slot; cancelling brings the tier back).
//   The building level-up takes the last free cell (RenderUpgradeSlot).
//
// Button tint follows the category color scheme:
//   green = economy, blue = defenses, brown = research,
//   red = military, purple = religion.
// Icons come from the catalog entitySymbols (by id/label) with the
// Resources/UI/Icons building textures as fallback; icon-less actions show
// a text caption instead.

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ActionsPanelPrefabBinder : MonoBehaviour
    {
        private const float RefreshInterval = 0.15f;
        private const int Cols = 5;
        private const int SlotCount = 15;
        private const int TrainSlots = 5;      // row 0
        private const int ResearchSlots = 10;  // rows 1-2

        /// <summary>
        /// THE BUILD PALETTE IS FOUR CATEGORY ROWS (2026-10-22): Economy /
        /// Training / Special / Defense, one row each (BuildRowOf, BuildOrder).
        /// The prefab authors three rows, so the palette clones a fourth from
        /// the last authored button at bind time; building and unit modes
        /// never touch it (they stay within SlotCount). In worker mode the
        /// grid and its separator slide up one row pitch so the fourth row
        /// keeps inside the frame (SetWorkerRows).
        /// </summary>
        private const int WorkerRows = 4;
        private const int WorkerSlotCount = WorkerRows * Cols;

        /// <summary>Synthetic action id for the building level-up cell.</summary>
        private const string UpgradeActionId = "BuildingUpgrade";

        /// <summary>True while an authored ActionsPanel is bound.</summary>
        public static bool Active { get; private set; }

        private enum Category { Economy, Defense, Research, Military, Religion, Temple }

        private static readonly Color EconomyGreen  = new Color(0.18f, 0.45f, 0.20f, 1f);
        private static readonly Color DefenseBlue   = new Color(0.02f, 0.38f, 0.68f, 1f);
        private static readonly Color ResearchBrown = new Color(0.42f, 0.30f, 0.16f, 1f);
        private static readonly Color MilitaryRed   = new Color(0.55f, 0.15f, 0.12f, 1f);
        private static readonly Color ReligionPurple= new Color(0.42f, 0.22f, 0.55f, 1f);
        private static readonly Color TempleGold    = new Color(0.62f, 0.47f, 0.10f, 1f);

        private static readonly HashSet<string> EconomyBuildings = new()
        {
            "Hut", "GatherersHut", "Mine", "VeilstoneMine",
            "Feraldis_GreatForge", "Runai_Sanctuary",
            "Feraldis_HuntingLodge", "Feraldis_LoggingStation",
            "Alanthor_Farm", "Alanthor_Market",
        };
        // The Fortress sits in the Defense row of the build palette
        // (2026-10-22), so it is tinted with the row.
        private static readonly HashSet<string> DefenseBuildings = new()
        {
            "Palisade", "Alanthor_Wall", "Alanthor_Tower", "Feraldis_Tower", "Fortress",
        };

        // ── The build palette's four rows (2026-10-22) ────────────────────
        // Economy / Training / Special / Defense, one grid row each, in this
        // order within the row. An id the table does not name joins the row
        // its colour category implies, after the named ones, in catalog
        // order — so a new building is never dropped from the palette.
        // THE PALETTE IS 4 x 4 (2026-11-13, developer: square again now that
        // the Guild and the Mine are built from their node - NodeButton.cs;
        // it was 5 x 4 from 2026-11-11): economy (green) /
        // military (red) / three defensive (blue) + the Temple (gold) /
        // the sect buildings (purple), shown greyed until their sect is
        // adopted, so the grid never reflows as the match goes on.
        private enum BuildRow { Economy, Training, Defense, Special }

        private static readonly string[][] BuildOrder =
        {
            // Economy: Farm, House, Market (decision 55). The extractors -
            // Guild, Mine, Veilstone Mine - are not palette entries: they are
            // built from the button over their node (NodeButton.cs).
            new[] { "Alanthor_Farm", "Hut", "Alanthor_Market", "Feraldis_GreatForge" },
            // Training: Barracks, Archery Range, Royal Stables, Siege Yard
            new[] { "Barracks", "ArcheryRange", "Alanthor_RoyalStable", "Alanthor_SiegeYard",
                    "Feraldis_HallOfAxes", "Feraldis_Pasture", "Feraldis_SiegeYard",
                    "Runai_GrazingGrounds", "Runai_Arrowyard", "Runai_SiegeWorkshop" },
            // Defense: tower, wall, Fortress - then the Temple in the fourth cell
            new[] { "Alanthor_Tower", "Feraldis_Tower", "Palisade", "Alanthor_Wall", "Fortress",
                    "TempleOfRidan" },
            // Special: the sect buildings
            new[] { "Sect_Reliquary", "Sect_MendingHall", "Sect_Stonehold",
                    "Sect_Treasury", "Sect_MusterYard", "Feraldis_WarTotem" },
        };
        /// <summary>Columns of the build palette (the action grid keeps Cols).</summary>
        // 4 x 4 since 2026-11-13 (developer): a perfect square. The palette
        // has as many columns as rows, and its cells grow to fill the frame
        // (SetGrid).
        private const int PaletteCols = 4;
        /// <summary>Cells the build palette may use.</summary>
        private const int PaletteSlotCount = PaletteCols * WorkerRows;

        private static BuildRow BuildRowOf(string id)
        {
            for (int r = 0; r < BuildOrder.Length; r++)
                if (System.Array.IndexOf(BuildOrder[r], id) >= 0) return (BuildRow)r;
            return BuildingCategory(id) switch
            {
                Category.Economy  => BuildRow.Economy,
                Category.Defense  => BuildRow.Defense,
                Category.Temple   => BuildRow.Defense,
                Category.Religion => BuildRow.Special,
                _                 => BuildRow.Training,
            };
        }

        /// <summary>Rank of an id inside its row: named ids first, in table
        /// order; unnamed ones after, keeping their incoming order.</summary>
        private static int BuildRankOf(string id, int row)
        {
            int i = System.Array.IndexOf(BuildOrder[row], id);
            return i >= 0 ? i : BuildOrder[row].Length;
        }
        private static readonly HashSet<string> ReligionBuildings = new()
        {
            "TempleOfRidan", "FiendstoneKeep", "VaultOfAlmierra", "ThessarasCrossing",
            // Sect buildings are religion (Religion.md §3.2), not military.
            "Sect_Reliquary", "Sect_MendingHall", "Sect_Stonehold", "Sect_Treasury",
            "Sect_MusterYard",
        };
        // Everything else placeable (Barracks, ranges, siege yards, stables,
        // Longhouse = cultured Barracks, ...) falls through to Military.

        private sealed class Slot
        {
            public GameObject Root;
            public Button Button;
            public Image Icon;
            public Image CooldownFill;
            public TMP_Text Caption;
            public Image[] TintBg;          // Normal/Highlighted/Selected backgrounds
            public System.Action Click;
            public string Tooltip;
            public string ActionId;
            public string[] ChainIds;       // techs collapsed into this slot
            public CanvasGroup Group;       // BlankSlot: active but invisible
            /// <summary>A short number over the bottom of the cell, under the
            /// icon (the Market's rates, the Vault's bonuses; 2026-11-15).
            /// Hidden unless SetBadge shows it.</summary>
            public GameObject BadgeRoot;
            public TMP_Text Badge;
        }

        private Slot[] _slots;
        private CanvasGroup _group;

        // The authored grid / separator rects and their authored positions,
        // for the one-row slide the worker palette applies (SetWorkerRows).
        private RectTransform _gridRect;
        private RectTransform _separatorRect;
        private GridLayoutGroup _gridLayout;
        private Vector2 _gridPosAuthored, _gridSizeAuthored, _separatorPosAuthored;
        private Vector2 _cellAuthored, _spacingAuthored;
        private float _rowPitch;
        private GridLayoutGroup.Constraint _constraintAuthored;
        private int _constraintCountAuthored;

        /// <summary>How the grid is laid out: as authored (3 x 5 with the
        /// separator), the large five-column grid every building menu uses,
        /// or the builder's square palette.</summary>
        private enum GridMode { Authored, Large, Palette }
        private GridMode _gridMode = GridMode.Authored;

        /// <summary>
        /// THE BUILD PALETTE FILLS THE PANEL (2026-11-02). In worker mode the
        /// separator is hidden and the grid takes the frame's whole interior:
        /// the panel's content square minus this inset on each side, square
        /// cells sized so the mode's columns span that width - four for the
        /// builder's palette, which makes it a square of 4 x 4; five for a
        /// building's menu. The authored grid, cells and separator come back
        /// in GridMode.Authored.
        /// </summary>
        private const float WorkerGridInset = 40f;
        private const float WorkerCellSpacing = 8f;

        private Dictionary<string, Sprite> _symbols;
        private static readonly Dictionary<Texture2D, Sprite> _spriteCache = new();

        private SelectionChangeDetector _detector;
        private float _timer;
        private Entity _entity;
        private bool _upgradeOverflowLogged;

        /// <param name="nodeButtonTemplate">The hexagonal age-up button the
        /// node button copies (NodeButton.cs); null falls back to a round
        /// copy of this panel's own button.</param>
        public void Init(Dictionary<string, Sprite> symbols, GameObject nodeButtonTemplate = null)
        {
            _symbols = symbols;
            if (_slots != null) BindNodeButton(_nodeFallback, nodeButtonTemplate);
            _nodeFallback = null;
        }

        // ── Setup ──────────────────────────────────────────────────────────

        private void Awake()
        {
            Active = true;

            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            var grid = GameUIKit.FindDeep(transform, "Actions");
            if (grid == null)
            {
                TWBLog.Log("[GameUI] ActionsPanel prefab: no \"Actions\" node found — panel disabled.");
                Active = false;
                enabled = false;
                return;
            }

            int count = Mathf.Min(grid.childCount, SlotCount);
            if (count < SlotCount)
                TWBLog.Log($"[GameUI] ActionsPanel prefab: expected {SlotCount} buttons under " +
                    $"\"Actions\", found {count}.");

            // The fourth (worker-only) row: pristine copies of the last
            // authored button, cloned BEFORE any slot is bound so the clones
            // carry no caption or listener of their own.
            int total = count;
            Transform nodeClone = null;
            if (count > 0)
            {
                var template = grid.GetChild(count - 1).gameObject;
                for (int i = count; i < WorkerSlotCount; i++)
                {
                    var clone = Instantiate(template, grid);
                    clone.name = template.name + "_row" + (i / Cols);
                    total++;
                }
                // One more for the button that pops over a resource node
                // (NodeButton.cs); it leaves the grid before any slot is bound.
                nodeClone = Instantiate(template, grid).transform;
            }
            // Kept inactive (the grid skips it) until Init builds the node
            // button from it or from the hexagonal template.
            if (nodeClone != null) nodeClone.gameObject.SetActive(false);
            _nodeFallback = nodeClone;
            _slots = new Slot[total];
            for (int i = 0; i < total; i++)
                _slots[i] = BindSlot(grid.GetChild(i));
            // Every cell starts HIDDEN. The clones above are copies of a live
            // authored button, so until 2026-11-02 the fourth row sat in the
            // grid showing the template's placeholder glyph for any selection
            // whose render never reached slots 15+ — a building with no
            // authored layout, a unit — until a layout building's render
            // happened to clear them: five blank "buttons" after the Garrison's
            // real actions.
            for (int i = 0; i < total; i++) ClearSlot(_slots[i]);

            _gridRect = grid as RectTransform;
            _separatorRect = GameUIKit.FindDeep(transform, "Separator") as RectTransform;
            var layout = grid.GetComponent<GridLayoutGroup>();
            _gridLayout = layout;
            _rowPitch = layout != null ? layout.cellSize.y + layout.spacing.y : 0f;
            if (layout != null)
            {
                _cellAuthored = layout.cellSize;
                _spacingAuthored = layout.spacing;
                _constraintAuthored = layout.constraint;
                _constraintCountAuthored = layout.constraintCount;
            }
            if (_gridRect != null)
            {
                _gridPosAuthored = _gridRect.anchoredPosition;
                _gridSizeAuthored = _gridRect.sizeDelta;
            }
            if (_separatorRect != null) _separatorPosAuthored = _separatorRect.anchoredPosition;

            // Icons resolve through a callback rather than a captured
            // dictionary: Init() runs AFTER AddComponent has already fired
            // Awake, so _symbols is still null at this point.
            SetShown(false);
        }

        private void OnDestroy()
        {
            Active = false;
            DestroyNodeButton();
        }

        /// <summary>
        /// Worker mode shows four rows: the grid's top edge and the separator
        /// move up one row pitch (the bottom edge stays), so the cloned fourth
        /// row lands where the authored third row sat. Every other mode gets
        /// the authored rects back.
        /// </summary>
        private void SetWorkerRows(bool on) => SetGrid(on ? GridMode.Large : GridMode.Authored);

        private void SetGrid(GridMode mode)
        {
            if (_gridMode == mode) return;
            _gridMode = mode;
            if (_gridRect == null) return;
            bool on = mode != GridMode.Authored;
            if (_separatorRect != null && _separatorRect.gameObject.activeSelf == on)
                _separatorRect.gameObject.SetActive(!on);
            if (!on)
            {
                _gridRect.sizeDelta = _gridSizeAuthored;
                _gridRect.anchoredPosition = _gridPosAuthored;
                if (_gridLayout != null)
                {
                    _gridLayout.cellSize = _cellAuthored;
                    _gridLayout.spacing = _spacingAuthored;
                    _gridLayout.constraint = _constraintAuthored;
                    _gridLayout.constraintCount = _constraintCountAuthored;
                }
                return;
            }
            // The builder's palette is a square: as many columns as rows, so
            // its cells are larger than a building menu's five-column ones
            // and the grid is as tall as it is wide.
            int cols = mode == GridMode.Palette ? PaletteCols : Cols;
            var content = _gridRect.parent as RectTransform;
            float avail = (content != null ? content.rect.width : _gridSizeAuthored.x) - 2f * WorkerGridInset;
            if (mode == GridMode.Palette && content != null && content.rect.height > 0f)
                avail = Mathf.Min(avail, content.rect.height - 2f * WorkerGridInset);
            float cell = (avail - (cols - 1) * WorkerCellSpacing) / cols;
            float height = WorkerRows * cell + (WorkerRows - 1) * WorkerCellSpacing;
            if (_gridLayout != null)
            {
                _gridLayout.cellSize = new Vector2(cell, cell);
                _gridLayout.spacing = new Vector2(WorkerCellSpacing, WorkerCellSpacing);
                // The column count is stated, never left to how many cells
                // happen to fit the width after rounding.
                _gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                _gridLayout.constraintCount = cols;
            }
            _gridRect.sizeDelta = new Vector2(avail, height);
            // Centred in the content square; the authored x offset keeps the
            // grid centred inside the frame's slightly asymmetric border.
            _gridRect.anchoredPosition = new Vector2(_gridPosAuthored.x, 0f);
        }

        private Slot BindSlot(Transform root)
        {
            var slot = new Slot { Root = root.gameObject };
            slot.Group = root.GetComponent<CanvasGroup>();
            if (slot.Group == null) slot.Group = root.gameObject.AddComponent<CanvasGroup>();

            slot.Button = root.GetComponentInChildren<Button>(true);
            if (slot.Button != null)
            {
                var s = slot;
                slot.Button.onClick.AddListener(() => s.Click?.Invoke());

                // Hover tooltip; clicks stay on the Button so the Synty
                // pressed/disabled animations keep working. Read through a
                // callback — the slot's contents change with the selection.
                UITooltip.Bind(slot.Button.gameObject, () => s.Tooltip);
            }

            var icon = GameUIKit.FindDeep(root, "ICON");
            if (icon != null)
            {
                slot.Icon = icon.GetComponent<Image>();
                if (slot.Icon != null) slot.Icon.preserveAspect = true;
            }
            var iconAdditive = GameUIKit.FindDeep(root, "ICON_Additive");
            if (iconAdditive != null) iconAdditive.gameObject.SetActive(false);

            var cooldown = GameUIKit.FindDeep(root, "SPR_Cooldown");
            if (cooldown != null)
            {
                slot.CooldownFill = cooldown.GetComponent<Image>();
                if (slot.CooldownFill != null)
                {
                    slot.CooldownFill.type = Image.Type.Filled;
                    slot.CooldownFill.fillMethod = Image.FillMethod.Radial360;
                    slot.CooldownFill.fillOrigin = (int)Image.Origin360.Top;
                    slot.CooldownFill.fillClockwise = false;
                    slot.CooldownFill.color = new Color(0f, 0f, 0f, 0.55f);
                    slot.CooldownFill.fillAmount = 0f;
                }
            }

            // The Synty hotkey label under each button — unused here.
            var input = GameUIKit.FindDeep(root, "Input");
            if (input != null) input.gameObject.SetActive(false);

            // State backgrounds to tint with the category color. Disabled
            // keeps its authored dark look (the Button animator swaps to it
            // for locked slots).
            var tints = new List<Image>(4);
            CollectStateBackgrounds(root, "Normal", tints);
            CollectStateBackgrounds(root, "Highlighted", tints);
            CollectStateBackgrounds(root, "Selected", tints);
            slot.TintBg = tints.ToArray();

            // The Synty button swaps these state subtrees under the pointer.
            // They are artwork; only the Button itself may take input, or the
            // hover changes hit target every time the state changes.
            foreach (var state in new[] { "Normal", "Highlighted", "Selected", "Disabled" })
            {
                var node = GameUIKit.FindDeep(root, state);
                if (node != null) GameUIKit.DisableRaycasts(node.gameObject);
            }

            // Caption for icon-less actions (research techs mostly).
            var caption = GameUIKit.Text(root, "caption", "", 15f, GameUIKit.TextMain,
                TextAlignmentOptions.Center);
            caption.enableAutoSizing = true;
            caption.fontSizeMin = 8f;
            caption.fontSizeMax = 16f;
            caption.raycastTarget = false;
            caption.rectTransform.anchorMin = new Vector2(0f, 0f);
            caption.rectTransform.anchorMax = new Vector2(1f, 1f);
            caption.rectTransform.offsetMin = new Vector2(5f, 5f);
            caption.rectTransform.offsetMax = new Vector2(-5f, -5f);
            slot.Caption = caption;
            caption.gameObject.SetActive(false);

            // The badge: a dark strip across the bottom of the cell with a
            // number on it (SetBadge). Never takes input.
            var badgeBg = GameUIKit.Image(root, "badge", new Color(0f, 0f, 0f, 0.62f));
            var br = badgeBg.rectTransform;
            br.anchorMin = new Vector2(0.06f, 0.05f);
            br.anchorMax = new Vector2(0.94f, 0.32f);
            br.offsetMin = br.offsetMax = Vector2.zero;
            var badge = GameUIKit.Text(br, "text", "", 15f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            badge.enableAutoSizing = true;
            badge.fontSizeMin = 7f;
            badge.fontSizeMax = 16f;
            badge.raycastTarget = false;
            GameUIKit.Stretch(badge.rectTransform);
            badge.rectTransform.offsetMin = new Vector2(2f, 0f);
            badge.rectTransform.offsetMax = new Vector2(-2f, 0f);
            slot.BadgeRoot = badgeBg.gameObject;
            slot.Badge = badge;
            slot.BadgeRoot.SetActive(false);

            return slot;
        }

        private static void CollectStateBackgrounds(Transform buttonRoot, string stateName,
            List<Image> into)
        {
            var state = GameUIKit.FindDeep(buttonRoot, stateName);
            if (state == null) return;
            foreach (var t in state.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "SPR_Background") continue;
                var img = t.GetComponent<Image>();
                if (img != null) into.Add(img);
            }
        }

        // ── Refresh loop ───────────────────────────────────────────────────

        private void Update()
        {
            if (_slots == null || _slots.Length == 0) return;
            bool changed = _detector.Poll();
            _timer += Time.unscaledDeltaTime;
            if (_timer >= RefreshInterval || changed)
            {
                _timer = 0f;
                Refresh();
            }
            // Every frame: the button follows its node on screen.
            UpdateNodeButton();
        }

        private static EntityManager EM(out bool ok)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            ok = world != null && world.IsCreated;
            return ok ? world.EntityManager : default;
        }

        private void Refresh()
        {
            var em = EM(out bool ok);
            if (!ok || GameSettings.IsSpectating) { SetShown(false); return; }

            _vaultWanted = false;
            // Only the builder branch below sets it again.
            _builderActions = null;
            _nodeDirty = true;
            _entity = FirstOwnedSelected(em);
            if (_entity == Entity.Null) { SetShown(false); return; }
            if (em.HasComponent<UnderConstruction>(_entity)
                && !em.HasComponent<GathererHutConverting>(_entity)) { SetShown(false); return; }

            int used;
            if (em.HasComponent<CanBuild>(_entity))
            {
                var info = EntityActionExtractor.GetActionInfo(_entity, em);
                if (info.Type != ActionType.BuildingPlacement
                    || info.Actions == null || info.Actions.Count == 0)
                { SetShown(false); return; }
                used = RenderBuilder(em, info.Actions);
                _builderActions = info.Actions;
            }
            else
            {
                used = RenderBuilding(em);
                // Not a building this panel owns — movable units get the
                // formation buttons here instead (the panel's unit mode).
                if (used < 0) used = RenderUnitFormations(em);
                if (used < 0) { SetShown(false); return; }
            }

            SetShown(used > 0);
        }

        private void SetShown(bool shown)
        {
            _group.alpha = shown ? 1f : 0f;
            _group.interactable = shown;
            _group.blocksRaycasts = shown;
            // The Vault's bars ride the same panel; they show only while a
            // Vault is the selection this refresh rendered.
            ShowVaultSection(shown && _vaultWanted);
            // No tooltip teardown needed: UITooltip polls the pointer, and a
            // panel with blocksRaycasts off simply stops being hit.
        }

        // ── Worker mode ───────────────────────────────────────────────────

        // ── Unit mode: formations ──────────────────────────────────────────

        // Labels/Tips stay English — Loc.T at render, same keys the old
        // floating strip registered in the PT table.
        private static readonly string[] FormationLabels = { "Box", "Line", "Wedge", "Stagger" };
        private static readonly string[] FormationTips =
        {
            "<b>Box</b>\nCompact rectangle. The all-round default — good for moving a "
                + "mixed group without exposing a flank.",
            "<b>Line</b>\nWide, shallow rank. Maximises how many units can shoot or "
                + "engage at once; fragile if hit from the side.",
            "<b>Wedge</b>\nArrowhead. Concentrates the leading edge for a charge that "
                + "punches through a line.",
            "<b>Stagger</b>\nOffset rows. Spreads the group out so area damage and "
                + "siege hit fewer units at a time.",
        };

        // ── Building mode ──────────────────────────────────────────────────

        // ── Slot rendering ─────────────────────────────────────────────────

        // ── Categories, colors, icons ──────────────────────────────────────

        // ── Click execution ────────────────────────────────────────────────

        // ── Helpers ────────────────────────────────────────────────────────

    }
}
