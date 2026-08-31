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

namespace TheWaningBorder.AI
{
    public static class AIPivotalReserve
    {
        private static readonly Dictionary<(Faction faction, string key), Cost> _pending
            = new Dictionary<(Faction, string), Cost>();

        /// <summary>Headroom above the summed reserve before discretionary
        /// spending resumes — the pivotal purchase must land before the
        /// next competitor drains the bank again.</summary>
        private const int Pad = 120;

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

        /// <summary>Longest continuous hold per faction. FAMINE RELEASE
        /// (2026-08-11, 67-min match): every iron deposit on the map ran
        /// dry by minute 30, the Temple-L3 reserve (350 iron) became
        /// unfillable, and the hold froze walls, towers, expansion and army
        /// growth for the remaining 37 minutes — a total economic deadlock.
        /// Saving only makes sense while income can actually fill the
        /// reserve; past this window the hold releases (the reserve entry
        /// stays, so the purchase still fires the moment it ever becomes
        /// affordable).</summary>
        // 90 -> 240 (2026-08-31): 90 s was shorter than the time territory
        // income needs to accumulate an expansion Hall (600 supplies), so
        // the hold always lapsed and the claim never happened — 12 matches,
        // zero Halls. With army training now pausing under the hold
        // (SimpleAISystem.Production), the bank fills in well under this
        // ceiling; the ceiling only catches a genuinely starved faction.
        private const float MaxHoldSeconds = 240f;

        /// <summary>Breathing window between holds — see the duty cycle in
        /// <see cref="ShouldHold"/>.</summary>
        private const float ReleaseSeconds = 60f;

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
            bool shortfall = res.Supplies < s + Pad
                || res.Iron < iron + Pad
                || res.Veilstone < v + Pad
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
            float phase = (now - since) % (MaxHoldSeconds + ReleaseSeconds);
            return phase <= MaxHoldSeconds;
        }
    }
}
