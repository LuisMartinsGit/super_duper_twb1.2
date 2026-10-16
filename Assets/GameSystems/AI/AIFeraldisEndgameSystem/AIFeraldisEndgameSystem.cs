// AIFeraldisEndgameSystem.cs
// The Feraldis late game, mirroring AIAlanthorEndgameSystem's shape.
//
// Alanthor's endgame is "fortify and purify". Feraldis's is the opposite:
// build the raiding economy, then CRACK WELLS OPEN and smash them. Destroy
// every well at once and the match is won outright (NodeVictorySystem
// already awards that instantly for a Feraldis destroyer).
//
// Phases per think tick:
//   1. Strategy latch (HasAgedUp, pressure flip)
//   2. Economy spine   — Raider Camps are the whole Feraldis income
//   3. Age-2 buildings — Pasture / Thrower Camp / Temple
//   4. Temple leveling — the Corruptor needs Temple Lv 3
//   5. THE VERB       — train a Corruptor, send it at a well, escort it,
//                        and commit the army onto a cracked well
//
// It deliberately does NOT duplicate SimpleAISystem's job (workers, basic
// army, research) — that keeps running underneath, exactly as it does for
// Alanthor.
//
// STAGED THINK (2026-10-15 perf), as AIAlanthorEndgameSystem: one think was
// 6-10 ms on one tick. It now runs in ThinkStages stages, one per
// AIThinkBudget claim — slaves / training / Forge, then Harkon / totem rites
// / sacrifice, then totem planting / Age-2 buildings / sect adoption. The
// interval still runs from the think's start; a stage waiting a whole
// interval is forced through. Host-only, lockstep commands only.

using Unity.Collections;
using TheWaningBorder.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Commands.Types;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.World.Terrain;
using TheWaningBorder.Core.Settings;

namespace TheWaningBorder.AI
{
    /// <summary>Per-brain think throttle. Its OWN component — sharing
    /// AIAlanthorTickState would have the two endgame systems stealing each
    /// other's ticks in a mixed match.</summary>
    public struct AIFeraldisTickState : IComponentData
    {
        public float NextThinkTime;

        /// <summary>Game time the AI first wanted to send its Corruptor but
        /// had no escort. 0 = not currently holding. Drives the patience
        /// timeout in TryRunTheVerb so a hold can never be permanent.</summary>
        public float CorruptorHeldSince;

        /// <summary>The stage of the think in progress (0 = none).</summary>
        public byte Stage;

        /// <summary>When the current stage became due (starvation guard).</summary>
        public float StageSince;
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SimpleAISystem))]
    public partial struct AIFeraldisEndgameSystem : ISystem
    {
        /// <summary>Stages one think is split into (engine shape: the number
        /// of stage cases in OnUpdate, not a tunable).</summary>
        private const int ThinkStages = 3;

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

        #endregion

        #region Tuning

        /// <summary>Every number below used to be a const in this file;
        /// they live in AIFeraldisEndgameSystem.asset now.</summary>
        static AIFeraldisEndgameSystemConfig Cfg => AIFeraldisEndgameSystemConfig.I;

        #endregion






        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (!GameSettings.ShouldRunAIBrains()) return;
            // Charged to the AI's per-frame TIME budget (AIFrameBudget,
            // 2026-10-16); its stage claims (AIThinkBudget) read that budget.
            AIFrameBudget.Enter();
            try { RunThinks(ref state); }
            finally { AIFrameBudget.Exit(); }
        }

        private void RunThinks(ref SystemState state)
        {
            // Host authority only — the same guard the Alanthor sibling uses.
            if (!GameSettings.ShouldRunAIBrains()) return;

            var em = state.EntityManager;
            float now = TheWaningBorder.Core.SimClock.Now;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // Snapshot brains before any structural change.
            var brainQuery = QC_AIBrain.Get(em, QT_AIBrain);
            using var brains = brainQuery.ToEntityArray(Allocator.Temp);

            for (int i = 0; i < brains.Length; i++)
            {
                var brainEntity = brains[i];
                var brain = em.GetComponentData<AIBrain>(brainEntity);
                if (brain.IsActive == 0) continue;
                var faction = brain.Owner;

                // Throttle, staggered per faction so eight AIs don't all
                // think on the same frame — and scaled by difficulty, which
                // used to reach this system not at all.
                float think = Cfg.thinkInterval
                            * AISimpleDifficulty.GetProfile(brain.Difficulty).SupportThinkScale;

                if (!em.HasComponent<AIFeraldisTickState>(brainEntity))
                {
                    em.AddComponentData(brainEntity, new AIFeraldisTickState
                    {
                        NextThinkTime = now + think + (int)faction * (think / 8f)
                    });
                    continue;
                }
                var tick = em.GetComponentData<AIFeraldisTickState>(brainEntity);
                if (tick.Stage == 0)
                {
                    if (now < tick.NextThinkTime) continue;
                    // Shared per-frame heavy-think budget (AIThinkBudget).
                    if (!AIThinkBudget.TryClaim(now - tick.NextThinkTime > think)) continue;
                    tick.NextThinkTime = now + think;
                    tick.Stage = 1;
                }
                else if (!AIThinkBudget.TryClaim(now - tick.StageSince > think)) continue;
                int stage = tick.Stage;
                tick.Stage = (byte)(stage >= ThinkStages ? 0 : stage + 1);
                tick.StageSince = now;
                em.SetComponentData(brainEntity, tick);

                // Culture + era gate — the fork point from the Alanthor sibling.
                // A faction that fails it ends its think here, as it always did.
                bool gate = TryGetHall(em, faction, out float3 hallPos, out byte culture)
                    && culture == Cultures.Feraldis
                    && FactionEconomy.TryGetBank(em, faction, out var bank)
                    && em.HasComponent<FactionEra>(bank)
                    && em.GetComponentData<FactionEra>(bank).Value >= 2;
                if (!gate)
                {
                    tick.Stage = 0;
                    em.SetComponentData(brainEntity, tick);
                    continue;
                }

                // 2026-10-12 redesign (docs/Design/Age_1_Feraldis.md): slaves
                // staff the extractors and the Forge instead of being
                // conscripted; the Corruptor verb is retired (no wells).
                if (stage == 1)
                {
                    StaffSlaves(em, faction);
                    TrainSlavesAndUniques(em, faction, hallPos);
                    ManageForge(em, faction);
                    continue;
                }
                if (stage == 2)
                {
                    UseHarkon(em, faction);
                    UseTotemRites(em, faction);
                    SacrificeForReligion(em, faction);
                    continue;
                }
                // Mines and Raider Camps (Gatherer's Huts) are NOT built here
                // (2026-10-03): this system searched a ring beside the ore and
                // around the Hall, and an extractor must stand ON its node, so
                // every placement was refused. SimpleAISystem's extractor walk
                // and hut pipeline place both on free nodes for every culture.
                TryPlantTotem(em, faction, hallPos);
                TryBuildAge2(em, faction, hallPos);
                TryAdoptSect(em, faction);
            }

            sw.Stop();
            TheWaningBorder.Core.Diagnostics.PerfSpikeLog.Report(
                "AIFeraldisEndgame", sw.Elapsed.TotalMilliseconds);
        }

        // ---------------------------------------------------------------

        private static bool TryGetHall(EntityManager em, Faction faction,
            out float3 hallPos, out byte culture)
        {
            hallPos = default; culture = Cultures.None;
            var q = QC_HallTagFactionTagFactionProgressLocalTransform.Get(em, QT_HallTagFactionTagFactionProgressLocalTransform);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (em.GetComponentData<FactionTag>(ents[i]).Value != faction) continue;
                culture = em.GetComponentData<FactionProgress>(ents[i]).Culture;
                hallPos = em.GetComponentData<LocalTransform>(ents[i]).Position;
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static int CountFactionWith<T>(EntityManager em, Faction faction)
            where T : unmanaged, IComponentData
        {
            var q = AIQueryCache.TagFaction<T>(em);
            using var ents = q.ToEntityArray(Allocator.Temp);
            int n = 0;
            for (int i = 0; i < ents.Length; i++)
                if (em.GetComponentData<FactionTag>(ents[i]).Value == faction) n++;
            return n;
        }

    }
}
