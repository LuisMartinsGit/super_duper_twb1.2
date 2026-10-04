// FactionReligionPoints.cs
// Per-faction RP balance for the religion layer. Lives on the faction bank.
//
// docs/Design/Religion.md (2026-09-29):
//
// RP sources:
//  - killing curse units: the last hit pays POINTS (crystalling / veilstinger /
//    godsplinter), and points convert to RP at an escalating rate — the first
//    RP is cheap, later ones cost the cap (CurseKillReligionSystem);
//  - the Temple's Tithe: RP for resources, dearer each time.
//  (Age-ups do not award RP, and the Temple has no levels.)
//
// RP sinks:
//  - the Temple itself (1), a chapel (2 with affinity / 3 without),
//  - the second active and the wildcard, chapel levels II / III,
//  - a sect hero.
//
// Every number is in FactionReligionPoints.asset (FactionReligionPointsConfig).

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

        /// <summary>
        /// The age the faction is currently in (1/2/3/4). Stored here so age-up
        /// hooks can detect transitions and apply the carryover formula.
        /// Initialised to 1 on faction creation.
        /// </summary>
        public byte CurrentAge;

        /// <summary>Kill points banked toward the next RP (Religion.md §1).</summary>
        public int Pts;

        /// <summary>RP earned from kills so far — the n that prices the next one.</summary>
        public int KillRp;

        /// <summary>Tithes bought so far — the exponent of the Tithe's price.</summary>
        public int TithesBought;

        /// <summary>Seconds a standing Temple has banked toward its next
        /// point (Religion.md §2 — the Temple's slow trickle).</summary>
        public int TempleSeconds;
    }

    /// <summary>
    /// Static helpers for awarding / spending Religion Points. Called by
    /// AgeUpSystem (per-age award), BuildingConstructionSystem,
    /// and SectAdoption (spending on chapels and lever upgrades).
    /// </summary>
    public static class FactionReligionPointsHelper
    {
        private static FactionReligionPointsConfig _cfg;
        public static FactionReligionPointsConfig Cfg =>
            _cfg != null ? _cfg
            : (_cfg = TheWaningBorder.Core.Settings.ComponentConfig.Require<FactionReligionPointsConfig>());

        /// <summary>Kill points the next RP costs, given how many RP kills
        /// have already paid: min(base + step x n, cap).</summary>
        public static int PtsForNext(int killRpSoFar)
            => System.Math.Min(Cfg.ptsBase + Cfg.ptsStep * killRpSoFar, Cfg.ptsCap);

        /// <summary>
        /// Bank curse-kill points for a faction and convert every full RP
        /// they pay for. Returns the RP gained (0 when none).
        /// </summary>
        public static int AddKillPoints(EntityManager em, Faction faction, int pts)
        {
            if (pts <= 0) return 0;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return 0;
            if (!em.HasComponent<FactionReligionPoints>(bank)) return 0;
            var rp = em.GetComponentData<FactionReligionPoints>(bank);
            rp.Pts += pts;
            int gained = 0;
            for (int need = PtsForNext(rp.KillRp); rp.Pts >= need; need = PtsForNext(rp.KillRp))
            {
                rp.Pts -= need;
                rp.KillRp++;
                rp.Balance++;
                gained++;
            }
            em.SetComponentData(bank, rp);
            return gained;
        }

        /// <summary>(points banked, points the next RP needs) for the HUD.</summary>
        public static (int have, int need) PtsProgress(EntityManager em, Faction faction)
        {
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)
                || !em.HasComponent<FactionReligionPoints>(bank)) return (0, 0);
            var rp = em.GetComponentData<FactionReligionPoints>(bank);
            return (rp.Pts, PtsForNext(rp.KillRp));
        }

        /// <summary>The Tithe's price for this faction's next purchase:
        /// the base price x step^(tithes already bought), rounded.</summary>
        public static TheWaningBorder.Core.Cost TitheCost(EntityManager em, Faction faction)
        {
            int bought = 0;
            if (FactionEconomy.TryGetBank(em, faction, out var bank)
                && em.HasComponent<FactionReligionPoints>(bank))
                bought = em.GetComponentData<FactionReligionPoints>(bank).TithesBought;
            float m = (float)System.Math.Pow(Cfg.titheStep, bought);
            return TheWaningBorder.Core.Cost.Of(
                supplies: (int)System.Math.Round(Cfg.titheSupplies * m),
                iron: (int)System.Math.Round(Cfg.titheIron * m),
                veilstone: (int)System.Math.Round(Cfg.titheVeilstone * m));
        }

        /// <summary>Buy one RP through the Tithe. Executor side — every peer
        /// runs it at the same tick. False (nothing spent) when unaffordable.</summary>
        public static bool TryBuyTithe(EntityManager em, Faction faction)
        {
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return false;
            if (!em.HasComponent<FactionReligionPoints>(bank)) return false;
            var cost = TitheCost(em, faction);
            if (!FactionEconomy.Spend(em, faction, cost, SpendCategory.Religion)) return false;
            var rp = em.GetComponentData<FactionReligionPoints>(bank);
            rp.Balance++;
            rp.TithesBought++;
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
            // RETIRED (docs/Design/Religion.md §1, 2026-09-29): Religion
            // Points come from killing the curse and from the Temple's Tithe,
            // never from an age. The call sites (age-up, Temple upgrade, the
            // start-age promoter) still land here so the faction's age is
            // tracked; no RP changes hands and no balance is converted.
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return 0;
            if (!em.HasComponent<FactionReligionPoints>(bank)) return 0;
            var rp = em.GetComponentData<FactionReligionPoints>(bank);
            if (rp.CurrentAge >= newAge) return 0;
            rp.CurrentAge = (byte)newAge;
            em.SetComponentData(bank, rp);
            return 0;
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

        /// <summary>
        /// Add RP back to the faction's balance — used to roll back a failed
        /// composite spend (e.g. RP succeeded but materials failed in
        /// SectAdoption.TryStartAdoption). No upper bound enforced; caller
        /// is responsible for sane amounts.
        /// </summary>
        public static void Refund(EntityManager em, Faction faction, int amount)
        {
            if (amount <= 0) return;
            if (!FactionEconomy.TryGetBank(em, faction, out var bank)) return;
            if (!em.HasComponent<FactionReligionPoints>(bank)) return;
            var rp = em.GetComponentData<FactionReligionPoints>(bank);
            rp.Balance += amount;
            em.SetComponentData(bank, rp);
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
