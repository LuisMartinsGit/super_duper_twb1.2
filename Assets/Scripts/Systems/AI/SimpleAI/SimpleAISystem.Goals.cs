// SimpleAISystem.Goals.cs
// The decision layer: a PRIORITY LIST, not a script.
// Partial of SimpleAISystem.cs.
//
// ─────────────────────────────────────────────────────────────────────────
// WHY THE BUILD ORDER WAS REPLACED
//
// The AI ran a STRICT SEQUENCE. Step N had to succeed before step N+1 was even
// considered, and a step that could not be paid for sat there burning a
// 92-second timeout before being skipped. Meanwhile a dozen standing checks —
// housing, gatherer's huts, territory claims, extractors, army floors, the
// age-up director — spent from the same three wallets with no coordination.
//
// So the failure was structural and it recurred no matter what was fixed:
//
//   * one unaffordable step froze everything behind it, including the Barracks
//   * measured: 31 "wallet short" and 15 "bank short" refusals in ten minutes,
//     29 of them the opening's own TrainUnit:Worker
//   * a faction sat on 330 supplies, 2,388 iron and 3,950 veilstone at
//     population 3 of 190 — able to buy anything, building nothing
//   * five separate blockers were found and fixed in this chain, and each fix
//     revealed the next one behind it
//
// A sequence cannot express "do whatever is most valuable that I can actually
// afford right now", which is the only sane behaviour when money arrives
// unevenly. So the sequence is gone.
//
// HOW THIS WORKS
//
// Every tick, walk an ordered list of GOALS. Each goal knows what it wants,
// how many, what it needs first, and which wallet pays. Take the FIRST goal
// that is unmet, unlocked and affordable, and act on it. If nothing is
// affordable, do nothing this tick and try again next — no timeout, no skip,
// no head-of-line blocking, and the AI is never idle while it has money for
// something further down the list.
//
// Priority order IS the strategy. The plan layer (SimpleAISystem.Plan.cs)
// reweights it: MASSING lifts the military goals, BOOMING lifts expansion.
// ─────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Data;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        /// <summary>How long an age-up hold stands before it must re-arm.</summary>
        private const float AgeUpSaveSeconds = 90f;

        /// <summary>Outranks the Hall claim's hold (0) for the single
        /// reservation slot.</summary>
        private const int AgeUpReservePriority = 1;

        /// <summary>The choice building that unlocks the age-up.</summary>
        private const string AgeUpGateBuilding = "ShrineOfRidan";

        private enum GoalKind : byte { Build, Train, AgeUp }

        private readonly struct Goal
        {
            public readonly GoalKind Kind;
            public readonly string Id;
            public readonly int Want;             // desired count of this thing
            public readonly int Have;             // what we have now
            public readonly AIBudgetCategory Cat;
            public readonly string Why;

            public Goal(GoalKind kind, string id, int want, int have,
                        AIBudgetCategory cat, string why)
            { Kind = kind; Id = id; Want = want; Have = have; Cat = cat; Why = why; }

            public bool Unmet => Have < Want;
        }

        private readonly Dictionary<int, float> _nextGoalLog = new Dictionary<int, float>();

        /// <summary>Why each building id was last refused — fills the goal log.</summary>
        private readonly Dictionary<string, string> _lastBuildReason = new Dictionary<string, string>();

        /// <summary>
        /// Decide and act. Replaces the build-order stepper.
        ///
        /// One action per tick on purpose: the think interval is short, so the
        /// AI still fires steadily, and taking one decision at a time keeps a
        /// single expensive goal from draining a wallet three other goals were
        /// about to use.
        /// </summary>
        private void TickGoals(EntityManager em, Entity brainEntity, AIBrain brain,
            ref SimpleAIState aiState, AISettingsSO settings,
            AISettingsSO.PersonalityBlock personality, AIDifficultyProfile profile, float now)
        {
            var faction = brain.Owner;
            var plan = PlanProfileOf(faction);
            int era = FactionEra(em, faction);
            bool aged = era >= 2;

            PopulationHelper.TryGetFactionPopulation(faction, out int pop, out int popMax);
            int workers = CountAliveMiners(em, faction);   // Workers; the name predates gathering being removed
            int army = CountAliveMilitary(em, faction);

            int armyWant = math.max(1, (int)math.round(profile.SustainArmyCap * plan.ArmyScale));
            int workerWant = math.clamp(personality.minerFloor, 2, 5);
            int perKind = math.max(2, profile.ProductionBuildingTarget / (aged ? 4 : 2));

            var goals = new List<Goal>(24);

            // ── 1. NEVER BE POPULATION-BLOCKED. ──
            // Housing first when it is actually about to stop production;
            // everything else is worthless if nothing can be trained.
            if (popMax < FactionPopulation.AbsoluteMax && popMax - pop <= PopulationHeadroomFloor)
                goals.Add(new Goal(GoalKind.Build, "Hut", 1, 0,
                    AIBudgetCategory.EconomyExpansion, "population blocked"));

            // ── 2. A SMALL BUILD CREW. ──
            // Workers only build now (Regions.md §4), so this is a crew, not an
            // economy — it was 45% of every unit produced before.
            goals.Add(new Goal(GoalKind.Train, "Worker", workerWant, workers,
                AIBudgetCategory.EconomyExpansion, "build crew"));

            // ── 3. SOMEWHERE TO TRAIN SOLDIERS. ──
            // The single most valuable building in the game and the one the AI
            // most often finished a match without.
            goals.Add(new Goal(GoalKind.Build, "Barracks", 1,
                CountFactionBuildings<BarracksTag>(em, faction),
                AIBudgetCategory.Military, "first barracks"));

            // ── 4. INCOME. ──
            goals.Add(new Goal(GoalKind.Build, "GatherersHut", 3,
                CountFactionBuildings<GathererHutTag>(em, faction),
                AIBudgetCategory.EconomyExpansion, "supply income"));

            // ── 5. AN ARMY THAT EXISTS. ──
            // AGE 0 IS THE RACE, NOT THE WAR. Combat_Pacing.md calls Age 0
            // "the economy race to age-up" — one unit exists (the Spearman;
            // ranged is deliberately an Age-1 unlock after the uncounterable
            // archer-rush playtest), so every supply past a defensive
            // garrison buys a longer, samey spear age instead of the age-up
            // that ends it. Measured on Veilmarch: factions held 87-unit
            // spear armies while 42 of 48 never aged up in 30 minutes — the
            // floor is therefore clamped until era 2, and growth beyond it
            // waits entirely (below). The emergency plan still overrides all
            // of this when the base is actually under attack.
            int militaryFloor = math.max(4, (int)math.round(
                personality.militaryFloor * plan.ArmyScale));
            if (!aged) militaryFloor = math.min(militaryFloor, 8);
            goals.Add(new Goal(GoalKind.Train, "@military", militaryFloor, army,
                AIBudgetCategory.Military, "army floor"));

            // ── 6. THE AGE-UP AND ITS GATE. ──
            if (!aged)
            {
                goals.Add(new Goal(GoalKind.Build, "ShrineOfRidan", 1,
                    CountFactionBuildings<ShrineTag>(em, faction),
                    AIBudgetCategory.Advancement, "age-up gate"));
                goals.Add(new Goal(GoalKind.AgeUp, "AgeUp", 1, aiState.AgeUpIssued != 0 ? 1 : 0,
                    AIBudgetCategory.Advancement, "advance"));
            }

            // ── 7. EVERY PRODUCTION LINE, SEVERAL OF EACH. ──
            // The old rotation only ever alternated Barracks and Archery Range,
            // so the Royal Stable and Siege Yard were never built at all — and
            // cavalry and siege were consequently 0.0% of every unit produced.
            // BEFORE THE AGE-UP, TWO IS ENOUGH. perKind runs to 5-7, and at
            // 220 supplies each that is over 1,100 spent on redundant Age 0
            // production while the 257-supply Shrine that opens the entire
            // Age 1 tree goes unbought. The extra halls only pay off once
            // there is a roster worth queueing in parallel.
            goals.Add(new Goal(GoalKind.Build, "Barracks", aged ? perKind : 2,
                CountFactionBuildings<BarracksTag>(em, faction),
                AIBudgetCategory.Military, "melee line"));
            if (aged)
            {
                goals.Add(new Goal(GoalKind.Build, "ArcheryRange", perKind,
                    CountFactionBuildings<ArcheryRangeTag>(em, faction),
                    AIBudgetCategory.Military, "ranged line"));
                goals.Add(new Goal(GoalKind.Build, "Alanthor_RoyalStable", perKind,
                    CountFactionBuildings<RoyalStableTag>(em, faction),
                    AIBudgetCategory.Military, "cavalry line"));
                goals.Add(new Goal(GoalKind.Build, "Alanthor_SiegeYard", perKind,
                    CountFactionBuildings<SiegeYardTag>(em, faction),
                    AIBudgetCategory.Military, "siege line"));
            }

            // ── 8. HOUSE AHEAD OF THE ARMY, NOT BEHIND IT. ──
            //
            // This sat BELOW army growth, and the ordering inverted the whole
            // economy: a faction that could afford soldiers trained one every
            // tick and never reached the housing goal, so its cap never moved.
            // Measured in one match — Blue could afford military, ended at cap
            // 30 with ZERO Huts; Green could not afford military, fell through
            // to housing every tick and ended at cap 185. Exactly backwards:
            // the faction doing well was the one that stayed capped.
            //
            // Housing is 80 supplies for 30 population and it is what every
            // later unit is bought WITH. It goes first.
            if (popMax < FactionPopulation.AbsoluteMax)
                goals.Add(new Goal(GoalKind.Build, "Hut", HousingTarget(pop), CountFactionBuildings<HutTag>(em, faction),
                    AIBudgetCategory.EconomyExpansion, "housing ahead"));

            // ── 9. GROW THE ARMY TO THE PLAN'S TARGET — once there is an
            // army worth growing. Pre-age-up the roster is one unit, so
            // growth here only delays the era that diversifies it; the
            // clamped floor above is the whole Age 0 military.
            if (aged)
                goals.Add(new Goal(GoalKind.Train, "@military", armyWant, army,
                    AIBudgetCategory.Military, "army growth"));

            // ── Act on the first goal that is unmet and affordable. ──
            foreach (var g in goals)
            {
                if (!g.Unmet) continue;
                if (TryPursue(em, brainEntity, brain, g, ref aiState, profile, now))
                {
                    aiState.StepStuckSeconds = 0f;
                    return;
                }
            }

            // Nothing affordable this tick. Say so occasionally — silence here
            // is what made five blockers take five batches to find.
            int key = (int)faction;
            if (!_nextGoalLog.TryGetValue(key, out float next) || now >= next)
            {
                _nextGoalLog[key] = now + 60f;
                // List the first few REFUSED goals, not just the first unmet
                // one. "top want = Barracks" was reported 57 times in a match
                // where the actual blocker was a build gate three goals further
                // down that failed for every building alike — the log named a
                // symptom and hid the cause.
                var refused = new List<string>(4);
                foreach (var g in goals)
                {
                    if (!g.Unmet) continue;
                    _lastBuildReason.TryGetValue(g.Id, out string cause);
                    refused.Add($"{g.Id} {g.Have}/{g.Want} [{cause ?? g.Why}]");
                    if (refused.Count == 4) break;
                }
                AILogger.Log(faction, "GOALS",
                    $"nothing affordable; refused: {string.Join(", ", refused)} " +
                    $"| pop {pop}/{popMax} crew {workers} army {army}");
            }
        }

        private bool TryPursue(EntityManager em, Entity brainEntity, AIBrain brain, in Goal g,
            ref SimpleAIState aiState, AIDifficultyProfile profile, float now)
        {
            switch (g.Kind)
            {
                case GoalKind.Build:
                    // The age-up gate spends the pot IT is saving for, so it
                    // must not be blocked by its own reservation -- same rule
                    // the Hall claim uses.
                    if (TryBuildBuildingBudgeted(em, brain.Owner, g.Id, g.Cat,
                            honourReservation: g.Id != AgeUpGateBuilding)) return true;
                    // Record why, so the "nothing affordable" log can name the
                    // actual cause instead of the first unmet want.
                    if (!AIBudget.TryAfford(brain.Owner, g.Cat, ToCost(
                            TechCatalog.TryGetBuilding(g.Id, out var bd) && bd != null
                                ? bd.cost : default)))
                        _lastBuildReason[g.Id] = "wallet short";
                    else
                    {
                        TryBuildBuildingWithReason(em, brain.Owner, g.Id, out string why);
                        _lastBuildReason[g.Id] = why ?? "placement refused";
                    }
                    return false;

                case GoalKind.Train:
                    if (g.Id == "@military")
                    {
                        string unit = PickCompositionUnit(em, brainEntity, brain.Owner, now,
                            profile.CounterCompEnabled);
                        if (string.IsNullOrEmpty(unit)) return false;
                        if (!TryTrainUnitBudgeted(em, brain.Owner, unit, g.Cat)) return false;
                        aiState.LastMilitaryUnit = new Unity.Collections.FixedString64Bytes(unit);
                        if (aiState.DesiredMilitary < g.Want) aiState.DesiredMilitary = g.Want;
                        return true;
                    }
                    if (!TryTrainUnitBudgeted(em, brain.Owner, g.Id, g.Cat)) return false;
                    if (g.Id == "Worker" && aiState.DesiredMiners < g.Want)
                        aiState.DesiredMiners = g.Want;
                    return true;

                case GoalKind.AgeUp:
                    if (TryAgeUp(em, brain.Owner, ref aiState))
                    {
                        AIBudget.ClearReservation(brain.Owner);
                        return true;
                    }

                    // COMMIT INCOME TO IT. The age-up is 700 supplies, 140 iron
                    // and 105 veilstone in one lump, tested against the bank --
                    // and the bank never held it, because every goal below
                    // spends supplies the moment they land. Across 12 measured
                    // matches only 2 of 48 factions ever aged up.
                    //
                    // Everything above Age 0 hangs off it: the Archery Range,
                    // Royal Stable and Siege Yard are all minEra 2, and every
                    // combat technology is culture-gated while the culture is
                    // chosen AT the age-up. So 90% of every army was three
                    // melee units, cavalry and siege were literally
                    // unreachable, and the only technologies anyone finished
                    // were the four openers and some resource surveys.
                    //
                    // Only save once the choice building exists -- reserving
                    // before that starves the Shrine this goal depends on.
                    // SAVE IN TWO STAGES. Reserving only the age-up price was
                    // half a fix: the hold does not arm until a choice building
                    // exists, and the Shrine that provides it was itself
                    // refused 506 times for "wallet short" in 13 matches. So
                    // the faction never bought the Shrine, never armed the
                    // hold, and never aged up -- 17 of 52 got there.
                    //
                    // Stage 1 saves for the Shrine, stage 2 for the age-up.
                    Cost stage;
                    if (!FactionHasChoiceBuilding(em, brain.Owner))
                    {
                        if (!TechCatalog.TryGetBuilding(AgeUpGateBuilding, out var gate)
                            || gate == null) return false;
                        stage = ToCost(gate.cost);
                    }
                    else
                    {
                        stage = CultureConfig.AgeUpCost;

                        // DO NOT SAVE FOR WHAT CANNOT BE FINISHED. The age-up
                        // costs veilstone, and on a map where veilstone is
                        // geographically gated (Veilmarch: centre-only) a
                        // faction with no veilstone node in its territory can
                        // NEVER fill this pot — while the priority-1 hold
                        // starves the 600-supply Hall claim that is the only
                        // way to REACH a veilstone region. That circle held
                        // every faction at one territory for entire matches.
                        // Expansion first; the hold arms once the missing
                        // resource is actually obtainable.
                        if (FactionEconomy.TryGetBank(em, brain.Owner, out var aBank))
                        {
                            var res = em.GetComponentData<FactionResources>(aBank);
                            if (res.Veilstone < stage.Veilstone
                                && !OwnsVeilstoneNode(em, brain.Owner))
                                return false;
                        }
                    }

                    AIBudget.Reserve(brain.Owner, stage, now,
                        AgeUpSaveSeconds, AgeUpReservePriority);
                    return false;
            }
            return false;
        }

        /// <summary>
        /// How many Huts the faction should own right now.
        ///
        /// Housing runs AHEAD of population rather than chasing it: enough
        /// Huts for the current population plus a full training buffer, capped
        /// at what reaches the 200 ceiling. At 30 population per Hut that is
        /// about six Huts for the cap — 480 supplies against the ~12,000 a
        /// faction earns in twenty minutes.
        /// </summary>
        private static int HousingTarget(int pop)
        {
            const int PerHut = 30;              // FactionPopulation table
            const int HallProvides = 30;
            const int Buffer = 40;              // room to keep training into

            int wantMax = math.min(FactionPopulation.AbsoluteMax, pop + Buffer);
            int fromHuts = math.max(0, wantMax - HallProvides);
            int need = (fromHuts + PerHut - 1) / PerHut;
            int ceiling = (FactionPopulation.AbsoluteMax - HallProvides + PerHut - 1) / PerHut;
            return math.clamp(need, 1, ceiling);
        }

        /// <summary>Any veilstone outcropping standing in territory this
        /// faction owns — i.e. veilstone income is REACHABLE, so saving for a
        /// veilstone-priced goal can actually finish.</summary>
        private static bool OwnsVeilstoneNode(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
            using var xfs = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(
                Unity.Collections.Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                int r = TheWaningBorder.World.Regions.RegionMap.RegionAt(
                    xfs[i].Position.x, xfs[i].Position.z);
                if (r == TheWaningBorder.World.Regions.RegionMap.None) continue;
                if (TheWaningBorder.World.Regions.TerritoryOwnership.OwnerOf(r)
                    == (int)faction) return true;
            }
            return false;
        }

        private static int FactionEra(EntityManager em, Faction faction)
        {
            if (FactionEconomy.TryGetBank(em, faction, out var bank)
                && em.HasComponent<FactionEra>(bank))
                return em.GetComponentData<FactionEra>(bank).Value;
            return 1;
        }
    }
}
