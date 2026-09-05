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
    }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(SimpleAISystem))]
    public partial struct AIFeraldisEndgameSystem : ISystem
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
            // Host authority only — the same guard the Alanthor sibling uses.
            if (!GameSettings.ShouldRunAIBrains()) return;

            var em = state.EntityManager;
            float now = (float)SystemAPI.Time.ElapsedTime;
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
                if (now < tick.NextThinkTime) continue;
                tick.NextThinkTime = now + think;
                em.SetComponentData(brainEntity, tick);

                // Culture + era gate — the fork point from the Alanthor sibling.
                if (!TryGetHall(em, faction, out float3 hallPos, out byte culture)) continue;
                if (culture != Cultures.Feraldis) continue;
                if (!FactionEconomy.TryGetBank(em, faction, out var bank)) continue;
                if (!em.HasComponent<FactionEra>(bank)) continue;
                if (em.GetComponentData<FactionEra>(bank).Value < 2) continue;

                if (em.HasComponent<AIStrategyState>(brainEntity))
                {
                    var ss = em.GetComponentData<AIStrategyState>(brainEntity);
                    if (ss.HasAgedUp == 0) { ss.HasAgedUp = 1; em.SetComponentData(brainEntity, ss); }
                }

                ConscriptSurplusWorkers(em, faction, hallPos);
                TryBuildMine(em, faction, hallPos);
                TryBuildEconomy(em, faction, hallPos);
                TryPlantTotem(em, faction, hallPos);
                TryBuildAge2(em, faction, hallPos);
                TryLevelTemple(em, faction);
                TryAdoptSect(em, faction);
                TryRunTheVerb(em, brainEntity, faction, hallPos, now);
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
