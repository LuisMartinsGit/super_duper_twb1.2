// SimpleAISystem.cs
// Build-order driven AI for the Age-1 phase.
//
// One AIBrain entity per AI faction. Each think tick, the AI looks at the next
// step of its assigned build order and tries to issue it (queue a unit, place
// a building, queue a research, or trigger age-up). On success, it advances to
// the next step. On failure (resource shortfall, no idle builder, queue full),
// it waits for the next tick.
//
// Replaces the old AIBrain / Manager / Behavior multi-system architecture.
// Location: Assets/Scripts/AI/SimpleAISystem.cs

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
    /// <summary>
    /// Per-faction build-order executor. Not Bursted (touches managed
    /// TechTreeDB / FactionResearchState / Debug.Log).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
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

        // How many items can pile up in a building's TrainQueueItem buffer
        // before the AI defers a queue-train step. Low enough that the AI
        // doesn't blindly stack 50 miners; high enough to keep Hall busy.
        private const int MaxTrainQueue = 5;

        // Build placement scan ring: how far from the Hall and at how many
        // angles we try before giving up on this tick. The min was bumped to
        // 10 m and max to 30 m so buildings have room to fan out around the
        // Hall without crowding it (and around each other — see spacing below).
        private const float BuildRingDistanceMin = 10f;
        private const float BuildRingDistanceMax = 30f;
        private const int BuildAngleSamples = 24;

        // 64-bit splitmix RNG seeded per-faction for placement angles + skip rolls.
        private uint _rngState = 0x12345678u;

        // ─────────────────────────────────────────────────────────────────
        // ARMY MISSIONS (AoE4-style encounters)
        //
        // Military is organized into persistent missions instead of a single
        // fire-and-forget blob: each mission owns its members, its objective,
        // and its own lifecycle (success -> regroup home; outmatched ->
        // retreat just that army; timeout -> disband). A small fast RAID
        // party harasses the enemy economy while the main ATTACK army pushes
        // the scored objective — mirroring the army/encounter structure of
        // the Relic-lineage RTS AIs (CoH / AoE4).
        //
        // Managed state: the AI runs host-only, nothing here replicates;
        // every effect flows out as ordinary unit commands.
        // ─────────────────────────────────────────────────────────────────

        private enum MissionType : byte { Attack, Raid }

        // Attack-mission phases (AoE4-plus: forward staging). Direct missions
        // march straight at the objective; staged missions (Hard+) first form
        // up at a point near the target on the home side, then commit at full
        // strength — fixing AoE4's documented always-rally-at-homebase habit.
        private enum MissionPhase : byte { Direct = 0, Staging = 1, Striking = 2 }

        private sealed class Mission
        {
            public MissionType Type;
            public MissionPhase Phase;
            public Entity Target;
            public float3 TargetPos;
            public float3 StagePos;
            public float StartTime;
            public readonly System.Collections.Generic.List<Entity> Members
                = new System.Collections.Generic.List<Entity>();
        }

        private const float MissionTimeoutSeconds = 180f;
        // Forward staging: form up this far from the target (home side), and
        // commit once the army centroid is within the gather radius (or the
        // staging phase times out — stragglers must not stall the push).
        private const float StagingDistance = 30f;
        private const float StagingGatherRadius = 12f;
        private const float StagingTimeoutSeconds = 60f;
        private const int RaidPartySize = 3;
        // Launch a raid alongside the attack only when this many EXTRA idle
        // units exist beyond the attack threshold.
        private const int RaidSurplus = 2;

        private readonly System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Mission>> _missions
            = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<Mission>>();

        private System.Collections.Generic.List<Mission> MissionsFor(Faction f)
        {
            int key = (int)f;
            if (!_missions.TryGetValue(key, out var list))
            {
                list = new System.Collections.Generic.List<Mission>();
                _missions[key] = list;
            }
            return list;
        }

        protected override void OnCreate()
        {
            RequireForUpdate<AIBrain>();
            _missions.Clear();

            // Deterministic, match-specific RNG seed. Under the fixed-step
            // lockstep this stream advances identically on every client (same
            // number of updates, same call order), so a shared seed is enough;
            // seeding from SpawnSeed (instead of the old hardcoded constant)
            // also varies AI behaviour per match without breaking determinism.
            _rngState = (uint)GameSettings.SpawnSeed * 747796405u + 2891336453u;
            if (_rngState == 0u) _rngState = 0x12345678u;
        }

        protected override void OnUpdate()
        {
            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;
            var perfSw = System.Diagnostics.Stopwatch.StartNew();
            int perfThinks = 0;

            // Snapshot brains so we can mutate ECS state freely inside the loop.
            var brainsQuery = SystemAPI.QueryBuilder().WithAll<AIBrain, SimpleAIState>().Build();
            using var brainEntities = brainsQuery.ToEntityArray(Allocator.Temp);

            foreach (var brainEntity in brainEntities)
            {
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                if (brain.IsActive == 0) continue;

                var aiState = em.GetComponentData<SimpleAIState>(brainEntity);

                // Tick countdown.
                aiState.ThinkTimer -= dt;
                if (aiState.ThinkTimer > 0f)
                {
                    em.SetComponentData(brainEntity, aiState);
                    continue;
                }
                // Difficulty is DATA (AoE4 model): one brain, per-tier knobs.
                var profile = AISimpleDifficulty.GetProfile(brain.Difficulty);
                float thinkInterval = profile.ThinkInterval;
                // STAGGER (2026-08-05, 8-FFA "lag every ~2 seconds"): the
                // brains all spawn together and previously thought on the
                // SAME frame every interval — 8 full think passes stacked in
                // one frame. Slightly different periods per faction drift
                // the brains apart and keep them apart. Deterministic
                // (owner index), so lockstep peers agree.
                aiState.ThinkTimer = thinkInterval * (1f + 0.037f * (int)brain.Owner);
                perfThinks++;
                aiState.RetreatCooldown = math.max(0f, aiState.RetreatCooldown - thinkInterval);

                var settings = AISettings.Get();
                var personality = settings.For(brain.Personality);
                float now = (float)SystemAPI.Time.ElapsedTime;

                // The AI owns miner tasking. Idle miners get explicit
                // GatherCommands — no auto-find anywhere. The veilstone-vs-iron
                // split is whatever the build order set via SetVeilstoneTarget;
                // 0 (default) means iron-only.
                AssignIdleMiners(em, brain.Owner, aiState.VeilstoneMinerTarget, brain.Strategy, now);

                // Replace any military/miners that died since the build order
                // queued them. Runs before the next step so replacements take
                // priority on the train queue and resources.
                ReplaceLostUnits(em, brain.Owner, ref aiState);

                // Scout movement is owned by ScoutDirectorSystem (AI plan M3):
                // zone-based exploration + recon requests replace the old
                // random PatrolWithScouts wander.

                // ANTI-STAGNATION: keep population headroom at all times.
                // Without this, the army + worker floors filled the cap, every
                // train queue clogged, and the whole faction stalled into
                // "workers mining, nothing else" — the maintenance loop never
                // built Huts.
                EnsurePopulationHeadroom(em, brain.Owner);

                // Army missions: prune the dead, regroup finished armies,
                // retreat outmatched ones (per mission, not globally).
                UpdateMissions(em, brain.Owner, ref aiState, settings, now);

                // M4: evaluate the posture (threat near base -> Defend; gutted
                // army -> Rebuild; assembled army + healthy bank -> Pressure)
                // and act on Defend (recall + repair) / M6 retreat.
                EvaluatePosture(em, brain.Owner, ref aiState, settings, personality);

                // ATTACK WAVES (2026-08-04): pressure is a RHYTHM, not a
                // one-off. Runs in BOTH phases — during the build order
                // (waves start the moment the first-attack gate passes, even
                // mid-script) and forever after it. Scripted LaunchAttack
                // steps still fire as authored strategy openings.
                TickAttackWaves(em, brainEntity, brain.Owner, ref aiState,
                    settings, personality, profile, now);

                // CORRUPTION COUNTERPLAY (2026-08-04): when veilstone-poor,
                // strike the Sporeling hazing the home patches — without a
                // military answer, corruption bleeds the AI's veilstone
                // income out patch by patch until the non-skippable
                // choice-building gate freezes the whole build order.
                TryReclaimCorruptedPatches(em, brain.Owner, now);

                // ALWAYS-ON ECONOMY (2026-08-04 rev.2): the worker floor and
                // the Gatherer's Hut pipeline run in BOTH phases — observed
                // twice: a stalled opener (waiting on a step it could not
                // afford) starved supplies forever because ALL economy growth
                // lived in the post-build-order maintenance loop. Supplies
                // are the universal constraint; the economy layer must never
                // be hostage to the script.
                // BUDGET ALLOCATOR (M-A, docs/AI_Manager_Architecture.md):
                // situational weights split measured income into the three
                // wallets every spend center below draws from. The old
                // savings-mode hack is now just a policy input — an active
                // advancement gate (age-up / choice step) tilts the split
                // to Advancement instead of hard-pausing the economy.
                var boForPolicy = AIBuildOrder.For(brain.Strategy);
                bool advancementGate = aiState.StepIndex < boForPolicy.Length
                    && (boForPolicy[aiState.StepIndex].Kind == BuildStepKind.AgeUp
                        || (boForPolicy[aiState.StepIndex].Kind == BuildStepKind.BuildBuilding
                            && BuildingFactory.IsChoiceBuilding(boForPolicy[aiState.StepIndex].Id)));
                bool suppliesStarved = FactionEconomy.TryGetBank(em, brain.Owner, out var policyBank)
                    && em.GetComponentData<FactionResources>(policyBank).Supplies < 150;
                AIBudget.EvaluateWeights(aiState.Posture, advancementGate, suppliesStarved,
                    out float wAdv, out float wMil, out float wEco);
                AIBudget.Tick(em, brain.Owner, wAdv, wMil, wEco, thinkInterval, now);

                TickEconomy(em, brain.Owner, ref aiState, profile, now);

                // ENDGAME RESEARCH SWEEP (era 2+, ~20 s cadence): once the
                // authored economy ladder has no affordable next step (or
                // from era 3 regardless), walk every owned research-capable
                // building and buy whatever its def still offers — the
                // "eventually research ALL of it" mop-up.
                TickEndgameResearchSweep(em, brain.Owner, now);

                var buildOrder = AIBuildOrder.For(brain.Strategy);
                if (aiState.StepIndex >= buildOrder.Length)
                {
                    // Build order finished. Keep the AI alive: top up the army,
                    // train replacement workers, and keep pushing attacks at
                    // the nearest enemy (player economy or border hive).
                    // Without this every non-Rush strategy goes idle the moment
                    // it ages up.
                    RunMaintenanceLoop(em, brainEntity, brain, ref aiState, settings, personality, profile, now);
                    em.SetComponentData(brainEntity, aiState);
                    continue;
                }

                var step = buildOrder[aiState.StepIndex];

                // Lower difficulties may randomly skip optional steps (never
                // Hut, Barracks, Choice or AgeUp — those aren't marked Optional).
                float skipChance = profile.OptionalStepSkipChance;
                if (step.Optional && skipChance > 0f && NextRandFloat01() < skipChance)
                {
                    aiState.StepIndex++;
                    em.SetComponentData(brainEntity, aiState);
                    continue;
                }

                bool issued = TryIssueStep(em, brainEntity, brain, step, ref aiState, settings, personality, profile, now);
                if (issued)
                {
                    AILogger.Log(brain.Owner, "BUILDORDER",
                        $"step {aiState.StepIndex} issued: {step.Kind}:{step.Id} at {(int)now}s");
                    aiState.StepIndex++;
                    aiState.StepStuckSeconds = 0f;
                }
                else
                {
                    // INSTANT SKIP — REFINED (2026-08-04 match 2: the eager
                    // version skipped step 0's Worker at t=0 because the
                    // Hall had not spawned yet, and amputated Rush's early
                    // Spearman steps while its Barracks was merely PENDING —
                    // a military strategy must WAIT for its trainer, not
                    // discard its soul). Skip only when all three hold:
                    //   * past the opening grace (spawn systems settled),
                    //   * the trainer building does not exist, AND
                    //   * no foundation of it is under construction
                    //     (in-flight trainer → keep retrying instead).
                    if (step.Kind == BuildStepKind.TrainUnit
                        && now > 30f
                        && FindTrainerForUnit(em, brain.Owner, step.Id) == Entity.Null
                        && !TrainerInFlight(em, brain.Owner, step.Id))
                    {
                        AILogger.Log(brain.Owner, "BUILDORDER",
                            $"step {aiState.StepIndex} SKIPPED (no trainer for {step.Id})");
                        aiState.StepIndex++;
                        aiState.StepStuckSeconds = 0f;
                        em.SetComponentData(brainEntity, aiState);
                        continue;
                    }

                    // ANTI-STAGNATION: a step that keeps failing (unreachable
                    // attack target, no valid build spot, pop-blocked train)
                    // must not freeze the build order forever — that left whole
                    // factions doing nothing but mining. Skippable steps are
                    // abandoned after the timeout; AgeUp and the choice
                    // building are never skipped (they gate the whole game
                    // plan) and keep retrying.
                    aiState.StepStuckSeconds += thinkInterval;
                    if (aiState.StepStuckSeconds > StepTimeoutSeconds && IsSkippableStep(step))
                    {
                        aiState.StepIndex++;
                        aiState.StepStuckSeconds = 0f;
                    }
                    else if (aiState.StepStuckSeconds > StepTimeoutSeconds
                        && (int)((aiState.StepStuckSeconds - thinkInterval) / 60f)
                            != (int)(aiState.StepStuckSeconds / 60f))
                    {
                        // A NON-skippable step blocked for minutes is a whole
                        // faction going dark (2026-08-04: choice buildings
                        // cost 70 veilstone — zero income froze AIs with
                        // banked iron and "no progress whatsoever"). Loud,
                        // once a minute, with the exact step AND the failing
                        // gate (match 2: Shrine stuck 120s with a full bank
                        // and the log could not say why).
                        string why = string.Empty;
                        if (step.Kind == BuildStepKind.BuildBuilding
                            && TechCatalog.TryGetBuilding(step.Id, out var stuckDef) && stuckDef != null)
                        {
                            why = $" (afford={FactionEconomy.CanAfford(em, brain.Owner, ToCost(stuckDef.cost))}" +
                                  $", idleBuilders={CountIdleBuilders(em, brain.Owner)})";
                        }
                        TWBLog.Log($"[AI {brain.Owner}] build order STUCK at " +
                                   $"{step.Kind}:{step.Id} for {(int)aiState.StepStuckSeconds}s{why}");
                        AILogger.Log(brain.Owner, "STUCK",
                            $"{step.Kind}:{step.Id} blocked {(int)aiState.StepStuckSeconds}s at {(int)now}s{why}");
                    }
                }

                em.SetComponentData(brainEntity, aiState);
            }

            perfSw.Stop();
            if (perfThinks > 0)
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                    "AIThink", perfSw.Elapsed.TotalMilliseconds, $"brains {perfThinks}");
        }

        // ─────────────────────────────────────────────────────────────────
        // STEP DISPATCH
        // ─────────────────────────────────────────────────────────────────

        // A failing build-order step is skipped after this long (anti-stagnation).
        private const float StepTimeoutSeconds = 90f;

        // (The sustained-production army ceiling moved into the per-difficulty
        // profile — AIDifficultyProfile.SustainArmyCap.)

        /// <summary>
        /// Steps the stuck-step timeout may abandon. AgeUp and the choice
        /// building are the spine of the game plan — never skipped.
        /// </summary>
        private static bool IsSkippableStep(BuildOrderStep step)
        {
            if (step.Kind == BuildStepKind.AgeUp) return false;
            if (step.Kind == BuildStepKind.BuildBuilding
                && BuildingFactory.IsChoiceBuilding(step.Id)) return false;
            return true;
        }

        private bool TryIssueStep(EntityManager em, Entity brainEntity, AIBrain brain, BuildOrderStep step,
            ref SimpleAIState aiState, AISettingsSO settings, AISettingsSO.PersonalityBlock personality,
            AIDifficultyProfile profile, float now)
        {
            Faction faction = brain.Owner;
            return step.Kind switch
            {
                BuildStepKind.TrainUnit        => TryTrainUnitFromBuildOrder(em, faction, step.Id, ref aiState),
                BuildStepKind.BuildBuilding    => TryBuildBuilding(em, faction, step.Id),
                BuildStepKind.Research         => TryResearchTech(em, faction, step.Id),
                BuildStepKind.AgeUp            => TryAgeUp(em, faction, ref aiState),
                BuildStepKind.SetVeilstoneTarget => SetVeilstoneTarget(ref aiState, step.IntArg),
                BuildStepKind.LaunchAttack     => TryLaunchAttack(em, brainEntity, faction, step.IntArg, ref aiState, settings, personality, profile, now),
                _                              => true,  // unknown step kind: skip silently
            };
        }

        /// <summary>
        /// Build-order Train wrapper: queues the unit and, on success, increments
        /// the matching Desired counter so ReplaceLostUnits knows the AI is now
        /// committed to having this unit alive. Replacement training calls
        /// TryTrainUnit directly so the counter doesn't double-bump.
        /// </summary>
        private static bool TryTrainUnitFromBuildOrder(
            EntityManager em, Faction faction, string unitId, ref SimpleAIState aiState)
        {
            if (!TryTrainUnit(em, faction, unitId)) return false;
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
        {
            if (!TechCatalog.IsReady) return false;
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return false;

            // Find the right training building for this unit.
            Entity trainer = FindTrainerForUnit(em, faction, unitId);
            if (trainer == Entity.Null) return false;

            // Don't queue into a building still under construction.
            if (em.HasComponent<UnderConstruction>(trainer)) return false;
            if (!em.HasBuffer<TrainQueueItem>(trainer)) return false;

            // Combined train + research cap — see CommandRouter.MaxProductionQueue.
            if (TheWaningBorder.Core.Commands.CommandRouter.IsProductionQueueFull(em, trainer)) return false;

            // Level gate BEFORE spending — IssueTrain drops silently for AI
            // sources, which would leak the cost.
            if (!CommandRouter.CanTrainAtBuilding(em, trainer, unitId, out _, out _)) return false;

            // ANTI-STAGNATION: don't queue what population can't spawn. A
            // pop-blocked item sits in the 5-slot queue forever, clogging
            // every later train/research order for the faction. The Hut
            // headroom loop (EnsurePopulationHeadroom) frees this gate.
            if (!PopulationHelper.HasPopulationCapacity(faction, UnitFactory.GetPopulationCost(unitId)))
                return false;

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;
            if (!FactionEconomy.Spend(em, faction, cost)) return false;

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
                    return FindLeastBusyTrainer<BarracksTag>(em, faction);
                case "Archer":
                    return FindLeastBusyTrainer<ArcheryRangeTag>(em, faction);
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

        /// <summary>Least-queued completed trainer of the given tag — this is
        /// what makes multiple Barracks/Ranges train in PARALLEL (the old
        /// first-found lookup funneled every order into one building's
        /// 5-slot queue no matter how many stood idle).</summary>
        private static Entity FindLeastBusyTrainer<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            Entity best = Entity.Null;
            int bestQueue = int.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (!em.HasBuffer<TrainQueueItem>(ents[i])) continue;
                int len = em.GetBuffer<TrainQueueItem>(ents[i]).Length;
                if (len < bestQueue) { bestQueue = len; best = ents[i]; }
            }
            return best;
        }

        /// <summary>The faction's completed culture (from its Hall's
        /// FactionProgress) — Cultures.None while still Age 0 or mid
        /// age-up research.</summary>
        private static byte FactionCultureOf(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var prog = q.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) return prog[i].Culture;
            return Cultures.None;
        }

        /// <summary>Under-construction foundations of a tag — the "in
        /// flight" count for pipeline-style building (Gatherer's Huts).</summary>
        private static int CountFactionBuildingsUnderConstruction<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<UnderConstruction>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) n++;
            return n;
        }

        /// <summary>Faction buildings of a tag, INCLUDING under-construction
        /// foundations — growth targets must count them or the maintenance
        /// loop re-places the same building every tick until one finishes.</summary>
        private static int CountFactionBuildings<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) n++;
            return n;
        }

        private static bool TrainsUnit(EntityManager em, string buildingId, string unitId)
        {
            if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def?.trains == null) return false;
            for (int i = 0; i < def.trains.Length; i++)
                if (def.trains[i] == unitId) return true;
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // BUILD BUILDING
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Place + dispatch builders for <paramref name="buildingId"/>. The
        /// placement ring is anchored on the faction Hall.
        /// </summary>
        private bool TryBuildBuilding(EntityManager em, Faction faction, string buildingId)
        {
            if (!TechCatalog.IsReady) return false;
            if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def == null) return false;

            // task-109 Phase 7 / AD-6 / R9: SimpleAISystem must never try to
            // place wall primitives. Alanthor AI does NOT build walls in v1
            // of the BFME2 rework — wall construction is deferred to a
            // follow-up task. This guard is a safety net so a future
            // AIBuildOrder entry that accidentally lists "Alanthor_Wall"
            // (or any wall-related id) doesn't propagate through the build
            // pipeline. The same skip is applied below in the existing-
            // building iteration so wall pieces never become target
            // candidates for AI repair/attack actions either.
            if (buildingId == "Alanthor_Wall"
                || buildingId == "Alanthor_WallTower"
                || buildingId == "Alanthor_WallGate")
                return false;

            // Choice-buildings are limited to one per faction.
            if (BuildingFactory.IsChoiceBuilding(buildingId))
            {
                var existing = BuildingFactory.GetFactionChoiceBuilding(em, faction);
                if (existing != null) return false;
            }

            // Need a Hall to anchor placement around.
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null) return false;
            if (!em.HasComponent<LocalTransform>(hall)) return false;
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;

            int2 size = BuildingSizeConfig.GetSize(buildingId);
            if (!TryFindBuildPosition(em, hallPos, size, buildingId, faction, out float3 pos)) return false;

            // Pre-flight: at least one idle builder must be available BEFORE we
            // spend the cost and place the foundation. Without this gate the
            // build-order step advanced on a successful placement even when zero
            // builders were dispatched, leaving an orphan UnderConstruction site
            // that never gained HP and a permanently stalled build queue (the
            // build order would never re-attempt the same step). (task-062 G-2)
            if (CountIdleBuilders(em, faction) == 0) return false;

            if (!FactionEconomy.Spend(em, faction, cost)) return false;

            // F4 (2026-07-15): route through IssuePlaceBuilding, NOT
            // PlaceBuildingDirect — the direct call is the post-lockstep
            // executor, so every AI building existed on the host only and
            // clients watched an empty AI base. In multiplayer the foundation
            // is created on every peer two ticks later, so builders are
            // dispatched at the POSITION with a null target and auto-find the
            // site on arrival (same pattern as the human MP flow in
            // BuildCommandPannel).
            bool queued = CommandRouter.IssuePlaceBuilding(em, buildingId, pos, faction,
                out Entity building, CommandSource.AI);
            if (queued)
            {
                DispatchBuildersTo(em, faction, Entity.Null, buildingId, pos, maxBuilders: 2);
                // No rollback path here: the placement command is already
                // queued on every peer. Past the idle-builder pre-flight a
                // zero dispatch is a rare race; builders auto-chain to nearby
                // unfinished structures, so the site still gets picked up.
                return true;
            }
            if (building == Entity.Null) return false;

            // Dispatch idle builders to actually construct the thing — without
            // this the building is created with HP=1 and UnderConstruction but
            // never gains progress. The human player flow does the same step
            // explicitly via BuildCommandPanel.AssignBuildersToConstruction.
            int dispatched = DispatchBuildersTo(em, faction, building, buildingId, pos, maxBuilders: 2);
            if (dispatched == 0)
            {
                // Race: a builder went busy between the pre-flight check and
                // dispatch. Refund cost + destroy the orphan foundation rather
                // than advancing the step on a stalled site.
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                return false;
            }
            return true;
        }

        /// <summary>
        /// COMMAND FOLLOW-THROUGH: a worker already committed to construction
        /// or repair must not be re-tasked by any other AI routine. BuildCommand
        /// covers the en-route phase (BuildOrder only appears once construction
        /// starts at the site) — missing it was the "AI places foundations but
        /// never builds them" bug: AssignIdleMiners stole the walking builder
        /// back to mining every think tick.
        /// </summary>
        private static bool IsCommittedWorker(EntityManager em, Entity worker)
        {
            return em.HasComponent<BuildCommand>(worker)
                || em.HasComponent<BuildOrder>(worker)
                || em.HasComponent<RepairOrder>(worker);
        }

        /// <summary>
        /// Count the faction's idle builders. Cheap O(N) snapshot used as a
        /// pre-flight gate so TryBuildBuilding doesn't spend resources on a
        /// foundation that no builder will ever pick up. (task-062 G-2)
        /// </summary>
        private static int CountIdleBuilders(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (IsCommittedWorker(em, ents[i])) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Find up to <paramref name="maxBuilders"/> idle builders of the given
        /// faction and issue BuildCommand on each, pointing at <paramref name="site"/>.
        /// Idle = has CanBuild but no current BuildOrder.
        /// </summary>
        /// <returns>Number of builders actually dispatched (0 = nobody available).</returns>
        private static int DispatchBuildersTo(
            EntityManager em, Faction faction, Entity site,
            string buildingId, float3 sitePos, int maxBuilders)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs  = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Collect available builders + their distance² to the site. Workers
            // already committed to a build/repair are never pulled. Truly idle
            // workers are preferred over mining ones (mining is interruptible —
            // construction is imperative — but only as a second choice).
            var idle = new System.Collections.Generic.List<BuilderCandidate>();
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var b = ents[i];
                if (IsCommittedWorker(em, b)) continue;           // already building/repairing
                bool mining = em.HasComponent<GatherCommand>(b)
                    || (em.HasComponent<MinerState>(b)
                        && em.GetComponentData<MinerState>(b).State != MinerWorkState.Idle);
                float dx = xfs[i].Position.x - sitePos.x;
                float dz = xfs[i].Position.z - sitePos.z;
                idle.Add(new BuilderCandidate { Entity = b, DistSq = dx * dx + dz * dz, Mining = mining });
            }

            // Sort: idle workers first, then by distance ascending.
            idle.Sort((a, c) => a.Mining != c.Mining
                ? a.Mining.CompareTo(c.Mining)
                : a.DistSq.CompareTo(c.DistSq));

            int dispatched = 0;
            for (int i = 0; i < idle.Count && dispatched < maxBuilders; i++)
            {
                // CommandSource.AI, not the LocalPlayer default — mislabeled
                // AI orders ride the player's command stream. (audit F20)
                CommandRouter.IssueBuild(em, idle[i].Entity, site, buildingId, sitePos,
                    CommandSource.AI);
                dispatched++;
            }
            return dispatched;
        }

        private struct BuilderCandidate
        {
            public Entity Entity;
            public float DistSq;
            public bool Mining;
        }

        // Default: candidate must be ≥12 m from any existing building so the
        // AI leaves wide walkable corridors. Earlier 7 m was just enough that
        // unit pathing could squeeze through, but Gaussian-smoothed flow at
        // tight cell-corner thresholds would dither and units got stuck.
        // 12 m → ~6-9 m of clear corridor between most building footprints,
        // comfortably wider than any unit's collision radius.
        private const float MinBuildingSpacing = 12f;
        /// <summary>Placement keep-out around resource nodes (veilstone
        /// outcroppings + iron deposits). Structures parked against a patch
        /// blocked the workers' approach ring — they orbited the node
        /// forever (2026-08-03 playtest). Sized so a 4x4-cell footprint plus
        /// worker corridor always fits between building edge and node.</summary>
        private const float MinResourceNodeClearance = 10f;

        // GathererHut income falls off when their 15 m gather circles overlap
        // (production = unobstructed area). Two GHs need ≥2× the gather radius
        // between centres to keep their footprints disjoint. The previous AI
        // honoured this; this constant restores that behaviour.
        private const float MinGHutToGHutSpacing = 30f;

        private bool TryFindBuildPosition(EntityManager em, float3 anchor, int2 size, out float3 pos)
        {
            // Faction sentinel (out of the 0..7 player range): the cover
            // preference only applies to GHut placement, which always comes
            // through the faction-aware overload.
            return TryFindBuildPosition(em, anchor, size, buildingId: null, (Faction)byte.MaxValue, out pos);
        }

        private bool TryFindBuildPosition(EntityManager em, float3 anchor, int2 size, string buildingId, Faction faction, out float3 pos)
        {
            // Snapshot existing buildings once per call. We need both positions
            // and "is GathererHut?" so we can apply the GH-vs-GH spacing rule.
            var bldgQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var bldgEntities  = bldgQuery.ToEntityArray(Allocator.Temp);
            using var bldgTransforms = bldgQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // Pre-mark which existing buildings are GathererHuts so we can do
            // the 30 m check only against them when placing another GHut.
            // Managed bool[] sidesteps NativeArray's `using var` write-access
            // restriction and SimpleAISystem isn't Bursted, so it costs nothing.
            var bldgIsGHut = new bool[bldgEntities.Length];
            for (int i = 0; i < bldgEntities.Length; i++)
                bldgIsGHut[i] = em.HasComponent<GathererHutTag>(bldgEntities[i]);

            bool placingGHut = buildingId == "GatherersHut";
            float minSpacingSq      = MinBuildingSpacing      * MinBuildingSpacing;
            float minGHutSpacingSq  = MinGHutToGHutSpacing    * MinGHutToGHutSpacing;

            // Resource keep-out: never wall off a patch's approach ring.
            var veilNodeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ironNodeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var veilNodeXfs = veilNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var ironNodeXfs = ironNodeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            float nodeClearSq = MinResourceNodeClearance * MinResourceNodeClearance;

            // Sample a ring of angles around the anchor at increasing radii.
            // GHs naturally need a wider ring to satisfy the 30 m spacing —
            // and their reach GROWS with every hut standing (2026-08-04): the
            // economy marches outward across the map instead of saturating
            // one ring around the Hall and stalling.
            float maxRadius = BuildRingDistanceMax;
            if (placingGHut)
            {
                int ghCount = 0;
                for (int i = 0; i < bldgIsGHut.Length; i++)
                    if (bldgIsGHut[i]) ghCount++;
                maxRadius = math.min(160f, BuildRingDistanceMax + 30f + ghCount * 12f);
            }
            // COVERED-GROUND PREFERENCE for huts (2026-08-04, log-proven
            // churn: 16 huts built, 9 standing — the frontier ones died to
            // the curse). Pass 1 only accepts ground the faction already
            // holds (own influence at/over threshold, or inside the Hall
            // hearth ring); pass 2 falls back to any valid spot so the
            // spread never deadlocks. Hut expansion now FOLLOWS the
            // influence war instead of feeding it.
            int passes = placingGHut ? 2 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                bool requireCover = placingGHut && pass == 0;
                for (float r = BuildRingDistanceMin; r <= maxRadius; r += 4f)
                {
                    int angleStart = (int)(NextRandFloat01() * BuildAngleSamples);
                    for (int i = 0; i < BuildAngleSamples; i++)
                    {
                        int idx = (angleStart + i) % BuildAngleSamples;
                        float angle = (idx / (float)BuildAngleSamples) * math.PI * 2f;
                        float3 candidate = new float3(
                            anchor.x + math.cos(angle) * r,
                            0f,
                            anchor.z + math.sin(angle) * r);
                        candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                        if (requireCover && !IsCoveredGround(faction, candidate, anchor))
                            continue;

                        if (TooCloseToExistingBuilding(
                                candidate, bldgTransforms, bldgIsGHut,
                                minSpacingSq, minGHutSpacingSq, placingGHut))
                            continue;

                        if (TooCloseToAny(candidate, veilNodeXfs, nodeClearSq)
                            || TooCloseToAny(candidate, ironNodeXfs, nodeClearSq))
                            continue;

                        // Never place on crusted ground (2026-08-04): the
                        // curse crumbles the foundation before builders
                        // arrive — money in, nothing out, forever.
                        if (IsCursedGround(em, candidate))
                            continue;

                        if (BuildCommandHelper.IsValidBuildPosition(em, candidate, size))
                        {
                            pos = candidate;
                            return true;
                        }
                    }
                }
            }
            pos = default;
            return false;
        }

        /// <summary>
        /// Per-pair spacing check. All buildings keep <paramref name="minDistSq"/>
        /// from each other; additionally, GathererHut→GathererHut placement uses
        /// <paramref name="minGHutDistSq"/> so their 15 m gather circles don't
        /// overlap (which halves their unobstructed-area-driven income).
        /// </summary>
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

        /// <summary>Ground this faction already HOLDS: own influence at/over
        /// the threshold, or inside the anchor Hall's hearth ring (the Age 0
        /// case, when no influence exists yet).</summary>
        private static bool IsCoveredGround(Faction faction, float3 p, float3 hallAnchor)
        {
            float hr = TheWaningBorder.Core.Config.VeilCrustConstants.HallHearthRadius;
            float dx = p.x - hallAnchor.x, dz = p.z - hallAnchor.z;
            if (dx * dx + dz * dz <= hr * hr) return true;

            int f = (int)faction;
            if (f < 0 || f >= TheWaningBorder.Influence.PlayerInfluenceMap.PlayerChannels)
                return false;
            return TheWaningBorder.Influence.PlayerInfluenceMap.Ready
                && TheWaningBorder.Influence.PlayerInfluenceMap.ChannelStrengthWorld(f, p.x, p.z)
                    >= TheWaningBorder.Core.Config.VeilCrustConstants.InfluenceThreshold;
        }

        /// <summary>Plain XZ proximity check against a position set — used
        /// for the resource-node keep-out.</summary>
        private static bool TooCloseToAny(
            float3 candidate,
            NativeArray<LocalTransform> positions,
            float minDistSq)
        {
            for (int i = 0; i < positions.Length; i++)
            {
                float dx = candidate.x - positions[i].Position.x;
                float dz = candidate.z - positions[i].Position.z;
                if (dx * dx + dz * dz < minDistSq) return true;
            }
            return false;
        }

        private static bool TooCloseToExistingBuilding(
            float3 candidate,
            NativeArray<LocalTransform> existing,
            bool[] existingIsGHut,
            float minDistSq,
            float minGHutDistSq,
            bool placingGHut)
        {
            for (int i = 0; i < existing.Length; i++)
            {
                float dx = candidate.x - existing[i].Position.x;
                float dz = candidate.z - existing[i].Position.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < minDistSq) return true;
                if (placingGHut && existingIsGHut[i] && d2 < minGHutDistSq) return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        // RESEARCH TECH
        // ─────────────────────────────────────────────────────────────────

        private static bool TryResearchTech(EntityManager em, Faction faction, string techId)
        {
            if (!TechCatalog.IsReady) return false;
            if (!TechCatalog.TryGetTechnology(techId, out var def) || def == null) return false;

            // Skip if already researched (or in flight) on this faction.
            var researchState = FactionResearchState.Instance;
            if (researchState != null && researchState.HasResearched(faction, techId)) return true;

            // Conscription/StoneWeapons research at the Barracks. Other techs
            // route through the Hall. We only ship Barracks techs in the
            // current build orders, but support both for forward-compat.
            string researchAt = string.IsNullOrEmpty(def.researchAt) ? "Hall" : def.researchAt;
            Entity bldg = researchAt switch
            {
                "Barracks"             => FindFactionBuilding<BarracksTag>(em, faction),
                "Hall"                 => FindFactionBuilding<HallTag>(em, faction),
                "ArcheryRange"         => FindFactionBuilding<ArcheryRangeTag>(em, faction),
                "GatherersHut"         => FindFactionBuilding<GathererHutTag>(em, faction),
                "Hut"                  => FindFactionBuilding<HutTag>(em, faction),
                // Alanthor Age-1 research hosts (Wave 2 military tree).
                "Alanthor_RoyalStable" => FindFactionBuilding<RoyalStableTag>(em, faction),
                "Alanthor_SiegeYard"   => FindFactionBuilding<SiegeYardTag>(em, faction),
                "Alanthor_Smelter"     => FindFactionBuilding<SmelterTag>(em, faction),
                "ShrineOfRidan"        => FindFactionBuilding<ShrineTag>(em, faction),
                _                      => Entity.Null,
            };
            if (bldg == Entity.Null) return false;
            if (em.HasComponent<UnderConstruction>(bldg)) return false;
            if (!em.HasBuffer<ResearchQueueItem>(bldg)) return false;

            // Combined train + research cap — see CommandRouter.MaxProductionQueue.
            if (TheWaningBorder.Core.Commands.CommandRouter.IsProductionQueueFull(em, bldg)) return false;

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;
            if (!FactionEconomy.Spend(em, faction, cost)) return false;

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
            if (!FactionEconomy.CanAfford(em, faction, target)) return false;
            if (!FactionEconomy.Spend(em, faction, ageUpCost)) return false;

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
        /// Count military units of <paramref name="faction"/>: combat-class
        /// UnitTag, battalion leader OR loose unit (skip members so a 4-man
        /// battalion still counts as 1 toward DesiredMilitary, matching the
        /// "1 Train step = 1 entry" bookkeeping).
        /// </summary>
        private static int CountAliveMilitary(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;

                // FREE BODIES DO NOT COUNT AS AN ARMY. Conscripted Feraldis
                // Workers and Raider-Camp Plunderers are both combat-class
                // UnitTags, so they satisfied this floor and the AI stopped
                // training real soldiers entirely: the 2026-08-06 match had a
                // Feraldis AI finish on 18,629 supplies, 5,499 veilstone and
                // military 0, having trained ZERO units in 32 minutes while
                // recycling conscripted workers 38 times.
                //
                // They are still real fighters on the map — they just must not
                // be mistaken for the standing army the floor is sizing.
                if (em.HasComponent<ConscriptedTag>(ents[i])) continue;
                if (em.HasComponent<PlundererTag>(ents[i])) continue;
                // THE THIRD FREE BODY, missed when the other two were fixed.
                // Feraldis Houses spawn Raiders on construction AND on every
                // upgrade (BuildingConstructionSystem / BuildingUpgradeSystem
                // → FeraldisRaider.CreateUncontrolled). They are combat-class
                // UnitTags that nobody trained and nobody can command
                // (NotControllableTag), and they were satisfying this floor.
                //
                // Measured 2026-08-07, 46-minute match: the Feraldis AI
                // trained 7 Workers, 1 Scout and 10 Iconoclasts — and ZERO
                // combat units — while logging "floor blocked" exactly ONCE.
                // It was never blocked; the House raiders kept telling it the
                // army was already big enough. Its military went 5 → 6 → 4 →
                // 4 → 0 while the Alanthor AI on the same map climbed to 25.
                //
                // A raider is a real fighter on the map. It is just not the
                // standing army this floor is sizing, and it cannot be sent
                // anywhere, so it must not suppress recruitment.
                if (em.HasComponent<FeraldisRaiderTag>(ents[i])) continue;
                // Nor is the culture ritualist a soldier: counting the
                // Scholar/Acolyte/Corruptor here would let a single 300-supply
                // caster satisfy part of the army floor.
                if (IsVerbUnit(em, ents[i])) continue;
                n++;
            }
            return n;
        }

        /// <summary>
        /// Living workers. Counts CanBuild, NOT MinerTag: Feraldis Workers
        /// have the mining half stripped at age-up, so a MinerTag count read
        /// zero for them forever and the worker floor retrained endlessly —
        /// the 2026-08-05 match ended with a yard full of idle Feraldis
        /// workers and no army.
        /// </summary>
        private static int CountAliveMiners(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                // A conscripted Feraldis Worker is a soldier now, not a
                // builder — counting it kept the floor "satisfied" by troops
                // out on the map, so a faction that sent everyone to war
                // never rebuilt its build crew. Excluding them makes the
                // floor maintain exactly WorkerFloorFor() real builders.
                if (em.HasComponent<ConscriptedTag>(ents[i])) continue;
                n++;
            }
            return n;
        }

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

        /// <summary>
        /// Count items in this faction's training queues that match either the
        /// combat-class predicate or the miner predicate. Either flag may be
        /// set; both unset returns 0. Avoids walking the queues twice for
        /// callers that need both counts.
        /// </summary>
        private static int CountQueuedByPredicate(
            EntityManager em, Faction faction, bool isCombat = false, bool isMiner = false)
        {
            if (!isCombat && !isMiner) return 0;

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<TrainQueueItem>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);

            int n = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var buffer = em.GetBuffer<TrainQueueItem>(ents[i]);
                for (int j = 0; j < buffer.Length; j++)
                {
                    string id = buffer[j].UnitId.ToString();
                    UnitClass cls = UnitFactory.GetUnitClass(id);
                    if (isCombat && IsCombatClass(cls)) n++;
                    // Worker (formerly Builder + Miner) is UnitClass.Economy
                    // since the merge but still counts as a miner slot —
                    // every Worker carries MinerTag and can auto-find a
                    // deposit. Without this branch the AI would chase
                    // miners forever after training the unified unit.
                    else if (isMiner && (cls == UnitClass.Miner || cls == UnitClass.Economy)) n++;
                }
            }
            return n;
        }

        // ─────────────────────────────────────────────────────────────────
        // LAUNCH ATTACK
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Send all idle military (battalion leaders + loose units, not members)
        /// to attack-move toward the best-scored enemy target (AI plan M2):
        /// candidates come from the brain's intel buffer and are scored by
        /// TargetScorer (type value - defense risk - travel - staleness), with
        /// the legacy nearest-by-priority ladder as fallback when the AI has
        /// no intel yet. Returns false (blocks the build order) until at least
        /// <paramref name="minUnits"/> idle units are available, while the
        /// posture forbids attacking, or while the chosen assault target needs
        /// a recon pass first (scout-then-strike, M3).
        /// </summary>
        private bool TryLaunchAttack(EntityManager em, Entity brainEntity, Faction faction, int minUnits,
            ref SimpleAIState aiState, AISettingsSO settings, AISettingsSO.PersonalityBlock personality,
            AIDifficultyProfile profile, float now)
        {
            // Difficulty knob: no offensive missions before the tier's first-
            // attack time (AoE4: first Hardest attack ≈ 8 min, later on lower
            // tiers). Defense (posture engine) is unaffected.
            if (now < profile.FirstAttackEarliestSeconds) return false;

            // Defend / Rebuild postures hold the army home (M4).
            if (aiState.Posture == AIPosture.Defend || aiState.Posture == AIPosture.Rebuild)
                return false;

            // Pressure posture commits with a slightly smaller wave.
            if (aiState.Posture == AIPosture.Pressure)
                minUnits = math.max(2, minUnits - 1);

            // Need a Hall to know where the army is staging from (used as the
            // "origin" for picking the closest enemy). If no Hall exists we
            // can't pick a target meaningfully — fail silently.
            Entity myHall = FindFactionBuilding<HallTag>(em, faction);
            if (myHall == Entity.Null) return false;
            if (!em.HasComponent<LocalTransform>(myHall)) return false;
            float3 originPos = em.GetComponentData<LocalTransform>(myHall).Position;

            // Find idle military: any UnitTag with a combat class, this faction,
            // no active commands, not currently a battalion *member* (members
            // follow their leader; we issue to the leader only).
            var militaryQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = militaryQuery.ToEntityArray(Allocator.Temp);
            using var tags = militaryQuery.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = militaryQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);

            // Units already enrolled in a living mission are never re-drafted.
            var enrolled = new System.Collections.Generic.HashSet<Entity>();
            foreach (var m in MissionsFor(faction))
                foreach (var member in m.Members)
                    enrolled.Add(member);

            var idleMilitary = new System.Collections.Generic.List<Entity>();
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                Entity e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (IsVerbUnit(em, e)) continue;   // ritualists are not army
                // Uncommandable bodies (Feraldis House Raiders) cannot be sent
                // anywhere — FeraldisRaiderPatrolSystem owns them and
                // overrides any order the same frame. Drafting them inflates
                // the wave's apparent strength with units that never march.
                if (em.HasComponent<NotControllableTag>(e)) continue;
                if (enrolled.Contains(e)) continue;
                // Already on a mission or carrying out another order — leave alone.
                if (em.HasComponent<AttackMoveTag>(e)) continue;
                if (em.HasComponent<MoveCommand>(e)) continue;
                if (em.HasComponent<AttackCommand>(e)) continue;
                if (em.HasComponent<UserMoveOrder>(e)) continue;
                idleMilitary.Add(e);
            }

            if (idleMilitary.Count < minUnits) return false; // wait for the army

            // M2: scored target selection from intel; legacy ladder fallback.
            Entity target = ChooseAttackTargetScored(
                em, brainEntity, faction, originPos, settings, personality, now,
                out float intelAge, out IntelCategory category);
            bool scored = target != Entity.Null;

            // Wells are never plain-army targets (2026-07-12): the culture's
            // ritualist works them with the army as escort (per-culture
            // endgame systems). A scored BorderNode pick is discarded here —
            // raw waves at wells only fed the crystal spread.
            if (scored && category == IntelCategory.BorderNode)
            {
                target = Entity.Null;
                scored = false;
            }

            if (!scored)
                target = ChooseAttackTarget(em, faction, originPos);
            if (target == Entity.Null) return false; // no enemy reachable
            if (!em.HasComponent<LocalTransform>(target)) return false;
            float3 targetPos = em.GetComponentData<LocalTransform>(target).Position;

            // PURSUE THE CURSE (2026-08-04): when the corridor to the enemy
            // is buried under deep crust, a wave dies mid-field without ever
            // fighting (log-proven stall — both armies bleeding out between
            // the bases). The blocking anchor IS the objective — but the
            // anchor an army can actually KILL depends on culture: only
            // Feraldis breaks wells (rev.2 same day: wells are untargetable
            // for everyone else); Age 0 / Alanthor / Runai waves reroute
            // onto the nearest live Sporeling instead — its death collapses
            // its held crust (the tether) all the same.
            bool rerouted = false;
            if (CurseBlocksCorridor(em, originPos, targetPos))
            {
                float3 mid = (originPos + targetPos) * 0.5f;
                Entity anchor;
                float3 anchorPos;
                if (IsFeraldisCulture(em, faction))
                    anchor = FindNearestActiveWell(em, mid, out anchorPos);
                else
                    anchor = FindNearestSporeling(em, mid, out anchorPos);
                if (anchor != Entity.Null)
                {
                    target = anchor;
                    targetPos = anchorPos;
                    scored = false;
                    rerouted = true;
                    AILogger.Log(faction, "WAVE",
                        $"corridor cursed — wave rerouted to the curse anchor at ({anchorPos.x:0},{anchorPos.z:0})");
                }
            }

            // M3 scout-then-strike: assault targets (halls / military
            // buildings) with stale intel get a recon pass first — march in
            // blind and the army may walk into a fresh garrison.
            // ANTI-STAGNATION: only when a living scout exists to serve the
            // request — with all scouts dead this gate deadlocked the build
            // order at its LaunchAttack step forever.
            if (scored
                && (category == IntelCategory.Hall || category == IntelCategory.MilitaryBuilding)
                && intelAge > settings.reconMaxIntelAge
                && CountScouts(em, faction) > 0)
            {
                aiState.ReconTarget = targetPos;
                aiState.HasReconRequest = 1;
                return false;
            }

            // ── Raid split (AoE4-style harass encounter) ──
            // With enough surplus beyond the wave threshold, peel off the
            // fastest few units as a raid party aimed at the enemy ECONOMY
            // (miners / eco buildings) while the main army takes the scored
            // objective. Two simultaneous pressure points instead of one blob.
            var missions = MissionsFor(faction);
            if (!rerouted && profile.RaidingEnabled
                && idleMilitary.Count >= minUnits + RaidPartySize + RaidSurplus)
            {
                Entity raidTarget = ChooseAttackTargetScored(
                    em, brainEntity, faction, originPos, settings, personality, now,
                    out _, out _, ecoOnly: true);
                if (raidTarget != Entity.Null && raidTarget != target
                    && em.HasComponent<LocalTransform>(raidTarget))
                {
                    // Fastest units make the raid party.
                    idleMilitary.Sort((a, b) =>
                    {
                        float sa = em.HasComponent<MoveSpeed>(a) ? em.GetComponentData<MoveSpeed>(a).Value : 0f;
                        float sb = em.HasComponent<MoveSpeed>(b) ? em.GetComponentData<MoveSpeed>(b).Value : 0f;
                        return sb.CompareTo(sa);
                    });
                    float3 raidPos = em.GetComponentData<LocalTransform>(raidTarget).Position;
                    var raid = new Mission
                    {
                        Type = MissionType.Raid,
                        Target = raidTarget,
                        TargetPos = raidPos,
                        StartTime = now,
                    };
                    for (int i = 0; i < RaidPartySize && idleMilitary.Count > 0; i++)
                    {
                        Entity u = idleMilitary[0];
                        idleMilitary.RemoveAt(0);
                        raid.Members.Add(u);
                        AttackMoveCommandHelper.Execute(em, u, raidPos);
                    }
                    missions.Add(raid);
                }
            }

            // ── Main attack mission ──
            // The army marches through the FORMATION pipeline (virtual leader,
            // type-ranked slots, slowest-member speed) — the same machinery
            // player group orders use. Hard+ tiers stage first: form up near
            // the target on the home side, then commit at full strength.
            var attack = new Mission
            {
                Type = MissionType.Attack,
                Target = target,
                TargetPos = targetPos,
                StartTime = now,
            };
            for (int i = 0; i < idleMilitary.Count; i++)
                attack.Members.Add(idleMilitary[i]);

            float3 fromTarget = originPos - targetPos;
            fromTarget.y = 0f;
            float approachDist = math.length(fromTarget);
            if (profile.ForwardStaging && approachDist > StagingDistance * 2f)
            {
                attack.Phase = MissionPhase.Staging;
                attack.StagePos = targetPos + (fromTarget / approachDist) * StagingDistance;
                FormationMoveCommandHelper.Execute(
                    em, attack.Members, attack.StagePos, FormationShape.Box, attackMove: true);
            }
            else
            {
                attack.Phase = MissionPhase.Striking;
                FormationMoveCommandHelper.Execute(
                    em, attack.Members, targetPos, FormationShape.Box, attackMove: true);
            }
            missions.Add(attack);

            // Remember where this wave went so newly-finished units can be
            // fed into it (ReinforceActiveWave). Without this a wave was a
            // one-shot: everything trained after launch stood in the base
            // until the NEXT wave's larger minimum was met, so armies
            // trickled away at the front while reinforcements idled at home.
            aiState.WaveTarget = targetPos;
            aiState.WaveActive = 1;
            aiState.WaveStartTime = now;

            return true;
        }

        /// <summary>Seconds between reinforcement sweeps for a live wave.</summary>
        private const float ReinforceInterval = 10f;

        /// <summary>
        /// A unit standing within this many metres of the wave target has
        /// ARRIVED. Arrivals are not reinforcement candidates: re-issuing
        /// AttackMove to a position a unit is already standing on completes
        /// instantly, which is what made a spent wave look permanently "alive"
        /// (2026-08-07 match: `wave 4 reinforced with 47 unit(s) (0 already
        /// committed)` every 10 s for twenty minutes, army parked on a razed
        /// objective, `wave 5 BLOCKED` because everyone was nominally busy).
        /// </summary>
        private const float WaveArrivedRadius = 30f;

        /// <summary>
        /// Hard cap on a single wave's life. Backstop for the case the arrival
        /// test cannot see — units that keep an AttackMoveTag forever because
        /// they are stuck on terrain read as "committed" and would hold the
        /// wave open indefinitely. At the cap the wave is retired and its army
        /// is released to the next draft, so the cadence keeps running.
        /// </summary>
        private const float WaveMaxLifetime = 150f;

        /// <summary>
        /// Feed idle military into the wave that is already out.
        ///
        /// A wave used to be a single draft: units finished after it left had
        /// no way to join, so the front thinned while fresh troops stood at
        /// home waiting for a next wave whose minimum kept GROWING
        /// (WaveBaseUnits + WaveNumber * WaveGrowthUnits). That is the shape
        /// of "wave N BLOCKED, posture Rebuild" repeating for 18 minutes.
        ///
        /// The wave stays reinforceable until nothing of ours is still
        /// attack-moving — at which point it is over, won or lost.
        /// </summary>
        private void ReinforceActiveWave(EntityManager em, Faction faction,
            ref SimpleAIState aiState, float now)
        {
            if (aiState.WaveActive == 0) return;
            if (now < aiState.NextReinforceTime) return;
            aiState.NextReinforceTime = now + ReinforceInterval;

            // Age out a wave that has overstayed its welcome, whatever its
            // members are doing. Releasing the army is what lets the NEXT wave
            // draft it — a wave that never retires starves every wave after it.
            if (now - aiState.WaveStartTime > WaveMaxLifetime)
            {
                aiState.WaveActive = 0;
                AILogger.Log(faction, "WAVE",
                    $"wave {aiState.WaveNumber} SPENT (lifetime) — army released for the next wave");
                return;
            }

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int committed = 0, sent = 0, arrived = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                var e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                // Free bodies are not the standing army (see CountAliveMilitary).
                if (em.HasComponent<PlundererTag>(e)) continue;
                // Ritualists carry the culture verb — never draft them.
                if (IsVerbUnit(em, e)) continue;
                // Nor uncommandable raiders: ordering them is a no-op that
                // still counts as "sent", which kept spent waves alive.
                if (em.HasComponent<NotControllableTag>(e)) continue;

                bool busy = em.HasComponent<AttackMoveTag>(e)
                         || em.HasComponent<AttackCommand>(e);
                if (busy) { committed++; continue; }
                if (em.HasComponent<UserMoveOrder>(e)) continue;
                if (em.HasComponent<BuildCommand>(e)) continue;

                // Idle AND already standing on the objective: this unit has
                // arrived and there is nothing left here to fight. Re-ordering
                // it is a no-op that would keep the wave alive forever.
                float dx = xfs[i].Position.x - aiState.WaveTarget.x;
                float dz = xfs[i].Position.z - aiState.WaveTarget.z;
                if (dx * dx + dz * dz <= WaveArrivedRadius * WaveArrivedRadius)
                {
                    arrived++;
                    continue;
                }

                AttackMoveCommandHelper.Execute(em, e, aiState.WaveTarget);
                sent++;
            }

            // Nothing marching and nothing to march: the wave is over — either
            // it arrived and cleared the objective, or it died on the way.
            // Either way the army is free and the next wave picks a FRESH
            // scored target instead of re-walking a dead one.
            if (committed == 0 && sent == 0)
            {
                aiState.WaveActive = 0;
                if (arrived > 0)
                    AILogger.Log(faction, "WAVE",
                        $"wave {aiState.WaveNumber} SPENT — {arrived} unit(s) hold the objective, " +
                        "army released for the next wave");
                return;
            }
            if (sent > 0)
                AILogger.Log(faction, "WAVE",
                    $"wave {aiState.WaveNumber} reinforced with {sent} unit(s) " +
                    $"({committed} already committed, {arrived} on the objective)");
        }

        // ── Curse-aware corridor + placement checks (2026-08-04) ──
        /// <summary>Fraction of corridor samples that must be deep crust
        /// before a wave counts the route as blocked.</summary>
        private const float CurseCorridorHeavyFraction = 0.35f;
        private const float CurseCorridorSampleStep = 8f;

        private static bool TryGetVeilField(EntityManager em, out VeilField field)
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<VeilField>());
            if (q.CalculateEntityCount() == 0) { field = default; return false; }
            field = q.GetSingleton<VeilField>();
            return field.Initialised != 0;
        }

        /// <summary>True when a substantial share of the straight line
        /// between the two points crosses deep crust — marching an army
        /// through it costs more HP than the fight at the end.</summary>
        private static bool CurseBlocksCorridor(EntityManager em, float3 a, float3 b)
        {
            if (!TryGetVeilField(em, out var field)) return false;
            float dist = math.distance(new float2(a.x, a.z), new float2(b.x, b.z));
            int samples = math.max(2, (int)(dist / CurseCorridorSampleStep));
            int crusted = 0;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float x = math.lerp(a.x, b.x, t);
                float z = math.lerp(a.z, b.z, t);
                int cx = (int)math.floor((x - field.Origin.x) / field.CellSize);
                int cz = (int)math.floor((z - field.Origin.y) / field.CellSize);
                if (cx < 0 || cx >= field.Width || cz < 0 || cz >= field.Height) continue;
                if (field.Saturation[field.Index(cx, cz)] >= VeilField.CrustThreshold)
                    crusted++;
            }
            return crusted >= (samples + 1) * CurseCorridorHeavyFraction;
        }

        /// <summary>Nearest well still feeding the curse (Active, awake).</summary>
        private static Entity FindNearestActiveWell(EntityManager em, float3 origin, out float3 wellPos)
        {
            wellPos = default;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<BorderNodeState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var states = q.ToComponentDataArray<BorderNodeState>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            Entity best = Entity.Null;
            float bestD = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].State != NodeState.Active) continue;
                if (em.HasComponent<NodeDormant>(ents[i])) continue;
                // WellDormant ≠ NodeDormant. NodeDormant is a DESTROYED well
                // lying inert; WellDormant is an unwoken one (canon §2.8) —
                // still Active, still full HP, but feeding nothing. This
                // method exists to find what is driving the crust, so an
                // unwoken well is the wrong answer: marching on it wastes the
                // squad and does not even wake it (only a verb channel does).
                // Skipping it lets the caller fall through to a Sporeling,
                // which in the early/mid game is the real source anyway.
                if (em.HasComponent<WellDormant>(ents[i])) continue;
                var p = xfs[i].Position;
                float dx = p.x - origin.x, dz = p.z - origin.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = ents[i]; wellPos = p; }
            }
            return best;
        }

        /// <summary>Whether this faction has completed the Feraldis age-up —
        /// the only culture allowed to attack wells.</summary>
        private static bool IsFeraldisCulture(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var progs = q.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction)
                    return progs[i].Culture == Cultures.Feraldis;
            return false;
        }

        /// <summary>Nearest live Sporeling — the curse anchor a non-Feraldis
        /// army CAN kill (wells are Feraldis-only targets).</summary>
        private static Entity FindNearestSporeling(EntityManager em, float3 origin, out float3 pos)
        {
            pos = default;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<SporelingTag>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var hps = q.ToComponentDataArray<Health>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            Entity best = Entity.Null;
            float bestD = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (hps[i].Value <= 0) continue;
                var p = xfs[i].Position;
                float dx = p.x - origin.x, dz = p.z - origin.z;
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = ents[i]; pos = p; }
            }
            return best;
        }

        /// <summary>True when the crust at this position is at/over the crust
        /// threshold. Building here burns money — the curse crumbles the
        /// foundation within seconds (the log-proven hut-pipeline loop:
        /// "started (total 14)" every 4 s while totals fell).</summary>
        private static bool IsCursedGround(EntityManager em, float3 p)
        {
            if (!TryGetVeilField(em, out var field)) return false;
            int cx = (int)math.floor((p.x - field.Origin.x) / field.CellSize);
            int cz = (int)math.floor((p.z - field.Origin.y) / field.CellSize);
            if (cx < 0 || cx >= field.Width || cz < 0 || cz >= field.Height) return false;
            return field.Saturation[field.Index(cx, cz)] >= VeilField.CrustThreshold;
        }

        /// <summary>
        /// Per-think-tick mission upkeep (AoE4-style encounter lifecycle):
        ///   * prune dead/missing members; empty missions disband.
        ///   * objective destroyed -> regroup the army home (attack-move, so
        ///     it fights through) and disband.
        ///   * mission locally outmatched -> retreat THAT army only (M6),
        ///     cooldown-gated per faction.
        ///   * stale missions (timeout) disband so members become draftable.
        /// </summary>
        private void UpdateMissions(EntityManager em, Faction faction,
            ref SimpleAIState aiState, AISettingsSO settings, float now)
        {
            var missions = MissionsFor(faction);
            if (missions.Count == 0) return;

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            bool hasHall = hall != Entity.Null && em.HasComponent<LocalTransform>(hall);
            float3 hallPos = hasHall ? em.GetComponentData<LocalTransform>(hall).Position : default;

            for (int m = missions.Count - 1; m >= 0; m--)
            {
                var mission = missions[m];

                // Prune dead members.
                for (int i = mission.Members.Count - 1; i >= 0; i--)
                    if (!em.Exists(mission.Members[i]))
                        mission.Members.RemoveAt(i);
                if (mission.Members.Count == 0) { missions.RemoveAt(m); continue; }

                bool objectiveDown = mission.Target == Entity.Null || !em.Exists(mission.Target);
                bool timedOut = now - mission.StartTime > MissionTimeoutSeconds;
                if (objectiveDown || timedOut)
                {
                    // Success (or stale): regroup home and free the units for
                    // the next wave. Formation attack-move so the army marches
                    // back in shape and engages stragglers on the way.
                    if (hasHall)
                        FormationMoveCommandHelper.Execute(
                            em, mission.Members, hallPos, FormationShape.Box, attackMove: true);
                    missions.RemoveAt(m);
                    continue;
                }

                // Army centroid (used by staging commit AND the retreat check).
                float3 sum = float3.zero;
                for (int i = 0; i < mission.Members.Count; i++)
                {
                    if (!em.HasComponent<LocalTransform>(mission.Members[i])) continue;
                    sum += em.GetComponentData<LocalTransform>(mission.Members[i]).Position;
                }
                float3 centroid = sum / mission.Members.Count;

                // Forward-staging commit: once the army has gathered at the
                // staging point (or staging times out — stragglers must not
                // stall the push), strike the objective as one formation.
                if (mission.Phase == MissionPhase.Staging)
                {
                    float sx = centroid.x - mission.StagePos.x;
                    float sz = centroid.z - mission.StagePos.z;
                    bool gathered = sx * sx + sz * sz <= StagingGatherRadius * StagingGatherRadius;
                    bool stageTimedOut = now - mission.StartTime > StagingTimeoutSeconds;
                    if (gathered || stageTimedOut)
                    {
                        mission.Phase = MissionPhase.Striking;
                        FormationMoveCommandHelper.Execute(
                            em, mission.Members, mission.TargetPos, FormationShape.Box, attackMove: true);
                    }
                }

                // Per-mission retreat: compare local strength at the army's
                // centroid. Raids disengage more readily (they harass, they
                // don't trade).
                if (aiState.RetreatCooldown > 0f || !hasHall) continue;

                // Don't retreat from our own base defense.
                float dxh = centroid.x - hallPos.x, dzh = centroid.z - hallPos.z;
                if (dxh * dxh + dzh * dzh < settings.defendRadius * settings.defendRadius) continue;

                int myStr = TacticalQuery.FactionStrengthInRadius(em, faction, centroid, 30f);
                int enemyStr = TacticalQuery.EnemyStrengthInRadius(em, faction, centroid, 30f);
                if (myStr <= 0) continue;
                float ratio = mission.Type == MissionType.Raid
                    ? settings.retreatStrengthRatio * 0.65f
                    : settings.retreatStrengthRatio;
                if (enemyStr <= myStr * ratio) continue;

                // Retreat: plain formation move home (no engaging on the way).
                FormationMoveCommandHelper.Execute(
                    em, mission.Members, hallPos, FormationShape.Box, attackMove: false);
                missions.RemoveAt(m);
                aiState.RetreatCooldown = settings.retreatCooldownSeconds;
                if (mission.Type == MissionType.Attack)
                    aiState.Posture = AIPosture.Rebuild;
            }
        }

        /// <summary>Disband every mission (Defend entry — all hands home).</summary>
        private void DisbandAllMissions(Faction faction)
        {
            MissionsFor(faction).Clear();
        }

        /// <summary>
        /// M2 target value assessment: pick the highest-scored candidate from
        /// the brain's EnemySightingRecord buffer. Honors the same fog rules
        /// as the legacy ladder (mobile targets need CURRENT visibility,
        /// statics only need revealed). Returns Entity.Null when the AI has
        /// no usable intel (caller falls back to the legacy ladder).
        /// </summary>
        private static Entity ChooseAttackTargetScored(
            EntityManager em, Entity brainEntity, Faction myFaction, float3 originPos,
            AISettingsSO settings, AISettingsSO.PersonalityBlock personality, float now,
            out float intelAge, out IntelCategory category, bool ecoOnly = false)
        {
            intelAge = 0f;
            category = IntelCategory.MilitaryUnit;
            if (!em.HasBuffer<EnemySightingRecord>(brainEntity)) return Entity.Null;
            var buffer = em.GetBuffer<EnemySightingRecord>(brainEntity);
            if (buffer.Length == 0) return Entity.Null;

            var fogMgr = FogOfWarManager.Instance;
            Entity best = Entity.Null;
            float bestScore = float.MinValue;
            for (int i = 0; i < buffer.Length; i++)
            {
                var rec = buffer[i];
                if (!em.Exists(rec.Enemy)) continue;
                if (em.HasComponent<UnderConstruction>(rec.Enemy)) continue;
                // Raid mode: economy targets only (miners + eco buildings).
                if (ecoOnly && rec.Category != IntelCategory.Miner
                            && rec.Category != IntelCategory.EcoBuilding) continue;

                bool mobile = rec.Category == IntelCategory.MilitaryUnit
                           || rec.Category == IntelCategory.Miner;
                if (fogMgr != null)
                {
                    Vector3 p = (Vector3)rec.Position;
                    bool seen = mobile
                        ? fogMgr.IsVisible(myFaction, p)
                        : fogMgr.IsRevealed(myFaction, p);
                    if (!seen) continue;
                }

                float score = TargetScorer.Score(em, settings, personality.riskMultiplier, originPos, rec, now);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = rec.Enemy;
                    intelAge = now - rec.LastSeenTime;
                    category = rec.Category;
                }
            }
            return best;
        }

        private static bool IsCombatClass(UnitClass c)
        {
            return c == UnitClass.Melee || c == UnitClass.Ranged
                || c == UnitClass.Siege || c == UnitClass.Magic;
        }

        /// <summary>
        /// RITUALISTS ARE NOT ARMY. The three culture verbs (Alanthor purify /
        /// Runai pacify / Feraldis destroy) are carried by units that are all
        /// combat-class UnitTags — Scholar and Acolyte are UnitClass.Magic,
        /// the Iconoclast/Corruptor is UnitClass.Melee — so every draft site in
        /// this file happily swept them into attack waves, and
        /// AttackMoveCommandHelper.Execute → CommandHelper.ClearAllCommands
        /// stripped the verb command off them.
        ///
        /// The channel times are 35 s (purify), 45 s (pacify) and 40 s
        /// (corrupt); ReinforceActiveWave re-commands every 10 s. That made
        /// well domination ARITHMETICALLY UNREACHABLE for an AI: the
        /// 2026-08-07 match logged 128 Corruptor dispatches at the same well
        /// over 22 minutes without a single one ever landing, because the wave
        /// sweep stole the unit before it could finish channelling.
        ///
        /// AIFeraldisEndgameSystem.CommitArmy already had this guard
        /// (`if (em.HasComponent&lt;CorruptorTag&gt;(u)) continue;`) — SimpleAISystem
        /// runs underneath it and never did.
        ///
        /// Covers both the unit identity (tags, so an idle ritualist walking
        /// home is still never drafted) and an in-flight order/channel on any
        /// unit, so future verb carriers are protected by the second half even
        /// if someone forgets to add their tag here.
        /// </summary>
        private static bool IsVerbUnit(EntityManager em, Entity e)
        {
            return em.HasComponent<ScholarTag>(e)
                || em.HasComponent<AcolyteTag>(e)
                || em.HasComponent<CorruptorTag>(e)
                || em.HasComponent<RitualState>(e)
                || em.HasComponent<PurifyCommand>(e)
                || em.HasComponent<ConvertNodeCommand>(e)
                || em.HasComponent<CorruptCommand>(e);
        }

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
                if (barracksCount == 0)
                    TryBuildBuildingBudgeted(em, faction, "Barracks", AIBudgetCategory.Military);
                else if (barracksCount + rangeCount < profile.ProductionBuildingTarget)
                    TryBuildBuildingBudgeted(em, faction,
                        rangeCount < barracksCount ? "ArcheryRange" : "Barracks",
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
                    TryTrainUnitBudgeted(em, faction, "Ledger", AIBudgetCategory.Advancement);
                if (!TheWaningBorder.Abilities.HeroTrainLimit.HasLiveOrQueuedKingLexor(em, faction))
                    TryTrainUnitBudgeted(em, faction, "King Lexor", AIBudgetCategory.Advancement);
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
                && CountAliveMilitary(em, faction) >= aiState.DesiredMilitary)
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

        /// <summary>
        /// Recurring attack waves (2026-08-04): once the difficulty's
        /// first-attack gate passes, launch a wave every
        /// AttackWaveIntervalSeconds. The idle-army minimum starts at
        /// WaveBaseUnits and grows by WaveGrowthUnits per successful wave
        /// (capped near SustainArmyCap) — and since TryLaunchAttack drafts
        /// ALL idle military, real waves scale up with the economy beyond the
        /// minimum. A wave that cannot launch (army short, posture holds, no
        /// target) retries on a short fuse instead of waiting a full
        /// interval.
        /// </summary>
        private const float WaveRetrySeconds = 20f;

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
            int alive = CountAliveMiners(em, faction);
            int queued = CountQueuedByPredicate(em, faction, isMiner: true);
            if (alive + queued < WorkerFloorFor(em, faction))
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
            var research = FactionResearchState.Instance;
            var ladder = EconomyLadderFor(em, faction);
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
                if (TryResearchTechBudgeted(em, faction, techId, cat))
                {
                    AILogger.Log(faction, "RESEARCH", $"{techId} queued");
                    break;
                }
            }
        }

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

                string buildingId = TheWaningBorder.UI.EntityActionExtractor
                    .GetBuildingIdPublic(building, em);
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
                    if (!TechCultureAllowed(tech, culture)) continue;
                    if (math.max(1, tech.minBuildingLevel) > level) continue;
                    if (research != null
                        && !research.MeetsPrerequisites(faction, tech.prerequisites)) continue;

                    // Spend-then-issue, mirroring TryResearchTech. IssueResearch
                    // itself never drops after the pre-checks above (queue cap
                    // + buffer existence are the only direct-path gates).
                    var cost = ToCost(tech.cost);
                    if (!FactionEconomy.CanAfford(em, faction, cost)) continue;
                    if (!FactionEconomy.Spend(em, faction, cost)) continue;

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

        /// <summary>Culture gate for the sweep: data-driven tech.culture
        /// first, then the legacy Survey/Raiding id split (mirrors
        /// EntityActionExtractor.TechAvailableToCulture — the Gatherer's Hut
        /// def lists BOTH economy ladders and each is inert for the other
        /// culture).</summary>
        private static bool TechCultureAllowed(TechnologyDef tech, byte culture)
        {
            if (!string.IsNullOrEmpty(tech.culture))
            {
                switch (tech.culture)
                {
                    case "Runai":    return culture == Cultures.Runai;
                    case "Alanthor": return culture == Cultures.Alanthor;
                    case "Feraldis": return culture == Cultures.Feraldis;
                    // Unknown culture name: fall through to the id switch.
                }
            }
            switch (tech.id)
            {
                // Feraldis Raider Camp ladder.
                case "Raiding1":
                case "Raiding2":
                case "Raiding3":
                case "IronPlunder":
                case "VeilstonePlunder":
                case "VeilsteelPlunder":
                    return culture == Cultures.Feraldis;

                // Alanthor Guild gather drips — dead weight on a Raider Camp.
                case "IronSurveying1":
                case "IronSurveying2":
                case "IronSurveying3":
                case "VeilstoneSurvey1":
                case "VeilstoneSurvey2":
                case "VeilsteelSurvey":
                    return culture != Cultures.Feraldis;

                default:
                    return true;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // BUDGETED SPEND WRAPPERS (M-A): wallet pre-check, real purchase,
        // spend record. The real bank/CommandRouter path is unchanged.
        // ─────────────────────────────────────────────────────────────

        private static bool TryTrainUnitBudgeted(EntityManager em, Faction faction,
            string unitId, AIBudgetCategory cat)
        {
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return false;
            var cost = ToCost(def.cost);
            if (!AIBudget.CanSpend(faction, cat, cost)) return false;
            if (!TryTrainUnit(em, faction, unitId)) return false;
            AIBudget.RecordSpend(faction, cat, cost);
            return true;
        }

        private bool TryBuildBuildingBudgeted(EntityManager em, Faction faction,
            string buildingId, AIBudgetCategory cat)
        {
            if (!TechCatalog.TryGetBuilding(buildingId, out var def) || def == null) return false;
            var cost = ToCost(def.cost);
            if (!AIBudget.CanSpend(faction, cat, cost)) return false;
            if (!TryBuildBuilding(em, faction, buildingId)) return false;
            AIBudget.RecordSpend(faction, cat, cost);
            return true;
        }

        private static bool TryResearchTechBudgeted(EntityManager em, Faction faction,
            string techId, AIBudgetCategory cat)
        {
            if (!TechCatalog.TryGetTechnology(techId, out var def) || def == null) return false;
            var cost = ToCost(def.cost);
            if (!AIBudget.CanSpend(faction, cat, cost)) return false;
            if (!TryResearchTech(em, faction, techId)) return false;
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

        // §2.5b corruption counterplay knobs.
        private const float ReclaimEarliestSeconds = 240f;   // opening stays scripted
        private const int ReclaimVeilstonePoorBelow = 150;   // bank level that counts as starving
        private const float ReclaimRadius = 110f;            // "threatening the home economy"
        /// <summary>Inside this ring of the Hall, a curse growth is attacked
        /// regardless of the veilstone bank — threat-based, not poverty-based.</summary>
        private const float ReclaimHallThreatRadius = 65f;
        private const int ReclaimSquadSize = 6;

        /// <summary>When veilstone-poor, attack-move a small squad onto the
        /// nearest live Sporeling near the base — the military reclaim the
        /// corruption design demands. Drafted units carry AttackMoveTag, so
        /// consecutive ticks never double-draft; killing the Sporeling
        /// collapses the growth and pays the residue field.</summary>
        private void TryReclaimCorruptedPatches(EntityManager em, Faction faction, float now)
        {
            if (now < ReclaimEarliestSeconds) return;

            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall)) return;
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;

            // Nearest live Sporeling threatening the home economy.
            var sporeQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<SporelingTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<Health>());
            using var sXfs = sporeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var sHps = sporeQuery.ToComponentDataArray<Health>(Allocator.Temp);
            float bestD2 = ReclaimRadius * ReclaimRadius;
            float3 target = default;
            bool found = false;
            for (int i = 0; i < sXfs.Length; i++)
            {
                if (sHps[i].Value <= 0) continue;
                float dx = sXfs[i].Position.x - hallPos.x;
                float dz = sXfs[i].Position.z - hallPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; target = sXfs[i].Position; found = true; }
            }

            // Announced blood contaminations count as threats too (2026-08-04
            // telegraph): send the squad NOW so it is standing on the site
            // when the creatures rise.
            var pendingSpawns = TheWaningBorder.Systems.Border.BloodCurseSpawnSystem.Pending;
            for (int i = 0; i < pendingSpawns.Count; i++)
            {
                float dx = pendingSpawns[i].Pos.x - hallPos.x;
                float dz = pendingSpawns[i].Pos.z - hallPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; target = pendingSpawns[i].Pos; found = true; }
            }
            if (!found) return;

            // Engage when the growth THREATENS the base (inside the hall
            // threat ring — 2026-08-04 match 2: Red sat veilstone-RICH while
            // the curse ate its base, because this trigger was poverty-only)
            // or when veilstone-poor anywhere in the reclaim radius.
            bool atDoorstep = bestD2 < ReclaimHallThreatRadius * ReclaimHallThreatRadius;
            bool veilstonePoor = FactionEconomy.TryGetBank(em, faction, out var bank)
                && em.GetComponentData<FactionResources>(bank).Veilstone < ReclaimVeilstonePoorBelow;
            if (!atDoorstep && !veilstonePoor) return;

            // Draft a small squad of uncommitted military (same eligibility
            // rules as the attack waves).
            var mq = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = mq.ToEntityArray(Allocator.Temp);
            using var tags = mq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = mq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int drafted = 0;
            for (int i = 0; i < ents.Length && drafted < ReclaimSquadSize; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                Entity e = ents[i];
                if (em.HasComponent<UnderConstruction>(e)) continue;
                if (IsVerbUnit(em, e)) continue;   // ritualists are not army
                if (em.HasComponent<AttackMoveTag>(e)) continue;
                if (em.HasComponent<MoveCommand>(e)) continue;
                if (em.HasComponent<AttackCommand>(e)) continue;
                if (em.HasComponent<UserMoveOrder>(e)) continue;
                AttackMoveCommandHelper.Execute(em, e, target);
                drafted++;
            }
            if (drafted > 0)
            {
                TWBLog.Log($"[AI {faction}] veilstone-poor — {drafted} units sent to clear the " +
                           $"curse node at ({target.x:0},{target.z:0}).");
                AILogger.Log(faction, "RECLAIM",
                    $"{drafted} units vs curse node at ({target.x:0},{target.z:0})");
            }
        }

        private void TickAttackWaves(EntityManager em, Entity brainEntity, Faction faction,
            ref SimpleAIState aiState, AISettingsSO settings, AISettingsSO.PersonalityBlock personality,
            AIDifficultyProfile profile, float now)
        {
            if (now < profile.FirstAttackEarliestSeconds) return;

            // Reinforcement is NOT gated on the wave cooldown: the whole
            // point is to feed the live push continuously between waves.
            ReinforceActiveWave(em, faction, ref aiState, now);

            if (now < aiState.NextWaveTime) return;

            int minUnits = math.min(
                profile.WaveBaseUnits + aiState.WaveNumber * profile.WaveGrowthUnits,
                math.max(profile.WaveBaseUnits, profile.SustainArmyCap - 2));

            if (TryLaunchAttack(em, brainEntity, faction, minUnits,
                    ref aiState, settings, personality, profile, now))
            {
                aiState.WaveNumber++;
                // PRESS THE ADVANTAGE (2026-08-04 playtest: "the winning
                // player was very shy to attack, giving the other player
                // breathing space"): Pressure posture means the army is at
                // or above its desired size — a winner should convert that
                // NOW, not politely wait a full interval.
                float interval = profile.AttackWaveIntervalSeconds
                    * (aiState.Posture == AIPosture.Pressure ? 0.5f : 1f);
                aiState.NextWaveTime = now + interval;
                AILogger.Log(faction, "WAVE",
                    $"wave {aiState.WaveNumber} LAUNCHED at {(int)now}s (min {minUnits}, " +
                    $"posture {aiState.Posture}); next at {(int)aiState.NextWaveTime}s");
            }
            else
            {
                aiState.NextWaveTime = now + WaveRetrySeconds;
                // A wave that cannot launch for minutes is the "20 min, zero
                // attacks" bug class — log the why once per ~2 minutes.
                if ((int)(now / 120f) != (int)((now - WaveRetrySeconds) / 120f))
                {
                    TWBLog.Log($"[AI {faction}] wave {aiState.WaveNumber + 1} blocked at " +
                               $"{(int)now}s (need {minUnits} idle military, posture " +
                               $"{aiState.Posture}, desired {aiState.DesiredMilitary})");
                    AILogger.Log(faction, "WAVE",
                        $"wave {aiState.WaveNumber + 1} BLOCKED at {(int)now}s " +
                        $"(need {minUnits} idle, posture {aiState.Posture}, " +
                        $"desired {aiState.DesiredMilitary})");
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
                if (cavStr * 2 > meleeStr + rangedStr) desiredRangedFrac = 0.25f;      // spear wall vs cavalry
                else if (meleeStr > rangedStr * 3 / 2) desiredRangedFrac = 0.6f;       // shoot the melee blob
                else if (rangedStr > meleeStr * 3 / 2) desiredRangedFrac = 0.25f;      // close the gap
            }

            int total = ownMelee + ownRanged;
            if (total == 0) return "Spearman";
            return ownRanged < total * desiredRangedFrac ? "Archer" : "Spearman";
        }

        /// <summary>Living scouts of this faction (vision pipeline health check).</summary>
        private static int CountScouts(EntityManager em, Faction faction)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < tags.Length; i++)
                if (facs[i].Value == faction && tags[i].Class == UnitClass.Scout) n++;
            return n;
        }

        // Build a Hut whenever population headroom drops to this or below.
        private const int PopulationHeadroomFloor = 2;

        /// <summary>
        /// ANTI-STAGNATION: keep building Huts while population headroom is
        /// tight (and the absolute cap isn't reached). Runs every think tick —
        /// both during the build order and in maintenance — because the train
        /// pop-gate in TryTrainUnit depends on headroom eventually appearing.
        /// TryBuildBuilding's own pre-flights (cost, idle builder, valid spot)
        /// make the retry safe.
        /// </summary>
        private void EnsurePopulationHeadroom(EntityManager em, Faction faction)
        {
            if (!PopulationHelper.TryGetFactionPopulation(faction, out int current, out int max)) return;
            if (max >= FactionPopulation.AbsoluteMax) return;
            if (max - current > PopulationHeadroomFloor) return;
            TryBuildBuildingBudgeted(em, faction, "Hut", AIBudgetCategory.EconomyExpansion);
        }

        // ─────────────────────────────────────────────────────────────────
        // POSTURE + DEFENSE RESPONSE + RETREAT (M4 / M6)
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Evaluate and act on the AI's posture each think tick:
        ///   Defend  — ThreatMap spike near the Hall: recall the fielded army,
        ///             dispatch an idle builder to repair damaged buildings.
        ///   Rebuild — army below half its desired size: hold attacks.
        ///   Pressure— army at/above desired size: attack with smaller waves.
        ///   Develop — everything else.
        /// Also runs the M6 retreat check while an attack wave is out.
        /// </summary>
        private void EvaluatePosture(EntityManager em, Faction faction,
            ref SimpleAIState aiState, AISettingsSO settings, AISettingsSO.PersonalityBlock personality)
        {
            Entity hall = FindFactionBuilding<HallTag>(em, faction);
            if (hall == Entity.Null || !em.HasComponent<LocalTransform>(hall))
            {
                aiState.Posture = AIPosture.Rebuild; // no base: just rebuild
                return;
            }
            float3 hallPos = em.GetComponentData<LocalTransform>(hall).Position;

            int aliveMil = CountAliveMilitary(em, faction);

            // Defend triggers on enemies PHYSICALLY inside the base (tight
            // 30 u probe around the Hall), not on the ambient threat map —
            // a static border garrison parked 40 u away was keeping factions
            // in Defend permanently, freezing their build orders at every
            // LaunchAttack step (total stagnation). The threat map still
            // drives scout routing and target risk.
            int enemyInBase = TacticalQuery.EnemyStrengthInRadius(em, faction, hallPos, 30f);
            if (enemyInBase > settings.defendThreatThreshold / 2)
            {
                // COMMAND FOLLOW-THROUGH: the recall + repair dispatch fires
                // once on ENTERING Defend, not every think tick — re-issuing
                // attack-moves each tick was resetting units mid-order.
                bool entering = aiState.Posture != AIPosture.Defend;
                aiState.Posture = AIPosture.Defend;
                if (entering)
                {
                    // All hands home: standing missions are void when the
                    // base itself is under attack (the imperative exception
                    // to command follow-through).
                    DisbandAllMissions(faction);
                    DefendBase(em, faction, hallPos, settings);
                }
                return;
            }

            // OUTLYING BUILDING UNDER ATTACK (2026-07-12: "AI is not
            // assigning defenders — they rely on auto-acquire while the base
            // burns"). The Hall probe above only covers 30 u; a GathererHut
            // or Barracks being razed across the base triggered nothing.
            // A building counts as under attack when it is damaged, its
            // last attacker still exists, and enemy strength is STILL
            // present around it (so long-healed scars don't lock Defend).
            if (TryFindAttackedBuilding(em, faction, out float3 attackedPos))
            {
                bool entering = aiState.Posture != AIPosture.Defend;
                aiState.Posture = AIPosture.Defend;
                if (entering)
                {
                    DisbandAllMissions(faction);
                    // Rally at the ATTACKED building, not the Hall — the
                    // defenders converge on the actual fight.
                    DefendBase(em, faction, attackedPos, settings);
                }
                return;
            }

            // (Per-mission retreat lives in UpdateMissions — the old global
            // CheckRetreat pulled EVERY army home when one was outmatched.)

            if (aiState.DesiredMilitary > 0 && aliveMil < aiState.DesiredMilitary / 2)
            {
                aiState.Posture = AIPosture.Rebuild;
                return;
            }

            aiState.Posture = aliveMil >= math.max(personality.attackThreshold, aiState.DesiredMilitary)
                ? AIPosture.Pressure
                : AIPosture.Develop;
        }

        /// <summary>Find a damaged own building whose attacker still exists
        /// AND still has enemy presence nearby — the "outpost under attack"
        /// signal for the defense posture. Deterministic entity-order scan;
        /// returns the first match.</summary>
        private static bool TryFindAttackedBuilding(EntityManager em, Faction faction, out float3 pos)
        {
            pos = default;
            var bq = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<LastAttackerEntity>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = bq.ToEntityArray(Allocator.Temp);
            using var facs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var hps = bq.ToComponentDataArray<Health>(Allocator.Temp);
            using var atk = bq.ToComponentDataArray<LastAttackerEntity>(Allocator.Temp);
            using var xfs = bq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (hps[i].Value <= 0 || hps[i].Value >= hps[i].Max) continue;
                if (atk[i].Value == Entity.Null || !em.Exists(atk[i].Value)) continue;
                // Live fight check: enemies still near the building (a scar
                // from minutes ago must not pin the AI in Defend).
                if (TacticalQuery.EnemyStrengthInRadius(em, faction, xfs[i].Position, 25f) <= 0)
                    continue;
                pos = xfs[i].Position;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Defend response (M4): recall every fielded military unit that is
        /// outside the defend radius back to the Hall (attack-move, so it
        /// fights through), and put an idle builder on the most-damaged
        /// completed building.
        /// </summary>
        private static void DefendBase(EntityManager em, Faction faction, float3 hallPos, AISettingsSO settings)
        {
            float defendRadiusSq = settings.defendRadius * settings.defendRadius;

            // The actual threat: nearest ENEMY unit inside the defend ring.
            // (2026-08-04 playtest: "AI was under attack and had defenders,
            // but it didn't use them" — home units got no order and stood on
            // auto-aggro while archers picked the base apart from range.)
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var tags = q.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            bool threatFound = false;
            float threatD2 = defendRadiusSq;
            float3 threatPos = hallPos;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value == faction || facs[i].Value == Faction.Border) continue;
                float dx = xfs[i].Position.x - hallPos.x;
                float dz = xfs[i].Position.z - hallPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < threatD2) { threatD2 = d2; threatPos = xfs[i].Position; threatFound = true; }
            }

            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                if (!IsCombatClass(tags[i].Class)) continue;
                // Ritualists are never recalled. This recall is unconditional
                // — no busy/committed check at all — so it was the harshest of
                // the draft sites: a Corruptor or Scholar part-way through the
                // walk to a well got dragged home the instant the brain
                // flipped to Defend, and the endgame system re-dispatched it
                // 5 s later. Defending the base must not cost you the verb.
                if (IsVerbUnit(em, ents[i])) continue;
                float dx = xfs[i].Position.x - hallPos.x;
                float dz = xfs[i].Position.z - hallPos.z;
                bool home = dx * dx + dz * dz <= defendRadiusSq;

                if (!home)
                {
                    // Fielded army: recall toward the base.
                    AttackMoveCommandHelper.Execute(em, ents[i], hallPos);
                    continue;
                }

                // HOME defenders ENGAGE the intruder — but never yank a unit
                // already fighting (Target set) or already ordered.
                if (!threatFound) continue;
                Entity e = ents[i];
                if (em.HasComponent<Target>(e)
                    && em.GetComponentData<Target>(e).Value != Entity.Null) continue;
                if (em.HasComponent<AttackMoveTag>(e)) continue;
                if (em.HasComponent<AttackCommand>(e)) continue;
                if (em.HasComponent<UserMoveOrder>(e)) continue;
                AttackMoveCommandHelper.Execute(em, e, threatPos);
            }

            // Repair: most-damaged completed building gets one idle builder.
            Entity worst = Entity.Null;
            float worstFrac = 0.85f; // only bother below 85% HP
            var bq = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>());
            using var bEnts = bq.ToEntityArray(Allocator.Temp);
            using var bFacs = bq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var bHps = bq.ToComponentDataArray<Health>(Allocator.Temp);
            for (int i = 0; i < bEnts.Length; i++)
            {
                if (bFacs[i].Value != faction) continue;
                if (bHps[i].Max <= 0 || bHps[i].Value <= 0) continue;
                if (em.HasComponent<UnderConstruction>(bEnts[i])) continue;
                float frac = bHps[i].Value / (float)bHps[i].Max;
                if (frac < worstFrac) { worstFrac = frac; worst = bEnts[i]; }
            }
            if (worst == Entity.Null) return;

            var cq = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>());
            using var cEnts = cq.ToEntityArray(Allocator.Temp);
            using var cFacs = cq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < cEnts.Length; i++)
            {
                if (cFacs[i].Value != faction) continue;
                if (IsCommittedWorker(em, cEnts[i])) continue; // building/repairing already
                CommandRouter.IssueRepair(em, cEnts[i], worst, CommandSource.AI);
                break;
            }
        }

        // (M6 retreat moved into UpdateMissions: per-mission centroid strength
        // checks replace the old global CheckRetreat, which yanked every army
        // home whenever any single one was outmatched.)

        // Scout movement moved to ScoutDirectorSystem (AI plan M3): zone-based
        // exploration with staleness/enemy-base priorities, recon requests
        // (scout-then-strike), threat-aware routing, and flee-when-hurt.

        /// <summary>
        /// Pick the closest enemy target by priority:
        /// Miners → GathererHuts → Veilstone hives → Veilstone sub-nodes → Halls.
        /// Distance is measured from <paramref name="originPos"/> (the AI's
        /// Hall) so the army marches toward the nearest enemy first.
        ///
        /// Border targets (BorderMainNodeTag, BorderSubNodeTag) live under
        /// Faction.Border — they pass the !=myFaction filter automatically and
        /// give the AI something to chew on even when no enemy player base
        /// has been scouted yet. Main nodes are higher priority than sub-nodes
        /// (killing a hive rolls back the border spread).
        ///
        /// Fog of war: AI must respect the same visibility rules the human
        /// player has. Miners are mobile and require *current* visibility
        /// (the AI can chase what its scouts / military see right now).
        /// Static targets (GHuts, Halls, Veilstone nodes) only need *revealed*
        /// visibility — once seen they're known targets (matches the "explored
        /// ghost" rule for buildings), so the AI can march toward a last-seen
        /// hive even after the scout moves on.
        /// </summary>
        private static Entity ChooseAttackTarget(EntityManager em, Faction myFaction, float3 originPos)
        {
            // 1. Visible enemy miners — most actionable raid target.
            Entity t = FindClosestEnemyOf<MinerTag>(em, myFaction, originPos, requireCurrentVisibility: true);
            if (t != Entity.Null) return t;
            // 2. Revealed enemy economy buildings.
            t = FindClosestEnemyOf<GathererHutTag>(em, myFaction, originPos, requireCurrentVisibility: false);
            if (t != Entity.Null) return t;
            // (Border wells REMOVED from the plain-army ladder, 2026-07-12.
            //  Wells are VERB objectives — the culture's ritualist (Scholar /
            //  Acolyte / Iconoclast) works them with the army as ESCORT,
            //  dispatched by the per-culture endgame system. Sending raw
            //  waves at a well just fed armies to the crystal spread.)
            // 3. Enemy Halls — finisher.
            return FindClosestEnemyOf<HallTag>(em, myFaction, originPos, requireCurrentVisibility: false);
        }

        private static Entity FindClosestEnemyOf<TTag>(
            EntityManager em, Faction myFaction, float3 originPos, bool requireCurrentVisibility)
            where TTag : unmanaged, IComponentData
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<TTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            // FogOfWarManager may be null when fog is disabled (Observer mode,
            // or future modes). In that case treat everything as visible —
            // matches the human player's behaviour with fog off.
            var fogMgr = FogOfWarManager.Instance;

            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value == myFaction) continue;
                // Skip targets still under construction (Halls only — others
                // wouldn't have UnderConstruction). Easier to detect by checking
                // the component than to add a separate query exclusion.
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;

                if (fogMgr != null)
                {
                    Vector3 pos = (Vector3)xfs[i].Position;
                    bool seen = requireCurrentVisibility
                        ? fogMgr.IsVisible(myFaction, pos)
                        : fogMgr.IsRevealed(myFaction, pos);
                    if (!seen) continue;
                }

                float dx = xfs[i].Position.x - originPos.x;
                float dz = xfs[i].Position.z - originPos.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            return best;
        }

        // ─────────────────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────────────────

        private static Entity FindFactionBuilding<TTag>(EntityManager em, Faction faction)
            where TTag : unmanaged, IComponentData
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<TTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != faction) continue;
                // Skip buildings still under construction unless caller checks itself.
                return entities[i];
            }
            return Entity.Null;
        }

        private static bool FactionHasChoiceBuilding(EntityManager em, Faction faction)
        {
            // Choice buildings carry ChoiceBuildingTag (set by BuildingFactory for
            // ShrineOfRidan / VaultOfAlmierra / FiendstoneKeep). The AI age-up
            // gate must require a COMPLETED choice building — the canonical
            // helper that excludes UnderConstruction is
            // GetCompletedFactionChoiceBuilding. (Player + AI gates were both
            // counting under-construction choice buildings before the fix.)
            var existing = BuildingFactory.GetCompletedFactionChoiceBuilding(em, faction);
            if (existing != null) return true;

            // Also accept a completed TempleOfRidan even though it isn't a
            // "choice" building per ChoiceBuildingIds.
            Entity temple = FindFactionBuilding<TempleTag>(em, faction);
            return temple != Entity.Null && !em.HasComponent<UnderConstruction>(temple);
        }

        private static Entity FindBrainEntity(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<AIBrain>(),
                ComponentType.ReadOnly<FactionTag>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction) return entities[i];
            }
            return Entity.Null;
        }

        private static Cost ToCost(CostBlock block)
        {
            if (block == null) return default;
            return new Cost
            {
                Supplies  = block.Supplies,
                Iron      = block.Iron,
                Veilstone   = block.Veilstone,
                Veilsteel = block.Veilsteel,
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // MINER TASKING
        // ─────────────────────────────────────────────────────────────────

        // Hard upper bound on the per-strategy SetVeilstoneTarget value. Bumped
        // to 16 so the runtime 50/50 floor (totalMiners / 2) isn't crushed by
        // an old clamp from when veilstone miners were treated as a niche.
        private const int MaxVeilstoneMiners = 16;

        // Mining stays anchored to the base: deposits within this range of
        // the home building (Hall) are "home" deposits (miners pick the
        // nearest to themselves among them, so they spread). Only when NO home deposit
        // exists do miners take the deposit nearest to the HALL — never the
        // one nearest to the worker, which on lopsided maps marched the
        // whole workforce toward the enemy base on the first think tick.
        private const float HomeMiningRadius = 70f;

        // (Research ladder lives in EconomyResearchLadder — walked by the
        // always-on TickEconomy with continue-past-failure semantics.)

        /// <summary>Extended crust-dig reach used only when NO crust exists
        /// inside <see cref="HomeMiningRadius"/> — the veil seeds around the
        /// wells, which usually sit far from the AI base, and the old hard
        /// tether meant the AI never dug veilstone at all.</summary>
        private const float VeilReachFallback = 170f;

        /// <summary>
        /// Issue explicit GatherCommands to every idle AI miner. Iron and veilstone
        /// are separate flows: the AI counts current veilstone miners and, while
        /// under the effective target, sends new idle miners to outcroppings; the
        /// rest go to iron.
        ///
        /// Default effective target = <c>max(buildOrderTarget, totalMiners / 2)</c>.
        /// The build-order SetVeilstoneTarget normally acts as a FLOOR — strategies
        /// can front-load veilstone demand (e.g. TechBoom asking for 2 with only
        /// 4 miners) and the steady-state allocation is 50/50 because veilstone
        /// is just as important as iron for age-up + tech.
        ///
        /// EXCEPTION: military-rush strategies (Rush) treat their SetVeilstoneTarget
        /// as an explicit CAP, not a floor. The 50/50 floor would otherwise
        /// override Rush's `SetVeilstoneTarget(1)` (only "enough veilstone for
        /// Shrine + age-up") and starve early military production. (task-062 G-1)
        ///
        /// Auto-find is fully removed from MiningSystem and VeilstoneMiningSystem
        /// for AI factions — every miner movement is the result of a command
        /// issued here (or the LOS-based after-depletion routing inside the
        /// mining systems, which is intentional player UX).
        /// </summary>
        private static void AssignIdleMiners(EntityManager em, Faction faction, int targetVeilstone, AIStrategy strategy, double now)
        {
            // FERALDIS WORKERS CANNOT GATHER — the retrofit strips MinerTag
            // and MinerState at age-up, and their ore comes from Mines. This
            // pass was still handing them veilstone gather orders every tick,
            // which fought the endgame system's own orders: a worker would be
            // told to walk to an outcropping, then told to hold or conscript,
            // then re-issued the gather. The player-visible symptom was
            // workers twitching between "go mine veilstone" and "stay put".
            if (CultureConfig.GetCompletedCulture(em, faction) == Cultures.Feraldis) return;

            // Defensive clamp: SetVeilstoneTarget already clamps writes, but a
            // bootstrap that left VeilstoneMinerTarget at default still produces
            // a sane non-negative value here.
            targetVeilstone = math.clamp(targetVeilstone, 0, MaxVeilstoneMiners);
            // Find this faction's home anchor (Hall first, then any
            // GathererHut) — mining stays tethered to the base even though
            // resources are credited directly to the bank.
            Entity home = FindFactionBuilding<HallTag>(em, faction);
            if (home == Entity.Null)
                home = FindFactionBuilding<GathererHutTag>(em, faction);
            if (home == Entity.Null) return; // no base to anchor mining to
            if (!em.HasComponent<LocalTransform>(home)) return;
            float3 homePos = em.GetComponentData<LocalTransform>(home).Position;

            // Snapshot all non-depleted iron deposits and outcroppings. We do per-
            // miner nearest selection below so miners spread across multiple
            // deposits instead of all converging on one.
            var ironQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<IronDepositState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ironEnts = ironQuery.ToEntityArray(Allocator.Temp);
            using var ironStates = ironQuery.ToComponentDataArray<IronDepositState>(Allocator.Temp);
            using var ironTransforms = ironQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            var outcroppingQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<VeilstoneOutcroppingState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var outcroppingEnts = outcroppingQuery.ToEntityArray(Allocator.Temp);
            using var outcroppingStates = outcroppingQuery.ToComponentDataArray<VeilstoneOutcroppingState>(Allocator.Temp);
            using var outcroppingTransforms = outcroppingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            bool anyIron = HasAnyIron(ironStates);
            bool anyVeilstoneOutcropping = HasAnyVeilstoneOutcropping(outcroppingStates);

            // THE VEIL (canon §2.3): on cursed maps there are no veilstone
            // deposit entities at all — the crust sheet is dug directly.
            // Veilstone is "available" when there is crust within the home
            // mining radius; diggers are tasked with GatherVeilCommand.
            bool hasVeilField = false;
            VeilField veilField = default;
            var veilFieldQuery = em.CreateEntityQuery(ComponentType.ReadOnly<VeilField>());
            if (!veilFieldQuery.IsEmpty)
            {
                veilField = veilFieldQuery.GetSingleton<VeilField>();
                hasVeilField = veilField.Initialised == 1 && veilField.Saturation.IsCreated;
            }
            // Dig-the-sheet is retired with the wall model (§2.5b): the
            // walkable veil cannot be mined, and tasking GatherVeilCommand
            // marched AI workers into the crust where exposure killed them
            // (2026-08-03 playtest: 3-4 workers lost at game start). The
            // field stays sampled above — the outcropping pickers use it to
            // avoid hazed nodes.
            bool anyVeilCrust = TheWaningBorder.Core.Config.VeilCrustConstants.CrustPhysical
                && hasVeilField && VeilMiningUtil.TryFindCrustVertexNear(
                in veilField, homePos, homePos, HomeMiningRadius, out _);

            // FAR-CRUST FALLBACK (2026-07-12): crust seeds around the WELLS,
            // which on most maps sit well beyond the 70 m home tether — the
            // probe above always failed and the AI never dug a single cell
            // ("AI is not pursuing veilstone"). When nothing is in home range,
            // extend the reach: the veil is the ONLY veilstone source on
            // cursed maps, so a longer walk beats zero income.
            float veilReach = HomeMiningRadius;
            if (TheWaningBorder.Core.Config.VeilCrustConstants.CrustPhysical
                && hasVeilField && !anyVeilCrust && VeilMiningUtil.TryFindCrustVertexNear(
                    in veilField, homePos, homePos, VeilReachFallback, out _))
            {
                anyVeilCrust = true;
                veilReach = VeilReachFallback;
            }

            bool anyVeilSource = anyVeilstoneOutcropping || anyVeilCrust;
            if (!anyIron && !anyVeilSource) return;

            // Snapshot this faction's miners.
            var minerQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<MinerTag>(),
                ComponentType.ReadOnly<MinerState>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var minerEntities = minerQuery.ToEntityArray(Allocator.Temp);
            using var minerStates = minerQuery.ToComponentDataArray<MinerState>(Allocator.Temp);
            using var minerFactions = minerQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var minerTransforms = minerQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            int totalMiners = 0;
            int crystalMiners = 0;
            var idleMiners = new System.Collections.Generic.List<(Entity ent, float3 pos)>();

            for (int i = 0; i < minerEntities.Length; i++)
            {
                if (minerFactions[i].Value != faction) continue;
                totalMiners++;
                var ms = minerStates[i];
                if (ms.GatheringResource == 1) crystalMiners++;
                // Idle = not currently moving/mining/returning AND not already
                // commanded (no GatherCommand pending). Skipping miners that
                // already hold a GatherCommand prevents reissuing every tick.
                // COMMAND FOLLOW-THROUGH: workers committed to construction or
                // repair (BuildCommand / BuildOrder / RepairOrder) are off
                // limits — re-tasking them to mine was clearing their build
                // command mid-walk, so AI foundations never got built.
                if (ms.State == MinerWorkState.Idle
                    && !em.HasComponent<GatherCommand>(minerEntities[i])
                    && !em.HasComponent<TheWaningBorder.Core.Commands.Types.GatherVeilCommand>(minerEntities[i])
                    && !em.HasComponent<UserMoveOrder>(minerEntities[i])
                    && !IsCommittedWorker(em, minerEntities[i]))
                    idleMiners.Add((minerEntities[i], minerTransforms[i].Position));
            }

            // 50/50 floor: at minimum, half the workforce should be on veilstone
            // when outcroppings are reachable. The build-order target only wins if
            // it asks for MORE veilstone than 50/50 (early front-loading). This
            // replaces the previous cap-driven allocation where the AI sat at
            // 1-3 veilstone miners regardless of army size and starved on veilstone.
            //
            // Rush keeps a LIGHTER floor, never a zero one (2026-08-04,
            // supersedes the task-062 G-1 full opt-out): progression
            // hard-gates on veilstone (70-veilstone choice building), and a
            // Rush AI observed with ZERO veilstone miners froze mid-order
            // with banked iron. 1 miner at 4+, 2 at 8+ — military-first
            // stays intact, starvation does not.
            if (anyVeilSource)
                targetVeilstone = strategy == AIStrategy.Rush
                    ? math.max(targetVeilstone, math.min(2, totalMiners / 4))
                    : math.max(targetVeilstone, totalMiners / 2);

            for (int i = 0; i < idleMiners.Count; i++)
            {
                var (miner, minerPos) = idleMiners[i];

                // Prefer veilstone until the AI hits its target count, but only
                // if a source is actually available. Otherwise send to iron.
                bool wantVeilstone = crystalMiners < targetVeilstone && anyVeilSource;

                // Veilstone on cursed maps = dig the sheet directly: closest
                // crusted vertex to this worker (home-tethered).
                if (wantVeilstone && !anyVeilstoneOutcropping)
                {
                    if (anyVeilCrust && VeilMiningUtil.TryFindCrustVertexNear(
                            in veilField, minerPos, homePos, veilReach, out float3 digSite))
                    {
                        GatherVeilCommandHelper.Execute(em, miner, digSite);
                        crystalMiners++;
                        continue;
                    }
                    wantVeilstone = false; // no crust reachable — fall to iron
                }

                Entity target = wantVeilstone
                    ? PickNearestVeilstoneOutcropping(minerPos, homePos, outcroppingEnts, outcroppingStates, outcroppingTransforms, in veilField, hasVeilField, em, now)
                    : PickNearestIron(minerPos, homePos, ironEnts, ironStates, ironTransforms, em, now);

                if (target == Entity.Null)
                {
                    // First-choice resource is gone (e.g. last outcropping depleted
                    // mid-loop). Try the other side once before giving up.
                    target = wantVeilstone
                        ? PickNearestIron(minerPos, homePos, ironEnts, ironStates, ironTransforms, em, now)
                        : PickNearestVeilstoneOutcropping(minerPos, homePos, outcroppingEnts, outcroppingStates, outcroppingTransforms, in veilField, hasVeilField, em, now);
                    if (target == Entity.Null) continue;
                    wantVeilstone = !wantVeilstone;
                }

                GatherCommandHelper.Execute(em, miner, target);
                if (wantVeilstone) crystalMiners++;
            }

            // ANTI-STAGNATION rebalance: miners locked on iron never go idle
            // (they auto-chain to the next deposit), so a faction whose whole
            // workforce started on iron could NEVER reach its veilstone target —
            // it sat veilstone-starved forever and couldn't afford age-up.
            // Re-task ONE actively-iron-mining worker per think tick toward
            // veilstone until the target is met (one per tick = no churn).
            if (anyVeilSource && crystalMiners < targetVeilstone)
            {
                Entity candidate = Entity.Null;
                float3 candidatePos = default;
                float bestDistSq = float.MaxValue;
                for (int i = 0; i < minerEntities.Length; i++)
                {
                    if (minerFactions[i].Value != faction) continue;
                    var ms = minerStates[i];
                    if (ms.GatheringResource == 1) continue;          // already on veilstone
                    if (ms.State == MinerWorkState.Idle) continue;    // idle ones were handled above
                    if (em.HasComponent<UserMoveOrder>(minerEntities[i])) continue;
                    if (IsCommittedWorker(em, minerEntities[i])) continue;

                    // Distance to the nearest veilstone source: deposit
                    // entity (curse-free maps) or diggable crust vertex.
                    float d2;
                    if (anyVeilstoneOutcropping)
                    {
                        Entity nearest = PickNearestVeilstoneOutcropping(minerTransforms[i].Position, homePos, outcroppingEnts, outcroppingStates, outcroppingTransforms, in veilField, hasVeilField, em, now);
                        if (nearest == Entity.Null) continue;
                        float dx = em.GetComponentData<LocalTransform>(nearest).Position.x - minerTransforms[i].Position.x;
                        float dz = em.GetComponentData<LocalTransform>(nearest).Position.z - minerTransforms[i].Position.z;
                        d2 = dx * dx + dz * dz;
                    }
                    else
                    {
                        if (!VeilMiningUtil.TryFindCrustVertexNear(in veilField,
                                minerTransforms[i].Position, homePos, HomeMiningRadius, out float3 v))
                            continue;
                        float dx = v.x - minerTransforms[i].Position.x;
                        float dz = v.z - minerTransforms[i].Position.z;
                        d2 = dx * dx + dz * dz;
                    }
                    if (d2 < bestDistSq)
                    {
                        bestDistSq = d2;
                        candidate = minerEntities[i];
                        candidatePos = minerTransforms[i].Position;
                    }
                }
                if (candidate != Entity.Null)
                {
                    if (anyVeilstoneOutcropping)
                    {
                        Entity outcropping = PickNearestVeilstoneOutcropping(candidatePos, homePos, outcroppingEnts, outcroppingStates, outcroppingTransforms, in veilField, hasVeilField, em, now);
                        if (outcropping != Entity.Null)
                            GatherCommandHelper.Execute(em, candidate, outcropping);
                    }
                    // Dig-the-sheet only exists in the wall model (§2.5b) —
                    // never send workers into the walkable crust.
                    else if (TheWaningBorder.Core.Config.VeilCrustConstants.CrustPhysical
                             && VeilMiningUtil.TryFindCrustVertexNear(in veilField,
                                 candidatePos, homePos, HomeMiningRadius, out float3 digSite))
                    {
                        GatherVeilCommandHelper.Execute(em, candidate, digSite);
                    }
                }
            }
        }

        private static bool HasAnyIron(Unity.Collections.NativeArray<IronDepositState> states)
        {
            for (int i = 0; i < states.Length; i++)
                if (states[i].Depleted == 0 && states[i].RemainingIron > 0) return true;
            return false;
        }

        private static bool HasAnyVeilstoneOutcropping(Unity.Collections.NativeArray<VeilstoneOutcroppingState> states)
        {
            for (int i = 0; i < states.Length; i++)
                if (states[i].Depleted == 0 && states[i].RemainingVeilstone > 0) return true;
            return false;
        }

        // Home-anchored deposit picks (two passes):
        //   1. Nearest to the WORKER among deposits within HomeMiningRadius
        //      of the Hall — home deposits, workers spread across them.
        //   2. No home deposit at all → nearest to the HALL, so the whole
        //      workforce migrates to the closest outside cluster as a group
        //      instead of scattering toward whatever is nearest to each
        //      worker (which pointed straight at the enemy base on maps with
        //      lopsided resources).

        private static Entity PickNearestIron(float3 from, float3 home,
            Unity.Collections.NativeArray<Entity> ents,
            Unity.Collections.NativeArray<IronDepositState> states,
            Unity.Collections.NativeArray<LocalTransform> transforms,
            EntityManager em, double now)
        {
            float homeSq = HomeMiningRadius * HomeMiningRadius;
            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted == 1 || states[i].RemainingIron <= 0) continue;
                if (IsMarkedUnreachable(em, ents[i], now)) continue;
                float hx = transforms[i].Position.x - home.x;
                float hz = transforms[i].Position.z - home.z;
                if (hx * hx + hz * hz > homeSq) continue;
                float dx = transforms[i].Position.x - from.x;
                float dz = transforms[i].Position.z - from.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            if (best != Entity.Null) return best;

            // Fallback: nearest to home.
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted == 1 || states[i].RemainingIron <= 0) continue;
                if (IsMarkedUnreachable(em, ents[i], now)) continue;
                float hx = transforms[i].Position.x - home.x;
                float hz = transforms[i].Position.z - home.z;
                float d = hx * hx + hz * hz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            return best;
        }

        /// <summary>True when the deposit was recently proven unreachable
        /// (StuckRedirectSystem's UnreachableMark still unexpired) — pickers
        /// must skip it or workers orbit the same blocked node forever.</summary>
        private static bool IsMarkedUnreachable(EntityManager em, Entity deposit, double now)
            => em.HasComponent<UnreachableMark>(deposit)
               && em.GetComponentData<UnreachableMark>(deposit).Until > now;

        private static Entity PickNearestVeilstoneOutcropping(float3 from, float3 home,
            Unity.Collections.NativeArray<Entity> ents,
            Unity.Collections.NativeArray<VeilstoneOutcroppingState> states,
            Unity.Collections.NativeArray<LocalTransform> transforms,
            in VeilField veilField, bool hasVeilField,
            EntityManager em, double now)
        {
            float homeSq = HomeMiningRadius * HomeMiningRadius;
            Entity best = Entity.Null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted == 1 || states[i].RemainingVeilstone <= 0) continue;
                // Never auto-assign onto cursed ground (§2.5b loop damping) —
                // a hazed node costs exposure; only an explicit player order
                // may take that trade.
                if (hasVeilField && veilField.SaturationAt(transforms[i].Position)
                    >= VeilField.CrustThreshold) continue;
                if (IsMarkedUnreachable(em, ents[i], now)) continue;
                float hx = transforms[i].Position.x - home.x;
                float hz = transforms[i].Position.z - home.z;
                if (hx * hx + hz * hz > homeSq) continue;
                float dx = transforms[i].Position.x - from.x;
                float dz = transforms[i].Position.z - from.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            if (best != Entity.Null) return best;

            // Fallback: nearest to home.
            for (int i = 0; i < ents.Length; i++)
            {
                if (states[i].Depleted == 1 || states[i].RemainingVeilstone <= 0) continue;
                if (hasVeilField && veilField.SaturationAt(transforms[i].Position)
                    >= VeilField.CrustThreshold) continue;
                if (IsMarkedUnreachable(em, ents[i], now)) continue;
                float hx = transforms[i].Position.x - home.x;
                float hz = transforms[i].Position.z - home.z;
                float d = hx * hx + hz * hz;
                if (d < bestDist) { bestDist = d; best = ents[i]; }
            }
            return best;
        }

        // ─────────────────────────────────────────────────────────────────
        // RNG (cheap splitmix; deterministic per-system but not per-faction)
        // ─────────────────────────────────────────────────────────────────

        private uint NextRandUint()
        {
            _rngState = unchecked(_rngState * 1103515245u + 12345u);
            return _rngState;
        }

        private float NextRandFloat01()
        {
            return (NextRandUint() & 0x00FFFFFF) / (float)0x01000000;
        }
    }
}
