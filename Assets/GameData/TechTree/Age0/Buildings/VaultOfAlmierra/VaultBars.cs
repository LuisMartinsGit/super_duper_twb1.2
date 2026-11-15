// THE VAULT'S FOUR BARS (docs/Design/Age_0.md § Vault of Almiérra; decision 46,
// 2026-11-10, reworked by decision 60, 2026-11-15).
//
//   * EACH BAR BELONGS TO ONE RESOURCE (the Vault SO's vaultBarResource):
//     bars 1 and 2 supplies, bar 3 iron, bar 4 veilstone. A bar is filled
//     only with its own resource and raises only that resource's income.
//   * What a bar holds and what a full bar gives come from the Vault's LEVEL
//     (the level SOs' vaultBarCapacity and vaultBarBonus; the Vault SO's own
//     before any level): each level-up raises the bonus and the capacity.
//   * Each bar is cut into vaultSubBars sub-bars; every FILLED sub-bar adds
//     its share of the bonus at once (a full bar = the whole bonus).
//   * Bar 1 is open from the start; Second / Third / Fourth Coffer open the
//     next, in order (research at the Vault).
//   * A withdrawal returns exactly what was paid in, by resource, and
//     empties every bar: the bonus and the progress are gone.
//
// The bonus applies to resource INCOME only (territory, extractors, the
// capital, building trickles, Farms) — never to refunds, grants, loot, curse
// bounties, Market purchases, a Vault withdrawal or religion points.
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.Systems.Economy
{
    public static class VaultBars
    {
        private const string VaultId = "VaultOfAlmierra";

        /// <summary>The Vault's research, by id (the tech SOs under
        /// Age0/Buildings/VaultOfAlmierra/Research/). A roster, not tuning.</summary>
        public const string SecondCoffer = "SecondCoffer";
        public const string ThirdCoffer = "ThirdCoffer";
        public const string FourthCoffer = "FourthCoffer";

        public static readonly string[] ResourceNames = { "None", "Supplies", "Iron", "Veilstone" };

        static bool Has(Faction f, string tech)
        {
            var r = FactionResearchState.Instance;
            return r != null && r.HasResearched(f, tech);
        }

        /// <summary>The level the Vault's bars read: the building's own (levels
        /// are faction-wide per type), at least 1.</summary>
        public static int LevelOf(EntityManager em, Entity vault)
            => em.HasComponent<BuildingUpgradeState>(vault)
                ? System.Math.Max(1, (int)em.GetComponentData<BuildingUpgradeState>(vault).Level)
                : 1;

        /// <summary>The resource bar <paramref name="i"/> (0..3) takes and
        /// boosts: 1 supplies, 2 iron, 3 veilstone.</summary>
        public static int ResourceOf(int i)
        {
            var res = TechCatalog.Building(VaultId).vaultBarResource;
            if (res == null || res.Length == 0) return 1;
            int r = res[System.Math.Min(i, res.Length - 1)];
            return r < 1 || r > 3 ? 1 : r;
        }

        /// <summary>What bar <paramref name="i"/> (0..3) holds when full, at
        /// Vault level <paramref name="level"/>.</summary>
        public static int CapacityOf(int i, int level)
        {
            int[] caps = TechCatalog.TryGetBuildingLevel(Cultures.Alanthor, VaultId, level, out var lvl)
                         && lvl.vaultBarCapacity != null && lvl.vaultBarCapacity.Length > 0
                ? lvl.vaultBarCapacity
                : TechCatalog.Building(VaultId).vaultBarCapacity;
            if (caps == null || caps.Length == 0) return 1;
            return System.Math.Max(1, caps[System.Math.Min(i, caps.Length - 1)]);
        }

        /// <summary>The income bonus one full bar gives at Vault level
        /// <paramref name="level"/>.</summary>
        public static float BonusPerBar(int level)
            => TechCatalog.TryGetBuildingLevel(Cultures.Alanthor, VaultId, level, out var lvl) && lvl.vaultBarBonus > 0f
                ? lvl.vaultBarBonus
                : TechCatalog.Building(VaultId).vaultBarBonus;

        public static int SubBars => System.Math.Max(1, TechCatalog.Building(VaultId).vaultSubBars);
        /// <summary>What one filled sub-bar pays at that level.</summary>
        public static float BonusPerSubBar(int level) => BonusPerBar(level) / SubBars;

        /// <summary>Filled sub-bars of bar <paramref name="i"/> (integer maths: lockstep-safe).</summary>
        public static int FilledSubBars(in VaultStorage s, int i, int level)
        {
            int cap = CapacityOf(i, level), subs = SubBars;
            return System.Math.Min(subs, (int)((long)s.Fill(i) * subs / cap));
        }

        /// <summary>Bars this faction may fill: 1, plus one per Coffer researched.</summary>
        public static int OpenBars(Faction f)
            => 1 + (Has(f, SecondCoffer) ? 1 : 0) + (Has(f, ThirdCoffer) ? 1 : 0) + (Has(f, FourthCoffer) ? 1 : 0);

        /// <summary>How much of resource <paramref name="res"/> (1..3) the open
        /// bars of that resource would take right now.</summary>
        public static int Room(in VaultStorage s, Faction f, int level, int res)
        {
            int open = System.Math.Min(OpenBars(f), VaultStorage.Bars), room = 0;
            for (int i = 0; i < open; i++)
            {
                if (ResourceOf(i) != res) continue;
                int left = CapacityOf(i, level) - s.Fill(i);
                if (left > 0) room += left;
            }
            return room;
        }

        /// <summary>Pour <paramref name="amount"/> of resource <paramref name="res"/>
        /// into the open bars of that resource, in order. Returns what was taken.</summary>
        public static int Pour(ref VaultStorage s, Faction f, int level, int res, int amount)
        {
            int open = System.Math.Min(OpenBars(f), VaultStorage.Bars), taken = 0;
            for (int i = 0; i < open && amount > 0; i++)
            {
                if (ResourceOf(i) != res) continue;
                int cap = CapacityOf(i, level);
                int fill = s.Fill(i);
                if (fill >= cap) continue;
                int put = System.Math.Min(cap - fill, amount);
                s.SetFill(i, fill + put);
                amount -= put;
                taken += put;
            }
            return taken;
        }

        /// <summary>Filled sub-bars of this storage, across its bars of
        /// resource <paramref name="res"/>.</summary>
        public static int FilledSubBarsFor(in VaultStorage s, int res, int level)
        {
            int n = 0;
            for (int i = 0; i < VaultStorage.Bars; i++)
                if (ResourceOf(i) == res && s.Fill(i) > 0) n += FilledSubBars(s, i, level);
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
                or IncomeSource.TerritoryClaim or IncomeSource.Farm => true,
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
            foreach (var (vault, faction, e) in SystemAPI
                .Query<RefRO<VaultStorage>, RefRO<FactionTag>>()
                .WithAll<VaultTag>()
                .WithNone<UnderConstruction>()
                .WithEntityAccess())
            {
                int level = SystemAPI.HasComponent<BuildingUpgradeState>(e)
                    ? System.Math.Max(1, (int)SystemAPI.GetComponent<BuildingUpgradeState>(e).Level)
                    : 1;
                float per = VaultBars.BonusPerSubBar(level);
                if (per <= 0f) continue;
                for (int res = 1; res <= 3; res++)
                {
                    int filled = VaultBars.FilledSubBarsFor(vault.ValueRO, res, level);
                    if (filled > 0) VaultBars.AddBonus(faction.ValueRO.Value, res, filled * per);
                }
            }
        }
    }
}
