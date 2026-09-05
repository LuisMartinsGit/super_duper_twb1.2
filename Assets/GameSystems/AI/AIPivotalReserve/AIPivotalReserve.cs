// AIPivotalReserve.cs
// Savings ledger for the AI's pivotal one-off purchases (Temple levels,
// King's Court uniques). 2026-08-11 log-proven failure: banks held 9,700
// iron / 8,300 veilstone while SUPPLIES never exceeded ~250 — every
// trickle was instantly consumed by discretionary spending (sustained
// army growth, research sweeps, expansion buildings), so a 500-supply
// lump sum never formed. The Temple sat at L1 all match, no Scholar ever
// trained, and the entire ritual / victory path stayed locked.
//
// Contract: a blocked pivotal purchase registers its cost here; while any
// reserve is unfunded, discretionary spenders hold their spend for the
// tick. Floors (military/worker deficits), replacements and the hut
// income pipeline are exempt by design — saving up must never starve the
// economy that does the saving.
//
// Host-side AI state (statics), same as AIBudget. Entries are cleared by
// their owners on purchase or when the goal disappears.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    public static class AIPivotalReserve
    {
        #region Tuning

        /// <summary>Every number below used to be a const in this file;
        /// they live in AIPivotalReserve.asset now.</summary>
        static AIPivotalReserveConfig Cfg => AIPivotalReserveConfig.I;

        #endregion

        private static readonly Dictionary<(Faction faction, string key), Cost> _pending
            = new Dictionary<(Faction, string), Cost>();


        /// <summary>
        /// Drop every faction's reserves. Called once per match from
        /// AIBootstrap, exactly as AIBudget.Initialize is.
        ///
        /// Without it a match that ended with a reserve still armed — an
        /// unspent "SiegeYard", an age-up that never landed — carried it into
        /// the next match, where that faction silently refused to spend on
        /// anything discretionary until the goal it no longer had was met.
        /// </summary>
        public static void Initialize()
        {
            _pending.Clear();
            _holdSince.Clear();
        }

        /// <summary>Register (or refresh) a pending pivotal purchase.</summary>
        public static void Set(Faction faction, string key, Cost cost)
            => _pending[(faction, key)] = cost;

        /// <summary>Withdraw a pending purchase — call on success or when
        /// the goal no longer exists (unit alive, temple maxed...).</summary>
        public static void Clear(Faction faction, string key)
            => _pending.Remove((faction, key));

        /// <summary>Is this exact reserve armed? Lets a spender yield to one
        /// SPECIFIC savings goal — the hut pipeline yields to the Hall claim
        /// without also pausing for temple levels or heroes.</summary>
        public static bool Has(Faction faction, string key)
            => _pending.ContainsKey((faction, key));



        private static readonly Dictionary<Faction, float> _holdSince
            = new Dictionary<Faction, float>();

        /// <summary>True while this faction is saving toward pending
        /// pivotal purchases — discretionary spenders skip their spend
        /// this tick. False as soon as the bank covers the summed
        /// reserves plus <see cref="Pad"/>, or once the hold has run
        /// longer than <see cref="MaxHoldSeconds"/> without filling.</summary>
        public static bool ShouldHold(EntityManager em, Faction faction)
        {
            int s = 0, iron = 0, v = 0, vs = 0;
            bool any = false;
            foreach (var kv in _pending)
            {
                if (kv.Key.faction != faction) continue;
                any = true;
                s    += kv.Value.Supplies;
                iron += kv.Value.Iron;
                v    += kv.Value.Veilstone;
                vs   += kv.Value.Veilsteel;
            }
            if (!any) { _holdSince.Remove(faction); return false; }

            if (!FactionEconomy.TryGetResources(em, faction, out var res)) return false;
            bool shortfall = res.Supplies < s + Cfg.pad
                || res.Iron < iron + Cfg.pad
                || res.Veilstone < v + Cfg.pad
                || res.Veilsteel < vs;
            if (!shortfall) { _holdSince.Remove(faction); return false; }

            // Simulated time — this gates an AI spending decision, so it
            // must tick with the simulation, not the render loop.
            float now = TheWaningBorder.Core.SimClock.Now;
            if (!_holdSince.TryGetValue(faction, out float since))
            {
                _holdSince[faction] = now;
                return true;
            }

            // DUTY CYCLE, not a one-shot (2026-08-31). The old code returned
            // false FOREVER once a hold ran past the ceiling — _holdSince was
            // never reset while the reserve stayed pending, so a faction
            // whose first hold lapsed in the poor opening minutes spent the
            // whole match "saving" with every spender un-gated: 12 matches,
            // zero expansion Halls. Now the hold breathes: save for
            // MaxHoldSeconds, release for ReleaseSeconds (so a genuinely
            // starved faction still gets to spend on survival), then save
            // again — repeating until the lump sum lands or the reserve is
            // cleared.
            float phase = (now - since) % (Cfg.maxHoldSeconds + Cfg.releaseSeconds);
            return phase <= Cfg.maxHoldSeconds;
        }
    }
}
