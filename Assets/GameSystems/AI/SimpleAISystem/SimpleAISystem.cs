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

            float dt = AIClock.Delta(ref _simClockLast);
            var em = EntityManager;
            double perfT0 = UnityEngine.Time.realtimeSinceStartupAsDouble;
            int perfThinks = 0;

            // Snapshot brains so we can mutate ECS state freely inside the loop.
            var brainsQuery = SystemAPI.QueryBuilder().WithAll<AIBrain, SimpleAIState>().Build();
            using var brainEntities = brainsQuery.ToEntityArray(Allocator.Temp);

            // Match-scoped host memory (failed site searches) must not leak
            // into the next match in the same process.
            if (_memoEpoch != SimCadence.Epoch)
            {
                _memoEpoch = SimCadence.Epoch;
                _siteFails.Clear();
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

            // 2. ROUND-ROBIN FRAME BUDGET (2026-09-25 AI perf pass). At most
            // maxBrainThinksPerFrame heavy thinks per rendered frame, shared
            // with the endgame systems (AIThinkBudget). Most overdue first, so
            // a brain that waited a frame goes next; ties by owner. A brain
            // left waiting keeps its (negative) timer and is due again next
            // frame; one overdue by a whole interval thinks regardless.
            _dueBrains.Sort((a, b) => a.Timer != b.Timer
                ? a.Timer.CompareTo(b.Timer) : a.Owner.CompareTo(b.Owner));

            for (int d = 0; d < _dueBrains.Count; d++)
            {
                var brainEntity = _dueBrains[d].Entity;
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                var aiState = em.GetComponentData<SimpleAIState>(brainEntity);

                // Difficulty is DATA (AoE4 model): one brain, per-tier knobs.
                var profile = AISimpleDifficulty.GetProfile(brain.Difficulty);
                // The static helpers (training pre-flight, age-up saving) read
                // the tier through ProfileOf (SimpleAISystem.EconomyDrive.cs).
                NoteProfile(brain.Owner, profile, brain.Personality);
                float thinkInterval = profile.ThinkInterval;

                bool starving = aiState.ThinkTimer <= -thinkInterval * Cfg.thinkStarvationIntervals;
                if (!AIThinkBudget.TryClaim(starving)) continue;
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

                // Fresh think: memoised counts, the site-search budgets and
                // the think clock start over.
                BeginThinkMemo();
                _siteCandidatesLeft = Cfg.siteSearchCandidateBudget;
                _siteValidationsLeft = Cfg.siteSearchValidateBudget;
                _sealChecksLeft = Cfg.sealChecksPerThink;

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
                profile.SustainArmyCap += ArmyCapBonus(em, brain.Owner, profile.SustainArmyCap, now);
                NoteProfile(brain.Owner, profile, brain.Personality);

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
                // VEILSTONE SURPLUS (Game_AI.md 5e): the Outpost's research,
                // and the throttled veilstone-held state line.
                TickSurplus(em, brain.Owner, now);
                // The Vault, and the ECON / PRODUCTION measurement lines
                // (Game_AI.md § 5h).
                TickEconomyDrive(em, brain.Owner, aiState.Posture, profile, now);

                // Army missions: prune the dead, regroup finished armies,
                // retreat outmatched ones (per mission, not globally).
                UpdateMissions(em, brainEntity, brain.Owner, ref aiState, settings, now);

                // TACTICAL layer: what each army does once it is in contact.
                // Runs AFTER UpdateMissions, so every mission it sees is live
                // and has already had its dead pruned. Dispatch decides where
                // an army goes; this decides what it fights when it gets
                // there, and keeps it together while it does.
                TickArmyTactics(em, brain.Owner, now);

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
                {
                    Entity home = FindFactionBuilding<HallTag>(em, brain.Owner);
                    if (home != Entity.Null && em.HasComponent<LocalTransform>(home))
                        TickSupport(em, brain.Owner, em.GetComponentData<LocalTransform>(home).Position, now);
                }

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

                // Economy spends whatever advancement did not claim.
                TickEconomy(em, brain.Owner, ref aiState, personality, profile, now);

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
