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
        /// Called by BuildingConstructionSystem when a chapel building completes.
        /// Performs the full adoption transaction: validates, deducts RP, sets
        /// per-sect state, fires SectAdopted event.
        ///
        /// Returns Ok on success. On failure no state changes (caller should
        /// not have allowed the chapel to be built — UI gating mirrors this).
        /// </summary>
        public static SectAdoptionResult OnChapelCompleted(EntityManager em, Faction faction, string chapelBuildingId)
        {
            string sectId = SectConfig.SectIdFromChapelId(chapelBuildingId);
            if (sectId == null) return SectAdoptionResult.UnknownSect;

            var check = CanAdopt(em, faction, sectId, out int cost);
            if (check != SectAdoptionResult.Ok) return check;

            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return SectAdoptionResult.BankMissing;

            // Deduct RP first — TrySpend is atomic and re-validates affordability.
            if (!FactionReligionPointsHelper.TrySpend(em, faction, cost))
                return SectAdoptionResult.NotEnoughRP;

            int idx = SectConfig.IndexOf(sectId);
            byte currentAge = em.GetComponentData<FactionReligionPoints>(bank).CurrentAge;
            if (currentAge == 0) currentAge = 1; // Defensive — should be set on faction init.

            var state = em.GetComponentData<SectAdoptionState>(bank);
            var sect  = new PerSectState
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
