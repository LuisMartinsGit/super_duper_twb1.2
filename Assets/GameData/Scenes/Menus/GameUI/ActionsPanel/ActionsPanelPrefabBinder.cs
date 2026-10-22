// ActionsPanelPrefabBinder.cs
// Binder for the ARTIST-AUTHORED ActionsPanel prefab (GameData/Scenes/Menus/
// GameUI/SelectionUI/ActionsPanel.prefab): a fixed 3x5 grid of Synty action
// buttons under the "Actions" node, bound in sibling order (row-major,
// 5 columns). Spawned by GameUIManager when GameUICatalog.actionsPanel is
// assigned; while it is active the code-built ActionsPanelBinder keeps only
// the special selections (vault, walls, hut age-up, temple upgrade lever).
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
//   Building level-up deliberately does NOT render here.
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

        /// <summary>True while an authored ActionsPanel is bound — the
        /// code-built ActionsPanelBinder checks this to avoid rendering the
        /// grids this panel owns.</summary>
        public static bool Active { get; private set; }

        private enum Category { Economy, Defense, Research, Military, Religion }

        private static readonly Color EconomyGreen  = new Color(0.18f, 0.45f, 0.20f, 1f);
        private static readonly Color DefenseBlue   = new Color(0.02f, 0.38f, 0.68f, 1f);
        private static readonly Color ResearchBrown = new Color(0.42f, 0.30f, 0.16f, 1f);
        private static readonly Color MilitaryRed   = new Color(0.55f, 0.15f, 0.12f, 1f);
        private static readonly Color ReligionPurple= new Color(0.42f, 0.22f, 0.55f, 1f);

        private static readonly HashSet<string> EconomyBuildings = new()
        {
            "Hut", "GatherersHut", "Mine", "VeilstoneMine",
            "Alanthor_TradingOutpost", "Feraldis_GreatForge", "Runai_Sanctuary",
            "Feraldis_HuntingLodge", "Feraldis_LoggingStation",
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
        private enum BuildRow { Economy, Training, Special, Defense }

        private static readonly string[][] BuildOrder =
        {
            // Economy: Guild, Mine, Trader's Post, House
            new[] { "GatherersHut", "Mine", "Alanthor_TradingOutpost", "Hut",
                    "Feraldis_GreatForge" },
            // Training: Barracks, Archery Range, Royal Stables, Siege Yard
            new[] { "Barracks", "ArcheryRange", "Alanthor_RoyalStable", "Alanthor_SiegeYard",
                    "Feraldis_HallOfAxes", "Feraldis_Pasture", "Feraldis_SiegeYard",
                    "Runai_GrazingGrounds", "Runai_Arrowyard", "Runai_SiegeWorkshop" },
            // Special: Temple, then the sect buildings
            new[] { "TempleOfRidan", "Sect_Reliquary", "Sect_MendingHall", "Sect_Stonehold",
                    "Sect_Treasury", "Sect_MusterYard", "Feraldis_WarTotem" },
            // Defense: Towers, walls, Fortress
            new[] { "Alanthor_Tower", "Feraldis_Tower", "Palisade", "Alanthor_Wall", "Fortress" },
        };

        private static BuildRow BuildRowOf(string id)
        {
            for (int r = 0; r < BuildOrder.Length; r++)
                if (System.Array.IndexOf(BuildOrder[r], id) >= 0) return (BuildRow)r;
            return BuildingCategory(id) switch
            {
                Category.Economy  => BuildRow.Economy,
                Category.Defense  => BuildRow.Defense,
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
        }

        private Slot[] _slots;
        private CanvasGroup _group;

        // The authored grid / separator rects and their authored positions,
        // for the one-row slide the worker palette applies (SetWorkerRows).
        private RectTransform _gridRect;
        private RectTransform _separatorRect;
        private Vector2 _gridPosAuthored, _gridSizeAuthored, _separatorPosAuthored;
        private float _rowPitch;
        private bool _workerRows;

        private Dictionary<string, Sprite> _symbols;
        private static readonly Dictionary<Texture2D, Sprite> _spriteCache = new();

        private SelectionChangeDetector _detector;
        private float _timer;
        private Entity _entity;
        private bool _upgradeOverflowLogged;

        public void Init(Dictionary<string, Sprite> symbols) => _symbols = symbols;

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
                Active = false;   // let the code-built panels take back over
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
            if (count > 0)
            {
                var template = grid.GetChild(count - 1).gameObject;
                for (int i = count; i < WorkerSlotCount; i++)
                {
                    var clone = Instantiate(template, grid);
                    clone.name = template.name + "_row" + (i / Cols);
                    total++;
                }
            }
            _slots = new Slot[total];
            for (int i = 0; i < total; i++)
                _slots[i] = BindSlot(grid.GetChild(i));

            _gridRect = grid as RectTransform;
            _separatorRect = GameUIKit.FindDeep(transform, "Separator") as RectTransform;
            var layout = grid.GetComponent<GridLayoutGroup>();
            _rowPitch = layout != null ? layout.cellSize.y + layout.spacing.y : 0f;
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
        }

        /// <summary>
        /// Worker mode shows four rows: the grid's top edge and the separator
        /// move up one row pitch (the bottom edge stays), so the cloned fourth
        /// row lands where the authored third row sat. Every other mode gets
        /// the authored rects back.
        /// </summary>
        private void SetWorkerRows(bool on)
        {
            if (_workerRows == on) return;
            _workerRows = on;
            if (_gridRect == null || _rowPitch <= 0f) return;
            float extra = on ? _rowPitch * (WorkerRows - SlotCount / Cols) : 0f;
            _gridRect.sizeDelta = _gridSizeAuthored + new Vector2(0f, extra);
            _gridRect.anchoredPosition = _gridPosAuthored + new Vector2(0f, extra * 0.5f);
            if (_separatorRect != null)
                _separatorRect.anchoredPosition = _separatorPosAuthored + new Vector2(0f, extra);
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
            if (_timer < RefreshInterval && !changed) return;
            _timer = 0f;
            Refresh();
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

            _entity = ActionsPanelBinder.FirstOwnedSelected(em);
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
