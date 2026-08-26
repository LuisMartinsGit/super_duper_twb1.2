// BuildingUpgradeAction.cs
// "Upgrade to Lv N" as a first-class ACTION rather than a floating pill.
//
// It used to be a code-built button hung off the right edge of the selection
// header — a widget in nobody's panel, in nobody's grid, with no tooltip and no
// cost readout. This turns it into the same shape every other action has
// (label / cost / tooltip / enabled / progress), so both action panels can
// render it in their own idiom:
//   - the authored 3x5 grid puts it in its last slot, tinted like a research
//     action, with the upgrade timer sweeping the radial cooldown fill;
//   - the code-built panel renders it as one of its wide rows.
// The header pill is gone.
//
// Everything the button decides — age gate, culture gate, cost, next level —
// comes from UpgradeBuildingCommandHelper, so this file adds no rules of its
// own; it only formats them.

using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Economy;
using TheWaningBorder.UI.Common;
using TheWaningBorder.UI.HUD;
using TheWaningBorder.UI.Panels;

namespace TheWaningBorder.UI.GameUI
{
    internal struct BuildingUpgradeInfo
    {
        public bool Show;
        public bool Enabled;
        /// <summary>0..1 while an upgrade is running; -1 when none is.</summary>
        public float Progress;
        public string Label;
        public string Tooltip;
        public Cost Cost;
    }

    internal static class BuildingUpgradeAction
    {
        /// <summary>
        /// Describe the upgrade action for one selected building. Show=false
        /// for anything that is not an owned, finished, upgradeable building of
        /// the local faction, or for a faction that has not aged up yet
        /// (upgrades are culture-gated).
        /// </summary>
        public static BuildingUpgradeInfo Describe(EntityManager em, Entity building)
        {
            var info = new BuildingUpgradeInfo { Progress = -1f };

            if (building == Entity.Null || !em.Exists(building)) return info;
            if (!em.HasComponent<BuildingUpgradeable>(building)) return info;
            if (em.HasComponent<UnderConstruction>(building)) return info;
            if (!em.HasComponent<FactionTag>(building)) return info;

            var faction = em.GetComponentData<FactionTag>(building).Value;
            if (faction != GameSettings.LocalPlayerFaction) return info;

            if (em.HasComponent<BuildingUpgrading>(building))
            {
                var up = em.GetComponentData<BuildingUpgrading>(building);
                float pct = up.Total > 0f ? Mathf.Clamp01(up.Progress / up.Total) : 0f;
                info.Show = true;
                info.Progress = pct;
                info.Label = string.Format(Loc.T("Upgrading\n{0}%"), (int)(pct * 100f));
                info.Tooltip = "<b>" + Loc.T("Upgrade in progress") + "</b>\n"
                    + string.Format(Loc.T("{0}% complete"), (int)(pct * 100f));
                return info;
            }

            if (BuildingActionLayouts.FactionAge(em, faction) < 1)
                return info;   // pre-culture: nothing to upgrade into yet
            if (!UpgradeBuildingCommandHelper.TryGetNextCost(em, building,
                    out var cost, out byte nextLevel))
                return info;   // already max level

            bool canAfford = FactionEconomy.CanAfford(em, faction, cost);
            var available = EntityActionExtractor.GetFactionResourcesAsCostPublic(em, faction);

            info.Show = true;
            info.Enabled = canAfford;
            info.Cost = cost;
            info.Label = string.Format(Loc.T("Upgrade\nLv {0}"), nextLevel);
            // "\n" + Loc.T("Cost: ") is the marker the actions panels re-split
            // on (ExpandTooltip) — composer and splitter must always agree.
            info.Tooltip = "<b>" + string.Format(Loc.T("Upgrade to Level {0}"), nextLevel)
                + "</b>\n"
                + Loc.T("Raises this building's stats and unlocks its next tier of units and research.")
                + "\n" + Loc.T("Cost: ") + UIHelpers.FormatCostRich(cost, available)
                + (canAfford ? ""
                    : "\n" + Loc.T("<color=#C08040>Not enough resources.</color>"));
            return info;
        }

        /// <summary>Route the upgrade and surface the refusal reason.</summary>
        public static void Execute(EntityManager em, Entity building)
        {
            if (building == Entity.Null || !em.Exists(building)) return;

            switch (UpgradeBuildingCommandHelper.Execute(em, building))
            {
                case UpgradeBuildingResult.CannotAfford:
                    PlayerNotificationSystem.NotifyError(
                        Loc.T("Not enough resources to upgrade"));
                    break;
                case UpgradeBuildingResult.NoCulture:
                    PlayerNotificationSystem.NotifyError(
                        Loc.T("Choose a culture before upgrading buildings"));
                    break;
                case UpgradeBuildingResult.AlreadyMaxLevel:
                    PlayerNotificationSystem.NotifyError(
                        Loc.T("Building is already at max level"));
                    break;
            }
        }
    }
}
