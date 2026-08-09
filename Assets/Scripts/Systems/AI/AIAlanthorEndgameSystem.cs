// AIAlanthorEndgameSystem.cs
// Culture-specific endgame AI for Alanthor factions. Picks up after the
// SimpleAISystem build order finishes (Age 2+) and drives the late-game
// behaviour Alanthor should ship with: defensive tower clusters, sect
// adoption (Fortitude / Renewal cluster) AND active-power firing,
// veilsteel production via the Smelter (build it once — limit 1 — then
// level it to L3 for 1/2/3 veilsteel per 10 s), armoured unit production
// from the Stable / SiegeYard, worker flee
// from threats, and on-age-up strategy transition to Defensive.
//
// Scope: SELF-SUFFICIENT. The legacy AIBuildingManager / AIEconomyManager /
// AIMilitaryManager are all [DisableAutoCreation] (replaced by
// SimpleAISystem) and their BuildRequest / RecruitmentRequest buffers
// are dead code. So this system bypasses those buffers entirely and
// drives Era-2+ Alanthor behaviour with direct ECS calls — same pattern
// SimpleAISystem uses for Age-1 buildings (CommandRouter.PlaceBuildingDirect
// + DispatchBuildersTo, FactionEconomy.Spend, push directly into
// TrainQueueItem).
//
// Tick rate: 5 seconds (slow loop — strategic decisions, not micro).
//
// Phases (each tick):
//   1. HasAgedUp latch — first frame era >= 2 is observed on the Hall.
//      Also flips AIStrategyState.Current to Defensive after enough
//      armies have been lost since the last strategy switch (preserves
//      previous as Previous so future evaluators can diff).
//   2. Sect adoption — when a Temple of Ridan exists and RP / supplies /
//      veilstone can afford a chapel, queue an adoption via SectAdoption.
//      Picks Alanthor-cluster sects in priority order (Fortitude first).
//   3. Sect active-power firing — for every adopted sect that has a
//      level-1+ Active-Power lever and is off cooldown, fire it at the
//      most useful target: offensive (Smite / Burning / Pyre) at enemy
//      clusters in/near our base; support (Heal / Armor / Damage / Speed)
//      on our own armies in combat; reveal at the last-known enemy
//      position.
//   4. Smelter construction — if no Alanthor_Smelter exists, build one
//      directly (FactionEconomy.Spend + CommandRouter.PlaceBuildingDirect
//      + DispatchBuildersTo). The Forge then generates veilsteel passively
//      (no miner supply chain).
//   6. Defensive tower spam — late-game (>5 min) build extra Alanthor_Towers
//      around the Hall up to a cap. Direct creation (was queueing into
//      the dead BuildRequest buffer; never actually built anything).
//   7. Armoured-unit production — when a Barracks / Alanthor_SiegeYard
//      exists and its TrainQueue has room, push Cataphract / Ballista
//      directly into the queue (charges cost via FactionEconomy.Spend).
//   8. Worker flee — for every miner / builder of this faction with an
//      enemy unit within FleeRadius, issue a MoveCommand toward the
//      nearest own Hall. Cooldowned per-worker so we don't spam orders.
//
// Walls: deferred. Closing a wall ring around the base — required for
// the WallEnclosureIncomeSystem bonus — needs a perimeter pathfinder
// and per-segment WallHubLink wiring that is out of scope for this PR.
// Sect aura/passive effects are applied automatically by their dedicated
// systems once a chapel is adopted; nothing for the AI to do there.
//
// Location: Assets/Scripts/AI/Managers/AIAlanthorEndgameSystem.cs

using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Entities;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.World.Terrain;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SimpleAISystem))]
    public partial struct AIAlanthorEndgameSystem : ISystem
    {
        // Tick interval — slow strategic loop.
        private const float ThinkInterval = 5f;

        // (The flat late-game tower cap + 5-minute gate are gone — towers are
        // governed by the doctrine below: per-difficulty budget, chokepoint /
        // threat-facing placement, anti-clump spacing, active from era 2.)

        // Material cost the AI keeps in reserve before queuing chapel
        // adoption (so adoption doesn't bankrupt the economy).
        private const int ChapelReserveSupplies = 100;
        private const int ChapelReserveVeilstone  = 40;

        // Train-queue cap per Stable / SiegeYard. Mirrors SimpleAISystem.
        private const int MaxTrainQueue = 5;

        // Worker flee tuning. Miners and builders run home if any enemy
        // unit is within FleeRadius. Throttled per worker so we don't
        // spam MoveCommands every tick once a threat is committed.
        private const float FleeRadius = 14f;

        // Strategy switch threshold: number of armies lost without dealing
        // significant damage since the last strategy switch before we flip
        // to Defensive. Cheap signal — armies-lost is bumped by combat
        // bookkeeping elsewhere; we just react to it.
        private const int LossesBeforeDefensiveFlip = 2;

        // Alanthor-cluster sect priority (best-first). IMPLEMENTED sects
        // lead (2026-07-12: the old order started with Fortitude/Antiquity/
        // Reclamation — all rejected by the SectConfig.IsImplemented rollout
        // gate, so the AI adopted at most ONE sect and rarely fired a power).
        // The flavor picks stay at the tail for when their kits land.
        private static readonly string[] AlanthorSectPriority =
        {
            SectConfig.Renewal,      // implemented — auto-repair fits defense
            SectConfig.Justice,      // implemented — support cleanse
            SectConfig.War,          // implemented — smite + elite unit
            SectConfig.Fortitude,    // pending kit
            SectConfig.Antiquity,    // pending kit
            SectConfig.Reclamation,  // pending kit
        };

        // All 12 sects — for the Active-Power firing pass (sect adoption is
        // not strictly Alanthor-cluster: a faction may adopt a non-cluster
        // sect too, and once adopted its Active Power should still fire).
        private static readonly string[] AllSects =
        {
            SectConfig.Antiquity, SectConfig.Renewal,    SectConfig.Fortitude,
            SectConfig.Reclamation, SectConfig.Silence,  SectConfig.Justice,
            SectConfig.Veneration, SectConfig.Witness,   SectConfig.War,
            SectConfig.Ash,        SectConfig.Ruin,      SectConfig.Wrath,
        };

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (GameSettings.IsMultiplayer && !GameSettings.IsHost()) return;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            // Snapshot brain entities first — we make structural changes
            // (creating buildings) that would invalidate a SystemAPI.Query
            // iteration.
            var perfSw = System.Diagnostics.Stopwatch.StartNew();
            int perfThinks = 0;
            var brainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<AIBrain>());
            using var brainEntities = brainQuery.ToEntityArray(Allocator.Temp);
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            for (int b = 0; b < brainEntities.Length; b++)
            {
                var entity = brainEntities[b];
                if (!em.Exists(entity)) continue;
                var brain = em.GetComponentData<AIBrain>(entity);
                if (brain.IsActive == 0) continue;

                Faction faction = brain.Owner;

                // Throttle: 5 s tick. Per-brain tick state, lazy-stamped on first sight.
                if (em.HasComponent<AIAlanthorTickState>(entity))
                {
                    var tick = em.GetComponentData<AIAlanthorTickState>(entity);
                    if (time < tick.NextThinkTime) continue;
                    tick.NextThinkTime = time + ThinkInterval;
                    em.SetComponentData(entity, tick);
                    perfThinks++;
                }
                else
                {
                    // STAGGERED first stamp (2026-08-05): every brain used to
                    // stamp the same NextThinkTime on the same frame, so all
                    // 8 factions' endgame passes landed together every 5 s.
                    // Resets are relative to each brain's own think time, so
                    // this initial offset persists for the whole match.
                    em.AddComponentData(entity, new AIAlanthorTickState
                    {
                        NextThinkTime = time + ThinkInterval
                            + (int)faction * (ThinkInterval / 8f),
                    });
                    continue; // skip first tick after stamp
                }

                // Find this faction's Hall and read culture/era.
                bool hasHall = false;
                byte culture = Cultures.None;
                int era = 1;
                float3 hallPos = float3.zero;
                Entity hallEntity = Entity.Null;
                {
                    var hallQuery = em.CreateEntityQuery(
                        ComponentType.ReadOnly<HallTag>(),
                        ComponentType.ReadOnly<FactionTag>(),
                        ComponentType.ReadOnly<FactionProgress>(),
                        ComponentType.ReadOnly<LocalTransform>());
                    using var hallEnts = hallQuery.ToEntityArray(Allocator.Temp);
                    for (int i = 0; i < hallEnts.Length; i++)
                    {
                        if (em.GetComponentData<FactionTag>(hallEnts[i]).Value != faction) continue;
                        culture  = em.GetComponentData<FactionProgress>(hallEnts[i]).Culture;
                        hallPos  = em.GetComponentData<LocalTransform>(hallEnts[i]).Position;
                        hallEntity = hallEnts[i];
                        hasHall  = true;
                        break;
                    }
                }
                if (!hasHall) continue;
                if (FactionEconomy.TryGetBank(em, faction, out var bank)
                    && em.HasComponent<FactionEra>(bank))
                    era = em.GetComponentData<FactionEra>(bank).Value;

                if (culture != Cultures.Alanthor) continue;
                if (era < 2) continue;

                // ─── 1. HasAgedUp latch + opportunistic strategy flip ─
                if (em.HasComponent<AIStrategyState>(entity))
                {
                    var ss = em.GetComponentData<AIStrategyState>(entity);
                    bool ssDirty = false;
                    if (ss.HasAgedUp == 0)
                    {
                        ss.HasAgedUp = 1;
                        ssDirty = true;
                        AILogger.Log(faction, "STRATEGY",
                            "Alanthor: aged up to era 2+ — endgame system engaged");
                    }
                    // Flip to Defensive if too many armies lost since the last
                    // switch. Cheap signal that doesn't require a full
                    // AIStrategyEvaluator (also [DisableAutoCreation]).
                    if (ss.Current != AIStrategy.Defensive
                        && ss.ArmiesLostSinceSwitch >= LossesBeforeDefensiveFlip)
                    {
                        ss.Previous = ss.Current;
                        ss.Current  = AIStrategy.Defensive;
                        ss.ArmiesLostSinceSwitch = 0;
                        ss.StrategyStartTime = time;
                        ssDirty = true;
                        AILogger.Log(faction, "STRATEGY",
                            $"Alanthor: switching to Defensive after {LossesBeforeDefensiveFlip}+ losses");
                    }
                    if (ssDirty) em.SetComponentData(entity, ss);
                }

                // ─── 2. Sect adoption ─────────────────────────────────
                TryAdoptNextSect(faction, em);

                // ─── 3. Sect active-power firing ──────────────────────
                TryFireSectPowers(faction, em, hallPos);

                // ─── 4. Age-2 building ladder ─────────────────────────
                // Temple FIRST (sect adoption hard-requires a Temple to
                // host chapels — without this ladder no strategy except
                // Turtle ever built one, so sects and their content never
                // appeared), then Smelter (veilsteel), then the military
                // production trio. One attempt per think tick.
                TryBuildAge2Ladder(faction, em, hallPos);

                // ─── 4a. Temple leveling toward max ───────────────────
                // The Holy Scholar (the purify ritualist) trains only at a
                // max-level Temple (2026-08-04 purify flow) — without this
                // ramp the AI could never field one and the well victory
                // stayed out of reach.
                TryLevelTemple(faction, em);

                // ─── 4b. The culture VERB: purify wells ───────────────
                // Curse & Shardroot canon: Alanthor's answer to a Wild well
                // is Purification — income, victory progress (well
                // domination), and the Shardroot if the well is the host.
                TryPurifyWells(faction, em, hallPos);

                // ─── 6. Tower doctrine ────────────────────────────────
                // Towers are BOTH Alanthor's territory claims (each projects
                // a 15 m build-space circle) and its static defense. Placed
                // toward the known threat with chokepoint preference and
                // anti-clump spacing — from era-2 start, budget by
                // difficulty (no more 4-in-a-row ring spam at minute 5).
                TryBuildDefensiveTower(faction, em, entity, brain.Difficulty, hallPos);

                // ─── 7. Armoured-unit production ──────────────────────
                TryQueueArmouredUnits(faction, em);

                // ─── 8. Worker flee ───────────────────────────────────
                HandleWorkerFlee(faction, em, hallPos, time);
            }

            ecb.Playback(em);
            ecb.Dispose();

            perfSw.Stop();
            if (perfThinks > 0)
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                    "AIEndgame", perfSw.Elapsed.TotalMilliseconds, $"brains {perfThinks}");
        }

        // ──────────────────────────────────────────────────────────────────
        // 2. SECT ADOPTION
        // ──────────────────────────────────────────────────────────────────

        private static void TryAdoptNextSect(Faction faction, EntityManager em)
        {
            // Need a Temple of Ridan to host chapels.
            Entity temple = FindFactionBuilding<TempleOfRidanTag>(em, faction);
            if (temple == Entity.Null) return;

            for (int i = 0; i < AlanthorSectPriority.Length; i++)
            {
                string sectId = AlanthorSectPriority[i];
                if (SectQuery.IsAdopted(em, faction, sectId)) continue;

                if (!BuildCosts.TryGet(SectConfig.ChapelIdFor(sectId), out var chapelCost)) continue;

                if (FactionEconomy.TryGetResources(em, faction, out var res))
                {
                    if (res.Supplies < chapelCost.Supplies + ChapelReserveSupplies) return;
                    if (res.Veilstone  < chapelCost.Veilstone  + ChapelReserveVeilstone)  return;
                }
                else return;

                var result = SectAdoption.TryStartAdoption(em, faction, sectId, chapelCost, temple);
                if (result == SectAdoptionResult.Ok)
                {
                    AILogger.Log(faction, "STRATEGY",
                        $"Alanthor: adopting sect {sectId.Substring(5)}");
                    // Replicated slot stamp (audit F7) — host-only writes left
                    // clients without the chapel or the sect bonuses.
                    CommandRouter.IssueSectAdoption(em, temple, sectId, -1, 30f,
                        CommandSource.AI);
                    return; // one adoption per tick
                }
                if (result == SectAdoptionResult.NotEnoughRP) return; // wait for RP
                // For other failure modes (slot full, already adopted), try next priority.
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // 3. SECT ACTIVE-POWER FIRING
        // ──────────────────────────────────────────────────────────────────

        // Fire every adopted sect's Active Power that has a level-1+ lever
        // and an off-cooldown timer. Targeting depends on the power's
        // intent: offensive at enemy clusters near our base, support on
        // our own units (preferring those in combat), reveal at the
        // last-known enemy position.
        private static void TryFireSectPowers(Faction faction, EntityManager em, float3 hallPos)
        {
            for (int i = 0; i < AllSects.Length; i++)
            {
                string sectId = AllSects[i];
                if (!SectActivePowerHelper.CanFire(em, faction, sectId)) continue;

                var spec = SectLeverEffects.ActiveOf(sectId);
                float3 target;
                bool haveTarget;
                switch (spec.Kind)
                {
                    case SectActivePowerKind.SmiteCircle:
                    case SectActivePowerKind.BurningCircle:
                    case SectActivePowerKind.SpawnPyre:
                        haveTarget = TryPickEnemyClusterNearBase(em, faction, hallPos, spec.Radius, out target);
                        break;
                    case SectActivePowerKind.HealCircle:
                    case SectActivePowerKind.ArmorCircle:
                    case SectActivePowerKind.DamageCircle:
                    case SectActivePowerKind.SpeedCircle:
                        haveTarget = TryPickFriendlyArmy(em, faction, hallPos, spec.Radius, out target);
                        break;
                    case SectActivePowerKind.RevealCircle:
                        haveTarget = TryPickRevealTarget(em, faction, hallPos, out target);
                        break;
                    default:
                        continue;
                }
                if (!haveTarget) continue;

                if (SectActivePowerHelper.Fire(em, faction, sectId, target))
                {
                    AILogger.Log(faction, "STRATEGY",
                        $"Alanthor: fired {sectId.Substring(5)} active power");
                }
            }
        }

        // Densest enemy cluster within ~80 m of the Hall. Returns the
        // grid cell with the most enemy units (4 m bucket size). Avoids
        // wasting a 60-100 cooldown power on a single straggler.
        private static bool TryPickEnemyClusterNearBase(
            EntityManager em, Faction faction, float3 hallPos, float castRadius,
            out float3 target)
        {
            target = default;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>());
            using var ents = query.ToEntityArray(Allocator.Temp);

            const float scanRadius = 80f;
            float scanRadiusSq = scanRadius * scanRadius;

            // Snapshot enemy positions within scan radius.
            var enemyPositions = new NativeList<float3>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.GetComponentData<FactionTag>(e).Value == faction) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - hallPos.x, dz = p.z - hallPos.z;
                if (dx * dx + dz * dz > scanRadiusSq) continue;
                enemyPositions.Add(p);
            }

            if (enemyPositions.Length == 0) { enemyPositions.Dispose(); return false; }

            // Pick the densest cluster: for each candidate enemy, count
            // how many other enemies are within castRadius; pick the
            // enemy with the highest count. Ties broken by the first one
            // encountered. O(N²) but N is bounded by units within 80 m.
            float castRadiusSq = castRadius * castRadius;
            int bestCount = 0;
            int bestIdx = -1;
            for (int i = 0; i < enemyPositions.Length; i++)
            {
                int count = 0;
                for (int j = 0; j < enemyPositions.Length; j++)
                {
                    float dx = enemyPositions[j].x - enemyPositions[i].x;
                    float dz = enemyPositions[j].z - enemyPositions[i].z;
                    if (dx * dx + dz * dz <= castRadiusSq) count++;
                }
                if (count > bestCount) { bestCount = count; bestIdx = i; }
            }

            // Need at least 3 units in the cluster to justify a 60-150s cd power.
            if (bestCount < 3) { enemyPositions.Dispose(); return false; }
            target = enemyPositions[bestIdx];
            enemyPositions.Dispose();
            return true;
        }

        // Pick the centroid of our largest army group within ~120 m of the
        // Hall. Bias toward groups that are currently taking damage so the
        // heal/buff actually matters.
        private static bool TryPickFriendlyArmy(
            EntityManager em, Faction faction, float3 hallPos, float castRadius,
            out float3 target)
        {
            target = default;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>());
            using var ents = query.ToEntityArray(Allocator.Temp);

            const float scanRadius = 120f;
            float scanRadiusSq = scanRadius * scanRadius;

            var positions = new NativeList<float3>(Allocator.Temp);
            var damaged   = new NativeList<bool>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                if (em.GetComponentData<FactionTag>(e).Value != faction) continue;
                var hp = em.GetComponentData<Health>(e);
                if (hp.Value <= 0) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - hallPos.x, dz = p.z - hallPos.z;
                if (dx * dx + dz * dz > scanRadiusSq) continue;
                positions.Add(p);
                damaged.Add(hp.Value < hp.Max);
            }

            if (positions.Length == 0) { positions.Dispose(); damaged.Dispose(); return false; }

            // Score each unit by (cluster size in castRadius) + (2× damaged
            // friends in radius), so heals/buffs land where they help most.
            float castRadiusSq = castRadius * castRadius;
            float bestScore = 0f;
            int bestIdx = -1;
            for (int i = 0; i < positions.Length; i++)
            {
                float score = 0f;
                for (int j = 0; j < positions.Length; j++)
                {
                    float dx = positions[j].x - positions[i].x;
                    float dz = positions[j].z - positions[i].z;
                    if (dx * dx + dz * dz > castRadiusSq) continue;
                    score += 1f + (damaged[j] ? 2f : 0f);
                }
                if (score > bestScore) { bestScore = score; bestIdx = i; }
            }

            // Need at least 3 units (or 2 wounded) to justify the cooldown.
            bool worthwhile = bestScore >= 3f;
            float3 best = bestIdx >= 0 ? positions[bestIdx] : default;
            positions.Dispose();
            damaged.Dispose();
            if (!worthwhile) return false;
            target = best;
            return true;
        }

        // Reveal goes to the AISharedKnowledge.EnemyLastKnownPosition if
        // recent; otherwise we skip rather than blow the cooldown blind.
        private static bool TryPickRevealTarget(EntityManager em, Faction faction,
            float3 hallPos, out float3 target)
        {
            target = default;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<AIBrain>(),
                ComponentType.ReadOnly<AISharedKnowledge>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<AIBrain>(ents[i]).Owner != faction) continue;
                var sk = em.GetComponentData<AISharedKnowledge>(ents[i]);
                // Only fire reveal if we've actually seen something — saves the
                // cooldown vs spraying it on the Hall position.
                if (sk.EnemyLastSeenTime <= 0) return false;
                target = sk.EnemyLastKnownPosition;
                return true;
            }
            return false;
        }

        // ──────────────────────────────────────────────────────────────────
        // 4. SMELTER CONSTRUCTION (one-shot)
        // ──────────────────────────────────────────────────────────────────

        // Age-2 build ladder, priority-ordered. Temple leads: sect adoption
        // (chapel plots), Litharch training and the whole religious layer
        // hang off it. Then the veilsteel Smelter, then the military
        // production pair the armoured-unit pass trains from. (The Practice
        // Range is the LEVELED Archery Range now, not a placeable building.)
        private static readonly (string id, float rMin, float rMax)[] Age2Ladder =
        {
            ("TempleOfRidan",          16f, 26f),
            ("Alanthor_Smelter",       18f, 28f),
            ("Alanthor_RoyalStable",   18f, 30f),
            ("Alanthor_SiegeYard",     20f, 32f),
        };

        // ──────────────────────────────────────────────────────────────────
        // 4b. WELL PURIFICATION (the Alanthor verb)
        // ──────────────────────────────────────────────────────────────────

        private static void TryPurifyWells(Faction faction, EntityManager em, float3 hallPos)
        {
            // Find a free Scholar (not already channeling / ordered).
            Entity scholar = Entity.Null;
            bool anyScholar = false;
            {
                var sq = em.CreateEntityQuery(
                    ComponentType.ReadOnly<ScholarTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<LocalTransform>());
                using var sEnts = sq.ToEntityArray(Allocator.Temp);
                using var sFacs = sq.ToComponentDataArray<FactionTag>(Allocator.Temp);
                for (int i = 0; i < sEnts.Length; i++)
                {
                    if (sFacs[i].Value != faction) continue;
                    anyScholar = true;
                    if (em.HasComponent<RitualState>(sEnts[i])) continue;
                    if (em.HasComponent<PurifyCommand>(sEnts[i])) continue;
                    scholar = sEnts[i];
                    break;
                }
            }

            // No Scholar at all → train ONE at the Temple (the ladder builds
            // the Temple; TryQueueAt pre-flights queue space + cost). The
            // in-queue check is what stops the 5-Scholars-in-25-seconds
            // money furnace the 2026-08-04 logs caught — a Scholar takes
            // 68 s to train and every 5 s think tick was buying another.
            if (!anyScholar)
            {
                if (!IsUnitQueued(em, faction, "Alanthor_Scholar"))
                    TryQueueAt<TempleOfRidanTag>(em, faction, "Alanthor_Scholar");
                return;
            }
            if (scholar == Entity.Null) return; // all Scholars busy

            // Nearest claimable well: Active, built, no ritual in progress,
            // and fog-honest (the AI only verbs wells it has revealed).
            var fogMgr = TheWaningBorder.World.FogOfWar.FogOfWarManager.Instance;
            var nq = em.CreateEntityQuery(
                ComponentType.ReadOnly<BorderMainNodeTag>(),
                ComponentType.ReadOnly<BorderNodeState>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var nEnts = nq.ToEntityArray(Allocator.Temp);
            using var nStates = nq.ToComponentDataArray<BorderNodeState>(Allocator.Temp);
            using var nXfs = nq.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            Entity best = Entity.Null;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < nEnts.Length; i++)
            {
                // Active wells AND Destroyed rubble are both purifiable
                // (PurificationRitualSystem only rejects Cleansed/Converted)
                // — consecrating a broken well before it rebuilds is the
                // cheapest hold Alanthor ever gets.
                bool rubble = nStates[i].State == NodeState.Destroyed;
                bool active = nStates[i].State == NodeState.Active
                    && !em.HasComponent<NodeDormant>(nEnts[i]);
                if (!active && !rubble) continue;
                if (em.HasComponent<UnderConstruction>(nEnts[i])) continue;
                if (em.HasComponent<ActiveRitualOnNode>(nEnts[i])) continue;
                var p = nXfs[i].Position;
                if (fogMgr != null && !fogMgr.IsRevealed(faction,
                        new UnityEngine.Vector3(p.x, 0f, p.z))) continue;
                float dx = p.x - hallPos.x, dz = p.z - hallPos.z;
                float d = dx * dx + dz * dz;
                if (d < bestDistSq) { bestDistSq = d; best = nEnts[i]; }
            }
            if (best == Entity.Null) return;

            CommandRouter.IssuePurify(em, scholar, best, CommandSource.AI);
            AILogger.Log(faction, "STRATEGY", "Alanthor: Scholar dispatched to purify a well");

            // ESCORT (2026-07-12): the army is the Scholar's BODYGUARD, not
            // the main force — plain waves at wells only fed the crystal
            // spread. Send up to EscortSize idle military attack-moving to
            // the well so they screen the channel; committed units are never
            // re-drafted (command follow-through).
            float3 wellPos = em.GetComponentData<LocalTransform>(best).Position;
            var eq = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var eEnts = eq.ToEntityArray(Allocator.Temp);
            using var eTags = eq.ToComponentDataArray<UnitTag>(Allocator.Temp);
            using var eFacs = eq.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int sent = 0;
            for (int i = 0; i < eEnts.Length && sent < EscortSize; i++)
            {
                if (eFacs[i].Value != faction) continue;
                var cls = eTags[i].Class;
                if (cls != UnitClass.Melee && cls != UnitClass.Ranged
                    && cls != UnitClass.Siege) continue;
                Entity u = eEnts[i];
                if (em.HasComponent<UnderConstruction>(u)) continue;
                if (em.HasComponent<AttackCommand>(u)) continue;
                if (em.HasComponent<AttackMoveTag>(u)) continue;
                if (em.HasComponent<UserMoveOrder>(u)) continue;
                // STAND OFF — ring, not pile-on. Sending the whole escort to
                // the Scholar's own tile shoves it off the node: a channelling
                // ritualist has DesiredDestination.Has = 0 and SteeringSystem
                // keeps separation at full strength, so the bodyguard ratchets
                // its own charge past RitualCancelRange (10 m) and breaks the
                // 35 s channel. Measured on the Feraldis sibling in the
                // 2026-08-07 8-player match: mean 18.5 s between re-dispatches
                // at escort 12+, versus 123 s once the escort thinned out.
                float ang = (sent / (float)EscortSize) * 2f * math.PI;
                float3 slot = wellPos + new float3(
                    math.cos(ang) * EscortStandoffRadius, 0f,
                    math.sin(ang) * EscortStandoffRadius);
                AttackMoveCommandHelper.Execute(em, u, slot);
                sent++;
            }
            if (sent > 0)
                AILogger.Log(faction, "STRATEGY",
                    $"Alanthor: {sent} escorts sent with the Scholar");
        }

        /// <summary>Bodyguards dispatched alongside a well ritualist.
        /// HEAVY (2026-08-04, was 5): the node births defenders at the
        /// channeling Scholar — a token screen kept losing the ritual.</summary>
        private const int EscortSize = 10;

        /// <summary>Radius (m) of the escort ring around a well. Outside
        /// RitualCancelRange (10 m) so the screen cannot break its own
        /// Scholar's channel — see the note at the dispatch site.</summary>
        private const float EscortStandoffRadius = 14f;

        /// <summary>True while any of this faction's buildings holds the unit
        /// in its train queue — the guard that stops a per-tick re-buy while
        /// the first copy is still training.</summary>
        private static bool IsUnitQueued(EntityManager em, Faction faction, string unitId)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<TrainQueueItem>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var buf = em.GetBuffer<TrainQueueItem>(ents[i]);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].UnitId.ToString() == unitId) return true;
            }
            return false;
        }

        /// <summary>Level the Temple toward max on the Advancement budget —
        /// era progression, sect levers, and (at max) the Holy Scholar all
        /// hang off it. One attempt per think tick; the cost is spent by the
        /// caller (TempleUpgradeCommandDirect never touches the bank).</summary>
        private static void TryLevelTemple(Faction faction, EntityManager em)
        {
            Entity temple = FindFactionBuilding<TempleOfRidanTag>(em, faction);
            if (temple == Entity.Null) return;
            if (em.HasComponent<UnderConstruction>(temple)) return;
            if (!em.HasComponent<TempleLevel>(temple)) return;
            if (em.HasComponent<TempleUpgradeState>(temple)) return;

            int level = em.GetComponentData<TempleLevel>(temple).Level;
            if (level >= TempleLevelConfig.MaxLevel) return;

            var cost = TempleLevelConfig.GetUpgradeCost(level);
            if (!TheWaningBorder.AI.AIBudget.CanSpend(faction,
                    TheWaningBorder.AI.AIBudgetCategory.Advancement, cost)) return;
            if (!FactionEconomy.Spend(em, faction, cost)) return;
            TheWaningBorder.AI.AIBudget.RecordSpend(faction,
                TheWaningBorder.AI.AIBudgetCategory.Advancement, cost);

            CommandRouter.IssueTempleUpgrade(em, temple, CommandSource.AI);
            AILogger.Log(faction, "BUILDING", $"Temple upgrading to L{level + 1}");
        }

        private static void TryBuildAge2Ladder(Faction faction, EntityManager em, float3 hallPos)
        {
            // Veilsteel engine FIRST, independent of ladder progress. This used
            // to run only after the whole ladder stood, which log-provably
            // starved it: one unplaceable ladder entry (rings saturated by
            // gatherer huts) blocked Smelter levels for an entire match while
            // hut upgrades drained every shard of veilsteel the L1 output made.
            TryLevelSmelter(faction, em);

            for (int i = 0; i < Age2Ladder.Length; i++)
            {
                var (id, rMin, rMax) = Age2Ladder[i];
                if (CountFactionBuildings(em, faction, id) > 0) continue;
                TryBuildOnce(faction, em, hallPos, id, rMin, rMax);
                return; // one ladder attempt per think tick, in order
            }
        }

        /// <summary>Level the faction's single Smelter (Forge) toward L3.
        /// UpgradeBuildingCommandHelper does the validation, cost check and
        /// spend; a NotUpgradeable / CannotAfford / AlreadyMaxLevel result
        /// simply means "not this tick".</summary>
        private static void TryLevelSmelter(Faction faction, EntityManager em)
        {
            Entity smelter = FindFactionBuilding<SmelterTag>(em, faction);
            if (smelter == Entity.Null) return;
            if (em.HasComponent<UnderConstruction>(smelter)) return;
            if (em.HasComponent<BuildingUpgrading>(smelter)) return;

            var result = UpgradeBuildingCommandHelper.Execute(em, smelter, CommandSource.AI);
            if (result == UpgradeBuildingResult.Ok)
                AILogger.Log(faction, "BUILDING", "Alanthor: Smelter upgrade queued");
        }

        private static void TryBuildOnce(Faction faction, EntityManager em, float3 hallPos,
            string buildingId, float ringMin, float ringMax)
        {
            if (!BuildCosts.TryGet(buildingId, out var cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;

            // Pre-flight: need an idle builder. Don't spend cost on a foundation
            // nobody will work on.
            if (CountIdleBuilders(em, faction) == 0) return;

            int2 size = BuildingSizeConfig.GetSize(buildingId);
            // The base rings clog up over a long match (gatherer huts tile the
            // ground around the hall). If the authored ring has no slot, retry
            // once at 1.6x the radius rather than silently stalling the ladder
            // forever — an outlying stable beats no stable.
            if (!TryFindBuildPositionRing(em, hallPos, size, ringMin, ringMax, out float3 pos)
                && !TryFindBuildPositionRing(em, hallPos, size, ringMax, ringMax * 1.6f, out pos))
                return;

            if (!FactionEconomy.Spend(em, faction, cost)) return;

            // Replicating entry point (audit F4) — PlaceBuildingDirect was
            // host-only. Queued case: dispatch at the position, null target;
            // builders auto-find the foundation on arrival.
            bool queuedPlacement = CommandRouter.IssuePlaceBuilding(em, buildingId, pos, faction,
                out Entity building, CommandSource.AI);
            if (queuedPlacement)
            {
                DispatchBuildersTo(em, faction, Entity.Null, buildingId, pos, maxBuilders: 2);
                AILogger.Log(faction, "BUILDING", $"Alanthor age-2 ladder: queued {buildingId}");
                return;
            }
            if (building == Entity.Null) { FactionEconomy.Add(em, faction, cost); return; }

            int dispatched = DispatchBuildersTo(em, faction, building, buildingId, pos, maxBuilders: 2);
            if (dispatched == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                return;
            }
            AILogger.Log(faction, "BUILDING", $"Alanthor age-2 ladder: queued {buildingId}");
        }

        // ──────────────────────────────────────────────────────────────────
        // 6. TOWER DOCTRINE (chokepoints + territory claims, anti-clump)
        // ──────────────────────────────────────────────────────────────────

        // Own towers may never stand closer than this — 1.6× the 15 m
        // influence radius, so their build-space circles TILE new ground
        // instead of stacking (the old ring placement produced 4-in-a-row).
        private const float MinTowerSpacing = 24f;
        // A corridor narrower than this along the enemy approach counts as
        // a chokepoint worth fortifying.
        private const float ChokeWidthThreshold = 26f;

        /// <summary>Nearest resource node (veilstone or iron) within
        /// <see cref="UnprotectedNodeRadius"/> of the hall whose ground this
        /// faction's influence does NOT yet cover — the next tower anchor.</summary>
        private const float UnprotectedNodeRadius = 130f;

        private static bool TryFindUnprotectedResourceNode(
            EntityManager em, Faction faction, float3 hallPos, out float3 nodePos)
        {
            nodePos = default;
            if (!TheWaningBorder.Influence.PlayerInfluenceMap.Ready) return false;

            float bestD2 = UnprotectedNodeRadius * UnprotectedNodeRadius;
            bool found = false;

            var veilQ = em.CreateEntityQuery(
                ComponentType.ReadOnly<VeilstoneOutcroppingTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            var ironQ = em.CreateEntityQuery(
                ComponentType.ReadOnly<IronMineTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            for (int q = 0; q < 2; q++)
            {
                using var xfs = (q == 0 ? veilQ : ironQ)
                    .ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < xfs.Length; i++)
                {
                    float dx = xfs[i].Position.x - hallPos.x;
                    float dz = xfs[i].Position.z - hallPos.z;
                    float d2 = dx * dx + dz * dz;
                    if (d2 >= bestD2) continue;
                    // Already covered by our own influence → protected.
                    if (TheWaningBorder.Influence.PlayerInfluenceMap.ChannelStrengthWorld(
                            (int)faction, xfs[i].Position.x, xfs[i].Position.z)
                        >= TheWaningBorder.Core.Config.VeilCrustConstants.InfluenceThreshold)
                        continue;
                    bestD2 = d2;
                    nodePos = xfs[i].Position;
                    found = true;
                }
            }
            return found;
        }

        // Raised 2026-08-04 ("AI must build more towers outside influence"):
        // towers are Alanthor's long territorial arm (45 m influence claim) —
        // they extend curse suppression, corruption immunity, and the
        // Gatherer's Huts' influence-border income bonus across the map.
        private static int TowerBudget(AIDifficulty d) => d switch
        {
            AIDifficulty.Easy => 3,
            AIDifficulty.Normal => 5,
            AIDifficulty.Hard => 8,
            AIDifficulty.Expert => 10,
            _ => 5,
        };

        private static void TryBuildDefensiveTower(Faction faction, EntityManager em,
            Entity brainEntity, AIDifficulty difficulty, float3 hallPos)
        {
            const string towerId = "Alanthor_Tower";
            int existing = CountFactionBuildings(em, faction, towerId);
            if (existing >= TowerBudget(difficulty)) return;

            if (!BuildCosts.TryGet(towerId, out var cost)) return;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;
            if (CountIdleBuilders(em, faction) == 0) return;

            // Own tower positions — the anti-clump constraint.
            var ownTowers = new NativeList<float3>(Allocator.Temp);
            CollectOwnTowerPositions(em, faction, ownTowers);

            // Threat bearing (fog-honest): freshest remembered enemy sighting,
            // base sightings preferred; pre-contact, claim toward map center.
            GetThreatHint(em, brainEntity, out float3 threatHint);

            // RESOURCE ANCHORING (2026-08-04, design: "the most effective way
            // of defeating the curse is to build influence"): an UNPROTECTED
            // resource node — one this faction's influence does not yet cover
            // — outranks the threat bearing. The tower's 45 m influence claim
            // shields the patch from curse growth, mining corruption and the
            // slow curse-influence escalation, and feeds the huts' border
            // bonus. Nearest unprotected node within tower reach wins.
            if (TryFindUnprotectedResourceNode(em, faction, hallPos, out float3 nodePos))
                threatHint = nodePos;

            int2 towerSize = BuildingSizeConfig.GetSize(towerId);
            bool found = TryFindTowerSpot(em, hallPos, threatHint, ownTowers, towerSize, out float3 pos);
            ownTowers.Dispose();
            if (!found) return;

            if (!FactionEconomy.Spend(em, faction, cost)) return;

            // Replicating entry point (audit F4) — PlaceBuildingDirect was
            // host-only. Queued case: dispatch at the position, null target.
            bool queuedPlacement = CommandRouter.IssuePlaceBuilding(em, towerId, pos, faction,
                out Entity building, CommandSource.AI);
            if (queuedPlacement)
            {
                DispatchBuildersTo(em, faction, Entity.Null, towerId, pos, maxBuilders: 1);
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor towers: {existing + 1}/{TowerBudget(difficulty)} toward " +
                    $"({threatHint.x:F0},{threatHint.z:F0})");
                return;
            }
            if (building == Entity.Null) { FactionEconomy.Add(em, faction, cost); return; }

            int dispatched = DispatchBuildersTo(em, faction, building, towerId, pos, maxBuilders: 1);
            if (dispatched == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                return;
            }
            AILogger.Log(faction, "BUILDING",
                $"Alanthor towers: {existing + 1}/{TowerBudget(difficulty)} toward " +
                $"({threatHint.x:F0},{threatHint.z:F0})");
        }

        private static void CollectOwnTowerPositions(EntityManager em, Faction faction,
            NativeList<float3> into)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<WatchTowerTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) into.Add(xfs[i].Position);
        }

        // Freshest remembered enemy sighting (base categories strongly
        // preferred). Fog-honest — the buffer only holds what this faction
        // has actually seen. Pre-contact fallback: map center (forward
        // territory claim, no intel assumed).
        private static void GetThreatHint(EntityManager em, Entity brainEntity, out float3 hint)
        {
            if (em.HasBuffer<EnemySightingRecord>(brainEntity))
            {
                var buf = em.GetBuffer<EnemySightingRecord>(brainEntity);
                float best = float.MinValue;
                bool found = false;
                float3 bestPos = default;
                for (int i = 0; i < buf.Length; i++)
                {
                    var rec = buf[i];
                    bool baseCat = rec.Category == IntelCategory.Hall
                        || rec.Category == IntelCategory.MilitaryBuilding;
                    float score = rec.LastSeenTime + (baseCat ? 100000f : 0f);
                    if (score > best) { best = score; bestPos = rec.Position; found = true; }
                }
                if (found) { hint = bestPos; return; }
            }
            TheWaningBorder.World.Terrain.TerrainUtility.GetPlayableBounds(out var mn, out var mx);
            hint = new float3((mn.x + mx.x) * 0.5f, 0f, (mn.y + mx.y) * 0.5f);
        }

        /// <summary>
        /// Tower spot selection, in preference order:
        ///   1. CHOKEPOINT — walk the straight approach line from the Hall
        ///      toward the threat; measure corridor width at each step by
        ///      perpendicular passability probes on the nav grid; flank the
        ///      narrowest sub-threshold corridor on its clearer side.
        ///   2. DIRECTED RING — deterministic angles within ±60° of the
        ///      threat bearing at 25–40 m ("facing the enemy").
        /// All candidates respect MinTowerSpacing + placement validity.
        /// </summary>
        private static bool TryFindTowerSpot(EntityManager em, float3 hallPos, float3 threatHint,
            NativeList<float3> ownTowers, int2 towerSize, out float3 spot)
        {
            spot = default;
            float3 dir = threatHint - hallPos;
            dir.y = 0f;
            float len = math.length(dir);
            if (len < 20f) return false; // threat on top of us — no bearing
            dir /= len;
            float3 perp = new float3(-dir.z, 0f, dir.x);

            // ── 1. Chokepoint scan along the approach. ──
            float maxWalk = math.min(len - 10f, 90f);
            float bestWidth = ChokeWidthThreshold;
            float3 chokePos = default, chokeSide = default;
            bool choke = false;
            for (float d = 14f; d <= maxWalk; d += 4f)
            {
                float3 p = hallPos + dir * d;
                float left = ClearanceAlong(p, perp, 14f);
                float right = ClearanceAlong(p, -perp, 14f);
                if (left + right <= 2f) continue; // solid wall, not a corridor
                float width = left + right;
                if (width < bestWidth)
                {
                    bestWidth = width;
                    chokePos = p;
                    chokeSide = left >= right ? perp : -perp;
                    choke = true;
                }
            }
            if (choke)
            {
                for (float off = 4f; off <= 10f; off += 3f)
                {
                    float3 c = chokePos + chokeSide * off;
                    c.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(c.x, c.z);
                    if (IsTowerSpotOk(em, c, towerSize, ownTowers)) { spot = c; return true; }
                }
            }

            // ── 2. Directed ring toward the threat. ──
            // Angle order: straight at it, then ±30°, then ±60°.
            float[] angles = { 0f, 0.5236f, -0.5236f, 1.0472f, -1.0472f };
            for (float r = 25f; r <= 40f; r += 5f)
            {
                for (int a = 0; a < angles.Length; a++)
                {
                    float cos = math.cos(angles[a]);
                    float sin = math.sin(angles[a]);
                    float3 rd = dir * cos + perp * sin;
                    float3 c = hallPos + rd * r;
                    c.y = TheWaningBorder.World.Terrain.TerrainUtility.GetHeight(c.x, c.z);
                    if (IsTowerSpotOk(em, c, towerSize, ownTowers)) { spot = c; return true; }
                }
            }
            return false;
        }

        // Walkable meters from `from` along `stepDir` before hitting an
        // impassable nav cell (max capped). Integer-grid deterministic.
        private static float ClearanceAlong(float3 from, float3 stepDir, float max)
        {
            for (float s = 1f; s <= max; s += 1f)
            {
                float3 p = from + stepDir * s;
                var cell = TheWaningBorder.Systems.Navigation.NavGridQuery.WorldToCellInt2(p);
                if (cell.x == int.MinValue) return s - 1f;
                if (!TheWaningBorder.Systems.Navigation.NavGridQuery.IsCellPassable(cell)) return s - 1f;
            }
            return max;
        }

        private static bool IsTowerSpotOk(EntityManager em, float3 pos, int2 size,
            NativeList<float3> ownTowers)
        {
            for (int i = 0; i < ownTowers.Length; i++)
            {
                float dx = pos.x - ownTowers[i].x;
                float dz = pos.z - ownTowers[i].z;
                if (dx * dx + dz * dz < MinTowerSpacing * MinTowerSpacing) return false;
            }
            return BuildCommandHelper.IsValidBuildPosition(em, pos, size);
        }

        // ──────────────────────────────────────────────────────────────────
        // 7. ARMOURED-UNIT PRODUCTION
        // ──────────────────────────────────────────────────────────────────

        // Push Cataphract / Ballista directly into the Barracks / SiegeYard
        // TrainQueue. Same pattern SimpleAISystem uses for Age-1 units.
        // Charges cost via FactionEconomy.Spend so we don't double-deduct.
        private static void TryQueueArmouredUnits(Faction faction, EntityManager em)
        {
            TryQueueAt<BarracksTag> (em, faction, "Alanthor_Cataphract");
            TryQueueAt<SiegeYardTag>(em, faction, "Alanthor_Catapult");
        }

        private static void TryQueueAt<TBuildingTag>(EntityManager em, Faction faction, string unitId)
            where TBuildingTag : unmanaged, IComponentData
        {
            Entity trainer = FindFactionBuilding<TBuildingTag>(em, faction);
            if (trainer == Entity.Null) return;
            if (em.HasComponent<UnderConstruction>(trainer)) return;
            if (!em.HasBuffer<TrainQueueItem>(trainer)) return;
            if (em.GetBuffer<TrainQueueItem>(trainer).Length >= MaxTrainQueue) return;

            if (!TechCatalog.IsReady) return;
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return;

            // Level gate BEFORE spending — IssueTrain drops silently for AI
            // sources, which would leak the cost.
            if (!CommandRouter.CanTrainAtBuilding(em, trainer, unitId, out _, out _)) return;

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return;
            if (!FactionEconomy.Spend(em, faction, cost)) return;

            // Through CommandRouter (CommandSource.AI) so host-AI training
            // replicates — a direct queue.Add spawned units on the host only.
            CommandRouter.IssueTrain(em, trainer, unitId, CommandSource.AI);
            AILogger.Log(faction, "MILITARY", $"Alanthor: queued {unitId}");
        }

        // ──────────────────────────────────────────────────────────────────
        // 8. WORKER FLEE
        // ──────────────────────────────────────────────────────────────────

        // For every miner / builder of this faction, scan for an enemy unit
        // within FleeRadius and — if found — issue a MoveCommand toward
        // the Hall. Throttled per-worker via FleeCooldownState so we don't
        // override a fresh order on the same tick.
        private static void HandleWorkerFlee(Faction faction, EntityManager em,
            float3 hallPos, float time)
        {
            // Collect enemy unit positions once per tick.
            var enemyPositions = new NativeList<float3>(Allocator.Temp);
            {
                var enemyQuery = em.CreateEntityQuery(
                    ComponentType.ReadOnly<UnitTag>(),
                    ComponentType.ReadOnly<LocalTransform>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<Health>());
                using var eEnts = enemyQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < eEnts.Length; i++)
                {
                    if (em.GetComponentData<FactionTag>(eEnts[i]).Value == faction) continue;
                    if (em.GetComponentData<Health>(eEnts[i]).Value <= 0) continue;
                    enemyPositions.Add(em.GetComponentData<LocalTransform>(eEnts[i]).Position);
                }
            }
            if (enemyPositions.Length == 0) { enemyPositions.Dispose(); return; }

            float fleeRadiusSq = FleeRadius * FleeRadius;

            // Process miners.
            FleeWorkers<MinerTag>(em, faction, enemyPositions, hallPos, fleeRadiusSq, time);
            // Process builders (CanBuild marker is what SimpleAISystem queries).
            FleeWorkers<CanBuild>(em, faction, enemyPositions, hallPos, fleeRadiusSq, time);

            enemyPositions.Dispose();
        }

        private static void FleeWorkers<TWorkerTag>(EntityManager em, Faction faction,
            NativeList<float3> enemyPositions, float3 hallPos, float fleeRadiusSq, float time)
            where TWorkerTag : unmanaged, IComponentData
        {
            var workerQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<TWorkerTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var wEnts = workerQuery.ToEntityArray(Allocator.Temp);

            for (int w = 0; w < wEnts.Length; w++)
            {
                var worker = wEnts[w];
                if (em.GetComponentData<FactionTag>(worker).Value != faction) continue;

                // Only flee once actually HURT. Proximity-fleeing made
                // sneak-mining the well crystal fields impossible — workers
                // oscillated between their gather order and the flee order
                // ("walking away from their destination") and never mined.
                // Canon (§2.1): mining under threat is intended; the worker
                // runs when the curse actually bites.
                if (em.HasComponent<Health>(worker))
                {
                    var whp = em.GetComponentData<Health>(worker);
                    if (whp.Max <= 0 || whp.Value >= (int)(whp.Max * 0.8f)) continue;
                }

                float3 wPos = em.GetComponentData<LocalTransform>(worker).Position;

                // Closest enemy in flee radius?
                bool threatNearby = false;
                for (int e = 0; e < enemyPositions.Length; e++)
                {
                    float dx = enemyPositions[e].x - wPos.x;
                    float dz = enemyPositions[e].z - wPos.z;
                    if (dx * dx + dz * dz <= fleeRadiusSq) { threatNearby = true; break; }
                }
                if (!threatNearby) continue;

                // Cooldown: don't re-issue inside FleeReissueInterval seconds.
                const float FleeReissueInterval = 4f;
                if (em.HasComponent<AIWorkerFleeState>(worker))
                {
                    var fs = em.GetComponentData<AIWorkerFleeState>(worker);
                    if (time < fs.NextRetryTime) continue;
                    fs.NextRetryTime = time + FleeReissueInterval;
                    em.SetComponentData(worker, fs);
                }
                else
                {
                    em.AddComponentData(worker, new AIWorkerFleeState
                    {
                        NextRetryTime = time + FleeReissueInterval,
                    });
                }

                // Drop any active gather/build order so the move sticks.
                if (em.HasComponent<MinerState>(worker))
                {
                    var ms = em.GetComponentData<MinerState>(worker);
                    ms.State            = MinerWorkState.Idle;
                    ms.AssignedDeposit  = Entity.Null;
                    em.SetComponentData(worker, ms);
                }
                if (em.HasComponent<BuildOrder>(worker))
                    em.RemoveComponent<BuildOrder>(worker);

                // Move toward Hall, biased a couple metres past so the
                // worker doesn't stop right at the threat boundary.
                float3 to = hallPos;
                float3 away = to - wPos;
                float len = math.length(new float2(away.x, away.z));
                if (len > 0.01f)
                {
                    away = math.normalize(new float3(away.x, 0f, away.z));
                    to = wPos + away * (len + 4f);
                }
                MoveCommandHelper.Execute(em, worker, to);
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // BUILDER / PLACEMENT HELPERS (mirrors SimpleAISystem private helpers)
        // ──────────────────────────────────────────────────────────────────

        private static int CountIdleBuilders(EntityManager em, Faction faction)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<BuildOrder>(ents[i])) continue;
                count++;
            }
            return count;
        }

        private static int DispatchBuildersTo(EntityManager em, Faction faction,
            Entity site, string buildingId, float3 sitePos, int maxBuilders)
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<CanBuild>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var ents = query.ToEntityArray(Allocator.Temp);

            // Sort by distance ascending — pick the nearest few.
            var candidates = new NativeList<Candidate>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<BuildOrder>(ents[i])) continue;
                float3 p = em.GetComponentData<LocalTransform>(ents[i]).Position;
                float dx = p.x - sitePos.x, dz = p.z - sitePos.z;
                candidates.Add(new Candidate { Entity = ents[i], DistSq = dx * dx + dz * dz });
            }

            // Insertion sort — list is short, no need for a comparer setup.
            for (int i = 1; i < candidates.Length; i++)
            {
                var key = candidates[i];
                int j = i - 1;
                while (j >= 0 && candidates[j].DistSq > key.DistSq)
                {
                    candidates[j + 1] = candidates[j];
                    j--;
                }
                candidates[j + 1] = key;
            }

            int dispatched = 0;
            for (int i = 0; i < candidates.Length && dispatched < maxBuilders; i++)
            {
                // CommandSource.AI, not the LocalPlayer default. (audit F20)
                CommandRouter.IssueBuild(em, candidates[i].Entity, site, buildingId, sitePos,
                    CommandSource.AI);
                dispatched++;
            }
            candidates.Dispose();
            return dispatched;
        }

        private struct Candidate
        {
            public Entity Entity;
            public float DistSq;
        }

        // Simple ring-scan placement: try angles around the anchor at radii
        // within [rmin, rmax]. Returns the first candidate that
        // BuildCommandHelper.IsValidBuildPosition accepts. Used for endgame
        // buildings (Smelter, towers) where SimpleAISystem's GH-spacing
        // and sand-spacing rules don't matter.
        private static bool TryFindBuildPositionRing(EntityManager em, float3 anchor,
            int2 buildingSize, float rmin, float rmax, out float3 pos)
        {
            const int angleSamples = 24;
            // Anchor-derived seed: stable per-Hall but varies between calls
            // because rmin/rmax produce different hashes for tower vs smelter.
            uint seed = math.hash(new float3(anchor.x, rmin, rmax));
            if (seed == 0) seed = 1u;
            var rng = new Unity.Mathematics.Random(seed);
            for (float r = rmin; r <= rmax; r += 4f)
            {
                int start = rng.NextInt(0, angleSamples);
                for (int i = 0; i < angleSamples; i++)
                {
                    int idx = (start + i) % angleSamples;
                    float angle = (idx / (float)angleSamples) * math.PI * 2f;
                    float3 candidate = new float3(
                        anchor.x + math.cos(angle) * r,
                        0f,
                        anchor.z + math.sin(angle) * r);
                    candidate.y = TerrainUtility.GetHeight(candidate.x, candidate.z);

                    if (BuildCommandHelper.IsValidBuildPosition(em, candidate, buildingSize))
                    {
                        pos = candidate;
                        return true;
                    }
                }
            }
            pos = default;
            return false;
        }

        // ──────────────────────────────────────────────────────────────────
        // GENERIC HELPERS
        // ──────────────────────────────────────────────────────────────────

        private static int CountFactionBuildings(EntityManager em, Faction faction, string buildingId)
        {
            int pid = BuildingFactory.GetPresentationId(buildingId);
            int count = 0;
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<PresentationId>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.GetComponentData<PresentationId>(ents[i]).Id != pid) continue;
                count++;
            }
            return count;
        }

        private static Entity FindFactionBuilding<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value == faction) return ents[i];
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
    }

    /// <summary>
    /// Per-AIBrain tick state for the Alanthor endgame loop. Lazy-stamped
    /// the first time AIAlanthorEndgameSystem inspects a brain.
    /// </summary>
    public struct AIAlanthorTickState : IComponentData
    {
        public float NextThinkTime;
    }

    /// <summary>
    /// Per-worker (miner / builder) flee throttle. Stamped by
    /// AIAlanthorEndgameSystem.HandleWorkerFlee on first detection of a
    /// nearby threat; prevents the system from re-issuing MoveCommand
    /// every tick while the worker is already running home.
    /// </summary>
    public struct AIWorkerFleeState : IComponentData
    {
        public float NextRetryTime;
    }
}
