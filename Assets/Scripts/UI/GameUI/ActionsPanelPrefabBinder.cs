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
// Location: Assets/Scripts/UI/GameUI/ActionsPanelPrefabBinder.cs

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
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
        private RectTransform _tooltipRoot;
        private TMP_Text _tooltipText;

        private Dictionary<string, Sprite> _symbols;
        private static readonly Dictionary<Texture2D, Sprite> _spriteCache = new();

        private SelectionChangeDetector _detector;
        private float _timer;
        private Entity _entity;

        public void Init(Dictionary<string, Sprite> symbols) => _symbols = symbols;

        // ── Setup ──────────────────────────────────────────────────────────

        private void Awake()
        {
            Active = true;

            _group = gameObject.GetComponent<CanvasGroup>();
            if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();

            var grid = FindDeep(transform, "Actions");
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

            BuildTooltip();
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

                // Hover relay for the tooltip; clicks stay on the Button so
                // the Synty pressed/disabled animations keep working.
                var relay = slot.Button.gameObject.AddComponent<UiClickRelay>();
                relay.OnEnter = () => ShowTooltip(s.Tooltip);
                relay.OnExit = HideTooltip;
            }

            var icon = FindDeep(root, "ICON");
            if (icon != null)
            {
                slot.Icon = icon.GetComponent<Image>();
                if (slot.Icon != null) slot.Icon.preserveAspect = true;
            }
            var iconAdditive = FindDeep(root, "ICON_Additive");
            if (iconAdditive != null) iconAdditive.gameObject.SetActive(false);

            var cooldown = FindDeep(root, "SPR_Cooldown");
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
            var input = FindDeep(root, "Input");
            if (input != null) input.gameObject.SetActive(false);

            // State backgrounds to tint with the category color. Disabled
            // keeps its authored dark look (the Button animator swaps to it
            // for locked slots).
            var tints = new List<Image>(4);
            CollectStateBackgrounds(root, "Normal", tints);
            CollectStateBackgrounds(root, "Highlighted", tints);
            CollectStateBackgrounds(root, "Selected", tints);
            slot.TintBg = tints.ToArray();

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
            var state = FindDeep(buttonRoot, stateName);
            if (state == null) return;
            foreach (var t in state.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "SPR_Background") continue;
                var img = t.GetComponent<Image>();
                if (img != null) into.Add(img);
            }
        }

        private void BuildTooltip()
        {
            _tooltipRoot = GameUIKit.Rect(transform, "tooltip");
            _tooltipRoot.anchorMin = new Vector2(0f, 1f);
            _tooltipRoot.anchorMax = new Vector2(1f, 1f);
            _tooltipRoot.pivot = new Vector2(0.5f, 0f);
            _tooltipRoot.anchoredPosition = new Vector2(0f, 8f);
            _tooltipRoot.sizeDelta = new Vector2(0f, 0f);

            var bg = GameUIKit.Image(_tooltipRoot, "bg", GameUIKit.PanelBg);
            GameUIKit.Stretch(bg.rectTransform);

            _tooltipText = GameUIKit.Text(_tooltipRoot, "text", "", 22f, GameUIKit.TextMain);
            _tooltipText.rectTransform.anchorMin = Vector2.zero;
            _tooltipText.rectTransform.anchorMax = Vector2.one;
            _tooltipText.rectTransform.offsetMin = new Vector2(14f, 10f);
            _tooltipText.rectTransform.offsetMax = new Vector2(-14f, -10f);

            var fitter = _tooltipRoot.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var layout = _tooltipRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            // The bg must not participate in the layout — it just stretches.
            bg.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            _tooltipRoot.gameObject.SetActive(false);
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
            }
            else
            {
                used = RenderBuilding(em);
                if (used < 0) { SetShown(false); return; }
            }

            SetShown(used > 0);
        }

        private void SetShown(bool shown)
        {
            _group.alpha = shown ? 1f : 0f;
            _group.interactable = shown;
            _group.blocksRaycasts = shown;
            if (!shown && _tooltipRoot != null && _tooltipRoot.gameObject.activeSelf)
                _tooltipRoot.gameObject.SetActive(false);
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

            RenderProgress(em, info);
            return used;
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
            int idx = tip.IndexOf("\nCost: ", System.StringComparison.Ordinal);
            if (idx >= 0)
            {
                int after = idx + "\nCost: ".Length;
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
                    if (!em.HasComponent<BazaarPackCommand>(entity))
                        em.AddComponent<BazaarPackCommand>(entity);
                    return;
                case "Reliquary_Build":
                {
                    var faction = OwnFaction(em);
                    if (!FactionEconomy.Spend(em, faction, b.Cost))
                    {
                        PlayerNotificationSystem.NotifyError("Not enough resources");
                        return;
                    }
                    var pos = em.GetComponentData<Unity.Transforms.LocalTransform>(entity).Position;
                    var site = new float3(pos.x + 8f, 0f, pos.z);
                    site.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(site.x, site.z);
                    TheWaningBorder.Entities.BuildingFactory
                        .CreateReliquaryUnderConstruction(em, site, faction);
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
                    TheWaningBorder.Systems.Sect.ReliquaryHelper.Fire(em, entity, 2, default);
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
                    TheWaningBorder.Systems.Sect.ReliquaryHelper.Fire(em, reliquary, ability, target);
            });
        }

        private void ExecuteKeepWing(EntityManager em, Entity entity, in ActionButton b)
        {
            if (!em.HasComponent<KeepWings>(entity)) return;
            if (em.HasComponent<KeepWingConstruction>(entity))
            {
                PlayerNotificationSystem.Notify("A wing is already under construction");
                return;
            }
            if (!System.Enum.TryParse(b.Id.Substring("KeepWing_".Length), out KeepWingType wing)
                || wing == KeepWingType.None)
                return;
            var wings = em.GetComponentData<KeepWings>(entity);
            if (wings.Count >= TheWaningBorder.Core.Settings.KeepWingConfig.MaxWings
                || wings.Has(wing))
                return;
            if (!FactionEconomy.Spend(em, OwnFaction(em), b.Cost))
            {
                PlayerNotificationSystem.NotifyError("Not enough resources");
                return;
            }
            float duration = TheWaningBorder.Core.Settings.KeepWingConfig.BuildDuration;
            em.AddComponentData(entity, new KeepWingConstruction
            {
                Wing = (byte)wing, Remaining = duration, Total = duration,
            });
        }

        private void ExecuteTrain(EntityManager em, Entity entity, in ActionButton b)
        {
            var faction = OwnFaction(em);

            if (CommandRouter.IsProductionQueueFull(em, entity))
            {
                PlayerNotificationSystem.Notify("Training queue full");
                return;
            }
            int popCost = PopulationHelper.GetUnitPopulationCost(b.Id);
            if (!PopulationHelper.HasPopulationCapacity(faction, popCost))
            {
                PlayerNotificationSystem.Notify("Population cap reached");
                return;
            }
            var cost = WarSectCostHelper.MilitaryDiscount(em, faction, b.Id, b.Cost);
            if (!FactionEconomy.Spend(em, faction, cost))
            {
                PlayerNotificationSystem.NotifyError("Not enough resources");
                return;
            }
            CommandRouter.IssueTrain(em, entity, b.Id);
        }

        private void ExecuteResearch(EntityManager em, Entity entity, in ActionButton b)
        {
            var faction = OwnFaction(em);

            if (CommandRouter.IsProductionQueueFull(em, entity))
            {
                PlayerNotificationSystem.NotifyError("Production queue full");
                return;
            }
            if (!FactionEconomy.Spend(em, faction, b.Cost))
            {
                PlayerNotificationSystem.NotifyError("Not enough resources");
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

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, name, System.StringComparison.OrdinalIgnoreCase))
                    return t;
            return null;
        }

        private void ShowTooltip(string text)
        {
            if (string.IsNullOrEmpty(text)) { HideTooltip(); return; }
            _tooltipText.text = text;
            if (!_tooltipRoot.gameObject.activeSelf) _tooltipRoot.gameObject.SetActive(true);
        }

        private void HideTooltip()
        {
            if (_tooltipRoot != null && _tooltipRoot.gameObject.activeSelf)
                _tooltipRoot.gameObject.SetActive(false);
        }
    }
}
