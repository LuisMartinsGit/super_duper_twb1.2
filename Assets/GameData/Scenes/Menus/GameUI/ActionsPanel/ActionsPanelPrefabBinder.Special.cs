// ActionsPanelPrefabBinder.Special.cs
// ONE ACTIONS PANEL FOR EVERY BUILDING (2026-11-10, developer: "lots of
// buildings do not use the building actions UI but use their own UI. This
// cannot be."). The selections the retired code-built ActionsPanelBinder used
// to draw — wall pieces and hubs (gate / tower / emplacement / hub
// conversions, Build Wall, the wall level and its lock), gates (open /
// close), manned towers and wall sections (let the men out), the Alanthor
// hut's age-up choice, the Bazaar wagon and the Vault of Almiérra — are
// laid out on this grid like any other building: their own actions first,
// as ordinary cells, then the building's research rows, then its level-up
// in the last free cell. The Vault adds its four bars above the grid
// (ActionsPanelPrefabBinder.Vault.cs).
//
// What each action DOES did not change: every click still routes through
// CommandRouter exactly as the old panel issued it.

using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ActionsPanelPrefabBinder
    {
        /// <summary>Selections whose actions come from the extractor's special
        /// branches rather than a roster.</summary>
        private static bool IsSpecialType(ActionType t) =>
            t == ActionType.WallInstanceUpgrade
            || t == ActionType.HubBuildWall
            || t == ActionType.GathererHutAgeUpChoice
            || t == ActionType.BazaarWagonUnpack
            || t == ActionType.VaultManagement;

        /// <summary>
        /// The special selection on the standard grid: its own cells from slot
        /// 0, research rows on the next whole row (never on wall pieces —
        /// their one research, the wall level, is already one of their own
        /// cells), the level-up in the last free cell. Returns cells used.
        /// </summary>
        private int RenderSpecial(EntityManager em, in EntityActionInfo info)
        {
            for (int i = 0; i < SlotCount && i < _slots.Length; i++) ClearSlot(_slots[i]);

            int cell = 0, used = 0;
            Entity entity = _entity;

            if (info.Type == ActionType.VaultManagement)
            {
                cell = RenderVaultControls(em);
                used += cell;
                _vaultWanted = true;
            }
            else if (info.Type == ActionType.GathererHutAgeUpChoice
                     && em.HasComponent<GathererHutConverting>(entity)
                     && (info.Actions == null || info.Actions.Count == 0))
            {
                // Mid-conversion: one locked cell that sweeps with the timer.
                var conv = em.GetComponentData<GathererHutConverting>(entity);
                var b = new ActionButton
                {
                    Id = "HutConverting",
                    Label = Loc.T("Converting..."),
                    Tooltip = Loc.T("Converting..."),
                    Enabled = false,
                    CanAfford = true,
                };
                var slot = _slots[cell++];
                FillSlot(slot, b, Category.Defense, null, null, em);
                if (slot.CooldownFill != null && conv.Total > 0f)
                    slot.CooldownFill.fillAmount = Mathf.Clamp01(conv.Remaining / conv.Total);
                used++;
            }
            else if (info.Actions != null)
            {
                for (int i = 0; i < info.Actions.Count && cell < SlotCount; i++)
                {
                    var b = info.Actions[i];
                    var slot = _slots[cell++];
                    FillSlot(slot, b, SpecialCategory(b.Id), null,
                        b.Enabled ? (System.Action)(() => Execute(entity, b, isTrain: false)) : null, em);
                    used++;

                    // The wall level runs on whichever hub took it; the
                    // extractor hands its progress over as ProductionState.
                    if (b.Id == "WallLevelResearching" && slot.CooldownFill != null
                        && info.ProductionState.HasValue && info.ProductionState.Value.IsBusy)
                        slot.CooldownFill.fillAmount = 1f - Mathf.Clamp01(info.ProductionState.Value.Progress);
                }
                if (info.Actions.Count > SlotCount)
                    TWBLog.Log($"[GameUI] ActionsPanel: {info.Actions.Count} special actions, " +
                        $"only {SlotCount} slots — overflow dropped.");
            }

            // Research on the next whole row (row 1 at the earliest, so a
            // building with one or two actions keeps the usual layout).
            bool wallPiece = em.HasComponent<WallTag>(entity);
            if (!wallPiece && info.Type != ActionType.BazaarWagonUnpack)
            {
                int firstRow = Mathf.Max(1, (cell + Cols - 1) / Cols);
                // The grid skips inactive cells, so the rest of the row is
                // held with blanks or the research would slide up beside the
                // building's own cells.
                for (int i = cell; i < firstRow * Cols && i < SlotCount; i++) BlankSlot(_slots[i]);
                int research = RenderResearchRows(em, firstRow * Cols);
                // Nothing to research: no gap to hold.
                if (research == 0)
                    for (int i = cell; i < firstRow * Cols && i < SlotCount; i++) ClearSlot(_slots[i]);
                used += research;
            }

            used += RenderUpgradeSlot(em);
            RenderProgress(em, info);
            return used;
        }

        /// <summary>Cell tint for a special action, by the category scheme
        /// (blue defenses, brown research, red military, green economy).</summary>
        private static Category SpecialCategory(string id)
        {
            switch (id)
            {
                case "BazaarPack":
                case "BazaarUnpack":
                    return Category.Economy;
                case "WallUngarrison":
                    return Category.Military;
                case "WallLevelResearching":
                case "CancelWallLevel":
                case TheWaningBorder.Entities.WallTiers.BattlementsTechId:
                case TheWaningBorder.Entities.WallTiers.ShieldedTechId:
                    return Category.Research;
            }
            return Category.Defense;
        }

        /// <summary>
        /// The special actions' clicks, moved verbatim from the retired
        /// ActionsPanelBinder. Returns false for an id that is not one of
        /// them (the caller then treats it as a train or research id — the
        /// wall level button carries its tech id and IS a research).
        /// </summary>
        private bool ExecuteSpecial(EntityManager em, Entity entity, in ActionButton b)
        {
            switch (b.Id)
            {
                case "ConvertToWallHub":
                    CommandRouter.IssueConvertHut(em, entity, HutConversionTarget.WallHub);
                    return true;
                case "ConvertToWatchTower":
                    CommandRouter.IssueConvertHut(em, entity, HutConversionTarget.WatchTower);
                    return true;
                case "WallSegmentToGate":
                {
                    Entity segment = em.HasComponent<WallInstanceParent>(entity)
                        ? em.GetComponentData<WallInstanceParent>(entity).Segment
                        : Entity.Null;
                    if (segment != Entity.Null)
                        CommandRouter.IssueConvertSegmentToGate(em, segment, entity);
                    return true;
                }
                case "WallInstanceToTower":
                    // Affordability CHECK only — the SPEND lives in the charged
                    // executor (WallUpgradeChargedDirect) so single-player and
                    // every lockstep peer debit the same bank at the same tick
                    // (docs/Multiplayer_LAN_Readiness.md).
                    if (CheckAfford(em, b.Cost))
                        CommandRouter.IssueWallUpgradeCharged(em, entity, 1, 10f);
                    return true;
                case "WallToBallista":
                case "WallToTrebuchet":
                    // 4 = ballista, 5 = trebuchet; the executor re-checks the
                    // clear-run rule and does the spend.
                    if (CheckAfford(em, b.Cost))
                        CommandRouter.IssueWallUpgradeCharged(em, entity,
                            b.Id == "WallToTrebuchet" ? 5 : 4, 12f);
                    return true;
                case "WallInstanceToHub":
                    // Type 3 is charged a hub's price in the executor; on
                    // completion the cell becomes a hub, its segment split there.
                    if (CheckAfford(em, b.Cost))
                        CommandRouter.IssueWallUpgradeCharged(em, entity, 3, 10f);
                    return true;
                case "BuildWall":
                    WorkerCommandPanel.TriggerHubBuildWall(entity);
                    return true;
                case "CancelWallLevel":
                    // The wall level runs on whichever hub took it, not
                    // necessarily the selected piece. Cancel through the
                    // router so the refund lands on every peer; the wall lock
                    // lifts on the next refresh because nothing is queued.
                    // docs/Design/Age_1_Alanthor.md § The four wall levels
                    if (TheWaningBorder.Entities.WallTiers.TryGetLevelResearch(
                            em, OwnFaction(em), out var host, out int slot, out _))
                        CommandRouter.IssueCancelProduction(em, host, slot);
                    _timer = RefreshInterval;
                    return true;
                case "GateClose":
                    CommandRouter.IssueSetGateLock(em, entity, true);
                    return true;
                case "GateOpen":
                    CommandRouter.IssueSetGateLock(em, entity, false);
                    return true;
                case "ReplaceEquipment":
                    // Affordability CHECK only — CommandRouter.ReplaceEquipmentDirect
                    // re-validates and spends on every peer.
                    if (CheckAfford(em, b.Cost))
                        CommandRouter.IssueReplaceEquipment(em, entity);
                    return true;
            }
            return false;
        }

        /// <summary>The click-side affordability check every charged order
        /// makes before it is issued (the executor spends).</summary>
        private bool CheckAfford(EntityManager em, in Cost cost)
        {
            var faction = OwnFaction(em);
            if (FactionEconomy.CanAfford(em, faction, cost)) return true;
            PlayerNotificationSystem.NotifyError(TheWaningBorder.UI.Common.UIHelpers.CantAfford(em, faction, cost));
            return false;
        }

        /// <summary>The first selected entity the local player owns — the
        /// one the panel describes. (Moved here from the retired code-built
        /// ActionsPanelBinder.)</summary>
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
    }
}
