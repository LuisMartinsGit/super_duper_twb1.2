// ActionsPanelPrefabBinder.Execute.cs
// Acting on a click. These issue commands through CommandRouter; the
// grid decides WHICH button was pressed, not what pressing it means.

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
        private void UpgradeClicked(Entity entity)
        {
            var em = EM(out bool ok);
            if (!ok) return;
            BuildingUpgradeAction.Execute(em, entity);
            _timer = RefreshInterval;   // repaint on the next tick
        }

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

        private Faction OwnFaction(EntityManager em)
        {
            if (_entity != Entity.Null && em.Exists(_entity) && em.HasComponent<FactionTag>(_entity))
                return em.GetComponentData<FactionTag>(_entity).Value;
            return GameSettings.LocalPlayerFaction;
        }
    }
}
