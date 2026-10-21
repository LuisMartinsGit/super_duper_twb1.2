// SimpleAISystem.cs
// Build-order driven AI for the Age-1 phase.
//
// One AIBrain entity per AI faction. Each think tick, the AI looks at the next
// step of its assigned build order and tries to issue it (queue a unit, place
// a building, queue a research, or trigger age-up). On success, it advances to
// the next step. On failure (resource shortfall, no idle worker, queue full),
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
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Per-faction build-order executor. Not Bursted (touches managed
    /// TechTreeDB / FactionResearchState / Debug.Log).
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SimpleAISystem : SystemBase
    {
        #region Tuning

        /// <summary>Every number below used to be a const in this file;
        /// they live in SimpleAISystem.asset now.</summary>
        static SimpleAISystemConfig Cfg => SimpleAISystemConfig.I;

        #endregion

        // 64-bit splitmix RNG seeded per-faction for placement angles + skip rolls.
        private uint _rngState = 0x12345678u;

        protected override void OnCreate()
        {
            RequireForUpdate<AIBrain>();
            _missions.Clear();
            // Static, keyed on simulated time: a previous match's entries
            // would pass units over at the start of this one.
            _unitUnaffordableUntil.Clear();
            _lastAffordFallback.Clear();

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

            // Every millisecond spent here is charged to the AI's per-frame
            // TIME budget (AIFrameBudget, 2026-10-16), shared with the
            // endgame systems and the tactics micro.
            AIFrameBudget.Enter();
            try { RunBrains(); }
            finally { AIFrameBudget.Exit(); }
            ReportBudgetSummary();
        }

        private void RunBrains()
        {
            float dt = AIClock.Delta(ref _simClockLast);
            var em = EntityManager;

            // Snapshot brains so we can mutate ECS state freely inside the loop.
            var brainsQuery = SystemAPI.QueryBuilder().WithAll<AIBrain, SimpleAIState>().Build();
            using var brainEntities = brainsQuery.ToEntityArray(Allocator.Temp);

            // Match-scoped host memory (failed site searches) must not leak
            // into the next match in the same process.
            if (_memoEpoch != SimCadence.Epoch)
            {
                _memoEpoch = SimCadence.Epoch;
                _siteFails.Clear();
                System.Array.Clear(_stage, 0, _stage.Length);
            }

            // 1. Tick every brain's countdown; collect the ones that are due.
            _dueBrains.Clear();
            foreach (var brainEntity in brainEntities)
            {
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                if (brain.IsActive == 0) continue;

                var aiState = em.GetComponentData<SimpleAIState>(brainEntity);
                aiState.ThinkTimer -= dt;
                em.SetComponentData(brainEntity, aiState);
                if (aiState.ThinkTimer > 0f) continue;
                _dueBrains.Add((brainEntity, aiState.ThinkTimer, (int)brain.Owner));
            }

            // 2. ROUND-ROBIN FRAME BUDGET (2026-09-25 AI perf pass). Most
            // overdue first, so a brain that waited a frame goes next; ties
            // by owner. A brain left waiting keeps its (negative) timer and
            // is due again next frame.
            _dueBrains.Sort((a, b) => a.Timer != b.Timer
                ? a.Timer.CompareTo(b.Timer) : a.Owner.CompareTo(b.Owner));

            // STEPPED THINK (2026-10-15 staged, 2026-10-16 stepped + timed).
            // A think is ThinkSteps short steps — one or two of the old
            // calls each (territory, extractors, armies, defence, waves,
            // strategy, economy, goals, maintenance) — run back to back while
            // the AI's per-frame TIME budget (AIFrameBudget) has room for the
            // next step's measured cost, and resumed next frame when it has
            // not. Three fat stages of 3-9 ms each could not be bounded; a
            // step can. Brains already mid-think go first with their next
            // step; a due brain still mid-think finishes before it starts
            // again (its timer keeps its phase, as below). Host-only, lockstep
            // commands only: WHEN the AI thinks carries no desync risk.
            _work.Clear();
            float stageNow = TheWaningBorder.Core.SimClock.Now;
            foreach (var brainEntity in brainEntities)
            {
                var b = em.GetComponentData<AIBrain>(brainEntity);
                if (b.IsActive == 0) continue;
                int o = (int)b.Owner;
                if (o >= 0 && o < StageSlots && _stage[o] > 0 && _stageBrain[o] == brainEntity)
                    _work.Add((brainEntity, _stage[o]));
            }
            for (int d = 0; d < _dueBrains.Count; d++)
            {
                int o = _dueBrains[d].Owner;
                if (o >= 0 && o < StageSlots && _stage[o] > 0 && _stageBrain[o] == _dueBrains[d].Entity) continue;
                _work.Add((_dueBrains[d].Entity, 1));
            }

            for (int d = 0; d < _work.Count; d++)
            {
                var brainEntity = _work[d].Entity;
                int step = _work[d].Stage;
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                var aiState = em.GetComponentData<SimpleAIState>(brainEntity);
                int owner = (int)brain.Owner;
                bool staged = owner >= 0 && owner < StageSlots;

                // Difficulty is DATA (AoE4 model): one brain, per-tier knobs.
                var profile = AISimpleDifficulty.GetProfile(brain.Difficulty);
                // The static helpers (training pre-flight, age-up saving) read
                // the tier through ProfileOf (SimpleAISystem.EconomyDrive.cs).
                NoteProfile(brain.Owner, profile, brain.Personality);
                float thinkInterval = profile.ThinkInterval;

                if (step > 1)
                {
                    // A later step of a think in progress. It carries the
                    // profile its first step settled (army cap included).
                    bool late = stageNow - _stageSince[owner] > thinkInterval * Cfg.thinkStarvationIntervals;
                    if (!ClaimStep(step, late)) continue;
                    profile = _stageProfile[owner];
                }
                else
                {
                    bool starving = aiState.ThinkTimer <= -thinkInterval * Cfg.thinkStarvationIntervals;
                    if (!AIThinkBudget.TryClaim(starving, _stepCostMs[1])) continue;
                    if (staged)
                    {
                        _stage[owner] = 1;
                        _stageBrain[owner] = brainEntity;
                        _stageSince[owner] = stageNow;
                    }
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
                    aiState.RetreatCooldown = math.max(0f, aiState.RetreatCooldown - thinkInterval);
                }

                // The memoised counts start over whenever the think resumes
                // (other systems ran in between); consecutive steps inside
                // one update share them, as the steps of one stage always did.
                BeginThinkMemo();

                var settings = AISettings.Get();
                // Dampened by tier (Game_AI.md § 3): Easy plays its flavour in
                // full, Expert a fraction of it.
                var personality = settings.For(brain.Personality, profile.PersonalityWeight);
                // MATCH-relative clock. World ElapsedTime starts at APP
                // launch (the bootstrap world predates the menu), so on the
                // first match of a session every "now > Ns" gate — the 30s
                // opening grace, the 240s floor Barracks, the 360s first
                // wave — had already expired before the match began. Anchor
                // on the first think of this world; menu return disposes the
                // world, so the anchor resets per match.
                if (_matchTimeAnchor < 0f)
                    _matchTimeAnchor = TheWaningBorder.Core.SimClock.Now;
                float now = TheWaningBorder.Core.SimClock.Now - _matchTimeAnchor;
                _thinkNow = now;

                // THE ARMY CAP RISES WITH UNSPENT MONEY (§ 6j): every reader of
                // the profile below, and ProfileOf, sees the raised cap.
                if (step == 1)
                {
                    profile.SustainArmyCap += ArmyCapBonus(em, brain.Owner, profile.SustainArmyCap, now);
                    // The Sanctum armies (§ 6t) are on top of the tier's army.
                    profile.SustainArmyCap += SanctumArmyCapBonus(brain.Owner);
                    if (staged) _stageProfile[owner] = profile;
                }
                NoteProfile(brain.Owner, profile, brain.Personality);

                while (true)
                {
                    // The site-search budgets are per old STAGE (territory &
                    // economy / armies & defence / strategy & production), so
                    // a think searches exactly as much as it did before.
                    if (step == 1 || step == 6 || step == 10)
                    {
                        _siteCandidatesLeft = Cfg.siteSearchCandidateBudget;
                        _siteValidationsLeft = Cfg.siteSearchValidateBudget;
                        _sealChecksLeft = Cfg.sealChecksPerThink;
                    }

                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    RunStep(step, em, brainEntity, in brain, ref aiState, settings, personality,
                            in profile, thinkInterval, now);
                    NoteStepCost(step, t0, brain.Owner);

                    if (step >= ThinkSteps)
                    {
                        if (staged) _stage[owner] = 0;
                        break;
                    }
                    step++;
                    if (!staged) continue;           // outside the cursor table: one go, as before
                    _stage[owner] = (byte)step;
                    _stageSince[owner] = stageNow;
                    // Keep going while the frame has room for the next step.
                    if (!ClaimStep(step, false)) break;
                }
                em.SetComponentData(brainEntity, aiState);
            }
        }

        /// <summary>One step of a think — the calls of the old stage blocks,
        /// in their old order (1-5 territory &amp; economy, 6-9 armies &amp;
        /// defence, 10-13 strategy, budget, age-up, production).</summary>
        private void RunStep(int step, EntityManager em, Entity brainEntity, in AIBrain brain,
            ref SimpleAIState aiState, TheWaningBorder.Data.AI.AISettingsSO settings,
            TheWaningBorder.Data.AI.AISettingsSO.PersonalityBlock personality,
            in AIDifficultyProfile profile, float thinkInterval, float now)
        {
            switch (step)
            {
                case 1:
                {
                    // THE LATE-GAME ALL-IN (Game_AI.md § 6n): armed past
                    // allInAfterSeconds, before anything below drafts the army.
                    TickAllInDoctrine(em, brain.Owner, personality, now);

                    // Worker tasking is gone: income comes from held territory, not
                    // from workers on deposits (Regions.md §4). The AI's economic
                    // decision is now WHERE TO CLAIM, which belongs in the build
                    // order rather than here.

                    // Replace any military/workers that died since the build order
                    // queued them. Runs before the next step so replacements take
                    // priority on the train queue and resources.
                    ReplaceLostUnits(em, brainEntity, brain.Owner, ref aiState,
                        RoleBudget.For(personality.personality, personality.weight), profile.IntelFreshnessSeconds, now);

                    // Scout movement is owned by ScoutDirectorSystem (AI plan M3):
                    // zone-based exploration + recon requests replace the old
                    // random PatrolWithScouts wander.

                    // ANTI-STAGNATION: keep population headroom at all times.
                    // Without this, the army + worker floors filled the cap, every
                    // train queue clogged, and the whole faction stalled into
                    // "workers mining, nothing else" — the maintenance loop never
                    // built Huts.
                    EnsurePopulationHeadroom(em, brain.Owner);
                    break;
                }
                case 2:
                {
                    // TERRITORY IS THE ECONOMY (Regions.md §4): a region yields
                    // only to whoever holds it, and a Hall is what holds it. This
                    // is the "WHERE TO CLAIM" decision noted above — opportunistic,
                    // because it depends on the bank and on what ground is still
                    // free, neither of which a scripted build order can know.
                    // DEFEND-BASED EXPANSION (Game_AI.md § 5b): claim squads go
                    // out in parallel to every free claimable territory the army
                    // can spare one for, until it is stretched — there is no
                    // territory cap (Territory_Claims.md §10, 2026-10-04).
                    EnsureTerritoryClaim(em, brain.Owner, aiState.Posture, profile, now);
                    break;
                }
                case 3:
                {
                    // …and LOCK what the army cannot garrison: a Fortress in held
                    // ground locks it and re-links cut-off ground (Game_AI.md §
                    // Fortress expansion). It never gates claiming.
                    // PER-TERRITORY DEVELOPMENT (Game_AI.md 5g): every held
                    // territory in the operator's order — resource buildings, the
                    // Fortress (its spot reserved from the moment the ground is
                    // held), periphery watch towers, then ONE production building.
                    // PRODUCTION SATURATION (Game_AI.md 5g): sampled every think,
                    // so ProductionGate can tell busy production from idle.
                    SampleProductionSaturation(em, brain.Owner, now);
                    TickTerritoryDevelopment(em, brain.Owner, now);

                    EnsureFortressExpansion(em, brain.Owner, now);
                    break;
                }
                case 4:
                {
                    // A LOST EXTRACTOR IS OWED A REBUILD (Game_AI.md § 5i): read
                    // the losses before the extractor walk, which then puts the
                    // lost kind first.
                    TrackExtractorLosses(em, brain.Owner, now);

                    // …and INVEST in the ground already held. With nodes depleting,
                    // an unworked territory gets poorer whether or not anyone is
                    // extracting from it, so the extraction buildings are not a
                    // late-game optimisation any more.
                    EnsureExtractors(em, brain.Owner, now);
                    ManageTradingOutposts(em, brain.Owner, now);
                    break;
                }
                case 5:
                {
                    // VEILSTONE SURPLUS (Game_AI.md 5e): the Outpost's research,
                    // and the throttled veilstone-held state line.
                    TickSurplus(em, brain.Owner, now);
                    // The Vault, and the ECON / PRODUCTION measurement lines
                    // (Game_AI.md § 5h).
                    TickEconomyDrive(em, brain.Owner, aiState.Posture, profile, now);
                    break;
                }
                case 6:
                {
                    // Army missions: prune the dead, regroup finished armies,
                    // retreat outmatched ones (per mission, not globally).
                    UpdateMissions(em, brainEntity, brain.Owner, ref aiState, settings, now);

                    // TACTICAL layer: what each army does once it is in contact.
                    // Runs AFTER UpdateMissions, so every mission it sees is live
                    // and has already had its dead pruned. Dispatch decides where
                    // an army goes; this decides what it fights when it gets
                    // there, and keeps it together while it does.
                    TickArmyTactics(em, brain.Owner, now);
                    break;
                }
                case 7:
                {
                    // MAN THE FORTIFICATIONS (Game_AI.md § 6s): towers kept
                    // garrisoned, threatened wall stretches manned, a few
                    // sentries on the long walls. First in the step, so a
                    // posted unit is already out of the economy defence and
                    // Defend's recall below.
                    TickWallDefence(em, brain.Owner, in aiState, now);

                    // DEFEND THE ECONOMY (Game_AI.md § 5i): an attack on an
                    // extractor, a house or a worker in held ground — the curse's
                    // raids included — gets a response sized to the attacker,
                    // never at parity, from the standing army.
                    TickEconomyDefence(em, brain.Owner, profile, now);

                    // M4: evaluate the posture (threat near base -> Defend; gutted
                    // army -> Rebuild; assembled army + healthy bank -> Pressure)
                    // and act on Defend (recall + repair) / M6 retreat.
                    EvaluatePosture(em, brain.Owner, ref aiState, settings, personality);
                    // THE WALL GUARD (Game_AI.md § 3b): a Turtle posts part of its
                    // idle army at the home ring's gates.
                    TickWallGuard(em, brainEntity, brain.Owner, in aiState, personality, now);
                    break;
                }
                case 8:
                {
                    // ATTACK WAVES (2026-08-04): pressure is a RHYTHM, not a
                    // one-off. Runs in BOTH phases — during the build order
                    // (waves start the moment the first-attack gate passes, even
                    // mid-script) and forever after it. Scripted LaunchAttack
                    // steps still fire as authored strategy openings.
                    // THE SCOUTS GO WHERE THE INCOME IS (Game_AI.md § 6f): an
                    // income-targeting tier keeps a recon request on the enemy's
                    // holdings until it knows enough of them to aim at.
                    TickIncomeRecon(em, brain.Owner, ref aiState, profile, brainEntity, now);

                    TickAttackWaves(em, brainEntity, brain.Owner, ref aiState,
                        settings, personality, profile, now);

                    // THE IDLE ARMY CLEARS THE CURSE (Game_AI.md § 5b): no wave
                    // out, no defence need, surplus above the standing floor —
                    // march on the weakest curse-held neighbour so it can be
                    // claimed.
                    TryClearAdjacentCurse(em, brain.Owner, aiState, profile, now);
                    break;
                }
                case 9:
                {
                    // CORRUPTION COUNTERPLAY (2026-08-04): when veilstone-poor,
                    // strike the SmallNode hazing the home patches — without a
                    // military answer, corruption bleeds the AI's veilstone
                    // income out patch by patch until the non-skippable
                    // choice-building gate freezes the whole build order.
                    // THE FIRST RELIGION POINT (2026-10-03): with no Temple and
                    // no RP the faction hunts a curse node for it; while that
                    // hunt owns the army the reclaim squad stands down.
                    // AGE-UP FIRST (2026-10-04): an Age 0 faction saving for its
                    // landmark sends the reclaim squad only against curse at its
                    // doorstep — the 60-minute batch's age-up stragglers (14-23
                    // min, one never) each fed 65-112 units to curse nodes one at
                    // a time and never banked the landmark's supplies.
                    bool savingAgeUpNow = !HasAgedUp(em, brain.Owner)
                        && now > personality.ageUpPushSeconds
                        && SavingForAgeUp(brain.Personality, aiState, now, personality.ageUpPushSeconds);
                    if (!TryHuntFirstReligionPoint(em, brain.Owner, ref aiState, now))
                        TryReclaimCorruptedPatches(em, brain.Owner, savingAgeUpNow, now);

                    // THE SHARDROOT AND THE HEALERS (2026-10-07, Game_AI.md § 6l-6m):
                    // strike for the artifact / bring it home / king rides out;
                    // Litharchs trained at the Temple and walked after the army.
                    TickShardroot(em, brain.Owner, ref aiState, now);
                    // THE SANCTUM ARMY (2026-10-20, Game_AI.md § 6t).
                    TickSanctumArmies(em, brain.Owner, ref aiState, now);
                    {
                        Entity home = FindFactionBuilding<HallTag>(em, brain.Owner);
                        if (home != Entity.Null && em.HasComponent<LocalTransform>(home))
                            TickSupport(em, brain.Owner, em.GetComponentData<LocalTransform>(home).Position, now);
                    }
                    break;
                }
                case 10:
                {
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
                    // advancement gate tilts the split to Advancement instead of
                    // hard-pausing the economy.
                    // The age-up director (below) IS the advancement gate: the
                    // age-up costs 700 SUPPLIES, and without the wallet tilt the
                    // economy spends supplies as fast as they arrive — the AIs
                    // sat on 1500 iron/veilstone for whole matches while never
                    // banking the one resource that gates. Aggressive / Rush push
                    // their ONE Age 0 wave first, and only then save (Age_0.md §
                    // The AI and the age-up). (The per-personality scripted build
                    // orders that used to open this gate are gone, 2026-10-05:
                    // their step pointer never advanced, so the gate never fired.)
                    bool ageUpPush = now > personality.ageUpPushSeconds && aiState.AgeUpIssued == 0
                                     && SavingForAgeUp(brain.Personality, aiState, now, personality.ageUpPushSeconds);
                    bool advancementGate = ageUpPush;
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

                    // ECONOMY DISTRESS (Game_AI.md § 5i): with the income just
                    // measured, pause the ordinary savings goals while supply
                    // income is collapsed, the economy is under attack, or a lost
                    // extractor is owed.
                    EvaluateEconomyDistress(em, brain.Owner, aiState.Posture, now);

                    // A LOST SOLE TRAINER COMES FIRST (2026-10-04, Game_AI.md
                    // 6c): a production line the faction had and has none of —
                    // the Barracks above all — is replaced before the age-up
                    // director and the economy tick spend the bank.
                    EnsureLostTrainersRebuilt(em, brain.Owner, now);

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
                    if (ageUpPush)
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
                        // THE LANDMARK IS THE AGE-UP (Age_0.md § Age-up by
                        // landmark): save for it, place it, and the age-up lands
                        // when it finishes. A landmark destroyed mid-build is gone
                        // with everything spent — the latch re-opens so the
                        // director pays for another.
                        if (!FactionHasLandmark(em, brain.Owner))
                        {
                            aiState.OpportunisticChoiceStarted = 0;
                            string landmark = AgeUpLandmark(em, brain.Owner);
                            // STRICT: the hold does not breathe open every four
                            // minutes while the landmark is unpaid — the release
                            // window was exactly when the supplies drained away.
                            if (TheWaningBorder.Data.BuildCosts.TryGet(landmark, out var choiceCost))
                                AIPivotalReserve.Set(brain.Owner, "AgeUpChoice", choiceCost, strict: true);
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
                                && TryBuildBuilding(em, brain.Owner, landmark))
                            {
                                aiState.OpportunisticChoiceStarted = 1;
                                AIPivotalReserve.Clear(brain.Owner, "AgeUpChoice");
                                AILogger.Log(brain.Owner, "CULTURE",
                                    $"age-up director: choice building started at {(int)now}s");
                            }
                        }
                        else
                        {
                            // Landmark placed — nothing left to buy. TryAgeUp
                            // only observes the era advancing when it finishes.
                            AIPivotalReserve.Clear(brain.Owner, "AgeUpChoice");
                            if (TryAgeUp(em, brain.Owner, ref aiState))
                            {
                                AIPivotalReserve.Clear(brain.Owner, "AgeUp");
                                AIPivotalReserve.Clear(brain.Owner, "AgeUpChoice");
                                AILogger.Log(brain.Owner, "CULTURE",
                                    $"age-up director: issued at {(int)now}s (build order at step {aiState.StepIndex})");
                            }
                        }
                    }
                    break;
                }
                case 11:
                {
                    // Economy spends whatever advancement did not claim.
                    TickEconomy(em, brain.Owner, ref aiState, personality, profile, now);

                    // ENDGAME RESEARCH SWEEP (era 2+, ~20 s cadence): once the
                    // authored economy ladder has no affordable next step (or
                    // from era 3 regardless), walk every owned research-capable
                    // building and buy whatever its def still offers — the
                    // "eventually research ALL of it" mop-up.
                    TickEndgameResearchSweep(em, brain.Owner, now);
                    break;
                }
                case 12:
                {
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
                    break;
                }
                case 13:
                {
                    // The maintenance loop still runs: it owns replacements,
                    // scouting and the attack cadence, none of which are goals.
                    RunMaintenanceLoop(em, brainEntity, brain, ref aiState, settings,
                                       personality, profile, now);
                    break;
                }
            }
        }

        // ── Step budget (2026-10-16) ─────────────────────────────────────

        /// <summary>Steps one think is split into (engine shape: the number of
        /// cases in RunStep, not a tunable).</summary>
        private const int ThinkSteps = 13;

        private static readonly string[] ThinkStepNames =
        {
            "", "replace+housing", "claims", "territory dev", "extractors", "surplus+econ drive",
            "missions+tactics", "econ defence+posture", "recon+waves+curse", "religion+shardroot+support",
            "plan+budget+age-up", "economy+research", "goals", "maintenance",
        };

        /// <summary>Running cost estimate per step, ms (exponential average of
        /// what the step measured; host diagnostics + the budget's look-ahead).</summary>
        private static readonly double[] _stepCostMs = new double[ThinkSteps + 1];
        private static readonly double[] _stepPeakMs = new double[ThinkSteps + 1];

        /// <summary>May the next step start this frame? Within the AI's time
        /// budget (with room for the step's running cost), or — a think in
        /// progress for longer than the starvation window — forced, at most
        /// maxForcedAIWorkPerFrame times a frame.</summary>
        private static bool ClaimStep(int step, bool late)
        {
            double est = _stepCostMs[math.clamp(step, 0, ThinkSteps)];
            return late ? AIFrameBudget.TryGrantOrForce(est) : AIFrameBudget.TryGrant(est);
        }

        private static void NoteStepCost(int step, long t0, Faction faction)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0
                        / System.Diagnostics.Stopwatch.Frequency;
            ref double avg = ref _stepCostMs[step];
            avg = avg <= 0.0 ? ms : avg * 0.8 + ms * 0.2;
            if (ms > _stepPeakMs[step]) _stepPeakMs[step] = ms;
            if (ms >= Cfg.aiStepSpikeLogMs)   // (the strings only when a line is written)
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                    "AIStep " + ThinkStepNames[step], ms, faction.ToString(), Cfg.aiStepSpikeLogMs);
        }

        private float _nextBudgetSummary;

        /// <summary>Every aiBudgetSummarySeconds: the AI's average and peak
        /// ms per frame, and each step's running and peak cost, to Perf.log.</summary>
        private void ReportBudgetSummary()
        {
            float every = Cfg.aiBudgetSummarySeconds;
            if (every <= 0f) return;
            float t = UnityEngine.Time.realtimeSinceStartup;
            if (t < _nextBudgetSummary) return;
            _nextBudgetSummary = t + every;
            AIFrameBudget.TakeWindow(out double avg, out double peak, out int frames);
            if (frames == 0) return;
            var sb = new System.Text.StringBuilder(256);
            sb.Append($"avg/frame (budget {AIFrameBudget.BudgetMs:0.0}), peak {peak:0.0} ms over {frames} frames | steps:");
            for (int i = 1; i <= ThinkSteps; i++)
            {
                sb.Append($" {i}={_stepCostMs[i]:0.00}/{_stepPeakMs[i]:0.0}");
                _stepPeakMs[i] = 0.0;
            }
            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report("AI budget", avg, sb.ToString(), 0.0);
        }


        private double _simClockLast = -1d;

        // Match-relative clock anchor: world ElapsedTime at the first think
        // (see the OnUpdate comment). -1 = not yet anchored.
        private float _matchTimeAnchor = -1f;

        /// <summary>The current think's match-relative clock, for helpers
        /// that are not handed `now` (the failed-site-search memory).</summary>
        private float _thinkNow;

        private int _memoEpoch = -1;

        /// <summary>Brains due this frame (host scratch, cleared per update).</summary>
        private readonly System.Collections.Generic.List<(Entity Entity, float Timer, int Owner)> _dueBrains
            = new System.Collections.Generic.List<(Entity Entity, float Timer, int Owner)>(8);

        // ── Stepped think (see RunBrains; ThinkSteps sits with RunStep) ───
        /// <summary>Owners a stage cursor is kept for (player factions); a
        /// brain outside it thinks in one go, as before.</summary>
        private const int StageSlots = 16;
        /// <summary>Per owner: the NEXT step of the think in progress (0 = none).</summary>
        private readonly byte[] _stage = new byte[StageSlots];
        private readonly Entity[] _stageBrain = new Entity[StageSlots];
        private readonly float[] _stageSince = new float[StageSlots];
        private readonly AIDifficultyProfile[] _stageProfile = new AIDifficultyProfile[StageSlots];
        private readonly System.Collections.Generic.List<(Entity Entity, int Stage)> _work
            = new System.Collections.Generic.List<(Entity Entity, int Stage)>(16);

        // (The age-up push time moved into the per-difficulty profile —
        // AIDifficultyProfile.AgeUpPushSeconds. It was a single 300 s constant
        // for every tier, which made Easy and Expert advance on the same
        // clock; the ladder now scales 90 s [Expert] to 200 s [Easy].)

        // (The sustained-production army ceiling moved into the per-difficulty
        // profile — AIDifficultyProfile.SustainArmyCap.)

        private static readonly System.Collections.Generic.HashSet<int> _anyFreeOwned =
            new System.Collections.Generic.HashSet<int>();

        private static bool AnyFreeNodeFor(EntityManager em, Faction faction, string buildingId)
        {
            var required = TheWaningBorder.World.Regions.TerritoryOwnership
                .RequiredNodeFor(buildingId);
            if (required == null) return true;      // not an extractor

            var mine = TheWaningBorder.World.Regions.TerritoryOwnership.TerritoriesOf(faction);
            if (mine.Count == 0) return true;       // pre-partition: do not block
            var owned = _anyFreeOwned;              // pooled
            owned.Clear();
            owned.UnionWith(mine);
            var snap = BuildSiteSnapshot.Current(em);

            var q = AIQueryCache.NodeAt(em, required.Value);
            using var xfs = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(
                Unity.Collections.Allocator.Temp);
            for (int i = 0; i < xfs.Length; i++)
            {
                var p = xfs[i].Position;
                int r = TheWaningBorder.World.Regions.RegionMap.RegionAt(p.x, p.z);
                if (r == TheWaningBorder.World.Regions.RegionMap.None || !owned.Contains(r)) continue;
                if (snap.OnFreeNodeFor(em, buildingId, p.x, p.z)) return true;
            }
            return false;
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
