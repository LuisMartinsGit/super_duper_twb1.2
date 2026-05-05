// FactionReligionPoints.cs
// Per-faction RP balance + age-tracking for the sect-adoption economy.
// Lives on the faction bank entity.
//
// RP sources:
//  - Shrine of Ahridan completion (Age 1) → +1 (one-time, latched)
//  - Age II / III / IV up           → +6 / +8 / +10
//  - Carryover at age-up: floor(leftover / 2) added to the per-age award
//
// RP sinks:
//  - Adopt sect           → 2 (same cluster) or 3 (cross cluster)
//  - Lever upgrade Lv I→II → 2
//  - Lever upgrade Lv II→III → 3
//
// Phase 1 (task-063): component + grant/spend helpers. Adoption logic lives
// in SectAdoption; age-up wiring lives in AgeUpSystem; Shrine wiring lives
// in BuildingConstructionSystem.GrantShrineRPBonus.
//
// Location: Assets/Scripts/Economy/FactionReligionPoints.cs

using Unity.Entities;

namespace TheWaningBorder.Economy
{
    /// <summary>
    /// Religion-Points balance for a faction. Sits on the faction bank entity.
    /// </summary>
    public struct FactionReligionPoints : IComponentData
    {
        /// <summary>Current spendable RP balance.</summary>
        public int Balance;

        /// <summary>1 = Shrine bonus already awarded (latched), 0 = not yet.
        /// Prevents the +1 Shrine reward from firing more than once if the
        /// player happens to rebuild a Shrine.</summary>
        public byte ShrineBonusAwarded;

        /// <summary>
        /// The age the faction is currently in (1/2/3/4). Stored here so age-up
        /// hooks can detect transitions and apply the carryover formula.
        /// Initialised to 1 on faction creation.
        /// </summary>
        public byte CurrentAge;
    }

    /// <summary>
    /// Static helpers for awarding / spending Religion Points. Called by
    /// AgeUpSystem (per-age award), BuildingConstructionSystem (Shrine bonus),
    /// and SectAdoption (spending on chapels and lever upgrades).
    /// </summary>
    public static class FactionReligionPointsHelper
    {
        /// <summary>
        /// Award the +1 Shrine bonus exactly once per faction. Idempotent —
        /// safe to call from BuildingConstructionSystem on every Shrine
        /// completion event; the latch flag suppresses duplicates.
        /// Returns true if RP was actually awarded this call.
        /// </summary>
        public static bool TryAwardShrineBonus(EntityManager em, Faction faction)
        {
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return false;
            if (!em.HasComponent<FactionReligionPoints>(bank)) return false;

            var rp = em.GetComponentData<FactionReligionPoints>(bank);
            if (rp.ShrineBonusAwarded != 0) return false;

            rp.Balance += SectConfig.RpAwardShrine;
            rp.ShrineBonusAwarded = 1;
            em.SetComponentData(bank, rp);
            return true;
        }

        /// <summary>
        /// Award the per-age bonus for the given age (2/3/4). Applies the 2:1
        /// carryover rule on the *previous* balance: floor(leftover / 2) is
        /// rolled into the new age's award. Updates CurrentAge to the new age.
        /// Returns the total RP added (post-carryover) or 0 if not applicable.
        /// </summary>
        public static int AwardAgeUp(EntityManager em, Faction faction, int newAge)
        {
            int award = SectConfig.RpAwardForAge(newAge);
            if (award == 0) return 0;

            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return 0;
            if (!em.HasComponent<FactionReligionPoints>(bank)) return 0;

            var rp = em.GetComponentData<FactionReligionPoints>(bank);

            // Skip if we've already awarded this age (re-entrancy guard).
            if (rp.CurrentAge >= newAge) return 0;

            // Carry-over: leftover before the award is halved (floor) and added on top.
            int carry = rp.Balance / SectConfig.CarryoverDivisor;
            int delta = award + carry;

            // Drop the un-carried half (the post-carry remainder is discarded —
            // i.e. balance is REPLACED by carry, not balance + carry, otherwise
            // the player would keep their leftover *and* gain the halved bonus).
            // Spec: "Unspent points carry to the next age at 2:1 (4 unspent → 2 next age)".
            // That reads as a replacement: the leftover is CONVERTED at 2:1.
            rp.Balance = carry + award;
            rp.CurrentAge = (byte)newAge;
            em.SetComponentData(bank, rp);
            return delta;
        }

        /// <summary>
        /// Try to deduct <paramref name="cost"/> RP. Returns true if the spend
        /// succeeded (cost was deducted), false if the faction couldn't afford
        /// it (no change made). Negative or zero cost returns true with no
        /// change.
        /// </summary>
        public static bool TrySpend(EntityManager em, Faction faction, int cost)
        {
            if (cost <= 0) return true;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return false;
            if (!em.HasComponent<FactionReligionPoints>(bank)) return false;

            var rp = em.GetComponentData<FactionReligionPoints>(bank);
            if (rp.Balance < cost) return false;

            rp.Balance -= cost;
            em.SetComponentData(bank, rp);
            return true;
        }

        /// <summary>Read-only balance lookup. Returns 0 if no bank or no RP component.</summary>
        public static int GetBalance(EntityManager em, Faction faction)
        {
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return 0;
            if (!em.HasComponent<FactionReligionPoints>(bank)) return 0;
            return em.GetComponentData<FactionReligionPoints>(bank).Balance;
        }

        /// <summary>True if the faction can afford <paramref name="cost"/> RP right now.</summary>
        public static bool CanAfford(EntityManager em, Faction faction, int cost)
        {
            if (cost <= 0) return true;
            return GetBalance(em, faction) >= cost;
        }
    }
}
