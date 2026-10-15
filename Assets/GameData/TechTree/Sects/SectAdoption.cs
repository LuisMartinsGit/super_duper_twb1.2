// SectAdoption.cs
// Static helper for adopting sects (docs/Design/Religion.md §3, 2026-10-15).
//
// Adoption is INSTANT and FINAL: the SectAdopt command, run on every peer,
// re-validates (CanAdopt: known sect, free slot, standing Temple, RP), spends
// the RP and stamps PerSectState through ApplyAdoption. The chapel on the
// Temple is decoration; TempleChapelBuildSystem seats it from the bank state.
//
// The lever-upgrade code below is retired (levels are bought through
// ReligionPurchases) and kept only until its last callers are gone.

using System;
using TheWaningBorder.Core;
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
        NotYetImplemented        = 11,
    }

    /// <summary>
    /// Static helpers for adoption / upgrade transitions.
    /// </summary>
    public static class SectAdoption
    {
        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and this one was never disposed. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_HallTagFactionTagFactionProgress =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionProgress>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagFactionProgress;

        #endregion
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

            // Rollout gate — unimplemented sects can't be adopted by anyone
            // (players or AI); every adoption path funnels through here.
            if (!SectConfig.IsImplemented(sectId)) return SectAdoptionResult.NotYetImplemented;
            // Not in this edition (Alpha_Build.md §4): treated as not implemented.
            if (!SectConfig.IsInEdition(sectId)) return SectAdoptionResult.NotYetImplemented;

            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return SectAdoptionResult.BankMissing;
            if (!em.HasComponent<SectAdoptionState>(bank)) return SectAdoptionResult.BankMissing;

            var state = em.GetComponentData<SectAdoptionState>(bank);
            if (state.Get(idx).IsAdopted) return SectAdoptionResult.AlreadyAdopted;
            if (state.AdoptedCount() >= SectConfig.MaxAdoptedSects) return SectAdoptionResult.SlotsFull;

            // No standing Temple, no adoption (Religion.md §2).
            if (!SectQuery.HasStandingTemple(em, faction)) return SectAdoptionResult.TempleMissing;

            byte culture = LookupCulture(em, faction);
            cost = SectConfig.AdoptionCost(sectId, culture);
            if (cost < 0) return SectAdoptionResult.UnknownSect;

            if (!FactionReligionPointsHelper.CanAfford(em, faction, cost))
                return SectAdoptionResult.NotEnoughRP;

            return SectAdoptionResult.Ok;
        }

        /// <summary>
        /// Can <paramref name="faction"/> upgrade the given lever on <paramref name="sectId"/>?
        /// Sets <paramref name="cost"/> (RP) and <paramref name="materialCost"/> on success.
        /// </summary>
        public static SectAdoptionResult CanUpgradeLever(
            EntityManager em, Faction faction, string sectId, SectLeverKind lever,
            out int cost, out TheWaningBorder.Core.Cost materialCost)
        {
            cost = 0;
            materialCost = default;

            // Auto-leveling (design 2026-07-05): levers rise with the Temple's
            // level — manual RP lever buys are retired. Kept compiling so the
            // age-gating logic below can be revived if the design reverts.
            return SectAdoptionResult.UnsupportedLeverState;

#pragma warning disable CS0162 // unreachable — intentional, see above
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
            materialCost = SectConfig.UpgradeMaterialCost(level);

            if (!FactionReligionPointsHelper.CanAfford(em, faction, cost))
                return SectAdoptionResult.NotEnoughRP;
            if (!FactionEconomy.CanAfford(em, faction, materialCost))
                return SectAdoptionResult.NotEnoughRP; // re-using enum for "can't pay"

            return SectAdoptionResult.Ok;
#pragma warning restore CS0162
        }

        /// <summary>
        /// Back-compat overload used by callers that only care about the RP cost.
        /// Forwards to the multi-resource version and discards the material cost.
        /// </summary>
        public static SectAdoptionResult CanUpgradeLever(
            EntityManager em, Faction faction, string sectId, SectLeverKind lever, out int cost)
            => CanUpgradeLever(em, faction, sectId, lever, out cost, out _);

        // ═══════════════════════════════════════════════════════════════════
        // MUTATIONS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Click-time check for the religion menu and the AI: may
        /// <paramref name="faction"/> adopt <paramref name="sectId"/> now?
        /// Nothing is spent here — the SectAdopt command spends on every peer
        /// (docs/Multiplayer_LAN_Readiness.md).
        /// </summary>
        public static SectAdoptionResult ValidateAdoption(EntityManager em, Faction faction, string sectId)
            => CanAdopt(em, faction, sectId, out _);

        /// <summary>
        /// EXECUTOR side of an adoption: re-validates through
        /// <see cref="CanAdopt"/>, spends the adoption RP (adoption has no
        /// resource price, Religion.md §5) and applies the adoption. Run by
        /// CommandRouter.SectAdoptionCommandDirect on every peer, so the RP
        /// debit and the state stamp land identically everywhere. Returns
        /// false (spending nothing) when the adoption is not legal.
        /// </summary>
        public static bool TryAdopt(EntityManager em, Faction faction, string sectId)
        {
            if (CanAdopt(em, faction, sectId, out int rpCost) != SectAdoptionResult.Ok) return false;
            if (!FactionReligionPointsHelper.TrySpend(em, faction, rpCost)) return false;
            if (ApplyAdoption(em, faction, sectId) == SectAdoptionResult.Ok) return true;
            FactionReligionPointsHelper.Refund(em, faction, rpCost);
            return false;
        }

        /// <summary>
        /// Stamp the adoption state (no spend — <see cref="TryAdopt"/> pays
        /// first) and fire the public event. Idempotent: a sect already
        /// adopted returns AlreadyAdopted. Also used by scenario setup.
        /// </summary>
        public static SectAdoptionResult ApplyAdoption(EntityManager em, Faction faction, string sectId)
        {
            int idx = SectConfig.IndexOf(sectId);
            if (idx < 0) return SectAdoptionResult.UnknownSect;

            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return SectAdoptionResult.BankMissing;
            if (!em.HasComponent<SectAdoptionState>(bank)) return SectAdoptionResult.BankMissing;

            var state = em.GetComponentData<SectAdoptionState>(bank);
            if (state.Get(idx).IsAdopted) return SectAdoptionResult.AlreadyAdopted;

            byte currentAge = em.HasComponent<FactionReligionPoints>(bank)
                ? em.GetComponentData<FactionReligionPoints>(bank).CurrentAge : (byte)1;
            if (currentAge == 0) currentAge = 1;

            // Every lever starts at Lv I. The Temple has no levels
            // (docs/Design/Religion.md); higher levels are bought with RP.
            byte startLevel = 1;

            var sect = new PerSectState
            {
                AdoptedAtAge = currentAge,
                // A new sect is level I with power A unlocked
                // (docs/Design/Religion.md §3). B, the wildcard and levels
                // II / III are bought with RP, B and the wildcard in either order.
                PowerLevel = 1,
                UnlockMask = 1,
                PassiveLevel = startLevel,
                BuildingLevel = startLevel,
                UnitLevel = startLevel,
                ActivePowerLevel = startLevel,
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
        /// on success atomically deducts RP + materials and bumps the lever level
        /// + stamps current age. If the material spend fails after the RP spend
        /// succeeds, the RP is refunded so the player never loses one currency
        /// without the upgrade applying.
        /// </summary>
        public static SectAdoptionResult TryUpgradeLever(
            EntityManager em, Faction faction, string sectId, SectLeverKind lever)
        {
            var check = CanUpgradeLever(em, faction, sectId, lever, out int cost, out var materialCost);
            if (check != SectAdoptionResult.Ok) return check;

            if (!FactionReligionPointsHelper.TrySpend(em, faction, cost))
                return SectAdoptionResult.NotEnoughRP;
            if (!FactionEconomy.Spend(em, faction, materialCost, TheWaningBorder.Economy.SpendCategory.Religion))
            {
                FactionReligionPointsHelper.Refund(em, faction, cost);
                return SectAdoptionResult.NotEnoughRP;
            }

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
            var query = QC_HallTagFactionTagFactionProgress.Get(em, QT_HallTagFactionTagFactionProgress);
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
