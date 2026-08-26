// ActionsPanelBinder.cs
// Selection-driven ACTIONS panel for the final game UI. Code-built (no
// authored prefab yet — see GameUIKit) and spawned by GameUIManager in the
// bottom-right, left of the minimap.
//
// Content comes from the kept data layer, which already encodes every
// availability rule:
// - BuildingActionLayouts.TryResolve — the authored 3x4 grid for buildings
//   with a fixed layout (Hall / Hut / Gatherer's Hut, Alanthor + pre-culture):
//   appear-age blanks, level/age/prereq locks, chain tiers, consumed techs.
// - EntityActionExtractor.GetActionInfo — everything else: training rosters
//   (culture + building-level gated), research lists (era/prereq gated),
//   temple upgrade, hut age-up choice, wall gate/tower conversions, bazaar
//   pack/unpack, hub wall extension, vault storage, reliquary abilities.
// Buttons that fail a requirement render locked (dim, requirement in the
// tooltip); unaffordable ones render red-tinted. Clicks re-validate before
// spending (queue caps, population, cost) and route through CommandRouter
// so orders replicate in multiplayer.
//
// Builder placement (ActionType.BuildingPlacement) is deliberately NOT
// rendered here — BuilderPanelBinder owns the build palette.

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
    public sealed class ActionsPanelBinder : MonoBehaviour
    {
        private const float RefreshInterval = 0.15f;
        private const float PanelWidth = 950f;
        private const int GridCols = 4;
        private static readonly Vector2 GridCell = new Vector2(214f, 150f);
        private const int QueueSlots = 5;

        private RectTransform _root;
        private TMP_Text _title;
        private RectTransform _grid;
        private TMP_Text _statusA, _statusB;      // requirement / info lines
        private ProgressBar _trainBar, _researchBar;
        private QueueSlot[] _queue;
        private WideButton[] _wide;               // temple upgrade / vault rows

        private readonly List<ActionWidget> _widgets = new List<ActionWidget>();

        private SelectionChangeDetector _detector;
        private float _timer;
        private Entity _entity;

        // ── Widget shells ──────────────────────────────────────────────────

        private sealed class ActionWidget
        {
            public GameObject Root;
            public Image Bg;
            public RawImage Icon;
            public TMP_Text Label;
            public TMP_Text CostLine;
            public System.Action Click;
            public string Tooltip;
        }

        private sealed class QueueSlot
        {
            public GameObject Root;
            public Image Bg;
            public TMP_Text Label;
            public int Index;
            public bool Cancellable;
        }

        private sealed class WideButton
        {
            public GameObject Root;
            public Image Bg;
            public TMP_Text Label;
            public System.Action Click;
            public string Tooltip;
        }

        private sealed class ProgressBar
        {
            public GameObject Root;
            public RectTransform Fill;
            public Image FillImage;
            public TMP_Text Label;

            public void Set(bool visible, string text, float pct, Color color)
            {
                if (Root.activeSelf != visible) Root.SetActive(visible);
                if (!visible) return;
                Fill.anchorMax = new Vector2(Mathf.Clamp01(pct), 1f);
                FillImage.color = color;
                Label.text = text;
            }
        }

        // ── Construction ───────────────────────────────────────────────────

        private void Awake()
        {
            _root = GameUIKit.Rect(transform, "ActionsPanel");
            _root.anchorMin = new Vector2(1f, 0f);
            _root.anchorMax = new Vector2(1f, 0f);
            _root.pivot = new Vector2(1f, 0f);
            // Left of the minimap (minimap occupies the right ~470 px).
            _root.anchoredPosition = new Vector2(-510f, 40f);
            _root.sizeDelta = new Vector2(PanelWidth, 400f);

            GameUIKit.PanelChrome(_root);

            // Root stack + fitter: the panel grows upward from its bottom
            // pivot as sections appear. Children propagate their preferred
            // heights through the layout-group chain — no inner fitters.
            var content = GameUIKit.Rect(_root, "content");
            GameUIKit.VStack(content, 20f, 12f);
            var fitter = _root.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var rootStack = _root.gameObject.AddComponent<VerticalLayoutGroup>();
            rootStack.childControlWidth = true;
            rootStack.childControlHeight = true;
            rootStack.childForceExpandWidth = true;
            rootStack.childForceExpandHeight = false;

            _title = GameUIKit.Text(content, "title", Loc.T("Actions"), 40f, GameUIKit.Gold);
            _title.fontStyle = FontStyles.Bold;

            _grid = GameUIKit.Rect(content, "grid");
            var gl = _grid.gameObject.AddComponent<GridLayoutGroup>();
            gl.cellSize = GridCell;
            gl.spacing = new Vector2(10f, 10f);
            gl.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gl.constraintCount = GridCols;

            _statusA = GameUIKit.Text(content, "statusA", "", 26f, GameUIKit.TextMain);
            _statusB = GameUIKit.Text(content, "statusB", "", 24f, GameUIKit.TextLocked);

            _trainBar = MakeBar(content, "trainBar");
            _researchBar = MakeBar(content, "researchBar");

            var queueRow = GameUIKit.Rect(content, "queueRow");
            var h = queueRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 8f;
            h.childControlWidth = false;
            h.childControlHeight = false;
            h.childForceExpandWidth = false;
            h.childAlignment = TextAnchor.MiddleLeft;
            GameUIKit.FixHeight(queueRow.gameObject, 90f);
            _queue = new QueueSlot[QueueSlots];
            for (int i = 0; i < QueueSlots; i++)
                _queue[i] = MakeQueueSlot(queueRow, i);

            _wide = new WideButton[4];
            for (int i = 0; i < _wide.Length; i++)
                _wide[i] = MakeWideButton(content, "wide" + i);

            _root.gameObject.SetActive(false);
        }

        private ProgressBar MakeBar(Transform parent, string name)
        {
            var bar = new ProgressBar();
            var rt = GameUIKit.Rect(parent, name);
            GameUIKit.FixHeight(rt.gameObject, 36f);
            var bg = GameUIKit.Image(rt, "bg", GameUIKit.BarBg);
            GameUIKit.Stretch(bg.rectTransform);
            var fill = GameUIKit.Image(rt, "fill", GameUIKit.BarGold);
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            fill.rectTransform.offsetMin = Vector2.zero;
            fill.rectTransform.offsetMax = Vector2.zero;
            var label = GameUIKit.Text(rt, "label", "", 22f, Color.white,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(label.rectTransform);
            bar.Root = rt.gameObject;
            bar.Fill = fill.rectTransform;
            bar.FillImage = fill;
            bar.Label = label;
            rt.gameObject.SetActive(false);
            return bar;
        }

        private QueueSlot MakeQueueSlot(Transform parent, int index)
        {
            var slot = new QueueSlot { Index = index };
            var rt = GameUIKit.Rect(parent, "slot" + index);
            rt.sizeDelta = new Vector2(90f, 90f);
            var bg = GameUIKit.Image(rt, "bg", GameUIKit.BarBg, raycast: true);
            GameUIKit.Stretch(bg.rectTransform);
            var label = GameUIKit.Text(rt, "label", "", 22f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(label.rectTransform);

            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnRightClick = () => CancelQueueSlot(slot);
            UITooltip.Bind(bg.gameObject, () => slot.Cancellable
                ? Loc.T("Queued — right-click to cancel and refund") : null);

            slot.Root = rt.gameObject;
            slot.Bg = bg;
            slot.Label = label;
            rt.gameObject.SetActive(false);
            return slot;
        }

        private WideButton MakeWideButton(Transform parent, string name)
        {
            var wide = new WideButton();
            var rt = GameUIKit.Rect(parent, name);
            GameUIKit.FixHeight(rt.gameObject, 64f);
            var bg = GameUIKit.Image(rt, "bg", GameUIKit.ButtonBg, raycast: true);
            GameUIKit.Stretch(bg.rectTransform);
            var label = GameUIKit.Text(rt, "label", "", 26f, GameUIKit.TextMain,
                TextAlignmentOptions.Center, wrap: false);
            GameUIKit.Stretch(label.rectTransform);

            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnLeftClick = () => wide.Click?.Invoke();
            UITooltip.Bind(bg.gameObject, () => wide.Tooltip);

            wide.Root = rt.gameObject;
            wide.Bg = bg;
            wide.Label = label;
            rt.gameObject.SetActive(false);
            return wide;
        }

        private ActionWidget MakeActionWidget()
        {
            var w = new ActionWidget();
            var rt = GameUIKit.Rect(_grid, "action" + _widgets.Count);
            var bg = GameUIKit.Image(rt, "bg", GameUIKit.ButtonBg, raycast: true);
            GameUIKit.Stretch(bg.rectTransform);

            var iconGo = new GameObject("icon", typeof(RectTransform), typeof(RawImage));
            iconGo.transform.SetParent(rt, false);
            var icon = iconGo.GetComponent<RawImage>();
            icon.raycastTarget = false;
            var iconRt = icon.rectTransform;
            iconRt.anchorMin = new Vector2(0.5f, 1f);
            iconRt.anchorMax = new Vector2(0.5f, 1f);
            iconRt.pivot = new Vector2(0.5f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, -8f);
            iconRt.sizeDelta = new Vector2(76f, 76f);

            var label = GameUIKit.Text(rt, "label", "", 24f, GameUIKit.TextMain,
                TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = new Vector2(0f, 0.28f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(6f, 0f);
            label.rectTransform.offsetMax = new Vector2(-6f, -4f);

            var cost = GameUIKit.Text(rt, "cost", "", 20f, GameUIKit.TextDim,
                TextAlignmentOptions.Center, wrap: false);
            cost.rectTransform.anchorMin = new Vector2(0f, 0f);
            cost.rectTransform.anchorMax = new Vector2(1f, 0.28f);
            cost.rectTransform.offsetMin = new Vector2(4f, 4f);
            cost.rectTransform.offsetMax = new Vector2(-4f, 0f);

            var relay = UITooltip.Relay(bg.gameObject);
            relay.OnLeftClick = () => w.Click?.Invoke();
            UITooltip.Bind(bg.gameObject, () => w.Tooltip);

            w.Root = rt.gameObject;
            w.Bg = bg;
            w.Icon = icon;
            w.Label = label;
            w.CostLine = cost;
            _widgets.Add(w);
            return w;
        }

        // ── Refresh loop ───────────────────────────────────────────────────

        private void Update()
        {
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
            if (!ok || GameSettings.IsObserver) { Hide(); return; }

            _entity = FirstOwnedSelected(em);
            if (_entity == Entity.Null) { Hide(); return; }
            // Under-construction buildings surface no actions.
            if (em.HasComponent<UnderConstruction>(_entity)
                && !em.HasComponent<GathererHutConverting>(_entity)) { Hide(); return; }

            var info = EntityActionExtractor.GetActionInfo(_entity, em);
            bool hasLayout = BuildingActionLayouts.TryResolve(_entity, em, out var layoutSlots);

            // The builder palette panel owns placement; nothing else to show.
            if (!hasLayout && (info.Type == ActionType.None
                || info.Type == ActionType.BuildingPlacement)) { Hide(); return; }

            // While the AUTHORED actions panel is active it owns the layout
            // grids and the train/research grids; this panel keeps only the
            // special selections (temple upgrade lever, vault, wall
            // conversions, hut age-up, bazaar wagon, hub wall).
            bool authored = ActionsPanelPrefabBinder.Active;
            if (authored && (hasLayout
                || info.Type == ActionType.UnitTraining
                || info.Type == ActionType.UnitTrainingAndResearch)) { Hide(); return; }

            BeginContent();

            if (hasLayout)
            {
                _title.text = Loc.T("Actions");
                RenderLayoutGrid(em, layoutSlots);
            }
            else
            {
                switch (info.Type)
                {
                    case ActionType.UnitTraining:
                    case ActionType.UnitTrainingAndResearch:
                        _title.text = info.Actions != null && info.Actions.Count > 0
                            ? Loc.T("Train Units") : Loc.T("Research");
                        RenderClassicActions(em, info.Actions, treatAsTraining: true);
                        break;

                    case ActionType.TempleUpgrade:
                        _title.text = Loc.T("Temple of Ridan");
                        // Training row lives on the authored panel when active;
                        // the upgrade lever stays here (it must not sit in the
                        // authored unit/research grid).
                        if (!authored)
                            RenderClassicActions(em, info.Actions, treatAsTraining: true);
                        RenderTempleUpgrade(em);
                        break;

                    case ActionType.GathererHutAgeUpChoice:
                        _title.text = Loc.T("Age-Up Choice");
                        if (info.Actions == null || info.Actions.Count == 0)
                            _statusA.text = Loc.T("Converting...");
                        else
                            RenderClassicActions(em, info.Actions, treatAsTraining: false);
                        break;

                    case ActionType.WallInstanceUpgrade:
                        _title.text = Loc.T("Upgrade Wall");
                        RenderClassicActions(em, info.Actions, treatAsTraining: false);
                        break;

                    case ActionType.BazaarWagonUnpack:
                        _title.text = Loc.T("Bazaar Wagon");
                        RenderClassicActions(em, info.Actions, treatAsTraining: false);
                        break;

                    case ActionType.HubBuildWall:
                        _title.text = BuilderCommandPanel.IsPlacingBuilding
                            ? Loc.T("Left-click to place hub, Right/Esc to cancel")
                            : Loc.T("Extend Wall");
                        RenderClassicActions(em, info.Actions, treatAsTraining: false);
                        break;

                    case ActionType.VaultManagement:
                        _title.text = Loc.T("Vault of Almiérra");
                        RenderVault(em);
                        break;
                }
            }

            // Research actions render for any building with a research list —
            // the layout grid consumes them itself (chain slots), so classic
            // panels only (and never while the authored panel owns research).
            if (!hasLayout && !authored
                && (info.Type == ActionType.UnitTraining
                    || info.Type == ActionType.UnitTrainingAndResearch
                    || info.Type == ActionType.TempleUpgrade))
            {
                var research = EntityActionExtractor.GetResearchActions(_entity, em);
                foreach (var b in research)
                {
                    var button = b;
                    AddGridButton(button, isTrain: false, em);
                }
            }

            // Building level-up, as a wide row rather than the pill that used
            // to float off the selection header. The authored grid renders its
            // own copy in a free cell; only one of the two panels is ever up.
            RenderUpgradeRow(em);

            // Queue + progress bars for anything that trains or researches.
            RenderQueueAndBars(em, info);

            EndContent();
        }

        private void Hide()
        {
            if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }

        private int _usedWidgets, _usedWide;

        private void BeginContent()
        {
            _usedWidgets = 0;
            _usedWide = 0;
            _statusA.text = "";
            _statusB.text = "";
            _trainBar.Set(false, "", 0f, GameUIKit.BarGold);
            _researchBar.Set(false, "", 0f, GameUIKit.BarBlue);
            for (int i = 0; i < _queue.Length; i++) _queue[i].Root.SetActive(false);
            _queue[0].Root.transform.parent.gameObject.SetActive(false);
        }

        private void EndContent()
        {
            for (int i = _usedWidgets; i < _widgets.Count; i++)
                if (_widgets[i].Root.activeSelf) _widgets[i].Root.SetActive(false);
            for (int i = _usedWide; i < _wide.Length; i++)
                if (_wide[i].Root.activeSelf) _wide[i].Root.SetActive(false);

            _statusA.gameObject.SetActive(!string.IsNullOrEmpty(_statusA.text));
            _statusB.gameObject.SetActive(!string.IsNullOrEmpty(_statusB.text));
            _grid.gameObject.SetActive(_usedWidgets > 0);
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        }

        // ── Grid rendering ─────────────────────────────────────────────────

        private void RenderLayoutGrid(EntityManager em, ResolvedSlot[] slots)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].Empty) continue;
                AddGridButton(slots[i].Button, slots[i].IsTrain, em);
            }
        }

        private void RenderClassicActions(EntityManager em, List<ActionButton> actions,
            bool treatAsTraining)
        {
            if (actions == null) return;
            foreach (var b in actions)
                AddGridButton(b, treatAsTraining, em);
        }

        private void AddGridButton(ActionButton b, bool isTrain, EntityManager em)
        {
            var w = _usedWidgets < _widgets.Count ? _widgets[_usedWidgets] : MakeActionWidget();
            _usedWidgets++;

            bool locked = !b.Enabled;
            bool poor = b.Enabled && !b.CanAfford;

            w.Root.SetActive(true);
            w.Bg.color = locked ? GameUIKit.ButtonBgLocked
                       : poor ? GameUIKit.ButtonBgPoor
                       : GameUIKit.ButtonBg;
            w.Icon.gameObject.SetActive(b.Icon != null);
            if (b.Icon != null) w.Icon.texture = b.Icon;
            w.Label.text = b.Label;
            w.Label.color = locked ? GameUIKit.TextDim : GameUIKit.TextMain;
            // With an icon the label drops to a caption under it.
            w.Label.rectTransform.anchorMin = new Vector2(0f, b.Icon != null ? 0.05f : 0.28f);
            w.Label.rectTransform.anchorMax = new Vector2(1f, b.Icon != null ? 0.42f : 1f);
            w.CostLine.gameObject.SetActive(b.Icon == null && !b.Cost.IsZero);
            if (b.Icon == null && !b.Cost.IsZero)
            {
                var faction = OwnFaction(em);
                var available = EntityActionExtractor.GetFactionResourcesAsCostPublic(em, faction);
                w.CostLine.text = UIHelpers.FormatCostRich(b.Cost, available);
            }
            w.Tooltip = ExpandTooltip(b, em);

            var button = b;
            Entity entity = _entity;
            w.Click = locked ? null
                : (System.Action)(() => ExecuteAction(entity, button, isTrain));
        }

        /// <summary>The layout grid's tooltips end in a bare "Cost: " line
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

        // ── Click execution ────────────────────────────────────────────────

        private void ExecuteAction(Entity entity, ActionButton b, bool isTrain)
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
                case "BazaarUnpack":
                    CommandRouter.IssueBazaarPack(em, entity, pack: false);
                    return;
                case "ConvertToWallHub":
                    CommandRouter.IssueConvertHut(em, entity, HutConversionTarget.WallHub);
                    return;
                case "ConvertToWatchTower":
                    CommandRouter.IssueConvertHut(em, entity, HutConversionTarget.WatchTower);
                    return;
                case "WallSegmentToGate":
                {
                    Entity segment = em.HasComponent<WallInstanceParent>(entity)
                        ? em.GetComponentData<WallInstanceParent>(entity).Segment
                        : Entity.Null;
                    if (segment != Entity.Null)
                        CommandRouter.IssueConvertSegmentToGate(em, segment, entity);
                    return;
                }
                case "WallInstanceToTower":
                {
                    var faction = OwnFaction(em);
                    // Affordability CHECK only — the SPEND lives in the
                    // charged executor (WallUpgradeChargedDirect) so single-
                    // player and every lockstep peer debit the same bank at
                    // the same tick (docs/Multiplayer_LAN_Readiness.md).
                    if (!FactionEconomy.CanAfford(em, faction, b.Cost))
                    {
                        PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                        return;
                    }
                    TheWaningBorder.Core.Commands.CommandRouter.IssueWallUpgradeCharged(
                        em, entity, 1, 10f);
                    return;
                }
                case "BuildWall":
                    BuilderCommandPanel.TriggerHubBuildWall(entity);
                    return;
                case "Reliquary_Build":
                {
                    // Antiquity chapel lever: spend and spawn the Reliquary
                    // under construction beside the chapel.
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

        // ── Building level-up ──────────────────────────────────────────────

        private void RenderUpgradeRow(EntityManager em)
        {
            var upgrade = BuildingUpgradeAction.Describe(em, _entity);
            if (!upgrade.Show) return;

            if (upgrade.Progress >= 0f)
            {
                _researchBar.Set(true,
                    string.Format(Loc.T("Upgrading building  {0}%"),
                        (int)(upgrade.Progress * 100f)),
                    upgrade.Progress, GameUIKit.BarGold);
                return;
            }

            Entity entity = _entity;
            AddWideButton(upgrade.Label.Replace('\n', ' '), upgrade.Enabled, upgrade.Tooltip,
                () =>
                {
                    var em2 = EM(out bool ok);
                    if (!ok) return;
                    BuildingUpgradeAction.Execute(em2, entity);
                    _timer = RefreshInterval;
                });
        }

        // ── Temple upgrade section ─────────────────────────────────────────

        private void RenderTempleUpgrade(EntityManager em)
        {
            if (!em.HasComponent<TempleLevel>(_entity)) return;
            var level = em.GetComponentData<TempleLevel>(_entity);

            if (level.Level >= TempleLevelConfig.MaxLevel)
            {
                _statusA.text = string.Format(
                    Loc.T("Temple Level {0} (Maximum) — all eras unlocked"), level.Level);
                return;
            }
            if (em.HasComponent<TempleUpgradeState>(_entity))
            {
                var up = em.GetComponentData<TempleUpgradeState>(_entity);
                float pct = 1f - up.Remaining / up.Duration;
                _researchBar.Set(true,
                    string.Format(Loc.T("Upgrading to Level {0}  {1}%"),
                        up.TargetLevel, (int)(pct * 100)), pct,
                    GameUIKit.BarGold);
                return;
            }

            var faction = OwnFaction(em);
            if (EntityInfoExtractor.GetFactionEra(em, faction) < 2)
            {
                _statusB.text = Loc.T("Advance to Era 2 first (culture choice)");
                return;
            }

            int nextLevel = level.Level + 1;
            int nextEra = TempleLevelConfig.GetEraForLevel(nextLevel);
            var cost = TempleLevelConfig.GetUpgradeCost(level.Level);
            float duration = TempleLevelConfig.GetUpgradeDuration(level.Level);
            int rp = TempleLevelConfig.GetRPGranted(nextLevel);
            bool canAfford = FactionEconomy.CanAfford(em, faction, cost);

            Entity temple = _entity;
            AddWideButton(
                string.Format(Loc.T("Upgrade to Level {0} (Era {1}) — {2}s"),
                    nextLevel, nextEra, (int)duration),
                enabled: canAfford,
                tooltip: "<b>" + Loc.T("Temple upgrade") + "</b>\n" + Loc.T("Cost: ")
                    + UIHelpers.FormatCostRich(cost,
                        EntityActionExtractor.GetFactionResourcesAsCostPublic(em, faction))
                    + "\n" + string.Format(Loc.T("Grants +{0} Religion Points"), rp),
                onClick: () =>
                {
                    var em2 = EM(out bool ok2);
                    if (!ok2 || !em2.Exists(temple)) return;
                    // Affordability CHECK only — TempleUpgradeCommandDirect
                    // spends on every peer (docs/Multiplayer_LAN_Readiness.md).
                    if (!FactionEconomy.CanAfford(em2, OwnFaction(em2), cost))
                    {
                        PlayerNotificationSystem.NotifyError(Loc.T("Not enough resources"));
                        return;
                    }
                    CommandRouter.IssueTempleUpgrade(em2, temple);
                    PlayerNotificationSystem.Notify(string.Format(
                        Loc.T("Temple upgrade started ({0}s)"), (int)duration));
                });
        }

        // ── Vault section ──────────────────────────────────────────────────

        private static readonly string[] VaultResourceNames =
            { "None", "Supplies", "Iron", "Veilstone", "Veilsteel", "Glow" };
        private static int _vaultSelectedResource = 1;

        private void RenderVault(EntityManager em)
        {
            if (!em.HasComponent<VaultStorage>(_entity)) return;
            var vault = em.GetComponentData<VaultStorage>(_entity);
            var faction = OwnFaction(em);

            string stored = vault.ResourceType > 0 && vault.ResourceType < VaultResourceNames.Length
                ? $"{(int)vault.StoredAmount} {Loc.T(VaultResourceNames[vault.ResourceType])}"
                : Loc.T("Empty");
            _statusA.text = string.Format(
                Loc.T("Interest: {0:F0}%/min (compound)   Stored: {1}"),
                vault.InterestRate * 100f, stored);

            bool locked = vault.LockTimer > 0f;
            if (locked)
                _statusB.text = string.Format(Loc.T("LOCKED — {0}:{1:D2} remaining"),
                    (int)(vault.LockTimer / 60f), (int)(vault.LockTimer % 60f));

            if (vault.ResourceType > 0) _vaultSelectedResource = vault.ResourceType;
            int sel = _vaultSelectedResource;
            Entity entity = _entity;

            AddWideButton(
                string.Format(Loc.T("Resource: {0}  (click to cycle)"),
                    Loc.T(VaultResourceNames[sel])),
                enabled: vault.ResourceType == 0,
                tooltip: Loc.T("Pick which resource this vault stores. Locked to the stored type once a deposit is made."),
                onClick: () =>
                {
                    _vaultSelectedResource = _vaultSelectedResource >= 5 ? 1 : _vaultSelectedResource + 1;
                    _timer = RefreshInterval; // repaint next frame
                });

            foreach (int amount in new[] { 100, 500 })
            {
                int amt = amount;
                bool canDeposit = !locked
                    && (vault.ResourceType == 0 || vault.ResourceType == sel)
                    && FactionEconomy.CanAfford(em, faction, VaultCost(sel, amt));
                AddWideButton(string.Format(Loc.T("Deposit {0}"), amt), canDeposit,
                    Loc.T("Deposits lock the vault for a while; interest compounds per minute."),
                    () => VaultAction(entity, sel, amt, deposit: true));
            }

            bool canWithdraw = !locked && vault.ResourceType > 0 && vault.StoredAmount > 0f;
            AddWideButton(
                string.Format(Loc.T("Withdraw All ({0})"), (int)vault.StoredAmount), canWithdraw,
                Loc.T("Returns the stored amount (plus accrued interest) to the bank."),
                () => VaultAction(entity, 0, 0, deposit: false));
        }

        private static Cost VaultCost(int type, int amount) => type switch
        {
            1 => Cost.Of(supplies: amount),
            2 => Cost.Of(iron: amount),
            3 => Cost.Of(veilstone: amount),
            4 => Cost.Of(veilsteel: amount),
            5 => Cost.Of(glow: amount),
            _ => default,
        };

        private void VaultAction(Entity entity, int resourceType, int amount, bool deposit)
        {
            var em = EM(out bool ok);
            if (!ok || !em.Exists(entity) || !em.HasComponent<VaultStorage>(entity)) return;
            var faction = OwnFaction(em);

            // Affordability CHECK only — the bank/VaultStorage move lives in
            // CommandRouter.VaultTransferDirect, which every peer executes
            // (the old body moved resources on the clicking peer alone, and
            // the bank is in the desync checksum).
            // docs/Multiplayer_Desync_Sweep_2026-08-16.md
            if (deposit && !FactionEconomy.CanAfford(em, faction, VaultCost(resourceType, amount)))
                return;

            TheWaningBorder.Core.Commands.CommandRouter.IssueVaultTransfer(
                em, entity, resourceType, amount, deposit);
        }

        private void AddWideButton(string label, bool enabled, string tooltip, System.Action onClick)
        {
            if (_usedWide >= _wide.Length) return;
            var w = _wide[_usedWide++];
            w.Root.SetActive(true);
            w.Label.text = label;
            w.Label.color = enabled ? GameUIKit.Gold : GameUIKit.TextDim;
            w.Bg.color = enabled ? GameUIKit.ButtonBg : GameUIKit.ButtonBgLocked;
            w.Tooltip = tooltip;
            w.Click = enabled ? onClick : null;
        }

        // ── Queue + progress ───────────────────────────────────────────────

        private void RenderQueueAndBars(EntityManager em, in EntityActionInfo info)
        {
            if (info.TrainingState.HasValue)
            {
                var t = info.TrainingState.Value;
                if (t.IsTraining)
                    _trainBar.Set(true,
                        string.Format(Loc.T("Training {0}  {1:F1}s"),
                            t.CurrentUnitId, t.TimeRemaining),
                        t.Progress, GameUIKit.BarGold);

                var queueRow = _queue[0].Root.transform.parent.gameObject;
                queueRow.SetActive(true);

                // Rebuild the full slot list: in-production item + pending.
                int total = t.QueueCapacity;
                var names = new string[QueueSlots];
                int idx = 0;
                if (t.IsTraining && t.CurrentUnitId != null && idx < QueueSlots)
                    names[idx++] = t.CurrentUnitId;
                if (t.Queue != null)
                    for (int i = 0; i < t.Queue.Length && idx < QueueSlots; i++, idx++)
                        names[idx] = t.Queue[i];

                for (int i = 0; i < QueueSlots; i++)
                {
                    var slot = _queue[i];
                    bool occupied = i < total && names[i] != null;
                    slot.Root.SetActive(true);
                    bool producing = i == 0 && t.IsTraining;
                    slot.Cancellable = occupied && !producing;
                    slot.Bg.color = !occupied ? GameUIKit.BarBg
                        : producing ? new Color(0.83f, 0.66f, 0.26f, 0.55f)
                        : GameUIKit.ButtonBg;
                    slot.Label.text = occupied
                        ? (names[i].Length > 3 ? names[i].Substring(0, 3) : names[i])
                        : "";
                }
            }

            if (info.ResearchState.HasValue)
            {
                var r = info.ResearchState.Value;
                if (r.IsResearching)
                    _researchBar.Set(true,
                        string.Format(Loc.T("Researching {0}  {1:F1}s"),
                            r.CurrentTechName, r.TimeRemaining),
                        r.Progress, GameUIKit.BarBlue);
                if (r.Queue != null && r.Queue.Length > 0)
                    _statusB.text = Loc.T("Research queue: ") + string.Join(", ", r.Queue);
            }
        }

        private void CancelQueueSlot(QueueSlot slot)
        {
            if (!slot.Cancellable) return;
            var em = EM(out bool ok);
            if (!ok || !em.Exists(_entity)) return;
            // Through the router, not the helper: the refund must land on
            // every peer via the CancelTrain lockstep opcode, mirroring the
            // spend that now lives in the train executor
            // (docs/Multiplayer_LAN_Readiness.md).
            CommandRouter.IssueCancelTrain(em, _entity, slot.Index,
                TheWaningBorder.Core.Commands.CommandSource.LocalPlayer);
            _timer = RefreshInterval;
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private Faction OwnFaction(EntityManager em)
        {
            if (_entity != Entity.Null && em.Exists(_entity) && em.HasComponent<FactionTag>(_entity))
                return em.GetComponentData<FactionTag>(_entity).Value;
            return GameSettings.LocalPlayerFaction;
        }

        internal static Entity FirstOwnedSelected(EntityManager em)
        {
            var selection = TheWaningBorder.Input.SelectionSystem.CurrentSelection;
            if (selection == null) return Entity.Null;
            for (int i = 0; i < selection.Count; i++)
            {
                var e = selection[i];
                if (!em.Exists(e)) continue;
                if (!em.HasComponent<FactionTag>(e)) continue;
                if (em.GetComponentData<FactionTag>(e).Value != GameSettings.LocalPlayerFaction)
                    continue;
                return e;
            }
            return Entity.Null;
        }

        // The inline tooltip label this panel used to draw is GONE. It lived
        // inside the content VerticalLayoutGroup under a ContentSizeFitter
        // root pivoted at its bottom edge, so showing it grew the panel upward
        // and shifted every grid button — the button under the cursor moved
        // away, pointer-exit fired, the label hid, the panel shrank, the button
        // came back. That loop is what made the buttons flicker. Hovers go to
        // UITooltip, which is on its own canvas and reflows nothing.
    }
}
