// AIStrategyEvaluator.cs
// Evaluates game state and transitions the AI's strategy dynamically.
// Runs periodically (frequency depends on difficulty).
using Unity.Entities;
using Unity.Mathematics;
using TheWaningBorder.Economy;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Evaluates game conditions and transitions AI strategy when triggers are met.
    /// Easy AI never adapts. Normal adapts on phase change. Hard every 60s. Expert every 30s.
    /// </summary>
    [DisableAutoCreation] // Replaced by SimpleAISystem (Age-1 build-order driven AI).
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateBefore(typeof(AIEconomyManager))]
    public partial struct AIStrategyEvaluator : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<AIBrain>();
        }

        public void OnUpdate(ref SystemState state)
        {
            if (GameSettings.IsMultiplayer && !GameSettings.IsHost()) return;

            float time = (float)SystemAPI.Time.ElapsedTime;
            var em = state.EntityManager;

            foreach (var (brain, strategy, knowledge, entity) in SystemAPI
                .Query<RefRO<AIBrain>, RefRW<AIStrategyState>, RefRO<AISharedKnowledge>>()
                .WithEntityAccess())
            {
                if (brain.ValueRO.IsActive == 0) continue;

                ref var strat = ref strategy.ValueRW;

                // Check evaluation interval
                if (time < strat.LastEvalTime + strat.EvalInterval) continue;
                strat.LastEvalTime = time;

                Faction faction = brain.ValueRO.Owner;
                float elapsed = time - strat.StrategyStartTime;
                float gameTime = time;

                // Gather state for evaluation
                int ownStrength = knowledge.ValueRO.OwnMilitaryStrength;
                int enemyStrength = knowledge.ValueRO.EnemyEstimatedStrength;
                int ownEcon = knowledge.ValueRO.OwnEconomicStrength;
                bool underAttack = knowledge.ValueRO.EnemyLastSeenTime > 0 &&
                    (time - knowledge.ValueRO.EnemyLastSeenTime) < 15.0;

                // Check for Hall health (emergency)
                bool hallCritical = false;
                foreach (var (fTag, health, bTag) in SystemAPI
                    .Query<RefRO<FactionTag>, RefRO<Health>, RefRO<BuildingTag>>()
                    .WithAll<HallTag>())
                {
                    if (fTag.ValueRO.Value == faction && health.ValueRO.Value < health.ValueRO.Max * 0.5f)
                    {
                        hallCritical = true;
                        break;
                    }
                }

                // Count crystal income (how many cadavers nearby)
                int crystalBank = 0;
                if (FactionEconomy.TryGetResources(em, faction, out var res))
                    crystalBank = res.Crystal;

                AIStrategy next = strat.Current;

                // ═══════════════════════════════════════════════════════
                // EMERGENCY OVERRIDE: Hall critical → Defensive
                // ═══════════════════════════════════════════════════════
                if (hallCritical && strat.Current != AIStrategy.Defensive)
                {
                    next = AIStrategy.Defensive;
                    AILogger.Log(faction, "STRATEGY", "EMERGENCY: Hall below 50% → Defensive");
                }
                // ═══════════════════════════════════════════════════════
                // STRATEGY-SPECIFIC TRANSITIONS
                // ═══════════════════════════════════════════════════════
                else
                {
                    switch (strat.Current)
                    {
                        case AIStrategy.Rush:
                            // Failed harass: lost armies without dealing damage
                            if (strat.ArmiesLostSinceSwitch >= 2 && strat.SuccessfulAttacks == 0 && elapsed > 120f)
                            {
                                next = AIStrategy.Defensive;
                                AILogger.Log(faction, "STRATEGY", "Rush failed (lost 2+ armies, no damage) → Defensive");
                            }
                            // Successful harass: keep pressure
                            else if (strat.SuccessfulAttacks >= 1 && gameTime > 300f)
                            {
                                next = AIStrategy.Aggressive;
                                AILogger.Log(faction, "STRATEGY", "Rush succeeded → Aggressive");
                            }
                            break;

                        case AIStrategy.EcoBoom:
                            // Attacked before ready
                            if (underAttack && gameTime < 300f && ownStrength < 5)
                            {
                                next = AIStrategy.Defensive;
                                AILogger.Log(faction, "STRATEGY", "Eco Boom interrupted by attack → Defensive");
                            }
                            // Crystal stockpile high enough to tech
                            else if (crystalBank >= 200 && !underAttack && gameTime > 180f)
                            {
                                next = AIStrategy.TechRush;
                                AILogger.Log(faction, "STRATEGY", $"Eco Boom → Tech Rush (crystal:{crystalBank})");
                            }
                            // Economy established, time to attack
                            else if (ownEcon > 800 && gameTime > 480f)
                            {
                                next = AIStrategy.Aggressive;
                                AILogger.Log(faction, "STRATEGY", $"Eco Boom → Aggressive (econ:{ownEcon})");
                            }
                            break;

                        case AIStrategy.TechRush:
                            // Attacked during transition
                            if (underAttack && ownStrength < 8 && !strat.HasAgedUp.IsTrue())
                            {
                                next = AIStrategy.Defensive;
                                AILogger.Log(faction, "STRATEGY", "Tech Rush under attack pre-age → Defensive");
                            }
                            // Age-up complete → go aggressive
                            else if (strat.HasAgedUp.IsTrue() && gameTime > 600f)
                            {
                                next = AIStrategy.Aggressive;
                                AILogger.Log(faction, "STRATEGY", "Tech Rush complete → Aggressive");
                            }
                            break;

                        case AIStrategy.Aggressive:
                            // Lost too many armies without result
                            if (strat.ArmiesLostSinceSwitch >= 3 && strat.SuccessfulAttacks <= 1)
                            {
                                next = AIStrategy.Defensive;
                                AILogger.Log(faction, "STRATEGY", "Aggressive failing (3+ armies lost) → Defensive");
                            }
                            // Ran out of resources
                            else if (ownEcon < 100 && ownStrength < 3)
                            {
                                next = AIStrategy.EcoBoom;
                                AILogger.Log(faction, "STRATEGY", "Aggressive depleted → Eco Boom");
                            }
                            break;

                        case AIStrategy.Defensive:
                            // Threat gone for 120s, safe to boom
                            if (!underAttack && elapsed > 120f && ownStrength >= 8)
                            {
                                next = AIStrategy.Aggressive;
                                AILogger.Log(faction, "STRATEGY", "Defensive: no threats 120s + strong → Aggressive");
                            }
                            else if (!underAttack && elapsed > 120f && ownStrength < 8)
                            {
                                next = AIStrategy.EcoBoom;
                                AILogger.Log(faction, "STRATEGY", "Defensive: no threats 120s + weak → Eco Boom");
                            }
                            break;
                    }
                }

                // Apply transition
                if (next != strat.Current)
                {
                    strat.Previous = strat.Current;
                    strat.Current = next;
                    strat.StrategyStartTime = time;
                    strat.ArmiesLostSinceSwitch = 0;
                    strat.SuccessfulAttacks = 0;
                }
            }
        }
    }

    // Extension for byte-as-bool
    internal static class ByteBoolExt
    {
        public static bool IsTrue(this byte b) => b != 0;
    }
}
