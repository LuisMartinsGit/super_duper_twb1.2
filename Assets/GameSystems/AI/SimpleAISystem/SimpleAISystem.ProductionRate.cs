// SimpleAISystem.ProductionRate.cs
// THE PRODUCTION RATE GOVERNOR (decision 86, 2026-11-19; Game_AI.md § 6ah).
// Developer: "If I need a 100 unit army every 3-5 minutes, then I need to have
// the resources and production buildings to back it up. Is this assured? It
// has to be. This is the hardest AI, it must be overwhelming."
//
// Batch 18 measured the Expert AIs at 6 units a minute at minute 10, 13 at
// 15 and a ceiling of ~21 from minute 25, with 14-17 production buildings
// a third busy and 35-40 % of income going to units. The tier now carries a
// target curve (AIDifficultyProfileSO unitRateAt10Min / unitRateFrom20Min:
// a ramp from the match start to the first, linear to the second, flat
// after; 0 = no governor). Every think the governor measures the military
// orders of the last rateWindowSeconds; while that is below rateBehindShare
// of the target and the faction has population room:
//   * the budget's military weight is raised to at least rateDeficitMilWeight;
//   * every surplus spender (levels, research, Vault deposits) yields to the
//     army (AIBudget.SetRateDeficit -> ArmyFirstYield);
//   * an extra production building passes the saturation gate whenever the
//     bank can pay for it (ProductionGate).
// One RATE line a minute names the measured rate, the target and the verdict.
// Host-side AI state, match-relative clock (SimClock), reset per match.

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem
    {
        private static readonly Dictionary<int, Queue<float>> _unitOrders = new Dictionary<int, Queue<float>>();
        private static readonly Dictionary<int, bool> _rateBehind = new Dictionary<int, bool>();
        private static readonly Dictionary<int, float> _rateLogAt = new Dictionary<int, float>();
        private static int _rateEpoch = int.MinValue;

        private static void EnsureRateEpoch()
        {
            if (_rateEpoch == SimCadence.Epoch) return;
            _rateEpoch = SimCadence.Epoch;
            _unitOrders.Clear();
            _rateBehind.Clear();
            _rateLogAt.Clear();
        }

        /// <summary>One military unit ordered (TryTrainUnitBudgeted).</summary>
        private static void NoteUnitOrdered(Faction faction)
        {
            EnsureRateEpoch();
            int k = (int)faction;
            if (!_unitOrders.TryGetValue(k, out var q)) _unitOrders[k] = q = new Queue<float>();
            q.Enqueue((float)SimClock.Now);
        }

        /// <summary>Units a minute the tier wants at match second <paramref name="t"/>.</summary>
        private static float UnitRateTarget(in AIDifficultyProfile p, float t)
        {
            float a = p.UnitRateAt10Min, b = p.UnitRateFrom20Min;
            if (a <= 0f && b <= 0f) return 0f;
            float m = t / 60f;
            if (m <= 10f) return a * math.saturate(m / 10f);
            if (m >= 20f) return b;
            return math.lerp(a, b, (m - 10f) / 10f);
        }

        /// <summary>Is this faction's unit rate behind its tier's target?
        /// Valid after <see cref="TickProductionRate"/> ran this think.</summary>
        private static bool RateBehind(Faction faction)
        {
            EnsureRateEpoch();
            return _rateBehind.TryGetValue((int)faction, out bool b) && b;
        }

        /// <summary>Once per think: measure, decide, tell the budget, log a minute.</summary>
        private void TickProductionRate(EntityManager em, Faction faction, in AIDifficultyProfile profile)
        {
            EnsureRateEpoch();
            int k = (int)faction;
            float now = (float)SimClock.Now;
            float window = math.max(10f, Cfg.rateWindowSeconds);
            if (!_unitOrders.TryGetValue(k, out var q)) _unitOrders[k] = q = new Queue<float>();
            while (q.Count > 0 && q.Peek() < now - window) q.Dequeue();
            float rate = q.Count * 60f / window;
            float target = UnitRateTarget(profile, now);

            bool behind = false;
            string why = "no target";
            if (target > 0f)
            {
                PopulationHelper.TryGetFactionPopulation(faction, out int pop, out int cap);
                bool room = cap <= 0 || cap - pop > Cfg.waveFullPopSlack;
                behind = room && rate < target * math.saturate(Cfg.rateBehindShare);
                why = !room ? "population full" : behind ? "behind: units first, extra production allowed" : "on target";
            }
            _rateBehind[k] = behind;
            AIBudget.SetRateDeficit(faction, behind);

            if (target > 0f && (!_rateLogAt.TryGetValue(k, out float at) || now >= at))
            {
                _rateLogAt[k] = now + 60f;
                AILogger.Log(faction, "RATE",
                    $"{rate:0.#} units/min ordered over {window:0}s, target {target:0.#}/min — {why} " +
                    $"(trainers {CountMilitaryTrainers(em, faction)})");
            }
        }
    }
}
