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

        // 64-bit splitmix RNG seeded per-faction for placement angles + skip rolls.
        private uint _rngState = 0x12345678u;

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
            // AI brains run on the HOST ONLY in multiplayer. A client that also
            // thought for the AI applied every decision twice — its own brain
            // executes directly (CommandSource.AI does not queue on a client)
            // and the host's replicated command arrives on top — and the two
            // _rngState streams forked on the first differing call anyway.
            // docs/Multiplayer_LAN_Readiness.md
            if (!GameSettings.ShouldRunAIBrains()) return;

            float dt = SystemAPI.Time.DeltaTime;
            var em = EntityManager;
            double perfT0 = UnityEngine.Time.realtimeSinceStartupAsDouble;
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
                // PHASE stagger (2026-08-16; replaces the period stagger of
                // 2026-08-05). Different PERIODS per faction drift brains
                // apart but also beat back into alignment every ~27 ticks,
                // and a long frame decrements every timer past zero at once
                // — re-arming to a fixed value collapsed all phases into the
                // same frame again (the "AIThink brains 4" spikes). Adding
                // the interval to the EXPIRED timer preserves each brain's
                // phase across hitches; the clamp re-seeds a deterministic
                // per-owner offset only when a stall dragged the timer deep
                // negative. Deterministic, so lockstep peers agree.
                aiState.ThinkTimer += thinkInterval;
                if (aiState.ThinkTimer < thinkInterval * 0.25f)
                    aiState.ThinkTimer = thinkInterval * (0.25f + 0.125f * ((int)brain.Owner % 8));
                perfThinks++;
                aiState.RetreatCooldown = math.max(0f, aiState.RetreatCooldown - thinkInterval);

                var settings = AISettings.Get();
                var personality = settings.For(brain.Personality);
                // MATCH-relative clock. World ElapsedTime starts at APP
                // launch (the bootstrap world predates the menu), so on the
                // first match of a session every "now > Ns" gate — the 30s
                // opening grace, the 240s floor Barracks, the 360s first
                // wave — had already expired before the match began. Anchor
                // on the first think of this world; menu return disposes the
                // world, so the anchor resets per match.
                if (_matchTimeAnchor < 0f)
                    _matchTimeAnchor = (float)SystemAPI.Time.ElapsedTime;
                float now = (float)SystemAPI.Time.ElapsedTime - _matchTimeAnchor;

                // Miner tasking is gone: income comes from held territory, not
                // from workers on deposits (Regions.md §4). The AI's economic
                // decision is now WHERE TO CLAIM, which belongs in the build
                // order rather than here.

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

                // TERRITORY IS THE ECONOMY (Regions.md §4): a region yields
                // only to whoever holds it, and a Hall is what holds it. This
                // is the "WHERE TO CLAIM" decision noted above — opportunistic,
                // because it depends on the bank and on what ground is still
                // free, neither of which a scripted build order can know.
                EnsureTerritoryClaim(em, brain.Owner, now);

                // …and INVEST in the ground already held. With nodes depleting,
                // an unworked territory gets poorer whether or not anyone is
                // extracting from it, so the extraction buildings are not a
                // late-game optimisation any more.
                EnsureExtractors(em, brain.Owner, now);

                // Army missions: prune the dead, regroup finished armies,
                // retreat outmatched ones (per mission, not globally).
                UpdateMissions(em, brainEntity, brain.Owner, ref aiState, settings, now);

                // TACTICAL layer: what each army does once it is in contact.
                // Runs AFTER UpdateMissions, so every mission it sees is live
                // and has already had its dead pruned. Dispatch decides where
                // an army goes; this decides what it fights when it gets
                // there, and keeps it together while it does.
                TickArmyTactics(em, brain.Owner, now);

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
                // strike the SmallNode hazing the home patches — without a
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
                // The age-up director (below) counts as an active advancement
                // gate too: the age-up costs 700 SUPPLIES, and without the
                // wallet tilt the economy spends supplies as fast as they
                // arrive — the AIs sat on 1500 iron/veilstone for whole
                // matches while never banking the one resource that gates.
                if (now > profile.AgeUpPushSeconds && aiState.AgeUpIssued == 0)
                    advancementGate = true;
                // ── STRATEGY FIRST: pick (or keep) a committed plan, then let
                //    that plan set the budget. ──
                //
                // The advancement gate is now an INPUT to the decision rather
                // than an override of it. It used to tilt 65% of income to a
                // wallet that never lends, for as long as an age-up was
                // pending — which was most of the match, and which is why four
                // AIs managed 17 combat units between them in 30 minutes.
                // Now it argues for the Tech plan, and the Tech plan's commit
                // window is what bounds the push.
                TickStrategicPlan(em, brain.Owner, ref aiState, personality,
                    profile, advancementGate, now);

                var planProfile = PlanProfileOf(brain.Owner);
                AIBudget.EvaluateWeights(planProfile, aiState.Posture,
                    out float wAdv, out float wMil, out float wEco);
                AIBudget.Tick(em, brain.Owner, wAdv, wMil, wEco, thinkInterval, now);

                // ORDER MATTERS: the age-up director runs BEFORE the economy
                // tick (2026-08-18). It used to run after, so every think the
                // economy spent the bank on workers, huts and the floor
                // Barracks and the director inherited the change — an Expert
                // holding 220 supplies, enough for its 210-supply Shrine,
                // bought a Barracks instead and advanced nothing. Advancing
                // is the priority while an age-up is pending, so it gets first
                // call on the bank; the economy still spends everything left
                // over in the same tick.

                // AGE-UP DIRECTOR (2026-08-16): the authored AgeUp step is
                // 13-30 steps deep and every stuck step burns up to 90s, so
                // most strategies never reached it inside a playable match —
                // zero AI age-ups in whole logged games. From mid-game on,
                // age up the moment the requirements hold, wherever the
                // build order stands. TryAgeUp latches AgeUpIssued, turning
                // the authored step into a no-op afterwards. If the choice
                // building itself is missing (Turtle's Temple was silently
                // timeout-skipped for a whole match), start one.
                // Difficulty-scaled push (2026-08-18). Was a single hard-coded
                // 300 s for every tier, which made the age-up clock identical
                // on Easy and Expert and put even the fastest AI's Shrine
                // START at 5 minutes — after which it still had to build the
                // Shrine and bank 700 supplies. Now Expert pushes at 90 s and
                // Easy at 200 s, so the whole ladder lands in its intended
                // window (see AIDifficultyProfile.AgeUpPushSeconds).
                if (now > profile.AgeUpPushSeconds && aiState.AgeUpIssued == 0)
                {
                    // ARM THE SAVINGS HOLD. Weight-tilting alone does not
                    // work here (2026-08-18): the wallets are accounting over
                    // ONE shared bank, so an Expert AI showed 395 supplies of
                    // Advancement entitlement while its actual bank held 18 —
                    // army growth and the research sweep had spent every
                    // supply the moment it arrived, and the 210-supply Shrine
                    // was never affordable. AIPivotalReserve is the existing
                    // answer to precisely this ("500-supply lump sums never
                    // formed"): it holds discretionary spending until the bank
                    // covers the pending purchase.
                    if (!FactionHasChoiceBuilding(em, brain.Owner))
                    {
                        if (TheWaningBorder.Data.BuildCosts.TryGet("ShrineOfRidan", out var choiceCost))
                            AIPivotalReserve.Set(brain.Owner, "AgeUpChoice", choiceCost);
                        // BANK-DIRECT, not wallet-budgeted (2026-08-18,
                        // log-proven): this is the OVERRIDE path — its whole
                        // job is "age up the moment the requirements hold,
                        // wherever the build order stands". Buying through the
                        // Advancement wallet re-imposed the throttle the
                        // director exists to escape: one AI sat on 1227
                        // banked supplies at 468s and still did not start its
                        // Shrine until 516s, because the wallet slice had not
                        // filled. Age-up then landed at 662s, well past the
                        // ten-minute mark the design wants even the weakest
                        // AI to beat. TryBuildBuilding spends from the bank,
                        // and TryAgeUp below already gates on the bank too.
                        if (aiState.OpportunisticChoiceStarted == 0
                            && TryBuildBuilding(em, brain.Owner, "ShrineOfRidan"))
                        {
                            aiState.OpportunisticChoiceStarted = 1;
                            AIPivotalReserve.Clear(brain.Owner, "AgeUpChoice");
                            AILogger.Log(brain.Owner, "CULTURE",
                                $"age-up director: choice building started at {(int)now}s");
                        }
                    }
                    else
                    {
                        // Choice building up — now hold the bank for the
                        // 700-supply age-up itself.
                        AIPivotalReserve.Set(brain.Owner, "AgeUp", CultureConfig.AgeUpCost);
                        if (TryAgeUp(em, brain.Owner, ref aiState))
                        {
                            AIPivotalReserve.Clear(brain.Owner, "AgeUp");
                            AIPivotalReserve.Clear(brain.Owner, "AgeUpChoice");
                            AILogger.Log(brain.Owner, "CULTURE",
                                $"age-up director: issued at {(int)now}s (build order at step {aiState.StepIndex})");
                        }
                    }
                }

                // Economy spends whatever advancement did not claim.
                TickEconomy(em, brain.Owner, ref aiState, profile, now);

                // ENDGAME RESEARCH SWEEP (era 2+, ~20 s cadence): once the
                // authored economy ladder has no affordable next step (or
                // from era 3 regardless), walk every owned research-capable
                // building and buy whatever its def still offers — the
                // "eventually research ALL of it" mop-up.
                TickEndgameResearchSweep(em, brain.Owner, now);

                // ── DECISION LAYER: a priority list, not a script. ──
                //
                // The strict build order is gone. It froze everything behind
                // any step it could not pay for — measured at 31 "wallet
                // short" refusals in ten minutes, 29 of them the opening's own
                // Worker — while a dozen standing checks drained the same
                // wallets with no coordination. Five blockers were found and
                // fixed in that chain and each revealed the next.
                //
                // SimpleAISystem.Goals.cs walks an ordered want-list and does
                // the first thing that is unmet, unlocked and affordable. No
                // head-of-line blocking, no timeouts, and the AI is never idle
                // while it can afford something further down the list.
                TickGoals(em, brainEntity, brain, ref aiState, settings,
                          personality, profile, now);

                // The maintenance loop still runs: it owns replacements,
                // scouting and the attack cadence, none of which are goals.
                RunMaintenanceLoop(em, brainEntity, brain, ref aiState, settings,
                                   personality, profile, now);
                em.SetComponentData(brainEntity, aiState);
            }
        }

        private const float StepTimeoutSeconds = 90f;

        // Match-relative clock anchor: world ElapsedTime at the first think
        // (see the OnUpdate comment). -1 = not yet anchored.
        private float _matchTimeAnchor = -1f;

        // (The age-up push time moved into the per-difficulty profile —
        // AIDifficultyProfile.AgeUpPushSeconds. It was a single 300 s constant
        // for every tier, which made Easy and Expert advance on the same
        // clock; the ladder now scales 90 s [Expert] to 200 s [Easy].)

        // (The sustained-production army ceiling moved into the per-difficulty
        // profile — AIDifficultyProfile.SustainArmyCap.)

        /// <summary>
        /// Steps the stuck-step timeout may abandon. AgeUp and the choice
        /// building are the spine of the game plan — never skipped.
        /// </summary>
        /// <summary>
        /// Is there a free node anywhere this faction could put that extractor?
        /// Territory-scoped: a node on someone else's ground is not available,
        /// and the placement gate would refuse the site anyway.
        /// </summary>
        private static bool AnyFreeNodeFor(EntityManager em, Faction faction, string buildingId)
        {
            var required = TheWaningBorder.World.Regions.TerritoryOwnership
                .RequiredNodeFor(buildingId);
            if (required == null) return true;      // not an extractor

            var mine = TheWaningBorder.World.Regions.TerritoryOwnership.TerritoriesOf(faction);
            if (mine.Count == 0) return true;       // pre-partition: do not block
            var owned = new System.Collections.Generic.HashSet<int>(mine);

            var q = em.CreateEntityQuery(required.Value,
                ComponentType.ReadOnly<Unity.Transforms.LocalTransform>());
            using var xfs = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(
                Unity.Collections.Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                var p = xfs[i].Position;
                int r = TheWaningBorder.World.Regions.RegionMap.RegionAt(p.x, p.z);
                if (r == TheWaningBorder.World.Regions.RegionMap.None || !owned.Contains(r)) continue;
                if (TheWaningBorder.World.Regions.TerritoryOwnership.OnFreeNodeFor(
                        em, buildingId, p.x, p.z)) return true;
            }
            return false;
        }

        private static bool IsSkippableStep(BuildOrderStep step)
        {
            if (step.Kind == BuildStepKind.AgeUp) return false;
            if (step.Kind == BuildStepKind.BuildBuilding
                && BuildingFactory.IsChoiceBuilding(step.Id)) return false;
            // The Temple satisfies TryAgeUp's choice-building gate (Turtle's
            // build order uses it as its choice) but is NOT in
            // BuildingFactory.ChoiceBuildingIds — the timeout silently
            // dropped it and froze the faction on the AgeUp step forever.
            if (step.Kind == BuildStepKind.BuildBuilding
                && step.Id == "TempleOfRidan") return false;
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
                // Charged to a wallet like every other purchase — see
                // CategoryForBuilding. Bank-direct here was the leak that made
                // Military and Advancement fight over the same money.
                BuildStepKind.BuildBuilding    => TryBuildBuildingFromBuildOrder(em, faction, step.Id),
                BuildStepKind.Research         => TryResearchTech(em, faction, step.Id),
                BuildStepKind.AgeUp            => TryAgeUp(em, faction, ref aiState),
                BuildStepKind.SetVeilstoneTarget => SetVeilstoneTarget(ref aiState, step.IntArg),
                BuildStepKind.LaunchAttack     => TryLaunchAttack(em, brainEntity, faction, step.IntArg, ref aiState, settings, personality, profile, now),
                _                              => true,  // unknown step kind: skip silently
            };
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
