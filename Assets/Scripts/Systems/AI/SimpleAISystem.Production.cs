// SimpleAISystem.Production.cs
// Training, research, age-up and unit-replacement decisions.
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
        // Bank thresholds before triggering AgeUp: cost + reserve buffer.
        // Set to 0: with the optimised build-orders the AI accumulates well
        // beyond the bare cost, but we shouldn't *gate* on it — earlier 500/
        // 200/100 reserves caused the AI to sit on enough resources for age-up
        // (cost ≈ 1000/200/150) and never trigger because the bank stalled
        // between cost and cost+reserve. Players reasonably expected the AI
        // to age up the moment it could afford. Reintroduce a small reserve
        // here only if the post-age-up economy noticeably stalls.
        private const int AgeUpReserveSupplies = 0;
        private const int AgeUpReserveIron     = 0;
        private const int AgeUpReserveVeilstone  = 0;
        /// <summary>
        /// Build-order Train wrapper: queues the unit and, on success, increments
        /// the matching Desired counter so ReplaceLostUnits knows the AI is now
        /// committed to having this unit alive. Replacement training calls
        /// TryTrainUnit directly so the counter doesn't double-bump.
        /// </summary>
        /// <summary>
        /// Build-order train step — CHARGED TO A WALLET like every other
        /// purchase (2026-08-18). This used to call TryTrainUnit directly, so
        /// the authored order's workers and soldiers were bought out of the
        /// shared bank with no category ever debited — the same leak that let
        /// its BuildBuilding steps outspend the age-up. Workers draw Economy,
        /// anything that fights draws Military.
        /// </summary>
        private static bool TryTrainUnitFromBuildOrder(
            EntityManager em, Faction faction, string unitId, ref SimpleAIState aiState)
        {
            var cat = IsCombatClass(UnitFactory.GetUnitClass(unitId))
                ? AIBudgetCategory.Military
                : AIBudgetCategory.EconomyExpansion;
            if (!TryTrainUnitBudgeted(em, faction, unitId, cat)) return false;
            RegisterTrainedUnit(ref aiState, unitId);
            return true;
        }

        private static void RegisterTrainedUnit(ref SimpleAIState aiState, string unitId)
        {
            UnitClass cls = UnitFactory.GetUnitClass(unitId);
            if (IsCombatClass(cls))
            {
                aiState.DesiredMilitary++;
                aiState.LastMilitaryUnit = new FixedString64Bytes(unitId);
            }
            else if (cls == UnitClass.Miner || cls == UnitClass.Economy)
            {
                // Worker unification: the Worker trains as UnitClass.Economy but
                // carries MinerTag and acts as a miner. Without counting Economy
                // here, DesiredMiners never increments — ReplaceLostUnits would
                // see deficit=0 and stop replacing dead workers, gutting the
                // post-fight economy. (worker-unification fix)
                aiState.DesiredMiners++;
            }
            // Scout/Support not auto-replaced for now — none of the current
            // build orders rely on them surviving in the same way.
        }

        /// <summary>
        /// Apply a SetVeilstoneTarget build-order step. Just clamps and writes the
        /// target on the AI brain's SimpleAIState — AssignIdleMiners reads it on
        /// the next think tick. Always succeeds so the build order advances.
        /// </summary>
        private static bool SetVeilstoneTarget(ref SimpleAIState aiState, int count)
        {
            // Clamp at the system cap (4) so a typo in a build order can't
            // request 50 veilstone miners and starve iron entirely.
            aiState.VeilstoneMinerTarget = math.clamp(count, 0, MaxVeilstoneMiners);
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // TRAIN UNIT
        // ─────────────────────────────────────────────────────────────────

        private static bool TryTrainUnit(EntityManager em, Faction faction, string unitId)
            => TryTrainUnitWithReason(em, faction, unitId, out _);

        /// <summary>Training pre-flight + issue, reporting WHICH gate blocked
        /// on failure — every gate here is silent by design (next tick
        /// retries), which made big-ticket one-offs like King Lexor
        /// undiagnosable from the match log (2026-08-11: "AI is not training
        /// the hero unit" with nothing in the log to say why).</summary>
        private static bool TryTrainUnitWithReason(EntityManager em, Faction faction,
            string unitId, out string blockReason)
        {
            blockReason = null;
            if (!TechCatalog.IsReady) { blockReason = "catalog not ready"; return false; }
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null)
            { blockReason = "no catalog def"; return false; }

            // Find the right training building for this unit.
            Entity trainer = FindTrainerForUnit(em, faction, unitId);
            if (trainer == Entity.Null) { blockReason = "no trainer"; return false; }

            // Don't queue into a building still under construction.
            if (em.HasComponent<UnderConstruction>(trainer))
            { blockReason = "trainer under construction"; return false; }
            if (!em.HasBuffer<TrainQueueItem>(trainer))
            { blockReason = "trainer has no queue"; return false; }

            // Combined train + research cap — see CommandRouter.MaxProductionQueue.
            if (TheWaningBorder.Core.Commands.CommandRouter.IsProductionQueueFull(em, trainer))
            { blockReason = "trainer queue full"; return false; }

            // King's Court seat (2026-08-11): an aged-up Alanthor faction
            // that still owes a Hall unique (Ledger / King Lexor) keeps ONE
            // Hall production slot free — the 5-slot queue stayed
            // permanently full of workers, so the hero never found an
            // opening ("trainer queue full" once a minute, all match).
            if ((unitId == "Worker" || unitId == "Scout")
                && em.HasComponent<HallTag>(trainer)
                && CultureConfig.GetCompletedCulture(em, faction) == Cultures.Alanthor
                && (!TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedKingLexor(em, faction)
                    || !TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedLedger(em, faction)))
            {
                int queued = em.GetBuffer<TrainQueueItem>(trainer).Length;
                if (em.HasBuffer<ResearchQueueItem>(trainer))
                    queued += em.GetBuffer<ResearchQueueItem>(trainer).Length;
                if (queued >= TheWaningBorder.Core.Commands.CommandRouter.MaxProductionQueue - 1)
                { blockReason = "hall seat reserved"; return false; }
            }

            // Level gate BEFORE spending — IssueTrain drops silently for AI
            // sources, which would leak the cost.
            if (!CommandRouter.CanTrainAtBuilding(em, trainer, unitId,
                    out int reqLevel, out string trainerName))
            { blockReason = $"needs Lv{reqLevel} {trainerName}"; return false; }

            // ANTI-STAGNATION: don't queue what population can't spawn. A
            // pop-blocked item sits in the 5-slot queue forever, clogging
            // every later train/research order for the faction. The Hut
            // headroom loop (EnsurePopulationHeadroom) frees this gate.
            if (!PopulationHelper.HasPopulationCapacity(faction, UnitFactory.GetPopulationCost(unitId)))
            { blockReason = "population capped"; return false; }

            // Affordability CHECK only — TrainCommandDirect spends on every
            // peer (docs/Multiplayer_LAN_Readiness.md); an AI-side Spend
            // here would double-charge the host and charge clients nothing.
            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost))
            { blockReason = "bank short"; return false; }

            // Through CommandRouter (CommandSource.AI) so host-AI training
            // replicates — a direct queue.Add spawned units on the host only.
            CommandRouter.IssueTrain(em, trainer, unitId, CommandSource.AI);
            return true;
        }

        private static Entity FindTrainerForUnit(EntityManager em, Faction faction, string unitId)
        {
            // Hall trains support units (Worker, Scout) and — as the Alanthor
            // King's Court — the Ledger automaton and King Lexor (those two
            // are UI-injected on HallTag, never in the Hall def's trains
            // list, so the data fallback below cannot resolve them).
            // Barracks trains the melee line; the archer line trains at the
            // Archery Range (2026-08-04 roster fix — routing Archer to the
            // Barracks silently stranded the AI without ranged production).
            // TempleOfRidan trains the Litharch healer.
            switch (unitId)
            {
                case "Worker":
                case "Scout":
                case "Ledger":
                case "King Lexor":
                case "KingLexor":
                    return FindFactionBuilding<HallTag>(em, faction);
                case "Spearman":
                case "Swordsman":
                case "Alanthor_Swordsman":
                case "Alanthor_Nobleman":
                case "Alanthor_Sentinel":
                    return FindLeastBusyTrainer<BarracksTag>(em, faction);
                case "Archer":
                case "Alanthor_Crossbowman":
                case "Alanthor_Longbowman":
                    return FindLeastBusyTrainer<ArcheryRangeTag>(em, faction);
                case "Alanthor_Cataphract":
                case "Alanthor_Outrider":
                    return FindLeastBusyTrainer<RoyalStableTag>(em, faction);
                case "Alanthor_Ballista":
                case "Alanthor_Trebuchet":
                case "Alanthor_BatteringRam":
                    return FindLeastBusyTrainer<SiegeYardTag>(em, faction);
                case "Litharch":
                case "Alanthor_Scholar":
                    return FindFactionBuilding<TempleTag>(em, faction);
            }

            // Data-driven fallback: resolve via the building defs' `trains`
            // lists so a roster change in the TechTree (e.g. the Swordsman ->
            // Spearman switch) can never silently strand the AI with an
            // untrainable unit again.
            if (TrainsUnit(em, "Hall", unitId)) return FindFactionBuilding<HallTag>(em, faction);
            if (TrainsUnit(em, "Barracks", unitId)) return FindLeastBusyTrainer<BarracksTag>(em, faction);
            if (TrainsUnit(em, "ArcheryRange", unitId)) return FindLeastBusyTrainer<ArcheryRangeTag>(em, faction);
            if (TrainsUnit(em, "TempleOfRidan", unitId)) return FindFactionBuilding<TempleTag>(em, faction);
            // Cultured military buildings (2026-08-04): cavalry at the Royal
            // Stable, catapults at the Siege Yard.
            if (TrainsUnit(em, "Alanthor_RoyalStable", unitId)) return FindLeastBusyTrainer<RoyalStableTag>(em, faction);
            if (TrainsUnit(em, "Alanthor_SiegeYard", unitId)) return FindLeastBusyTrainer<SiegeYardTag>(em, faction);
            return Entity.Null;
        }
        /// <summary>
        /// Per-pair spacing check. All buildings keep <paramref name="minDistSq"/>
        /// from each other; additionally, GathererHut→GathererHut placement uses
        /// <paramref name="minGHutDistSq"/> so their 15 m gather circles don't
        /// overlap (which halves their unobstructed-area-driven income).
        /// </summary>
        /// <summary>
        /// The buildable trainer for a unit, mirroring FindTrainerForUnit's
        /// routing — used by the build-order stepper to BUILD the missing
        /// trainer instead of skipping the Train step. Null for Hall-trained
        /// units (the Hall is never built through this path).
        /// </summary>
        private static string TrainerBuildingIdFor(EntityManager em, string unitId)
        {
            switch (unitId)
            {
                case "Worker":
                case "Scout":
                case "Ledger":
                case "King Lexor":
                case "KingLexor":
                    return null; // Hall-trained
                case "Spearman":
                case "Swordsman":
                case "Alanthor_Swordsman":
                case "Alanthor_Nobleman":
                case "Alanthor_Sentinel":
                    return "Barracks";
                case "Archer":
                case "Alanthor_Crossbowman":
                case "Alanthor_Longbowman":
                    return "ArcheryRange";
                case "Alanthor_Cataphract":
                case "Alanthor_Outrider":
                    return "Alanthor_RoyalStable";
                case "Alanthor_Ballista":
                case "Alanthor_Trebuchet":
                case "Alanthor_BatteringRam":
                    return "Alanthor_SiegeYard";
                case "Litharch":
                case "Alanthor_Scholar":
                    return "TempleOfRidan";
            }
            // Data-driven fallback, same ladder as FindTrainerForUnit.
            if (TrainsUnit(em, "Hall", unitId)) return null;
            if (TrainsUnit(em, "Barracks", unitId)) return "Barracks";
            if (TrainsUnit(em, "ArcheryRange", unitId)) return "ArcheryRange";
            if (TrainsUnit(em, "TempleOfRidan", unitId)) return "TempleOfRidan";
            if (TrainsUnit(em, "Alanthor_RoyalStable", unitId)) return "Alanthor_RoyalStable";
            if (TrainsUnit(em, "Alanthor_SiegeYard", unitId)) return "Alanthor_SiegeYard";
            return null;
        }

        /// <summary>True while a foundation of the unit's trainer building is
        /// under construction — the build order should WAIT for it rather
        /// than instant-skip the Train step.</summary>
        private static bool TrainerInFlight(EntityManager em, Faction faction, string unitId)
        {
            switch (unitId)
            {
                case "Worker":
                case "Scout":
                case "Ledger":
                case "King Lexor":
                case "KingLexor":
                    return CountFactionBuildingsUnderConstruction<HallTag>(em, faction) > 0;
                case "Spearman":
                case "Swordsman":
                    return CountFactionBuildingsUnderConstruction<BarracksTag>(em, faction) > 0;
                case "Archer":
                    return CountFactionBuildingsUnderConstruction<ArcheryRangeTag>(em, faction) > 0;
                default:
                    // Unknown roster entries: err toward waiting when ANY
                    // production building is going up.
                    return CountFactionBuildingsUnderConstruction<BarracksTag>(em, faction) > 0
                        || CountFactionBuildingsUnderConstruction<ArcheryRangeTag>(em, faction) > 0;
            }
        }
        // ─────────────────────────────────────────────────────────────────
        // RESEARCH TECH
        // ─────────────────────────────────────────────────────────────────

        private static bool TryResearchTech(EntityManager em, Faction faction, string techId)
            => TryResearchTechWithReason(em, faction, techId, out _);

        /// <summary>Research pre-flight + issue, reporting WHICH gate blocked
        /// on failure — the economy ladder retries failures silently forever,
        /// which hid a 57-minute survey-line stall in the 2026-08-11 match
        /// (no Iron Surveying all game, map iron ran dry, total freeze).</summary>
        private static bool TryResearchTechWithReason(EntityManager em, Faction faction,
            string techId, out string blockReason)
        {
            blockReason = null;
            if (!TechCatalog.IsReady) { blockReason = "catalog not ready"; return false; }
            if (!TechCatalog.TryGetTechnology(techId, out var def) || def == null)
            { blockReason = "no catalog def"; return false; }

            // Skip if already researched (or in flight) on this faction.
            var researchState = FactionResearchState.Instance;
            if (researchState != null && researchState.HasResearched(faction, techId)) return true;

            // Resolve a host that can actually TAKE the research now —
            // completed, research-capable, queue not full. The old
            // first-found lookup gambled on chunk order: with the hut
            // pipeline keeping one Gatherer's Hut permanently under
            // construction, the first-found hut could be that foundation
            // for an entire match, silently starving the Survey line.
            string researchAt = string.IsNullOrEmpty(def.researchAt) ? "Hall" : def.researchAt;
            Entity bldg = researchAt switch
            {
                "Barracks"             => FindResearchHost<BarracksTag>(em, faction),
                "Hall"                 => FindResearchHost<HallTag>(em, faction),
                "ArcheryRange"         => FindResearchHost<ArcheryRangeTag>(em, faction),
                "GatherersHut"         => FindResearchHost<GathererHutTag>(em, faction),
                "Hut"                  => FindResearchHost<HutTag>(em, faction),
                // Alanthor Age-1 research hosts (Wave 2 military tree).
                "Alanthor_RoyalStable" => FindResearchHost<RoyalStableTag>(em, faction),
                "Alanthor_SiegeYard"   => FindResearchHost<SiegeYardTag>(em, faction),
                "Alanthor_Smelter"     => FindResearchHost<SmelterTag>(em, faction),
                "ShrineOfRidan"        => FindResearchHost<ShrineTag>(em, faction),
                // Sect buildings — each sells exactly its own sect's research
                // (docs/Design/Sects.md section 1).
                "Sect_Reliquary"       => FindResearchHost<ReliquaryTag>(em, faction),
                "Sect_MendingHall"     => FindResearchHost<MendingHallTag>(em, faction),
                "Sect_Stonehold"       => FindResearchHost<StoneholdTag>(em, faction),
                "Sect_Veilworks"       => FindResearchHost<VeilworksTag>(em, faction),
                "Sect_MusterYard"      => FindResearchHost<MusterYardTag>(em, faction),
                _                      => Entity.Null,
            };
            if (bldg == Entity.Null)
            { blockReason = $"no ready {researchAt} host"; return false; }

            // Affordability CHECK only — ResearchCommandDirect spends on
            // every peer (docs/Multiplayer_LAN_Readiness.md).
            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost))
            { blockReason = "bank short"; return false; }

            // Through CommandRouter (CommandSource.AI) so host-AI research
            // replicates to clients in multiplayer.
            TheWaningBorder.Core.Commands.CommandRouter.IssueResearch(em, bldg, techId,
                TheWaningBorder.Core.Commands.CommandSource.AI);
            return true;
        }
        // ─────────────────────────────────────────────────────────────────
        // AGE UP
        // ─────────────────────────────────────────────────────────────────

        private bool TryAgeUp(EntityManager em, Faction faction, ref SimpleAIState aiState)
        {
            if (aiState.AgeUpIssued != 0) return true; // already triggered, advance

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null) return false;

            // Need a choice building (Shrine / Vault / Keep / TempleOfRidan).
            if (!FactionHasChoiceBuilding(em, faction)) return false;

            // Wait for: cost + reserve. Matches the optimised build-order targets.
            var ageUpCost = CultureConfig.AgeUpCost;
            var target = new Cost
            {
                Supplies = ageUpCost.Supplies + AgeUpReserveSupplies,
                Iron     = ageUpCost.Iron     + AgeUpReserveIron,
                Veilstone  = ageUpCost.Veilstone  + AgeUpReserveVeilstone,
            };
            // Affordability CHECK only (cost + reserve) — AgeUpCommandDirect
            // spends the age-up cost on every peer
            // (docs/Multiplayer_LAN_Readiness.md).
            if (!FactionEconomy.CanAfford(em, faction, target)) return false;

            // Pick the Age-2 culture from personality + difficulty + whatever
            // this AI has actually SCOUTED by now (AICultureChoice). Fog-honest:
            // an AI that never explored falls back to its personality prior.
            var brainEntity = FindBrainEntity(em, faction);
            byte culture = Cultures.None;
            if (brainEntity != Entity.Null)
            {
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                culture = AICultureChoice.Pick(em, faction, brainEntity,
                    brain.Strategy, brain.Difficulty, NextRandUint());
                AILogger.Log(faction, "CULTURE",
                    $"age-up culture = {CultureConfig.GetName(culture)} " +
                    $"(strategy {brain.Strategy}, difficulty {brain.Difficulty})");
            }

            // Replicated age-up (audit F3): host-only direct writes left the
            // AI faction frozen in Age 1 on every client.
            CommandRouter.IssueAgeUp(em, hall, culture, CommandSource.AI);

            aiState.AgeUpIssued = 1;
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // REPLACE LOST UNITS
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-queue training for any military/miner units that died after the
        /// build order originally trained them. The deficit = DesiredX - (alive
        /// of that type + already queued of that type). Queues at most one
        /// replacement per category per think tick — replacements pile up over
        /// successive ticks rather than flooding the train queue or blowing
        /// the bank in one frame.
        ///
        /// We never decrement DesiredX. A dead unit just stops contributing to
        /// "alive" and the deficit appears naturally; once a replacement is
        /// queued and trained, alive catches back up and the deficit closes.
        /// </summary>
        /// <summary>Consecutive think ticks the military floor failed to train
        /// anything, per faction — drives the throttled "floor blocked" log.</summary>
        private static readonly System.Collections.Generic.Dictionary<Faction, int> _floorBlockTicks
            = new System.Collections.Generic.Dictionary<Faction, int>();

        /// <summary>Last unit reported as having no trainer, per faction —
        /// de-dupes an otherwise per-tick log line.</summary>
        private static readonly System.Collections.Generic.Dictionary<Faction, string> _lastMissingTrainer
            = new System.Collections.Generic.Dictionary<Faction, string>();

        private static void ReplaceLostUnits(EntityManager em, Faction faction, ref SimpleAIState aiState)
        {
            // Military deficit
            if (aiState.DesiredMilitary > 0 && !aiState.LastMilitaryUnit.IsEmpty)
            {
                int aliveMil = CountAliveMilitary(em, faction);
                int queuedMil = CountQueuedByPredicate(em, faction, isCombat: true);
                int deficit = aiState.DesiredMilitary - (aliveMil + queuedMil);
                if (deficit > 0)
                {
                    // TryTrainUnit (not the build-order wrapper) so DesiredMilitary
                    // doesn't double-count. Failure (queue full / can't afford) is
                    // silent — next tick will try again. Up to 3 per tick
                    // (2026-08-04): with parallel production buildings a big
                    // post-battle deficit refills in seconds, not minutes.
                    int refill = math.min(deficit, 3);
                    int trained = 0;
                    for (int t = 0; t < refill; t++)
                    {
                        if (!TryTrainUnitBudgeted(em, faction,
                                aiState.LastMilitaryUnit.ToString(), AIBudgetCategory.Military))
                            break;
                        trained++;
                    }

                    // A silently blocked floor gets a log line about once a
                    // minute (2026-08-04: Blue held 0 military for 25 min
                    // with a Barracks standing and the log said nothing).
                    if (trained == 0)
                    {
                        // Floor unit's trainer is GONE (log-proven: Blue's
                        // PracticeRange died with LastMilitaryUnit = Archer
                        // and the floor blocked at deficit 19 forever) →
                        // fall back to the Barracks line so the floor can
                        // refill through ANY surviving production.
                        if (FindTrainerForUnit(em, faction, aiState.LastMilitaryUnit.ToString()) == Entity.Null
                            && !aiState.LastMilitaryUnit.Equals(new FixedString64Bytes("Spearman")))
                        {
                            // Log ONCE per distinct missing trainer. The
                            // build order re-adopts its preferred unit every
                            // time a Train step runs, so this fallback fires
                            // continuously while the trainer is missing —
                            // 158 identical lines in the 2026-08-06 match,
                            // which buried everything else in the log.
                            string missing = aiState.LastMilitaryUnit.ToString();
                            if (!_lastMissingTrainer.TryGetValue(faction, out string prev) || prev != missing)
                            {
                                _lastMissingTrainer[faction] = missing;
                                AILogger.Log(faction, "MILITARY",
                                    $"floor unit {missing} has no trainer — falling back to Spearman " +
                                    "(repeats suppressed until it changes)");
                            }
                            aiState.LastMilitaryUnit = new FixedString64Bytes("Spearman");
                        }

                        _floorBlockTicks.TryGetValue(faction, out int ticks);
                        if (++ticks >= 30)
                        {
                            ticks = 0;
                            AILogger.Log(faction, "MILITARY",
                                $"floor blocked ~1 min: deficit {deficit} x {aiState.LastMilitaryUnit} " +
                                "(trainer missing/queue full/wallet or bank short)");
                        }
                        _floorBlockTicks[faction] = ticks;
                    }
                    else
                        _floorBlockTicks[faction] = 0;
                }
            }

            // Miner deficit
            if (aiState.DesiredMiners > 0)
            {
                int aliveMin = CountAliveMiners(em, faction);
                int queuedMin = CountQueuedByPredicate(em, faction, isMiner: true);
                int deficit = aiState.DesiredMiners - (aliveMin + queuedMin);
                if (deficit > 0)
                {
                    // Worker handles both build + mine since the merge —
                    // train "Worker" (the unified factory), it carries
                    // MinerTag too so it'll auto-find deposits.
                    TryTrainUnitBudgeted(em, faction, "Worker", AIBudgetCategory.EconomyExpansion);
                }
            }
        }
        /// <summary>
        /// Composition-vector unit pick (AoE4 model): the army maintains a
        /// desired ranged fraction (default 40%); with counter-composition
        /// enabled the fraction skews against fresh (&lt; 90 s) enemy intel —
        /// enemy melee blob → more archers (shoot the approach), enemy
        /// ranged-heavy or cavalry-heavy → more spears (armored line / brace).
        /// Each call returns whichever unit the CURRENT army is short of, so
        /// successive trains converge on the mix. Age-1 vocabulary
        /// (Spearman/Archer); the Alanthor endgame system layers age-2 units
        /// on top.
        /// </summary>
        private static string PickCompositionUnit(
            EntityManager em, Entity brainEntity, Faction faction, float now, bool counterComp)
        {
            // Own composition.
            int ownMelee = 0, ownRanged = 0;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using (var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    var c = tags[i].Class;
                    if (c == UnitClass.Ranged) ownRanged++;
                    else if (IsCombatClass(c)) ownMelee++;
                }
            }

            float desiredRangedFrac = 0.4f;
            bool cavHeavy = false;
            if (counterComp && em.HasBuffer<EnemySightingRecord>(brainEntity))
            {
                var buffer = em.GetBuffer<EnemySightingRecord>(brainEntity);
                int meleeStr = 0, rangedStr = 0, cavStr = 0;
                for (int i = 0; i < buffer.Length; i++)
                {
                    var rec = buffer[i];
                    if (rec.Category != IntelCategory.MilitaryUnit) continue;
                    if (now - rec.LastSeenTime > 90f) continue;
                    if (!em.Exists(rec.Enemy) || !em.HasComponent<UnitTag>(rec.Enemy)) continue;
                    var cls = em.GetComponentData<UnitTag>(rec.Enemy).Class;
                    if (em.HasComponent<CavalryTag>(rec.Enemy)) cavStr += rec.EstStrength;
                    else if (cls == UnitClass.Ranged || cls == UnitClass.Siege) rangedStr += rec.EstStrength;
                    else meleeStr += rec.EstStrength;
                }
                cavHeavy = cavStr * 2 > meleeStr + rangedStr;
                if (cavHeavy) desiredRangedFrac = 0.25f;                               // spear wall vs cavalry
                else if (meleeStr > rangedStr * 3 / 2) desiredRangedFrac = 0.6f;       // shoot the melee blob
                else if (rangedStr > meleeStr * 3 / 2) desiredRangedFrac = 0.25f;      // close the gap
            }

            // Class choice as before; the unit WITHIN the class follows the
            // age meta ladder (2026-08-11: the picker only ever returned
            // Archer/Spearman, so the AI shipped an Age-0 army all game).
            // Ranged: Longbowman (Range L3) > Crossbowman (L2) > Archer.
            // Melee: Swordsman > Spearman — EXCEPT under cavalry pressure,
            // where the spear wall is the counter and stays the pick.
            string melee = "Spearman";
            string ranged = "Archer";
            if (FactionCultureOf(em, faction) == Cultures.Alanthor)
            {
                ranged = FirstTrainable(em, faction,
                    "Alanthor_Longbowman", "Alanthor_Crossbowman", "Archer");
                if (!cavHeavy)
                    melee = FirstTrainable(em, faction,
                        "Alanthor_Swordsman", "Spearman");
            }

            // Ranged is an Age-1 unlock (2026-08-11): with no Archery Range
            // standing (era 1 cannot build one, or it was razed), the ranged
            // pick has no trainer — train the melee line instead of feeding
            // the "floor blocked" retry loop.
            if (FindTrainerForUnit(em, faction, ranged) == Entity.Null)
                return melee;

            int total = ownMelee + ownRanged;
            if (total == 0) return melee;
            return ownRanged < total * desiredRangedFrac ? ranged : melee;
        }

        /// <summary>First unit in <paramref name="priority"/> that has a
        /// catalog def, a standing trainer, and an open level gate — the
        /// "best currently trainable" resolver behind the age meta ladder.
        /// Falls back to the last entry unconditionally.</summary>
        private static string FirstTrainable(EntityManager em, Faction faction,
            params string[] priority)
        {
            for (int i = 0; i < priority.Length - 1; i++)
            {
                string id = priority[i];
                if (!TechCatalog.TryGetUnit(id, out var def) || def == null) continue;
                Entity trainer = FindTrainerForUnit(em, faction, id);
                if (trainer == Entity.Null) continue;
                if (em.HasComponent<UnderConstruction>(trainer)) continue;
                if (!CommandRouter.CanTrainAtBuilding(em, trainer, id, out _, out _)) continue;
                return id;
            }
            return priority[priority.Length - 1];
        }
    }
}
