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
            // ORDER MATTERS, AND IT USED TO BE WRONG (2026-08-18).
            //
            // These were four independent `if`s, so the POSTURE clause
            // overwrote the advancement gate outright and a threatened AI
            // dropped to adv 0.15 no matter how overdue its age-up was. The
            // logged Expert reproduces exactly: gate on (0.60/0.25/0.15),
            // then Rebuild overwrote it (0.15/0.60/0.25), then starved added
            // 0.20 to eco (0.15/0.60/0.45), normalising to the 0.13/0.50/0.38
            // in its BUDGET line — advancement LAST while it was supposed to
            // be saving to advance.
            //
            // That inverted the whole difficulty ladder: aggressive, higher
            // tiers attack early (Expert from 180 s), lose their army, fall
            // into Rebuild, and so are precisely the AIs whose advancement
            // budget gets killed. Easy attacks last, stays in Develop, and
            // was the only tier that ever aged up.
            //
            // Now: posture sets the BASE, and the advancement push is applied
            // LAST as an override that nothing else can undo.

            bool threatened = posture == AIPosture.Defend || posture == AIPosture.Rebuild;

            // 1. Base split by posture. Opening is economy-leaning; a
            //    threatened AI rebuilds its army first.
            if (threatened) { adv = 0.10f; mil = 0.60f; eco = 0.30f; }
            else            { adv = 0.15f; mil = 0.35f; eco = 0.50f; }

            // 2. A supply famine buys more economy — but NEVER while the age
            //    up push is on. Advancing IS the cure for a poor economy
            //    (Age 1 is where the income multipliers live), so tilting to
            //    eco here funds the very worker/hut spending that keeps the
            //    bank empty.
            if (suppliesStarved && !advancementGateActive) eco += 0.15f;

            // 3. THE PUSH WINS. Applied last and unconditionally: while an
            //    age-up is pending this AI is saving, and no posture may
            //    quietly opt out of it. Under threat the army still gets an
            //    equal share so saving never means standing there unarmed.
            if (advancementGateActive)
            {
                adv = threatened ? 0.45f : 0.65f;
                mil = threatened ? 0.45f : 0.20f;
                eco = 0.10f;
            }

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

                // 1. Split this window's income by the weights.
                for (int c = 0; c < Categories; c++)
                    b.Wallets[c, r] += gross * weights[c];

                // 2. RECONCILE — THE INVARIANT (2026-08-18):
                //        wallet[adv] + wallet[mil] + wallet[eco] == bank
                //
                // The wallets are a PARTITION of the money the faction
                // actually has, not a set of independent allowances. Without
                // this step they drifted apart from the bank in both
                // directions: the per-wallet CAP silently deleted allocation,
                // and every bank-direct purchase (build-order steps, the
                // opening huts, scouts, heroes) debited the bank while
                // leaving the wallets untouched. The result was entitlement
                // that did not exist — a logged Expert held 391 supplies of
                // Advancement against a bank of 70, so its 210-supply Shrine
                // was unaffordable while its own budget said otherwise.
                //
                // Scaling to the real balance fixes both directions at once:
                // spending outside the budget shrinks every wallet in
                // proportion, and a wallet can never promise money the
                // faction does not hold. CanSpend therefore means what it
                // says, and no floor, reserve or pause is needed to make it
                // true.
                float sum = 0f;
                for (int c = 0; c < Categories; c++) sum += b.Wallets[c, r];

                float actual = bank[r];
                if (actual <= 0f)
                {
                    for (int c = 0; c < Categories; c++) b.Wallets[c, r] = 0f;
                }
                else if (sum <= 0.0001f)
                {
                    // Nothing allocated yet (or everything was spent): split
                    // what is on hand by the current weights.
                    for (int c = 0; c < Categories; c++)
                        b.Wallets[c, r] = actual * weights[c];
                }
                else
                {
                    float scale = actual / sum;
                    for (int c = 0; c < Categories; c++) b.Wallets[c, r] *= scale;
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
        /// <summary>
        /// Supplies currently allocated to one wallet. Used to turn the
        /// Advancement allocation into a REAL floor in the shared bank —
        /// see the note on <see cref="CanSpend"/>.
        /// </summary>
        public static int WalletSupplies(Faction faction, AIBudgetCategory cat)
        {
            var b = GetBrain(faction);
            return !b.Seeded ? 0 : (int)b.Wallets[(int)cat, 0];
        }

        /// <summary>
        /// Cover <paramref name="cost"/> from <paramref name="cat"/>, BORROWING
        /// from the other wallets when this one is short and they are flush.
        ///
        /// The wallets partition the bank, so a transfer between them moves no
        /// real money — the invariant (sum == bank) is untouched. What it
        /// prevents is the failure this budget kept producing: a faction
        /// sitting on money it was not allowed to use, because the allocation
        /// happened to sit in the wrong pocket. One logged AI banked 1,546
        /// supplies while its Advancement share was too small to buy a
        /// 210-supply Shrine.
        ///
        /// ADVANCEMENT NEVER LENDS. It is the strategic wallet: the age-up is
        /// a lump sum that only pays off once it completes, so letting the
        /// army raid it is precisely how a faction spends its future on
        /// another Barracks. Economy and Military lend freely — to each other
        /// and to Advancement.
        ///
        /// Returns false when even the whole bank cannot cover the cost, in
        /// which case nothing is moved.
        /// </summary>
        public static bool TryAfford(Faction faction, AIBudgetCategory cat, Cost cost)
        {
            var b = GetBrain(faction);
            if (!b.Seeded) return true;   // pre-allocator grace

            int c = (int)cat;
            var want = new float[Resources]
                { cost.Supplies, cost.Iron, cost.Veilstone, cost.Veilsteel };

            // Lenders, poorest-priority first. Advancement is absent by design.
            System.Span<int> lenders = stackalloc int[2];
            int lenderCount = 0;
            if (cat != AIBudgetCategory.EconomyExpansion)
                lenders[lenderCount++] = (int)AIBudgetCategory.EconomyExpansion;
            if (cat != AIBudgetCategory.Military)
                lenders[lenderCount++] = (int)AIBudgetCategory.Military;

            // Affordability first: never move anything for a purchase that
            // still cannot happen.
            for (int r = 0; r < Resources; r++)
            {
                if (want[r] <= b.Wallets[c, r]) continue;
                float available = b.Wallets[c, r];
                for (int i = 0; i < lenderCount; i++) available += b.Wallets[lenders[i], r];
                if (available < want[r]) return false;
            }

            // Move the shortfall.
            for (int r = 0; r < Resources; r++)
            {
                float shortfall = want[r] - b.Wallets[c, r];
                if (shortfall <= 0f) continue;
                for (int i = 0; i < lenderCount && shortfall > 0f; i++)
                {
                    int l = lenders[i];
                    float take = System.Math.Min(shortfall, b.Wallets[l, r]);
                    if (take <= 0f) continue;
                    b.Wallets[l, r] -= take;
                    b.Wallets[c, r] += take;
                    shortfall -= take;
                }
            }
            return true;
        }

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
