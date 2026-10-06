// AIAlanthorEndgameSystem.cs
// Culture-specific endgame AI for Alanthor factions. Picks up after the
// SimpleAISystem build order finishes (Age 2+) and drives the late-game
// behaviour Alanthor should ship with: defensive tower clusters (with a
// Gatherer's Hut coverage pass), wall hubs at chokepoints (or a base
// ring), sect adoption (Fortitude / Renewal cluster) AND active-power
// firing, housing toward 8 Houses, armoured unit production from the Stable /
// SiegeYard, and worker flee from threats.
//
// Scope: SELF-SUFFICIENT. The legacy AIBuildingManager / AIEconomyManager /
// AIMilitaryManager are all [DisableAutoCreation] (replaced by
// SimpleAISystem) and their BuildRequest / RecruitmentRequest buffers
// are dead code. So this system bypasses those buffers entirely and
// drives Era-2+ Alanthor behaviour with direct ECS calls — same pattern
// SimpleAISystem uses for Age-1 buildings (CommandRouter.IssuePlaceBuilding
// + DispatchWorkersTo, IssueTrain; every cost is charged inside the
// per-peer command executors, never AI-side —
// docs/Multiplayer_LAN_Readiness.md).
//
// Tick rate: 5 seconds (slow loop — strategic decisions, not micro).
//
// Phases (each tick):
//   1. (removed 2026-10-05 — the AIStrategyState latch / Defensive flip;
//      the personality is the only strategy state now.)
//   2. Sect adoption — when a Temple of Ridan exists and RP / supplies /
//      veilstone can afford a chapel, queue an adoption via SectAdoption.
//      Picks Alanthor-cluster sects in priority order (Fortitude first).
//   3. Sect active-power firing — for every adopted sect that has a
//      level-1+ Active-Power lever and is off cooldown, fire it at the
//      most useful target: offensive (Smite / Burning / Pyre) at enemy
//      clusters in/near our base; support (Heal / Armor / Damage / Speed)
//      on our own armies in combat; reveal at the last-known enemy
//      position.
//   4. Age-2 ladder + expansion — Temple / Stable / SiegeYard in order
//      (CanAfford gate + CommandRouter placement + DispatchWorkersTo).
//      Once the ladder stands: sect buildings, then Huts toward 8
//      Houses, one foundation per tick.
//   6. Defensive tower spam — late-game (>5 min) build extra Alanthor_Towers
//      around the Hall up to a cap. Direct creation (was queueing into
//      the dead BuildRequest buffer; never actually built anything).
//   7. Armoured-unit production — when a Barracks / Alanthor_SiegeYard
//      exists and its TrainQueue has room, push Cataphract / Ballista
//      through IssueTrain (cost charged in the per-peer executor).
//   8. Worker flee — for every worker of this faction with an
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
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SimpleAISystem))]
    public partial struct AIAlanthorEndgameSystem : ISystem
    {
        #region Cached queries

        // CreateEntityQuery registers a new query with the world on EVERY
        // call; these run per think tick. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_AIBrain =
        {
            ComponentType.ReadOnly<AIBrain>(),
        };
        static CachedEntityQuery QC_AIBrain;

        static readonly ComponentType[] QT_HallTagFactionTagFactionProgressLocalTransform =
        {
            ComponentType.ReadOnly<HallTag>(),
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<FactionProgress>(),
            ComponentType.ReadOnly<LocalTransform>(),
        };
        static CachedEntityQuery QC_HallTagFactionTagFactionProgressLocalTransform;

        static readonly ComponentType[] QT_BuildingTagPresentationIdFactionTag =
        {
            ComponentType.ReadOnly<BuildingTag>(),
            ComponentType.ReadOnly<PresentationId>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_BuildingTagPresentationIdFactionTag;

        #endregion

        #region Tuning

        /// <summary>Every number below used to be a const in this file;
        /// they live in AIAlanthorEndgameSystem.asset now.</summary>
        static AIAlanthorEndgameSystemConfig Cfg => AIAlanthorEndgameSystemConfig.I;

        #endregion



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
            var brainQuery = QC_AIBrain.Get(em, QT_AIBrain);
            using var brainEntities = brainQuery.ToEntityArray(Allocator.Temp);
            for (int b = 0; b < brainEntities.Length; b++)
            {
                var entity = brainEntities[b];
                if (!em.Exists(entity)) continue;
                var brain = em.GetComponentData<AIBrain>(entity);
                if (brain.IsActive == 0) continue;

                Faction faction = brain.Owner;

                // Throttle, SCALED BY DIFFICULTY. The cadence used to be one
                // flat 5 s for every tier, so an Expert fortified no faster
                // than an Easy — see AIDifficultyProfileSO.supportThinkScale.
                float think = Cfg.thinkInterval
                            * AISimpleDifficulty.GetProfile(brain.Difficulty).SupportThinkScale;

                // Per-brain tick state, lazy-stamped on first sight.
                if (em.HasComponent<AIAlanthorTickState>(entity))
                {
                    var tick = em.GetComponentData<AIAlanthorTickState>(entity);
                    if (time < tick.NextThinkTime) continue;
                    // Shared per-frame heavy-think budget (AIThinkBudget):
                    // refused = try again next frame, unless a whole interval
                    // overdue.
                    if (!AIThinkBudget.TryClaim(time - tick.NextThinkTime > think)) continue;
                    tick.NextThinkTime = time + think;
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
                        NextThinkTime = time + think + (int)faction * (think / 8f),
                    });
                    continue; // skip first tick after stamp
                }

                // Find this faction's Hall and read culture/era.
                bool hasHall = false;
                byte culture = Cultures.None;
                int era = 1;
                float3 hallPos = float3.zero;
                {
                    var hallQuery = QC_HallTagFactionTagFactionProgressLocalTransform.Get(em, QT_HallTagFactionTagFactionProgressLocalTransform);
                    // THE HOME HALL, not the first Hall the query returns.
                    // With expansion claims live a faction holds 4-7 Halls,
                    // and chunk order is arbitrary — batch-proven: every
                    // walled Red base was an EXPANSION (wall centroid 270-320
                    // m from home, 3-14 m from an expansion Hall) while the
                    // home stood bare, because this anchor drives the wall
                    // doctrine, houses and sect buildings. The
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

                // (Phase 1, the AIStrategyState HasAgedUp latch and the
                // losses-to-Defensive flip, was removed on 2026-10-05: the
                // loss counter was never incremented, so the flip never
                // fired, and nothing read the flipped value.)

                // ─── 2. Sect adoption ─────────────────────────────────
                TryAdoptNextSect(faction, em);

                // ─── 3. Sect active-power firing ──────────────────────
                TryFireSectPowers(faction, em, hallPos);

                // ─── 4. Age-2 building ladder ─────────────────────────
                // Temple FIRST (sect adoption hard-requires a Temple to
                // host chapels — without this ladder no strategy except
                // Turtle ever built one, so sects and their content never
                // appeared), then the military production pair. One attempt
                // per think tick. Returns true while a ladder entry is still
                // missing so the expansion passes below wait for the core to
                // stand.
                bool ladderBusy = TryBuildAge2Ladder(faction, em, hallPos);

                // Pivotal savings hold (AIPivotalReserve): while the faction
                // saves toward a pivotal purchase, the discretionary passes
                // below skip their spends. Sects and worker flee always run.
                // RESOURCE-AWARE (2026-10-03): each pass tests the hold at
                // its own spend point against THAT purchase's cost
                // (TryBuildOnce holdable, TryQueueAt, the sect trainers and
                // research, the tower), so a save short only on veilstone no
                // longer freezes supplies/iron spending. The wall program is
                // gated here on a stone hub's price.
                bool wallsHeld = BuildCosts.TryGet("Alanthor_Wall", out var wallHubCost)
                    ? AIPivotalReserve.ShouldHold(em, faction, wallHubCost)
                    : AIPivotalReserve.ShouldHold(em, faction);

                // ─── 4c/4d. Expansion targets ─────────────────────────
                // Once the Age-2 core stands: Huts toward 8 Houses, one
                // foundation per think tick. Sect buildings come FIRST:
                // each one unlocks a unit and a faction-wide research the
                // AI cannot get any other way, whereas a Hut is only more
                // of what it already has.
                if (!ladderBusy
                    && !TryBuildSectBuildings(faction, em, hallPos)
                    )
                    TryBuildHouses(faction, em, hallPos);

                // ─── 4e. Sect research ────────────────────────────────
                // One faction-wide effect per adopted sect, bought at that
                // sect's own building (docs/Design/Sects.md section 1).
                TryResearchSectTech(faction, em);

                // ─── 6. Tower doctrine ────────────────────────────────
                // Towers are BOTH Alanthor's territory claims (each projects
                // a 15 m build-space circle) and its static defense. Placed
                // toward the known threat with chokepoint preference and
                // anti-clump spacing — from era-2 start, budget by
                // difficulty (no more 4-in-a-row ring spam at minute 5).
                TryBuildDefensiveTower(faction, em, entity, brain.Difficulty, hallPos);

                // ─── 6b. Wall doctrine ────────────────────────────────
                // Terrain-aware plan execution — endgame only (the ladder
                // keeps priority on the bank while it is building).
                if (!wallsHeld && !ladderBusy)
                    TryBuildWallDefenses(faction, em, entity, hallPos);
                // …except the gates: a ring with no gate seals the army in,
                // so cutting one ignores the ladder and the savings holds
                // (TryBuildWallDefenses runs it first when it runs at all).
                else
                    TryEnsureRingGates(faction, em, entity, hallPos);

                // ─── 7. (Armoured-unit production moved to the composition
                //    layer, 2026-10-03 — see AIAlanthorEndgameSystem.Military.cs.)

                // ─── 7b. Sect units ───────────────────────────────────
                // Canon trains the sect unit at the SECT BUILDING; the chapel
                // path stays for the sects that have no building yet
                // (docs/Design/Sects.md section 1; cap 2 per sect).
                TryTrainSectUnitsAtSectBuildings(faction, em);
                TryTrainSectUnits(faction, em);

                // ─── 8. Worker flee ───────────────────────────────────
                HandleWorkerFlee(faction, em, hallPos, time);
            }

            perfSw.Stop();
            if (perfThinks > 0)
                TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                    "AIEndgame", perfSw.Elapsed.TotalMilliseconds, $"brains {perfThinks}");
        }

        // ──────────────────────────────────────────────────────────────────
        // WORKER / PLACEMENT HELPERS (mirrors SimpleAISystem private helpers)
        // ──────────────────────────────────────────────────────────────────





        // Simple ring-scan placement: try angles around the anchor at radii
        // within [rmin, rmax]. Returns the first candidate that
        // BuildCommandHelper.IsValidBuildPosition accepts. Used for endgame
        // buildings (towers, sect halls) where SimpleAISystem's GH-spacing
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
            var query = QC_BuildingTagPresentationIdFactionTag.Get(em, QT_BuildingTagPresentationIdFactionTag);
            using var ents = query.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                if (em.GetComponentData<PresentationId>(ents[i]).Id != pid) continue;
                count++;
            }
            return count;
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
    /// Per-worker flee throttle. Stamped by
    /// AIAlanthorEndgameSystem.HandleWorkerFlee on first detection of a
    /// nearby threat; prevents the system from re-issuing MoveCommand
    /// every tick while the worker is already running home.
    /// </summary>
    public struct AIWorkerFleeState : IComponentData
    {
        public float NextRetryTime;
    }
}
