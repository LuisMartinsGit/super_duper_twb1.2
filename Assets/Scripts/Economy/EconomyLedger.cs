// EconomyLedger.cs
// Gross income and spending per faction, by source — for the match metrics.
//
// WHY
// Metrics_Faction.csv records the BANK. A bank delta is income minus
// spending, so a faction that earns 3,000 supplies a minute and spends 2,900
// looks exactly like one that earns 100 and spends nothing. The 2026-10-04
// "something happens at 30 minutes" hunt was that confusion: the income never
// changed, the spending stopped. This ledger records both halves, so the
// Muster Rolls can plot what was EARNED and what was SPENT, not just what was
// left over.
//
// OBSERVATION ONLY. Nothing in the simulation reads this class. Every call is
// a no-op unless MatchMetrics is recording, and it never touches an entity —
// it is a float table in a static, drained by MatchMetrics each sample. It is
// therefore lockstep-safe by construction: two peers may disagree about what
// is in here and nothing in the game can notice.
//
// Callers tag the credit/debit at the point the bank moves:
//   FactionEconomy.Add(em, f, cost, IncomeSource.X)
//   FactionEconomy.Spend(em, f, cost, SpendCategory.Y)
// An untagged Add is booked as Refund (what every untagged caller left in the
// tree is: cancelled plans, deleted sites, AI foundations that found no
// worker), an untagged Spend as Other. Writes that bypass FactionEconomy
// entirely are caught by MatchMetrics as the residual between the bank delta
// and this ledger, and land in the "untracked" column.

using TheWaningBorder.Core;
using Cost = TheWaningBorder.Core.Cost;

namespace TheWaningBorder.Economy
{
    /// <summary>Where a credit to a faction bank came from.</summary>
    public enum IncomeSource : byte
    {
        /// <summary>A held resource node with no extractor on it.</summary>
        EmptySlot,
        /// <summary>A Gatherer's Hut working a supply slot.</summary>
        GatherersHut,
        /// <summary>A Mine working an iron slot.</summary>
        Mine,
        /// <summary>A Veilstone Mine working an outcrop.</summary>
        VeilstoneMine,
        /// <summary>The capital-level (Hall/Fortress L2 x2, L3 x4) share of
        /// a territory's yield: everything above the x1 base.</summary>
        FortressLevel,
        /// <summary>The capital's own SuppliesIncome (ResourceTickSystem).</summary>
        Capital,
        /// <summary>Any other building's flat SuppliesIncome / IronIncome /
        /// VeilstoneIncome / VeilsteelIncome.</summary>
        BuildingPassive,
        /// <summary>Trading Outpost output, Runai trader deliveries.</summary>
        Trade,
        /// <summary>A Vault withdrawal (deposit plus interest).</summary>
        Vault,
        /// <summary>Rewards for killing curse structures/units.</summary>
        CurseKill,
        /// <summary>Plunder, pillage, kill bounties, dead caravans.</summary>
        Loot,
        /// <summary>Money coming back: cancelled plans/production, deletes,
        /// refunded upgrades, AI foundation refunds.</summary>
        Refund,
        /// <summary>Scripted grants (start-age bonus, tutorial).</summary>
        Grant,
        /// <summary>Sect / well / anything else tagged but uncategorised.</summary>
        Other,
        Count
    }

    /// <summary>What a debit from a faction bank bought.</summary>
    public enum SpendCategory : byte
    {
        Units,
        Buildings,
        /// <summary>Building level-ups and wall tier upgrades.</summary>
        Upgrades,
        /// <summary>Technologies and equipment tiers.</summary>
        Research,
        AgeUp,
        /// <summary>Trading Outpost inputs.</summary>
        Trade,
        Repair,
        /// <summary>Tithe, chapels, sect purchases.</summary>
        Religion,
        /// <summary>Vault deposits.</summary>
        Vault,
        /// <summary>Credits lost to FactionResources.ResourceCap — paid out
        /// but clamped away. Not a purchase; booked so the ledger balances.</summary>
        Overflow,
        Other,
        Count
    }

    public static class EconomyLedger
    {
        public const int Factions = 8;
        public const int Resources = 4;   // supplies, iron, veilstone, veilsteel

        // [faction, source, resource] — floats, because the territory tick
        // pays fractional per-tick amounts that the bank carries.
        private static readonly float[,,] _income =
            new float[Factions, (int)IncomeSource.Count, Resources];
        private static readonly float[,,] _spend =
            new float[Factions, (int)SpendCategory.Count, Resources];
        /// <summary>Income since the match began, never reset by the sample
        /// (the Score's "resources earned", docs/Design/Score.md). Kept
        /// whether or not the metrics recorder runs: it is the one ledger
        /// the shipped game reads.</summary>
        private static readonly float[,,] _lifetimeIncome =
            new float[Factions, (int)IncomeSource.Count, Resources];

        /// <summary>True while the match metrics recorder is running. Off,
        /// every entry point returns at the first line.</summary>
        public static bool Recording
            => TheWaningBorder.Core.Diagnostics.MatchMetrics.Enabled;

        public static void Credit(Faction faction, IncomeSource source,
            float supplies, float iron, float veilstone, float veilsteel)
        {
            int f = (int)faction;
            if (f < 0 || f >= Factions || source >= IncomeSource.Count) return;
            int s = (int)source;
            _lifetimeIncome[f, s, 0] += supplies;
            _lifetimeIncome[f, s, 1] += iron;
            _lifetimeIncome[f, s, 2] += veilstone;
            _lifetimeIncome[f, s, 3] += veilsteel;
            if (!Recording) return;
            _income[f, s, 0] += supplies;
            _income[f, s, 1] += iron;
            _income[f, s, 2] += veilstone;
            _income[f, s, 3] += veilsteel;
        }

        public static void Credit(Faction faction, IncomeSource source, in Cost c)
            => Credit(faction, source, c.Supplies, c.Iron, c.Veilstone, c.Veilsteel);

        public static void Debit(Faction faction, SpendCategory category,
            float supplies, float iron, float veilstone, float veilsteel)
        {
            if (!Recording) return;
            int f = (int)faction;
            if (f < 0 || f >= Factions || category >= SpendCategory.Count) return;
            int k = (int)category;
            _spend[f, k, 0] += supplies;
            _spend[f, k, 1] += iron;
            _spend[f, k, 2] += veilstone;
            _spend[f, k, 3] += veilsteel;
        }

        public static void Debit(Faction faction, SpendCategory category, in Cost c)
            => Debit(faction, category, c.Supplies, c.Iron, c.Veilstone, c.Veilsteel);

        /// <summary>Accumulated income since the last <see cref="Reset"/>.</summary>
        public static float IncomeOf(int faction, IncomeSource source, int resource)
            => _income[faction, (int)source, resource];

        /// <summary>Income since the match began (see the field).</summary>
        public static float LifetimeIncomeOf(int faction, IncomeSource source, int resource)
            => _lifetimeIncome[faction, (int)source, resource];

        /// <summary>A new match: MatchScoreSystem calls this on the epoch change.</summary>
        public static void ResetLifetime()
            => System.Array.Clear(_lifetimeIncome, 0, _lifetimeIncome.Length);

        /// <summary>Accumulated spending since the last <see cref="Reset"/>.</summary>
        public static float SpendOf(int faction, SpendCategory category, int resource)
            => _spend[faction, (int)category, resource];

        /// <summary>Zero every bucket. MatchMetrics calls this after each
        /// sample is written, and on match start.</summary>
        public static void Reset()
        {
            System.Array.Clear(_income, 0, _income.Length);
            System.Array.Clear(_spend, 0, _spend.Length);
        }
    }
}
