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
using TheWaningBorder.Core.Settings;

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
        #region Tuning

        /// <summary>Every number below used to be a const in this file;
        /// they live in AIBudget.asset now.</summary>
        static AIBudgetConfig Cfg => AIBudgetConfig.I;

        #endregion

        private const int Categories = 3;
        private const int Resources = 4; // Supplies, Iron, Veilstone, Veilsteel

        private sealed class BrainBudget
        {
            public readonly float[,] Wallets = new float[Categories, Resources];
            public readonly float[] IncomeEma = new float[Resources]; // per second
            public readonly int[] LastBank = new int[Resources];
            public readonly float[] WindowSpends = new float[Resources];
            public bool Seeded;
            public int ReservePriority;
            public float NextLog;

            /// <summary>Resources held back from every spender, so the brain
            /// can accumulate a lump sum it could never reach by opportunistic
            /// buying. Zero when not saving.</summary>
            public readonly float[] Reserved = new float[Resources];
            /// <summary>When the reservation lapses. A saving goal that never
            /// completes must not starve the faction forever.</summary>
            public float ReserveExpiry;

            /// <summary>Per resource: the simulated time until which the
            /// army counts as SHORT of it — refreshed by every military
            /// purchase the bank could not cover (see NoteMilitaryShort).</summary>
            public readonly float[] MilitaryShortUntil = new float[Resources];

            /// <summary>Veilstone EARMARKED for the army (see
            /// <see cref="SetMilitaryVeilstoneClaim"/>): a share of every
            /// window's veilstone income while the claim is on, debited by
            /// military purchases, never more than the bank holds.</summary>
            public float MilVeilCredit;
            public bool MilVeilClaim;

            /// <summary>ARMY FIRST (see SetArmyStatus): the army floor's
            /// last reading, stamped in simulated time.</summary>
            public int ArmyAlive, ArmyDesired;
            public Cost ArmyUnitCost;
            public bool ArmyCanAbsorb;
            public float ArmyStamp = -1f;
            /// <summary>Yields since the last ARMYFIRST log line, and when
            /// the next line may be written.</summary>
            public int ArmyYields;
            public float ArmyNextLog;
        }

        /// <summary>Resource indices for <see cref="IsMilitaryShort"/> — the
        /// wallet column order.</summary>
        public const int ResSupplies = 0, ResIron = 1, ResVeilstone = 2, ResVeilsteel = 3;

        private static readonly Dictionary<Faction, BrainBudget> _brains = new();

        public static void Initialize() => _brains.Clear();

        // ─────────────────────────────────────────────────────────────
        // POLICY — situational weights (Advancement, Military, Economy)
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Weight vector from the brain's committed PLAN — the strategic
        /// policy, replacing the situational one below for the SimpleAI.
        ///
        /// The situational version steered by posture, a supply-famine flag and
        /// an age-up gate. Those inputs are the same for everybody, so four AIs
        /// with five different personalities produced one identical game — and
        /// its age-up branch set adv/mil/eco to 0.65/0.20/0.10 "last and
        /// unconditionally", permanently, into a wallet that never lends. One
        /// logged AI sat on 6,113 supplies and 15,242 veilstone while its
        /// Military wallet held 22 iron against a 45-iron Swordsman.
        ///
        /// A plan's split is deliberate and TIME-BOXED by its commit window, so
        /// an advancement push is a phase rather than a life sentence.
        ///
        /// Posture still gets a say, because no plan survives being attacked at
        /// home: a threatened AI buys troops whatever it had intended.
        /// </summary>
        public static void EvaluateWeights(in AIPlanProfile plan, AIPosture posture,
            out float adv, out float mil, out float eco)
        {
            adv = plan.WeightAdv;
            mil = plan.WeightMil;
            eco = plan.WeightEco;

            if (posture == AIPosture.Defend || posture == AIPosture.Rebuild)
            {
                mil += 0.25f;
                adv *= 0.5f;
            }

            if (adv < Cfg.weightFloor) adv = Cfg.weightFloor;
            if (mil < Cfg.weightFloor) mil = Cfg.weightFloor;
            if (eco < Cfg.weightFloor) eco = Cfg.weightFloor;
            float total = adv + mil + eco;
            adv /= total; mil /= total; eco /= total;
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
                // THE ARMY'S VEILSTONE EARMARK (2026-10-03). See
                // SetMilitaryVeilstoneClaim: while the composition is behind
                // its targets, this share of the window's veilstone income is
                // held for the army — every other veilstone spender must leave
                // it in the bank (LeavesMilitaryVeilstone).
                if (r == ResVeilstone)
                {
                    if (b.MilVeilClaim)
                        b.MilVeilCredit += gross * Cfg.militaryVeilstoneShare;
                    else
                        b.MilVeilCredit = 0f;
                    float creditCap = System.Math.Min((float)bank[r], Cfg.militaryVeilstoneCreditCap);
                    if (b.MilVeilCredit > creditCap) b.MilVeilCredit = creditCap;
                    if (b.MilVeilCredit < 0f) b.MilVeilCredit = 0f;
                }

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
                b.NextLog = now + Cfg.logInterval;
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
            => TryAfford(faction, cat, cost, 0f, honourReservation: true);

        /// <param name="honourReservation">False for the spender the
        /// reservation was made FOR — otherwise the saving goal would be
        /// blocked by its own savings.</param>
        public static bool TryAfford(Faction faction, AIBudgetCategory cat, Cost cost,
            float now, bool honourReservation)
        {
            var b = GetBrain(faction);
            if (!b.Seeded) return true;   // pre-allocator grace

            int c = (int)cat;
            var want = new float[Resources]
                { cost.Supplies, cost.Iron, cost.Veilstone, cost.Veilsteel };

            // Lenders, in the order they are drained.
            //
            // ADVANCEMENT USED TO BE EXCLUDED "by design", to stop opportunistic
            // buying from eating the age-up savings. It stranded capital instead.
            // Measured mid-match: Blue held 333 supplies split
            // [Adv 199 | Mil 103 | Eco 31] and could not buy a 220-supply
            // Barracks, because Military could only reach 134 of its own faction's
            // money. Every faction in the match finished with ZERO military
            // buildings for the same reason, while Advancement also sat on 1,114
            // veilstone and 310 veilsteel it had no remaining use for — the age-up
            // is bought once, and the weight keeps feeding the wallet afterwards.
            //
            // Protecting a lump sum is what Reserve/ReservedAmount is FOR, and
            // TryAfford already subtracts a live reservation off the top. So
            // Advancement lends like any other wallet, and goes LAST so it is
            // touched only when the other two genuinely cannot cover the price.
            System.Span<int> lenders = stackalloc int[3];
            int lenderCount = 0;
            if (cat != AIBudgetCategory.EconomyExpansion)
                lenders[lenderCount++] = (int)AIBudgetCategory.EconomyExpansion;
            if (cat != AIBudgetCategory.Military)
                lenders[lenderCount++] = (int)AIBudgetCategory.Military;
            if (cat != AIBudgetCategory.Advancement)
                lenders[lenderCount++] = (int)AIBudgetCategory.Advancement;

            // Affordability first: never move anything for a purchase that
            // still cannot happen.
            //
            // A reservation comes off the TOP. The wallets partition the bank,
            // so holding a lump sum back has to be checked against the total
            // the purchase could reach — not against one pocket, which the
            // borrowing below would simply refill from the pot being saved.
            for (int r = 0; r < Resources; r++)
            {
                float held = honourReservation ? ReservedAmount(b, r, now) : 0f;
                float available = b.Wallets[c, r];
                for (int i = 0; i < lenderCount; i++) available += b.Wallets[lenders[i], r];
                available -= held;
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

        /// <summary>
        /// SAVE FOR A LUMP SUM. Holds <paramref name="cost"/> back from every
        /// other spender until <see cref="ClearReservation"/> or the deadline.
        ///
        /// Opportunistic buying cannot reach a big-ticket item. Measured over a
        /// 14-minute four-AI match: supplies oscillated between 30 and 748 with
        /// a mean near 250, because every spender bought whatever it could
        /// afford the moment it could afford it. A 600-supply Hall was
        /// therefore unreachable — not once did any faction's bank sit high
        /// enough at the instant the claim check ran, while iron piled to
        /// 2,000+ unspent. Expanding is a DECISION, and a decision means
        /// committing income to it instead of hoping for a windfall.
        ///
        /// The reservation is subtracted from what TryAfford will lend or
        /// spend, so the pot fills instead of leaking. It ALWAYS expires: a
        /// goal that cannot complete has to release its hold, or the faction
        /// stalls into "banking for a building it will never buy" — the same
        /// failure the wallet floors exist to prevent.
        /// </summary>
        /// <param name="priority">Higher wins. There is ONE reservation slot per
        /// faction, so two savers thrash over it and neither pot ever fills:
        /// the Hall claim re-arms every 90 s, which meant an age-up saving
        /// alongside it was overwritten on the very next tick. A live
        /// reservation can only be replaced by one of equal or higher
        /// priority. Age-up is 1 because it unlocks the culture, three
        /// production lines and every combat technology; claiming one more
        /// region is 0.</param>
        public static void Reserve(Faction faction, Cost cost, float now, float holdSeconds,
            int priority = 0)
        {
            var b = GetBrain(faction);
            if (now < b.ReserveExpiry && priority < b.ReservePriority) return;
            b.ReservePriority = priority;
            b.Reserved[0] = cost.Supplies;
            b.Reserved[1] = cost.Iron;
            b.Reserved[2] = cost.Veilstone;
            b.Reserved[3] = cost.Veilsteel;
            b.ReserveExpiry = now + holdSeconds;
        }

        /// <summary>Release the hold — the purchase happened, or the goal is
        /// gone.</summary>
        /// <param name="maxPriority">Only clear a hold at or below this
        /// priority. A caller that owns a priority-0 hold must not destroy
        /// the age-up's priority-1 savings; int.MaxValue (the default) keeps
        /// the old clear-anything behaviour for the purchase-completed
        /// paths.</param>
        public static void ClearReservation(Faction faction, int maxPriority = int.MaxValue)
        {
            var b = GetBrain(faction);
            if (b.ReservePriority > maxPriority) return;
            for (int r = 0; r < Resources; r++) b.Reserved[r] = 0f;
            b.ReserveExpiry = 0f;
            b.ReservePriority = 0;
        }

        /// <summary>True while a reservation is being held for this faction.</summary>
        public static bool IsReserving(Faction faction, float now)
        {
            var b = GetBrain(faction);
            if (now >= b.ReserveExpiry) return false;
            for (int r = 0; r < Resources; r++) if (b.Reserved[r] > 0f) return true;
            return false;
        }

        /// <summary>
        /// Working capital the reservation may never touch, per resource. The
        /// brain must be able to keep training workers and raising huts while
        /// it saves.
        ///
        /// Supplies are sized at a worker (140) plus a hut (80) plus slack:
        /// below that the opening build order simply stops.
        /// </summary>
        private static readonly float[] WorkingFloat = { 120f, 40f, 0f, 0f };
        // 260/120 was sized against a 140-supply Worker plus an 80-supply Hut.
        // Both got cheaper, and more importantly the number was ABOVE the bank:
        // measured across a full match the supply bank sat at 15-50 the entire
        // time, because every goal spends supplies the instant they land. Since
        // only the surplus ABOVE this float is ever held, a float of 260 made
        // every reservation inert -- the Hall claim logged "saving for Region N"
        // 58 times and the pot never grew by a single supply.
        //
        // At 120 the hold engages: spenders are refused once the bank minus 120
        // is spoken for, so the bank ratchets up instead of being drained flat.
        // 120 still covers a Hut plus a Worker, which is what "keep operating
        // while you save" has to mean.

        /// <summary>
        /// How much of resource r is held back right now.
        ///
        /// ONLY THE SURPLUS IS HELD. A flat hold of the full goal price starves
        /// the faction outright: saving 600 supplies for a Hall out of a bank
        /// of 526 left NEGATIVE availability, so TryAfford refused everything —
        /// including the 140-supply Worker at build-order step 1, which then
        /// timed out at 92 s, twice, for every faction in the match. The claim
        /// takes about three minutes to afford, so a 90-second hold that
        /// re-arms on a 12-second cooldown is effectively continuous through
        /// the whole opening.
        ///
        /// Holding only what sits above the working float still fills the pot —
        /// income above operating needs accumulates exactly as before — while
        /// the economy keeps running underneath it. Lapsed reservations hold
        /// nothing.
        /// </summary>
        private static float ReservedAmount(BrainBudget b, int r, float now)
        {
            if (now >= b.ReserveExpiry || b.Reserved[r] <= 0f) return 0f;

            float bank = 0f;
            for (int c = 0; c < Categories; c++) bank += b.Wallets[c, r];
            float spare = bank - (r < WorkingFloat.Length ? WorkingFloat[r] : 0f);
            if (spare <= 0f) return 0f;
            return System.Math.Min(b.Reserved[r], spare);
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
            // A military purchase spends the army's own earmark first.
            if (cat == AIBudgetCategory.Military) DebitMilitaryVeilstone(faction, cost.Veilstone);
        }

        // ─────────────────────────────────────────────────────────────
        // THE ARMY'S VEILSTONE EARMARK (2026-10-03)
        // ─────────────────────────────────────────────────────────────
        //
        // Every Alanthor Age 1 unit is priced in veilstone (Swordsman 55,
        // Outrider 93, Cataphract 189, Catapult 121, King Lexor 350), and the
        // only income is the Trading Outposts' Buy recipe — 65 a minute each
        // at the time (100 since the same day, faster with Swift Caravans). The 0.0.33 60-minute batch: Fortress savings (300 + 100
        // reserve + pad), the research sweep, sect units and the endgame's own
        // cavalry picker all drank from that trickle, the composition picker
        // passed every veilstone unit over while the Fortress hold was short
        // of veilstone, and the army filled with the two veilstone-free
        // basics until population capped.
        //
        // The wallets cannot fix this: Military already borrows from both
        // other wallets in TryAfford. What it lacked was PRIORITY — a slice of
        // the veilstone income nobody else may spend. While the claim is on,
        // militaryVeilstoneShare of every window's veilstone income is
        // credited to the army (capped at militaryVeilstoneCreditCap and at
        // the bank). Military purchases debit it; every non-military
        // veilstone spender (buildings — the Fortress included — and
        // research) must leave it in the bank; and the pivotal savings hold
        // judges a military purchase only on the veilstone the credit does
        // not cover. The rest of the income still funds the Fortress.

        /// <summary>Turn the army's veilstone claim on or off. The
        /// composition layer sets it every think: on while a ladder line is
        /// behind its target (or King Lexor is owed) and there is
        /// population to spawn into.</summary>
        public static void SetMilitaryVeilstoneClaim(Faction faction, bool active)
            => GetBrain(faction).MilVeilClaim = active;

        /// <summary>Veilstone currently earmarked for the army.</summary>
        public static int MilitaryVeilstoneCredit(Faction faction)
        {
            var b = GetBrain(faction);
            return b.MilVeilClaim ? (int)b.MilVeilCredit : 0;
        }

        /// <summary>A military veilstone purchase made outside RecordSpend
        /// (the capital uniques) spends the earmark too.</summary>
        public static void DebitMilitaryVeilstone(Faction faction, int veilstone)
        {
            if (veilstone <= 0) return;
            var b = GetBrain(faction);
            b.MilVeilCredit -= veilstone;
            if (b.MilVeilCredit < 0f) b.MilVeilCredit = 0f;
        }

        /// <summary>True when a NON-military purchase leaves the army's
        /// earmarked veilstone in the bank (always true for a purchase that
        /// costs no veilstone, or while no claim is on).</summary>
        public static bool LeavesMilitaryVeilstone(EntityManager em, Faction faction, Cost cost)
        {
            if (cost.Veilstone <= 0) return true;
            int credit = MilitaryVeilstoneCredit(faction);
            if (credit <= 0) return true;
            if (!FactionEconomy.TryGetResources(em, faction, out var res)) return true;
            return res.Veilstone - cost.Veilstone >= credit;
        }

        // ─────────────────────────────────────────────────────────────
        // MILITARY SHORTAGE — the signal other spenders yield to
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// A military purchase was refused: record WHICH resources the bank
        /// could not cover, so the rest of the brain can act on it instead of
        /// the refusal vanishing (docs: the AI silent-failure pattern).
        ///
        /// Measured 2026-10-03 (0.0.33, 60-minute batch): every Alanthor AI
        /// sat on 100,000 supplies and 30,000 iron with 10-430 veilstone, its
        /// army at 2-80 of 300, while the floor logged "Military budget short
        /// for Alanthor_Catapult" ~800 times — and nothing else in the brain
        /// knew veilstone was the wall. Readers: the wall doctrine (yields
        /// while the army is short of supplies or iron), the Trading Outposts
        /// (stay on Buy while it is short of veilstone) and the claim picker
        /// (favours outcrops while it is short of veilstone).
        ///
        /// Simulated time (SimClock), so the record ticks with the match.
        /// Host-side AI state only; nothing here touches the real bank.
        /// </summary>
        public static void NoteMilitaryShort(EntityManager em, Faction faction, Cost cost)
        {
            if (!FactionEconomy.TryGetResources(em, faction, out var res)) return;
            var b = GetBrain(faction);
            float until = SimClock.Now + Cfg.militaryShortHoldSeconds;
            if (res.Supplies  < cost.Supplies)  b.MilitaryShortUntil[ResSupplies]  = until;
            if (res.Iron      < cost.Iron)      b.MilitaryShortUntil[ResIron]      = until;
            if (res.Veilstone < cost.Veilstone) b.MilitaryShortUntil[ResVeilstone] = until;
            if (res.Veilsteel < cost.Veilsteel) b.MilitaryShortUntil[ResVeilsteel] = until;
        }

        /// <summary>True while the army's last refused purchases were short
        /// of this resource (one of the Res* indices).</summary>
        public static bool IsMilitaryShort(Faction faction, int resource)
        {
            if (resource < 0 || resource >= Resources) return false;
            var b = GetBrain(faction);
            return SimClock.Now < b.MilitaryShortUntil[resource];
        }

        // ─────────────────────────────────────────────────────────────
        // SURPLUS (2026-10-04, docs/Design/Game_AI.md 5e)
        // ─────────────────────────────────────────────────────────────
        //
        // The basics cap is hard: an army waiting on veilstone never turns
        // idle supplies and iron into more Spearmen. These two reads are the
        // trigger every surplus spender shares (the extractor walk, the claim
        // and Fortress pickers, the research sweep, AIBuildingUpgradeSystem).
        // Bank reads only — the same answer for every caller in a think.

        /// <summary>The bank holds at least surplusSupplies supplies AND
        /// surplusIron iron. False when either threshold is 0.</summary>
        public static bool BankOverflowing(EntityManager em, Faction faction)
        {
            if (Cfg.surplusSupplies <= 0 || Cfg.surplusIron <= 0) return false;
            if (!FactionEconomy.TryGetResources(em, faction, out var res)) return false;
            return res.Supplies >= Cfg.surplusSupplies && res.Iron >= Cfg.surplusIron;
        }

        /// <summary>Overflowing AND the army is short of veilstone: the
        /// surplus goes to veilstone (Outposts, their research, outcrop
        /// ground) and, if spent on anything else, never on veilstone.</summary>
        public static bool VeilstoneHeldSurplus(EntityManager em, Faction faction)
            => IsMilitaryShort(faction, ResVeilstone) && BankOverflowing(em, faction);

        // ─────────────────────────────────────────────────────────────
        // ARMY FIRST (2026-10-04, docs/Design/Game_AI.md 5f)
        // ─────────────────────────────────────────────────────────────
        //
        // Headless28, spending per faction-minute: units 542, buildings 537,
        // building levels 489, trade 258, research 245 -- and levels were
        // 560k supplies + 153k iron across the batch against the army's
        // 175k + 190k, while iron fell to ~1,900. The surplus spenders (the
        // non-production building levels, the research sweep) bought ahead
        // of an army far below its target. They now yield to it:
        //
        //   * army at target (alive + queued >= armyFirstTargetFraction x
        //     desired), or unable to take the bank (every trainer full,
        //     population capped, no trainer standing), or no reading in the
        //     last armyFirstStatusMaxAge seconds: nothing yields;
        //   * the army can buy its next unit (it has not been refused for
        //     want of any resource lately): the spend yields outright;
        //   * the army is waiting on a resource (veilstone, usually): a spend
        //     that costs that resource yields; any other spend must leave
        //     armyFirstReserveUnits of the army's next unit in the bank for
        //     the resources it does cost -- the units are not affordable, so
        //     the rest of the bank may go, but not the part that buys the
        //     army the moment the veilstone lands.
        //
        // Host-side AI state; simulated time; keyed lookups only.

        /// <summary>The army floor's reading, written every think by
        /// SimpleAISystem.ReplaceLostUnits. <paramref name="canAbsorb"/> is
        /// false when the army could not take more money right now (every
        /// trainer's queue full, population capped, no trainer).</summary>
        public static void SetArmyStatus(Faction faction, int aliveAndQueued, int desired,
            Cost nextUnitCost, bool canAbsorb)
        {
            var b = GetBrain(faction);
            b.ArmyAlive = aliveAndQueued;
            b.ArmyDesired = desired;
            b.ArmyUnitCost = nextUnitCost;
            b.ArmyCanAbsorb = canAbsorb;
            b.ArmyStamp = SimClock.Now;
        }

        /// <summary>The army's desired size from the floor's latest reading
        /// (SetArmyStatus); false when there is none or it is older than
        /// armyFirstStatusMaxAge. Read by the production saturation rule
        /// (Game_AI.md 5g): an extra production building needs an army below
        /// its target.</summary>
        public static bool TryGetArmyDesired(Faction faction, out int desired)
        {
            var b = GetBrain(faction);
            desired = b.ArmyDesired;
            return b.ArmyStamp >= 0f && SimClock.Now - b.ArmyStamp <= Cfg.armyFirstStatusMaxAge;
        }

        /// <summary>
        /// Null when this surplus spend may go; otherwise why it yields to the
        /// army (see the block comment above). The caller logs through
        /// <see cref="NoteArmyFirstYield"/>.
        /// </summary>
        public static string ArmyFirstYield(EntityManager em, Faction faction, Cost spend)
        {
            if (Cfg.armyFirstTargetFraction <= 0f) return null;
            var b = GetBrain(faction);
            if (b.ArmyStamp < 0f || SimClock.Now - b.ArmyStamp > Cfg.armyFirstStatusMaxAge) return null;
            if (b.ArmyDesired <= 0 || !b.ArmyCanAbsorb) return null;
            if (b.ArmyAlive >= b.ArmyDesired * Cfg.armyFirstTargetFraction) return null;

            bool shortS = IsMilitaryShort(faction, ResSupplies);
            bool shortI = IsMilitaryShort(faction, ResIron);
            bool shortV = IsMilitaryShort(faction, ResVeilstone);
            bool shortVs = IsMilitaryShort(faction, ResVeilsteel);
            if (!(shortS || shortI || shortV || shortVs))
                return AILogger.Enabled
                    ? $"army {b.ArmyAlive}/{b.ArmyDesired} and its units affordable" : "army can buy";

            if ((spend.Supplies > 0 && shortS) || (spend.Iron > 0 && shortI)
                || (spend.Veilstone > 0 && shortV) || (spend.Veilsteel > 0 && shortVs))
                return AILogger.Enabled
                    ? $"army {b.ArmyAlive}/{b.ArmyDesired} short of what this costs" : "army short";

            int n = System.Math.Max(0, System.Math.Min(Cfg.armyFirstReserveUnits,
                b.ArmyDesired - b.ArmyAlive));
            if (n == 0) return null;
            if (!FactionEconomy.TryGetResources(em, faction, out var res)) return null;
            var u = b.ArmyUnitCost;
            if ((spend.Supplies  > 0 && res.Supplies  - spend.Supplies  < u.Supplies  * n)
             || (spend.Iron      > 0 && res.Iron      - spend.Iron      < u.Iron      * n)
             || (spend.Veilstone > 0 && res.Veilstone - spend.Veilstone < u.Veilstone * n)
             || (spend.Veilsteel > 0 && res.Veilsteel - spend.Veilsteel < u.Veilsteel * n))
                return AILogger.Enabled
                    ? $"army {b.ArmyAlive}/{b.ArmyDesired}: keeps {n} units' worth banked" : "army reserve";
            return null;
        }

        /// <summary>"ARMYFIRST: &lt;what&gt; yields — &lt;reason&gt; (n yields
        /// since the last line)", at most once per armyFirstLogInterval per
        /// faction.</summary>
        public static void NoteArmyFirstYield(Faction faction, string what, string reason)
        {
            var b = GetBrain(faction);
            b.ArmyYields++;
            if (!AILogger.Enabled) return;
            float now = SimClock.Now;
            if (now < b.ArmyNextLog) return;
            b.ArmyNextLog = now + Cfg.armyFirstLogInterval;
            AILogger.Log(faction, "ARMYFIRST",
                $"{what} yields — {reason} ({b.ArmyYields} yield(s) since the last line)");
            b.ArmyYields = 0;
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
    public enum AIRequestKind : byte { Resources, Housing, Worker, Troops, Production }
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
