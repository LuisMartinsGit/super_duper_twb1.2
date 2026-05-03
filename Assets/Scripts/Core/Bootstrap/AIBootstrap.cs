// AIBootstrap.cs
// Initializes AI players and creates AI brain entities
// Location: Assets/Scripts/Core/Bootstrap/AIBootstrap.cs

using Unity.Entities;
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
        // ═══════════════════════════════════════════════════════════════
        // CONFIGURATION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>How often AI evaluates decisions (in seconds)</summary>
        public const float DefaultUpdateInterval = 0.5f;

        /// <summary>How often AI checks mine assignments (in seconds)</summary>
        public const float MineCheckInterval = 5.0f;

        /// <summary>How often AI checks build queue (in seconds)</summary>
        public const float BuildCheckInterval = 3.0f;

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
                AIPersonality personality = GetDefaultPersonality(faction);

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
            var query = em.CreateEntityQuery(typeof(AIBrain), typeof(FactionTag));
            
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
            var query = em.CreateEntityQuery(typeof(AIBrain), typeof(FactionTag));
            
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
            var query = em.CreateEntityQuery(typeof(AIBrain), typeof(FactionTag));
            
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
            var query = em.CreateEntityQuery(typeof(AIBrain));
            em.DestroyEntity(query);

        }

        // ═══════════════════════════════════════════════════════════════
        // PRIVATE IMPLEMENTATION
        // ═══════════════════════════════════════════════════════════════

        private static Entity CreateAIBrain(EntityManager em, Faction faction, 
            AIPersonality personality, AIDifficulty difficulty)
        {
            var brainEntity = em.CreateEntity();

            // Pick the build-order strategy up-front. The SimpleAISystem reads
            // AIBrain.Strategy each tick to look up the corresponding build order.
            AIStrategy initialStrategy = GetRandomStrategy(faction);

            // Core AI Brain
            em.AddComponentData(brainEntity, new AIBrain
            {
                Owner = faction,
                UpdateInterval = DefaultUpdateInterval,
                NextUpdateTime = 0,
                IsActive = 1,
                Personality = personality,
                Difficulty = difficulty,
                Strategy = initialStrategy,
            });

            em.AddComponentData(brainEntity, new FactionTag { Value = faction });

            // SimpleAISystem state — build-order step pointer + think timer.
            em.AddComponentData(brainEntity, new SimpleAIState
            {
                StepIndex = 0,
                ThinkTimer = 0f,           // fire on first update
                AgeUpIssued = 0,
                CrystalMinerTarget = 0,    // raised by SetCrystalTarget steps in the build order
                DesiredMilitary = 0,       // bumped by each successful military Train step
                DesiredMiners = 0,         // bumped by each successful Miner Train step
                LastMilitaryUnit = default,// e.g. "Swordsman" — used to refill losses
            });

            // Economy Manager State
            em.AddComponentData(brainEntity, new AIEconomyState
            {
                AssignedMiners = 0,
                DesiredMiners = 0,
                ActiveGatherersHuts = 0,
                DesiredGatherersHuts = 0,
                LastMineAssignmentCheck = 0,
                MineCheckInterval = MineCheckInterval,
                NeedsMoreSupplyIncome = 0,
                NeedsMoreIronIncome = 0
            });

            // Building Manager State
            em.AddComponentData(brainEntity, new AIBuildingState
            {
                ActiveBuilders = 0,
                DesiredBuilders = 2,
                QueuedConstructions = 0,
                LastBuildCheck = 0,
                BuildCheckInterval = BuildCheckInterval
            });

            // Military Manager State
            em.AddComponentData(brainEntity, new AIMilitaryState
            {
                TotalSoldiers = 0,
                TotalArchers = 0,
                TotalSiegeUnits = 0,
                ActiveBarracks = 0,
                DesiredBarracks = 0,
                ArmiesCount = 0,
                ScoutsCount = 0,
                QueuedSoldiers = 0,
                QueuedArchers = 0,
                QueuedSiegeUnits = 0,
                LastRecruitmentCheck = 0,
                RecruitmentCheckInterval = 5.0f
            });

            // Shared Intelligence
            em.AddComponentData(brainEntity, new AISharedKnowledge
            {
                EnemyLastSeenTime = 0,
                EnemyEstimatedStrength = 0,
                KnownEnemyBases = 0,
                OwnMilitaryStrength = 0,
                OwnEconomicStrength = 0
            });

            // Scouting Manager State
            em.AddComponentData(brainEntity, new AIScoutingState
            {
                ActiveScouts = 0,
                DesiredScouts = 2,
                LastScoutUpdate = 0,
                ScoutUpdateInterval = 2.0f,
                LastPriorityUpdate = 0,
                PriorityUpdateInterval = 10.0f,
                UnexploredZoneCount = 0,
                MapExplorationPercent = 0f
            });

            // Crystal Hunt State
            em.AddComponentData(brainEntity, new AICrystalHuntState
            {
                LastHuntCheck = 0,
                HuntCheckInterval = 8.0f
            });

            // Dynamic Strategy State — random initial strategy, eval rate by difficulty
            float evalInterval = difficulty switch
            {
                AIDifficulty.Easy => 9999f,   // Never adapts
                AIDifficulty.Normal => 120f,  // Every 2 minutes
                AIDifficulty.Hard => 60f,     // Every minute
                AIDifficulty.Expert => 30f,   // Every 30s
                _ => 120f
            };
            em.AddComponentData(brainEntity, new AIStrategyState
            {
                Current = initialStrategy,
                Previous = initialStrategy,
                LastEvalTime = 0,
                EvalInterval = evalInterval,
                StrategyStartTime = 0,
                ArmiesLostSinceSwitch = 0,
                SuccessfulAttacks = 0,
                HasAgedUp = 0
            });
            AILogger.Log(faction, "STRATEGY", $"Initial strategy: {initialStrategy} (difficulty: {difficulty})");

            // Dynamic Buffers
            em.AddBuffer<MineAssignment>(brainEntity);
            em.AddBuffer<BuildRequest>(brainEntity);
            em.AddBuffer<RecruitmentRequest>(brainEntity);
            em.AddBuffer<EnemySighting>(brainEntity);
            em.AddBuffer<ResourceRequest>(brainEntity);
            em.AddBuffer<ScoutAssignment>(brainEntity);
            em.AddBuffer<ExplorationZone>(brainEntity);

            return brainEntity;
        }

        private static AIPersonality GetDefaultPersonality(Faction faction)
        {
            // Assign different personalities to different AI factions for variety
            return faction switch
            {
                Faction.Red => AIPersonality.Aggressive,
                Faction.Green => AIPersonality.Defensive,
                Faction.Yellow => AIPersonality.Economic,
                Faction.Purple => AIPersonality.Balanced,
                Faction.Orange => AIPersonality.Rush,
                Faction.Teal => AIPersonality.Balanced,
                Faction.White => AIPersonality.Aggressive,
                _ => AIPersonality.Balanced
            };
        }

        private static AIDifficulty GetFactionDifficulty(Faction faction)
        {
            // Try to get difficulty from LobbyConfig
            int factionIndex = (int)faction;
            if (factionIndex >= 0 && factionIndex < LobbyConfig.Slots.Length)
            {
                var slot = LobbyConfig.Slots[factionIndex];
                if (slot.Type == SlotType.AI)
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

        private static AIStrategy GetRandomStrategy(Faction faction)
        {
            // First, honour the lobby's per-slot strategy choice if set.
            int factionIndex = (int)faction;
            if (factionIndex >= 0 && factionIndex < LobbyConfig.Slots.Length)
            {
                var slot = LobbyConfig.Slots[factionIndex];
                if (slot != null && slot.Type == SlotType.AI)
                {
                    var picked = LobbyToAIStrategy(slot.AIStrategy);
                    if (picked.HasValue) return picked.Value;
                    // else fall through to random roll (LobbyAIStrategy.Random)
                }
            }

            // Deterministic random based on faction + spawn seed so multiplayer stays synced.
            uint hash = (uint)((int)faction * 7919 + GameSettings.SpawnSeed + 31);
            hash ^= hash >> 13;
            hash *= 0x5bd1e995;
            hash ^= hash >> 15;
            // Six AIStrategy values (Rush, EcoBoom, TechRush, Aggressive,
            // Defensive, Turtle) — TechRush/Aggressive are legacy aliases for
            // TechBoom/Balanced. SimpleAISystem maps both to their Age-1 builds.
            int roll = (int)(hash % 6);
            return (AIStrategy)roll;
        }

        /// <summary>
        /// Map the lobby-side strategy choice onto the runtime AIStrategy enum.
        /// Returns null when the lobby picked Random (caller rolls a random).
        /// </summary>
        private static AIStrategy? LobbyToAIStrategy(LobbyAIStrategy choice) => choice switch
        {
            LobbyAIStrategy.EcoBoom   => AIStrategy.EcoBoom,
            LobbyAIStrategy.Balanced  => AIStrategy.Aggressive, // legacy alias for Balanced
            LobbyAIStrategy.TechBoom  => AIStrategy.TechRush,   // legacy alias for TechBoom
            LobbyAIStrategy.Rush      => AIStrategy.Rush,
            LobbyAIStrategy.Turtle    => AIStrategy.Turtle,
            LobbyAIStrategy.Defensive => AIStrategy.Defensive,
            _                         => (AIStrategy?)null,     // Random
        };
    }
}