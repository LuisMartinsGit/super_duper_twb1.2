// THE VAULT'S FOUR BARS (docs/Design/Age_0.md § Vault of Almiérra,
// Unification decision 46, 2026-11-10). Replaces VaultInterestSystem and the
// capped simple interest of decision 41.
//
//   * Resources paid in fill the open bars in order, any resource one for
//     one, up to the Vault SO's vaultBarCapacity for that bar (each bar
//     harder to fill than the last).
//   * Each bar boosts ONE resource, chosen when it starts filling: supplies
//     always, iron after Iron Accounts, veilstone after Veilstone Accounts.
//   * Each bar is cut into vaultSubBars sub-bars; every FILLED sub-bar adds
//     its share of the Vault SO's vaultBarBonus to the income of the resource
//     its bar boosts, at once (a full bar = the whole vaultBarBonus); bars on
//     one resource stack.
//   * Bar 1 is open from the start; Second / Third / Fourth Coffer open the
//     others (research at the Vault).
//   * A withdrawal returns exactly what was paid in, by the resource it was
//     paid in, and empties every bar: the bonus and the progress are gone.
//
// The bonus applies to resource INCOME only (territory, extractors, Trading
// Outposts, the capital, building trickles) — never to refunds, grants,
// loot, curse bounties, a Vault withdrawal or religion points.
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Economy
{
    public static class VaultBars
    {
        /// <summary>The Vault's research, by id (the tech SOs under
        /// Age0/Buildings/VaultOfAlmierra/Research/). A roster, not tuning.</summary>
        public const string SecondCoffer = "SecondCoffer";
        public const string ThirdCoffer = "ThirdCoffer";
        public const string FourthCoffer = "FourthCoffer";
        public const string IronAccounts = "IronAccounts";
        public const string VeilstoneAccounts = "VeilstoneAccounts";

        public static readonly string[] ResourceNames = { "None", "Supplies", "Iron", "Veilstone" };

        static bool Has(Faction f, string tech)
        {
            var r = FactionResearchState.Instance;
            return r != null && r.HasResearched(f, tech);
        }

        /// <summary>What bar <paramref name="i"/> (0..3) holds when full.</summary>
        public static int CapacityOf(int i)
        {
            var caps = TechCatalog.Building("VaultOfAlmierra").vaultBarCapacity;
            if (caps == null || caps.Length == 0) return 1;
            return System.Math.Max(1, caps[System.Math.Min(i, caps.Length - 1)]);
        }
        public static float BonusPerBar => TechCatalog.Building("VaultOfAlmierra").vaultBarBonus;
        public static int SubBars => System.Math.Max(1, TechCatalog.Building("VaultOfAlmierra").vaultSubBars);
        /// <summary>What one filled sub-bar pays (+1 % with 5 sub-bars of a 5 % bar).</summary>
        public static float BonusPerSubBar => BonusPerBar / SubBars;

        /// <summary>Filled sub-bars of bar <paramref name="i"/> (integer maths: lockstep-safe).</summary>
        public static int FilledSubBars(in VaultStorage s, int i)
        {
            int cap = CapacityOf(i), subs = SubBars;
            return System.Math.Min(subs, (int)((long)s.Fill(i) * subs / cap));
        }

        /// <summary>Bars this faction may fill: 1, plus one per Coffer researched.</summary>
        public static int OpenBars(Faction f)
            => 1 + (Has(f, SecondCoffer) ? 1 : 0) + (Has(f, ThirdCoffer) ? 1 : 0) + (Has(f, FourthCoffer) ? 1 : 0);

        /// <summary>May a bar of this faction boost resource <paramref name="res"/> (1..3)?</summary>
        public static bool MayBoost(Faction f, int res)
            => res == 1 || (res == 2 && Has(f, IronAccounts)) || (res == 3 && Has(f, VeilstoneAccounts));

        /// <summary>How much of <paramref name="amount"/> the open bars would
        /// take right now, and which bar it lands in first (-1 = none).</summary>
        public static int Room(in VaultStorage s, Faction f, out int firstBar)
        {
            int open = System.Math.Min(OpenBars(f), VaultStorage.Bars), room = 0;
            firstBar = -1;
            for (int i = 0; i < open; i++)
            {
                int left = CapacityOf(i) - s.Fill(i);
                if (left <= 0) continue;
                if (firstBar < 0) firstBar = i;
                room += left;
            }
            return room;
        }

        /// <summary>Pour <paramref name="amount"/> into the open bars in order.
        /// A bar that starts filling takes <paramref name="boost"/>; a bar
        /// already started keeps its own. Returns what was taken.</summary>
        public static int Pour(ref VaultStorage s, Faction f, int amount, int boost)
        {
            int open = System.Math.Min(OpenBars(f), VaultStorage.Bars), taken = 0;
            for (int i = 0; i < open && amount > 0; i++)
            {
                int cap = CapacityOf(i);
                int fill = s.Fill(i);
                if (fill >= cap) continue;
                if (s.Boost(i) == 0) s.SetBoost(i, (byte)boost);
                int put = System.Math.Min(cap - fill, amount);
                s.SetFill(i, fill + put);
                amount -= put;
                taken += put;
            }
            return taken;
        }

        /// <summary>Filled sub-bars of this storage, across its bars, boosting
        /// resource <paramref name="res"/>.</summary>
        public static int FilledSubBarsFor(in VaultStorage s, int res)
        {
            int n = 0;
            for (int i = 0; i < VaultStorage.Bars; i++)
                if (s.Boost(i) == res) n += FilledSubBars(s, i);
            return n;
        }

        // ── The income bonus ─────────────────────────────────────────────

        const int MaxFactions = 16;
        // Per faction, per resource (supplies, iron, veilstone): the bonus
        // fraction its full bars give, refreshed every tick by VaultBarSystem.
        static readonly float[,] _bonus = new float[MaxFactions, 3];
        // The fractional part of each bonus payment, carried so small
        // trickles are not rounded away (as AICheat.ScaleIncome does).
        static readonly float[,] _carry = new float[MaxFactions, 3];
        // Systems and statics outlive a match: the carry restarts per match
        // (SimCadence.Epoch), as AICheat.ScaleIncome's does.
        static int _carryEpoch = -1;

        internal static void Clear()
        {
            System.Array.Clear(_bonus, 0, _bonus.Length);
        }

        internal static void ClearCarry()
        {
            System.Array.Clear(_carry, 0, _carry.Length);
        }

        internal static void AddBonus(Faction f, int res, float fraction)
        {
            int k = (int)f;
            if (k < 0 || k >= MaxFactions || res < 1 || res > 3) return;
            _bonus[k, res - 1] += fraction;
        }

        /// <summary>The bonus fraction this faction's full bars give resource
        /// <paramref name="res"/> (1..3) right now.</summary>
        public static float BonusOf(Faction f, int res)
        {
            int k = (int)f;
            return k < 0 || k >= MaxFactions || res < 1 || res > 3 ? 0f : _bonus[k, res - 1];
        }

        static bool IsIncome(IncomeSource s) => s switch
        {
            IncomeSource.EmptySlot or IncomeSource.GatherersHut or IncomeSource.Mine
                or IncomeSource.VeilstoneMine or IncomeSource.FortressLevel or IncomeSource.Capital
                or IncomeSource.BuildingPassive or IncomeSource.Trade or IncomeSource.GuildSurvey
                or IncomeSource.TerritoryClaim => true,
            _ => false,
        };

        /// <summary>FactionEconomy.Add's hook: resource income raised by the
        /// faction's full bars.</summary>
        public static Cost ScaleIncome(Faction f, in Cost c, IncomeSource source)
        {
            if (!IsIncome(source)) return c;
            int k = (int)f;
            if (k < 0 || k >= MaxFactions) return c;
            var r = c;
            r.Supplies = Scale(c.Supplies, k, 0);
            r.Iron = Scale(c.Iron, k, 1);
            r.Veilstone = Scale(c.Veilstone, k, 2);
            return r;
        }

        static int Scale(int amount, int k, int res)
        {
            float b = _bonus[k, res];
            if (amount <= 0 || b <= 0f) return amount;
            if (_carryEpoch != SimCadence.Epoch) { _carryEpoch = SimCadence.Epoch; ClearCarry(); }
            float exact = amount * b + _carry[k, res];
            int whole = (int)exact;
            _carry[k, res] = exact - whole;
            return amount + whole;
        }
    }

    /// <summary>Refreshes every faction's Vault bonus from its finished
    /// Vaults' full bars, every tick (a fallen Vault stops paying at once).</summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct VaultBarSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            VaultBars.Clear();
            VaultBars.ClearCarry();
        }

        public void OnUpdate(ref SystemState state)
        {
            VaultBars.Clear();
            float per = VaultBars.BonusPerSubBar;
            if (per <= 0f) return;
            foreach (var (vault, faction) in SystemAPI
                .Query<RefRO<VaultStorage>, RefRO<FactionTag>>()
                .WithAll<VaultTag>()
                .WithNone<UnderConstruction>())
            {
                for (int res = 1; res <= 3; res++)
                {
                    int filled = VaultBars.FilledSubBarsFor(vault.ValueRO, res);
                    if (filled > 0) VaultBars.AddBonus(faction.ValueRO.Value, res, filled * per);
                }
            }
        }
    }
}
