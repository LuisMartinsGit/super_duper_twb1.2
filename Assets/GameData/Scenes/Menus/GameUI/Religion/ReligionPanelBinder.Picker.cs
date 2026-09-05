// ReligionPanelBinder.Picker.cs
// The adoption picker: which sects are offered, and adopting one.

using System.Collections.Generic;
using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.UI.Ingame;
using TheWaningBorder.UI.World;
using TheWaningBorder.UI.Data;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ReligionPanelBinder
    {
        private void RefreshPicker(EntityManager em, Faction faction, Entity temple, int rp)
        {
            byte culture = LookupCulture(em, faction);
            foreach (var row in _pickerRows)
            {
                if (row.Label == null) continue;

                string name = SectInfo.ShortName(row.SectId);
                string text;
                bool enabled = false;

                if (!SectConfig.IsImplemented(row.SectId))
                {
                    text = string.Format(Loc.T("{0} - coming soon"), name);
                }
                else
                {
                    var check = SectAdoption.CanAdopt(em, faction, row.SectId, out int cost);
                    bool materials = BuildCosts.TryGet(
                            SectConfig.ChapelIdFor(row.SectId), out var chapelCost)
                        && FactionEconomy.CanAfford(em, faction, chapelCost);

                    switch (check)
                    {
                        case SectAdoptionResult.Ok when materials:
                            text = string.Format(Loc.T("{0} - {1} RP"), name, cost);
                            enabled = true;
                            break;
                        case SectAdoptionResult.Ok:
                            text = string.Format(Loc.T("{0} - need materials"), name);
                            break;
                        case SectAdoptionResult.AlreadyAdopted:
                            text = string.Format(Loc.T("{0} - adopted"), name);
                            break;
                        case SectAdoptionResult.NotEnoughRP:
                            text = string.Format(Loc.T("{0} - need {1} RP (have {2})"),
                                name, cost, rp);
                            break;
                        case SectAdoptionResult.SlotsFull:
                            text = string.Format(Loc.T("{0} - no free slot"), name);
                            break;
                        default:
                            text = string.Format(Loc.T("{0} - unavailable"), name);
                            break;
                    }
                }

                if (row.Label.text != text) row.Label.text = text;
                if (row.Button != null && row.Button.interactable != enabled)
                    row.Button.interactable = enabled;
            }
        }

        private void ClickAdopt(string sectId)
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            if (!TryGetTemple(em, faction, out var temple)) { ClosePicker(); return; }
            if (!BuildCosts.TryGet(SectConfig.ChapelIdFor(sectId), out var chapelCost)) return;

            // Validate only — the RP + material SPEND happens inside
            // SectAdoptionCommandDirect on every peer, alongside the slot
            // stamp (docs/Multiplayer_LAN_Readiness.md).
            var result = SectAdoption.ValidateAdoption(em, faction, sectId, chapelCost, temple);
            if (result == SectAdoptionResult.Ok)
            {
                CommandRouter.IssueSectAdoption(em, temple, sectId, _pickerSlotIndex,
                    ChapelBuildSeconds);
                ClosePicker();
                return;
            }

            PlayerNotificationSystem.NotifyError(result switch
            {
                SectAdoptionResult.NotEnoughRP => Loc.T("Not enough Religion Points"),
                SectAdoptionResult.SlotsFull => Loc.T("All chapel slots are in use"),
                SectAdoptionResult.AlreadyAdopted => Loc.T("Sect already adopted"),
                SectAdoptionResult.NotYetImplemented => Loc.T("This sect is coming soon"),
                _ => Loc.T("Cannot adopt this sect"),
            });
        }

        private void ClosePicker()
        {
            _pickerSlotIndex = -1;
            if (_picker != null && _picker.activeSelf) _picker.SetActive(false);
        }
    }
}
