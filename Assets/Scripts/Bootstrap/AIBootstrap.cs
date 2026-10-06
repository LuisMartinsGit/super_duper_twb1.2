// AIBootstrap.cs
// Initializes AI players and creates AI brain entities

using Unity.Entities;
using TheWaningBorder.Core;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using TheWaningBorder.Core.Config;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Initializes AI systems for all non-human players.
    /// Creates AI brain entities with all required manager components.
    /// Call this AFTER EconomyBootstrap.EnsureFactionBanks().
    /// </summary>
    public static class AIBootstrap
    {
        static readonly ComponentType[] QT_AIBrain =
        {
            ComponentType.ReadOnly<AIBrain>(),
        };
        static CachedEntityQuery QC_AIBrain;

        #region Cached queries

        // CreateEntityQuery registers a NEW query with the world on every
        // call and this one was never disposed. See Core/CachedEntityQuery.cs.

        static readonly ComponentType[] QT_AIBrainFactionTag =
        {
            ComponentType.ReadOnly<AIBrain>(),
            ComponentType.ReadOnly<FactionTag>(),
        };
        static CachedEntityQuery QC_AIBrainFactionTag;

        #endregion
        // ═══════════════════════════════════════════════════════════════
        // PUBLIC API
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Creates AI brain entities for all AI-controlled factions.
        /// </summary>
        /// <param name="totalPlayers">Total number of players (including human)</param>
        /// <param name="humanPlayerFaction">Faction controlled by human (typically Blue/0)</param>
        public static void InitializeAIPlayers(int totalPlayers, Faction humanPlayerFaction = Faction.Blue)
        {
            // Initialize per-faction AI logging (clears old logs)
            AILogger.Initialize();
            AIBudget.Initialize();     // M-A budget wallets (fresh per match)
            AIRequestBus.Initialize();
            AIPivotalReserve.Initialize();   // savings goals (fresh per match)
            AIEndgameCommon.Initialize();    // temple back-off counters

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                return;
            }

            var em = world.EntityManager;
            int aiCount = 0;

            for (int i = 0; i < totalPlayers; i++)
            {
                Faction faction = (Faction)i;
                
                // Skip human-controlled factions
                if (GameSettings.IsFactionHumanControlled(faction))
                    continue;

                // Get difficulty from lobby config if available
                AIDifficulty difficulty = GetFactionDifficulty(faction);
                AIPersonality personality = ResolvePersonality(faction);

                CreateAIBrain(em, faction, personality, difficulty);
                aiCount++;
            }

        }

        /// <summary>
        /// Creates a single AI brain for a specific faction.
        /// Useful for adding AI players mid-game or for testing.
        /// </summary>
        public static Entity CreateAIForFaction(Faction faction, 
            AIPersonality personality = AIPersonality.Balanced,
            AIDifficulty difficulty = AIDifficulty.Normal)
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null) return Entity.Null;

            return CreateAIBrain(world.EntityManager, faction, personality, difficulty);
        }

        /// <summary>
        /// Changes AI difficulty for a specific faction at runtime.
        /// </summary>
        public static void SetAIDifficulty(Faction faction, AIDifficulty difficulty)
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            var em = world.EntityManager;
            var query = QC_AIBrainFactionTag.Get(em, QT_AIBrainFactionTag);
            
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var brains = query.ToComponentDataArray<AIBrain>(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction)
                {
                    var brain = brains[i];
                    brain.Difficulty = difficulty;
                    em.SetComponentData(entities[i], brain);
                    break;
                }
            }
        }

        /// <summary>
        /// Changes AI personality for a specific faction at runtime.
        /// </summary>
        public static void SetAIPersonality(Faction faction, AIPersonality personality)
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            var em = world.EntityManager;
            var query = QC_AIBrainFactionTag.Get(em, QT_AIBrainFactionTag);
            
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var brains = query.ToComponentDataArray<AIBrain>(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction)
                {
                    var brain = brains[i];
                    brain.Personality = personality;
                    em.SetComponentData(entities[i], brain);
                    break;
                }
            }
        }

        /// <summary>
        /// Enables or disables AI for a specific faction.
        /// </summary>
        public static void SetAIActive(Faction faction, bool active)
        {
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            var em = world.EntityManager;
            var query = QC_AIBrainFactionTag.Get(em, QT_AIBrainFactionTag);
            
            using var entities = query.ToEntityArray(Allocator.Temp);
            using var brains = query.ToComponentDataArray<AIBrain>(Allocator.Temp);
            using var factions = query.ToComponentDataArray<FactionTag>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value == faction)
                {
                    var brain = brains[i];
                    brain.IsActive = active ? (byte)1 : (byte)0;
                    em.SetComponentData(entities[i], brain);
                    break;
                }
            }
        }

        /// <summary>
        /// Removes all AI brains. Call when returning to main menu.
        /// </summary>
        public static void CleanupAllAI()
        {
            // Close AI log file handles
            AILogger.Cleanup();

            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            var em = world.EntityManager;
            var query = QC_AIBrain.Get(em, QT_AIBrain);
            em.DestroyEntity(query);

        }

        // ═══════════════════════════════════════════════════════════════
        // PRIVATE IMPLEMENTATION
        // ═══════════════════════════════════════════════════════════════

        private static Entity CreateAIBrain(EntityManager em, Faction faction, 
            AIPersonality personality, AIDifficulty difficulty)
        {
            var brainEntity = em.CreateEntity();

            // Core AI Brain
            em.AddComponentData(brainEntity, new AIBrain
            {
                Owner = faction,
                IsActive = 1,
                Personality = personality,
                Difficulty = difficulty,
            });

            em.AddComponentData(brainEntity, new FactionTag { Value = faction });

            // SimpleAISystem state — build-order step pointer + think timer.
            // When the lobby pre-promoted every faction (StartAge > 0),
            // skip the entire Age-1 build order and drop straight into the
            // maintenance loop: ReplaceLostUnits keeps the army at floor,
            // and TryLaunchAttack pushes idle units at the nearest enemy.
            // StartAgePromoter has already placed the Hall + Temple + choice
            // building, so the AI has no early-game milestones left to hit.
            int initialStepIndex = 0;
            byte ageUpIssued = 0;
            if (GameSettings.StartAge != SkirmishStartAge.Age0)
            {
                // Any step index past the longest build order (~30 steps)
                // triggers the maintenance branch in SimpleAISystem.OnUpdate.
                initialStepIndex = int.MaxValue;
                ageUpIssued = 1; // already aged up — don't try to trigger again
            }
            em.AddComponentData(brainEntity, new SimpleAIState
            {
                StepIndex = initialStepIndex,
                // PHASE seed: brains are created together, and SimpleAISystem
                // re-arms phase-preservingly (+= interval), so whatever
                // separation exists here persists for the whole match. A flat
                // 0 put every brain's think in the SAME frame forever (the
                // "AIThink brains 4" frame spikes). ~0.4s apart per faction.
                ThinkTimer = 0.1f + 0.37f * (int)faction,
                AgeUpIssued = ageUpIssued,
                VeilstoneWorkerTarget = 0,    // raised by SetVeilstoneTarget steps in the build order
                DesiredMilitary = 0,       // bumped by each successful military Train step
                DesiredWorkers = 0,         // bumped by each successful Worker Train step
                LastMilitaryUnit = default,// e.g. "Swordsman" — used to refill losses
            });

            // Shared Intelligence (read by AICultureChoice)
            em.AddComponentData(brainEntity, new AISharedKnowledge
            {
                EnemyLastSeenTime = 0,
                EnemyEstimatedStrength = 0,
                KnownEnemyBases = 0,
                OwnMilitaryStrength = 0,
                OwnEconomicStrength = 0
            });

            // The legacy manager states (AIEconomyState, AIBuildingState,
            // AIMilitaryState, AIScoutingState, AIVeilstoneHuntState), the
            // AIStrategyState re-evaluator and the ResourceRequest buffer
            // were removed on 2026-10-05: their managers were
            // [DisableAutoCreation] and nothing read them. The personality
            // is the brain's whole layer-2 state (Resources/AISettings.asset).
            AILogger.Log(faction, "STRATEGY", $"Personality: {personality} (difficulty: {difficulty})");

            // Dynamic Buffers
            em.AddBuffer<MineAssignment>(brainEntity);
            em.AddBuffer<BuildRequest>(brainEntity);
            em.AddBuffer<RecruitmentRequest>(brainEntity);
            em.AddBuffer<EnemySighting>(brainEntity);
            em.AddBuffer<ScoutAssignment>(brainEntity);
            em.AddBuffer<ExplorationZone>(brainEntity);

            return brainEntity;
        }

        internal static AIDifficulty GetFactionDifficulty(Faction faction)
        {
            // Try to get difficulty from LobbyConfig. In observer matches an
            // Observer-typed slot is AI-controlled too (IsFactionHumanControlled
            // returns false for everyone), so honor its configured difficulty
            // instead of silently falling back to Normal.
            int factionIndex = (int)faction;
            if (factionIndex >= 0 && factionIndex < LobbyConfig.Slots.Length)
            {
                var slot = LobbyConfig.Slots[factionIndex];
                if (slot.Type == SlotType.AI
                    || (GameSettings.IsObserver && slot.Type == SlotType.Observer))
                {
                    return slot.AIDifficulty switch
                    {
                        LobbyAIDifficulty.Easy => AIDifficulty.Easy,
                        LobbyAIDifficulty.Normal => AIDifficulty.Normal,
                        LobbyAIDifficulty.Hard => AIDifficulty.Hard,
                        LobbyAIDifficulty.Expert => AIDifficulty.Expert,
                        _ => AIDifficulty.Normal
                    };
                }
            }

            return AIDifficulty.Normal;
        }

        /// <summary>
        /// LAYER 2 — resolve this faction's personality, in priority order:
        /// the lobby's explicit pick, then the canonical colour, then a
        /// deterministic roll.
        ///
        /// This replaces GetDefaultPersonality + GetRandomStrategy, which had
        /// become circular once the two enums merged: the second took a
        /// personality and returned a "strategy" drawn from a pool biased by
        /// that same personality, so an Economic AI could be handed a Turtle
        /// opener while still answering "Economic" to every floor query. One
        /// identity, chosen once.
        /// </summary>
        internal static AIPersonality ResolvePersonality(Faction faction)
        {
            // 1. The lobby's per-slot pick wins. Observer-typed slots count as
            //    AI in observer matches (see GetFactionDifficulty).
            int factionIndex = (int)faction;
            if (factionIndex >= 0 && factionIndex < LobbyConfig.Slots.Length)
            {
                var slot = LobbyConfig.Slots[factionIndex];
                if (slot != null && (slot.Type == SlotType.AI
                    || (GameSettings.IsObserver && slot.Type == SlotType.Observer)))
                {
                    var picked = LobbyToPersonality(slot.AIStrategy);
                    if (picked.HasValue) return picked.Value;
                    // else fall through — the lobby asked for Random.
                }
            }

            // 2. COLOURS ARE PERSONALITIES (2026-08-30, batch-analysis
            //    directive): a batch is only comparable across matches when
            //    Red is always the rusher and Blue always the turtle.
            switch (faction)
            {
                case Faction.Red:    return AIPersonality.Rush;
                case Faction.Yellow: return AIPersonality.TechBoom;
                case Faction.Green:  return AIPersonality.Economic;
                case Faction.Blue:   return AIPersonality.Turtle;
                case Faction.Orange: return AIPersonality.Rush;
                case Faction.White:  return AIPersonality.Aggressive;
            }

            // 3. Deterministic roll, seeded so multiplayer peers agree.
            uint hash = (uint)((int)faction * 7919 + GameSettings.SpawnSeed + 31);
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            hash ^= hash >> 15;

            var all = new[]
            {
                AIPersonality.Balanced, AIPersonality.Aggressive, AIPersonality.Defensive,
                AIPersonality.Economic, AIPersonality.Rush,
                AIPersonality.TechBoom, AIPersonality.Turtle,
            };
            return all[(int)(hash % (uint)all.Length)];
        }

        /// <summary>
        /// Map the lobby-side choice onto the runtime personality. The lobby
        /// keeps its own player-facing enum; this is the only place the two
        /// meet. Returns null when the lobby picked Random.
        /// </summary>
        private static AIPersonality? LobbyToPersonality(LobbyAIStrategy choice) => choice switch
        {
            LobbyAIStrategy.EcoBoom   => AIPersonality.Economic,
            LobbyAIStrategy.Balanced  => AIPersonality.Balanced,
            LobbyAIStrategy.TechBoom  => AIPersonality.TechBoom,
            LobbyAIStrategy.Rush      => AIPersonality.Rush,
            LobbyAIStrategy.Turtle    => AIPersonality.Turtle,
            LobbyAIStrategy.Defensive => AIPersonality.Defensive,
            _                         => (AIPersonality?)null,     // Random
        };
    }
}