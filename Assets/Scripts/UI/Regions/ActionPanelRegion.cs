// ActionPanelRegion — populates the bottom-right "ACTIONS" jade panel with
// command buttons for the currently selected entity. Mirrors the dispatch
// pipeline of the (now-suspended) IMGUI Assets/Scripts/UI/Panels/EntityActionPanel.cs.
//
// Data source: EntityActionExtractor.GetActionInfo(entity) — same struct the
// IMGUI panel consumed, so what you see here is what the old panel showed.
//
// Click dispatch (covered in this slice):
//   ActionType.BuildingPlacement → BuilderCommandPanel.TriggerBuildingPlacement(id)
//   ActionType.UnitTraining      → CommandRouter.IssueTrain(em, entity, id)
//   ActionType.UnitTrainingAndResearch → CommandRouter.IssueTrain for training,
//                                        deferred for research buttons.
// Other ActionTypes (VaultManagement, TempleUpgrade,
// WallInstanceUpgrade, BazaarWagonUnpack) render buttons but their click
// handlers log a TODO and return. They land in follow-up slices.
//
// Per-button dispatch payload lives in VisualElement.userData. The click
// callback is registered ONCE in MakeActionButton so re-binding on every
// 0.25s Refresh doesn't accumulate stale captured-lambda handlers. The
// previous version did `RegisterCallback(lambda)` per refresh with a no-op
// UnregisterCallback that targeted the wrong delegate — selecting a builder
// then a building re-fired the builder's old lambdas on click.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;          // BuildingFactory
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.UI.Panels;

namespace TheWaningBorder.UI.Regions
{
    public sealed class ActionPanelRegion
    {
        // Payload carried on each pooled button's userData. The single
        // static click handler reads this — never captured into a closure.
        private sealed class DispatchTarget
        {
            public ActionType Type;
            public ActionButton Data;
            public Entity Entity;
            public EntityManager Em;
        }

        private readonly Label _title;
        private readonly VisualElement _empty;
        private readonly VisualElement _grid;

        // Re-used button pool. Sized by the largest action set we've seen so
        // far; entries beyond the current action count are hidden, not destroyed.
        private readonly List<VisualElement> _buttonPool = new List<VisualElement>();

        public ActionPanelRegion(VisualElement root)
        {
            _title = root.Q<Label>("actions-title");
            _empty = root.Q<VisualElement>("actions-empty");
            _grid  = root.Q<VisualElement>("actions-grid");
        }

        public void Refresh()
        {
            if (_grid == null || _empty == null) return;

            // Observer mode can't issue commands.
            if (GameSettings.IsObserver) { ShowEmpty("ACTIONS", "Observer mode"); return; }

            // Don't show actions for enemy units we just selected.
            if (!UnifiedUIManager.IsSelectionOwnedByPlayer())
            {
                ShowEmpty("ACTIONS", "Select your own unit or building");
                return;
            }

            var entity = UnifiedUIManager.GetFirstSelectedEntity();
            if (entity == Entity.Null) { ShowEmpty("ACTIONS", "Select your own unit or building"); return; }

            var em = UnifiedUIManager.GetEntityManager();
            if (em.Equals(default(EntityManager))) { ShowEmpty("ACTIONS", "World not ready"); return; }

            var info = EntityActionExtractor.GetActionInfo(entity, em);

            // Synthesise an AGE_UP button when the Temple of Ridan is selected
            // before culture pick. The IMGUI Temple panel rendered this button
            // separately from the training actions list; we splice it into the
            // grid so the new action panel surfaces it without a special slot.
            if (info.Type == ActionType.TempleUpgrade)
            {
                if (info.Actions == null) info.Actions = new List<ActionButton>();
                MaybeInjectAgeUp(info.Actions, entity, em);
            }

            if (info.Type == ActionType.None || info.Actions == null || info.Actions.Count == 0)
            {
                ShowEmpty(TitleFor(info.Type), HintFor(info.Type));
                return;
            }

            ShowGrid(info, entity, em);
        }

        // The "Advance to Era 2" trigger. Mirrors the gating in
        // EntityActionPanel.cs:670-684:
        //   - faction culture is still Cultures.None
        //   - a choice building (Shrine/Vault/Keep) has been completed
        //   - affordability gates the button rather than hiding it
        private const string AgeUpButtonId = "AGE_UP";

        private static void MaybeInjectAgeUp(List<ActionButton> actions, Entity entity, EntityManager em)
        {
            if (!em.HasComponent<TempleOfRidanTag>(entity)) return;

            var faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            // Already aged up? Bail.
            if (GetFactionCulture(em, faction) != Cultures.None) return;

            // Must have completed a choice building (Shrine/Vault/Keep).
            string choice = BuildingFactory.GetFactionChoiceBuilding(em, faction);
            if (string.IsNullOrEmpty(choice)) return;

            bool canAfford = FactionEconomy.CanAfford(em, faction, CultureConfig.AgeUpCost);

            actions.Add(new ActionButton
            {
                Id = AgeUpButtonId,
                Label = "AGE UP",
                Tooltip = "Advance to Era 2 and choose your culture",
                Cost = CultureConfig.AgeUpCost,
                Enabled = true,
                CanAfford = canAfford
            });
        }

        private static byte GetFactionCulture(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var prog = q.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
                if (tags[i].Value == faction) return prog[i].Culture;
            return Cultures.None;
        }

        // ─── State switching ──────────────────────────────────────────────
        private void ShowEmpty(string title, string hint)
        {
            if (_title != null) _title.text = title;
            _empty.style.display = DisplayStyle.Flex;
            _grid.style.display  = DisplayStyle.None;

            var emptyTitle = _empty.Q<Label>(null, "tw-empty-title");
            var emptyHint  = _empty.Q<Label>(null, "tw-empty-hint");
            if (emptyTitle != null) emptyTitle.text = title;
            if (emptyHint  != null) emptyHint.text  = hint;
        }

        private void ShowGrid(EntityActionInfo info, Entity entity, EntityManager em)
        {
            if (_title != null) _title.text = TitleFor(info.Type);
            _empty.style.display = DisplayStyle.None;
            _grid.style.display  = DisplayStyle.Flex;

            // Grow pool if needed.
            while (_buttonPool.Count < info.Actions.Count)
            {
                var btn = MakeActionButton();
                _grid.Add(btn);
                _buttonPool.Add(btn);
            }

            // Bind each pooled button to its ActionButton data. The click
            // handler reads userData on fire — no per-Refresh re-registration.
            for (int i = 0; i < _buttonPool.Count; i++)
            {
                var btn = _buttonPool[i];
                if (i >= info.Actions.Count)
                {
                    btn.style.display = DisplayStyle.None;
                    btn.userData = null;
                    continue;
                }

                var data = info.Actions[i];
                btn.style.display = DisplayStyle.Flex;

                bool dim = !data.Enabled || !data.CanAfford;
                if (dim) btn.AddToClassList("tw-action-btn-disabled");
                else     btn.RemoveFromClassList("tw-action-btn-disabled");

                var label = btn.Q<Label>(null, "tw-action-btn-label");
                if (label != null) label.text = ShortLabel(data);

                btn.tooltip = data.Tooltip ?? data.Label;

                // Refresh the dispatch payload in-place. The DispatchTarget
                // object was allocated once on first bind; subsequent refreshes
                // overwrite the fields so the click handler always sees the
                // current selection's data.
                if (btn.userData is DispatchTarget dt)
                {
                    dt.Type = info.Type;
                    dt.Data = data;
                    dt.Entity = entity;
                    dt.Em = em;
                }
                else
                {
                    btn.userData = new DispatchTarget
                    {
                        Type = info.Type,
                        Data = data,
                        Entity = entity,
                        Em = em
                    };
                }
            }
        }

        // ─── Click dispatch ───────────────────────────────────────────────
        // Single static handler registered once per pooled button — reads the
        // VisualElement's userData payload that ShowGrid keeps fresh.
        private static void OnButtonClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement target &&
                target.userData is DispatchTarget dt)
            {
                if (!dt.Data.Enabled || !dt.Data.CanAfford) return;
                DispatchClick(dt.Type, dt.Data, dt.Entity, dt.Em);
            }
        }

        // Same constant as EntityActionPanel.MAX_TRAIN_QUEUE. Inlined so this
        // file doesn't take a hard reference on the suspended IMGUI panel.
        private const int MaxTrainQueue = 5;

        private static void DispatchClick(ActionType type, ActionButton data, Entity entity, EntityManager em)
        {
            // AGE_UP is the synthetic button injected by MaybeInjectAgeUp — id
            // takes precedence over ActionType so we can route to the culture
            // popup regardless of which type the Temple currently reports.
            if (data.Id == AgeUpButtonId)
            {
                Faction faction = GameSettings.LocalPlayerFaction;
                if (em.HasComponent<FactionTag>(entity))
                    faction = em.GetComponentData<FactionTag>(entity).Value;
                CultureChoicePopup.Show(entity, faction);
                return;
            }

            switch (type)
            {
                case ActionType.BuildingPlacement:
                    BuilderCommandPanel.TriggerBuildingPlacement(data.Id);
                    return;

                case ActionType.UnitTraining:
                case ActionType.UnitTrainingAndResearch:
                case ActionType.TempleUpgrade:
                    // Temple shares the unit-training pipeline for its trainable
                    // units; the AGE_UP id was intercepted above.
                    TryIssueTrain(em, entity, data);
                    return;

                case ActionType.GathererHutAgeUpChoice:
                    // task-109 phase 2 — Alanthor hut age-up choice. The
                    // button id encodes the target (ConvertToWallHub /
                    // ConvertToWatchTower); CommandRouter applies the
                    // ownership + affordability guards and routes through
                    // the lockstep queue when necessary.
                    {
                        HutConversionTarget target = data.Id == "ConvertToWatchTower"
                            ? HutConversionTarget.WatchTower
                            : HutConversionTarget.WallHub;
                        CommandRouter.IssueConvertHut(em, entity, target);
                    }
                    return;

                case ActionType.WallInstanceUpgrade:
                    // task-109 phase 6 — segment-level Gate conversion +
                    // per-instance Tower conversion. The id discriminates:
                    //   "WallSegmentToGate"   → resolve to parent segment,
                    //                            attach a segment-level
                    //                            WallSegmentUpgradeState
                    //                            (Phase 5 picks the 5-region
                    //                            centre on completion).
                    //   "WallInstanceToTower" → legacy single-instance
                    //                            WallUpgradeState path
                    //                            (mirrors the IMGUI panel).
                    DispatchWallInstanceUpgrade(em, entity, data);
                    return;

                default:
                    Debug.LogWarning("[ActionPanelRegion] Click dispatch for ActionType." +
                                     type + " not migrated yet. Button: " + data.Id);
                    return;
            }
        }

        // task-109 phase 6: click handler for the segment-level Gate /
        // per-instance Tower buttons surfaced when a wall instance is
        // selected. Gate routes through CommandRouter.IssueConvertSegmentToGate
        // (lockstep-safe); Tower mirrors EntityActionPanel.cs:1641-1660 by
        // spending + attaching WallUpgradeState directly. The IMGUI panel
        // is the reference; we do not re-enable it.
        private static void DispatchWallInstanceUpgrade(EntityManager em, Entity entity, ActionButton data)
        {
            if (!em.Exists(entity)) return;

            if (data.Id == "WallSegmentToGate")
            {
                // The selected entity is the wall instance; the segment lives
                // on its WallInstanceParent.Segment. Spend + state-add fires
                // on the segment, not the instance.
                if (!em.HasComponent<WallInstanceParent>(entity)) return;
                Entity segment = em.GetComponentData<WallInstanceParent>(entity).Segment;
                if (!em.Exists(segment)) return;
                if (!em.HasComponent<WallSegmentTag>(segment)) return;

                // Stash the focus instance on the segment so the Phase 5
                // PickGateRegionInstances helper lands on the clicked instance
                // — IssueConvertSegmentToGate also re-applies this, but pre-
                // setting matches the bridge-topic dispatch path (HudBridge
                // sets the focus pointer before routing through the helper).
                if (em.HasComponent<WallSegmentFocus>(segment))
                {
                    em.SetComponentData(segment, new WallSegmentFocus { Instance = entity });
                }
                else
                {
                    em.AddComponentData(segment, new WallSegmentFocus { Instance = entity });
                }

                CommandRouter.IssueConvertSegmentToGate(em, segment, entity);
                return;
            }

            if (data.Id == "WallInstanceToTower")
            {
                // Tower stays per-instance (Phase 5 retained the legacy
                // WallUpgradeSystem Loop 1 path for backward compatibility
                // and the IMGUI reference panel). Spend + state-add the
                // same way IMGUI did.
                if (em.HasComponent<WallTowerTag>(entity)) return; // already upgraded
                if (em.HasComponent<WallUpgradeState>(entity)) return; // already in progress

                Faction faction = GameSettings.LocalPlayerFaction;
                if (em.HasComponent<FactionTag>(entity))
                    faction = em.GetComponentData<FactionTag>(entity).Value;

                if (!TheWaningBorder.Data.BuildCosts.TryGet("Alanthor_WallTower", out var cost))
                    return;
                if (!FactionEconomy.Spend(em, faction, cost))
                    return;

                em.AddComponentData(entity, new WallUpgradeState
                {
                    UpgradeType = 1,
                    Duration = 10f,
                    Remaining = 10f,
                });
                return;
            }
        }

        // Mirrors the gating in EntityActionPanel.cs:328-358 — queue cap, pop
        // capacity, cost deduction — before the actual CommandRouter dispatch.
        // Without these, the player can over-queue, over-population, and never
        // pay for queued units.
        private static void TryIssueTrain(EntityManager em, Entity entity, ActionButton data)
        {
            if (!em.Exists(entity)) return;

            Faction faction = GameSettings.LocalPlayerFaction;
            if (em.HasComponent<FactionTag>(entity))
                faction = em.GetComponentData<FactionTag>(entity).Value;

            // Queue cap (max 5 items, including the currently-training unit).
            if (em.HasBuffer<TrainQueueItem>(entity))
            {
                var q = em.GetBuffer<TrainQueueItem>(entity);
                if (q.Length >= MaxTrainQueue)
                {
                    PlayerNotificationSystem.Notify("Training queue full");
                    return;
                }
            }

            // Population cap.
            int popCost = PopulationHelper.GetUnitPopulationCost(data.Id);
            if (!PopulationHelper.HasPopulationCapacity(faction, popCost))
            {
                PlayerNotificationSystem.Notify("Population cap reached");
                return;
            }

            // Resource cost (War Lv I military discount, same path as IMGUI).
            var trainCost = WarSectCostHelper.MilitaryDiscount(em, faction, data.Id, data.Cost);
            if (!FactionEconomy.Spend(em, faction, trainCost))
            {
                PlayerNotificationSystem.NotifyError("Not enough resources");
                return;
            }

            CommandRouter.IssueTrain(em, entity, data.Id);
        }

        // ─── Button factory ───────────────────────────────────────────────
        private static VisualElement MakeActionButton()
        {
            var v = new VisualElement();
            v.AddToClassList("tw-action-btn");
            v.pickingMode = PickingMode.Position;

            var label = new Label();
            label.AddToClassList("tw-action-btn-label");
            label.pickingMode = PickingMode.Ignore;
            v.Add(label);

            // Click handler registered exactly once per button lifetime.
            v.RegisterCallback<ClickEvent>(OnButtonClicked);
            return v;
        }

        // ─── Display helpers ──────────────────────────────────────────────
        private static string TitleFor(ActionType type)
        {
            switch (type)
            {
                case ActionType.BuildingPlacement:        return "BUILD";
                case ActionType.UnitTraining:             return "TRAIN";
                case ActionType.UnitTrainingAndResearch:  return "TRAIN";
                case ActionType.VaultManagement:          return "VAULT";
                case ActionType.TempleUpgrade:            return "TEMPLE";
                case ActionType.WallInstanceUpgrade:      return "UPGRADE";
                case ActionType.BazaarWagonUnpack:        return "WAGON";
                case ActionType.GathererHutAgeUpChoice:   return "CONVERT";
                default:                                  return "ACTIONS";
            }
        }

        private static string HintFor(ActionType type)
        {
            switch (type)
            {
                case ActionType.None: return "No actions available";
                default:              return "No commands for this selection";
            }
        }

        private static string ShortLabel(ActionButton b)
        {
            if (string.IsNullOrEmpty(b.Label)) return b.Id ?? "?";
            var s = b.Label;
            return s.Length > 10 ? s.Substring(0, 9).ToUpperInvariant() : s.ToUpperInvariant();
        }
    }
}
