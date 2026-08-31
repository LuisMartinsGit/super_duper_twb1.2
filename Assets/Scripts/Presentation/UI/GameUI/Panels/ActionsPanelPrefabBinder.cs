// ActionsPanelPrefabBinder.cs
// Binder for the ARTIST-AUTHORED ActionsPanel prefab (GameData/Scenes/Menus/
// GameUI/SelectionUI/ActionsPanel.prefab): a fixed 3x5 grid of Synty action
// buttons under the "Actions" node, bound in sibling order (row-major,
// 5 columns). Spawned by GameUIManager when GameUICatalog.actionsPanel is
// assigned; while it is active the code-built ActionsPanelBinder keeps only
// the special selections (vault, walls, hut age-up, temple upgrade lever).
//
// One panel, two selection modes (spec 2026-07-24):
// - BUILDER: the build palette. Only buildings unlockable at the CURRENT
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
using TheWaningBorder.UI.HUD;
using TheWaningBorder.UI.Panels;

namespace TheWaningBorder.UI.GameUI
{
    public sealed class ActionsPanelPrefabBinder : MonoBehaviour
    {
        private const float RefreshInterval = 0.15f;
        private const int Cols = 5;
        private const int SlotCount = 15;
        private const int TrainSlots = 5;      // row 0
        private const int ResearchSlots = 10;  // rows 1-2

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
            "Hut", "GatherersHut", "Hall", "Alanthor_Smelter",
            "Runai_Outpost", "Runai_TradeHub", "Runai_TradingPost", "ThessarasBazaar",
            "Feraldis_HuntingLodge", "Feraldis_LoggingStation",
        };
        private static readonly HashSet<string> DefenseBuildings = new()
        {
            "Alanthor_Wall", "Alanthor_Tower", "Feraldis_Tower",
        };
        private static readonly HashSet<string> ReligionBuildings = new()
        {
            "TempleOfRidan", "ShrineOfRidan", "FiendstoneKeep", "VaultOfAlmierra",
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
        }

        private Slot[] _slots;
        private CanvasGroup _group;
        private ProductionQueueStrip _queue;

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
            _slots = new Slot[count];
            for (int i = 0; i < count; i++)
                _slots[i] = BindSlot(grid.GetChild(i));
            if (count < SlotCount)
                TWBLog.Log($"[GameUI] ActionsPanel prefab: expected {SlotCount} buttons under " +
                    $"\"Actions\", found {count}.");

            // Icons resolve through a callback rather than a captured
            // dictionary: Init() runs AFTER AddComponent has already fired
            // Awake, so _symbols is still null at this point.
            _queue = new ProductionQueueStrip(transform, ResolveSpriteById);
            SetShown(false);
        }

        private void OnDestroy()
        {
            Active = false;
        }

        private Slot BindSlot(Transform root)
        {
            var slot = new Slot { Root = root.gameObject };

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
            if (!ok || GameSettings.IsObserver) { SetShown(false); return; }

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
                _queue.Hide();   // builders have no production queue
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
            if (!shown) _queue?.Hide();
        }

        // ── Builder mode ───────────────────────────────────────────────────

        /// <summary>Build palette: only current-age buildings are VISIBLE
        /// (the extractor marks future-era ones Enabled=false — those are
        /// hidden here, not greyed); missing resources grey a visible one.</summary>
        private int RenderBuilder(EntityManager em, List<ActionButton> actions)
        {
            bool placing = BuilderCommandPanel.IsPlacingBuilding;
            int used = 0;
            for (int i = 0; i < actions.Count && used < _slots.Length; i++)
            {
                var b = actions[i];
                if (!b.Enabled) continue;   // era-locked → hidden in this panel

                string id = b.Id;
                FillSlot(_slots[used++], b, BuildingCategory(id), null,
                    placing ? null
                            : (System.Action)(() => BuilderCommandPanel.TriggerBuildingPlacement(id)),
                    em);
            }
            for (int i = used; i < _slots.Length; i++) ClearSlot(_slots[i]);
            return used;
        }

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

        /// <summary>
        /// THE UNITS' ACTIONS PANEL (2026-08-31): a movable selection gets
        /// the four formation buttons in the grid, replacing the floating
        /// bottom-centre strip the old FormationsPanelBinder built. Clicking
        /// mirrors the X-key cycle (RequestFormationShape re-slots the whole
        /// selection immediately); the current shape is tinted gold, and the
        /// highlight follows shape changes from EITHER path on the panel's
        /// normal refresh.
        /// </summary>
        private int RenderUnitFormations(EntityManager em)
        {
            bool movable = false;
            var sel = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (sel != null)
                for (int i = 0; i < sel.Count && !movable; i++)
                    movable = em.Exists(sel[i]) && em.HasComponent<MoveSpeed>(sel[i]);
            if (!movable) return -1;

            int current = (int)TheWaningBorder.Input.RTSInputManager.CurrentFormationShape;
            int count = Mathf.Min(4, _slots.Length);
            for (int i = 0; i < count; i++)
            {
                var shape = (FormationShape)i;
                var b = new ActionButton
                {
                    Id = "Formation_" + FormationLabels[i],
                    Label = Loc.T(FormationLabels[i]),
                    Tooltip = Loc.T(FormationTips[i]),
                    Enabled = true,
                    CanAfford = true,
                };
                FillSlot(_slots[i], b, Category.Military, null, () =>
                {
                    TheWaningBorder.Input.RTSInputManager.RequestFormationShape(shape);
                    _timer = RefreshInterval;   // repaint the highlight now
                }, em);

                if (i == current)
                    foreach (var img in _slots[i].TintBg) img.color = GameUIKit.BarGold;
            }
            for (int i = count; i < _slots.Length; i++) ClearSlot(_slots[i]);

            _queue.Hide();   // units have no production queue
            return count;
        }

        // ── Building mode ──────────────────────────────────────────────────

        /// <summary>Row 0 = trainable units, rows 1-2 = research. Returns -1
        /// when this selection is not one the authored panel owns.</summary>
        private int RenderBuilding(EntityManager em)
        {
            var info = EntityActionExtractor.GetActionInfo(_entity, em);
            bool hasLayout = BuildingActionLayouts.TryResolve(_entity, em, out var layoutSlots);

            if (!hasLayout
                && info.Type != ActionType.UnitTraining
                && info.Type != ActionType.UnitTrainingAndResearch
                && info.Type != ActionType.TempleUpgrade)
                return -1;   // vault / walls / hut choice / wagon → code-built panel

            int used = 0;
            if (hasLayout)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (i >= layoutSlots.Length || layoutSlots[i].Empty) { ClearSlot(_slots[i]); continue; }
                    var resolved = layoutSlots[i];
                    var b = resolved.Button;
                    var cat = resolved.IsTrain ? UnitCategory(b.Id) : Category.Research;
                    Entity entity = _entity;
                    bool isTrain = resolved.IsTrain;
                    FillSlot(_slots[i], b, cat, resolved.ChainIds,
                        b.Enabled ? (System.Action)(() => Execute(entity, b, isTrain)) : null, em);
                    used++;
                }
            }
            else
            {
                // Top row: trainable units (and the building's special action
                // cells — bazaar pack, reliquary abilities — which ride the
                // training list).
                var trains = info.Actions;
                int t = 0;
                for (int i = 0; trains != null && i < trains.Count && t < TrainSlots; i++, t++)
                {
                    var b = trains[i];
                    Entity entity = _entity;
                    FillSlot(_slots[t], b, UnitCategory(b.Id), null,
                        b.Enabled ? (System.Action)(() => Execute(entity, b, isTrain: true)) : null, em);
                    used++;
                }
                if (trains != null && trains.Count > TrainSlots)
                    TWBLog.Log($"[GameUI] ActionsPanel: {trains.Count} training actions, " +
                        $"only {TrainSlots} slots — overflow dropped.");
                for (; t < TrainSlots && t < _slots.Length; t++) ClearSlot(_slots[t]);

                // Research rows: chain-stable slots.
                used += RenderResearchRows(em);
            }

            used += RenderUpgradeSlot(em);
            RenderProgress(em, info);
            _queue.Render(em, _entity, info);
            return used;
        }

        /// <summary>
        /// "Upgrade to Lv N" in the LAST free slot of the grid — the building
        /// level-up used to be a pill floating off the selection header, which
        /// is what "not integrated" meant. Takes the last empty cell so it
        /// never displaces a unit or a research chain; if the grid is
        /// genuinely full the upgrade is skipped rather than shadowing an
        /// action (logged once so it is not silent).
        /// </summary>
        private int RenderUpgradeSlot(EntityManager em)
        {
            var upgrade = BuildingUpgradeAction.Describe(em, _entity);
            if (!upgrade.Show) return 0;

            int index = -1;
            for (int i = _slots.Length - 1; i >= 0; i--)
                if (!_slots[i].Root.activeSelf) { index = i; break; }
            if (index < 0)
            {
                if (!_upgradeOverflowLogged)
                {
                    _upgradeOverflowLogged = true;
                    TWBLog.Log("[GameUI] ActionsPanel: no free slot for the building upgrade " +
                        "action — the grid is full.");
                }
                return 0;
            }

            Entity entity = _entity;
            var button = new ActionButton
            {
                Id = UpgradeActionId,
                Label = upgrade.Label,
                Tooltip = upgrade.Tooltip,
                Cost = upgrade.Cost,
                Enabled = upgrade.Enabled,
                CanAfford = upgrade.Enabled,
            };
            var slot = _slots[index];
            FillSlot(slot, button, Category.Research, null,
                upgrade.Enabled
                    ? (System.Action)(() => UpgradeClicked(entity))
                    : null,
                em);

            // FillSlot zeroes the radial sweep; a running upgrade drives it.
            if (upgrade.Progress >= 0f && slot.CooldownFill != null)
                slot.CooldownFill.fillAmount = 1f - upgrade.Progress;
            return 1;
        }

        private void UpgradeClicked(Entity entity)
        {
            var em = EM(out bool ok);
            if (!ok) return;
            BuildingUpgradeAction.Execute(em, entity);
            _timer = RefreshInterval;   // repaint on the next tick
        }

        /// <summary>
        /// Lay research out into slots 5-14 with STABLE positions: each tech
        /// chain (linked by prerequisites within this building's research
        /// list) owns one slot and shows its first not-yet-started tier;
        /// exhausted chains leave their slot blank so nothing shifts around.
        /// </summary>
        private int RenderResearchRows(EntityManager em)
        {
            var visible = EntityActionExtractor.GetResearchActions(_entity, em);
            var byId = new Dictionary<string, ActionButton>(visible.Count);
            foreach (var a in visible) byId[a.Id] = a;

            // Ordered slot plan: one entry per chain (null = chain exhausted,
            // keep the slot blank), then any extra actions that are not part
            // of the catalog research list (Keep wings, chapel levers, ...).
            var plan = new List<ActionButton?>();
            var claimed = new HashSet<string>();

            string buildingId = EntityActionExtractor.GetBuildingIdPublic(_entity, em);
            if (buildingId != null && TechCatalog.IsReady
                && TechCatalog.TryGetBuilding(buildingId, out var def) && def.research != null)
            {
                var ids = new List<string>();
                foreach (var id in def.research)
                    if (id != "Research_Era2") ids.Add(id);

                // parent = first prerequisite that is itself in this list.
                var parent = new Dictionary<string, string>();
                foreach (var id in ids)
                {
                    parent[id] = null;
                    if (!TechCatalog.TryGetTechnology(id, out var tech)
                        || tech.prerequisites == null) continue;
                    foreach (var pre in tech.prerequisites)
                        if (ids.Contains(pre)) { parent[id] = pre; break; }
                }

                foreach (var rootId in ids)
                {
                    if (parent[rootId] != null) continue;   // not a chain root
                    // Walk the chain root -> tier2 -> ... and show the first
                    // tier still offered by the extractor (started/finished
                    // tiers are absent from its list).
                    ActionButton? display = null;
                    string cursor = rootId;
                    while (cursor != null)
                    {
                        claimed.Add(cursor);
                        if (display == null && byId.TryGetValue(cursor, out var b))
                            display = b;
                        string next = null;
                        foreach (var id in ids)
                            if (parent[id] == cursor) { next = id; break; }
                        cursor = next;
                    }
                    plan.Add(display);
                }
            }

            foreach (var a in visible)
                if (!claimed.Contains(a.Id)) plan.Add(a);

            if (plan.Count > ResearchSlots)
                TWBLog.Log($"[GameUI] ActionsPanel: {plan.Count} research slots needed, " +
                    $"only {ResearchSlots} available — overflow dropped.");

            int used = 0;
            for (int i = 0; i < ResearchSlots; i++)
            {
                int slotIndex = TrainSlots + i;
                if (slotIndex >= _slots.Length) break;
                if (i >= plan.Count || plan[i] == null) { ClearSlot(_slots[slotIndex]); continue; }

                var b = plan[i].Value;
                Entity entity = _entity;
                FillSlot(_slots[slotIndex], b, Category.Research, null,
                    b.Enabled ? (System.Action)(() => Execute(entity, b, isTrain: false)) : null, em);
                used++;
            }
            return used;
        }

        /// <summary>In-progress training/research shown as a radial cooldown
        /// sweep on the matching button (for chains: on the slot the active
        /// tier's chain occupies, i.e. under the successor tier).</summary>
        private void RenderProgress(EntityManager em, in EntityActionInfo info)
        {
            if (info.TrainingState.HasValue && info.TrainingState.Value.IsTraining)
            {
                var t = info.TrainingState.Value;
                for (int i = 0; i < _slots.Length && i < TrainSlots; i++)
                    if (_slots[i].ActionId == t.CurrentUnitId && _slots[i].CooldownFill != null)
                        _slots[i].CooldownFill.fillAmount = 1f - Mathf.Clamp01(t.Progress);
            }
            if (info.ResearchState.HasValue && info.ResearchState.Value.IsResearching)
            {
                var r = info.ResearchState.Value;
                for (int i = 0; i < _slots.Length; i++)
                {
                    var s = _slots[i];
                    if (s.CooldownFill == null || s.ChainIds == null) continue;
                    foreach (var id in s.ChainIds)
                        if (id == r.CurrentTechId)
                        { s.CooldownFill.fillAmount = 1f - Mathf.Clamp01(r.Progress); break; }
                }
            }
        }

        // ── Slot rendering ─────────────────────────────────────────────────

        private void FillSlot(Slot slot, in ActionButton b, Category cat, string[] chainIds,
            System.Action click, EntityManager em)
        {
            bool locked = !b.Enabled || click == null;
            bool poor = b.Enabled && !b.CanAfford;

            slot.ActionId = b.Id;
            slot.ChainIds = chainIds;
            slot.Click = click;
            slot.Tooltip = ExpandTooltip(b, em);
            if (!slot.Root.activeSelf) slot.Root.SetActive(true);
            if (slot.Button != null) slot.Button.interactable = !locked;

            var baseCol = CategoryColor(cat);
            if (poor) baseCol = Color.Lerp(baseCol, new Color(0.16f, 0.16f, 0.16f), 0.55f);
            foreach (var img in slot.TintBg) img.color = baseCol;

            var sprite = ResolveSprite(b);
            if (slot.Icon != null)
            {
                slot.Icon.enabled = sprite != null;
                if (sprite != null)
                {
                    slot.Icon.sprite = sprite;
                    slot.Icon.color = locked ? new Color(1f, 1f, 1f, 0.35f)
                               : poor ? new Color(1f, 1f, 1f, 0.6f)
                               : Color.white;
                }
            }
            bool showCaption = sprite == null;
            if (slot.Caption.gameObject.activeSelf != showCaption)
                slot.Caption.gameObject.SetActive(showCaption);
            if (showCaption)
            {
                slot.Caption.text = b.Label;
                slot.Caption.color = locked ? GameUIKit.TextDim : GameUIKit.TextMain;
            }

            if (slot.CooldownFill != null) slot.CooldownFill.fillAmount = 0f;
        }

        private void ClearSlot(Slot slot)
        {
            slot.ActionId = null;
            slot.ChainIds = null;
            slot.Click = null;
            slot.Tooltip = null;
            if (slot.Root.activeSelf) slot.Root.SetActive(false);
        }

        /// <summary>The data layer's tooltips end in a bare "Cost: " line
        /// (the IMGUI panel drew icons there); splice the amounts in.</summary>
        private string ExpandTooltip(in ActionButton b, EntityManager em)
        {
            string tip = b.Tooltip ?? b.Label;
            // The marker must be the SAME expression the tooltip composer
            // uses ("\n" + Loc.T("Cost: ")) so splitter and composer agree
            // in every language.
            string marker = "\n" + Loc.T("Cost: ");
            int idx = tip.IndexOf(marker, System.StringComparison.Ordinal);
            if (idx >= 0)
            {
                int after = idx + marker.Length;
                bool bare = after >= tip.Length || tip[after] == '\n';
                if (bare && !b.Cost.IsZero)
                {
                    var available = EntityActionExtractor
                        .GetFactionResourcesAsCostPublic(em, OwnFaction(em));
                    tip = tip.Insert(after, UIHelpers.FormatCostRich(b.Cost, available));
                }
            }
            return tip;
        }

        // ── Categories, colors, icons ──────────────────────────────────────

        private static Color CategoryColor(Category cat) => cat switch
        {
            Category.Economy  => EconomyGreen,
            Category.Defense  => DefenseBlue,
            Category.Research => ResearchBrown,
            Category.Religion => ReligionPurple,
            _                 => MilitaryRed,
        };

        private static Category BuildingCategory(string id)
        {
            if (EconomyBuildings.Contains(id)) return Category.Economy;
            if (DefenseBuildings.Contains(id)) return Category.Defense;
            if (ReligionBuildings.Contains(id)) return Category.Religion;
            return Category.Military;
        }

        private static Category UnitCategory(string id)
        {
            switch (id)
            {
                case "Worker":
                case "Ledger":
                case "BazaarPack":
                case "BazaarUnpack":
                    return Category.Economy;
                case "Litharch":
                    return Category.Religion;
            }
            if (id.StartsWith("Sect_", System.StringComparison.Ordinal)
                || id.StartsWith("Reliquary_", System.StringComparison.Ordinal))
                return Category.Religion;
            if (id.StartsWith("KeepWing_", System.StringComparison.Ordinal))
            {
                return id switch
                {
                    "KeepWing_Civic"      => Category.Economy,
                    "KeepWing_Economic"   => Category.Economy,
                    "KeepWing_Engineers"  => Category.Defense,
                    "KeepWing_Librarians" => Category.Research,
                    "KeepWing_Temple"     => Category.Religion,
                    _                     => Category.Military,
                };
            }
            return Category.Military;
        }

        /// <summary>Catalog symbol for a bare id (queue chips have no
        /// ActionButton to read a label or texture from).</summary>
        private Sprite ResolveSpriteById(string id)
        {
            if (_symbols == null || string.IsNullOrEmpty(id)) return null;
            if (_symbols.TryGetValue(id, out var byId) && byId != null) return byId;
            string name = TheWaningBorder.UI.EntityInfoExtractor.GetUnitDisplayName(id);
            return !string.IsNullOrEmpty(name) && _symbols.TryGetValue(name, out var byName)
                ? byName : null;
        }

        private Sprite ResolveSprite(in ActionButton b)
        {
            if (_symbols != null)
            {
                if (b.Id != null && _symbols.TryGetValue(b.Id, out var byId) && byId != null)
                    return byId;
                string label = b.Label ?? "";
                int nl = label.IndexOf('\n');                                  // "Scry\n12s"
                if (nl > 0) label = label.Substring(0, nl);
                int lv = label.IndexOf("  (", System.StringComparison.Ordinal); // "Ledger  (Lv 2)"
                if (lv > 0) label = label.Substring(0, lv);
                if (_symbols.TryGetValue(label, out var byLabel) && byLabel != null)
                    return byLabel;
            }
            if (b.Icon != null)
            {
                if (!_spriteCache.TryGetValue(b.Icon, out var sprite) || sprite == null)
                {
                    sprite = Sprite.Create(b.Icon,
                        new Rect(0f, 0f, b.Icon.width, b.Icon.height),
                        new Vector2(0.5f, 0.5f), 100f);
                    _spriteCache[b.Icon] = sprite;
                }
                return sprite;
            }
            return null;
        }

        // ── Click execution ────────────────────────────────────────────────

        private void Execute(Entity entity, ActionButton b, bool isTrain)
        {
            var em = EM(out bool ok);
            if (!ok || !em.Exists(entity)) return;

            switch (b.Id)
            {
                case "BazaarPack":
                    // Routed: BazaarPackSystem destroys the building + spawns
                    // the wagon, so the trigger must land on every peer.
                    CommandRouter.IssueBazaarPack(em, entity, pack: true);
                    return;
                // UnitCategory() already classifies BazaarUnpack, so the button
                // shows on this panel — but it had no case here and fell through
                // to ExecuteTrain, i.e. clicking Unpack did nothing on the
                // authored UI while working fine on the code-built one.
                case "BazaarUnpack":
                    CommandRouter.IssueBazaarPack(em, entity, pack: false);
                    return;
                case "Reliquary_Build":
                {
                    var faction = OwnFaction(em);
                    // The Reliquary is a normal placeable building now, capped
                    // at 5 per faction (docs/Design/Sects.md section 1). Check
                    // the cap BEFORE spending, then route through the command
                    // path so the build replicates in multiplayer.
                    if (!TheWaningBorder.Core.Commands.CommandRouter.CanPlaceBuilding(
                            em, "Sect_Reliquary", faction))
                    {
                        PlayerNotificationSystem.NotifyError(Loc.T("Maximum 5 Reliquaries"));
                        return;
                    }
                    // Affordability CHECK only — PlaceBuildingDirect spends
                    // on every peer (docs/Multiplayer_LAN_Readiness.md).
                    if (!FactionEconomy.CanAfford(em, faction, b.Cost))
                    {
                        PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                        return;
                    }
                    var pos = em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Position;
                    var site = new float3(pos.x + 8f, 0f, pos.z);
                    site.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(site.x, site.z);
                    TheWaningBorder.Core.Commands.CommandRouter.IssuePlaceBuilding(
                        em, "Sect_Reliquary", site, faction);
                    return;
                }
                case "Reliquary_Scry":
                    BeginReliquaryGroundAbility(entity, 0,
                        TheWaningBorder.Systems.Sect.ReliquaryHelper.ScryRadius);
                    return;
                case "Reliquary_Lockout":
                    BeginReliquaryGroundAbility(entity, 1,
                        TheWaningBorder.Systems.Sect.ReliquaryHelper.LockoutRadius);
                    return;
                case "Reliquary_Vision":
                    TheWaningBorder.Core.Commands.CommandRouter.IssueReliquaryAbility(em, entity, 2, default);
                    return;
                case "Alanthor_Volleys":
                    if (!TheWaningBorder.Abilities.AlanthorActiveHelper
                            .TriggerChoreographedVolleys(em, OwnFaction(em)))
                        PlayerNotificationSystem.NotifyError(
                            Loc.T("Choreographed Volleys is recharging"));
                    return;
                case "Alanthor_RangingShot":
                    if (!TheWaningBorder.Abilities.AlanthorActiveHelper
                            .TriggerRangingShot(em, OwnFaction(em)))
                        PlayerNotificationSystem.NotifyError(
                            Loc.T("No planted siege engine ready"));
                    return;
            }

            if (b.Id.StartsWith("KeepWing_", System.StringComparison.Ordinal))
            {
                ExecuteKeepWing(em, entity, b);
                return;
            }

            if (isTrain) ExecuteTrain(em, entity, b);
            else ExecuteResearch(em, entity, b);
        }

        private static void BeginReliquaryGroundAbility(Entity reliquary, int ability, float radius)
        {
            GroundTargeting.Begin(radius, new Color(0.45f, 0.85f, 1f, 0.35f), target =>
            {
                var em = EM(out bool ok);
                if (ok && em.Exists(reliquary))
                    TheWaningBorder.Core.Commands.CommandRouter.IssueReliquaryAbility(em, reliquary, ability, target);
            });
        }

        private void ExecuteKeepWing(EntityManager em, Entity entity, in ActionButton b)
        {
            if (!em.HasComponent<KeepWings>(entity)) return;
            if (em.HasComponent<KeepWingConstruction>(entity))
            {
                PlayerNotificationSystem.Notify(Loc.T("A wing is already under construction"));
                return;
            }
            if (!System.Enum.TryParse(b.Id.Substring("KeepWing_".Length), out KeepWingType wing)
                || wing == KeepWingType.None)
                return;
            var wings = em.GetComponentData<KeepWings>(entity);
            if (wings.Count >= TheWaningBorder.Core.Settings.KeepWingConfig.MaxWings
                || wings.Has(wing))
                return;
            // Affordability CHECK only — the SPEND lives in the charged
            // executor (KeepWingChargedDirect) so single-player and every
            // lockstep peer debit the same bank at the same tick
            // (docs/Multiplayer_LAN_Readiness.md).
            if (!FactionEconomy.CanAfford(em, OwnFaction(em), b.Cost))
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                return;
            }
            float duration = TheWaningBorder.Core.Settings.KeepWingConfig.BuildDuration;
            TheWaningBorder.Core.Commands.CommandRouter.IssueKeepWingCharged(
                em, entity, (byte)wing, duration);
        }

        private void ExecuteTrain(EntityManager em, Entity entity, in ActionButton b)
        {
            var faction = OwnFaction(em);

            if (CommandRouter.IsProductionQueueFull(em, entity))
            {
                PlayerNotificationSystem.Notify(Loc.T("Training queue full"));
                return;
            }
            int popCost = PopulationHelper.GetUnitPopulationCost(b.Id);
            if (!PopulationHelper.HasPopulationCapacity(faction, popCost))
            {
                PlayerNotificationSystem.Notify(Loc.T("Population cap reached"));
                return;
            }
            // Affordability CHECK only — TrainCommandDirect spends on every
            // peer with this same formula (docs/Multiplayer_LAN_Readiness.md).
            var cost = WarSectCostHelper.MilitaryDiscount(em, faction, b.Id, b.Cost);
            if (!FactionEconomy.CanAfford(em, faction, cost))
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                return;
            }
            CommandRouter.IssueTrain(em, entity, b.Id);
        }

        private void ExecuteResearch(EntityManager em, Entity entity, in ActionButton b)
        {
            var faction = OwnFaction(em);

            if (CommandRouter.IsProductionQueueFull(em, entity))
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Production queue full"));
                return;
            }
            // Affordability CHECK only — ResearchCommandDirect spends on
            // every peer (docs/Multiplayer_LAN_Readiness.md).
            if (!FactionEconomy.CanAfford(em, faction, b.Cost))
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                return;
            }
            CommandRouter.IssueResearch(em, entity, b.Id);
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private Faction OwnFaction(EntityManager em)
        {
            if (_entity != Entity.Null && em.Exists(_entity) && em.HasComponent<FactionTag>(_entity))
                return em.GetComponentData<FactionTag>(_entity).Value;
            return GameSettings.LocalPlayerFaction;
        }

    }
}
