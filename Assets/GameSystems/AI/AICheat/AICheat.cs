// AICheat.cs
// The Cheating tier (docs/Design/Game_AI.md § 2b): Expert's play, with a clearly labelled
// bonus to income, population and stats. The only tier that cheats; the multipliers are on
// AISimpleDifficulty.asset.

using Unity.Collections;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Config;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    public static class AICheat
    {
        private const int Sides = 8, Resources = 4;

        private static readonly float[,] _carry = new float[Sides, Resources];
        private static int _carryEpoch = -1;

        /// <summary>The lobby slot's choice, which a replay's header carries too.</summary>
        public static bool IsCheating(Faction faction)
        {
            var slots = LobbyConfig.Slots;
            int i = (int)faction;
            return slots != null && i >= 0 && i < slots.Length && slots[i] != null
                   && slots[i].Type == SlotType.AI
                   && slots[i].AIDifficulty == LobbyAIDifficulty.Cheating;
        }

        public static bool AnyCheating()
        {
            var slots = LobbyConfig.Slots;
            if (slots == null) return false;
            for (int i = 0; i < slots.Length && i < Sides; i++)
                if (IsCheating((Faction)i)) return true;
            return false;
        }

        /// <summary>Income a cheater earns, with the fractions carried so a trickle of 1
        /// still pays its bonus. Money coming back or moving about is not income.</summary>
        public static Cost ScaleIncome(Faction faction, in Cost c, IncomeSource source)
        {
            if (source == IncomeSource.Refund || source == IncomeSource.Vault || source == IncomeSource.Grant) return c;
            if (!IsCheating(faction)) return c;
            if (_carryEpoch != SimCadence.Epoch)
            {
                _carryEpoch = SimCadence.Epoch;
                System.Array.Clear(_carry, 0, _carry.Length);
            }
            float m = AISimpleDifficultyConfig.I.cheatIncomeMultiplier;
            int f = (int)faction;
            var r = c;
            r.Supplies = Scale(c.Supplies, m, f, 0);
            r.Iron = Scale(c.Iron, m, f, 1);
            r.Veilstone = Scale(c.Veilstone, m, f, 2);
            r.Veilsteel = Scale(c.Veilsteel, m, f, 3);
            return r;
        }

        private static int Scale(int amount, float m, int faction, int resource)
        {
            if (amount <= 0) return amount;
            float exact = amount * m + _carry[faction, resource];
            int whole = (int)exact;
            _carry[faction, resource] = exact - whole;
            return whole;
        }

        /// <summary>A faction's population from its buildings.</summary>
        public static int ScalePopulation(Faction faction, int population)
            => IsCheating(faction)
                ? UnityEngine.Mathf.RoundToInt(population * AISimpleDifficultyConfig.I.cheatPopulationMultiplier)
                : population;
    }

    /// <summary>On a cheater's bank: PopulationSyncSystem is Burst-compiled and reads the
    /// population bonus from here rather than from managed code.</summary>
    public struct CheatPopulation : IComponentData
    {
        public float Multiplier;
    }

    /// <summary>Stamped once a cheater's unit or building has had its stat bonus.</summary>
    public struct CheatStatsApplied : IComponentData { }

    /// <summary>
    /// Gives each new unit and building of a cheating faction its stat bonus, once: health,
    /// damage, speed and sight. Research multiplies these same live values afterwards, so
    /// the bonus and research stack.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AICheatStatSystem : SystemBase
    {
        private static readonly ComponentType[] QT_New =
        {
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<CheatStatsApplied>(),
        };
        private static CachedEntityQuery QC_New;

        private static readonly ComponentType[] QT_Banks =
        {
            ComponentType.ReadOnly<TheWaningBorder.Economy.FactionPopulation>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.Exclude<CheatPopulation>(),
        };
        private static CachedEntityQuery QC_Banks;

        protected override void OnUpdate()
        {
            if (!AICheat.AnyCheating()) return;
            var em = EntityManager;
            using (var banks = QC_Banks.Get(em, QT_Banks).ToEntityArray(Allocator.Temp))
                foreach (var bank in banks)
                    if (AICheat.IsCheating(em.GetComponentData<FactionTag>(bank).Value))
                        em.AddComponentData(bank, new CheatPopulation
                        {
                            Multiplier = AISimpleDifficultyConfig.I.cheatPopulationMultiplier,
                        });

            using var ents = QC_New.Get(em, QT_New).ToEntityArray(Allocator.Temp);
            float m = AISimpleDifficultyConfig.I.cheatStatMultiplier;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                em.AddComponent<CheatStatsApplied>(e);
                if (!AICheat.IsCheating(em.GetComponentData<FactionTag>(e).Value)) continue;

                var hp = em.GetComponentData<Health>(e);
                hp.Max = UnityEngine.Mathf.RoundToInt(hp.Max * m);
                hp.Value = UnityEngine.Mathf.RoundToInt(hp.Value * m);
                em.SetComponentData(e, hp);
                if (em.HasComponent<Damage>(e))
                {
                    var d = em.GetComponentData<Damage>(e);
                    d.Value = UnityEngine.Mathf.RoundToInt(d.Value * m);
                    em.SetComponentData(e, d);
                }
                if (em.HasComponent<MoveSpeed>(e))
                {
                    var s = em.GetComponentData<MoveSpeed>(e);
                    s.Value *= m;
                    em.SetComponentData(e, s);
                }
                if (em.HasComponent<LineOfSight>(e))
                {
                    var l = em.GetComponentData<LineOfSight>(e);
                    l.Radius *= m;
                    em.SetComponentData(e, l);
                }
            }
        }
    }
}
