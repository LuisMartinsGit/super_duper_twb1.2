// SectAdoption.cs
// Static helper for adopting sects (= building chapels in Temple slots) and
// upgrading individual levers (Passive / Building / Unit / Active Power).
//
// Adoption flow (per task-063 design):
//   1. Player builds a chapel (Chapel_Sect_X) inside a Temple slot.
//   2. BuildingConstructionSystem completes the chapel, calls OnChapelCompleted.
//   3. OnChapelCompleted deducts adoption cost, sets PerSectState.AdoptedAtAge,
//      grants Lv I on every lever, fires SectAdopted event for broadcast.
//
// Upgrade flow:
//   1. Player clicks an upgrade button at a chapel (UI panel — Phase 1 stub).
//   2. UI calls TryUpgradeLever, which validates affordability + age-gating
//      + current-level constraints, then deducts cost and bumps lever.
//   3. Upgrade is *private* — no broadcast event. Visual differentiation only.
//
// Phase 1 (task-063): adoption + upgrade primitives. Effect dispatch (Phase 2)
// reads PerSectState to decide whether to apply each sect's bonuses.
//
// Location: Assets/Scripts/Economy/SectAdoption.cs

using System;
using Unity.Entities;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Why a Try* call failed. Returned via <c>out reason</c> so the UI can
    /// surface a meaningful tooltip.
    /// </summary>
    public enum SectAdoptionResult : byte
    {
        Ok                       = 0,
        UnknownSect              = 1,
        NotEnoughRP              = 2,
        AlreadyAdopted           = 3,
        SlotsFull                = 4,
        TempleMissing            = 5,
        NotAdopted               = 6,
        AlreadyMaxed             = 7,
        AgeGatingNotMet          = 8,
        UnsupportedLeverState    = 9,
        BankMissing              = 10,
    }

    /// <summary>
    /// Static helpers for adoption / upgrade transitions.
    /// </summary>
    public static class SectAdoption
    {
        // ═══════════════════════════════════════════════════════════════════
        // EVENTS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fired when a faction adopts a sect (chapel completed). Public —
        /// other players see this. Subscribers: PlayerNotificationSystem,
        /// minimap pings, sect-tracker UI.
        /// </summary>
        public static event Action<Faction, string /*sectId*/, int /*age*/> OnSectAdopted;

        // ═══════════════════════════════════════════════════════════════════
        // QUERIES (UI gating helpers — non-mutating)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Can <paramref name="faction"/> adopt <paramref name="sectId"/> right now?
        /// Sets <paramref name="cost"/> on success.
        /// </summary>
        public static SectAdoptionResult CanAdopt(EntityManager em, Faction faction, string sectId, out int cost)
        {
            cost = 0;
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return SectAdoptionResult.UnknownSect;

            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return SectAdoptionResult.BankMissing;
            if (!em.HasComponent<SectAdoptionState>(bank)) return SectAdoptionResult.BankMissing;

            var state = em.GetComponentData<SectAdoptionState>(bank);
            if (state.Get(idx).IsAdopted) return SectAdoptionResult.AlreadyAdopted;
            if (state.AdoptedCount() >= SectConfig.MaxAdoptedSects) return SectAdoptionResult.SlotsFull;

            byte culture = LookupCulture(em, faction);
            cost = SectConfig.AdoptionCost(sectId, culture);
            if (cost < 0) return SectAdoptionResult.UnknownSect;

            if (!FactionReligionPointsHelper.CanAfford(em, faction, cost))
                return SectAdoptionResult.NotEnoughRP;

            return SectAdoptionResult.Ok;
        }

        /// <summary>
        /// Can <paramref name="faction"/> upgrade the given lever on <paramref name="sectId"/>?
        /// Sets <paramref name="cost"/> on success.
        /// </summary>
        public static SectAdoptionResult CanUpgradeLever(
            EntityManager em, Faction faction, string sectId, SectLeverKind lever, out int cost)
        {
            cost = 0;
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return SectAdoptionResult.UnknownSect;

            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return SectAdoptionResult.BankMissing;
            if (!em.HasComponent<SectAdoptionState>(bank)) return SectAdoptionResult.BankMissing;
            if (!em.HasComponent<FactionReligionPoints>(bank)) return SectAdoptionResult.BankMissing;

            var state = em.GetComponentData<SectAdoptionState>(bank);
            var rp    = em.GetComponentData<FactionReligionPoints>(bank);
            var sect  = state.Get(idx);

            if (!sect.IsAdopted) return SectAdoptionResult.NotAdopted;

            byte level = sect.LevelOf(lever);
            if (level == 3) return SectAdoptionResult.AlreadyMaxed;
            if (level == 0)
                return SectAdoptionResult.UnsupportedLeverState; // Adoption sets 1 on every lever; should never happen.

            // Age-gating:
            //  - Lv I → Lv II requires sect to have been adopted in a previous age.
            //  - Lv II → Lv III requires THIS lever to have been at Lv II in a previous age.
            byte currentAge = rp.CurrentAge == 0 ? (byte)1 : rp.CurrentAge;
            if (level == 1)
            {
                if (sect.AdoptedAtAge >= currentAge) return SectAdoptionResult.AgeGatingNotMet;
            }
            else // level == 2
            {
                byte achieved = sect.LevelAchievedAtAgeOf(lever);
                if (achieved >= currentAge) return SectAdoptionResult.AgeGatingNotMet;
            }

            cost = SectConfig.UpgradeCost(level);
            if (cost < 0) return SectAdoptionResult.UnsupportedLeverState;

            if (!FactionReligionPointsHelper.CanAfford(em, faction, cost))
                return SectAdoptionResult.NotEnoughRP;

            return SectAdoptionResult.Ok;
        }

        // ═══════════════════════════════════════════════════════════════════
        // MUTATIONS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Try to start a chapel build for <paramref name="sectId"/>: validates
        /// the adoption is legal, then atomically deducts both the RP cost
        /// and the chapel material cost. The caller is responsible for actually
        /// queuing the slot once this returns Ok.
        ///
        /// This is the canonical click-time entry point for the Religion HUD.
        /// Adopting at click time (instead of at chapel completion) matches the
        /// player's expectation that resources drop immediately, and prevents
        /// the duplicate-build race the old chapel-completion path allowed.
        ///
        /// Refuses if any temple slot is currently building this same sect
        /// (reads <paramref name="temple"/> 's slot buffer if provided).
        /// </summary>
        public static SectAdoptionResult TryStartAdoption(
            EntityManager em, Faction faction, string sectId,
            in TheWaningBorder.Core.Cost chapelCost, Entity temple)
        {
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return SectAdoptionResult.UnknownSect;

            // Standard validation (RP afford / not already adopted / slot count).
            var check = CanAdopt(em, faction, sectId, out int rpCost);
            if (check != SectAdoptionResult.Ok) return check;

            // In-flight build guard — refuse if any temple slot is currently
            // building this same sect.
            if (em.Exists(temple) && em.HasBuffer<TempleChapelSlot>(temple))
            {
                var slots = em.GetBuffer<TempleChapelSlot>(temple);
                for (int i = 0; i < slots.Length; i++)
                {
                    if (slots[i].State == 1 && slots[i].SectId == sectId)
                        return SectAdoptionResult.AlreadyAdopted;
                }
            }

            // Material check before any spend, so an RP-only failure doesn't
            // leave the player with materials gone but adoption refused.
            if (!FactionEconomy.CanAfford(em, faction, chapelCost))
                return SectAdoptionResult.NotEnoughRP; // re-using enum for "can't pay"

            // Atomic spend: RP first, then materials. If the material spend
            // somehow fails (race), refund the RP.
            if (!FactionReligionPointsHelper.TrySpend(em, faction, rpCost))
                return SectAdoptionResult.NotEnoughRP;
            if (!FactionEconomy.Spend(em, faction, chapelCost))
            {
                FactionReligionPointsHelper.Refund(em, faction, rpCost);
                return SectAdoptionResult.NotEnoughRP;
            }

            return SectAdoptionResult.Ok;
        }

        /// <summary>
        /// Called by TempleChapelBuildSystem when a chapel building completes.
        /// At this point RP + materials were already deducted by
        /// <see cref="TryStartAdoption"/>; this method only stamps the
        /// adoption state and fires the public event. Idempotent — re-entry
        /// for the same sect is a no-op (returns AlreadyAdopted).
        /// </summary>
        public static SectAdoptionResult OnChapelCompleted(EntityManager em, Faction faction, string chapelBuildingId)
        {
            string sectId = SectConfig.SectIdFromChapelId(chapelBuildingId);
            if (sectId == null) return SectAdoptionResult.UnknownSect;
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return SectAdoptionResult.UnknownSect;

            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return SectAdoptionResult.BankMissing;
            if (!em.HasComponent<SectAdoptionState>(bank)) return SectAdoptionResult.BankMissing;

            var state = em.GetComponentData<SectAdoptionState>(bank);
            if (state.Get(idx).IsAdopted) return SectAdoptionResult.AlreadyAdopted;

            byte currentAge = em.HasComponent<FactionReligionPoints>(bank)
                ? em.GetComponentData<FactionReligionPoints>(bank).CurrentAge : (byte)1;
            if (currentAge == 0) currentAge = 1;

            var sect = new PerSectState
            {
                AdoptedAtAge = currentAge,
                PassiveLevel = 1,
                BuildingLevel = 1,
                UnitLevel = 1,
                ActivePowerLevel = 1,
                PassiveLevelAchievedAtAge = currentAge,
                BuildingLevelAchievedAtAge = currentAge,
                UnitLevelAchievedAtAge = currentAge,
                ActivePowerLevelAchievedAtAge = currentAge,
            };
            state.Set(idx, sect);
            em.SetComponentData(bank, state);

            OnSectAdopted?.Invoke(faction, sectId, currentAge);
            return SectAdoptionResult.Ok;
        }

        /// <summary>
        /// Try to upgrade a single lever by 1. Same validation as CanUpgradeLever;
        /// on success deducts RP and bumps the lever level + stamps current age.
        /// </summary>
        public static SectAdoptionResult TryUpgradeLever(
            EntityManager em, Faction faction, string sectId, SectLeverKind lever)
        {
            var check = CanUpgradeLever(em, faction, sectId, lever, out int cost);
            if (check != SectAdoptionResult.Ok) return check;

            if (!FactionReligionPointsHelper.TrySpend(em, faction, cost))
                return SectAdoptionResult.NotEnoughRP;

            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return SectAdoptionResult.BankMissing;

            int idx = SectConfig.IndexOf(sectId);
            byte currentAge = em.GetComponentData<FactionReligionPoints>(bank).CurrentAge;
            if (currentAge == 0) currentAge = 1;

            var state = em.GetComponentData<SectAdoptionState>(bank);
            var sect  = state.Get(idx);
            byte newLevel = (byte)(sect.LevelOf(lever) + 1);
            sect.SetLevel(lever, newLevel, currentAge);
            state.Set(idx, sect);
            em.SetComponentData(bank, state);

            return SectAdoptionResult.Ok;
        }

        // ═══════════════════════════════════════════════════════════════════
        // INTERNAL
        // ═══════════════════════════════════════════════════════════════════

        private static byte LookupCulture(EntityManager em, Faction faction)
        {
            // Find the faction's Hall (or any building carrying FactionProgress).
            // Pre-age-up the culture is None; SectConfig.AdoptionCost handles
            // that as a same-cluster default — irrelevant in practice since
            // Temple isn't buildable until Age 2 and culture is chosen at age-up.
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            using var progress = query.ToComponentDataArray<FactionProgress>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction) return progress[i].Culture;
            }
            return Cultures.None;
        }
    }
}
