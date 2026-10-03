// SimpleAISystem.Production.cs
// Training, research, age-up and unit-replacement decisions.
// Partial of SimpleAISystem.cs -- split 2026-08-12 for readability.

using System.Collections.Generic;
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
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_HallTagAgeUpStateFactionTag =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<AgeUpState>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_HallTagAgeUpStateFactionTag;

        static readonly ComponentType[] QT_UnitTagFactionTag =
        {
            ComponentType.ReadOnly<UnitTag>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_UnitTagFactionTag;

        #endregion

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
        private static void RegisterTrainedUnit(ref SimpleAIState aiState, string unitId)
        {
            UnitClass cls = UnitFactory.GetUnitClass(unitId);
            if (IsCombatClass(cls))
            {
                aiState.DesiredMilitary++;
                // THE FLOOR'S UNIT MUST BE A LINE UNIT (2026-09-12). Support
                // and Magic count as combat -- correctly, they fight -- but
                // they are not what an army is MADE of, and they have one
                // trainer apiece. Adopting one as `LastMilitaryUnit` pointed
                // the entire army deficit at a single queue: log-proven on
                // Hollow Table, "deficit 135 x <caster> -- trainer
                // queue full", with 17,206 iron unspent in the military budget
                // and an army of 34. Training a caster is fine; letting the
                // caster BE the army program is not.
                if (cls != UnitClass.Support && cls != UnitClass.Magic)
                    aiState.LastMilitaryUnit = new FixedString64Bytes(unitId);
            }
            else if (cls == UnitClass.Worker || cls == UnitClass.Economy)
            {
                // Worker unification: the Worker trains as UnitClass.Economy but
                // carries WorkerTag and acts as a worker. Without counting Economy
                // here, DesiredWorkers never increments — ReplaceLostUnits would
                // see deficit=0 and stop replacing dead workers, gutting the
                // post-fight economy. (worker-unification fix)
                // (No ++ any more: DesiredWorkers is THE WORKER RULE, assigned
                // every maintenance pass — a ratchet here hired past it.)
            }
            // Scout/Support not auto-replaced for now — none of the current
            // build orders rely on them surviving in the same way.
        }

        /// <summary>
        /// Apply a SetVeilstoneTarget build-order step. Just clamps and writes the
        /// target on the AI brain's SimpleAIState — AssignIdleWorkers reads it on
        /// the next think tick. Always succeeds so the build order advances.
        /// </summary>
        private static bool SetVeilstoneTarget(ref SimpleAIState aiState, int count)
        {
            // Clamp at the system cap (4) so a typo in a build order can't
            // request 50 veilstone workers and starve iron entirely.
            // Cap held locally now that the mining allocator (which owned
            // MaxVeilstoneWorkers) is gone. The field is vestigial and is kept
            // only so existing build orders still parse.
            aiState.VeilstoneWorkerTarget = math.clamp(count, 0, 4);
            return true;
        }

        // ─────────────────────────────────────────────────────────────────
        // TRAIN UNIT
        // ─────────────────────────────────────────────────────────────────

        private static bool TryTrainUnit(EntityManager em, Faction faction, string unitId)
            => TryTrainUnitWithReason(em, faction, unitId, out _);

        private static readonly System.Collections.Generic.Dictionary<int, float> _nextWorkerRefusalLog
            = new System.Collections.Generic.Dictionary<int, float>();

        /// <summary>About once a minute per faction, name the method that
        /// asked for a Worker past the worker rule. The stack walk runs only
        /// when a line is actually written.</summary>
        private static void LogWorkerRefusal(Faction faction)
        {
            if (!AILogger.Enabled) return;
            float now = TheWaningBorder.Core.SimClock.Now;
            if (_nextWorkerRefusalLog.TryGetValue((int)faction, out float next) && now < next) return;
            _nextWorkerRefusalLog[(int)faction] = now + 60f;
            var frames = new System.Diagnostics.StackTrace(false).GetFrames();
            string caller = "?";
            if (frames != null)
                foreach (var f in frames)
                {
                    var m = f.GetMethod();
                    if (m == null || m.Name.StartsWith("TryTrainUnit") || m.Name == "LogWorkerRefusal") continue;
                    caller = m.Name;
                    break;
                }
            AILogger.Log(faction, "ECONOMY", $"extra Worker refused (worker rule) — asked by {caller}");
        }

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

            // THE WORKER RULE IS A CEILING, ENFORCED HERE (2026-10-03). Every
            // AI training request funnels through this method, so no caller
            // can hire past 3 + 1 per conquered territory. Batch round 3
            // measured factions at 17 workers on 3 territories after age-up
            // with every target-setting path already on the rule — the
            // refusal log names the caller so the stray path shows itself.
            if (unitId == "Worker"
                && CountAliveWorkers(em, faction)
                   + CountQueuedByPredicate(em, faction, isWorker: true) >= WorkerFloorFor(em, faction))
            {
                blockReason = "worker rule reached";
                LogWorkerRefusal(faction);
                return false;
            }

            // Find the right training building for this unit.
            Entity trainer = FindTrainerForUnit(em, faction, unitId);
            if (trainer == Entity.Null) { blockReason = "no trainer"; return false; }

            // Don't queue into a building still under construction.
            if (em.HasComponent<UnderConstruction>(trainer))
            { blockReason = "trainer under construction"; return false; }
            if (!em.HasBuffer<ProductionQueueItem>(trainer))
            { blockReason = "trainer has no queue"; return false; }

            // One queue for units, research and level-ups — see
            // CommandRouter.MaxProductionQueue.
            if (TheWaningBorder.Core.Commands.CommandRouter.IsProductionQueueFull(em, trainer))
            { blockReason = "trainer queue full"; return false; }

            // Capital seat (2026-08-11): an aged-up Alanthor faction that
            // still owes a capital unique (Ledger / King Lexor) keeps ONE
            // Fortress production slot free — the 5-slot queue stayed
            // permanently full of workers, so the hero never found an
            // opening ("trainer queue full" once a minute, all match).
            if ((unitId == "Worker" || unitId == "Scout")
                && em.HasComponent<HallTag>(trainer)
                && CultureConfig.GetCompletedCulture(em, faction) == Cultures.Alanthor
                && (!TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedKingLexor(em, faction)
                    || !TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedLedger(em, faction)))
            {
                int queued = TheWaningBorder.Core.Commands.CommandRouter
                    .GetProductionQueueLength(em, trainer);
                if (queued >= TheWaningBorder.Core.Commands.CommandRouter.MaxProductionQueue - 1)
                { blockReason = "capital seat reserved"; return false; }
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

            // PIVOTAL HOLD (2026-08-31): while the faction is saving a lump
            // sum (the expansion Hall), ARMY training pauses so the bank can
            // actually reach it. The fortress batch showed every faction
            // stuck at "saving for <territory> ... bank short" for entire
            // matches — military production out-earned the 90 s hold every
            // time, and not one Hall was claimed in 12 matches.
            //
            // Workers/Scouts are exempt ONLY UP TO THEIR FLOORS (batch 10).
            // The unconditional exemption backfired: during every hold the
            // only trainable units were Workers and Scouts, so they FILLED
            // THE POPULATION CAP — and once pop-capped, soldiers could never
            // spawn again. Armies froze at ~5 all match ("army first (5/8)"
            // for twenty straight minutes), which locked the army-first
            // claim gate and pinned every faction at exactly 3 territories
            // through three tuning rounds. The floor keeps the claim's
            // worker and the intel corps alive; it no longer eats the
            // army's population.
            // RESOURCE-AWARE (2026-10-03): only a unit that spends a resource
            // the save is short on is held — a veilstone-short Fortress save
            // no longer freezes supplies/iron infantry.
            var cost = AICommon.ToCost(def.cost);
            if (TheWaningBorder.AI.AIPivotalReserve.ShouldHold(em, faction, cost))
            {
                bool essential =
                    (unitId == "Worker"
                        && CountAliveWorkers(em, faction) < WorkerFloorFor(em, faction))
                    // Two scouts keep the intel corps alive through a save;
                    // the old flat 6 let 180 supplies of scouts eat the very
                    // pot the hold was protecting.
                    || (unitId == "Scout"
                        && CountAliveByUnitId(em, faction, "Scout") < 2)
                    // Army units below the claim gate are essential too
                    // (batch 18): rebuilding to MinArmyForNextClaim runs
                    // through holds — see the thermostat note in
                    // Expansion.TickClaims.
                    || (unitId != "Worker" && unitId != "Scout"
                        && CountAliveMilitary(em, faction) < Cfg.minArmyForNextClaim);
                // THE OPENING HUTS OUTRANK ALL OF THAT (2026-10-03): the
                // second scout and the claim-gate spearman were buying
                // themselves out of the starting bank ahead of the huts.
                // While the opening reserve is armed only the build crew
                // that raises the huts is essential.
                if (OpeningHutsPending(faction))
                    essential = unitId == "Worker"
                        && CountAliveWorkers(em, faction) < WorkerFloorFor(em, faction);
                if (!essential)
                {
                    blockReason = AILogger.Enabled
                        ? $"pivotal hold (saving) short {TheWaningBorder.AI.AIPivotalReserve.ShortResources(em, faction)}"
                        : "pivotal hold (saving)";
                    return false;
                }
            }

            // Affordability CHECK only — TrainCommandDirect spends on every
            // peer (docs/Multiplayer_LAN_Readiness.md); an AI-side Spend
            // here would double-charge the host and charge clients nothing.
            if (!FactionEconomy.CanAfford(em, faction, cost))
            { blockReason = "bank short"; return false; }

            // Through CommandRouter (CommandSource.AI) so host-AI training
            // replicates — a direct queue.Add spawned units on the host only.
            CommandRouter.IssueTrain(em, trainer, unitId, CommandSource.AI);
            InvalidateThinkMemo();   // a queue and (single-player) the bank moved
            return true;
        }

        private static Entity FindTrainerForUnit(EntityManager em, Faction faction, string unitId)
        {
            // The capital (Fortress) trains support units (Worker, Scout) and,
            // for Alanthor, the Ledger automaton and King Lexor — all four on
            // the Fortress SO's trains list.
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
                    return FindFactionBuilding<TempleTag>(em, faction);
            }

            // Data-driven fallback: resolve via the building defs' `trains`
            // lists so a roster change in the TechTree (e.g. the Swordsman ->
            // Spearman switch) can never silently strand the AI with an
            // untrainable unit again.
            if (TrainsUnit(em, "Fortress", unitId)) return FindFactionBuilding<HallTag>(em, faction);
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
        /// trainer instead of skipping the Train step. Null for capital-trained
        /// units (the capital is never built through this path).
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
                    return null; // capital-trained
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
                    return "TempleOfRidan";
            }
            // Data-driven fallback, same ladder as FindTrainerForUnit.
            if (TrainsUnit(em, "Fortress", unitId)) return null;
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
            // In flight counts as done for the ladder: the executor refuses a
            // second copy anyway (one-shot per faction, 2026-09-27), and
            // re-issuing it every think only logged refusals.
            if (TheWaningBorder.Core.Commands.CommandRouter.IsResearchQueued(
                    em, faction, techId, out _, out _)) return true;

            // Resolve a host that can actually TAKE the research now —
            // completed, research-capable, queue not full. The old
            // first-found lookup gambled on chunk order: with the hut
            // pipeline keeping one Gatherer's Hut permanently under
            // construction, the first-found hut could be that foundation
            // for an entire match, silently starving the Survey line.
            string researchAt = string.IsNullOrEmpty(def.researchAt) ? "Fortress" : def.researchAt;
            Entity bldg = researchAt switch
            {
                "Barracks"             => FindResearchHost<BarracksTag>(em, faction),
                // The capital (Shelter / Fortress) hosts the Age 0 bench and
                // the Alanthor tool ladder alike.
                "Fortress"             => FindResearchHost<HallTag>(em, faction),
                "ArcheryRange"         => FindResearchHost<ArcheryRangeTag>(em, faction),
                "GatherersHut"         => FindResearchHost<GathererHutTag>(em, faction),
                "Mine"                 => FindResearchHost<MineTag>(em, faction),
                "Hut"                  => FindResearchHost<HutTag>(em, faction),
                // Alanthor Age-1 research hosts (Wave 2 military tree).
                "Alanthor_RoyalStable" => FindResearchHost<RoyalStableTag>(em, faction),
                "Alanthor_SiegeYard"   => FindResearchHost<SiegeYardTag>(em, faction),
                // The Temple's own research (docs/Design/Religion.md §2).
                "TempleOfRidan"        => FindResearchHost<TempleOfRidanTag>(em, faction),
                // Sect buildings — each sells exactly its own sect's research
                // (docs/Design/Sects.md section 1).
                "Sect_Reliquary"       => FindResearchHost<ReliquaryTag>(em, faction),
                "Sect_MendingHall"     => FindResearchHost<MendingHallTag>(em, faction),
                "Sect_Stonehold"       => FindResearchHost<StoneholdTag>(em, faction),
                "Sect_Veilworks"       => FindResearchHost<VeilworksTag>(em, faction),
                "Sect_MusterYard"      => FindResearchHost<MusterYardTag>(em, faction),
                // The wall's own levels (Battlements, Shielded Ramparts) are
                // bought AT A WALL HUB (docs/Design/Age_1_Alanthor.md § The
                // four wall levels). Without this case the build orders'
                // optional Battlements step never found a host.
                "Alanthor_Wall"        => FindResearchHost<WallHubTag>(em, faction),
                _                      => Entity.Null,
            };
            if (bldg == Entity.Null)
            { blockReason = $"no ready {researchAt} host"; return false; }

            // PIVOTAL HOLD (2026-08-31): research spending waits out the
            // savings window like army training and building placement do —
            // only when the tech spends a resource the save is short on
            // (resource-aware, 2026-10-03).
            var cost = AICommon.ToCost(def.cost);
            if (TheWaningBorder.AI.AIPivotalReserve.ShouldHold(em, faction, cost))
            { blockReason = "pivotal hold (saving)"; return false; }

            // Affordability CHECK only — ResearchCommandDirect spends on
            // every peer (docs/Multiplayer_LAN_Readiness.md).
            if (!FactionEconomy.CanAfford(em, faction, cost))
            { blockReason = "bank short"; return false; }

            // Through CommandRouter (CommandSource.AI) so host-AI research
            // replicates to clients in multiplayer.
            TheWaningBorder.Core.Commands.CommandRouter.IssueResearch(em, bldg, techId,
                TheWaningBorder.Core.Commands.CommandSource.AI);
            InvalidateThinkMemo();
            return true;
        }
        // ─────────────────────────────────────────────────────────────────
        // AGE UP
        // ─────────────────────────────────────────────────────────────────

        /// <summary>Sim-time before which a faction must not re-issue its
        /// age-up. See the latch-on-outcome note below.</summary>
        private static readonly System.Collections.Generic.Dictionary<Faction, float>
            _ageUpRetryAt = new();

        /// <summary>True while any of this faction's halls carries a ticking
        /// AgeUpState — the age-up is in flight and must not be re-bought.</summary>
        private static bool FactionHasAgeingHall(EntityManager em, Faction faction)
        {
            var q = QC_HallTagAgeUpStateFactionTag.Get(em, QT_HallTagAgeUpStateFactionTag);
            // NOT disposed: the query is cached and reused. This call site
            // used to create a fresh one per call and dispose it, which is
            // the only reason a Dispose was ever correct here — disposing a
            // cached query kills it for every later call
            // (ObjectDisposedException in ToComponentDataArray).
            using var facs = q.ToComponentDataArray<FactionTag>(Unity.Collections.Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) return true;
            return false;
        }

        private bool TryAgeUp(EntityManager em, Faction faction, ref SimpleAIState aiState)
        {
            // THE AGE-UP IS THE LANDMARK (Age_0.md § Age-up by landmark,
            // 2026-09-29). There is nothing to issue: the landmark finishing
            // construction ages the faction up on every peer. This only
            // observes the outcome — latched once the era has advanced — and
            // places the landmark if the faction has none (the director and
            // the AgeUp goal both land here).
            if (aiState.AgeUpIssued != 0) return true;
            if (FactionEra(em, faction) >= 2)
            {
                aiState.AgeUpIssued = 1;
                return true;
            }
            if (!FactionHasLandmark(em, faction))
                TryBuildBuilding(em, faction, AgeUpLandmark(em, faction));
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // REPLACE LOST UNITS
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Re-queue training for any military/worker units that died after the
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

        /// <summary>Last "unaffordable, training X instead" pair logged, per
        /// faction — the line repeats only when the pair changes.</summary>
        private static readonly System.Collections.Generic.Dictionary<Faction, string> _lastAffordFallback
            = new System.Collections.Generic.Dictionary<Faction, string>();

        private static void ReplaceLostUnits(EntityManager em, Entity brainEntity, Faction faction,
            ref SimpleAIState aiState, RoleBudget budget, float intelFreshness, float now)
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
                    // ORDERS MUST NOT BE THE LIMIT (2026-09-12, Game_AI.md
                    // 6c). Three queue attempts per think tick was written
                    // when a faction had one or two trainers. The whole point
                    // of the snowball is scores of production buildings all
                    // busy, and a flat three would leave most of them idle no
                    // matter how many were raised -- the AI would build the
                    // capacity and then decline to use it. Give it one attempt
                    // per finished trainer, so every building it paid for can
                    // take an order on the same tick.
                    int refill = math.min(deficit, math.max(3, CountMilitaryTrainers(em, faction)));
                    int trained = 0;
                    string floorBlock = null;
                    for (int t = 0; t < refill; t++)
                    {
                        if (!TryTrainUnitBudgeted(em, faction,
                                aiState.LastMilitaryUnit.ToString(), AIBudgetCategory.Military,
                                out floorBlock))
                            break;
                        trained++;
                    }

                    // THE BANK CANNOT PAY FOR THIS UNIT — TRAIN ONE IT CAN
                    // (2026-10-03). The 0.0.33 60-minute batch: every Alanthor
                    // AI held 100,000 supplies and 30,000 iron, 10-430
                    // veilstone, an army of 2-80 against a 300 cap, and its
                    // top military line (~800x) was "deficit N x
                    // Alanthor_Catapult — Military budget short". The floor
                    // re-asked for the same veilstone unit every think while
                    // the Spearman and the Archer — no veilstone at all —
                    // went unbought. TryTrainUnitBudgeted has now recorded the
                    // refusal (the unit is passed over for
                    // unaffordableUnitCooldownSeconds), so re-picking returns
                    // the next-best unit the bank CAN pay for.
                    // Each refused pick is recorded, so each re-pick steps one
                    // unit further down; a few steps reach the veilstone-free
                    // bottom of the ladder within the same think.
                    string refused = aiState.LastMilitaryUnit.ToString();
                    string tried = refused;
                    const string BudgetShort = " budget short";
                    const int MaxAffordRepicks = 4;   // loop bound, not tuning
                    for (int attempt = 0; attempt < MaxAffordRepicks && trained == 0
                         && floorBlock != null
                         && floorBlock.StartsWith(AIBudgetCategory.Military + BudgetShort); attempt++)
                    {
                        string alt = PickCompositionUnit(em, brainEntity, faction, now,
                            budget, intelFreshness);
                        if (string.IsNullOrEmpty(alt) || alt == tried) break;
                        tried = alt;
                        for (int t = 0; t < refill; t++)
                        {
                            if (!TryTrainUnitBudgeted(em, faction, alt,
                                    AIBudgetCategory.Military, out floorBlock))
                                break;
                            trained++;
                        }
                        if (trained == 0) continue;

                        aiState.LastMilitaryUnit = new FixedString64Bytes(alt);
                        string pair = refused + ">" + alt;
                        if (!_lastAffordFallback.TryGetValue(faction, out string prevPair)
                            || prevPair != pair)
                        {
                            _lastAffordFallback[faction] = pair;
                            AILogger.Log(faction, "MILITARY",
                                $"{refused} unaffordable ({ShortOf(em, faction, refused)}) — " +
                                $"training {alt} instead (repeats suppressed until the pair changes)");
                        }
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
                        // A BACKED-UP TRAINER IS AS USELESS AS A MISSING ONE
                        // (2026-09-12). This rescued the floor only when the
                        // trainer had been DESTROYED. But `LastMilitaryUnit`
                        // is whatever the composition picker last chose, and
                        // when that is a support unit with a single trainer --
                        // a single caster -- the whole army deficit queues
                        // behind one full queue and stays there. Log-proven,
                        // Hollow Table 2026-09-12, with the named-reason log
                        // added the same day:
                        //     "floor blocked ~1 min: deficit 135 x
                        //      <caster> -- trainer queue full"
                        // Blue held 17,206 iron in its MILITARY budget alone
                        // and an army of 34. One support caster was absorbing
                        // the entire army program.
                        //
                        // So fall back when the trainer is gone OR when it is
                        // permanently full, and treat a support unit as never
                        // being the right answer to an army deficit.
                        Entity floorTrainer =
                            FindTrainerForUnit(em, faction, aiState.LastMilitaryUnit.ToString());
                        bool trainerUnusable =
                            floorTrainer == Entity.Null
                            || TheWaningBorder.Core.Commands.CommandRouter
                                   .IsProductionQueueFull(em, floorTrainer);
                        if (trainerUnusable
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
                            // NAME THE GATE (2026-09-12). This used to print
                            // "(trainer missing/queue full/wallet or bank
                            // short)" -- three guesses and no answer -- while
                            // Blue sat on 2,896 supplies and 20,505 iron with
                            // a deficit of 20 Spearmen and two Barracks
                            // standing. Every one of those causes needs a
                            // different fix and the log could not tell them
                            // apart, so the army stayed at four units and the
                            // evidence to say why did not exist. The pre-flight
                            // already knows which gate closed; carry it out.
                            AILogger.Log(faction, "MILITARY",
                                $"floor blocked ~1 min: deficit {deficit} x {aiState.LastMilitaryUnit} " +
                                $"— {floorBlock ?? "reason not reported"}");
                        }
                        _floorBlockTicks[faction] = ticks;
                    }
                    else
                        _floorBlockTicks[faction] = 0;
                }
            }

            // Worker deficit
            if (aiState.DesiredWorkers > 0)
            {
                int aliveMin = CountAliveWorkers(em, faction);
                int queuedMin = CountQueuedByPredicate(em, faction, isWorker: true);
                int deficit = aiState.DesiredWorkers - (aliveMin + queuedMin);
                if (deficit > 0)
                {
                    // Worker handles both build + mine since the merge —
                    // train "Worker" (the unified factory), it carries
                    // WorkerTag too so it'll auto-find deposits.
                    TryTrainUnitBudgeted(em, faction, "Worker", AIBudgetCategory.EconomyExpansion);
                }
            }
        }
        /// <summary>
        /// LAYER 3 — the composition pick. See AIComposition.cs for the layer
        /// contract; this is its implementation.
        ///
        /// <paramref name="budget"/> is the shape layer 2 asked for; enemy
        /// intel then bends it, because a counter outranks a preference. Each
        /// call returns whichever unit the CURRENT army is short of, so
        /// successive trains converge on the mix.
        ///
        /// <paramref name="intelFreshness"/> is layer 1's only say in this:
        /// how stale a sighting may be and still steer production. It replaced
        /// a bool that let a difficulty tier switch countering off entirely.
        /// </summary>
        private static string PickCompositionUnit(
            EntityManager em, Entity brainEntity, Faction faction, float now,
            RoleBudget budget, float intelFreshness)
        {
            // THE ROSTER HALF IS MEMOISED PER THINK (2026-09-25 AI perf pass).
            // One think called this up to five times (the maintenance steer,
            // a training burst of 1-3, the goal list), and each call walked
            // every unit, every sighting, the whole unit catalog with a
            // trainer lookup per id, and built a Dictionary. Everything that
            // walk reads — who is alive, what was sighted, which trainers
            // stand — is fixed for the length of a think; only the bank can
            // move between calls, so the bank-dependent tail below is still
            // evaluated live every time.
            ref var c = ref _compMemo;
            if (c.Stamp != _thinkStamp || c.F != faction || c.Brain != brainEntity)
            {
                c = ComputeComposition(em, brainEntity, faction, now, budget, intelFreshness);
                c.Stamp = _thinkStamp; c.F = faction; c.Brain = brainEntity;
            }
            return FinishComposition(em, faction, budget, in c);
        }

        private struct CompositionMemo
        {
            public int Stamp; public Faction F; public Entity Brain;
            public int OwnMelee, OwnRanged, OwnCav, OwnSiege;
            public float DesiredRangedFrac;
            public bool CavHeavy, RangedHeavy, RangedTrainerMissing;
            public string Melee, Ranged, Cavalry, Siege, Spread;
        }
        private static CompositionMemo _compMemo;

        private static CompositionMemo ComputeComposition(
            EntityManager em, Entity brainEntity, Faction faction, float now,
            RoleBudget budget, float intelFreshness)
        {
            // Own composition.
            int ownMelee = 0, ownRanged = 0, ownCav = 0, ownSiege = 0;
            var q = QC_UnitTagFactionTag.Get(em, QT_UnitTagFactionTag);
            using (var ents = q.ToEntityArray(Allocator.Temp))
            using (var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < tags.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    var c = tags[i].Class;
                    if (!IsCombatClass(c)) continue;
                    // Cavalry first: a Cataphract is UnitClass.Melee, so
                    // counting by class alone would file the whole horse arm
                    // under melee and the cavalry share would never open.
                    if (em.HasComponent<CavalryTag>(ents[i])) ownCav++;
                    else if (c == UnitClass.Siege) ownSiege++;
                    else if (c == UnitClass.Ranged) ownRanged++;
                    else ownMelee++;
                }
            }

            float desiredRangedFrac = budget.RangedFrac;
            bool cavHeavy = false;
            bool rangedHeavy = false;
            // Countering is UNCONDITIONAL. Only the freshness window varies.
            if (em.HasBuffer<EnemySightingRecord>(brainEntity))
            {
                var buffer = em.GetBuffer<EnemySightingRecord>(brainEntity);
                int meleeStr = 0, rangedStr = 0, cavStr = 0;
                for (int i = 0; i < buffer.Length; i++)
                {
                    var rec = buffer[i];
                    if (rec.Category != IntelCategory.MilitaryUnit) continue;
                    if (now - rec.LastSeenTime > intelFreshness) continue;
                    if (!em.Exists(rec.Enemy) || !em.HasComponent<UnitTag>(rec.Enemy)) continue;
                    var cls = em.GetComponentData<UnitTag>(rec.Enemy).Class;
                    if (em.HasComponent<CavalryTag>(rec.Enemy)) cavStr += rec.EstStrength;
                    else if (cls == UnitClass.Ranged || cls == UnitClass.Siege) rangedStr += rec.EstStrength;
                    else meleeStr += rec.EstStrength;
                }
                cavHeavy = cavStr * 2 > meleeStr + rangedStr;
                rangedHeavy = !cavHeavy && rangedStr > meleeStr * 3 / 2;

                if (cavHeavy) desiredRangedFrac = 0.25f;                               // spear wall vs cavalry
                else if (meleeStr > rangedStr * 3 / 2) desiredRangedFrac = 0.6f;       // shoot the melee blob
                // MASSED ARCHERS ARE ANSWERED BY HORSES, NOT BY THICKER
                // INFANTRY. This branch used to read desiredRangedFrac = 0.25
                // and call it "close the gap" — which bought MELEE, and the
                // melee ladder then preferred the heaviest, slowest unit it
                // could train. Walking a Sentinel (speed 5.0) into upgraded
                // Longbowmen (25 damage a shot) is not closing a gap, it is
                // feeding one: observed Sundered Crown 2026-09-09, where 106
                // archers melted Sentinels and the AI kept queueing more.
                //
                // The counter is reach, and reach here means SPEED — Outrider
                // 8.2 and Cataphract 6.6 against a melee ladder that tops out
                // at 5.7. So a ranged-heavy read does not touch the melee/
                // ranged split much; it opens the cavalry share below, and
                // keeps enough of our own ranged to trade at distance.
                else if (rangedHeavy) desiredRangedFrac = 0.35f;
            }

            // Class choice as before; the unit WITHIN the class follows the
            // age meta ladder (2026-08-11: the picker only ever returned
            // Archer/Spearman, so the AI shipped an Age-0 army all game).
            // Ranged: Longbowman (Range L3) > Crossbowman (L2) > Archer.
            // Melee: Swordsman > Spearman — EXCEPT under cavalry pressure,
            // where the spear wall is the counter and stays the pick.
            string melee = "Spearman";
            // NOT the bare "Archer": no such unit is authored. The only ids the
            // Archery Range trains are Alanthor_Archer / _Crossbowman /
            // _Longbowman, and asking for "Archer" logged
            // "[TechCatalog] No UnitDefSO for 'Archer'" and spawned a unit whose
            // every stat was a stub (Sundered Crown 2026-09-09, the match's only
            // error). Same culture-prefix trap as the roster matchers.
            string ranged = "Alanthor_Archer";
            if (FactionCultureOf(em, faction) == Cultures.Alanthor)
            {
                ranged = FirstTrainable(em, faction,
                    "Alanthor_Longbowman", "Alanthor_Crossbowman", "Alanthor_Archer");
                if (cavHeavy)
                {
                    // spear wall — melee stays Spearman
                }
                else if (rangedHeavy)
                {
                    // No stable yet and archers on the field: take the FASTEST
                    // body available, not the toughest. This is the fallback
                    // for the cavalry branch below being empty; a heavier unit
                    // only spends longer under fire crossing the same ground.
                    melee = FirstTrainable(em, faction,
                        "Alanthor_Nobleman", "Alanthor_Swordsman", "Spearman");
                }
                else
                {
                    melee = FirstTrainable(em, faction,
                        "Alanthor_Swordsman", "Spearman");
                }
            }

            // ── CAVALRY AND SIEGE. ──
            //
            // The ladder had only a melee line and a ranged line, so the AI
            // could not ASK for a Cataphract or a Catapult no matter what it
            // had built. One logged AI raised a Royal Stable at 12:43 and a
            // Siege Yard at 17:58 and trained neither, because nothing in this
            // method can name their units; over 30 minutes four AIs produced
            // 23 Workers, 17 Spearmen and 4 Scouts and nothing else.
            //
            // That is also why veilstone piled to 6,000-15,000 unspent. The
            // Age-0 line costs supplies and iron and NO veilstone
            // (Spearman 80/30/0, Archer 50/25/0), while the units that cost it
            // are exactly these two branches — Cataphract 320/120/60,
            // Catapult 180/80/40, Trebuchet 320/180/100. The sink was never
            // missing from the design; it was unreachable by the AI.
            string cavalry = null, siege = null;
            if (FactionCultureOf(em, faction) == Cultures.Alanthor)
            {
                cavalry = FirstTrainableOrNull(em, faction,
                    "Alanthor_Cataphract", "Alanthor_Outrider");
                siege = FirstTrainableOrNull(em, faction,
                    "Alanthor_Trebuchet", "Alanthor_Catapult", "Alanthor_Ballista");
            }

            return new CompositionMemo
            {
                OwnMelee = ownMelee, OwnRanged = ownRanged, OwnCav = ownCav, OwnSiege = ownSiege,
                DesiredRangedFrac = desiredRangedFrac,
                CavHeavy = cavHeavy, RangedHeavy = rangedHeavy,
                Melee = melee, Ranged = ranged, Cavalry = cavalry, Siege = siege,
                // ── TRAIN EVERY UNIT THE CULTURE OWNS ── (see FinishComposition)
                Spread = LeastRepresentedTrainable(em, faction),
                // Ranged is an Age-1 unlock (2026-08-11): with no Archery
                // Range standing the ranged pick has no trainer.
                RangedTrainerMissing = FindTrainerForUnit(em, faction, ranged) == Entity.Null,
            };
        }

        /// <summary>The bank-dependent tail of <see cref="PickCompositionUnit"/>,
        /// run live on every call over the per-think roster memo.</summary>
        private static string FinishComposition(EntityManager em, Faction faction,
            RoleBudget budget, in CompositionMemo c)
        {
            int ownMelee = c.OwnMelee, ownRanged = c.OwnRanged, ownCav = c.OwnCav, ownSiege = c.OwnSiege;
            bool rangedHeavy = c.RangedHeavy;
            string melee = c.Melee, ranged = c.Ranged, cavalry = c.Cavalry, siege = c.Siege;
            float desiredRangedFrac = c.DesiredRangedFrac;

            int totalArmy = ownMelee + ownRanged + ownCav + ownSiege;

            // SPEND WHAT YOU ARE DROWNING IN. A bank fat with veilstone and
            // thin on supplies cannot buy another Spearman but CAN buy the
            // heavy line, so surplus veilstone pulls the composition toward
            // the branches priced in it. This is the difference between an
            // economy with a sink and a number that only goes up.
            bool veilstoneRich = false;
            if (FactionEconomy.TryGetBank(em, faction, out var compBank))
            {
                var res = em.GetComponentData<FactionResources>(compBank);
                veilstoneRich = res.Veilstone > 600 && res.Veilstone > res.Supplies;
            }

            // The personality's shape is the baseline; a fat bank widens it.
            float cavFrac = veilstoneRich ? budget.CavalryFrac * 1.6f : budget.CavalryFrac;
            float siegeFrac = veilstoneRich ? budget.SiegeFrac * 2f : budget.SiegeFrac;

            // THE CAVALRY SHARE HAS TO ANSWER THE ENEMY, not just the bank.
            // It was a flat 0.18/0.30 that never once read what it was
            // fighting: facing an all-archer army produced the same horse
            // count as facing nothing. Against a ranged-heavy enemy the horse
            // IS the counter, so it becomes the bulk of new production until
            // the army actually holds enough of them.
            if (rangedHeavy) cavFrac = math.max(cavFrac, 0.45f);

            // A unit the bank could not pay for a moment ago is passed over
            // until the bank covers it again (RecordUnaffordable) — the next
            // line takes the order instead of the same refusal repeating.
            if (cavalry != null && totalArmy > 0 && ownCav < totalArmy * cavFrac
                && !IsPassedOver(em, faction, cavalry))
                return cavalry;
            if (siege != null && totalArmy >= 4 && ownSiege < totalArmy * siegeFrac
                && !IsPassedOver(em, faction, siege))
                return siege;

            // ── TRAIN EVERY UNIT THE CULTURE OWNS. ──
            //
            // The ladder above resolves the BEST unit per line, and the shares
            // decide which line. That still only ever produced the four lines
            // named here: across 26 measured matches Workers were 45% of all
            // units, Spearmen 26%, Archers 13%, and the entire Age-1 roster —
            // Swordsman, Crossbowman, Nobleman, Sentinel, Cataphract, Outrider,
            // Catapult, Trebuchet — came to under 1% combined. A roster that is
            // never built may as well not exist, for the player watching and
            // for balance.
            //
            // So after the line is chosen, look across EVERY trainable unit and
            // take whichever is furthest below an even share. The line logic
            // still leads (it carries the counter-composition read); this stops
            // the roster collapsing to two ids.
            string spread = c.Spread;
            if (spread != null && IsPassedOver(em, faction, spread))
                spread = LeastRepresentedTrainable(em, faction, skipPassedOver: true);
            if (spread != null) return spread;

            // Each line steps DOWN its own ladder past a unit the bank cannot
            // pay for: the Swordsman (veilstone) gives way to the Spearman,
            // the Longbowman and Crossbowman (veilstone) to the Archer. The
            // bottom rung of each line costs no veilstone at all.
            melee = FirstUsable(em, faction, melee, "Spearman") ?? melee;
            if (!c.RangedTrainerMissing)
                ranged = FirstUsable(em, faction, ranged,
                    "Alanthor_Crossbowman", "Alanthor_Archer") ?? ranged;

            // Ranged is an Age-1 unlock (2026-08-11): with no Archery Range
            // standing (era 1 cannot build one, or it was razed), the ranged
            // pick has no trainer — train the melee line instead of feeding
            // the "floor blocked" retry loop.
            if (c.RangedTrainerMissing)
                return melee;

            int total = ownMelee + ownRanged;
            if (total == 0) return melee;
            string pick = ownRanged < total * desiredRangedFrac ? ranged : melee;
            // Both lines still refused: whichever the bank can pay for.
            if (IsPassedOver(em, faction, pick))
            {
                string other = pick == ranged ? melee : ranged;
                if (!IsPassedOver(em, faction, other)) pick = other;
            }
            return pick;
        }

        // ── UNAFFORDABLE-UNIT MEMORY (2026-10-03) ─────────────────────────
        //
        // The AI silent-failure pattern again: the military floor asked for a
        // Catapult the bank could not pay for, nothing recorded the refusal,
        // and the next think asked for the same Catapult — ~800 times in a
        // 60-minute match while 100,000 supplies sat unspent. A refused unit
        // is now remembered for unaffordableUnitCooldownSeconds, and the
        // picker passes it over meanwhile — unless the bank covers it again
        // first, in which case it is the right unit and is bought.
        //
        // Keyed lookups only (never iterated), simulated time: deterministic.

        private static readonly Dictionary<(Faction faction, string unitId), float> _unitUnaffordableUntil
            = new Dictionary<(Faction, string), float>();

        /// <summary>The bank could not pay for this combat unit just now.</summary>
        private static void RecordUnaffordable(Faction faction, string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return;
            _unitUnaffordableUntil[(faction, unitId)] =
                TheWaningBorder.Core.SimClock.Now + Cfg.unaffordableUnitCooldownSeconds;
        }

        /// <summary>True while the picker should skip this unit: refused
        /// recently, and the bank still cannot pay for it — or it spends a
        /// resource the pivotal savings hold is short on.</summary>
        private static bool IsPassedOver(EntityManager em, Faction faction, string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return false;
            // RESOURCE-AWARE HOLD (2026-10-03): while a save is short on, say,
            // veilstone, a veilstone unit would only be refused by the hold
            // in TryTrainUnitWithReason — and the roster spread favours the
            // least-represented (usually the veilstone) units, so the army
            // stalled on the same refusal. Pass it over so the line steps
            // down to a unit the hold lets through.
            if (TechCatalog.TryGetUnit(unitId, out var heldDef) && heldDef != null
                && TheWaningBorder.AI.AIPivotalReserve.ShouldHold(em, faction,
                       AICommon.ToCost(heldDef.cost)))
                return true;
            if (!_unitUnaffordableUntil.TryGetValue((faction, unitId), out float until)) return false;
            if (TheWaningBorder.Core.SimClock.Now >= until)
            {
                _unitUnaffordableUntil.Remove((faction, unitId));
                return false;
            }
            if (TechCatalog.TryGetUnit(unitId, out var def) && def != null
                && FactionEconomy.CanAfford(em, faction, AICommon.ToCost(def.cost)))
                return false;
            return true;
        }

        /// <summary>The first candidate that is trainable now and not passed
        /// over, or null.</summary>
        private static string FirstUsable(EntityManager em, Faction faction, params string[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id) || IsPassedOver(em, faction, id)) continue;
                if (!TechCatalog.TryGetUnit(id, out var def) || def == null) continue;
                Entity trainer = FindTrainerForUnit(em, faction, id);
                if (trainer == Entity.Null) continue;
                if (em.HasComponent<UnderConstruction>(trainer)) continue;
                if (!CommandRouter.CanTrainAtBuilding(em, trainer, id, out _, out _)) continue;
                return id;
            }
            return null;
        }

        /// <summary>What the bank is short of for this unit, for the log.</summary>
        private static string ShortOf(EntityManager em, Faction faction, string unitId)
        {
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return "no def";
            if (!FactionEconomy.TryGetResources(em, faction, out var res)) return "no bank";
            var cost = AICommon.ToCost(def.cost);
            string s = null;
            if (res.Supplies  < cost.Supplies)  s += $", supplies {res.Supplies}/{cost.Supplies}";
            if (res.Iron      < cost.Iron)      s += $", iron {res.Iron}/{cost.Iron}";
            if (res.Veilstone < cost.Veilstone) s += $", veilstone {res.Veilstone}/{cost.Veilstone}";
            if (res.Veilsteel < cost.Veilsteel) s += $", veilsteel {res.Veilsteel}/{cost.Veilsteel}";
            return s != null ? "short of " + s.Substring(2) : "reserved savings";
        }

        /// <summary>
        /// Every combat unit this faction can actually train right now, and
        /// which of them it owns fewest of relative to an even spread.
        ///
        /// Returns null when nothing is under-represented, so the caller's
        /// composition logic still decides the ordinary case. Deliberately
        /// skips heroes and uniques — HeroTrainLimit owns those, and a
        /// one-per-player unit can never reach an even share.
        /// </summary>
        private static string LeastRepresentedTrainable(EntityManager em, Faction faction,
            bool skipPassedOver = false)
        {
            var ids = TrainableCombatIds(em, faction);
            if (ids.Count < 2) return null;

            // Counted against FixedString keys (a ToString per living unit
            // used to allocate one managed string each, every call).
            _rosterKeys.Clear();
            _rosterCounts.Clear();
            for (int k = 0; k < ids.Count; k++)
            {
                _rosterKeys.Add(new FixedString64Bytes(ids[k]));
                _rosterCounts.Add(0);
            }
            int total = 0;

            var q = QC_UnitTypeIdFactionTag.Get(em, QT_UnitTypeIdFactionTag);
            using (var uids = q.ToComponentDataArray<UnitTypeId>(Allocator.Temp))
            using (var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp))
                for (int i = 0; i < uids.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    var id = uids[i].Value;
                    for (int k = 0; k < _rosterKeys.Count; k++)
                        if (_rosterKeys[k] == id) { _rosterCounts[k]++; total++; break; }
                }

            // Even share across the roster, with a floor so the check still
            // bites while the army is small.
            float share = math.max(2f, total / (float)ids.Count);
            string worst = null; float worstGap = 0f;
            for (int k = 0; k < ids.Count; k++)
            {
                if (skipPassedOver && IsPassedOver(em, faction, ids[k])) continue;
                float gap = share - _rosterCounts[k];
                if (gap > worstGap) { worstGap = gap; worst = ids[k]; }
            }
            return worst;
        }

        /// <summary>Combat units with a standing, buildable trainer.</summary>
        // Host-only scratch for the roster walk.
        private static readonly List<FixedString64Bytes> _rosterKeys = new List<FixedString64Bytes>(16);
        private static readonly List<int> _rosterCounts = new List<int>(16);
        private static readonly List<string> _trainableIds = new List<string>(16);
        private static int _trainableStamp; private static Faction _trainableFaction;

        /// <summary>Combat units with a standing, buildable trainer.
        /// Memoised per think (the answer depends on the roster of trainers,
        /// fixed for a think); the returned list is shared scratch — read it,
        /// do not keep it.</summary>
        private static List<string> TrainableCombatIds(EntityManager em, Faction faction)
        {
            if (_trainableStamp == _thinkStamp && _trainableFaction == faction) return _trainableIds;
            _trainableStamp = _thinkStamp;
            _trainableFaction = faction;
            var outIds = _trainableIds;
            outIds.Clear();
            if (!TechCatalog.IsReady) return outIds;

            // CULTURE GATE. Without it this list carries every culture's roster
            // at once, because an Age 0 Barracks legitimately trains
            // Alanthor_* and Feraldis_* ids from the same def. The spreader
            // below then picks whatever is furthest below an even share -- and
            // a unit the faction can never legitimately own is permanently the
            // most under-represented thing there is. Measured over 13 matches
            // with no Feraldis player in any of them, Feraldis_Spearman was
            // 28.7% of every unit alive.
            byte culture = FactionCultureOf(em, faction);

            foreach (var def in TechCatalog.GetAllUnits())
            {
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                if (!TheWaningBorder.Data.CultureGate.CanFactionTrain(def.id, culture)) continue;
                if (TheWaningBorder.Data.CultureGate.IsSupersededByCulture(def.id, culture)) continue;
                if (!IsCombatClass(UnitFactory.GetUnitClass(def.id))) continue;
                // Uniques are rationed by HeroTrainLimit, not by composition.
                if (TheWaningBorder.Abilities.HeroTrainLimit.IsKingLexorId(def.id)
                    || TheWaningBorder.Abilities.HeroTrainLimit.IsLedgerId(def.id)) continue;

                var trainer = FindTrainerForUnit(em, faction, def.id);
                if (trainer == Entity.Null) continue;
                if (em.HasComponent<UnderConstruction>(trainer)) continue;
                if (!CommandRouter.CanTrainAtBuilding(em, trainer, def.id, out _, out _)) continue;
                outIds.Add(def.id);
            }
            return outIds;
        }

        /// <summary>
        /// Like <see cref="FirstTrainable"/>, but returns NULL when none of the
        /// candidates can be trained instead of falling back to the last one.
        ///
        /// The fallback is right for the melee and ranged lines, which always
        /// have an Age-0 answer. It is wrong for cavalry and siege, which
        /// simply do not exist until the building does — falling back would
        /// name a unit with no trainer and feed the "floor blocked" retry loop
        /// every tick.
        /// </summary>
        private static string FirstTrainableOrNull(EntityManager em, Faction faction,
            params string[] priority)
        {
            for (int i = 0; i < priority.Length; i++)
            {
                string id = priority[i];
                if (!TechCatalog.TryGetUnit(id, out var def) || def == null) continue;
                Entity trainer = FindTrainerForUnit(em, faction, id);
                if (trainer == Entity.Null) continue;
                if (em.HasComponent<UnderConstruction>(trainer)) continue;
                if (!CommandRouter.CanTrainAtBuilding(em, trainer, id, out _, out _)) continue;
                return id;
            }
            return null;
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
