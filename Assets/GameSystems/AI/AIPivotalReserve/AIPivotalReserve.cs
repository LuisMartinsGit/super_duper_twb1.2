// AIPivotalReserve.cs
// Savings ledger for the AI's pivotal one-off purchases (capital uniques,
// territory claims). 2026-08-11 log-proven failure: banks held 9,700
// iron / 8,300 veilstone while SUPPLIES never exceeded ~250 — every
// trickle was instantly consumed by discretionary spending (sustained
// army growth, research sweeps, expansion buildings), so a 500-supply
// lump sum never formed.
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
            _strict.Clear();
        }

        /// <summary>Reserves that do not breathe: while one is unfunded the
        /// hold stays on, with no release window (the age-up landmark,
        /// docs/Design/Age_0.md § The AI and the age-up).</summary>
        private static readonly HashSet<(Faction faction, string key)> _strict
            = new HashSet<(Faction, string)>();

        /// <summary>Register (or refresh) a pending pivotal purchase.
        /// <paramref name="strict"/> keeps the hold on until it is funded.</summary>
        public static void Set(Faction faction, string key, Cost cost, bool strict = false)
        {
            _pending[(faction, key)] = cost;
            if (strict) _strict.Add((faction, key)); else _strict.Remove((faction, key));
        }

        /// <summary>Withdraw a pending purchase — call on success or when
        /// the goal no longer exists (unit alive, temple maxed...).</summary>
        public static void Clear(Faction faction, string key)
        {
            _pending.Remove((faction, key));
            _strict.Remove((faction, key));
        }

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
        /// longer than <see cref="MaxHoldSeconds"/> without filling.
        /// RESOURCE-BLIND: use the <see cref="Cost"/> overload wherever
        /// the price of the purchase being gated is known.</summary>
        public static bool ShouldHold(EntityManager em, Faction faction)
            => HoldActive(em, faction, out _);

        /// <summary>
        /// RESOURCE-AWARE hold (2026-10-03): true only while the hold is on
        /// (same shortfall, strict and duty-cycle rules as the blind
        /// overload) AND <paramref name="purchase"/> spends a resource that
        /// is short against the pending reserves. The Fortress save made the
        /// blind hold permanent: veilstone (the scarce resource) never
        /// filled, so every spender froze while supplies and iron sat at the
        /// bank cap — median army 106 to 47 over a 60-minute batch. A
        /// purchase that touches none of the short resources cannot delay
        /// the save, so it passes.
        /// </summary>
        public static bool ShouldHold(EntityManager em, Faction faction, Cost purchase)
        {
            if (!HoldActive(em, faction, out var shortSet)) return false;
            return (purchase.Supplies  > 0 && shortSet.Supplies)
                || (purchase.Iron      > 0 && shortSet.Iron)
                || (purchase.Veilstone > 0 && shortSet.Veilstone)
                || (purchase.Veilsteel > 0 && shortSet.Veilsteel);
        }

        /// <summary>Which resources the bank is short on against this
        /// faction's summed pending reserves.</summary>
        public struct ShortSet
        {
            public bool Supplies, Iron, Veilstone, Veilsteel;
            public bool Any => Supplies || Iron || Veilstone || Veilsteel;

            /// <summary>Short resource names, for refusal logs.</summary>
            public override string ToString()
            {
                string s = "";
                if (Supplies)  s += "supplies ";
                if (Iron)      s += "iron ";
                if (Veilstone) s += "veilstone ";
                if (Veilsteel) s += "veilsteel ";
                return s.TrimEnd();
            }
        }

        /// <summary>The resources currently short against the pending
        /// reserves (no duty cycle applied) — for refusal logs.</summary>
        public static ShortSet ShortResources(EntityManager em, Faction faction)
        {
            ComputeShort(em, faction, out var set, out _);
            return set;
        }

        /// <summary>Summed reserves vs bank, per resource (veilsteel
        /// without the pad). False when nothing is pending or the bank is
        /// unreadable.</summary>
        private static bool ComputeShort(EntityManager em, Faction faction,
            out ShortSet set, out bool strict)
        {
            set = default;
            strict = false;
            int s = 0, iron = 0, v = 0, vs = 0;
            bool any = false;
            foreach (var kv in _pending)
            {
                if (kv.Key.faction != faction) continue;
                any = true;
                if (_strict.Contains(kv.Key)) strict = true;
                s    += kv.Value.Supplies;
                iron += kv.Value.Iron;
                v    += kv.Value.Veilstone;
                vs   += kv.Value.Veilsteel;
            }
            if (!any) return false;
            if (!FactionEconomy.TryGetResources(em, faction, out var res)) return false;
            set.Supplies  = res.Supplies  < s + Cfg.pad;
            set.Iron      = res.Iron      < iron + Cfg.pad;
            set.Veilstone = res.Veilstone < v + Cfg.pad;
            set.Veilsteel = res.Veilsteel < vs;
            return true;
        }

        /// <summary>The hold verdict both overloads share: shortfall on any
        /// resource, then strict / duty cycle. <paramref name="shortSet"/>
        /// names the short resources.</summary>
        private static bool HoldActive(EntityManager em, Faction faction, out ShortSet shortSet)
        {
            bool pendingAny = false;
            foreach (var kv in _pending)
                if (kv.Key.faction == faction) { pendingAny = true; break; }
            if (!pendingAny) { _holdSince.Remove(faction); shortSet = default; return false; }

            if (!ComputeShort(em, faction, out shortSet, out bool strict)) return false;
            if (!shortSet.Any) { _holdSince.Remove(faction); return false; }
            if (strict) return true;   // no duty cycle while a strict goal is unpaid

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
