// SimpleAISystem.ArmyCap.cs
// THE ARMY CAP RISES WITH UNSPENT MONEY (2026-10-07, docs/Design/Game_AI.md § 6j).
//
// Developer: "Army cap raises if there are 1000 resources unspent." Measured
// before it: every survivor of a 90-minute match reached the tier's
// sustainArmyCap (120 on Normal) by minute 30 and stopped there while its
// supplies and iron sat at the 100,000 bank cap from minute 45 on — four
// equal armies, and no economy could turn into a stronger one.
//
// Now, every armyCapRaiseInterval seconds a faction whose unspent supplies +
// iron are at least armyCapRaiseThreshold gets armyCapRaiseStep more army
// cap, up to the population ceiling. The raise is kept (an army that grew
// into it is not disbanded when the money is spent on it).

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private readonly Dictionary<int, (int Bonus, float Next)> _armyCapBonus
            = new Dictionary<int, (int, float)>();
        private int _armyCapEpoch = int.MinValue;

        /// <summary>The faction's army-cap raise, re-read every
        /// armyCapRaiseInterval seconds against its unspent bank.</summary>
        private int ArmyCapBonus(EntityManager em, Faction faction, int baseCap, float now)
        {
            if (_armyCapEpoch != SimCadence.Epoch) { _armyCapEpoch = SimCadence.Epoch; _armyCapBonus.Clear(); }
            _armyCapBonus.TryGetValue((int)faction, out var st);
            if (Cfg.armyCapRaiseStep <= 0 || now < st.Next) return st.Bonus;
            st.Next = now + System.Math.Max(1f, Cfg.armyCapRaiseInterval);
            if (FactionEconomy.TryGetResources(em, faction, out var bank)
                && bank.Supplies + bank.Iron >= Cfg.armyCapRaiseThreshold
                && baseCap + st.Bonus < FactionPopulation.AbsoluteMax)
            {
                st.Bonus = System.Math.Min(st.Bonus + Cfg.armyCapRaiseStep, FactionPopulation.AbsoluteMax - baseCap);
                AILogger.Log(faction, "ARMY",
                    $"army cap raised to {baseCap + st.Bonus} ({bank.Supplies + bank.Iron} supplies + iron unspent)");
            }
            _armyCapBonus[(int)faction] = st;
            return st.Bonus;
        }
    }
}
