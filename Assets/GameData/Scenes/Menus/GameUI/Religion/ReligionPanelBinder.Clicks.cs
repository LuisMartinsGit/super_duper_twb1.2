// ReligionPanelBinder.Clicks.cs
// Click handling for slots and tiers. Casting itself stays in the main
// file, where the two BeginCast overloads live together.

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
        private void ClickTier(int slotIndex, int tier)
        {
            if (slotIndex < 0 || slotIndex >= _slots.Count) return;
            var view = _slots[slotIndex];
            if (view.State != 2 || string.IsNullOrEmpty(view.SectId)) return;
            BeginCast(view.SectId, tier);
        }

        private void ClickSlot(int index)
        {
            if (index < 0 || index >= _slots.Count) return;
            var view = _slots[index];

            if (view.State == 0)
            {
                _pickerSlotIndex = index;
                if (_picker != null)
                {
                    _picker.SetActive(true);
                    var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
                    if (world != null && world.IsCreated
                        && TryGetTemple(world.EntityManager, GameSettings.LocalPlayerFaction, out var temple))
                        RefreshPicker(world.EntityManager, GameSettings.LocalPlayerFaction, temple,
                            FactionReligionPointsHelper.GetBalance(world.EntityManager, GameSettings.LocalPlayerFaction));
                }
                return;
            }

            if (view.State == 2 && !string.IsNullOrEmpty(view.SectId))
                BeginCast(view.SectId);
        }

        private void RightClickSlot(int index)
        {
            if (index < 0 || index >= _slots.Count) return;
            var view = _slots[index];
            if (view.State != 2 || string.IsNullOrEmpty(view.SectId)) return;

            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            var faction = GameSettings.LocalPlayerFaction;

            // MP: allocation must replicate — it halves the sect's power
            // cooldown, and a local-only toggle makes a later SectPower fire
            // on one peer and drop on the other. SP keeps the direct call for
            // the instant error message.
            bool has = SectActivePowerHelper.HasShardrootAllocated(em, faction, view.SectId);
            if (GameSettings.IsMultiplayer)
            {
                TheWaningBorder.Core.Commands.CommandRouter.IssueSectShardrootAlloc(
                    em, faction, view.SectId, allocate: !has);
            }
            else if (has)
                SectActivePowerHelper.DeallocateShardroot(em, faction, view.SectId);
            else if (!SectActivePowerHelper.AllocateShardroot(em, faction, view.SectId))
                PlayerNotificationSystem.NotifyError(Loc.T("No Glow stored in the Temple"));
        }
    }
}
