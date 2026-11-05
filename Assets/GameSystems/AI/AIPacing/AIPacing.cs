// AIPacing.cs
// How fast a tier and personality may spend (docs/Design/Game_AI.md § 2a).
// Every AI path that starts research, a level-up, an extractor or a sect power
// asks here first and reports here when it did, so a full bank buys no speed.
// The values are on the difficulty profiles and AISettings.asset; the running
// clocks are a component on the brain, so they end with the match and travel
// in a saved game.

using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Core;
using TheWaningBorder.Data.AI;

namespace TheWaningBorder.AI
{
    /// <summary>When this brain may next start each kind of work.</summary>
    public struct AIPacingState : IComponentData
    {
        public float NextResearch;
        public float NextLevelUp;
        public float NextExtractor;
        public float NextSectPower;
        public float NextWaste;
        public float ShardrootSeenAt;
        public int Wasted;
    }

    public static class AIPacing
    {
        private sealed class Pace
        {
            public AIDifficultyProfile Tier;
            public float ResearchGap, ExtractorGap, LevelGap, LevelTwoFrom, LevelThreeFrom;
            public int ResearchMax;
        }

        private static readonly Dictionary<int, Pace> _paces = new Dictionary<int, Pace>();

        private static readonly ComponentType[] QT_Brains =
        {
            ComponentType.ReadOnly<AIBrain>(),
            ComponentType.ReadOnly<AIPacingState>(),
        };
        private static CachedEntityQuery QC_Brains;

        private static readonly ComponentType[] QT_Queues =
        {
            ComponentType.ReadOnly<FactionTag>(),
            ComponentType.ReadOnly<ProductionQueueItem>(),
        };
        private static CachedEntityQuery QC_Queues;

        private static readonly string[] Extractors =
            { "GatherersHut", "Mine", "VeilstoneMine", "Alanthor_TradingOutpost", "Runai_Sanctuary" };

        public static bool IsExtractor(string buildingId)
            => System.Array.IndexOf(Extractors, buildingId) >= 0;

        private static Pace PaceOf(in AIBrain brain)
        {
            int key = (int)brain.Difficulty * 16 + (int)brain.Personality;
            if (_paces.TryGetValue(key, out var pace)) return pace;
            var tier = AISimpleDifficulty.GetProfile(brain.Difficulty);
            var p = AISettings.Get().For(brain.Personality, tier.PersonalityWeight);
            pace = new Pace
            {
                Tier = tier,
                ResearchGap = tier.ResearchGapSeconds * p.researchPaceScale,
                ExtractorGap = tier.ExtractorGapSeconds * p.extractorPaceScale,
                LevelGap = tier.LevelUpGapSeconds * p.levelUpPaceScale,
                LevelTwoFrom = tier.LevelUpsFromSeconds * p.levelUpPaceScale,
                LevelThreeFrom = tier.LevelThreeFromSeconds * p.levelUpPaceScale,
                ResearchMax = tier.ResearchMaxConcurrent > 0
                    ? tier.ResearchMaxConcurrent + p.researchExtraConcurrent : 0,
            };
            _paces[key] = pace;
            return pace;
        }

        /// <summary>The personality's attack pace on the tier's attack timing.</summary>
        public static void ApplyAttackPace(ref AIDifficultyProfile profile, AIPersonality personality)
        {
            float scale = AISettings.Get().For(personality, profile.PersonalityWeight).attackPaceScale;
            profile.FirstAttackEarliestSeconds *= scale;
            profile.AttackWaveIntervalSeconds *= scale;
        }

        /// <summary>The faction's brain. A faction with none has no pace to keep.</summary>
        private static bool TryBrain(EntityManager em, Faction faction, out Entity entity, out Pace pace)
        {
            var q = QC_Brains.Get(em, QT_Brains);
            using var ents = q.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                var brain = em.GetComponentData<AIBrain>(ents[i]);
                if (brain.Owner != faction) continue;
                entity = ents[i];
                pace = PaceOf(brain);
                return true;
            }
            entity = Entity.Null;
            pace = null;
            return false;
        }

        private static int InQueue(EntityManager em, Faction faction, ProductionKind kind)
        {
            int n = 0;
            var q = QC_Queues.Get(em, QT_Queues);
            using var ents = q.ToEntityArray(Allocator.Temp);
            using var facs = q.ToComponentDataArray<FactionTag>(Allocator.Temp);
            for (int i = 0; i < ents.Length; i++)
            {
                if (facs[i].Value != faction) continue;
                var buf = em.GetBuffer<ProductionQueueItem>(ents[i]);
                for (int j = 0; j < buf.Length; j++)
                    if (buf[j].Kind == kind) n++;
            }
            return n;
        }

        // ── Research ─────────────────────────────────────────────────────

        public static bool ResearchTurn(EntityManager em, Faction faction)
        {
            if (!TryBrain(em, faction, out var e, out var pace)) return true;
            if (SimClock.Now < em.GetComponentData<AIPacingState>(e).NextResearch) return false;
            return pace.ResearchMax <= 0 || InQueue(em, faction, ProductionKind.Research) < pace.ResearchMax;
        }

        public static bool ResearchLevelAllowed(EntityManager em, Faction faction, int minBuildingLevel)
        {
            if (!TryBrain(em, faction, out _, out var pace)) return true;
            return pace.Tier.ResearchMaxLevel <= 0 || minBuildingLevel <= pace.Tier.ResearchMaxLevel;
        }

        public static void NoteResearch(EntityManager em, Faction faction)
        {
            if (!TryBrain(em, faction, out var e, out var pace)) return;
            var s = em.GetComponentData<AIPacingState>(e);
            s.NextResearch = SimClock.Now + pace.ResearchGap;
            em.SetComponentData(e, s);
        }

        // ── Building levels ──────────────────────────────────────────────

        public static bool LevelUpTurn(EntityManager em, Faction faction)
        {
            if (!TryBrain(em, faction, out var e, out var pace)) return true;
            if (SimClock.Now < em.GetComponentData<AIPacingState>(e).NextLevelUp) return false;
            int max = pace.Tier.LevelUpMaxConcurrent;
            return max <= 0 || InQueue(em, faction, ProductionKind.BuildingUpgrade) < max;
        }

        /// <summary>It is this tier's turn to level, and the phase of that level is open.</summary>
        public static bool LevelAllowed(EntityManager em, Faction faction, byte level)
        {
            if (!LevelUpTurn(em, faction)) return false;
            if (!TryBrain(em, faction, out _, out var pace)) return true;
            return SimClock.Now >= (level >= 3 ? pace.LevelThreeFrom : pace.LevelTwoFrom);
        }

        public static void NoteLevelUp(EntityManager em, Faction faction)
        {
            if (!TryBrain(em, faction, out var e, out var pace)) return;
            var s = em.GetComponentData<AIPacingState>(e);
            s.NextLevelUp = SimClock.Now + pace.LevelGap;
            em.SetComponentData(e, s);
        }

        // ── Extractors ───────────────────────────────────────────────────

        public static bool ExtractorTurn(EntityManager em, Faction faction)
        {
            if (!TryBrain(em, faction, out var e, out _)) return true;
            return SimClock.Now >= em.GetComponentData<AIPacingState>(e).NextExtractor;
        }

        public static void NoteExtractor(EntityManager em, Faction faction)
        {
            if (!TryBrain(em, faction, out var e, out var pace)) return;
            var s = em.GetComponentData<AIPacingState>(e);
            s.NextExtractor = SimClock.Now + pace.ExtractorGap;
            em.SetComponentData(e, s);
        }

        // ── Sects ────────────────────────────────────────────────────────

        public static bool MayAdoptSect(EntityManager em, Faction faction, int adopted)
        {
            if (!TryBrain(em, faction, out _, out var pace)) return true;
            return pace.Tier.MaxSects <= 0 || adopted < pace.Tier.MaxSects;
        }

        public static bool SectPowerTurn(EntityManager em, Faction faction)
        {
            if (!TryBrain(em, faction, out var e, out _)) return true;
            return SimClock.Now >= em.GetComponentData<AIPacingState>(e).NextSectPower;
        }

        public static void NoteSectPower(EntityManager em, Faction faction)
        {
            if (!TryBrain(em, faction, out var e, out var pace)) return;
            var s = em.GetComponentData<AIPacingState>(e);
            s.NextSectPower = SimClock.Now + pace.Tier.SectPowerGapSeconds;
            em.SetComponentData(e, s);
        }

        // ── Shardroot ────────────────────────────────────────────────────

        /// <summary>Has the tier's reaction time passed since it first saw the
        /// Shardroot on the ground? The first call starts the clock.</summary>
        public static bool ShardrootReacted(EntityManager em, Faction faction)
        {
            if (!TryBrain(em, faction, out var e, out var pace)) return true;
            float wait = pace.Tier.ShardrootReactSeconds;
            if (wait <= 0f) return true;
            var s = em.GetComponentData<AIPacingState>(e);
            float now = SimClock.Now;
            if (s.ShardrootSeenAt <= 0f)
            {
                s.ShardrootSeenAt = math.max(now, 0.001f);
                em.SetComponentData(e, s);
            }
            return now - s.ShardrootSeenAt >= wait;
        }

        // ── Waste ────────────────────────────────────────────────────────

        /// <summary>Is a needless building due? Advances the clock whether or
        /// not the purchase then succeeds, as a careless player's would.</summary>
        public static bool WasteDue(EntityManager em, Faction faction, out int alreadyWasted)
        {
            alreadyWasted = 0;
            if (!TryBrain(em, faction, out var e, out var pace)) return false;
            float every = pace.Tier.WasteBuildSeconds;
            if (every <= 0f) return false;
            var s = em.GetComponentData<AIPacingState>(e);
            alreadyWasted = s.Wasted;
            if (s.Wasted >= pace.Tier.WasteBuildMax) return false;
            float now = SimClock.Now;
            if (now < s.NextWaste) return false;
            s.NextWaste = now + every;
            em.SetComponentData(e, s);
            return true;
        }

        public static void NoteWaste(EntityManager em, Faction faction)
        {
            if (!TryBrain(em, faction, out var e, out _)) return;
            var s = em.GetComponentData<AIPacingState>(e);
            s.Wasted++;
            em.SetComponentData(e, s);
        }
    }
}
