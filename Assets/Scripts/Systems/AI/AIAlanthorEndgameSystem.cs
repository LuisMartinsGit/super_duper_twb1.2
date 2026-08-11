// AIAlanthorEndgameSystem.cs
// Culture-specific endgame AI for Alanthor factions. Picks up after the
// SimpleAISystem build order finishes (Age 2+) and drives the late-game
// behaviour Alanthor should ship with: defensive tower clusters (with a
// Gatherer's Hut coverage pass), wall hubs at chokepoints (or a base
// ring), sect adoption (Fortitude / Renewal cluster) AND active-power
// firing, veilsteel production via the Smelter fleet (built toward the
// 5-cap, every one levelled to L3 for 1/2/3 veilsteel per 10 s each),
// housing toward 8 Houses, armoured unit production from the Stable /
// SiegeYard, worker flee from threats, and on-age-up strategy
// transition to Defensive.
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
//   4. Age-2 ladder + expansion — Temple / first Smelter / Stable /
//      SiegeYard in order (FactionEconomy.Spend + CommandRouter
//      placement + DispatchBuildersTo); every Smelter is levelled
//      lowest-first. Once the ladder stands: more Smelters toward the
//      5-cap and Huts toward 8 Houses, one foundation per tick. The
//      Forges generate veilsteel passively (no miner supply chain).
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
// Walls: the wall doctrine (phase 6b) executes a frozen AIWallPlanner
// plan — terrain-sheltered bases seal their ingress corridors wall-to-
// wall (gate in the middle, towers on the ends), open bases enclose the
// building cluster in a large square-ish perimeter with a gate facing
// each cardinal direction and towers on corners and gate shoulders.
// Segments are created explicitly since WallAutoSegmentSystem is
// [DisableAutoCreation]; gate conversion rides IssueConvertSegmentToGate
// and tower conversion mirrors the player's per-instance path.
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

        // Sect adoption priority (best-first for a defensive economic
        // culture). ALL 12 sects are adoptable since 2026-08-11 — the Temple
        // caps at 6 chapels and RP is finite, so this order IS the strategy:
        // the home cluster's defense/eco kits lead, high-value cross-cluster
        // powers follow, pure aggression flavor sits at the tail.
        private static readonly string[] AlanthorSectPriority =
        {
            SectConfig.Renewal,      // heal circle + hp lever — defense core
            SectConfig.Fortitude,    // armor circle + melee armor — the wall behind the wall
            SectConfig.Justice,      // reveal + global damage lever
            SectConfig.Antiquity,    // Lorekeeper + Reliquary intel hub
            SectConfig.Reclamation,  // miner armor + heal — the economy insurance
            SectConfig.Veneration,   // damage circle on the garrison
            SectConfig.War,          // speed surge + Warbreaker shock elite
            SectConfig.Witness,      // wide reveal — scout redundancy
            SectConfig.Silence,      // ranged damage lever
            SectConfig.Ash,          // burning ground
            SectConfig.Ruin,         // smite + siege lever
            SectConfig.Wrath,        // pyre — pure aggression flavor
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
                // production trio. One attempt per think tick. Returns true
                // while a ladder entry is still missing so the expansion
                // passes below wait for the core to stand.
                bool ladderBusy = TryBuildAge2Ladder(faction, em, hallPos);

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

                // Pivotal savings hold (AIPivotalReserve): while the faction
                // saves toward a Temple level / King's Court unique, the
                // discretionary passes below skip their spends. The verbs
                // (temple, purify, sects) and worker flee always run.
                bool saving = AIPivotalReserve.ShouldHold(em, faction);

                // ─── 4c/4d. Expansion targets ─────────────────────────
                // Once the Age-2 core stands: Smelters toward the 5-cap
                // (one foundation at a time), then Huts toward 8 Houses.
                // One foundation per think tick across the two passes.
                if (!saving && !ladderBusy && !TryExpandSmelters(faction, em, hallPos))
                    TryBuildHouses(faction, em, hallPos);

                // ─── 6. Tower doctrine ────────────────────────────────
                // Towers are BOTH Alanthor's territory claims (each projects
                // a 15 m build-space circle) and its static defense. Placed
                // toward the known threat with chokepoint preference and
                // anti-clump spacing — from era-2 start, budget by
                // difficulty (no more 4-in-a-row ring spam at minute 5).
                if (!saving)
                    TryBuildDefensiveTower(faction, em, entity, brain.Difficulty, hallPos);

                // ─── 6b. Wall doctrine ────────────────────────────────
                // Terrain-aware plan execution — endgame only (the ladder
                // keeps priority on the bank while it is building).
                if (!saving && !ladderBusy)
                    TryBuildWallDefenses(faction, em, entity, hallPos);

                // ─── 7. Armoured-unit production ──────────────────────
                if (!saving)
                    TryQueueArmouredUnits(faction, em);

                // ─── 7b. Sect chapel units ────────────────────────────
                // Every adopted sect's chapel keeps its unique unit in
                // play (docs/Design/Sect_Units.md; cap 2 per sect).
                if (!saving)
                    TryTrainSectUnits(faction, em);

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
                    // Recall the Codex (Antiquity): freezing enemy cooldowns
                    // is an offensive cast — same cluster targeting.
                    case SectActivePowerKind.FreezeCooldowns:
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

            // Snapshot enemy positions within scan radius. PLAYER enemies
            // only (2026-08-11): offensive powers were burning their 60-150 s
            // cooldowns on Border creature clusters at the wells — curse
            // critters respawn from the node, so the smite bought nothing.
            var enemyPositions = new NativeList<float3>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var fac = em.GetComponentData<FactionTag>(e).Value;
                if (fac == faction || fac == Faction.Border) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - hallPos.x, dz = p.z - hallPos.z;
                if (dx * dx + dz * dz > scanRadiusSq) continue;
                enemyPositions.Add(p);
            }

            if (enemyPositions.Length == 0)
            {
                enemyPositions.Dispose();
                return TryPickEnemyBuildingNearBase(em, faction, hallPos, out target);
            }

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

            // Need at least 3 units in the cluster to justify a 60-150s cd
            // power — with no such cluster, fall back to the nearest enemy
            // PLAYER building (smite damages structures since 2026-08-11).
            if (bestCount < 3)
            {
                enemyPositions.Dispose();
                return TryPickEnemyBuildingNearBase(em, faction, hallPos, out target);
            }
            target = enemyPositions[bestIdx];
            enemyPositions.Dispose();
            return true;
        }

        /// <summary>Nearest enemy PLAYER building within the base scan
        /// radius — the offensive-power fallback target. Walls are skipped
        /// (siege-only per Combat_Pacing.md, smite cannot hurt them) and so
        /// is anything Border-owned (wells are verb objectives).</summary>
        private static bool TryPickEnemyBuildingNearBase(
            EntityManager em, Faction faction, float3 hallPos, out float3 target)
        {
            target = default;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingTag>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>());
            using var ents = q.ToEntityArray(Allocator.Temp);

            const float scanRadius = 80f;
            float bestD2 = scanRadius * scanRadius;
            bool found = false;
            for (int i = 0; i < ents.Length; i++)
            {
                var e = ents[i];
                var fac = em.GetComponentData<FactionTag>(e).Value;
                if (fac == faction || fac == Faction.Border) continue;
                if (em.GetComponentData<Health>(e).Value <= 0) continue;
                if (em.HasComponent<WallTag>(e)) continue;
                if (em.HasComponent<UnderConstruction>(e)) continue;
                float3 p = em.GetComponentData<LocalTransform>(e).Position;
                float dx = p.x - hallPos.x, dz = p.z - hallPos.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < bestD2) { bestD2 = d2; target = p; found = true; }
            }
            return found;
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
        // 4. AGE-2 BUILDING LADDER + SMELTER LEVELLING
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

        /// <summary>Ticks the Temple ladder has been bank-blocked, per
        /// faction — drives the throttled stall log below.</summary>
        private static readonly System.Collections.Generic.Dictionary<Faction, int> _templeBlockTicks
            = new System.Collections.Generic.Dictionary<Faction, int>();

        /// <summary>Level the Temple toward max — era progression, sect
        /// levers, and (at L3) the Holy Scholar all hang off it, and the
        /// Scholar is the faction's well verb, i.e. the victory path.
        /// Deliberately NOT budget-windowed (2026-08-11): the 500-1200
        /// supply single spends starved inside the Advancement window's
        /// weighted share — one L2 upgrade happened across four AIs in a
        /// 35-minute match, so no Temple ever hit L3, no Scholar ever
        /// trained, and no ritual was EVER attempted. Bank-affordability
        /// still gates. One attempt per think tick.</summary>
        private static void TryLevelTemple(Faction faction, EntityManager em)
        {
            Entity temple = FindFactionBuilding<TempleOfRidanTag>(em, faction);
            if (temple == Entity.Null
                || !em.HasComponent<TempleLevel>(temple)
                || em.HasComponent<UnderConstruction>(temple)
                || em.HasComponent<TempleUpgradeState>(temple)
                || em.GetComponentData<TempleLevel>(temple).Level >= TempleLevelConfig.MaxLevel)
            {
                // No fundable goal right now — never hold the economy for it.
                AIPivotalReserve.Clear(faction, "Temple");
                return;
            }

            int level = em.GetComponentData<TempleLevel>(temple).Level;
            var cost = TempleLevelConfig.GetUpgradeCost(level);
            if (!FactionEconomy.Spend(em, faction, cost))
            {
                // Bank short — RESERVE the cost so discretionary spending
                // holds until the lump sum forms (2026-08-11: supplies were
                // consumed the tick they arrived, so 500s never accumulated
                // and the Temple sat at L1 for the whole match), and surface
                // the stall about once a minute.
                AIPivotalReserve.Set(faction, "Temple", cost);
                _templeBlockTicks.TryGetValue(faction, out int ticks);
                if (++ticks >= 12)
                {
                    ticks = 0;
                    AILogger.Log(faction, "BUILDING",
                        $"Temple L{level + 1} blocked ~1 min (bank short: " +
                        $"{cost.Supplies}s {cost.Iron}i {cost.Veilstone}v)");
                }
                _templeBlockTicks[faction] = ticks;
                return;
            }
            _templeBlockTicks.Remove(faction);
            AIPivotalReserve.Clear(faction, "Temple");

            CommandRouter.IssueTempleUpgrade(em, temple, CommandSource.AI);
            AILogger.Log(faction, "BUILDING", $"Temple upgrading to L{level + 1}");
        }

        /// <summary>Returns true while a ladder entry is still missing (an
        /// attempt was made this tick or is pending) — the expansion passes
        /// key off this so the core always outranks them.</summary>
        private static bool TryBuildAge2Ladder(Faction faction, EntityManager em, float3 hallPos)
        {
            // Veilsteel engine FIRST, independent of ladder progress. This used
            // to run only after the whole ladder stood, which log-provably
            // starved it: one unplaceable ladder entry (rings saturated by
            // gatherer huts) blocked Smelter levels for an entire match while
            // hut upgrades drained every shard of veilsteel the L1 output made.
            TryLevelSmelters(faction, em);

            for (int i = 0; i < Age2Ladder.Length; i++)
            {
                var (id, rMin, rMax) = Age2Ladder[i];
                if (CountFactionBuildings(em, faction, id) > 0) continue;
                TryBuildOnce(faction, em, hallPos, id, rMin, rMax);
                return true; // one ladder attempt per think tick, in order
            }
            return false; // ladder complete — expansion passes may run
        }

        /// <summary>Level EVERY Smelter (Forge) the faction owns toward L3,
        /// lowest level first, one upgrade attempt per think tick. The old
        /// pass took whichever Smelter the query returned first — with the
        /// build cap at 5 that left the rest of the fleet stuck at L1.
        /// UpgradeBuildingCommandHelper does the validation, cost check and
        /// spend; a NotUpgradeable / CannotAfford / AlreadyMaxLevel result
        /// simply means "not this tick".</summary>
        private static void TryLevelSmelters(Faction faction, EntityManager em)
        {
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<SmelterTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            Entity best = Entity.Null;
            int bestLevel = int.MaxValue;
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.HasComponent<UnderConstruction>(ents[i])) continue;
                if (em.HasComponent<BuildingUpgrading>(ents[i])) continue;
                int lvl = em.HasComponent<BuildingUpgradeState>(ents[i])
                    ? em.GetComponentData<BuildingUpgradeState>(ents[i]).Level : 0;
                if (lvl < bestLevel) { bestLevel = lvl; best = ents[i]; }
            }
            if (best == Entity.Null) return;

            var result = UpgradeBuildingCommandHelper.Execute(em, best, CommandSource.AI);
            if (result == UpgradeBuildingResult.Ok)
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor: Smelter upgrade queued (L{bestLevel} -> L{bestLevel + 1})");
        }

        // ──────────────────────────────────────────────────────────────────
        // 4c/4d. EXPANSION TARGETS (endgame completeness)
        // ──────────────────────────────────────────────────────────────────

        /// <summary>Endgame Smelter fleet target — matches
        /// CommandRouter.MaxSmeltersPerFaction. Five L3 Forges = 15
        /// veilsteel / 10 s, the ceiling of the veilsteel economy.</summary>
        private const int SmelterTarget = 5;

        /// <summary>Endgame housing target: 8 Huts. They auto-level to House
        /// L1 under culture (BuildingCultureAutoLevelSystem); the
        /// AIBuildingUpgradeSystem rotation takes them on to L3.</summary>
        private const int HouseTarget = 8;

        /// <summary>Build Smelters toward the cap, one at a time (never a
        /// second foundation while one is under construction). Returns true
        /// when a foundation was placed or queued this tick.</summary>
        private static bool TryExpandSmelters(Faction faction, EntityManager em, float3 hallPos)
        {
            if (CountFactionBuildingsByTag<SmelterTag>(em, faction) >= SmelterTarget) return false;
            if (AnyFactionBuildingUnderConstruction<SmelterTag>(em, faction)) return false;
            return TryBuildOnce(faction, em, hallPos, "Alanthor_Smelter", 18f, 28f);
        }

        /// <summary>Build Huts toward the housing target, one at a time.
        /// Returns true when a foundation was placed or queued this tick.</summary>
        private static bool TryBuildHouses(Faction faction, EntityManager em, float3 hallPos)
        {
            if (CountFactionBuildingsByTag<HutTag>(em, faction) >= HouseTarget) return false;
            if (AnyFactionBuildingUnderConstruction<HutTag>(em, faction)) return false;
            return TryBuildOnce(faction, em, hallPos, "Hut", 12f, 28f);
        }

        /// <summary>Returns true when the foundation was placed (or queued
        /// for lockstep) this tick — false on any pre-flight or placement
        /// failure (the cost is refunded on the rollback paths).</summary>
        private static bool TryBuildOnce(Faction faction, EntityManager em, float3 hallPos,
            string buildingId, float ringMin, float ringMax)
        {
            if (!BuildCosts.TryGet(buildingId, out var cost)) return false;
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;

            // Pre-flight: need an idle builder. Don't spend cost on a foundation
            // nobody will work on.
            if (CountIdleBuilders(em, faction) == 0) return false;

            int2 size = BuildingSizeConfig.GetSize(buildingId);
            // The base rings clog up over a long match (gatherer huts tile the
            // ground around the hall). If the authored ring has no slot, retry
            // once at 1.6x the radius rather than silently stalling the ladder
            // forever — an outlying stable beats no stable.
            if (!TryFindBuildPositionRing(em, hallPos, size, ringMin, ringMax, out float3 pos)
                && !TryFindBuildPositionRing(em, hallPos, size, ringMax, ringMax * 1.6f, out pos))
                return false;

            if (!FactionEconomy.Spend(em, faction, cost)) return false;

            // Replicating entry point (audit F4) — PlaceBuildingDirect was
            // host-only. Queued case: dispatch at the position, null target;
            // builders auto-find the foundation on arrival.
            bool queuedPlacement = CommandRouter.IssuePlaceBuilding(em, buildingId, pos, faction,
                out Entity building, CommandSource.AI);
            if (queuedPlacement)
            {
                DispatchBuildersTo(em, faction, Entity.Null, buildingId, pos, maxBuilders: 2);
                AILogger.Log(faction, "BUILDING", $"Alanthor age-2 ladder: queued {buildingId}");
                return true;
            }
            if (building == Entity.Null) { FactionEconomy.Add(em, faction, cost); return false; }

            int dispatched = DispatchBuildersTo(em, faction, building, buildingId, pos, maxBuilders: 2);
            if (dispatched == 0)
            {
                FactionEconomy.Add(em, faction, cost);
                em.DestroyEntity(building);
                return false;
            }
            AILogger.Log(faction, "BUILDING", $"Alanthor age-2 ladder: queued {buildingId}");
            return true;
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
            // HUT COVERAGE (endgame completeness): when the chokepoint /
            // directed-ring passes come up empty (anti-clump spacing
            // saturates the threat arc over a long match), spend the
            // remaining budget covering Gatherer's Huts — every hut farther
            // than HutCoverageRadius from a friendly Watch Tower gets one.
            if (!found)
                found = TryFindHutCoverageSpot(em, faction, hallPos, ownTowers, towerSize, out pos);
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

            // ── 1. Chokepoint scan along the approach (shared with the
            //       wall doctrine — TryFindApproachChokepoint). ──
            float maxWalk = math.min(len - 10f, 90f);
            bool choke = TryFindApproachChokepoint(hallPos, dir, perp, maxWalk,
                out float3 chokePos, out float3 chokeSide, out _);
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

        /// <summary>Chokepoint scan shared by the tower and wall doctrines:
        /// walk the straight approach line from the Hall toward the threat,
        /// measure corridor width at each step by perpendicular passability
        /// probes on the nav grid, and report the narrowest sub-threshold
        /// corridor. Integer-grid deterministic.</summary>
        private static bool TryFindApproachChokepoint(float3 hallPos, float3 dir, float3 perp,
            float maxWalk, out float3 chokePos, out float3 chokeSide, out float chokeWidth)
        {
            chokePos = default;
            chokeSide = default;
            chokeWidth = ChokeWidthThreshold;
            bool choke = false;
            for (float d = 14f; d <= maxWalk; d += 4f)
            {
                float3 p = hallPos + dir * d;
                float left = ClearanceAlong(p, perp, 14f);
                float right = ClearanceAlong(p, -perp, 14f);
                if (left + right <= 2f) continue; // solid wall, not a corridor
                float width = left + right;
                if (width < chokeWidth)
                {
                    chokeWidth = width;
                    chokePos = p;
                    chokeSide = left >= right ? perp : -perp;
                    choke = true;
                }
            }
            return choke;
        }

        /// <summary>A Gatherer's Hut counts as tower-covered when a friendly
        /// Watch Tower stands within this range.</summary>
        private const float HutCoverageRadius = 28f;

        /// <summary>Coverage fallback for the tower doctrine: find the
        /// uncovered Gatherer's Hut nearest the Hall (deterministic — the
        /// coverage grows outward from the core) and pick a tower spot in a
        /// ring around it, inside coverage range, with the doctrine's
        /// anti-clump spacing enforced by <see cref="IsTowerSpotOk"/>.</summary>
        private static bool TryFindHutCoverageSpot(EntityManager em, Faction faction,
            float3 hallPos, NativeList<float3> ownTowers, int2 towerSize, out float3 spot)
        {
            spot = default;

            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<GathererHutTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            float covSq = HutCoverageRadius * HutCoverageRadius;
            float3 hut = default;
            float bestDistSq = float.MaxValue;
            bool foundHut = false;
            for (int i = 0; i < facs.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                float3 p = xfs[i].Position;
                bool covered = false;
                for (int t = 0; t < ownTowers.Length; t++)
                {
                    float dx = p.x - ownTowers[t].x, dz = p.z - ownTowers[t].z;
                    if (dx * dx + dz * dz <= covSq) { covered = true; break; }
                }
                if (covered) continue;
                float hx = p.x - hallPos.x, hz = p.z - hallPos.z;
                float d2 = hx * hx + hz * hz;
                if (d2 < bestDistSq) { bestDistSq = d2; hut = p; foundHut = true; }
            }
            if (!foundHut) return false;

            // Ring around the hut — every radius stays inside coverage range.
            for (float r = 6f; r <= 18f; r += 4f)
            {
                for (int a = 0; a < 12; a++)
                {
                    float ang = (a / 12f) * 2f * math.PI;
                    float3 c = hut + new float3(math.cos(ang) * r, 0f, math.sin(ang) * r);
                    c.y = TerrainUtility.GetHeight(c.x, c.z);
                    if (IsTowerSpotOk(em, c, towerSize, ownTowers)) { spot = c; return true; }
                }
            }
            return false;
        }

        // ──────────────────────────────────────────────────────────────────
        // 6b. WALL DOCTRINE (terrain-aware: seal chokepoints, else enclose
        //     the base in a large square-ish perimeter)
        // ──────────────────────────────────────────────────────────────────
        //
        // The doctrine follows the player thought process: "Am I sheltered
        // by terrain? Does ingress mean going through chokepoints? If yes,
        // wall off and fortify the chokepoints. If not, wall a LARGE
        // square-ish area around what's important and push from there."
        //
        // AIWallPlanner runs the terrain-only shelter scan ONCE when the
        // doctrine first ticks and freezes the resulting plan (mode + hub
        // slot list with gate/tower flags) on the brain entity. Every think
        // tick afterwards executes one action from the plan:
        //   1. place the next missing hub (linking it to in-range friendly
        //      hubs — that stitching closes lines and perimeter loops);
        //   2. convert a finished gate-flagged segment to a Gate
        //      (gates auto-open for friendlies, so the enclosure never
        //      walls in the AI's own army; perimeter gates sit at the four
        //      side midpoints — one facing each cardinal direction);
        //   3. convert the wall instance at a tower-flagged slot (corners,
        //      line ends, gate shoulders) to a Wall Tower.
        //
        // Wall placement has no lockstep command yet — the player panel also
        // places hubs/segments with direct EM calls, so the AI mirrors that
        // (parity; multiplayer wall replication is future work). Gate
        // conversion rides the replicating CommandRouter entry point; tower
        // conversion mirrors ActionsPanelBinder's direct-EM path.

        /// <summary>Hard cap on doctrine-built wall hubs per faction —
        /// sized for a full max-extent perimeter (4 x 124 m / 12.5 m).</summary>
        private const int MaxWallHubs = 40;

        /// <summary>Hub / instance self-build time — mirrors
        /// BuilderCommandPanel.WallExtendBuildSeconds (30 s, AutoConstructTag,
        /// no builder dispatched).</summary>
        private const float WallHubBuildSeconds = 30f;

        /// <summary>A plan slot with a friendly hub within this range counts
        /// as filled.</summary>
        private const float WallSlotOccupiedRadius = 5f;

        /// <summary>Link radius for stitching a fresh hub to its plan
        /// neighbours — covers the plan's 30 m spacing plus nudge tolerance.
        /// Segments span any length (CreateSegment tiles 3 m modules); the
        /// 16 m WallAutoSegmentSystem constant is that DISABLED system's
        /// auto-link rule, not a segment limit, so it does not bound this.
        /// Kept under 2x HubSpacing so the wall never links across a dead
        /// slot's hole (that hole is deliberate — usually a mountain).</summary>
        private const float WallLinkRadius = AIWallPlanner.HubSpacing + 3f;

        private static void TryBuildWallDefenses(Faction faction, EntityManager em,
            Entity brainEntity, float3 hallPos)
        {
            // ── Plan once, then execute forever. ──
            if (!em.HasComponent<AIWallPlan>(brainEntity))
            {
                var planned = new NativeList<AIWallPlanSlot>(Allocator.Temp);
                byte mode = AIWallPlanner.BuildPlan(em, faction, hallPos, planned,
                    out string why);
                int gates = 0, towers = 0;
                for (int i = 0; i < planned.Length; i++)
                {
                    if ((planned[i].Flags & AIWallPlanner.FlagGateAfter) != 0) gates++;
                    if ((planned[i].Flags & AIWallPlanner.FlagTower) != 0) towers++;
                }
                em.AddComponentData(brainEntity, new AIWallPlan { Mode = mode });
                var buf = em.AddBuffer<AIWallPlanSlot>(brainEntity);
                for (int i = 0; i < planned.Length; i++) buf.Add(planned[i]);
                int slotCount = planned.Length;
                planned.Dispose();

                string modeName = mode switch
                {
                    AIWallPlanner.ModeNone => "fully sheltered, no walls needed",
                    AIWallPlanner.ModeChokepoints => "seal chokepoints",
                    _ => "perimeter around the base",
                };
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor walls: plan = {modeName} " +
                    $"({slotCount} hubs, {gates} gates, {towers} towers; {why})");
                return; // build from the next tick
            }

            var plan = em.GetComponentData<AIWallPlan>(brainEntity);
            if (plan.Mode == AIWallPlanner.ModeNone) return;
            if (!em.HasBuffer<AIWallPlanSlot>(brainEntity)) return;

            // Snapshot the slots — hub placement below is structural and
            // would invalidate a live buffer handle.
            var slots = em.GetBuffer<AIWallPlanSlot>(brainEntity)
                .ToNativeArray(Allocator.Temp);

            // Own hubs, snapshotted once (occupancy checks, link targets).
            var hubEntities = new NativeList<Entity>(Allocator.Temp);
            var hubPositions = new NativeList<float3>(Allocator.Temp);
            {
                var q = em.CreateEntityQuery(
                    ComponentType.ReadOnly<WallHubTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<LocalTransform>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    hubEntities.Add(ents[i]);
                    hubPositions.Add(xfs[i].Position);
                }
            }

            try
            {
                // One action per think tick, in priority order.
                if (hubEntities.Length < MaxWallHubs
                    && TryPlacePlannedHub(faction, em, brainEntity, slots,
                        hubEntities, hubPositions))
                    return;

                if (TryConvertPlannedGate(faction, em, slots, hubEntities, hubPositions))
                    return;

                TryConvertPlannedTower(faction, em, slots);
            }
            finally
            {
                slots.Dispose();
                hubEntities.Dispose();
                hubPositions.Dispose();
            }
        }

        /// <summary>Index of the first hub within <paramref name="radius"/>
        /// of <paramref name="pos"/>, or -1.</summary>
        private static int FindHubNear(NativeList<float3> hubPositions, float3 pos,
            float radius)
        {
            float r2 = radius * radius;
            for (int h = 0; h < hubPositions.Length; h++)
            {
                float dx = hubPositions[h].x - pos.x, dz = hubPositions[h].z - pos.z;
                if (dx * dx + dz * dz <= r2) return h;
            }
            return -1;
        }

        /// <summary>Unit direction along the plan chain at slot i — the
        /// nudge axis when the exact slot point is unbuildable.</summary>
        private static float3 ChainDirAt(NativeArray<AIWallPlanSlot> slots, int i)
        {
            int j = (i + 1 < slots.Length && slots[i + 1].Chain == slots[i].Chain) ? i + 1
                  : (i > 0 && slots[i - 1].Chain == slots[i].Chain) ? i - 1 : i;
            if (j == i) return new float3(1f, 0f, 0f);
            float3 d = slots[math.max(i, j)].Position - slots[math.min(i, j)].Position;
            d.y = 0f;
            float len = math.length(d);
            return len > 0.01f ? d / len : new float3(1f, 0f, 0f);
        }

        /// <summary>Place the first missing plan hub and link it to every
        /// friendly hub within <see cref="WallLinkRadius"/> (plan neighbours
        /// sit at HubSpacing, so the chain stitches itself and the perimeter
        /// loop closes on the last slot). Slots that fail placement even
        /// after nudging are marked dead. Returns true when a hub was placed
        /// this tick.</summary>
        private static bool TryPlacePlannedHub(Faction faction, EntityManager em,
            Entity brainEntity, NativeArray<AIWallPlanSlot> slots,
            NativeList<Entity> hubEntities, NativeList<float3> hubPositions)
        {
            if (!BuildCosts.TryGet("Alanthor_Wall", out var hubCost)) return false;
            int2 hubSize = BuildingSizeConfig.GetSize("Alanthor_Wall");
            const float maxLink = WallLinkRadius;

            int live = 0, filled = 0;
            for (int i = 0; i < slots.Length; i++)
                if ((slots[i].Flags & AIWallPlanner.FlagDead) == 0)
                {
                    live++;
                    if (FindHubNear(hubPositions, slots[i].Position,
                            WallSlotOccupiedRadius) >= 0) filled++;
                }

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if ((slot.Flags & AIWallPlanner.FlagDead) != 0) continue;
                if (FindHubNear(hubPositions, slot.Position,
                        WallSlotOccupiedRadius) >= 0) continue;

                // Wait for the bank rather than skipping ahead — the wall
                // grows in chain order so partial lines stay contiguous.
                if (!FactionEconomy.CanAfford(em, faction, hubCost)) return false;

                // Nudge candidates: PERPENDICULAR slides lead (2026-08-11,
                // Green's half wall: a rock on the line killed the middle
                // slot because along-chain nudges walked straight back into
                // it; sliding sideways clears a rock while keeping the
                // neighbour spacing inside the link radius).
                float3 chainDir = ChainDirAt(slots, i);
                float3 perp = new float3(-chainDir.z, 0f, chainDir.x);
                var nudges = new float3[]
                {
                    float3.zero,
                    perp * 2.5f, perp * -2.5f,
                    chainDir * 2.5f, chainDir * -2.5f,
                    perp * 5f, perp * -5f,
                };
                for (int n = 0; n < nudges.Length; n++)
                {
                    float3 pos = slot.Position + nudges[n];
                    pos.y = TerrainUtility.GetHeight(pos.x, pos.z);
                    if (!BuildCommandHelper.IsValidBuildPosition(em, pos, hubSize)) continue;

                    if (!FactionEconomy.Spend(em, faction, hubCost)) return false;
                    Entity hub = PlaceAutoBuildWallHub(em, pos, faction);
                    for (int h = 0; h < hubEntities.Length; h++)
                    {
                        float dx = hubPositions[h].x - pos.x;
                        float dz = hubPositions[h].z - pos.z;
                        if (dx * dx + dz * dz > maxLink * maxLink) continue;
                        if (!em.Exists(hubEntities[h])) continue;
                        if (AlanthorWall.AreHubsConnected(em, hub, hubEntities[h])) continue;
                        ConnectWallHubs(em, hub, hubEntities[h], faction);
                    }
                    AILogger.Log(faction, "BUILDING",
                        $"Alanthor walls: hub {filled + 1}/{live} at ({pos.x:F0},{pos.z:F0})");
                    return true;
                }

                // Unplaceable (veil crust / a building landed there since
                // planning) — kill the slot so the doctrine moves on, and
                // SAY SO: a dead slot is a hole in the wall (2026-08-11,
                // Green's silent half wall). No structural change has
                // happened this call, so the live buffer fetch is safe.
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor walls: slot at ({slot.Position.x:F0},{slot.Position.z:F0}) " +
                    "unplaceable after nudges — marked dead (HOLE in the wall line)");
                var buf = em.GetBuffer<AIWallPlanSlot>(brainEntity);
                var s = buf[i];
                s.Flags |= AIWallPlanner.FlagDead;
                buf[i] = s;
                slots[i] = s;
            }
            return false;
        }

        /// <summary>Convert the segment behind each gate-flagged slot to a
        /// Gate once both hubs stand and the wall pieces have finished
        /// self-building. One conversion per think tick; returns true when
        /// one was issued.</summary>
        private static bool TryConvertPlannedGate(Faction faction, EntityManager em,
            NativeArray<AIWallPlanSlot> slots,
            NativeList<Entity> hubEntities, NativeList<float3> hubPositions)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if ((slots[i].Flags & AIWallPlanner.FlagGateAfter) == 0) continue;
                if ((slots[i].Flags & AIWallPlanner.FlagDead) != 0) continue;

                // Far hub = next live slot of the same chain.
                int j = -1;
                for (int k = i + 1; k < slots.Length; k++)
                {
                    if (slots[k].Chain != slots[i].Chain) break;
                    if ((slots[k].Flags & AIWallPlanner.FlagDead) != 0) continue;
                    j = k;
                    break;
                }
                if (j < 0) continue;

                int ha = FindHubNear(hubPositions, slots[i].Position, WallSlotOccupiedRadius);
                int hb = FindHubNear(hubPositions, slots[j].Position, WallSlotOccupiedRadius);
                if (ha < 0 || hb < 0) continue;
                Entity hubA = hubEntities[ha], hubB = hubEntities[hb];
                if (!em.Exists(hubA) || !em.Exists(hubB)) continue;
                if (em.HasComponent<UnderConstruction>(hubA)) continue;
                if (em.HasComponent<UnderConstruction>(hubB)) continue;
                if (!em.HasBuffer<WallHubLink>(hubA)) continue;

                Entity segment = Entity.Null;
                var links = em.GetBuffer<WallHubLink>(hubA);
                for (int l = 0; l < links.Length; l++)
                    if (links[l].ConnectedHub == hubB) { segment = links[l].Segment; break; }
                if (segment == Entity.Null || !em.Exists(segment)) continue;
                if (em.HasComponent<WallSegmentUpgradeState>(segment)) continue; // converting
                if (SegmentHasGate(em, segment)) continue;                       // done
                if (SegmentUnderConstruction(em, segment)) continue;             // still rising

                if (!FactionEconomy.CanAfford(em, faction,
                        ConvertSegmentToGateCommandHelper.ConversionCost)) return false;
                CommandRouter.IssueConvertSegmentToGate(em, segment, Entity.Null,
                    CommandSource.AI);
                AILogger.Log(faction, "BUILDING",
                    $"Alanthor walls: gate conversion at " +
                    $"({slots[i].Position.x:F0},{slots[i].Position.z:F0})");
                return true;
            }
            return false;
        }

        private static bool SegmentHasGate(EntityManager em, Entity segment)
        {
            if (!em.HasBuffer<WallInstanceRef>(segment)) return false;
            var insts = em.GetBuffer<WallInstanceRef>(segment);
            for (int i = 0; i < insts.Length; i++)
                if (em.Exists(insts[i].Instance)
                    && em.HasComponent<WallGateTag>(insts[i].Instance))
                    return true;
            return false;
        }

        private static bool SegmentUnderConstruction(EntityManager em, Entity segment)
        {
            if (!em.HasBuffer<WallInstanceRef>(segment)) return false;
            var insts = em.GetBuffer<WallInstanceRef>(segment);
            for (int i = 0; i < insts.Length; i++)
                if (em.Exists(insts[i].Instance)
                    && em.HasComponent<UnderConstruction>(insts[i].Instance))
                    return true;
            return false;
        }

        /// <summary>Convert the wall instance nearest each tower-flagged
        /// slot (corners, line ends, gate shoulders) to a Wall Tower —
        /// mirrors ActionsPanelBinder's player path (cost + per-instance
        /// WallUpgradeState, UpgradeType 1). One conversion per think tick.
        /// A slot whose nearest instance already carries WallTowerTag is
        /// done and skipped.</summary>
        private static void TryConvertPlannedTower(Faction faction, EntityManager em,
            NativeArray<AIWallPlanSlot> slots)
        {
            if (!BuildCosts.TryGet("Alanthor_WallTower", out var towerCost)) return;

            var instEnts = new NativeList<Entity>(Allocator.Temp);
            var instPos = new NativeList<float3>(Allocator.Temp);
            {
                var q = em.CreateEntityQuery(
                    ComponentType.ReadOnly<WallInstanceTag>(),
                    ComponentType.ReadOnly<FactionTag>(),
                    ComponentType.ReadOnly<LocalTransform>());
                using var ents = q.ToEntityArray(Allocator.Temp);
                using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
                using var xfs = q.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                for (int i = 0; i < ents.Length; i++)
                {
                    if (facs[i].Value != faction) continue;
                    instEnts.Add(ents[i]);
                    instPos.Add(xfs[i].Position);
                }
            }

            try
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    if ((slots[i].Flags & AIWallPlanner.FlagTower) == 0) continue;
                    if ((slots[i].Flags & AIWallPlanner.FlagDead) != 0) continue;

                    int best = -1;
                    float bestD2 = 8f * 8f;
                    for (int k = 0; k < instEnts.Length; k++)
                    {
                        float dx = instPos[k].x - slots[i].Position.x;
                        float dz = instPos[k].z - slots[i].Position.z;
                        float d2 = dx * dx + dz * dz;
                        if (d2 < bestD2) { bestD2 = d2; best = k; }
                    }
                    if (best < 0) continue;
                    Entity inst = instEnts[best];
                    if (!em.Exists(inst)) continue;
                    if (em.HasComponent<WallTowerTag>(inst)) continue;      // done
                    if (em.HasComponent<WallUpgradeState>(inst)) continue;  // converting
                    if (em.HasComponent<WallGateTag>(inst)) continue;       // gate piece
                    if (em.HasComponent<WallGateRegionTag>(inst)) continue;
                    if (em.HasComponent<UnderConstruction>(inst)) continue; // still rising

                    if (!FactionEconomy.CanAfford(em, faction, towerCost)) return;
                    if (!FactionEconomy.Spend(em, faction, towerCost)) return;
                    em.AddComponentData(inst, new WallUpgradeState
                    {
                        UpgradeType = 1,
                        Duration = 10f,
                        Remaining = 10f,
                    });
                    AILogger.Log(faction, "BUILDING",
                        $"Alanthor walls: tower conversion at " +
                        $"({slots[i].Position.x:F0},{slots[i].Position.z:F0})");
                    return; // one per tick
                }
            }
            finally
            {
                instEnts.Dispose();
                instPos.Dispose();
            }
        }

        /// <summary>Place a self-building wall hub (30 s AutoConstruct, no
        /// builder) — mirrors BuilderCommandPanel.SpawnExtendedWallHub.
        /// Every hub SEALS to adjacent impassable terrain (curtain modules
        /// across the hub-to-rock gap) so chokepoint lines cannot be
        /// squeezed around at their ends.</summary>
        private static Entity PlaceAutoBuildWallHub(EntityManager em, float3 pos, Faction faction)
        {
            Entity hub = AlanthorWall.CreateHub(em, pos, faction);
            em.AddComponentData(hub, new UnderConstruction
            {
                Progress = 0f,
                Total = WallHubBuildSeconds,
            });
            em.AddComponent<AutoConstructTag>(hub);
            if (em.HasComponent<Health>(hub))
            {
                var hp = em.GetComponentData<Health>(hub);
                em.SetComponentData(hub, new Health { Value = 1, Max = hp.Max });
            }
            AlanthorWall.SealToTerrain(em, hub, autoConstruct: true);
            return hub;
        }

        /// <summary>Create the segment between two hubs and tag every spawned
        /// wall instance for auto-construction. The instance buffer is
        /// snapshotted first — the AddComponentData calls below are
        /// structural and would invalidate a live buffer handle (same
        /// pattern as the player's chain-placement code).</summary>
        private static void ConnectWallHubs(EntityManager em, Entity hubA, Entity hubB,
            Faction faction)
        {
            Entity segment = AlanthorWall.CreateSegment(em, hubA, hubB, faction);
            if (!em.HasBuffer<WallInstanceRef>(segment)) return;

            var instances = em.GetBuffer<WallInstanceRef>(segment);
            int count = instances.Length;
            var snapshot = new NativeArray<Entity>(count, Allocator.Temp);
            for (int i = 0; i < count; i++) snapshot[i] = instances[i].Instance;

            for (int i = 0; i < count; i++)
            {
                var inst = snapshot[i];
                if (!em.Exists(inst)) continue;
                if (!em.HasComponent<UnderConstruction>(inst))
                    em.AddComponentData(inst, new UnderConstruction
                    {
                        Progress = 0f,
                        Total = WallHubBuildSeconds,
                    });
                if (!em.HasComponent<AutoConstructTag>(inst))
                    em.AddComponent<AutoConstructTag>(inst);
                if (em.HasComponent<Health>(inst))
                {
                    var hp = em.GetComponentData<Health>(inst);
                    em.SetComponentData(inst, new Health { Value = 1, Max = hp.Max });
                }
            }
            snapshot.Dispose();
        }

        // ──────────────────────────────────────────────────────────────────
        // 7. ARMOURED-UNIT PRODUCTION
        // ──────────────────────────────────────────────────────────────────

        // Push the armoured lines into their production buildings' queues.
        // Same pattern SimpleAISystem uses for Age-1 units; charges cost via
        // FactionEconomy.Spend so we don't double-deduct.
        //   Stable    — Cataphract first (the heavy line), Outrider filler.
        //   SiegeYard — Trebuchet when its level gate opens, else Ballista.
        //     (Was "Alanthor_Catapult" — a UnitFactory ALIAS the TechCatalog
        //     does not carry, so TryGetUnit failed and the AI shipped ZERO
        //     siege in every match up to 2026-08-11. The catalog id is
        //     "Alanthor_Ballista".)
        // Infantry/archer lines stay with SimpleAISystem's composition
        // picker — the Barracks queue belongs to it.
        private static void TryQueueArmouredUnits(Faction faction, EntityManager em)
        {
            if (!TryQueueAt<RoyalStableTag>(em, faction, "Alanthor_Cataphract"))
                TryQueueAt<RoyalStableTag>(em, faction, "Alanthor_Outrider");
            if (!TryQueueAt<SiegeYardTag>(em, faction, "Alanthor_Trebuchet"))
                TryQueueAt<SiegeYardTag>(em, faction, "Alanthor_Ballista");
        }

        /// <summary>Alive-or-queued cap per sect for the chapel unit
        /// (docs/Design/Sect_Units.md) — elite specialists, not a line.</summary>
        private const int SectUnitCap = 2;

        /// <summary>Train each adopted sect's unique unit at its chapel —
        /// chapels carry a train queue from birth and are the ONLY trainer
        /// for these (SectConfig.UnitIdFor). One queue attempt per think
        /// tick across all chapels.</summary>
        private static void TryTrainSectUnits(Faction faction, EntityManager em)
        {
            if (!TechCatalog.IsReady) return;
            var q = em.CreateEntityQuery(
                ComponentType.ReadOnly<ChapelTag>(),
                ComponentType.ReadOnly<FactionTag>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                Entity chapel = ents[i];
                if (em.HasComponent<UnderConstruction>(chapel)) continue;
                if (!em.HasBuffer<TrainQueueItem>(chapel)) continue;
                if (em.GetBuffer<TrainQueueItem>(chapel).Length >= MaxTrainQueue) continue;

                string sectId = em.GetComponentData<ChapelTag>(chapel).SectId.ToString();
                string unitId = SectConfig.UnitIdFor(sectId);
                if (unitId == null) continue;
                if (CountAliveAndQueued(em, faction, unitId) >= SectUnitCap) continue;
                if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) continue;

                var cost = ToCost(def.cost);
                if (!FactionEconomy.CanAfford(em, faction, cost)) continue;
                if (!FactionEconomy.Spend(em, faction, cost)) continue;
                CommandRouter.IssueTrain(em, chapel, unitId, CommandSource.AI);
                AILogger.Log(faction, "MILITARY",
                    $"Alanthor: queued {unitId} at the {sectId.Substring(5)} chapel");
                return; // one per tick
            }
        }

        /// <summary>Living units of the exact type plus copies waiting in any
        /// of this faction's train queues — the sect-unit cap check.</summary>
        private static int CountAliveAndQueued(EntityManager em, Faction faction, string unitId)
        {
            int n = 0;
            var uq = em.CreateEntityQuery(
                ComponentType.ReadOnly<UnitTypeId>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<Health>());
            using (var uEnts = uq.ToEntityArray(Allocator.Temp))
            using (var uFacs = uq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < uEnts.Length; i++)
                {
                    if (uFacs[i].Value != faction) continue;
                    if (em.GetComponentData<Health>(uEnts[i]).Value <= 0) continue;
                    if (em.GetComponentData<UnitTypeId>(uEnts[i]).Value.ToString() == unitId) n++;
                }
            }
            var tq = em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<TrainQueueItem>());
            using (var tEnts = tq.ToEntityArray(Allocator.Temp))
            using (var tFacs = tq.ToComponentDataArray<FactionTag>(Allocator.Temp))
            {
                for (int i = 0; i < tEnts.Length; i++)
                {
                    if (tFacs[i].Value != faction) continue;
                    var buf = em.GetBuffer<TrainQueueItem>(tEnts[i]);
                    for (int j = 0; j < buf.Length; j++)
                        if (buf[j].UnitId.ToString() == unitId) n++;
                }
            }
            return n;
        }

        /// <summary>Returns true when the unit was queued — false on any
        /// pre-flight failure so callers can fall back down a priority list
        /// (e.g. Trebuchet gate closed, queue Ballista instead).</summary>
        private static bool TryQueueAt<TBuildingTag>(EntityManager em, Faction faction, string unitId)
            where TBuildingTag : unmanaged, IComponentData
        {
            Entity trainer = FindFactionBuilding<TBuildingTag>(em, faction);
            if (trainer == Entity.Null) return false;
            if (em.HasComponent<UnderConstruction>(trainer)) return false;
            if (!em.HasBuffer<TrainQueueItem>(trainer)) return false;
            if (em.GetBuffer<TrainQueueItem>(trainer).Length >= MaxTrainQueue) return false;

            if (!TechCatalog.IsReady) return false;
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return false;

            // Level gate BEFORE spending — IssueTrain drops silently for AI
            // sources, which would leak the cost.
            if (!CommandRouter.CanTrainAtBuilding(em, trainer, unitId, out _, out _)) return false;

            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;
            if (!FactionEconomy.Spend(em, faction, cost)) return false;

            // Through CommandRouter (CommandSource.AI) so host-AI training
            // replicates — a direct queue.Add spawned units on the host only.
            CommandRouter.IssueTrain(em, trainer, unitId, CommandSource.AI);
            AILogger.Log(faction, "MILITARY", $"Alanthor: queued {unitId}");
            return true;
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

        /// <summary>Count this faction's buildings by marker tag (completed
        /// AND under construction — expansion targets are totals).</summary>
        private static int CountFactionBuildingsByTag<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<FactionTag>());
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) count++;
            return count;
        }

        /// <summary>True while any of this faction's buildings with the given
        /// marker tag is still under construction — the one-foundation-at-a-
        /// time gate for the expansion passes.</summary>
        private static bool AnyFactionBuildingUnderConstruction<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<T>(),
                ComponentType.ReadOnly<UnderConstruction>(),
                ComponentType.ReadOnly<FactionTag>());
            using var facs = query.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < facs.Length; i++)
                if (facs[i].Value == faction) return true;
            return false;
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
