// THE VAULT'S FOUR BARS (docs/Design/Age_0.md § Vault of Almiérra; decision 46,
// 2026-11-10, reworked by decision 60, 2026-11-15, and decision 85,
// 2026-11-19).
//
// Decision 85 (developer: "Instead of being a 5% multiplier, it acts as a
// trickle income of whatever resource is stored, and it can't store supplies.
// No resource type locked behind research"):
//   * A bar stores IRON or VEILSTONE — and the LAST bar supplies too
//     (developer: "The last bar can be filled with supplies"). It takes
//     whichever is first paid into it and keeps it until a withdrawal
//     empties it.
//   * A FULL bar pays its own trickle of that resource every minute
//     (the level SOs' vaultBarIncomePerMinute, one entry per bar, read at the
//     Vault's level); a bar that is not full pays nothing.
//   * What a bar holds is the level SOs' vaultBarCapacity.
//   * Bar 1 is open from the start; Second / Third / Fourth Coffer open the
//     next, in order (research at the Vault). They open bars, not resources.
//   * A withdrawal returns exactly what was paid in, by resource, and empties
//     every bar: the trickle stops.
//
// The trickle is paid by VaultBarSystem once a second, in sixtieths carried
// on the Vault (VaultStorage.CarryIron / CarryVeilstone), booked as
// IncomeSource.VaultBar.
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

        /// <summary>Resource codes some bar may store (1 supplies, 2 iron, 3 veilstone).</summary>
        public static bool Storable(int res) => res >= 1 && res <= 3;

        /// <summary>Does bar <paramref name="i"/> take resource <paramref name="res"/>?
        /// Iron and veilstone go in any bar; supplies only in the last
        /// (decision 85).</summary>
        public static bool Takes(int i, int res)
            => res == 2 || res == 3 || (res == 1 && i == VaultStorage.Bars - 1);

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

        /// <summary>The resource bar <paramref name="i"/> holds: 1 supplies,
        /// 2 iron, 3 veilstone, 0 while it is empty and has not started.</summary>
        public static int ResourceOf(in VaultStorage s, int i) => s.Res(i);

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

        /// <summary>What a FULL bar <paramref name="i"/> pays a minute, of the
        /// resource it holds, at Vault level <paramref name="level"/>.</summary>
        public static int IncomeOf(int i, int level)
        {
            if (!TechCatalog.TryGetBuildingLevel(Cultures.Alanthor, VaultId, level, out var lvl)
                || lvl.vaultBarIncomePerMinute == null || lvl.vaultBarIncomePerMinute.Length == 0) return 0;
            var inc = lvl.vaultBarIncomePerMinute;
            return System.Math.Max(0, inc[System.Math.Min(i, inc.Length - 1)]);
        }

        public static int SubBars => System.Math.Max(1, TechCatalog.Building(VaultId).vaultSubBars);

        /// <summary>Filled sub-bars of bar <paramref name="i"/> (display only:
        /// only a full bar pays).</summary>
        public static int FilledSubBars(in VaultStorage s, int i, int level)
        {
            int cap = CapacityOf(i, level), subs = SubBars;
            return System.Math.Min(subs, (int)((long)s.Fill(i) * subs / cap));
        }

        public static bool IsFull(in VaultStorage s, int i, int level) => s.Fill(i) >= CapacityOf(i, level);

        /// <summary>Bars this faction may fill: 1, plus one per Coffer researched.</summary>
        public static int OpenBars(Faction f)
            => 1 + (Has(f, SecondCoffer) ? 1 : 0) + (Has(f, ThirdCoffer) ? 1 : 0) + (Has(f, FourthCoffer) ? 1 : 0);

        /// <summary>How much of resource <paramref name="res"/> the open bars
        /// would take right now: its own bars' room, and every empty bar.</summary>
        public static int Room(in VaultStorage s, Faction f, int level, int res)
        {
            if (!Storable(res)) return 0;
            int open = System.Math.Min(OpenBars(f), VaultStorage.Bars), room = 0;
            for (int i = 0; i < open; i++)
            {
                if (!Takes(i, res)) continue;
                int r = s.Res(i);
                if (r != 0 && r != res) continue;
                int left = CapacityOf(i, level) - s.Fill(i);
                if (left > 0) room += left;
            }
            return room;
        }

        /// <summary>Pour <paramref name="amount"/> of resource <paramref name="res"/>
        /// into the open bars in order: a bar already holding it, or an empty
        /// bar, which takes it from now on. Returns what was taken.</summary>
        public static int Pour(ref VaultStorage s, Faction f, int level, int res, int amount)
        {
            if (!Storable(res)) return 0;
            int open = System.Math.Min(OpenBars(f), VaultStorage.Bars), taken = 0;
            for (int i = 0; i < open && amount > 0; i++)
            {
                if (!Takes(i, res)) continue;
                int r = s.Res(i);
                if (r != 0 && r != res) continue;
                int cap = CapacityOf(i, level);
                int fill = s.Fill(i);
                if (fill >= cap) continue;
                int put = System.Math.Min(cap - fill, amount);
                s.SetFill(i, fill + put);
                s.SetRes(i, res);
                amount -= put;
                taken += put;
            }
            return taken;
        }

        /// <summary>What this storage's full bars pay a minute of resource
        /// <paramref name="res"/> (1 supplies, 2 iron, 3 veilstone) at its level.</summary>
        public static int IncomePerMinute(in VaultStorage s, int level, int res)
        {
            int n = 0;
            for (int i = 0; i < VaultStorage.Bars; i++)
                if (s.Res(i) == res && IsFull(s, i, level)) n += IncomeOf(i, level);
            return n;
        }
    }

    /// <summary>
    /// Pays every finished Vault's full bars (decision 85): once a second, each
    /// full bar's vaultBarIncomePerMinute of the resource it holds, in
    /// sixtieths carried on the Vault, into its owner's bank as
    /// IncomeSource.VaultBar. A fallen or unfinished Vault pays nothing.
    /// Lockstep: SimCadence clock, integer arithmetic, Vaults in entity-index order.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class VaultBarSystem : SystemBase
    {
        private const float TickSeconds = 1f;   // the pay clock (engine-side, as FarmSystem)
        private SimCadence.Periodic _tick;
        private readonly System.Collections.Generic.List<Entity> _vaults = new System.Collections.Generic.List<Entity>();

        protected override void OnCreate()
        {
            RequireForUpdate(GetEntityQuery(ComponentType.ReadOnly<VaultStorage>()));
        }

        protected override void OnUpdate()
        {
            if (!_tick.Due(SystemAPI.Time.DeltaTime, TickSeconds)) return;
            var em = EntityManager;
            _vaults.Clear();
            foreach (var (_, e) in SystemAPI.Query<RefRO<VaultStorage>>().WithAll<VaultTag, FactionTag>()
                         .WithNone<UnderConstruction>().WithEntityAccess())
                _vaults.Add(e);
            _vaults.Sort((a, b) => a.Index.CompareTo(b.Index));
            for (int k = 0; k < _vaults.Count; k++)
            {
                var e = _vaults[k];
                if (!em.Exists(e)) continue;
                if (em.HasComponent<Health>(e) && em.GetComponentData<Health>(e).Value <= 0) continue;
                var s = em.GetComponentData<VaultStorage>(e);
                if (s.Paid <= 0) continue;
                int level = VaultBars.LevelOf(em, e);
                int sup = VaultBars.IncomePerMinute(s, level, 1);
                int iron = VaultBars.IncomePerMinute(s, level, 2);
                int veil = VaultBars.IncomePerMinute(s, level, 3);
                if (sup <= 0 && iron <= 0 && veil <= 0) continue;
                s.CarrySupplies += sup;
                s.CarryIron += iron;
                s.CarryVeilstone += veil;
                int ws = s.CarrySupplies / 60, wi = s.CarryIron / 60, wv = s.CarryVeilstone / 60;
                s.CarrySupplies %= 60;
                s.CarryIron %= 60;
                s.CarryVeilstone %= 60;
                em.SetComponentData(e, s);
                if (ws > 0 || wi > 0 || wv > 0)
                    FactionEconomy.Add(em, em.GetComponentData<FactionTag>(e).Value,
                        Cost.Of(supplies: ws, iron: wi, veilstone: wv), IncomeSource.VaultBar);
            }
        }
    }
}
