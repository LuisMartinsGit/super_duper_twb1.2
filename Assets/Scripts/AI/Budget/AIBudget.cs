// AIBudget.cs
// M-A of the manager architecture (docs/AI_Manager_Architecture.md):
// per-faction INCOME BUDGETS over the single real bank, plus the request
// bus the coming managers negotiate through.
//
//   * Three virtual wallets per faction — Advancement / Military /
//     EconomyExpansion. Each think tick the allocator measures gross
//     income (bank delta + recorded spends) and splits it by the current
//     BudgetPolicy weights. A spend center may only buy when its wallet
//     covers the cost (checked BEFORE the real purchase, recorded after),
//     so no layer can starve another — the structural cure for this
//     week's bug class (huts vs Barracks, replacements vs age-up).
//   * Wallets are HOST-SIDE bookkeeping only: the real bank and every
//     CommandRouter contract are untouched, so lockstep is unaffected.
//   * BudgetPolicy: situational weight table (CoH/AoE4 lesson) — postures
//     and gates shift the split; every weight is floored so no wallet
//     ever fully starves (SC2-bot reservation lesson).
//   * AIRequestBus: typed, prioritized, EXPIRING requests between the
//     future managers (M-C/M-D consumers). Present now so extraction
//     phases land on a stable API.
//
// State is static per-faction (the AI is host-authoritative, mirroring
// AILogger / FactionResearchState); Initialize() resets it per match.

using System.Collections.Generic;
using Unity.Entities;
using TheWaningBorder.Core;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    public enum AIBudgetCategory : byte
    {
        Advancement = 0,
        Military = 1,
        EconomyExpansion = 2,
    }

    public static class AIBudget
    {
        private const int Categories = 3;
        private const int Resources = 4; // Supplies, Iron, Veilstone, Veilsteel
        /// <summary>No weight below this — a wallet may be lean, never dead.</summary>
        private const float WeightFloor = 0.10f;
        /// <summary>Wallet cap ≈ this many seconds of that resource's income
        /// (windfalls must not let one category hoard forever).</summary>
        private const float WalletCapSeconds = 120f;
        private const float WalletCapMinimum = 500f;
        private const float LogInterval = 60f;

        private sealed class BrainBudget
        {
            public readonly float[,] Wallets = new float[Categories, Resources];
            public readonly float[] IncomeEma = new float[Resources]; // per second
            public readonly int[] LastBank = new int[Resources];
            public readonly float[] WindowSpends = new float[Resources];
            public bool Seeded;
            public float NextLog;
        }

        private static readonly Dictionary<Faction, BrainBudget> _brains = new();

        public static void Initialize() => _brains.Clear();

        // ─────────────────────────────────────────────────────────────
        // POLICY — situational weights (Advancement, Military, Economy)
        // ─────────────────────────────────────────────────────────────

        /// <summary>Weight vector for the situation. Inputs are things the
        /// brain already computes each tick. Floored + normalized.</summary>
        public static void EvaluateWeights(AIPosture posture, bool advancementGateActive,
            bool suppliesStarved, out float adv, out float mil, out float eco)
        {
            // Base: early/neutral split — economy-leaning like every
            // surveyed game's opening posture.
            adv = 0.25f; mil = 0.35f; eco = 0.40f;

            if (advancementGateActive) { adv = 0.60f; mil = 0.25f; eco = 0.15f; }
            if (posture == AIPosture.Defend || posture == AIPosture.Rebuild)
            { mil = 0.60f; eco = 0.25f; adv = 0.15f; }
            if (suppliesStarved) { eco += 0.20f; }

            // Floor + normalize.
            if (adv < WeightFloor) adv = WeightFloor;
            if (mil < WeightFloor) mil = WeightFloor;
            if (eco < WeightFloor) eco = WeightFloor;
            float sum = adv + mil + eco;
            adv /= sum; mil /= sum; eco /= sum;
        }

        // ─────────────────────────────────────────────────────────────
        // ALLOCATOR
        // ─────────────────────────────────────────────────────────────

        /// <summary>Measure income since the last tick and split it into the
        /// wallets by the given weights. Call once per brain think tick.</summary>
        public static void Tick(EntityManager em, Faction faction,
            float adv, float mil, float eco, float dt, float now)
        {
            if (!FactionEconomy.TryGetBank(em, faction, out var bankEntity)) return;
            var res = em.GetComponentData<FactionResources>(bankEntity);
            var b = GetBrain(faction);

            var bank = new int[Resources] { res.Supplies, res.Iron, res.Veilstone, res.Veilsteel };
            if (!b.Seeded)
            {
                // First sight of the bank: seed the sample, split the
                // starting stock once so the opener has spending room.
                for (int r = 0; r < Resources; r++)
                {
                    b.LastBank[r] = bank[r];
                    b.Wallets[(int)AIBudgetCategory.Advancement, r] = bank[r] * adv;
                    b.Wallets[(int)AIBudgetCategory.Military, r] = bank[r] * mil;
                    b.Wallets[(int)AIBudgetCategory.EconomyExpansion, r] = bank[r] * eco;
                }
                b.Seeded = true;
                return;
            }

            float[] weights = { adv, mil, eco };
            for (int r = 0; r < Resources; r++)
            {
                // Gross income = bank delta + everything the wallets spent
                // this window (spends reduced the bank but were income too).
                float gross = (bank[r] - b.LastBank[r]) + b.WindowSpends[r];
                if (gross < 0f) gross = 0f; // outside drains (damage refunds etc.) never go negative
                b.LastBank[r] = bank[r];
                b.WindowSpends[r] = 0f;

                if (dt > 0.01f)
                {
                    float perSecond = gross / dt;
                    b.IncomeEma[r] = b.IncomeEma[r] <= 0f
                        ? perSecond
                        : b.IncomeEma[r] * 0.9f + perSecond * 0.1f;
                }

                float cap = b.IncomeEma[r] * WalletCapSeconds;
                if (cap < WalletCapMinimum) cap = WalletCapMinimum;
                for (int c = 0; c < Categories; c++)
                {
                    b.Wallets[c, r] += gross * weights[c];
                    if (b.Wallets[c, r] > cap) b.Wallets[c, r] = cap;
                }
            }

            if (now >= b.NextLog)
            {
                b.NextLog = now + LogInterval;
                AILogger.Log(faction, "BUDGET",
                    $"w(adv/mil/eco)=({adv:0.00}/{mil:0.00}/{eco:0.00}) " +
                    $"S[{W(b, 0)} | {W(b, 1)} | {W(b, 2)}] " +
                    $"emaS={b.IncomeEma[0]:0.0}/s emaI={b.IncomeEma[1]:0.0}/s");
            }
        }

        private static string W(BrainBudget b, int c)
            => $"{(int)b.Wallets[c, 0]}s,{(int)b.Wallets[c, 1]}i,{(int)b.Wallets[c, 2]}v,{(int)b.Wallets[c, 3]}vs";

        // ─────────────────────────────────────────────────────────────
        // SPEND GATE
        // ─────────────────────────────────────────────────────────────

        /// <summary>True when the category's wallet covers the cost. Check
        /// BEFORE the real purchase attempt; on success call RecordSpend.</summary>
        public static bool CanSpend(Faction faction, AIBudgetCategory cat, Cost cost)
        {
            var b = GetBrain(faction);
            if (!b.Seeded) return true; // pre-allocator grace (first ticks)
            int c = (int)cat;
            return b.Wallets[c, 0] >= cost.Supplies
                && b.Wallets[c, 1] >= cost.Iron
                && b.Wallets[c, 2] >= cost.Veilstone
                && b.Wallets[c, 3] >= cost.Veilsteel;
        }

        public static void RecordSpend(Faction faction, AIBudgetCategory cat, Cost cost)
        {
            var b = GetBrain(faction);
            int c = (int)cat;
            b.Wallets[c, 0] -= cost.Supplies;
            b.Wallets[c, 1] -= cost.Iron;
            b.Wallets[c, 2] -= cost.Veilstone;
            b.Wallets[c, 3] -= cost.Veilsteel;
            b.WindowSpends[0] += cost.Supplies;
            b.WindowSpends[1] += cost.Iron;
            b.WindowSpends[2] += cost.Veilstone;
            b.WindowSpends[3] += cost.Veilsteel;
            for (int r = 0; r < Resources; r++)
                if (b.Wallets[c, r] < 0f) b.Wallets[c, r] = 0f;
        }

        private static BrainBudget GetBrain(Faction faction)
        {
            if (!_brains.TryGetValue(faction, out var b))
                _brains[faction] = b = new BrainBudget();
            return b;
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // REQUEST BUS (consumers arrive with M-C/M-D — API is stable now)
    // ─────────────────────────────────────────────────────────────────

    public enum AIManagerId : byte { Economy, Advancement, Military, Defender, Attacker }
    public enum AIRequestKind : byte { Resources, Housing, Builder, Troops, Production }
    public enum AIRequestPriority : byte { Normal = 0, High = 1, Critical = 2 }

    public struct AIRequest
    {
        public AIRequestKind Kind;
        public AIManagerId From;
        public AIManagerId To;
        public int Amount;
        public Unity.Collections.FixedString64Bytes What;
        public AIRequestPriority Priority;
        public float Expiry; // sim time; expired requests are pruned unfulfilled
    }

    /// <summary>Per-faction request queues. A request is "slipped into" the
    /// target manager's queue; the receiver fulfills it from its own wallet
    /// (that IS the negotiation) or lets it expire — and logs the denial.</summary>
    public static class AIRequestBus
    {
        private static readonly Dictionary<Faction, List<AIRequest>> _queues = new();

        public static void Initialize() => _queues.Clear();

        public static void Post(Faction faction, in AIRequest request)
        {
            if (!_queues.TryGetValue(faction, out var q))
                _queues[faction] = q = new List<AIRequest>(8);
            // Priority insert: Critical before High before Normal, FIFO within.
            int at = q.Count;
            for (int i = 0; i < q.Count; i++)
                if (q[i].To == request.To && q[i].Priority < request.Priority) { at = i; break; }
            q.Insert(at, request);
        }

        /// <summary>All live requests addressed to a manager, pruning expired
        /// ones (a DENIED log per expiry so starvation is always visible).</summary>
        public static void DrainFor(Faction faction, AIManagerId manager, float now,
            List<AIRequest> into)
        {
            into.Clear();
            if (!_queues.TryGetValue(faction, out var q)) return;
            for (int i = q.Count - 1; i >= 0; i--)
            {
                if (q[i].To != manager) continue;
                if (q[i].Expiry > 0f && now > q[i].Expiry)
                {
                    AILogger.Log(faction, "REQUEST-DENIED",
                        $"{q[i].From}->{q[i].To} {q[i].Kind} {q[i].What} x{q[i].Amount} expired");
                    q.RemoveAt(i);
                    continue;
                }
                into.Add(q[i]);
                q.RemoveAt(i);
            }
            into.Reverse(); // restore priority order after reverse iteration
        }
    }
}
