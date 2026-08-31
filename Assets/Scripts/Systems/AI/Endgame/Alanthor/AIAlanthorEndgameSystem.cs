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
// SimpleAISystem uses for Age-1 buildings (CommandRouter.IssuePlaceBuilding
// + DispatchBuildersTo, IssueTrain; every cost is charged inside the
// per-peer command executors, never AI-side —
// docs/Multiplayer_LAN_Readiness.md).
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
//      SiegeYard in order (CanAfford gate + CommandRouter
//      placement + DispatchBuildersTo); every Smelter is levelled
//      lowest-first. Once the ladder stands: more Smelters toward the
//      5-cap and Huts toward 8 Houses, one foundation per tick. The
//      Forges generate veilsteel passively (no miner supply chain).
//   6. Defensive tower spam — late-game (>5 min) build extra Alanthor_Towers
//      around the Hall up to a cap. Direct creation (was queueing into
//      the dead BuildRequest buffer; never actually built anything).
//   7. Armoured-unit production — when a Barracks / Alanthor_SiegeYard
//      exists and its TrainQueue has room, push Cataphract / Ballista
//      through IssueTrain (cost charged in the per-peer executor).
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

        // Train-queue cap per Stable / SiegeYard. Mirrors SimpleAISystem.
        private const int MaxTrainQueue = 5;

        // Strategy switch threshold: number of armies lost without dealing
        // significant damage since the last strategy switch before we flip
        // to Defensive. Cheap signal — armies-lost is bumped by combat
        // bookkeeping elsewhere; we just react to it.
        private const int LossesBeforeDefensiveFlip = 2;

        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!GameSettings.ShouldRunAIBrains()) return;
            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            // Snapshot brain entities first — we make structural changes
            // (creating buildings) that would invalidate a SystemAPI.Query
            // iteration.
            var perfSw = System.Diagnostics.Stopwatch.StartNew();
            int perfThinks = 0;
            var brainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<AIBrain>());
            using var brainEntities = brainQuery.ToEntityArray(Allocator.Temp);
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
                {
                    var hallQuery = em.CreateEntityQuery(
                        ComponentType.ReadOnly<HallTag>(),
                        ComponentType.ReadOnly<FactionTag>(),
                        ComponentType.ReadOnly<FactionProgress>(),
                        ComponentType.ReadOnly<LocalTransform>());
                    // THE HOME HALL, not the first Hall the query returns.
                    // With expansion claims live a faction holds 4-7 Halls,
                    // and chunk order is arbitrary — batch-proven: every
                    // walled Red base was an EXPANSION (wall centroid 270-320
                    // m from home, 3-14 m from an expansion Hall) while the
                    // home stood bare, because this anchor drives the wall
                    // doctrine, houses, smelters and sect buildings. The
                    // starting Hall has the lowest NetworkId its faction
                    // owns — ids are handed out sequentially from spawn.
                    long bestNid = long.MaxValue;
                    using var hallEnts = hallQuery.ToEntityArray(Allocator.Temp);
                    for (int i = 0; i < hallEnts.Length; i++)
                    {
                        if (em.GetComponentData<FactionTag>(hallEnts[i]).Value != faction) continue;
                        long nid = em.HasComponent<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(hallEnts[i])
                            ? em.GetComponentData<TheWaningBorder.Core.Multiplayer.NetworkedEntity>(hallEnts[i]).NetworkId
                            : long.MaxValue - 1;
                        if (hasHall && nid >= bestNid) continue;
                        bestNid  = nid;
                        culture  = em.GetComponentData<FactionProgress>(hallEnts[i]).Culture;
                        hallPos  = em.GetComponentData<LocalTransform>(hallEnts[i]).Position;
                        hasHall  = true;
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
                // Sect buildings come FIRST of the three: each one unlocks a
                // unit and a faction-wide research the AI cannot get any other
                // way, whereas a Smelter or a Hut is only more of what it
                // already has.
                if (!saving && !ladderBusy
                    && !TryBuildSectBuildings(faction, em, hallPos)
                    && !TryExpandSmelters(faction, em, hallPos))
                    TryBuildHouses(faction, em, hallPos);

                // ─── 4e. Sect research ────────────────────────────────
                // One faction-wide effect per adopted sect, bought at that
                // sect's own building (docs/Design/Sects.md section 1).
                if (!saving)
                    TryResearchSectTech(faction, em);

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

                // ─── 7b. Sect units ───────────────────────────────────
                // Canon trains the sect unit at the SECT BUILDING; the chapel
                // path stays for the sects that have no building yet
                // (docs/Design/Sects.md section 1; cap 2 per sect).
                if (!saving)
                {
                    TryTrainSectUnitsAtSectBuildings(faction, em);
                    TryTrainSectUnits(faction, em);
                }

                // ─── 8. Worker flee ───────────────────────────────────
                HandleWorkerFlee(faction, em, hallPos, time);
            }

            perfSw.Stop();
            if (perfThinks > 0)
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                    "AIEndgame", perfSw.Elapsed.TotalMilliseconds, $"brains {perfThinks}");
        }

        /// <summary>Returns true when the unit was queued — false on any
        /// pre-flight failure so callers can fall back down a priority list
        /// (e.g. Trebuchet gate closed, queue Ballista instead).</summary>
        private static bool TryQueueAt<TBuildingTag>(EntityManager em, Faction faction, string unitId)
            where TBuildingTag : unmanaged, IComponentData
        {
            Entity trainer = AIEndgameCommon.FindFactionBuilding<TBuildingTag>(em, faction);
            if (trainer == Entity.Null) return false;
            if (em.HasComponent<UnderConstruction>(trainer)) return false;
            if (!em.HasBuffer<TrainQueueItem>(trainer)) return false;
            if (em.GetBuffer<TrainQueueItem>(trainer).Length >= MaxTrainQueue) return false;

            if (!TechCatalog.IsReady) return false;
            if (!TechCatalog.TryGetUnit(unitId, out var def) || def == null) return false;

            // Level gate BEFORE spending — IssueTrain drops silently for AI
            // sources, which would leak the cost.
            if (!CommandRouter.CanTrainAtBuilding(em, trainer, unitId, out _, out _)) return false;

            // Affordability CHECK only — TrainCommandDirect spends on every
            // peer (docs/Multiplayer_LAN_Readiness.md).
            var cost = ToCost(def.cost);
            if (!FactionEconomy.CanAfford(em, faction, cost)) return false;

            // Through CommandRouter (CommandSource.AI) so host-AI training
            // replicates — a direct queue.Add spawned units on the host only.
            CommandRouter.IssueTrain(em, trainer, unitId, CommandSource.AI);
            AILogger.Log(faction, "MILITARY", $"Alanthor: queued {unitId}");
            return true;
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
        /// <summary>Ring scan for a legal build spot. Tuning (24 samples,
        /// 4 m steps, hash-seeded start angle) is Alanthor's; the algorithm is
        /// shared with Feraldis in AIEndgameCommon.</summary>
        private static bool TryFindBuildPositionRing(EntityManager em, float3 anchor,
            int2 buildingSize, float rmin, float rmax, out float3 pos)
            => AIEndgameCommon.TryFindBuildSpotRing(em, anchor, buildingSize, rmin, rmax,
                angleSamples: 24, radiusStep: 4f, seededStart: true, out pos);

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
