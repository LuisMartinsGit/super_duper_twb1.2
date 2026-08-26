// SimpleAISystem.Economy.cs
// Always-on economy layer: worker floor, hut pipeline, research ladders, budget wrappers.
// Partial of SimpleAISystem.cs -- split 2026-08-12 for readability.

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Data.AI;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.World.FogOfWar;
using TheWaningBorder.World.Terrain;
using UnityEngine;

namespace TheWaningBorder.AI
{
    public partial class SimpleAISystem : SystemBase
    {
        /// <summary>
        /// Worker floor for this faction. Feraldis Workers cannot gather at
        /// all — ore comes from Mines and supplies from raiding — so a full
        /// economy crew is pure waste. It keeps a builder pair and turns the
        /// rest of its population into soldiers.
        /// </summary>
        private static int WorkerFloorFor(EntityManager em, Faction faction)
            => CultureConfig.GetCompletedCulture(em, faction) == Cultures.Feraldis
                ? FeraldisWorkerFloor
                : EconomyWorkerFloor;

        /// <summary>Builders a Feraldis faction keeps for base expansion.</summary>
        private const int FeraldisWorkerFloor = 2;

        /// <summary>Max Gatherer's Huts (= Raider Camps) a Feraldis AI builds.
        /// Each one is a permanent raider stream, not a gather bonus.</summary>
        private const int FeraldisRaiderCampCap = 5;

        /// <summary>Seconds over which a gathering culture's hut cap ramps
        /// from its difficulty target to double it.</summary>
        private const float HutCapDoublingSeconds = 1200f;   // 20 min
        // ─────────────────────────────────────────────────────────────────
        // MAINTENANCE LOOP (post build-order)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Once the build order is exhausted (StepIndex past the end), keep
        /// the AI productive: top up the army to the personality's maintenance
        /// floors (M4), counter-pick the replacement unit from enemy
        /// composition intel, and push every assembled wave at the best-scored
        /// enemy target.
        ///
        /// Without this loop, every non-Rush strategy ends its build order
        /// after age-up and the AI stops issuing orders entirely — no army
        /// growth, no attacks.
        ///
        /// ReplaceLostUnits already trains <c>LastMilitaryUnit</c> when the
        /// army falls short of <c>DesiredMilitary</c>, and "Worker" when
        /// miners are short of <c>DesiredMiners</c>. We just bump those
        /// targets and steer <c>LastMilitaryUnit</c>.
        /// </summary>
        private void RunMaintenanceLoop(EntityManager em, Entity brainEntity, AIBrain brain,
            ref SimpleAIState aiState, AISettingsSO settings, AISettingsSO.PersonalityBlock personality,
            AIDifficultyProfile profile, float now)
        {
            Faction faction = brain.Owner;

            // Composition-driven training (AoE4 model): maintain a desired
            // melee/ranged mix and, on counter-comp tiers, skew it against the
            // observed enemy composition. Every replacement/growth train pulls
            // the army toward the mix instead of stamping one unit type.
            aiState.LastMilitaryUnit = new FixedString64Bytes(
                PickCompositionUnit(em, brainEntity, faction, now, profile.CounterCompEnabled));

            // Raise the maintenance floors. Military floor comes from the
            // personality; the WORKER target follows the difficulty's per-age
            // curve (AoE4: villager targets rise with age and difficulty) —
            // the personality floor acts as a minimum. Never reduce.
            if (aiState.DesiredMilitary < personality.militaryFloor)
                aiState.DesiredMilitary = personality.militaryFloor;
            int workerTarget = math.max(personality.minerFloor,
                aiState.AgeUpIssued != 0 ? profile.WorkerTargetAge1 : profile.WorkerTargetAge0);
            if (aiState.DesiredMiners < workerTarget)
                aiState.DesiredMiners = workerTarget;

            // PRODUCTION BUILDINGS (2026-08-04): grow toward the difficulty
            // target, alternating Barracks / Archery Range so the melee and
            // archer lines pump in PARALLEL (FindLeastBusyTrainer spreads the
            // orders across them). The first Barracks is unconditional —
            // EcoBoom ends without one and could never queue military.
            {
                int barracksCount = CountFactionBuildings<BarracksTag>(em, faction);
                int rangeCount = CountFactionBuildings<ArcheryRangeTag>(em, faction);
                // Ranged is an Age-1 unlock (2026-08-11) — the Range only
                // enters the alternation once aged up; before that every
                // production slot is a Barracks.
                bool rangedUnlocked = false;
                if (FactionEconomy.TryGetBank(em, faction, out var prodBank)
                    && em.HasComponent<FactionEra>(prodBank))
                    rangedUnlocked = em.GetComponentData<FactionEra>(prodBank).Value >= 2;
                if (barracksCount == 0)
                    TryBuildBuildingBudgeted(em, faction, "Barracks", AIBudgetCategory.Military);
                else if (barracksCount + rangeCount < profile.ProductionBuildingTarget)
                    TryBuildBuildingBudgeted(em, faction,
                        rangedUnlocked && rangeCount < barracksCount ? "ArcheryRange" : "Barracks",
                        AIBudgetCategory.Military);
            }

            // (Gatherer's Hut growth moved to TickEconomy — the always-on
            // pipeline in the think loop, so it runs during the build order
            // too. Keeping it here starved stalled openers of supplies.)

            // PIVOTAL UNIQUES (2026-08-04): an aged-up Alanthor AI fields its
            // King's Court pieces — the Ledger automaton and King Lexor.
            // HeroTrainLimit's live-or-queued checks (the same gates the
            // player's training goes through) stop double-queueing, and both
            // re-train automatically after a death.
            if (aiState.AgeUpIssued != 0
                && FactionCultureOf(em, faction) == Cultures.Alanthor)
            {
                if (!TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedLedger(em, faction))
                    TryTrainPivotalUnique(em, faction, "Ledger");
                else
                    AIPivotalReserve.Clear(faction, "Ledger");
                if (!TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedKingLexor(em, faction))
                    TryTrainPivotalUnique(em, faction, "King Lexor");
                else
                    AIPivotalReserve.Clear(faction, "King Lexor");
            }

            // Keep at least one scout alive: the intel pipeline (and the
            // scout-then-strike gate) starves without map vision, and dead
            // scouts were never replaced by any build order.
            if (CountScouts(em, faction) == 0)
                TryTrainUnit(em, faction, "Scout");

            // (Steady-state research moved to TickEconomy's always-on ladder
            // — 2026-08-04: the old walk here only ran post-build-order AND
            // stopped at the first unresearched tech even when the attempt
            // failed, so one unaffordable tech blocked the hut resource
            // researches behind it indefinitely.)

            // (Economy expansion via forward GathererHuts was removed with the
            // carry/dropoff mechanic: mined resources credit the bank directly,
            // so hut proximity to deposits no longer matters.)

            // SUSTAINED PRODUCTION (AoE4 habit: never idle the production
            // buildings). Once the current floor is satisfied, keep growing
            // the army while the bank affords it, raising DesiredMilitary so
            // ReplaceLostUnits maintains the larger force. Capped per
            // difficulty so the economy and the Hut headroom loop keep pace.
            // BURST (2026-08-04): with multiple production buildings the loop
            // queues up to one unit per trainer per tick — the parallel
            // buildings actually pump in parallel instead of growing the army
            // one unit per think tick regardless of capacity.
            if (aiState.DesiredMilitary < profile.SustainArmyCap
                && CountAliveMilitary(em, faction) >= aiState.DesiredMilitary
                // Pivotal savings hold: army GROWTH (beyond the floor) is
                // discretionary — it was eating every supply the instant it
                // arrived, so 500-supply lump sums never formed.
                && !AIPivotalReserve.ShouldHold(em, faction))
            {
                int trainers = CountFactionBuildings<BarracksTag>(em, faction)
                             + CountFactionBuildings<ArcheryRangeTag>(em, faction);
                int burst = math.clamp(trainers, 1, 3);
                for (int t = 0; t < burst && aiState.DesiredMilitary < profile.SustainArmyCap; t++)
                {
                    string unit = PickCompositionUnit(em, brainEntity, faction, now,
                        profile.CounterCompEnabled);
                    if (!TryTrainUnitBudgeted(em, faction, unit, AIBudgetCategory.Military)) break;
                    aiState.DesiredMilitary++;
                    aiState.LastMilitaryUnit = new FixedString64Bytes(unit);
                }
            }

            // Attacks are owned by the wave layer (TickAttackWaves in the
            // think loop) — the old every-tick threshold launch here made
            // pacing an accident of the economy: one big army at ~20 min,
            // then whenever production happened to outrun the threshold.
        }
        /// <summary>Always-on economy layer (2026-08-04 rev.2). Not a budget
        /// system — a PRIORITY ladder the build order cannot override:
        ///   1. WORKER FLOOR — a stalled opener must still grow its miners.
        ///   2. HUT PIPELINE — "lack of supplies means build more huts":
        ///      whenever no Gatherer's Hut is under construction and the
        ///      cost is affordable, start the next one. Huts repay fast
        ///      (120 S + 10 I) and are the supplies engine everything else
        ///      (units, buildings, techs) draws from; the difficulty target
        ///      is irrelevant here — one is simply ALWAYS in flight.
        /// If the pipeline model still lets openers hoard, the escalation is
        /// true per-purpose income budgets (economy/research/expansion/
        /// building/military) — deferred until observed necessary.</summary>
        private const int EconomyWorkerFloor = 6;
        /// <summary>Huts below this count build unconditionally (bootstrap);
        /// past it the ECONOMY WALLET is the pipeline's constraint (M-A —
        /// the flat supplies reserve this replaced lives on in git).</summary>
        private const int HutPipelineFreeCount = 4;

        /// <summary>Research priority ladder for the always-on economy layer.
        /// GATHERER'S HUT SURVEY TECHS LEAD (2026-08-04, user: "there's a
        /// research at the hut to generate free resources — priority after
        /// age 1"): the Survey chain turns the hut spread into passive
        /// Iron/Veilstone/Veilsteel income — the mid-game iron starvation
        /// fix. (DeepGathering removed outright 2026-08-04 — the Surveys are
        /// the only hut drips.) The walk attempts every unresearched entry
        /// until ONE succeeds — the old walk stopped at the first
        /// unresearched tech even when the attempt FAILED (unaffordable), so
        /// one pricey early tech blocked everything behind it indefinitely.</summary>
        private static readonly string[] EconomyResearchLadder =
        {
            "StoneTools",                        // Hall — gather speed (cheap opener)
            "IronSurveying1",                    // Gatherer's Hut — iron drip
            "VeilstoneSurvey1",                  // Gatherer's Hut — veilstone drip
            "ArmedScouts",                       // Hall — arms scouts (attack gate)
            "Conscription", "StoneWeapons",      // Barracks — train speed / T1
            "Fletching", "StoneTippedArrows",    // Archery Range — range / T1
            "IronTools", "MasonGuild",           // Hall — T2 eco + building HP
            "IronSurveying2",                    // Gatherer's Hut — iron drip II
            "VeilstoneSurvey2",                  // Gatherer's Hut — veilstone drip II
            "VeilsteelSurvey",                   // Gatherer's Hut — veilsteel (maxed huts only)
            "IronSurveying3",                    // Gatherer's Hut — iron drip III
            "ScoutingCelestarii",                // Hall — scout tech
            "VeilstoneTools",                    // Hall — T3 eco
        };

        /// <summary>
        /// Feraldis variant of the ladder. Feraldis Gatherer's Huts are
        /// Raider Camps: they gather nothing, so every Survey on the shared
        /// ladder is dead weight for them. The Raiding line is the exact
        /// equivalent — it scales what their Plunderers steal, which IS the
        /// Feraldis economy. Without this split a Feraldis AI bought six
        /// techs that do nothing and never bought the six that matter.
        /// </summary>
        private static readonly string[] FeraldisEconomyResearchLadder =
        {
            "StoneTools",                        // Hall — gather speed (cheap opener)
            "Raiding1",                          // Raider Camp — bigger take
            "IronPlunder",                       // Raider Camp — steal iron too
            "ArmedScouts",                       // Hall — arms scouts (attack gate)
            "Conscription", "StoneWeapons",      // Barracks — train speed / T1
            "Fletching", "StoneTippedArrows",    // Thrower Camp — range / T1
            "IronTools",                         // Hall — T2 eco
            "Raiding2",                          // Raider Camp — bigger take II
            "VeilstonePlunder",                  // Raider Camp — steal veilstone
            "Raiding3",                          // Raider Camp — bigger take III
            "VeilsteelPlunder",                  // Raider Camp — steal veilsteel
            "ScoutingCelestarii",                // Hall — scout tech
            "VeilstoneTools",                    // Hall — T3 eco
        };

        /// <summary>Ladder for this faction's culture (see above).</summary>
        private static string[] EconomyLadderFor(EntityManager em, Faction faction)
            => CultureConfig.GetCompletedCulture(em, faction) == Cultures.Feraldis
                ? FeraldisEconomyResearchLadder
                : EconomyResearchLadder;

        private void TickEconomy(EntityManager em, Faction faction,
            ref SimpleAIState aiState, AIDifficultyProfile profile, float now)
        {
            // (1) Worker floor (EconomyExpansion wallet).
            //
            // The LAST unheld drain (2026-08-18). Income was never the
            // problem: Expert earns ~14.5 supplies/SECOND and still sat at
            // 20-76 banked for five straight minutes, because it converts
            // every supply into workers on arrival (its floor is 20). With
            // hut founding paused and army growth + the research sweep
            // already held, worker hiring was the one thing left spending the
            // age-up out from under itself. It pauses while the pivotal
            // reserve is armed — a 60-90 s window that AIPivotalReserve
            // releases by itself (MaxHoldSeconds), so this can never deadlock
            // the economy, and a BOOTSTRAP MINIMUM is always hired regardless
            // so a young faction still gets its first workers.
            const int WorkerSaveFloor = 6;
            int alive = CountAliveMiners(em, faction);
            int queued = CountQueuedByPredicate(em, faction, isMiner: true);
            bool workerHold = AIPivotalReserve.ShouldHold(em, faction)
                           && alive + queued >= WorkerSaveFloor;
            if (!workerHold && alive + queued < WorkerFloorFor(em, faction))
                TryTrainUnitBudgeted(em, faction, "Worker", AIBudgetCategory.EconomyExpansion);

            // (2) Hut pipeline — one in flight at all times. The first few
            // huts are unconditional bootstrap; past that the ECONOMY WALLET
            // is the constraint (replaces the flat supplies reserve AND the
            // savings-mode hack: when the age gate or a Defend posture
            // tilts the weights, this pipeline throttles by itself).
            {
                int ghTotal = CountFactionBuildings<GathererHutTag>(em, faction);
                bool started = false;

                // DIFFICULTY CAP. profile.GathererHutTarget was defined for
                // all four tiers (3/5/8/10) and read by NOTHING — the pipeline
                // grew on the economy wallet alone, which is how a Normal AI
                // whose profile says 5 ended a match with FIFTEEN huts.
                // Honouring it is also what makes the tiers differ
                // economically rather than only in reaction speed.
                //
                // FERALDIS is capped tighter still: its huts are Raider Camps,
                // so each one is a permanent free-raider stream rather than a
                // gather bonus.
                bool feraldis = CultureConfig.GetCompletedCulture(em, faction) == Cultures.Feraldis;

                // The cap GROWS through the match for gathering cultures.
                // A hut economy is supposed to keep spreading — the target is
                // an early-game figure, not a lifetime ceiling, and holding a
                // fixed number left Alanthor's economy plateauing at minute
                // ten while it had map and money to keep expanding. Doubles
                // over ~20 minutes.
                int hutCap;
                if (feraldis)
                {
                    // Feraldis huts are Raider Camps, not gatherers — more of
                    // them is more free raiders, so this stays hard-capped.
                    hutCap = math.min(FeraldisRaiderCampCap, profile.GathererHutTarget);
                }
                else
                {
                    float growth = 1f + math.min(now / HutCapDoublingSeconds, 1f);
                    hutCap = (int)math.round(profile.GathererHutTarget * growth);
                }

                // AGE-UP PUSH: once it is time to advance, STOP FOUNDING HUTS
                // and let the supplies pile up (2026-08-18, log-proven).
                // Founding a hut costs ~120 supplies, so an AI that keeps
                // expanding spends its income exactly as fast as it arrives —
                // logged matches reached minute 6 with 10-12 huts and 62-158
                // supplies banked against a 700-supply age-up, and no
                // difficulty ever raised its choice building. The estate the
                // AI already owns keeps earning while it saves; expansion
                // resumes the moment the age-up is issued.
                // NOTE (2026-08-18): the hut pipeline is NO LONGER paused
                // while saving. Pausing it froze Expert at four huts, which
                // capped the very income the age-up is waiting on — the save
                // and the economy were fighting each other. The Advancement
                // bank floor (LeavesAdvancementFloorIntact) throttles hut
                // spending on its own: huts are bought from the surplus ABOVE
                // the floor, so the economy keeps growing while the age-up
                // money is untouchable.
                if (ghTotal >= hutCap) { /* at the difficulty's target */ }
                else if (CountFactionBuildingsUnderConstruction<GathererHutTag>(em, faction) == 0)
                {
                    started = ghTotal < HutPipelineFreeCount
                        ? TryBuildBuilding(em, faction, "GatherersHut")
                        : TryBuildBuildingBudgeted(em, faction, "GatherersHut",
                            AIBudgetCategory.EconomyExpansion);
                }
                if (started)
                    AILogger.Log(faction, "ECONOMY",
                        $"GatherersHut started (total {CountFactionBuildings<GathererHutTag>(em, faction)}, " +
                        $"inflight {CountFactionBuildingsUnderConstruction<GathererHutTag>(em, faction)})");
            }

            // (3) MILITARY INFRASTRUCTURE + FLOOR (Military wallet).
            if (now > 240f
                && FindFactionBuilding<BarracksTag>(em, faction) == Entity.Null
                && CountFactionBuildingsUnderConstruction<BarracksTag>(em, faction) == 0
                && TryBuildBuildingBudgeted(em, faction, "Barracks", AIBudgetCategory.Military))
                AILogger.Log(faction, "ECONOMY", "floor Barracks started");

            if (FindFactionBuilding<BarracksTag>(em, faction) != Entity.Null)
            {
                int floor = math.min(profile.WaveBaseUnits + 2, profile.SustainArmyCap);
                if (aiState.DesiredMilitary < floor)
                    aiState.DesiredMilitary = floor;
                if (aiState.LastMilitaryUnit.IsEmpty)
                    aiState.LastMilitaryUnit = new FixedString64Bytes("Spearman");
            }

            // (4) Research — GH resource techs draw the Economy wallet, the
            // rest draw Advancement. Skips completed AND in-flight techs.
            // The walk CONTINUES past failures — but a step that fails every
            // tick forever must not fail silently (2026-08-11: the Survey
            // line stalled for 57 minutes and nothing said why), so the
            // first blocked step logs its reason about once a minute.
            var research = FactionResearchState.Instance;
            var ladder = EconomyLadderFor(em, faction);
            string firstBlocked = null, firstReason = null;
            bool queuedAny = false;
            for (int i = 0; i < ladder.Length; i++)
            {
                string techId = ladder[i];
                if (research != null && research.HasResearched(faction, techId))
                    continue;
                if (IsResearchInFlight(em, faction, techId))
                    continue;
                // Hut/camp resource techs are economy spends; the rest advance.
                var cat = techId.Contains("Survey")
                       || techId.Contains("Raiding")
                       || techId.Contains("Plunder")
                    ? AIBudgetCategory.EconomyExpansion
                    : AIBudgetCategory.Advancement;
                if (TryResearchTechBudgetedWithReason(em, faction, techId, cat, out string reason))
                {
                    AILogger.Log(faction, "RESEARCH", $"{techId} queued");
                    queuedAny = true;
                    break;
                }
                if (firstBlocked == null) { firstBlocked = techId; firstReason = reason; }
            }

            if (!queuedAny && firstBlocked != null)
            {
                _ladderBlockTicks.TryGetValue(faction, out int ticks);
                if (++ticks >= 30)
                {
                    ticks = 0;
                    AILogger.Log(faction, "RESEARCH",
                        $"ladder blocked ~1 min at {firstBlocked} ({firstReason})");
                }
                _ladderBlockTicks[faction] = ticks;
            }
            else
            {
                _ladderBlockTicks.Remove(faction);
            }
        }

        /// <summary>Ticks the economy research ladder has queued nothing
        /// while at least one step was blocked — drives the throttled
        /// ladder-block log above.</summary>
        private static readonly System.Collections.Generic.Dictionary<Faction, int> _ladderBlockTicks
            = new System.Collections.Generic.Dictionary<Faction, int>();

        // ─────────────────────────────────────────────────────────────
        // ENDGAME RESEARCH SWEEP (era 2+)
        //
        // The EconomyResearchLadder is a hand-authored opener covering the
        // ~15 techs the early game lives on. Everything else — the armour
        // ladders at the Smelter, the Vault bond line, Shrine masses, the
        // Stable / Siege Yard military trees, Keep emplacements — exists
        // only in each building def's research list. This sweep walks every
        // OWNED research-capable building and queues the first tech that is
        // unresearched, prereq-satisfied, culture-allowed, level-satisfied
        // by THAT building, affordable, and not already in flight — so the
        // AI eventually researches the whole tree (44+ techs) without a
        // hand-authored list per culture.
        //
        // Priority: the ladder keeps the early game. The sweep only fires
        // when the ladder has no affordable next step, or from era 3 on.
        // ─────────────────────────────────────────────────────────────

        /// <summary>Sweep cadence (seconds). Slow mop-up loop — research
        /// takes 30-90 s per tech, so 20 s keeps every host busy without
        /// hammering the queries.</summary>
        private const float ResearchSweepInterval = 20f;

        /// <summary>Per-faction next-sweep time. Managed instance state is
        /// fine here: the AI runs host-only and every effect flows out as a
        /// CommandRouter command (same pattern as _missions).</summary>
        private readonly System.Collections.Generic.Dictionary<int, float> _nextResearchSweep
            = new System.Collections.Generic.Dictionary<int, float>();

        private void TickEndgameResearchSweep(EntityManager em, Faction faction, float now)
        {
            if (!TechCatalog.IsReady) return;

            // Era gate: the sweep is era-2+ behaviour.
            int era = 1;
            if (FactionEconomy.TryGetBank(em, faction, out var bank)
                && em.HasComponent<FactionEra>(bank))
                era = em.GetComponentData<FactionEra>(bank).Value;
            if (era < 2) return;

            // Throttle (~20 s per faction).
            if (_nextResearchSweep.TryGetValue((int)faction, out float next) && now < next)
                return;
            _nextResearchSweep[(int)faction] = now + ResearchSweepInterval;

            // Ladder priority: while the authored economy ladder still has an
            // affordable unresearched step, it keeps the wallet (era 2 only —
            // from era 3 the sweep runs regardless).
            if (era < 3 && LadderHasAffordableStep(em, faction)) return;

            // Pivotal savings hold: the sweep is a steady discretionary
            // drain (a tech every ~20 s) — it waits while the faction saves
            // toward a Temple level / King's Court unique.
            if (AIPivotalReserve.ShouldHold(em, faction)) return;

            var research = FactionResearchState.Instance;
            byte culture = CultureConfig.GetCompletedCulture(em, faction);

            // Walk every owned research-capable building (a ResearchQueueItem
            // buffer is the research-host marker).
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<ResearchQueueItem>());
            using var hosts = q.ToEntityArray(Allocator.Temp);
            using var hostFacs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < hosts.Length; i++)
            {
                if (hostFacs[i].Value != faction) continue;
                var building = hosts[i];
                if (em.HasComponent<UnderConstruction>(building)) continue;
                if (CommandRouter.IsProductionQueueFull(em, building)) continue;

                string buildingId = TheWaningBorder.Entities.BuildingIds.Of(building, em);
                if (string.IsNullOrEmpty(buildingId)) continue;
                if (!TechCatalog.TryGetBuilding(buildingId, out var def)
                    || def == null || def.research == null) continue;

                // Host building level for minBuildingLevel gates (unstamped
                // buildings count as L1 — mirrors the research extractor).
                int level = 1;
                if (em.HasComponent<BuildingUpgradeState>(building))
                    level = math.max(level,
                        em.GetComponentData<BuildingUpgradeState>(building).Level);

                for (int t = 0; t < def.research.Length; t++)
                {
                    string techId = def.research[t];
                    if (techId == "Research_Era2") continue; // age-up rides its own flow
                    if (!TechCatalog.TryGetTechnology(techId, out var tech) || tech == null)
                        continue;
                    if (research != null && research.HasResearched(faction, techId)) continue;
                    if (IsResearchInFlight(em, faction, techId)) continue;
                    if (!TechCatalog.CultureAllows(tech, culture)) continue;
                    if (math.max(1, tech.minBuildingLevel) > level) continue;
                    if (research != null
                        && !research.MeetsPrerequisites(faction, tech.prerequisites)) continue;

                    // Check-then-issue, mirroring TryResearchTech — the SPEND
                    // lives in ResearchCommandDirect on every peer
                    // (docs/Multiplayer_LAN_Readiness.md).
                    var cost = ToCost(tech.cost);
                    if (!FactionEconomy.CanAfford(em, faction, cost)) continue;

                    TheWaningBorder.Core.Commands.CommandRouter.IssueResearch(
                        em, building, techId,
                        TheWaningBorder.Core.Commands.CommandSource.AI);
                    AILogger.Log(faction, "RESEARCH", $"sweep: {techId} at {buildingId}");
                    break; // one tech per building per sweep
                }
            }
        }

        /// <summary>True while the authored economy ladder still has an
        /// unresearched, not-in-flight entry the faction can afford right
        /// now — the signal that the early-game ladder keeps spending
        /// priority over the endgame sweep.</summary>
        private static bool LadderHasAffordableStep(EntityManager em, Faction faction)
        {
            var research = FactionResearchState.Instance;
            var ladder = EconomyLadderFor(em, faction);
            for (int i = 0; i < ladder.Length; i++)
            {
                string techId = ladder[i];
                if (research != null && research.HasResearched(faction, techId)) continue;
                if (IsResearchInFlight(em, faction, techId)) continue;
                if (!TechCatalog.TryGetTechnology(techId, out var def) || def == null) continue;
                if (FactionEconomy.CanAfford(em, faction, ToCost(def.cost))) return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────
        // BUDGETED SPEND WRAPPERS (M-A): wallet pre-check, real purchase,
        // spend record. The real bank/CommandRouter path is unchanged.
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The Advancement allocation is a REAL FLOOR in the bank, not just an
        /// entitlement (2026-08-18, log-proven).
        ///
        /// The three wallets are accounting over ONE shared bank, so a weight
        /// of 0.68 to Advancement meant nothing on its own: an Expert AI
        /// earning 16 supplies/SECOND showed 391 supplies of Advancement
        /// entitlement against an actual bank of 70, because Military and
        /// Economy simply spent the shared pool first. Over five minutes it
        /// earned ~4,800 supplies and banked 70 — its 210-supply Shrine was
        /// never affordable at any instant.
        ///
        /// Now a non-Advancement purchase must leave the bank still covering
        /// what Advancement has been allocated. The weights finally mean what
        /// they say, without pausing the economy: huts and workers keep being
        /// bought out of the surplus above the floor, which is what actually
        /// grows the income that pays for the age-up.
        /// </summary>
        private static bool LeavesAdvancementFloorIntact(EntityManager em, Faction faction,
            AIBudgetCategory cat, Cost cost)
        {
            if (cat == AIBudgetCategory.Advancement) return true;
            int floor = AIBudget.WalletSupplies(faction, AIBudgetCategory.Advancement);
            if (floor <= 0) return true;
            if (!FactionEconomy.TryGetResources(em, faction, out var res)) return true;
            return res.Supplies - cost.Supplies >= floor;
        }

        /// <summary>
        /// Which wallet a build-order purchase is charged to.
        ///
        /// The authored build order used to spend BANK-DIRECT, consulting no
        /// wallet at all (2026-08-18): its `BuildBuilding:Barracks` step
        /// bought a second Barracks out of the shared bank moments after the
        /// maintenance loop had already bought one THROUGH the Military
        /// wallet, and the same step class outspent the age-up all match. That
        /// is why Military and Advancement appeared to share a wallet — the
        /// build order was outside the budget entirely, so its purchases came
        /// out of everybody's money.
        /// </summary>
        private static AIBudgetCategory CategoryForBuilding(string buildingId)
        {
            if (BuildingFactory.IsChoiceBuilding(buildingId)) return AIBudgetCategory.Advancement;
            switch (buildingId)
            {
                case "Barracks":
                case "ArcheryRange":
                case "Alanthor_SiegeYard":
                case "Feraldis_SiegeYard":
                case "Runai_SiegeWorkshop":
                case "Alanthor_Tower":
                case "Feraldis_Tower":
                case "Alanthor_RoyalStable":
                    return AIBudgetCategory.Military;
                default:
                    return AIBudgetCategory.EconomyExpansion;
            }
        }

        /// <summary>Build-order building step, charged to its wallet.</summary>
        private bool TryBuildBuildingFromBuildOrder(EntityManager em, Faction faction, string buildingId)
            => TryBuildBuildingBudgeted(em, faction, buildingId, CategoryForBuilding(buildingId));

        private static bool TryTrainUnitBudgeted(EntityManager em, Faction faction,
            string unitId, AIBudgetCategory cat)
        {
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return false;
            var cost = ToCost(def.cost);
            if (!AIBudget.TryAfford(faction, cat, cost)) return false;
            if (!TryTrainUnit(em, faction, unitId)) return false;
            AIBudget.RecordSpend(faction, cat, cost);
            return true;
        }

        /// <summary>Ticks a King's Court unique has been blocked, per
        /// faction — drives the throttled block log below.</summary>
        private static readonly System.Collections.Generic.Dictionary<Faction, int> _uniqueBlockTicks
            = new System.Collections.Generic.Dictionary<Faction, int>();

        /// <summary>Train a King's Court pivotal unique (Ledger / King
        /// Lexor). Deliberately NOT budget-windowed: a 600-supply one-off
        /// starves inside the Advancement window's weighted share, and the
        /// HeroTrainLimit caller-side gate already makes this a one-time
        /// spend. Every silent pre-flight failure surfaces in the log about
        /// once a minute with its reason (2026-08-11: the hero never
        /// trained and the log could not say why).</summary>
        private static void TryTrainPivotalUnique(EntityManager em, Faction faction, string unitId)
        {
            if (TryTrainUnitWithReason(em, faction, unitId, out string reason))
            {
                _uniqueBlockTicks.Remove(faction);
                AIPivotalReserve.Clear(faction, unitId);
                AILogger.Log(faction, "MILITARY", $"King's Court: queued {unitId}");
                return;
            }

            // Bank short — reserve the cost so the discretionary spenders
            // stop eating the lump sum (see AIPivotalReserve).
            if (reason == "bank short"
                && TechCatalog.TryGetUnit(unitId, out var def) && def != null)
            {
                AIPivotalReserve.Set(faction, unitId, ToCost(def.cost));
            }

            _uniqueBlockTicks.TryGetValue(faction, out int ticks);
            if (++ticks >= 30)
            {
                ticks = 0;
                AILogger.Log(faction, "MILITARY",
                    $"King's Court: {unitId} blocked ~1 min ({reason})");
            }
            _uniqueBlockTicks[faction] = ticks;
        }

        private bool TryBuildBuildingBudgeted(EntityManager em, Faction faction,
            string buildingId, AIBudgetCategory cat)
        {
            if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def == null) return false;
            var cost = ToCost(def.cost);
            if (!AIBudget.TryAfford(faction, cat, cost)) return false;
            if (!TryBuildBuilding(em, faction, buildingId)) return false;
            AIBudget.RecordSpend(faction, cat, cost);
            return true;
        }

        private static bool TryResearchTechBudgetedWithReason(EntityManager em, Faction faction,
            string techId, AIBudgetCategory cat, out string blockReason)
        {
            blockReason = null;
            if (!TechCatalog.TryGetTechnology(techId, out var def) || def == null)
            { blockReason = "no catalog def"; return false; }
            var cost = ToCost(def.cost);
            if (!AIBudget.TryAfford(faction, cat, cost))
            { blockReason = $"{cat} wallet short"; return false; }
            if (!TryResearchTechWithReason(em, faction, techId, out blockReason)) return false;
            AIBudget.RecordSpend(faction, cat, cost);
            return true;
        }

        /// <summary>True while any of this faction's buildings already holds
        /// <paramref name="techId"/> in its research queue — the guard that
        /// stops the ladder re-buying an in-flight tech every tick.</summary>
        private static bool IsResearchInFlight(EntityManager em, Faction faction, string techId)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<ResearchQueueItem>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var buf = em.GetBuffer<ResearchQueueItem>(ents[i]);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].TechId.ToString() == techId) return true;
            }
            return false;
        }
    }
}
