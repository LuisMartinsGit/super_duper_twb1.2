// ActionsPanelPrefabBinder.Render.cs
// What the 3x5 grid shows for the current selection: builder palette,
// unit formations, building actions, the upgrade slot and research rows.

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
    public sealed partial class ActionsPanelPrefabBinder
    {
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

            int current = (int)TheWaningBorder.Input.FormationInput.CurrentShape;
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
                    TheWaningBorder.Input.FormationInput.RequestShape(shape);
                    _timer = RefreshInterval;   // repaint the highlight now
                }, em);

                if (i == current)
                    foreach (var img in _slots[i].TintBg) img.color = GameUIKit.BarGold;
            }
            for (int i = count; i < _slots.Length; i++) ClearSlot(_slots[i]);

            _queue.Hide();   // units have no production queue
            return count;
        }

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
    }
}
